using Godot;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
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

	XRTrackingService _xr;
    GodotPhysicsBackend _godotPhysics;
    private V12.Core.Systems.PickupSystem _pickup;
    private GamepakLauncher _launcher;
    private bool _gamepakMode;

    // ── Handlers ──
    private InputHandler _inputHandler;
    private NetworkHandler _networkHandler;
    private WorldSyncHandler _worldSyncHandler;
    private RemotePlayerManager _remotePlayerManager;
    private LaserVisual _laserVisual;

    // ── Threading ──
    private Thread _v12Thread;
    private CancellationTokenSource _cts;
    private ConcurrentQueue<FrameSnapshot> _frameQueue = new();
    private FrameSnapshot _latestFrame;

    // ── Debug ──
    private bool _debugMode = false;

    public override void _Input(Godot.InputEvent @event)
    {
        if (@event is Godot.InputEventMouseMotion motion)
            _inputHandler?.AccumulateMouseLook(motion.Relative);
    }

    void SetMouseCaptured(bool captured)
    {
        Input.MouseMode = captured ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (_inputHandler != null) _inputHandler.MouseCaptured = captured;
    }

    public override void _Ready()
    {
		V12.Core.Networking.BsonConfig.Initialize();
		Console.SetOut(new GodotConsoleWriter());

        // Enable Serilog so GameRoot.Log / GamepackLoader log messages actually appear
        GameRoot.ConfigureLogging();

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

        _godotPhysics = new V12TwoDog.GodotPhysicsBackend(GetTree().Root.World3D);
        root.Registry.Register(nameof(V12.Core.Interfaces.Physics.IPhysicsBackend), _godotPhysics);

        // ── Discover game paks ──
        var gamepaksDir = Path.Combine(AppContext.BaseDirectory, "gamepaks");
        GD.Print($"[RootLoop] Looking for gamepaks in: '{gamepaksDir}' (exists={Directory.Exists(gamepaksDir)})");
        root.LoadGamepacks(gamepaksDir);

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
            _gamepakMode = true;
            _launcher = new GamepakLauncher(root, root.Gamepaks);
            _launcher.OnLaunch += (pak) =>
            {
                GD.Print($"[RootLoop] Launcher: launching '{pak.Name}'...");
                pak.OnStart();
                _afterGamepakStart();
            };
            // ImGui.OnLayout(_launcher.OnLayout);
            GD.Print($"[RootLoop] {root.Gamepaks.Gamepaks.Count} game pak(s) found. Launcher shown.");
        }

        if (!_gamepakMode)
        {
            GD.Print("[RootLoop] No game paks found. Place .dll files in 'gamepaks/' or use --gamepak=<name>.");
            root.CreateWorld("Empty");
        }

        // ── Create handlers ──
        _inputHandler = new InputHandler();
        _worldSyncHandler = new WorldSyncHandler(root);
        _remotePlayerManager = new RemotePlayerManager(root);
        _networkHandler = new NetworkHandler(root, _worldSyncHandler, _remotePlayerManager);
        _laserVisual = new LaserVisual();

        // ── Multiplayer client ─────────────────────────────────────────
        root.SetupNetworking(7777, "127.0.0.1");
        root.Cables.OnMessageReceived += _networkHandler.HandleNetworkMessage;

        var networkClient = root.Registry.Get<NetworkClient>("NetworkClient");
        if (networkClient != null)
        {
            networkClient.OnConnected += () =>
            {
                GD.Print("[Network] \u2705 Connected to server!");
                if (_debugMode) GD.Print($"[Network] \U0001f4e1 NetworkClient.IsConnected: {networkClient.IsConnected}");
                if (_debugMode) GD.Print($"[Network] \U0001f4e4 Sending initial player data...");
                _networkHandler.SendPlayerDataToServer();
            };
            networkClient.OnDisconnected += () =>
            {
                GD.Print("[Network] \u274c Disconnected from server");

                // Clean up all remote players
                var remotePlayers = root.FindElements(e => e.Name != null && e.Name.StartsWith("RemotePlayer_"));
                foreach (var rp in remotePlayers)
                {
                    var world = root.GetWorldForElement(rp);
                    world?.RemoveElement(rp);
                    GD.Print($"[Network] \U0001f9f9 Removed remote player '{rp.Name}'");
                }

                // Switch back to the local world
                if (_remotePlayerManager.LocalWorldName != null)
                {
                    var localWorld = root.Worlds.Find(w => w.WorldName == _remotePlayerManager.LocalWorldName);
                    if (localWorld != null && root.SelectedWorld != localWorld)
                    {
                        root.SelectWorld(localWorld);
                        GD.Print($"[Network] \U0001f504 Switched back to local world '{_remotePlayerManager.LocalWorldName}'");
                    }
                }

                _remotePlayerManager.ClearTweens();
            };
            networkClient.OnConnectionFailed += (ex) =>
            {
                GD.PrintErr($"[Network] \u274c Connection failed: {ex.Message}");
            };
        }
        else
        {
            GD.PrintErr("[Network] \u274c NetworkClient not found in registry!");
        }

        // _networkInspector = new NetworkInspector(root);
        // ImGui.OnLayout(_networkInspector.OnLayout);

        // _worldInspector = new WorldInspector(root);
        // ImGui.OnLayout(_worldInspector.OnLayout);

		root.Initialize();

        if (!_gamepakMode)
            _afterGamepakStart();

        _laserVisual.Initialize(this);

        if (_xr?.IsAvailable != true && renderer?.LockMouse == true)
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
                _networkHandler.ProcessPendingNetworkMessages();

                // while (WorldInspector.PendingEditActions.TryDequeue(out var editAction))
                //     editAction();

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
        _xr?.Update();

        // ── Sync mouse capture with renderer option ──
        if (_xr?.IsAvailable != true && renderer?.LockMouse == true && !(_inputHandler?.MouseCaptured ?? false))
            SetMouseCaptured(true);
        else if (renderer?.LockMouse == false && (_inputHandler?.MouseCaptured ?? false))
            SetMouseCaptured(false);

        // ── Input ──
        var inputService = root.Registry.Get<V12.Core.Input.InputService>();
        if (_inputHandler?.ProcessFrame(inputService, _xr?.IsAvailable == true, root) == true)
        {
            if (_xr?.IsAvailable != true)
            {
                var newCaptured = !_inputHandler.MouseCaptured;
                SetMouseCaptured(newCaptured);
                if (renderer != null) renderer.LockMouse = newCaptured;
            }
        }
        if (_inputHandler != null) _debugMode = _inputHandler.DebugMode;

        // ── Flush deferred Godot API calls from the V12 worker thread ──
        GodotMainThread.FlushPending();

        // ── Read physics results back after Godot's physics tick ──
        _godotPhysics?.ReadbackAll();

        // ── Tween remote player positions BEFORE capturing the frame ──
        _remotePlayerManager?.UpdateTweens((float)delta);

        // ── Consume latest frame snapshot on main thread ──
        while (_frameQueue.TryDequeue(out var frame))
            _latestFrame = frame;

        if (_latestFrame != null)
        {
            renderer.ApplySnapshot(_latestFrame);
            audioPlayer.ApplySnapshot(_latestFrame);
        }

        debug.Update((float)delta);

        // ── Network timers (sync, heartbeat, status logging) ──
        _networkHandler?.Update((float)delta, _debugMode);

        // ── Laser visual ──
        _laserVisual?.Update(_pickup);
    }

    /// <summary>
    /// Initialize physics, locomotion, script, and pickup systems.
    /// Called after a game pak has set up its world.
    /// </summary>
    private void _afterGamepakStart()
    {
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

        _remotePlayerManager.LocalWorldName = root.SelectedWorld?.WorldName;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
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
