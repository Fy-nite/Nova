using Godot;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using NumQuaternion = System.Numerics.Quaternion;
using V12;
using V12.Basic.Components;
using V12.Components;
using V12.Components.Renderables;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.NetworkCable;
using V12.Core.Networking;
using V12.Core.Rendering;
using V12.WorldML;
using V12.Rendering;
using V12.UI;
using V12.Core.GamePak;
using V12TwoDog;
using V12TwoDog.Godot.Rendering;
using V12TwoDog.Godot.UI;

public partial class RootLoop : Node3D
{
	GameRoot root;
    IRenderer renderer;
    GodotAudioPlayer audioPlayer;
    DebugGameService debug;
    NetworkInspector _networkInspector;
    WorldInspector _worldInspector;
	readonly Dictionary<JoyButton, bool> _prevJoyButtons = new();
	Godot.Vector2 _mouseLook;
	float _mouseSensitivity = 0.002f;
	bool _mouseCaptured;
	XRTrackingService _xr;
    GodotPhysicsBackend _godotPhysics;
    private V12.Core.Systems.PickupSystem _pickup;
    private GamepakLauncher _launcher;
    private bool _gamepakMode;
    // private PortalBinding _portalBinding;

    // ── Laser visual ──
    private MeshInstance3D _laserLine;
    private MeshInstance3D _laserHit;

    // ── Threading ──
    private Thread _v12Thread;
    private CancellationTokenSource _cts;
    private ConcurrentQueue<FrameSnapshot> _frameQueue = new();
    private FrameSnapshot _latestFrame;
    
    // ── Player sync ──
    private float _playerSyncTimer = 0f;
    private const float PlayerSyncInterval = 0.1f; // Send player sync 10 times per second
    private const float HeartbeatIntervalSeconds = 5f; // Send heartbeat every 5 seconds
    private float _heartbeatTimer = 0f;
    private ConcurrentQueue<MessageDTO> _pendingNetworkMessages = new();
    private ConcurrentQueue<System.Action> _pendingMainThreadActions = new();
    private bool _debugMode = false;
    private bool _f8WasPressed = false;

    // ── Multi-world support ──
    private string _localWorldName;

    // ── Archive-loaded world ──
    private World _loadedArchiveWorld;

    // ── Remote player tweening ──
    private record RemoteTweenState(
        System.Numerics.Vector3 VisualPosition,
        NumQuaternion VisualRotation,
        System.Numerics.Vector3 TargetPosition,
        NumQuaternion TargetRotation);
    private readonly Dictionary<long, RemoteTweenState> _remoteTweens = new();
    private const float TweenSpeed = 12f; // Higher = faster snap to target

    public override void _Input(Godot.InputEvent @event)
    {
        if (@event is Godot.InputEventMouseMotion motion)
        {
            _mouseLook.X += motion.Relative.X;
            _mouseLook.Y += motion.Relative.Y;
        }
    }

    void SetMouseCaptured(bool captured)
    {
        Input.MouseMode = captured ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        _mouseCaptured = captured;
    }

    public override void _Ready()
    {
		V12.Core.Networking.BsonConfig.Initialize();
		Console.SetOut(new GodotConsoleWriter());
		root = new GameRoot();
		renderer = new V12TwoDog.Renderer(GetTree());

        audioPlayer = new GodotAudioPlayer();
        audioPlayer.Initialize(root);
        root.Registry.Register("IAudioPlayer", audioPlayer);
        AddChild(audioPlayer);
        root.Registry.Register("IRenderer", renderer);

        var renderTargetFactory = new GodotRenderTargetFactory();
        root.Registry.Register(nameof(IRenderTargetFactory), renderTargetFactory);

        var uiProvider = new GodotUIProvider();
        root.Registry.Register(nameof(IUIProvider), uiProvider);

        var _input = root.Registry.Get<InputService>();
        if (_input == null )
        {
            _input = new InputService();
            root.Registry.Register("InputService", _input);
        }
        debug = new DebugGameService();
        debug.Initialize(root);
        root.Registry.Register("DebugGameService", debug );

		// ── Initialize XR tracking before bootstrap so it can check availability ──
		var xrInput = root.Registry.Get<V12.Core.Input.InputService>();
        _xr = new XRTrackingService(this, xrInput);
        if (_xr.IsAvailable)
        {
            root.Registry.Register("XRTrackingService", _xr);
            GD.Print("[RootLoop] XR support active.");
        }
        else
        {
            GD.Print("[RootLoop] XR not available, running in desktop mode.");
        }

        // Register Godot physics backend BEFORE root.Initialize so it is the
        // first IPhysicsBackend in the registry (Bepu PhysicsService also implements
        // the interface but is registered during Initialize and has no Simulation until
        // manually initialized).
        _godotPhysics = new V12TwoDog.GodotPhysicsBackend(GetTree().Root.World3D);
        root.Registry.Register(nameof(V12.Core.Interfaces.Physics.IPhysicsBackend), _godotPhysics);

        // ── Discover game paks ──
        root.LoadGamepacks("gamepaks");

        // ── Parse --gamepak CLI arg ──
        string gamepakName = null;
        foreach (var arg in OS.GetCmdlineArgs())
        {
            if (arg.StartsWith("--gamepak="))
            {
                gamepakName = arg.Substring("--gamepak=".Length);
                break;
            }
        }

        // ── Game pak mode: load a specific pak or show launcher ──
        IV12Gamepack selectedPak = null;
        if (gamepakName != null)
        {
            // --gamepak was specified: load it directly
            selectedPak = root.Gamepaks.FindByName(gamepakName);
            if (selectedPak != null)
            {
                GD.Print($"[RootLoop] --gamepak '{gamepakName}' found, launching...");
                _gamepakMode = true;
                selectedPak.OnStart();
                _afterGamepakStart();
            }
            else
            {
                GD.PrintErr($"[RootLoop] --gamepak '{gamepakName}' not found in gamepaks/.");
            }
        }
        else if (root.Gamepaks.Gamepaks.Count > 0)
        {
            // Game paks exist but none specified: show launcher
            _gamepakMode = true;
            _launcher = new GamepakLauncher(root, root.Gamepaks);
            _launcher.OnLaunch += (pak) =>
            {
                GD.Print($"[RootLoop] Launcher: launching '{pak.Name}'...");
                pak.OnStart();
                _afterGamepakStart();
            };
            ImGui.OnLayout(_launcher.OnLayout);
            GD.Print($"[RootLoop] {root.Gamepaks.Gamepaks.Count} game pak(s) found. Launcher shown.");
        }

        // ── No game pak mode: empty renderer ──
        if (!_gamepakMode)
        {
            GD.Print("[RootLoop] No game paks found. Place .dll files in 'gamepaks/' or use --gamepak=<name>.");
            root.CreateWorld("Empty");
            _localWorldName = root.SelectedWorld?.WorldName;
        }

        // ── Multiplayer client ─────────────────────────────────────────
        // Connects to a headless server at 127.0.0.1:7777. The server's
        // WorldSync replaces the local world; subsequent WorldUpdate
        // messages apply incremental dirty changes.
        root.SetupNetworking(7777, "127.0.0.1");
        root.Cables.OnMessageReceived += HandleNetworkMessage;
        
        // Send player data to server when connected
        var networkClient = root.Registry.Get<NetworkClient>("NetworkClient");
        if (networkClient != null)
        {
            networkClient.OnConnected += () =>
            {
                GD.Print("[Network] ✅ Connected to server!");
                if (_debugMode) GD.Print($"[Network] 📡 NetworkClient.IsConnected: {networkClient.IsConnected}");
                if (_debugMode) GD.Print("[Network] 📤 Sending initial player data...");
                SendPlayerDataToServer();
            };
            networkClient.OnDisconnected += () =>
            {
                GD.Print("[Network] ❌ Disconnected from server");
                // Clean up all remote players (search all active worlds via query API)
                var remotePlayers = root.FindElements(e => e.Name != null && e.Name.StartsWith("RemotePlayer_"));
                foreach (var rp in remotePlayers)
                {
                    var world = root.GetWorldForElement(rp);
                    world?.RemoveElement(rp);
                    GD.Print($"[Network] 🧹 Removed remote player '{rp.Name}'");
                }

                // Switch back to the local world — PersistentWorld (Player, camera) stays intact
                if (_localWorldName != null)
                {
                    var localWorld = root.Worlds.Find(w => w.WorldName == _localWorldName);
                    if (localWorld != null && root.SelectedWorld != localWorld)
                    {
                        root.SelectWorld(localWorld);
                        GD.Print($"[Network] 🔄 Switched back to local world '{_localWorldName}'");
                    }
                }

                // Clear remote tween targets
                _remoteTweens.Clear();
            };
            networkClient.OnConnectionFailed += (ex) =>
            {
                GD.PrintErr($"[Network] ❌ Connection failed: {ex.Message}");
            };
        }
        else
        {
            GD.PrintErr("[Network] ❌ NetworkClient not found in registry!");
        }

        _networkInspector = new NetworkInspector(root);
        ImGui.OnLayout(_networkInspector.OnLayout);

        _worldInspector = new WorldInspector(root);
        ImGui.OnLayout(_worldInspector.OnLayout);

		root.Initialize();
        // Networking is set up but not auto-started. Use Network Inspector UI to connect manually.

        // ── Post-initialize: physics, pickup, laser ──
        if (!_gamepakMode)
            _afterGamepakStart();

        // ── Laser visual ──
        _laserLine = new MeshInstance3D();
        _laserLine.Name = "LaserLine";
        _laserLine.Mesh = new ImmediateMesh();
        var laserMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0, 1, 0, 0.6f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };
        _laserLine.MaterialOverride = laserMat;
        AddChild(_laserLine);

        _laserHit = new MeshInstance3D();
        _laserHit.Name = "LaserHit";
        var hitMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0, 1, 0),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        _laserHit.MaterialOverride = hitMat;
        AddChild(_laserHit);

        if (_xr?.IsAvailable != true)
            CallDeferred(nameof(SetMouseCaptured), true);

        // ── Start V12 worker thread ──
        _cts = new CancellationTokenSource();
        _v12Thread = new Thread(() => V12WorkerLoop(_cts.Token))
        {
            Name = "V12Worker",
            IsBackground = true
        };
        _v12Thread.Start();
    }

    private void V12WorkerLoop(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            float dt = (float)sw.Elapsed.TotalSeconds;
            sw.Restart();
            dt = Math.Clamp(dt, 0.001f, 0.05f);

            try
            {
                // Process any pending network messages on this thread (safe to modify world)
                ProcessPendingNetworkMessages();

                // Process queued inspector edits on this thread (safe to modify world)
                while (WorldInspector.PendingEditActions.TryDequeue(out var editAction))
                    editAction();

                root.Update(dt);

                var frame = root.CaptureFrame();
                _frameQueue.Enqueue(frame);
            }
            catch (Exception ex)
            {
                GD.Print($"[V12Worker] {ex.GetType().Name}: {ex.Message}");
            }

            int sleepMs = Math.Max(1, (int)((1f / 60f - dt) * 1000));
            Thread.Sleep(sleepMs);
        }
    }

	public override void _PhysicsProcess(double delta)
	{
        // ── Update XR tracking and input ──
        _xr?.Update();

        var inputService = root.Registry.Get<V12.Core.Input.InputService>();
		if (inputService != null)
		{
			float moveX = 0f;
			float moveY = 0f;
			float lookX = 0f;
			float lookY = 0f;

			// ── Digital input (keyboard) ──
			if (Input.IsActionPressed("strafe_right")) moveX += 1f;
			if (Input.IsActionPressed("strafe_left")) moveX -= 1f;
			if (Input.IsActionPressed("move_forwards")) moveY += 1f;
			if (Input.IsActionPressed("move_backwards")) moveY -= 1f;

			if (Input.IsActionPressed("look_right")) lookX += 1f;
			if (Input.IsActionPressed("look_left")) lookX -= 1f;
			if (Input.IsActionPressed("look_up")) lookY += 1f;
			if (Input.IsActionPressed("look_down")) lookY -= 1f;

            // ── Analog stick input (gamepad) ──
			float stickLX = Input.GetJoyAxis(0, JoyAxis.LeftX);
			float stickLY = Input.GetJoyAxis(0, JoyAxis.LeftY);
			float stickRX = Input.GetJoyAxis(0, JoyAxis.RightX);
			float stickRY = Input.GetJoyAxis(0, JoyAxis.RightY);

            // ── Mouse look (sent as delta to worker thread via PlayerComponent) ──
            if (_xr?.IsAvailable != true && _mouseCaptured && _mouseLook != Godot.Vector2.Zero)
            {
                var player = root.Player;
                if (player != null)
                {
                    var pc = player.GetComponent<PlayerComponent>();
                    if (pc != null)
                        pc.AddMouseDelta(_mouseLook.X, _mouseLook.Y);
                }
                _mouseLook = Godot.Vector2.Zero;
            }

            const float deadzone = 0.15f;
			if (Mathf.Abs(stickLX) < deadzone) stickLX = 0f;
			if (Mathf.Abs(stickLY) < deadzone) stickLY = 0f;
			if (Mathf.Abs(stickRX) < deadzone) stickRX = 0f;
			if (Mathf.Abs(stickRY) < deadzone) stickRY = 0f;

			if (Mathf.Abs(stickLX) > 0f || Mathf.Abs(stickLY) > 0f)
			{
				moveX = stickLX;
				moveY = -stickLY;
			}
			if (Mathf.Abs(stickRX) > 0f || Mathf.Abs(stickRY) > 0f)
			{
				lookX = stickRX;
				lookY = -stickRY;
			}

			moveX = Mathf.Clamp(moveX, -1f, 1f);
			moveY = Mathf.Clamp(moveY, -1f, 1f);

            // ── Send axis events ──
            inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_right", Value = moveX > 0 ? moveX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_left", Value = moveX < 0 ? -moveX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_forward", Value = moveY > 0 ? moveY : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "move_backward", Value = moveY < 0 ? -moveY : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_right", Value = lookX > 0 ? lookX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_left", Value = lookX < 0 ? -lookX : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_up", Value = lookY > 0 ? lookY : 0f });
			inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "look_down", Value = lookY < 0 ? -lookY : 0f });
            }

			// ── Button events ──
			if (Input.IsActionJustPressed("jump"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "jump", Value = 1 });
			if (Input.IsActionJustPressed("crouch"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "crouch", Value = 1 });
			if (Input.IsActionJustPressed("run"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "run", Value = 1 });
			if (Input.IsActionJustReleased("run"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = "run", Value = 0 });
			if (Input.IsActionJustPressed("lean_left"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "lean_left", Value = 1 });
			if (Input.IsActionJustReleased("lean_left"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = "lean_left", Value = 0 });
			if (Input.IsActionJustPressed("lean_right"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "lean_right", Value = 1 });
			if (Input.IsActionJustReleased("lean_right"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = "lean_right", Value = 0 });
			if (Input.IsActionJustPressed("escape"))
			{
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "esc", Value = 1 });
				if (_xr?.IsAvailable != true)
					SetMouseCaptured(!_mouseCaptured);
			}
			// F10 toggles debug logging
			if (Input.IsKeyPressed(Key.F10))
			{
				if (!_f8WasPressed)
				{
					_debugMode = !_debugMode;
					GD.Print($"[Debug] Debug mode: {(_debugMode ? "ON" : "OFF")}");
					_f8WasPressed = true;
				}
			}
			else
			{
				_f8WasPressed = false;
			}
			if (Input.IsActionJustPressed("interact"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "interact", Value = 1 });
			if (Input.IsActionJustReleased("interact"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = "interact", Value = 0 });

			// ── Fly mode events ──
			if (Input.IsActionJustPressed("fly_toggle"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = "fly_toggle", Value = 1 });
			if (Input.IsActionJustReleased("fly_toggle"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = "fly_toggle", Value = 0 });
			if (Input.IsActionPressed("fly_up"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "fly_up", Value = 1f });
			if (Input.IsActionPressed("fly_down"))
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "fly_down", Value = 1f });
 
			// ── Controller button events ──
			var currentJoy = new Dictionary<JoyButton, bool>();
			for (int i = 0; i < (int)JoyButton.Max; i++)
			{
				var btn = (JoyButton)i;
				currentJoy[btn] = Input.IsJoyButtonPressed(0, btn);
			}

			void SendCtrlBtn(string name, JoyButton btn)
			{
				bool now = currentJoy.GetValueOrDefault(btn, false);
				bool prev = _prevJoyButtons.GetValueOrDefault(btn, false);
				if (now && !prev)
					inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonDown, Name = name, Value = 1 });
				if (!now && prev)
					inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.ButtonUp, Name = name, Value = 0 });
			}

			SendCtrlBtn("jump", JoyButton.A);
			SendCtrlBtn("crouch", JoyButton.B);
			SendCtrlBtn("interact", JoyButton.X);
			SendCtrlBtn("fly_toggle", JoyButton.Y);
			SendCtrlBtn("lean_left", JoyButton.LeftShoulder);
			SendCtrlBtn("lean_right", JoyButton.RightShoulder);
			SendCtrlBtn("esc", JoyButton.Start);

			float ltAxis = Input.GetJoyAxis(0, (JoyAxis)4);
			if (ltAxis > 0.15f)
				inputService.SendEvent(new V12.Core.Input.InputEvent { Type = V12.Core.Input.InputEventType.Axis, Name = "run", Value = ltAxis });

			_prevJoyButtons.Clear();
			foreach (var kv in currentJoy)
				_prevJoyButtons[kv.Key] = kv.Value;

		// ── Flush deferred Godot API calls from the V12 worker thread ──
        GodotMainThread.FlushPending();

        // ── Read physics results back after Godot's physics tick ──
        _godotPhysics?.ReadbackAll();

        // ── Tween remote player positions BEFORE capturing the frame ──
        // This ensures the interpolated positions are baked into the snapshot
        // for the renderer rather than being one frame behind.
        if (_remoteTweens.Count > 0)
        {
            float t = 1f - MathF.Exp(-TweenSpeed * (float)delta);
            foreach (var kvp in _remoteTweens)
            {
                long playerId = kvp.Key;
                RemoteTweenState state = kvp.Value;

                var newVisPos = System.Numerics.Vector3.Lerp(
                    new System.Numerics.Vector3(state.VisualPosition.X, state.VisualPosition.Y, state.VisualPosition.Z),
                    new System.Numerics.Vector3(state.TargetPosition.X, state.TargetPosition.Y, state.TargetPosition.Z),
                    t);

                var newVisRot = System.Numerics.Quaternion.Slerp(state.VisualRotation, state.TargetRotation, t);

                var remoteName = $"RemotePlayer_{playerId}";
                var remote = root.FindElement(e => e.Name == remoteName);
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

        // ── Consume latest frame snapshot on main thread ──
        while (_frameQueue.TryDequeue(out var frame))
            _latestFrame = frame;

        if (_latestFrame != null)
        {
            renderer.ApplySnapshot(_latestFrame);
            audioPlayer.ApplySnapshot(_latestFrame);
        }

        // _portalBinding?.Update();
        debug.Update((float)delta);
        
        // ── Periodic status logging ──
        // Log connection status and world state every 5 seconds
        if (_debugMode && (int)(_playerSyncTimer * 10) % 50 == 0 && _playerSyncTimer > 0.1f)
        {
            var nc = root.Registry.Get<NetworkClient>("NetworkClient");
            GD.Print($"[Status] 📊 Client connected: {nc?.IsConnected ?? false}");
            GD.Print($"[Status] 📦 Pending network messages: {_pendingNetworkMessages.Count}");
            if (root.SelectedWorld != null)
            {
                GD.Print($"[Status] 🌍 World '{root.SelectedWorld.WorldName}': {root.SelectedWorld.Root.Count} root elements");
                foreach (var elem in root.SelectedWorld.Root)
                {
                    GD.Print($"    - {elem.Name} (Id={elem.Id}, Components={elem.Components.Count})");
                }
            }
        }

        // ── Periodic player sync ──
        _playerSyncTimer += (float)delta;
        if (_playerSyncTimer >= PlayerSyncInterval)
        {
            _playerSyncTimer = 0f;
            if (_debugMode) GD.Print($"[Network] ⏰ PlayerSync timer fired, sending data...");
            SendPlayerDataToServer();
        }

        // ── Heartbeat: send a heartbeat message every HeartbeatInterval seconds ──
        _heartbeatTimer += (float)delta;
        if (_heartbeatTimer >= HeartbeatIntervalSeconds)
        {
            _heartbeatTimer = 0f;
            var client = root.Registry.Get<NetworkClient>("NetworkClient");
            if (client != null && client.IsConnected)
            {
                root.Cables.SendData(new MessageDTO
                {
                    Sender = new Uri("networkcables://client"),
                    MessageType = MessageType.Heartbeat,
                    Message = Array.Empty<byte>()
                });
                if (_debugMode) GD.Print($"[Network] 💓 Heartbeat sent");
            }
        }
    }

    private void UpdateLaser()
    {
        if (_pickup == null || !_pickup.HasRay) return;

        var origin = new Godot.Vector3(_pickup.RayOrigin.X, _pickup.RayOrigin.Y, _pickup.RayOrigin.Z);
        var hit = new Godot.Vector3(_pickup.RayHitPoint.X, _pickup.RayHitPoint.Y, _pickup.RayHitPoint.Z);

        // ── Laser line ──
        var im = _laserLine.Mesh as ImmediateMesh;
        if (im != null)
        {
            im.ClearSurfaces();
            im.SurfaceBegin(Mesh.PrimitiveType.Lines);
            im.SurfaceAddVertex(origin);
            im.SurfaceAddVertex(hit);
            im.SurfaceEnd();
        }

        // ── Hit sphere ──
        var color = _pickup.RayHitSomething ? Colors.Green : Colors.Red;
        _laserHit.Visible = _pickup.RayHitSomething;
        if (_pickup.RayHitSomething)
        {
            var sphereMesh = _laserHit.Mesh as SphereMesh;
            if (sphereMesh == null)
            {
                sphereMesh = new SphereMesh { Radius = 0.08f, Height = 0.16f };
                _laserHit.Mesh = sphereMesh;
            }
            _laserHit.Position = hit;
            var mat = _laserHit.MaterialOverride as StandardMaterial3D;
            if (mat != null) mat.AlbedoColor = color;
        }
    }

    private void SendPlayerDataToServer()
    {
        var player = root.Player;
        if (player == null)
        {
            if (_debugMode) GD.PrintErr("[Network] ❌ No local Player found in PersistentWorld to send to server");
            return;
        }

        try
        {
            if (_debugMode)
            {
                GD.Print($"[Network] 📤 Preparing PlayerSync for '{player.Name}' (Id={player.Id})...");
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
                // Skip components that contain runtime references that can't be serialized
                if (comp is PhysicsBodyComponent || comp is PlayerComponent)
                {
                    if (_debugMode) GD.Print($"  ⏭ Skipping {comp.GetType().Name} (non-serializable)");
                    continue;
                }

                // Skip MeshRenderer — it's a wrapper that references a MeshComponent.
                // We serialize the MeshComponent directly, and the receiver can reconstruct
                // the MeshRenderer wrapper if needed. The Mesh property (IMeshRenderable)
                // is a runtime reference that doesn't serialize cleanly.
                if (comp is MeshRenderer)
                {
                    if (_debugMode) GD.Print($"  ⏭ Skipping MeshRenderer (wrapper, will be reconstructed)");
                    continue;
                }

                try
                {
                    if (_debugMode) GD.Print($"  📦 Serializing {comp.GetType().Name}...");
                    var csDto = AncientCompressor.CompressComponent(comp);
                    if (csDto != null)
                    {
                        syncDto.Components.Add(csDto);
                        if (_debugMode) GD.Print($"    ✅ {csDto.TypeName} ({csDto.Data?.Length ?? 0} bytes)");
                    }
                    else
                    {
                        if (_debugMode) GD.PrintErr($"    ❌ CompressComponent returned null");
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"    ❌ Failed to serialize {comp.GetType().Name}: {ex.Message}");
                }
            }

            if (_debugMode) GD.Print($"[Network] 📦 Total components serialized: {syncDto.Components.Count}");

            var message = new MessageDTO
            {
                Sender = new Uri("networkcables://client"),
                MessageType = MessageType.PlayerSync,
                Message = AncientCompressor.Compress(syncDto)
            };

            if (_debugMode) GD.Print($"[Network] 📤 Sending PlayerSync message ({message.Message.Length} bytes)...");
            root.Cables.SendData(message);
            if (_debugMode) GD.Print($"[Network] ✅ PlayerSync queued for send");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Network] ❌ Failed to send player data: {ex.Message}");
            if (_debugMode) GD.PrintErr($"  Stack: {ex.StackTrace}");
        }
    }

    // ── Network message handler ────────────────────────────────────────
    // Called from the networking thread — queue for processing on the V12 worker thread
    // to avoid race conditions with game logic.
    private void HandleNetworkMessage(MessageDTO message)
    {
        if (message == null) return;
        if (_debugMode) GD.Print($"[Network] 📥 Received {message.MessageType} from {message.Sender?.AbsoluteUri} (queueing for processing)");
        _pendingNetworkMessages.Enqueue(message);
    }

    // Called from the V12 worker thread — safe to modify world state here
    private void ProcessPendingNetworkMessages()
    {
        int processed = 0;
        while (_pendingNetworkMessages.TryDequeue(out var message))
        {
            processed++;
            try
            {
                if (_debugMode) GD.Print($"[Network] 🔄 Processing {message.MessageType}...");
                ProcessNetworkMessage(message);
                if (_debugMode) GD.Print($"[Network] ✅ Finished processing {message.MessageType}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Network] ❌ Error processing {message.MessageType}: {ex.Message}");
                if (_debugMode) GD.PrintErr($"  Stack: {ex.StackTrace}");
            }
        }
        if (processed > 0 && _debugMode)
        {
            GD.Print($"[Network] 📊 Processed {processed} message(s) this frame");
        }
    }

    private void ProcessNetworkMessage(MessageDTO message)
    {
        if (message == null) return;

        try
        {
            switch (message.MessageType)
            {
                case MessageType.WorldSync:
                    var received = AncientCompressor.Decompress<World>(message.Message);

                    // Preserve the local world by naming the server world differently
                    var serverWorldName = $"Server_{received.WorldName}";

                    // Remove old physics body references from the previous server world
                    if (root.SelectedWorld != null)
                    {
                        foreach (var el in root.SelectedWorld.Root)
                        {
                            var pbc = el.GetComponent<PhysicsBodyComponent>();
                            if (pbc != null) pbc.Body = null;
                        }
                    }

                    World worldToUse;
                    if (_loadedArchiveWorld != null)
                    {
                        // Use the locally-loaded world (has full mesh data from XML)
                        // and sync server-authoritative IDs so WorldUpdate patches match.
                        _loadedArchiveWorld.WorldName = serverWorldName;
                        SyncElementIds(received, _loadedArchiveWorld);
                        worldToUse = _loadedArchiveWorld;
                        GD.Print($"[Network] WorldSync: using locally-loaded archive world with synced IDs");
                    }
                    else
                    {
                        // Fallback: use BSON-serialized world (no local archive loaded)
                        received.WorldName = serverWorldName;
                        worldToUse = received;
                        GD.Print($"[Network] WorldSync: using BSON world (no local archive available)");
                    }

                    // Find and untrack the old server world so DirtyTracker subscriptions
                    // don't leak when we replace the element tree.
                    var oldServerWorld = root.Worlds.Find(w => w.WorldName == serverWorldName);
                    var dt = root.Registry.Get<DirtyTracker>("DirtyTracker");
                    if (oldServerWorld != null)
                        dt?.UntrackWorld(oldServerWorld);

                    World selected;
                    if (oldServerWorld != null)
                    {
                        oldServerWorld.ReplaceFrom(worldToUse);
                        selected = oldServerWorld;
                    }
                    else
                    {
                        root.Worlds.Add(worldToUse);
                        selected = worldToUse;
                    }
                    root.SelectWorld(selected);

                    // Track the new world's elements for dirty-change propagation
                    dt?.TrackWorld(selected);

                    GD.Print($"[Network] WorldSync applied as '{serverWorldName}' ({selected.Root.Count} root elements). PersistentWorld (Player) untouched.");
                    break;

                case MessageType.WorldArchive:
                    try
                    {
                        var data = message.Message;
                        int nameLen = BitConverter.ToInt32(data, 0);
                        var fileName = Encoding.UTF8.GetString(data, 4, nameLen);
                        var archiveBytes = data.AsSpan(4 + nameLen).ToArray();

                        var worldName = Path.GetFileNameWithoutExtension(fileName);
                        var tempDir = Path.Combine(Path.GetTempPath(), "V12Worlds", worldName + "_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempDir);

                        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
                        File.WriteAllBytes(tempPath, archiveBytes);
                        ZipFile.ExtractToDirectory(tempPath, tempDir);
                        File.Delete(tempPath);

                        // Check if an AssetResolver already exists — if so, add a mount to it
                        // instead of creating a new one (Registry.Register fails silently if name exists)
                        var existingResolver = root.Registry.Get<V12.Core.Interfaces.IAssetResolver>();
                        if (existingResolver is V12AssetResolver vr)
                        {
                            vr.Mount(worldName, tempDir);
                            GD.Print($"[Network] Added mount '{worldName}' → '{tempDir}' to existing AssetResolver");
                        }
                        else
                        {
                            var resolver = new V12AssetResolver();
                            resolver.Mount(worldName, tempDir);
                            root.Registry.Register("AssetResolver", resolver);
                            GD.Print($"[Network] Registered new AssetResolver with mount '{worldName}' → '{tempDir}'");
                        }

                        var templates = new WorldTemplateProvider();
                        var templatesDir = Path.Combine(tempDir, "templates");
                        if (Directory.Exists(templatesDir))
                            templates.LoadFromDirectory(templatesDir);
                        
                        var existingTemplates = root.Registry.Get<V12.WorldML.WorldTemplateProvider>();
                        if (existingTemplates != null)
                        {
                            // Merge templates from the new archive
                            if (Directory.Exists(templatesDir))
                                existingTemplates.LoadFromDirectory(templatesDir);
                            GD.Print($"[Network] Merged templates into existing TemplateProvider");
                        }
                        else
                        {
                            root.Registry.Register("TemplateProvider", templates);
                            GD.Print($"[Network] Registered new TemplateProvider");
                        }

                        GD.Print($"[Network] V12World archive received: '{fileName}' ({archiveBytes.Length} bytes) → {tempDir}");

                        // ── Also load the world from the extracted archive ──
                        // This gives us the full element tree with all components,
                        // mesh vertex data, etc., correctly parsed from XML rather
                        // than relying on BSON serialization which can lose data.
                        try
                        {
                            var worldXmlPath = Path.Combine(tempDir, "world.xml");
                            if (!File.Exists(worldXmlPath))
                                worldXmlPath = Path.Combine(tempDir, "main.xml");
                            if (File.Exists(worldXmlPath))
                            {
                                var loadTemplates = existingTemplates ?? new WorldTemplateProvider();
                                var parser = new WorldMLParser { TemplateProvider = loadTemplates };
                                var parsedRoot = parser.ParseFile(worldXmlPath);
                                _loadedArchiveWorld = new World(parsedRoot.Name ?? worldName) { ExtractPath = tempDir, MountPoint = worldName };
                                _loadedArchiveWorld.AddElement(parsedRoot);
                                GD.Print($"[Network] Loaded world from archive XML: '{_loadedArchiveWorld.WorldName}' ({_loadedArchiveWorld.Root.Count} root elements, {CountElementsRecursive(_loadedArchiveWorld.Root)} total elements)");
                            }
                            else
                            {
                                GD.PrintErr($"[Network] No world.xml or main.xml found in extracted archive at {tempDir}");
                                _loadedArchiveWorld = null;
                            }
                        }
                        catch (Exception loadEx)
                        {
                            GD.PrintErr($"[Network] Failed to load world from archive: {loadEx.Message}");
                            _loadedArchiveWorld = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[Network] Error processing WorldArchive: {ex.Message}");
                    }
                    break;

                case MessageType.Heartbeat:
                    // Heartbeats are informational — client already knows it's connected.
                    // No action needed. (Sender echo is handled by the PlayerSync ignore logic.)
                    if (_debugMode) GD.Print($"[Network] 💓 Heartbeat received from {message.Sender}");
                    break;

                case MessageType.PlayerSync:
                    try
                    {
                        if (_debugMode) GD.Print($"[Network] 📨 Received PlayerSync message, deserializing...");
                        var playerSync = AncientCompressor.Decompress<PlayerSyncDTO>(message.Message);
                        if (playerSync == null)
                        {
                            if (_debugMode) GD.PrintErr($"[Network] ❌ PlayerSync deserialization returned null");
                            break;
                        }

                        if (_debugMode)
                        {
                            GD.Print($"[Network] 📨 PlayerSync details:");
                            GD.Print($"  PlayerName: {playerSync.PlayerName}");
                            GD.Print($"  PlayerId: {playerSync.PlayerId}");
                            GD.Print($"  Position: ({playerSync.Position.X:F2}, {playerSync.Position.Y:F2}, {playerSync.Position.Z:F2})");
                            GD.Print($"  Components: {playerSync.Components.Count}");
                        }

                        // Check if this is our own player (ignore it)
                        var localPlayer = root.Player;
                        if (localPlayer != null && localPlayer.Id == playerSync.PlayerId)
                        {
                            if (_debugMode) GD.Print($"[Network] ⏭ Ignoring own player sync (localPlayer.Id={localPlayer.Id} == sync.PlayerId={playerSync.PlayerId})");
                            break;
                        }

                        if (_debugMode) GD.Print($"[Network] ✓ Not our player (localPlayer.Id={localPlayer?.Id ?? -1}, sync.PlayerId={playerSync.PlayerId})");

                        // Create or update remote player
                        var remotePlayerName = $"RemotePlayer_{playerSync.PlayerId}";
                        var remotePlayer = root.FindElement(e => e.Name == remotePlayerName);

                        if (_debugMode) GD.Print($"[Network] 🔍 Looking for existing remote player '{remotePlayerName}': {(remotePlayer != null ? "FOUND" : "NOT FOUND")}");

                        if (remotePlayer == null)
                        {
                            if (_debugMode) GD.Print($"[Network] 🆕 Creating new remote player '{remotePlayerName}'...");

                            // Create new remote player
                            remotePlayer = new Element
                            {
                                Name = remotePlayerName,
                                LocalTransform = new TRS
                                {
                                    Position = playerSync.Position,
                                    Rotation = playerSync.Rotation,
                                    Scale = System.Numerics.Vector3.One
                                }
                            };

                            if (_debugMode) GD.Print($"[Network]   Created Element with Id={remotePlayer.Id}");
                            if (_debugMode) GD.Print($"[Network]   Deserializing {playerSync.Components.Count} components...");

                            // Deserialize components
                            foreach (var csDto in playerSync.Components)
                            {
                                try
                                {
                                    if (_debugMode) GD.Print($"[Network]     Deserializing {csDto.TypeName} ({csDto.Data?.Length ?? 0} bytes)...");
                                    var comp = AncientCompressor.DecompressComponent(csDto);
                                    if (comp != null)
                                    {
                                        // Skip PlayerComponent for remote players (we don't control them)
                                        if (comp is PlayerComponent)
                                        {
                                            if (_debugMode) GD.Print($"[Network]       ⏭ Skipping PlayerComponent");
                                            continue;
                                        }
                                        
                                        remotePlayer.Components.Add(comp);
                                        comp.OnAttach(remotePlayer);
                                        if (_debugMode) GD.Print($"[Network]       ✅ Added {comp.GetType().Name}");
                                    }
                                    else
                                    {
                                        if (_debugMode) GD.PrintErr($"[Network]       ❌ DecompressComponent returned null for {csDto.TypeName}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    GD.PrintErr($"[Network]       ❌ Failed to deserialize {csDto.TypeName}: {ex.Message}");
                                }
                            }

                            if (_debugMode) GD.Print($"[Network]   Total components on remote player: {remotePlayer.Components.Count}");

                            // If we have a MeshComponent but no MeshRenderer, create a MeshRenderer wrapper
                            // so the remote player is visible. The renderer looks for IMeshRenderable components.
                            var meshComp = remotePlayer.Components.OfType<MeshComponent>().FirstOrDefault();
                            if (_debugMode) GD.Print($"[Network]   MeshComponent found: {(meshComp != null ? "YES" : "NO")}");
                            if (meshComp != null && _debugMode)
                            {
                                GD.Print($"[Network]     Shape: {meshComp.Shape}, Size: ({meshComp.Width}, {meshComp.Height}, {meshComp.Depth})");
                            }

                            if (meshComp != null && !remotePlayer.Components.Any(c => c is MeshRenderer))
                            {
                                if (_debugMode) GD.Print($"[Network]   🎨 Creating MeshRenderer wrapper...");
                                var mr = new MeshRenderer { Mesh = meshComp };
                                remotePlayer.Components.Add(mr);
                                mr.OnAttach(remotePlayer);
                                if (_debugMode) GD.Print($"[Network]   ✅ MeshRenderer created and attached");
                            }

                            if (_debugMode) GD.Print($"[Network]   Adding remote player to world...");
                            root.SelectedWorld?.AddElement(remotePlayer);
                            if (_debugMode) GD.Print($"[Network]   ✅ Added to world. World now has {root.SelectedWorld?.Root.Count ?? 0} root elements");

                            root.Registry.Get<DirtyTracker>("DirtyTracker")?.TrackElement(remotePlayer);
                            // Initialise tween state so interpolation starts from the correct position
                            _remoteTweens[playerSync.PlayerId] = new RemoteTweenState(playerSync.Position, playerSync.Rotation, playerSync.Position, playerSync.Rotation);
                            if (_debugMode) GD.Print($"[Network] ✅ Remote player '{remotePlayerName}' created successfully");

                            // Debug: list all root elements
                            if (_debugMode)
                            {
                                GD.Print($"[Network] 📋 Current world root elements:");
                                foreach (var elem in root.SelectedWorld?.Root)
                                {
                                    GD.Print($"    - {elem.Name} (Id={elem.Id}, Components={elem.Components.Count}, Children={elem.Children.Count})");
                                }
                            }
                        }
                        else
                        {
                            if (_debugMode) GD.Print($"[Network] 🔄 Updating existing remote player '{remotePlayerName}'...");
                            // Update the tween target; visual position will interpolate toward it each frame.
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

                            if (_debugMode) GD.Print($"[Network] ✅ Set tween target ({targetPos.X:F2}, {targetPos.Y:F2}, {targetPos.Z:F2})");
                        }
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[Network] ❌ Error handling PlayerSync: {ex.Message}");
                        if (_debugMode) GD.PrintErr($"  Stack: {ex.StackTrace}");
                    }
                    break;

                case MessageType.PlayerLeave:
                    try
                    {
                        var leaveDto = AncientCompressor.Decompress<PlayerLeaveDTO>(message.Message);
                        if (leaveDto == null) break;

                        GD.Print($"[Network] 📴 PlayerLeave received for PlayerId: {leaveDto.PlayerId}");

                        var remotePlayerName = $"RemotePlayer_{leaveDto.PlayerId}";
                        var remotePlayer = root.FindElement(e => e.Name == remotePlayerName);
                        if (remotePlayer != null)
                        {
                            var world = root.GetWorldForElement(remotePlayer);
                            world?.RemoveElement(remotePlayer);
                            GD.Print($"[Network] ✅ Removed remote player '{remotePlayerName}' from world");
                        }
                        else
                        {
                            if (_debugMode) GD.Print($"[Network] ⚠ Remote player '{remotePlayerName}' not found (already removed?)");
                        }

                        // Clean up tween state for this player
                        _remoteTweens.Remove(leaveDto.PlayerId);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[Network] ❌ Error handling PlayerLeave: {ex.Message}");
                    }
                    break;

                case MessageType.WorldUpdate:
                    ComponentBatchDTO batch = null;
                    try { batch = AncientCompressor.Decompress<ComponentBatchDTO>(message.Message); }
                    catch { }

                    if (batch == null || batch.Components.Count == 0) break;

                    lock (root)
                    {
                        foreach (var snapshot in batch.Components)
                        {
                            if (snapshot.Payload == null || snapshot.Payload.Length == 0) continue;
                            try
                            {
                                var csDto    = AncientCompressor.Decompress<ComponentSyncDTO>(snapshot.Payload);
                                var incoming = AncientCompressor.DecompressComponent(csDto);
                                if (incoming == null) continue;

                                // Search all active worlds for a component matching this ID (recursive)
                                ApplyComponentUpdateRecursive(incoming, snapshot.Id);
                            }
                            catch { }
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[Network] Error handling {message.MessageType}: {ex.Message}");
        }
    }

    /// <summary>
    /// Recursively search the world tree for a component matching the given ID,
    /// and copy public writable properties from the incoming component to the target.
    /// </summary>
    private void ApplyComponentUpdateRecursive(IComponent incoming, long targetId)
    {
        bool found = false;
        foreach (var w in root.ActiveWorlds)
        {
            foreach (var element in w.Root)
            {
                if (TryApplyToElement(element, incoming, targetId))
                {
                    found = true;
                    break;
                }
            }
            if (found) break;
        }
    }

    private static bool TryApplyToElement(IWorldElement element, IComponent incoming, long targetId)
    {
        // Check this element's components
        var target = element.Components.Find(c => c.Id == targetId);
        if (target != null)
        {
            foreach (var prop in incoming.GetType()
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (prop.Name == "Id" || !prop.CanRead || !prop.CanWrite) continue;
                try { prop.SetValue(target, prop.GetValue(incoming)); } catch { }
            }
            return true;
        }

        // Recurse into children
        foreach (var child in element.Children)
        {
            if (TryApplyToElement(child, incoming, targetId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Initialize physics, locomotion, script, and pickup systems.
    /// Called after a game pak has set up its world.
    /// </summary>
    private void _afterGamepakStart()
    {
        // ── Attach gamepak root widget to scene tree ──
        var rootWidget = root.Registry.Get<IWidget>("RootWidget");
        if (rootWidget?.NativeControl is Node node)
        {
            GD.Print($"[RootLoop] Adding gamepak root widget to scene tree.");
            AddChild(node);
        }

        root.Registry.Get<V12.Core.Systems.PhysicsLocomotionSystem>()?.Initialize(root);
        root.Registry.Get<V12.Core.Systems.LocomotionSystem>()?.Initialize(root);
        root.Registry.Get<V12.Core.Systems.ScriptSystem>()?.Initialize();

        _pickup = root.Registry.Get<V12.Core.Systems.PickupSystem>();
        _pickup?.Initialize(root);

        _localWorldName = root.SelectedWorld?.WorldName;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            // _portalBinding?.Cleanup();
            _xr?.Cleanup();
            _cts?.Cancel();
            _v12Thread?.Join(1000);
        }
    }
}

public class GodotConsoleWriter : TextWriter
{
    private readonly StringBuilder _buffer = new StringBuilder();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(string value)
    {
        _buffer.Append(value);
    }

    public override void WriteLine(string value)
    {
        _buffer.Append(value);
        Flush();
    }

    public override void WriteLine()
    {
        Flush();
    }

    public override void Flush()
    {
        if (_buffer.Length > 0)
        {
            GD.Print(_buffer.ToString().TrimEnd('\r', '\n'));
            _buffer.Clear();
        }
    }
}
