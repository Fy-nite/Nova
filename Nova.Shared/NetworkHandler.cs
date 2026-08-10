using Godot;

using System;
using System.Collections.Concurrent;
using V12.Basic.Components;
using V12.Components;
using V12.Components.Renderables;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.NetworkCable;
using V12.Core.Networking;
using V12TwoDog;

/// <summary>
/// Orchestrates network message dispatch, player sync, and heartbeat timers.
/// Delegates world-level message handling to <see cref="WorldSyncHandler"/>
/// and remote player lifecycle to <see cref="RemotePlayerManager"/>.
/// </summary>
public class NetworkHandler
{
    private readonly GameRoot _root;
    private readonly WorldSyncHandler _worldSyncHandler;
    private readonly RemotePlayerManager _remotePlayerManager;

    // ── Threading / queuing ──
    private readonly ConcurrentQueue<MessageDTO> _pendingNetworkMessages = new();

    // ── Player sync ──
    private float _playerSyncTimer = 0f;
    private const float PlayerSyncInterval = 0.1f;

    // ── Heartbeat ──
    private float _heartbeatTimer = 0f;
    private const float HeartbeatIntervalSeconds = 5f;

    private bool _debugMode;

    public NetworkHandler(GameRoot root, WorldSyncHandler worldSyncHandler, RemotePlayerManager remotePlayerManager)
    {
        _root = root;
        _worldSyncHandler = worldSyncHandler;
        _remotePlayerManager = remotePlayerManager;
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Called from the networking thread — enqueue for processing on the V12 worker thread.
    /// </summary>
    public void HandleNetworkMessage(MessageDTO message)
    {
        if (message == null) return;
        if (_debugMode) GD.Print($"[Network] \U0001f4e5 Received {message.MessageType} from {message.Sender?.AbsoluteUri} (queueing for processing)");
        _pendingNetworkMessages.Enqueue(message);
    }

    /// <summary>
    /// Called from the V12 worker thread — safe to modify world state here.
    /// </summary>
    public void ProcessPendingNetworkMessages()
    {
        int processed = 0;
        while (_pendingNetworkMessages.TryDequeue(out var message))
        {
            processed++;
            try
            {
                if (_debugMode) GD.Print($"[Network] \U0001f504 Processing {message.MessageType}...");
                ProcessNetworkMessage(message);
                if (_debugMode) GD.Print($"[Network] \u2705 Finished processing {message.MessageType}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Network] \u274c Error processing {message.MessageType}: {ex.Message}");
                if (_debugMode) GD.PrintErr($"  Stack: {ex.StackTrace}");
            }
        }
        if (processed > 0 && _debugMode)
        {
            GD.Print($"[Network] \U0001f4ca Processed {processed} message(s) this frame");
        }
    }

    /// <summary>
    /// Called each physics frame from the main thread to tick sync/heartbeat timers.
    /// </summary>
    public void Update(float delta, bool debugMode)
    {
        _debugMode = debugMode;

        // ── Periodic status logging ──
        if (_debugMode && (int)(_playerSyncTimer * 10) % 50 == 0 && _playerSyncTimer > 0.1f)
        {
            var nc = _root.Registry.Get<NetworkClient>("NetworkClient");
            GD.Print($"[Status] \U0001f4ca Client connected: {nc?.IsConnected ?? false}");
            GD.Print($"[Status] \U0001f4e6 Pending network messages: {_pendingNetworkMessages.Count}");
            if (_root.SelectedWorld != null)
            {
                GD.Print($"[Status] \U0001f30d World '{_root.SelectedWorld.WorldName}': {_root.SelectedWorld.Root.Count} root elements");
                foreach (var elem in _root.SelectedWorld.Root)
                {
                    GD.Print($"    - {elem.Name} (Id={elem.Id}, Components={elem.Components.Count})");
                }
            }
        }

        // ── Periodic player sync ──
        _playerSyncTimer += delta;
        if (_playerSyncTimer >= PlayerSyncInterval)
        {
            _playerSyncTimer = 0f;
            if (_debugMode) GD.Print($"[Network] \u23f0 PlayerSync timer fired, sending data...");
            SendPlayerDataToServer();
        }

        // ── Heartbeat ──
        _heartbeatTimer += delta;
        if (_heartbeatTimer >= HeartbeatIntervalSeconds)
        {
            _heartbeatTimer = 0f;
            var client = _root.Registry.Get<NetworkClient>("NetworkClient");
            if (client != null && client.IsConnected)
            {
                _root.Cables.SendData(new MessageDTO
                {
                    Sender = new Uri("networkcables://client"),
                    MessageType = MessageType.Heartbeat,
                    Message = Array.Empty<byte>()
                });
                if (_debugMode) GD.Print($"[Network] \U0001f493 Heartbeat sent");
            }
        }
    }

    // ── Message dispatch ────────────────────────────────────────────

    private void ProcessNetworkMessage(MessageDTO message)
    {
        if (message == null) return;

        try
        {
            switch (message.MessageType)
            {
                case MessageType.WorldSync:
                    _worldSyncHandler.HandleWorldSync(message, _debugMode);
                    break;

                case MessageType.WorldArchive:
                    _worldSyncHandler.HandleWorldArchive(message, _debugMode);
                    break;

                case MessageType.Heartbeat:
                    if (_debugMode) GD.Print($"[Network] \U0001f493 Heartbeat received from {message.Sender}");
                    break;

                case MessageType.PlayerSync:
                    _remotePlayerManager.HandlePlayerSync(message, _debugMode);
                    break;

                case MessageType.PlayerLeave:
                    _remotePlayerManager.HandlePlayerLeave(message, _debugMode);
                    break;

                case MessageType.WorldUpdate:
                    _worldSyncHandler.HandleWorldUpdate(message);
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[Network] Error handling {message.MessageType}: {ex.Message}");
        }
    }

    // ── Player sync ─────────────────────────────────────────────────

    /// <summary>
    /// Walk the Player element's XR_Root → head/hand children and copy their
    /// local poses into the sync DTO, plus the live controller analog state.
    /// Desktop players (no XR_Root) leave <see cref="PlayerSyncDTO.HasRig"/> false.
    /// </summary>
    private void CapturePlayerRig(PlayerSyncDTO syncDto, IWorldElement player)
    {
        try
        {
            IWorldElement? xrRoot = null;
            if (player.Children != null)
            {
                foreach (var child in player.Children)
                {
                    if (child.GetComponent<XRRootComponent>() != null)
                    {
                        xrRoot = child;
                        break;
                    }
                }
            }

            if (xrRoot?.Children == null) return;

            foreach (var child in xrRoot.Children)
            {
                if (child.GetComponent<XRHeadComponent>() != null)
                {
                    syncDto.HasRig = true;
                    syncDto.HeadPosition = child.LocalTransform.Position;
                    syncDto.HeadRotation = child.LocalTransform.Rotation;
                }
                else if (child.GetComponent<XRHandComponent>() is { } hand)
                {
                    syncDto.HasRig = true;
                    if (hand.Side == HandSide.Left)
                    {
                        syncDto.LeftHandPosition = child.LocalTransform.Position;
                        syncDto.LeftHandRotation = child.LocalTransform.Rotation;
                    }
                    else
                    {
                        syncDto.RightHandPosition = child.LocalTransform.Position;
                        syncDto.RightHandRotation = child.LocalTransform.Rotation;
                    }
                }
            }

            // ── Controller analog state (local input broadcast to peers) ──
            var xr = _root.Registry.Get<XRTrackingService>("XRTrackingService");
            if (xr != null)
            {
                syncDto.LeftTrigger = xr.LeftTrigger;
                syncDto.LeftGrip = xr.LeftGrip;
                syncDto.RightTrigger = xr.RightTrigger;
                syncDto.RightGrip = xr.RightGrip;
            }

            if (_debugMode && syncDto.HasRig)
            {
                GD.Print($"[Network] \U0001f9ed Rig captured — head=({syncDto.HeadPosition.X:F2},{syncDto.HeadPosition.Y:F2},{syncDto.HeadPosition.Z:F2}), " +
                         $"L=({syncDto.LeftHandPosition.X:F2},...), R=({syncDto.RightHandPosition.X:F2},...)");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Network] \u274c Failed to capture player rig: {ex.Message}");
        }
    }

    public void SendPlayerDataToServer()
    {
        var player = _root.Player;
        if (player == null)
        {
            if (_debugMode) GD.PrintErr("[Network] \u274c No local Player found in PersistentWorld to send to server");
            return;
        }

        try
        {
            if (_debugMode)
            {
                GD.Print($"[Network] \U0001f4e4 Preparing PlayerSync for '{player.Name}' (Id={player.Id})...");
                GD.Print($"  Position: ({player.LocalTransform.Position.X:F2}, {player.LocalTransform.Position.Y:F2}, {player.LocalTransform.Position.Z:F2})");
                GD.Print($"  Components: {player.Components.Count}");
            }

            var syncDto = new PlayerSyncDTO
            {
                PlayerId = player.Id,
                PlayerName = player.Name ?? "Player",
                Position = player.LocalTransform.Position,
                Rotation = player.LocalTransform.Rotation,
                ElementId = player.Id,
                Timestamp = DateTime.UtcNow
            };

            // Serialize player components, skipping non-serializable ones
            foreach (var comp in player.Components)
            {
                if (comp is PhysicsBodyComponent || comp is PlayerComponent)
                {
                    if (_debugMode) GD.Print($"  \u23ed Skipping {comp.GetType().Name} (non-serializable)");
                    continue;
                }

                if (comp is MeshRenderer)
                {
                    if (_debugMode) GD.Print($"  \u23ed Skipping MeshRenderer (wrapper, will be reconstructed)");
                    continue;
                }

                try
                {
                    if (_debugMode) GD.Print($"  \U0001f4e6 Serializing {comp.GetType().Name}...");
                    var csDto = AncientCompressor.CompressComponent(comp);
                    if (csDto != null)
                    {
                        syncDto.Components.Add(csDto);
                        if (_debugMode) GD.Print($"    \u2705 {csDto.TypeName} ({csDto.Data?.Length ?? 0} bytes)");
                    }
                    else
                    {
                        if (_debugMode) GD.PrintErr($"    \u274c CompressComponent returned null");
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"    \u274c Failed to serialize {comp.GetType().Name}: {ex.Message}");
                }
            }

            if (_debugMode) GD.Print($"[Network] \U0001f4e6 Total components serialized: {syncDto.Components.Count}");

            // ── XR rig: head + hand poses (local to the player element) ──
            CapturePlayerRig(syncDto, player);

            var message = new MessageDTO
            {
                Sender = new Uri("networkcables://client"),
                MessageType = MessageType.PlayerSync,
                Message = AncientCompressor.Compress(syncDto)
            };

            if (_debugMode) GD.Print($"[Network] \U0001f4e4 Sending PlayerSync message ({message.Message.Length} bytes)...");
            _root.Cables.SendData(message);
            if (_debugMode) GD.Print($"[Network] \u2705 PlayerSync queued for send");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Network] \u274c Failed to send player data: {ex.Message}");
            if (_debugMode) GD.PrintErr($"  Stack: {ex.StackTrace}");
        }
    }
}
