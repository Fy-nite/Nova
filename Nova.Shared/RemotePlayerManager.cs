using Godot;

using System;
using System.Collections.Generic;
using System.Linq;
using NumQuaternion = System.Numerics.Quaternion;
using V12.Basic.Components;
using V12.Components;
using V12.Components.Renderables;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.NetworkCable;
using V12.Core.Networking;

/// <summary>
/// Manages remote player lifecycle (creation, updates, removal) and position interpolation.
/// </summary>
public class RemotePlayerManager
{
    private readonly GameRoot _root;

    public record RemoteTweenState(
        System.Numerics.Vector3 VisualPosition,
        NumQuaternion VisualRotation,
        System.Numerics.Vector3 TargetPosition,
        NumQuaternion TargetRotation);

    private readonly Dictionary<long, RemoteTweenState> _remoteTweens = new();
    private const float TweenSpeed = 12f;

    /// <summary>
    /// Name of the local world, used to switch back on disconnect.
    /// </summary>
    public string LocalWorldName { get; set; }

    public RemotePlayerManager(GameRoot root)
    {
        _root = root;
    }

    /// <summary>
    /// Interpolate remote player positions toward their targets each physics frame.
    /// </summary>
    public void UpdateTweens(float delta)
    {
        if (_remoteTweens.Count == 0) return;

        float t = 1f - MathF.Exp(-TweenSpeed * delta);
        foreach (var kvp in _remoteTweens)
        {
            long playerId = kvp.Key;
            RemoteTweenState state = kvp.Value;

            var newVisPos = System.Numerics.Vector3.Lerp(
                new System.Numerics.Vector3(state.VisualPosition.X, state.VisualPosition.Y, state.VisualPosition.Z),
                new System.Numerics.Vector3(state.TargetPosition.X, state.TargetPosition.Y, state.TargetPosition.Z),
                t);

            var newVisRot = NumQuaternion.Slerp(state.VisualRotation, state.TargetRotation, t);

            var remoteName = $"RemotePlayer_{playerId}";
            var remote = _root.FindElement(e => e.Name == remoteName);
            if (remote != null)
            {
                remote.LocalTransform = new TRS
                {
                    Position = new System.Numerics.Vector3(newVisPos.X, newVisPos.Y, newVisPos.Z),
                    Rotation = newVisRot,
                    Scale = System.Numerics.Vector3.One
                };
            }

            _remoteTweens[playerId] = new RemoteTweenState(
                new System.Numerics.Vector3(newVisPos.X, newVisPos.Y, newVisPos.Z),
                newVisRot,
                state.TargetPosition,
                state.TargetRotation);
        }
    }

    public void ClearTweens() => _remoteTweens.Clear();

    /// <summary>
    /// Handle an incoming PlayerSync message: create or update a remote player element.
    /// </summary>
    public void HandlePlayerSync(MessageDTO message, bool debugMode)
    {
        try
        {
            if (debugMode) GD.Print($"[Network] \U0001f4e8 Received PlayerSync message, deserializing...");
            var playerSync = AncientCompressor.Decompress<PlayerSyncDTO>(message.Message);
            if (playerSync == null)
            {
                if (debugMode) GD.PrintErr($"[Network] \u274c PlayerSync deserialization returned null");
                return;
            }

            if (debugMode)
            {
                GD.Print($"[Network] \U0001f4e8 PlayerSync details:");
                GD.Print($"  PlayerName: {playerSync.PlayerName}");
                GD.Print($"  PlayerId: {playerSync.PlayerId}");
                GD.Print($"  Position: ({playerSync.Position.X:F2}, {playerSync.Position.Y:F2}, {playerSync.Position.Z:F2})");
                GD.Print($"  Components: {playerSync.Components.Count}");
            }

            // Check if this is our own player (ignore it)
            var localPlayer = _root.Player;
            if (localPlayer != null && localPlayer.Id == playerSync.PlayerId)
            {
                if (debugMode) GD.Print($"[Network] \u23ed Ignoring own player sync (localPlayer.Id={localPlayer.Id} == sync.PlayerId={playerSync.PlayerId})");
                return;
            }

            if (debugMode) GD.Print($"[Network] \u2713 Not our player (localPlayer.Id={localPlayer?.Id ?? -1}, sync.PlayerId={playerSync.PlayerId})");

            // Create or update remote player
            var remotePlayerName = $"RemotePlayer_{playerSync.PlayerId}";
            var remotePlayer = _root.FindElement(e => e.Name == remotePlayerName);

            if (debugMode) GD.Print($"[Network] \U0001f50d Looking for existing remote player '{remotePlayerName}': {(remotePlayer != null ? "FOUND" : "NOT FOUND")}");

            if (remotePlayer == null)
            {
                CreateRemotePlayer(playerSync, remotePlayerName, debugMode);
            }
            else
            {
                UpdateRemotePlayerTarget(playerSync, remotePlayerName, debugMode);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Network] \u274c Error handling PlayerSync: {ex.Message}");
            if (debugMode) GD.PrintErr($"  Stack: {ex.StackTrace}");
        }
    }

    private void CreateRemotePlayer(PlayerSyncDTO playerSync, string remotePlayerName, bool debugMode)
    {
        if (debugMode) GD.Print($"[Network] \U0001f195 Creating new remote player '{remotePlayerName}'...");

        var remotePlayer = new Element
        {
            Name = remotePlayerName,
            LocalTransform = new TRS
            {
                Position = playerSync.Position,
                Rotation = playerSync.Rotation,
                Scale = System.Numerics.Vector3.One
            }
        };

        if (debugMode) GD.Print($"[Network]   Created Element with Id={remotePlayer.Id}");
        if (debugMode) GD.Print($"[Network]   Deserializing {playerSync.Components.Count} components...");

        // Deserialize components
        foreach (var csDto in playerSync.Components)
        {
            try
            {
                if (debugMode) GD.Print($"[Network]     Deserializing {csDto.TypeName} ({csDto.Data?.Length ?? 0} bytes)...");
                var comp = AncientCompressor.DecompressComponent(csDto);
                if (comp != null)
                {
                    // Skip PlayerComponent for remote players (we don't control them)
                    if (comp is PlayerComponent)
                    {
                        if (debugMode) GD.Print($"[Network]       \u23ed Skipping PlayerComponent");
                        continue;
                    }

                    remotePlayer.Components.Add(comp);
                    comp.OnAttach(remotePlayer);
                    if (debugMode) GD.Print($"[Network]       \u2705 Added {comp.GetType().Name}");
                }
                else
                {
                    if (debugMode) GD.PrintErr($"[Network]       \u274c DecompressComponent returned null for {csDto.TypeName}");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Network]       \u274c Failed to deserialize {csDto.TypeName}: {ex.Message}");
            }
        }

        if (debugMode) GD.Print($"[Network]   Total components on remote player: {remotePlayer.Components.Count}");

        // If we have a MeshComponent but no MeshRenderer, create a MeshRenderer wrapper
        var meshComp = remotePlayer.Components.OfType<MeshComponent>().FirstOrDefault();
        if (debugMode) GD.Print($"[Network]   MeshComponent found: {(meshComp != null ? "YES" : "NO")}");
        if (meshComp != null && debugMode)
        {
            GD.Print($"[Network]     Shape: {meshComp.Shape}, Size: ({meshComp.Width}, {meshComp.Height}, {meshComp.Depth})");
        }

        if (meshComp != null && !remotePlayer.Components.Any(c => c is MeshRenderer))
        {
            if (debugMode) GD.Print($"[Network]   \U0001f3a8 Creating MeshRenderer wrapper...");
            var mr = new MeshRenderer { Mesh = meshComp };
            remotePlayer.Components.Add(mr);
            mr.OnAttach(remotePlayer);
            if (debugMode) GD.Print($"[Network]   \u2705 MeshRenderer created and attached");
        }

        if (debugMode) GD.Print($"[Network]   Adding remote player to world...");
        _root.SelectedWorld?.AddElement(remotePlayer);
        if (debugMode) GD.Print($"[Network]   \u2705 Added to world. World now has {_root.SelectedWorld?.Root.Count ?? 0} root elements");

        _root.Registry.Get<DirtyTracker>("DirtyTracker")?.TrackElement(remotePlayer);
        // Initialise tween state so interpolation starts from the correct position
        _remoteTweens[playerSync.PlayerId] = new RemoteTweenState(playerSync.Position, playerSync.Rotation, playerSync.Position, playerSync.Rotation);
        if (debugMode) GD.Print($"[Network] \u2705 Remote player '{remotePlayerName}' created successfully");

        // Debug: list all root elements
        if (debugMode)
        {
            GD.Print($"[Network] \U0001f4cb Current world root elements:");
            foreach (var elem in _root.SelectedWorld?.Root)
            {
                GD.Print($"    - {elem.Name} (Id={elem.Id}, Components={elem.Components.Count}, Children={elem.Children.Count})");
            }
        }
    }

    private void UpdateRemotePlayerTarget(PlayerSyncDTO playerSync, string remotePlayerName, bool debugMode)
    {
        if (debugMode) GD.Print($"[Network] \U0001f504 Updating existing remote player '{remotePlayerName}'...");

        var targetPos = playerSync.Position;
        var targetRot = playerSync.Rotation;

        if (!_remoteTweens.TryGetValue(playerSync.PlayerId, out var curTween))
        {
            // First update — initialise visual at the same spot as target
            _remoteTweens[playerSync.PlayerId] = new RemoteTweenState(targetPos, targetRot, targetPos, targetRot);
        }
        else
        {
            // Update target; visual continues from its current interpolated position
            _remoteTweens[playerSync.PlayerId] = curTween with { TargetPosition = targetPos, TargetRotation = targetRot };
        }

        if (debugMode) GD.Print($"[Network] \u2705 Set tween target ({targetPos.X:F2}, {targetPos.Y:F2}, {targetPos.Z:F2})");
    }

    /// <summary>
    /// Handle an incoming PlayerLeave message: remove the remote player element.
    /// </summary>
    public void HandlePlayerLeave(MessageDTO message, bool debugMode)
    {
        try
        {
            var leaveDto = AncientCompressor.Decompress<PlayerLeaveDTO>(message.Message);
            if (leaveDto == null) return;

            GD.Print($"[Network] \U0001f4f2 PlayerLeave received for PlayerId: {leaveDto.PlayerId}");

            var remotePlayerName = $"RemotePlayer_{leaveDto.PlayerId}";
            var remotePlayer = _root.FindElement(e => e.Name == remotePlayerName);
            if (remotePlayer != null)
            {
                var world = _root.GetWorldForElement(remotePlayer);
                world?.RemoveElement(remotePlayer);
                GD.Print($"[Network] \u2705 Removed remote player '{remotePlayerName}' from world");
            }
            else
            {
                if (debugMode) GD.Print($"[Network] \u26a0 Remote player '{remotePlayerName}' not found (already removed?)");
            }

            // Clean up tween state for this player
            _remoteTweens.Remove(leaveDto.PlayerId);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Network] \u274c Error handling PlayerLeave: {ex.Message}");
        }
    }
}
