using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Components;

namespace V12TwoDog
{
    public partial class GodotAudioPlayer : Node3D, IAudioPlayer
    {
        private readonly Dictionary<IAudioSource, AudioStreamPlayer3D> _players = new();
        private AudioListener3D _listener;
        private GameRoot _gameRoot;

        public void Initialize(GameRoot g)
        {
            _gameRoot = g;
            _listener = new AudioListener3D();
            AddChild(_listener);
        }

        public void Update(float deltaTime)
        {
            if (_gameRoot?.SelectedWorld == null) return;

            var steamAudio = _gameRoot.Registry.Get<V12.Core.Audio.SteamAudioService>();

            var v12Listener = _gameRoot.SelectedWorld.FindElementWithComponentRecursive<V12.Core.Core.Interfaces.IAudioListener>();
            if (v12Listener != null)
            {
                var listenerComp = v12Listener.GetComponent<V12.Core.Core.Interfaces.IAudioListener>();
                if (listenerComp != null)
                    _listener.GlobalPosition = new Vector3(
                        listenerComp.Position.X,
                        listenerComp.Position.Y,
                        listenerComp.Position.Z);
            }

            var worldSources = new HashSet<IAudioSource>();
            void Collect(IWorldElement element)
            {
                if (element?.Components == null) return;
                foreach (var c in element.Components.ToArray())
                    if (c is IAudioSource src)
                    {
                        worldSources.Add(src);
                        GD.Print($"[GodotAudioPlayer] Found source on element '{element.Name}': {src.AudioClipPath}");
                    }
                if (element.Children != null)
                    foreach (var child in element.Children.ToArray())
                        Collect(child);
            }
            foreach (var root in _gameRoot.SelectedWorld.Root.ToArray())
                Collect(root);
            GD.Print($"[GodotAudioPlayer] Found {worldSources.Count} sources in world");

            var toRemove = new List<IAudioSource>();
            foreach (var kvp in _players)
            {
                if (!worldSources.Contains(kvp.Key))
                {
                    if (GodotObject.IsInstanceValid(kvp.Value))
                        kvp.Value.QueueFree();
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var src in toRemove)
                _players.Remove(src);

            foreach (var source in worldSources)
            {
                if (!_players.TryGetValue(source, out var player) || !GodotObject.IsInstanceValid(player))
                {
                    player = new AudioStreamPlayer3D();
                    AddChild(player);
                    _players[source] = player;
                    GD.Print($"[GodotAudioPlayer] Created player for {source.AudioClipPath} playing={source.IsPlaying}");
                }

                if (!string.IsNullOrEmpty(source.AudioClipPath))
                {
                    var existingPath = player.Stream?.ResourcePath ?? "";
                    if (existingPath != source.AudioClipPath)
                        player.Stream = GD.Load<AudioStream>(source.AudioClipPath);
                }

                player.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
                player.MaxDistance = float.MaxValue;
                player.PitchScale = source.Pitch;

                float gain = 1f;
                if (steamAudio != null && steamAudio.IsInitialized)
                    steamAudio.TryGetSourceGain(source, out gain);
                SetGain(source, gain);

                var pos = source.Position;
                player.GlobalPosition = new Vector3(pos.X, pos.Y, pos.Z);

                if (source.IsPlaying && !player.Playing)
                    player.Play();
                else if (!source.IsPlaying && player.Playing)
                    player.Stop();
            }
        }

        public void Update(GameRoot gameRoot) => Update(0);

        public void Play(IAudioSource source)
        {
            if (_players.TryGetValue(source, out var player) && GodotObject.IsInstanceValid(player))
                player.Play();
        }

        public void Stop(IAudioSource source)
        {
            if (_players.TryGetValue(source, out var player) && GodotObject.IsInstanceValid(player))
                player.Stop();
        }

        public void SetGain(IAudioSource source, float gain)
        {
            if (_players.TryGetValue(source, out var player) && GodotObject.IsInstanceValid(player))
            {
                float finalVolume = Math.Clamp(gain * source.Volume, 0.0001f, 1f);
                player.VolumeDb = Mathf.LinearToDb(finalVolume);
            }
        }

        public void RemoveSource(IAudioSource source)
        {
            if (_players.TryGetValue(source, out var player))
            {
                if (GodotObject.IsInstanceValid(player))
                    player.QueueFree();
                _players.Remove(source);
            }
        }
    }
}
