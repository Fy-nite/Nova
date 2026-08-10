using Godot;

using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using V12;
using V12.Core;
using V12.Core.GamePak;
using V12.Core.Input;
using V12.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.NetworkCable;
using V12.Core.Networking;
using V12.Core.Rendering;
using V12.Core.Systems;
using V12.Rendering;
using V12.UI;
using V12TwoDog.Godot.Rendering;
using V12TwoDog.Godot.UI;

namespace V12TwoDog
{
    /// <summary>
    /// Options controlling what <see cref="V12Runtime"/> wires up. Everything
    /// defaults to the behaviour the original RootLoop had, so a plain
    /// <c>V12Runtime.Create(hostNode)</c> is a drop-in replacement.
    /// </summary>
    public sealed class V12RuntimeOptions
    {
        /// <summary>Attempt OpenXR initialisation (and keep retrying briefly).</summary>
        public bool EnableXR { get; set; } = true;

        /// <summary>Forward Godot keyboard/mouse/gamepad input into the V12 InputService.</summary>
        public bool EnableInput { get; set; } = true;

        /// <summary>Set up the NetworkCables client/server and sync handlers.</summary>
        public bool EnableNetworking { get; set; } = true;

        /// <summary>Port used when connecting (client) or listening (server).</summary>
        public int NetworkPort { get; set; } = 7777;

        /// <summary>Host to connect to. When null the runtime listens as a server.</summary>
        public string? ConnectHost { get; set; }

        /// <summary>Directory scanned for gamepak DLLs. "gamepaks" matches the old host behaviour.</summary>
        public string GamepakDirectory { get; set; } = "gamepaks";

        /// <summary>
        /// Assemblies to scan for gamepaks via direct project reference (no DLL
        /// scanning) — e.g. the Sample Game so it runs when pressing Play in the
        /// editor or on a headset. Paks found here are loaded, initialized, and
        /// started immediately, bypassing the launcher/force/empty-world logic.
        /// </summary>
        public IReadOnlyList<System.Reflection.Assembly>? GamepakAssemblies { get; set; }

        /// <summary>Name of a gamepak to launch immediately (equivalent of --gamepak=&lt;name&gt;).</summary>
        public string? ForceGamepak { get; set; }

        /// <summary>When paks exist but none is forced, show the launcher (else pick none).</summary>
        public bool ShowLauncher { get; set; } = true;

        /// <summary>Create an empty world when no gamepak is running.</summary>
        public bool CreateEmptyWorldIfNoPak { get; set; } = true;

        /// <summary>Capture the mouse at startup when not in XR.</summary>
        public bool LockMouseOnStart { get; set; } = true;
    }

    /// <summary>
    /// The complete V12 host wiring, extracted from the old scene-script
    /// <c>RootLoop</c> so any .NET process that can run Godot (the 2dog host,
    /// a WinForms/WPF app, a headless server, tests) can embed it:
    ///
    /// <list type="bullet">
    /// <item>GameRoot + registry + platform services (renderer, audio, physics, UI)</item>
    /// <item>XR tracking, input translation, gamepak discovery/launch</item>
    /// <item>Networking (client/server) + remote player interpolation</item>
    /// <item>A background worker thread driving <c>GameRoot.Update</c> and snapshot capture</item>
    /// <item>Main-thread pump (<see cref="ProcessPhysics"/>) that applies the latest frame</item>
    /// </list>
    ///
    /// Call <see cref="Create"/> once from a Godot node's <c>_Ready</c>, forward
    /// <c>_Input</c> to <see cref="HandleInputEvent"/>, call <see cref="ProcessPhysics"/>
    /// from <c>_PhysicsProcess</c>, and <see cref="Dispose"/> on node destruction.
    /// </summary>
    public sealed class V12Runtime : IDisposable
    {
        private readonly Node3D _host;
        private readonly V12RuntimeOptions _options;

        public GameRoot Root { get; }
        // Assigned in the constructor. (The compiler's flow analysis loses track of
        // interface-typed auto-properties here, hence the null-forgiving markers.)
        public IRenderer RendererService { get; } = null!;
        public GodotAudioPlayer AudioPlayer { get; }        public XRTrackingService? XR { get; private set; }
        public bool XrAvailable => XR?.IsAvailable == true;
        public InputHandler? InputHandler { get; private set; }
        public NetworkHandler? NetworkHandler { get; private set; }
        public RemotePlayerManager? RemotePlayerManager { get; private set; }
        public bool GamepakMode { get; private set; }
        public FrameSnapshot? LatestFrame => _latestFrame;

        private GodotPhysicsBackend? _physics;
        private DebugGameService? _debug;
        private LaserVisual? _laserVisual;
        private WorldCanvasSystem? _worldCanvas;
        private GamepakLauncher? _launcher;
        private PickupSystem? _pickup;

        // ── Threading ──
        private readonly ConcurrentQueue<FrameSnapshot> _frameQueue = new();
        private FrameSnapshot? _latestFrame;
        private Thread? _v12Thread;
        private CancellationTokenSource? _cts;
        private bool _debugMode;
        private bool _disposed;

        private V12Runtime(Node3D host, V12RuntimeOptions options)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _options = options ?? new V12RuntimeOptions();

            V12.Core.Networking.BsonConfig.Initialize();
            Console.SetOut(new GodotConsoleWriter());

            // Enable Serilog so GameRoot.Log / GamepackLoader log messages actually appear
            GameRoot.ConfigureLogging();

            Root = new GameRoot();
            var renderer = new global::V12TwoDog.Renderer(host.GetTree());
            RendererService = renderer;
            AudioPlayer = new GodotAudioPlayer();
            AudioPlayer.Initialize(Root);
            Root.Registry.Register("IAudioPlayer", AudioPlayer);
            host.AddChild(AudioPlayer);
            Root.Registry.Register("IRenderer", RendererService);

            var renderTargetFactory = new GodotRenderTargetFactory();
            Root.Registry.Register(nameof(IRenderTargetFactory), renderTargetFactory);

            var uiProvider = new GodotUIProvider();
            Root.Registry.Register(nameof(IUIProvider), uiProvider);

            var inputService = Root.Registry.Get<InputService>();
            if (inputService == null)
            {
                inputService = new InputService();
                Root.Registry.Register("InputService", inputService);
            }

            _debug = new DebugGameService();
            _debug.Initialize(Root);
            Root.Registry.Register("DebugGameService", _debug);

            // ── XR: attempt now, keep retrying while the runtime runs ──
            if (_options.EnableXR)
            {
                TryEnableXr(inputService);
            }

            _physics = new GodotPhysicsBackend(host.GetTree().Root.World3D);
            Root.Registry.Register(nameof(V12.Core.Interfaces.Physics.IPhysicsBackend), _physics);

            // ── Discover game paks ──
            Root.LoadGamepacks(_options.GamepakDirectory);

            // ── Direct-referenced gamepaks (ProjectReference, e.g. the Sample
            // Game so it runs when pressing Play in the editor / on a headset):
            // load + initialize + start them now, exactly like the forced path.
            bool assemblyPakStarted = false;
            if (_options.GamepakAssemblies != null)
            {
                foreach (var asm in _options.GamepakAssemblies)
                {
                    try
                    {
                        // Dedup guard: if the directory scan already loaded a pak
                        // from an assembly with this name (e.g. a stale gamepaks/
                        // folder), skip — double-loading would create a second
                        // type instance and duplicate worlds.
                        bool alreadyLoaded = Root.Gamepaks.Gamepaks.Any(p =>
                            p.GetType().Assembly.GetName().Name == asm.GetName().Name);

                        if (alreadyLoaded)
                        {
                            GD.Print($"[V12Runtime] Gamepaks from '{asm.GetName().Name}' already loaded via directory scan — skipping assembly load.");
                            continue;
                        }

                        int count = Root.LoadGamepacksFromAssembly(asm);
                        if (count > 0) assemblyPakStarted = true;
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[V12Runtime] Failed to load gamepaks from '{asm.GetName().Name}': {ex.Message}");
                    }
                }
            }

            // ── Pick a gamepak: direct assembly, forced, or launcher, or none ──
            string? gamepakName = _options.ForceGamepak ?? ParseGamepakArg();
            IV12Gamepack? selectedPak = null;

            if (assemblyPakStarted)
            {
                GD.Print($"[V12Runtime] {Root.Gamepaks.Gamepaks.Count} game pak(s) running from direct assembly reference(s).");
                GamepakMode = true;
                AfterGamepakStart();
            }
            else if (gamepakName != null)
            {
                selectedPak = Root.Gamepaks.FindByName(gamepakName);
                if (selectedPak != null)
                {
                    GD.Print($"[V12Runtime] --gamepak '{gamepakName}' found, launching...");
                    GamepakMode = true;
                    selectedPak.OnStart();
                    AfterGamepakStart();
                }
                else
                {
                    GD.PrintErr($"[V12Runtime] --gamepak '{gamepakName}' not found in gamepaks/.");
                }
            }
            else if (Root.Gamepaks.Gamepaks.Count > 0 && _options.ShowLauncher)
            {
                GamepakMode = true;
                _launcher = new GamepakLauncher(Root, Root.Gamepaks);
                _launcher.OnLaunch += (pak) =>
                {
                    GD.Print($"[V12Runtime] Launcher: launching '{pak.Name}'...");
                    GamepakMode = true;
                    pak.OnStart();
                    AfterGamepakStart();
                };
                GD.Print($"[V12Runtime] {Root.Gamepaks.Gamepaks.Count} game pak(s) found. Launcher shown.");
            }

            if (!GamepakMode)
            {
                if (_options.CreateEmptyWorldIfNoPak)
                {
                    GD.Print("[V12Runtime] No game paks running. Creating empty world.");
                    Root.CreateWorld("Empty");
                }
            }

            // ── Handlers ──
            InputHandler = new InputHandler();
            var worldSyncHandler = new WorldSyncHandler(Root);
            RemotePlayerManager = new RemotePlayerManager(Root);
            NetworkHandler = new NetworkHandler(Root, worldSyncHandler, RemotePlayerManager);
            _laserVisual = new LaserVisual();

            // ── Multiplayer ──
            if (_options.EnableNetworking)
            {
                Root.SetupNetworking(_options.NetworkPort, _options.ConnectHost);
                Root.Cables.OnMessageReceived += NetworkHandler.HandleNetworkMessage;

                var networkClient = Root.Registry.Get<NetworkClient>("NetworkClient");
                if (networkClient != null)
                {
                    networkClient.OnConnected += () =>
                    {
                        GD.Print("[Network] \u2705 Connected to server!");
                        NetworkHandler.SendPlayerDataToServer();
                    };
                    networkClient.OnDisconnected += () =>
                    {
                        GD.Print("[Network] \u274c Disconnected from server");

                        // Clean up all remote players
                        var remotePlayers = Root.FindElements(e => e.Name != null && e.Name.StartsWith("RemotePlayer_"));
                        foreach (var rp in remotePlayers)
                        {
                            var world = Root.GetWorldForElement(rp);
                            world?.RemoveElement(rp);
                            GD.Print($"[Network] \U0001f9f9 Removed remote player '{rp.Name}'");
                        }

                        // Switch back to the local world
                        if (RemotePlayerManager.LocalWorldName != null)
                        {
                            var localWorld = Root.Worlds.Find(w => w.WorldName == RemotePlayerManager.LocalWorldName);
                            if (localWorld != null && Root.SelectedWorld != localWorld)
                            {
                                Root.SelectWorld(localWorld);
                                GD.Print($"[Network] \U0001f504 Switched back to local world '{RemotePlayerManager.LocalWorldName}'");
                            }
                        }

                        RemotePlayerManager.ClearTweens();
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
            }

            Root.Initialize();

            if (!GamepakMode)
                AfterGamepakStart();

            _laserVisual.Initialize(_host);
            _worldCanvas = new WorldCanvasSystem(_host);

            if (XrAvailable != true && RendererService?.LockMouse == true && _options.LockMouseOnStart)
                SetMouseCaptured(true);

            // ── Start V12 worker thread ──
            _cts = new CancellationTokenSource();
            _v12Thread = new Thread(() => V12WorkerLoop(_cts.Token))
            {
                Name = "V12Worker",
                IsBackground = true
            };
            _v12Thread.Start();
        }

        /// <summary>
        /// Build a fully-wired runtime under <paramref name="host"/>. The host
        /// node must be inside the active scene tree (its viewport is used for
        /// XR and audio).
        /// </summary>
        public static V12Runtime Create(Node3D host, V12RuntimeOptions? options = null)
            => new V12Runtime(host, options ?? new V12RuntimeOptions());

        /// <summary>
        /// Forward Godot input events here from the host node's <c>_Input</c>.
        /// Mouse-motion is accumulated for the desktop look system.
        /// </summary>
        public void HandleInputEvent(global::Godot.InputEvent @event)
        {
            if (@event is global::Godot.InputEventMouseMotion motion)
                InputHandler?.AccumulateMouseLook(motion.Relative);
        }

        private void SetMouseCaptured(bool captured)
        {
            Input.MouseMode = captured ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            if (InputHandler != null) InputHandler.MouseCaptured = captured;
        }

        private void TryEnableXr(InputService inputService)
        {
            XR = new XRTrackingService(_host, inputService, (global::V12TwoDog.Renderer)RendererService);
            if (XR.IsAvailable)
            {
                Root.Registry.Register("XRTrackingService", XR);
                GD.Print("[V12Runtime] XR support active.");
            }
            else
            {
                GD.Print("[V12Runtime] XR not available yet — will keep retrying while running.");
            }
        }

        private static string? ParseGamepakArg()
        {
            foreach (var arg in OS.GetCmdlineArgs())
            {
                if (arg.StartsWith("--gamepak="))
                    return arg.Substring("--gamepak=".Length);
            }
            return null;
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
                    NetworkHandler?.ProcessPendingNetworkMessages();
                    Root.Update(dt);

                    var frame = Root.CaptureFrame();
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

        /// <summary>
        /// Main-thread pump: call from the host node's <c>_PhysicsProcess</c>.
        /// Updates XR/input, flushes deferred Godot calls, reads physics back,
        /// tweens remote players, and applies the latest worker snapshot.
        /// </summary>
        public void ProcessPhysics(float delta)
        {
            if (_disposed) return;

            XR?.Update();

            // ── Sync mouse capture with renderer option ──
            if (XrAvailable != true && RendererService?.LockMouse == true && !(InputHandler?.MouseCaptured ?? false))
                SetMouseCaptured(true);
            else if (RendererService?.LockMouse == false && (InputHandler?.MouseCaptured ?? false))
                SetMouseCaptured(false);

            // ── Input ──
            var inputService = Root.Registry.Get<V12.Core.Input.InputService>();
            if (_options.EnableInput && inputService != null && InputHandler?.ProcessFrame(inputService, XrAvailable, Root) == true)
            {
                if (XrAvailable != true)
                {
                    var newCaptured = !InputHandler.MouseCaptured;
                    SetMouseCaptured(newCaptured);
                    if (RendererService != null) RendererService.LockMouse = newCaptured;
                }
            }
            if (InputHandler != null) _debugMode = InputHandler.DebugMode;

            // ── Flush deferred Godot API calls from the V12 worker thread ──
            GodotMainThread.FlushPending();

            // ── Read physics results back after Godot's physics tick ──
            _physics?.ReadbackAll();

            // ── Tween remote player positions BEFORE capturing the frame ──
            RemotePlayerManager?.UpdateTweens(delta);

            // ── Consume latest frame snapshot on main thread ──
            while (_frameQueue.TryDequeue(out var frame))
                _latestFrame = frame;

            if (_latestFrame != null)
            {
                RendererService!.ApplySnapshot(_latestFrame);
                AudioPlayer.ApplySnapshot(_latestFrame);
            }

            _debug?.Update(delta);

            // ── Network timers (sync, heartbeat, status logging) ──
            NetworkHandler?.Update(delta, _debugMode);

            // ── Laser visual ──
            // Pass the live right-hand source so the laser is drawn from the hand's
            // actual tracked position (zero-latency) instead of a stale snapshot ray.
            if (_laserVisual != null && _pickup != null)
                _laserVisual.Update(_pickup, XR?.RightHandSource);

            // ── World/screen-space UI (CanvasComponent) ──
            _worldCanvas?.Update(Root);
        }

        /// <summary>
        /// Initialize physics, locomotion, script, and pickup systems.
        /// Called after a game pak has set up its world.
        /// </summary>
        private void AfterGamepakStart()
        {
            var rootWidget = Root.Registry.Get<IWidget>("RootWidget");
            if (rootWidget?.NativeControl is Node node)
            {
                GD.Print($"[V12Runtime] Adding gamepak root widget to scene tree.");
                _host.AddChild(node);
            }

            Root.Registry.Get<PhysicsLocomotionSystem>()?.Initialize(Root);
            Root.Registry.Get<LocomotionSystem>()?.Initialize(Root);
            Root.Registry.Get<ScriptSystem>()?.Initialize();

            _pickup = Root.Registry.Get<PickupSystem>();
            _pickup?.Initialize(Root);

            if (RemotePlayerManager != null)
                RemotePlayerManager.LocalWorldName = Root.SelectedWorld?.WorldName ?? "";
        }

        /// <summary>Stop the worker thread and release XR resources.</summary>
        public void Shutdown()
        {
            if (_disposed) return;
            _disposed = true;

            XR?.Cleanup();
            _worldCanvas?.Dispose();
            _worldCanvas = null;
            _cts?.Cancel();
            _v12Thread?.Join(1000);

            try { Root.Shutdown(); } catch { }
        }

        public void Dispose() => Shutdown();
    }

    /// <summary>
    /// Forwards Console output to Godot's GD.Print so Serilog / Console.WriteLine
    /// messages appear in the Godot console instead of vanishing.
    /// </summary>
    public class GodotConsoleWriter : System.IO.TextWriter
    {
        private readonly StringBuilder _buffer = new StringBuilder();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(string? value)
        {
            _buffer.Append(value);
        }

        public override void WriteLine(string? value)
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
}
