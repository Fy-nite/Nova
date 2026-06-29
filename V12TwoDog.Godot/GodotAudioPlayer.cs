using Godot;
using System;
using System.Collections.Generic;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Rendering;

namespace V12TwoDog
{
    public partial class GodotAudioPlayer : Node3D, IAudioPlayer
    {
        private readonly Dictionary<long, AudioStreamPlayer3D> _players = new();
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
            // Audio snapshot is applied from main thread's _Process via ApplySnapshot(_latestFrame)
        }

        private void CaptureAudioSnapshot(FrameSnapshot snapshot, World world)
        {
            void Collect(IWorldElement element)
            {
                if (element?.Components == null) return;
                foreach (var c in element.Components.ToArray())
                {
                    if (c is IAudioSource src)
                    {
                        var a = new AudioSourceSnapshot
                        {
                            OwnerElementId = src.Id,
                            Position = src.Position,
                            Volume = src.Volume,
                            Pitch = src.Pitch,
                            MaxDistance = src.MaxDistance,
                            IsPlaying = src.IsPlaying,
                            ClipPath = src.AudioClipPath ?? ""
                        };
                        snapshot.AudioSources.Add(a);
                    }
                }
                if (element.Children != null)
                    foreach (var child in element.Children)
                        Collect(child);
            }

            foreach (var root in world.Root.ToArray())
                Collect(root);

            var listenerElement = world.FindElementWithComponentRecursive<IAudioListener>();
            if (listenerElement != null)
            {
                var listenerComp = listenerElement.GetComponent<IAudioListener>();
                if (listenerComp != null)
                {
                    snapshot.Listener = new AudioListenerSnapshot
                    {
                        Position = listenerComp.Position,
                        Forward = listenerComp.Forward,
                        Up = listenerComp.Up,
                        HasValue = true
                    };
                }
            }
        }

        public void ApplySnapshot(FrameSnapshot snapshot)
        {
            if (snapshot == null) return;

            // ── Reparent listener to the active camera so it follows the head ──
            var cam = GetViewport()?.GetCamera3D();
            if (cam != null && _listener.GetParent() != cam)
                _listener.Reparent(cam);

            // ── Build set of current audio source IDs ──
            var currentIds = new HashSet<long>();
            foreach (var a in snapshot.AudioSources)
                currentIds.Add(a.OwnerElementId);

            // ── Remove stale sources ──
            var toRemove = new List<long>();
            foreach (var kvp in _players)
            {
                if (!currentIds.Contains(kvp.Key))
                {
                    if (GodotObject.IsInstanceValid(kvp.Value))
                        kvp.Value.QueueFree();
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
                _players.Remove(id);

            // ── Create / update sources ──
            foreach (var a in snapshot.AudioSources)
            {
                if (!_players.TryGetValue(a.OwnerElementId, out var player) || !GodotObject.IsInstanceValid(player))
                {
                    player = new AudioStreamPlayer3D();
                    AddChild(player);
                    _players[a.OwnerElementId] = player;
                }

                if (!string.IsNullOrEmpty(a.ClipPath))
                {
                    var existingPath = player.Stream?.ResourcePath ?? "";
                    if (existingPath != a.ClipPath)
                        player.Stream = GD.Load<AudioStream>(V12.Core.V12AssetResolver.ResolveGlobal(a.ClipPath));
                }

                player.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance;
                player.MaxDistance = a.MaxDistance;
                player.PitchScale = a.Pitch;

                float finalVolume = Math.Clamp(1f * a.Volume, 0.0001f, 1f);
                player.VolumeDb = Mathf.LinearToDb(finalVolume);

                player.GlobalPosition = new Vector3(a.Position.X, a.Position.Y, a.Position.Z);

                if (a.IsPlaying && !player.Playing)
                    player.Play();
                else if (!a.IsPlaying && player.Playing)
                    player.Stop();
            }
        }

        public void Update(GameRoot gameRoot) => Update(0);

        public void Play(IAudioSource source)
        {
            foreach (var kvp in _players)
            {
                if (kvp.Value.Playing == false)
                {
                    kvp.Value.Play();
                    break;
                }
            }
        }

        public void Stop(IAudioSource source)
        {
            foreach (var kvp in _players)
            {
                if (kvp.Value.Playing)
                {
                    kvp.Value.Stop();
                    break;
                }
            }
        }

        public void SetGain(IAudioSource source, float gain)
        {
            foreach (var kvp in _players)
            {
                float finalVolume = Math.Clamp(gain * source.Volume, 0.0001f, 1f);
                kvp.Value.VolumeDb = Mathf.LinearToDb(finalVolume);
                break;
            }
        }

        public void RemoveSource(IAudioSource source)
        {
            long id = source.Id;
            if (_players.TryGetValue(id, out var player))
            {
                if (GodotObject.IsInstanceValid(player))
                    player.QueueFree();
                _players.Remove(id);
            }
        }
    }
}
