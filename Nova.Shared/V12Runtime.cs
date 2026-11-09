using Godot;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using V12;
using V12.Core;
using V12.Core.Diagnostics;
using V12.Core.GamePak;
using V12.Core.Input;
using V12.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.NetworkCable;
using V12.Core.Networking;
using V12.Core.Rendering;
using V12.Core.Systems;
using V12.Pak;
using V12.Rendering;
using V12.UI;
using V12TwoDog.Godot.Rendering;
using V12TwoDog.Godot.UI;
using IRenderer = V12.Core.Interfaces.IRenderer;

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

        /// <summary>Create the DebugGameService, which draws a Label3D
        /// world/element dump at a "debug" slot in the scene. Games leave it
        /// on; the studio editor turns it off so no stray labels float in
        /// the 3D viewport.</summary>
        public bool EnableDebugOverlay { get; set; } = true;

        /// <summary>Port used when connecting (client) or listening (server).</summary>
        public int NetworkPort { get; set; } = 7777;

        /// <summary>Host to connect to. When null the runtime listens as a server.</summary>
        public string? ConnectHost { get; set; }

        /// <summary>Directory scanned for gamepak DLLs. "gamepaks" matches the old host behaviour.</summary>
        public string GamepakDirectory { get; set; } = "gamepaks";

        /// <summary>
        /// Optional <c>.v12pak</c> archive to load at startup, mirroring the
        /// MonoGame/Rockstar sibling-pak behaviour. The generic Godot host sets
        /// this to the executable's sibling pak.
        /// </summary>
        public string? PakPath { get; set; }

        /// <summary>
        /// Assemblies to scan for gamepaks via direct project reference (no DLL
        /// scanning) — e.g. the Sample Game so it runs when pressing Play in the
        /// editor or on a headset. Paks found here are loaded, initialized, and
        /// started immediately, bypassing the launcher/force/empty-world logic.
        /// </summary>
        public IReadOnlyList<System.Reflection.Assembly>? GamepakAssemblies { get; set; }

        /// <summary>Name of a gamepak to launch immediately (equivalent of --gamepak=&lt;name&gt;).</summary>
        public string? ForceGamepak { get; set; }

        /// <summary>
        /// Game pak instances registered explicitly by an embedding host
        /// (e.g. <c>V12Host.RegisterGamePak</c>). When present they are started
        /// instead of any pak discovered from <see cref="GamepakAssemblies"/>.
        /// </summary>
        public IReadOnlyList<IV12Gamepack>? Gamepaks { get; set; }

        /// <summary>When paks exist but none is forced, show the launcher (else pick none).</summary>
        public bool ShowLauncher { get; set; } = true;

        /// <summary>Create an empty world when no gamepak is running.</summary>
        public bool CreateEmptyWorldIfNoPak { get; set; } = true;

        /// <summary>Capture the mouse at startup when not in XR.</summary>
        public bool LockMouseOnStart { get; set; } = true;

        /// <summary>When true, the legacy Godot Control-based WorldCanvasSystem is created.
        /// Set to false to disable Godot UI and use only Paper/Quill (engine-level default).</summary>
        public bool EnableGodotUI { get; set; } = true;
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
        public XRTrackingService? XR { get; private set; }
        public bool XrAvailable => XR?.IsAvailable == true;
        public InputHandler? InputHandler { get; private set; }
        public NetworkHandler? NetworkHandler { get; private set; }
        public RemotePlayerManager? RemotePlayerManager { get; private set; }
        public bool GamepakMode { get; private set; }
        public FrameSnapshot? LatestFrame => _latestFrame;
        public long PendingFrameCount => Interlocked.Read(ref _pendingFrameCount);
        public long LastFrameCaptureTicks => Interlocked.Read(ref _lastFrameCaptureTicks);
        public long LastFrameApplyTicks => Interlocked.Read(ref _lastFrameApplyTicks);
        public long LastFrameAgeTicks => Interlocked.Read(ref _lastFrameAgeTicks);

        private GodotPhysicsBackend? _physics;
        private DebugGameService? _debug;
        private LaserVisual? _laserVisual;
        private WorldCanvasSystem? _worldCanvas;
        private V12.Core.UI.Paper.ProwlPaperRenderer? _paperRenderer;
        private V12TwoDog.Paper.GodotViewportSurfaceProvider? _viewportSurfaces;
        private CanvasLayer? _paperLayer;
        private Control? _paperRootControl;
        private V12TwoDog.Paper.GodotPaperCanvasRenderer? _godotPaperRenderer;

        private GamepakLauncher? _launcher;
        private PickupSystem? _pickup;

        // ── Threading ──
        private readonly ConcurrentQueue<(FrameSnapshot Frame, long Timestamp)> _frameQueue = new();
        private long _pendingFrameCount;
        private long _lastFrameCaptureTicks;
        private long _lastFrameApplyTicks;
        private long _lastFrameAgeTicks;
        private FrameSnapshot? _latestFrame;
        private V12PakLoadResult? _loadedPakArchive;
        private Thread? _v12Thread;
        private CancellationTokenSource? _cts;
        private bool _debugMode;
        private bool _disposed;

        // ── Paper UI thread (Quill/Paper/Origami layout off the physics thread) ──
        private Thread? _paperThread;
        private CancellationTokenSource? _paperCts;
        private readonly ConcurrentQueue<global::Godot.InputEvent> _paperInputQueue = new();
        private Vector2 _pendingPaperViewportSize = new Vector2(1280, 720);
        private readonly AutoResetEvent _paperWake = new(false);

        private V12Runtime(Node3D host, V12RuntimeOptions options)
        {
            using var _zone = V12Profiler.BeginZone("V12Runtime.ctor", V12Profiler.Colors.Engine);
            V12Profiler.SetThreadName("Godot Main");
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _options = options ?? new V12RuntimeOptions();

            V12.Core.Networking.BsonConfig.Initialize();
            Console.SetOut(new GodotConsoleWriter());

            // Enable Serilog so GameRoot.Log / GamepackLoader log messages actually appear.
            // Skip when the embedding app already configured sinks (e.g. a file sink in
            // the game host) — reconfiguring would drop them.
            if (!V12.Core.GameRoot.LoggingConfigured)
                GameRoot.ConfigureLogging();

            Root = new GameRoot();
            var renderer = new global::V12TwoDog.Renderer(host.GetTree());
            RendererService = renderer;
            Root.Registry.Register("IRenderer", RendererService);

            // WorldCanvasSystem handles both world-space (3D quads) and screen-space (CanvasLayer Controls).
            // When Paper is the engine-level default, WorldCanvasSystem still runs for world-space
            // canvases but skips screen-space (handled by Paper mesh bridge). Set EnableGodotUI=false
            // to fully disable even world-space (e.g., for headless).
            if (_options.EnableGodotUI)
            {
                _worldCanvas = new WorldCanvasSystem(host, (global::V12TwoDog.Renderer)RendererService);
                Root.Registry.Register("WorldCanvasSystem", _worldCanvas);
                if (V12.Core.UI.Paper.ProwlPaperRenderer.UseProwlPaper)
                    GD.Print("[V12Runtime] WorldCanvasSystem active (world-space only, screen-space via Paper)");
            }
            else
            {
                GD.Print("[V12Runtime] Godot WorldCanvasSystem disabled by options.EnableGodotUI=false");
            }

            // Engine-level Paper/Origami/Quill UI (default, Godot renders meshes).
            // Mirrors Paper/Samples/OpenTK/PaperRenderer.cs but via Godot ArrayMesh.
            // When UseProwlPaper is false, WorldCanvasSystem (Godot Controls) remains sole UI.
            if (V12.Core.UI.Paper.ProwlPaperRenderer.UseProwlPaper)
            {
                try
                {
                    var vpSize = host.GetViewport().GetVisibleRect().Size;
                    if (vpSize.X < 64) vpSize = new Vector2(1280, 720);
                    _paperLayer = new CanvasLayer { Layer = 100, Name = "PaperLayer" };
                    _paperRootControl = new Control { Name = "PaperRoot" };
                    _paperRootControl.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                    _paperLayer.AddChild(_paperRootControl);
                    host.CallDeferred(Node.MethodName.AddChild, _paperLayer);
                    _godotPaperRenderer = new V12TwoDog.Paper.GodotPaperCanvasRenderer(_paperLayer, _paperRootControl);
                    _paperRenderer = new V12.Core.UI.Paper.ProwlPaperRenderer(_godotPaperRenderer, (int)vpSize.X, (int)vpSize.Y, 1f);
                    _viewportSurfaces = new V12TwoDog.Paper.GodotViewportSurfaceProvider(_host, renderer);
                    _paperRenderer.ViewportTextureProvider = _viewportSurfaces.GetTexture;
                    _paperRenderer.ViewportRectSink = _viewportSurfaces.SetRect;
                    _paperRenderer.ViewportReleaseSink = _viewportSurfaces.Release;
                    Root.Registry.RegisterOrReplace("IUIRenderer", _paperRenderer);
                    GD.Print($"[V12Runtime] Paper/Origami/Quill IUIRenderer active (mesh bridge, {vpSize.X}x{vpSize.Y}) - Godot Control fallback kept");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[V12Runtime] Paper IUIRenderer init failed, falling back to Godot Controls: {ex.Message}");
                    V12.Core.UI.Paper.ProwlPaperRenderer.UseProwlPaper = false;
                }
            }

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

            if (_options.EnableDebugOverlay)
            {
                _debug = new DebugGameService();
                _debug.Initialize(Root);
                Root.Registry.Register("DebugGameService", _debug);
            }

            // ── XR: attempt now, keep retrying while the runtime runs ──
            if (_options.EnableXR)
            {
                TryEnableXr(inputService);
            }

            _physics = new GodotPhysicsBackend(host);
            Root.Registry.Register(nameof(V12.Core.Interfaces.Physics.IPhysicsBackend), _physics);



            // Godot hosts live in a plugin AssemblyLoadContext. Pak DLLs loaded
            // with Assembly.LoadFrom would land in the default context and keep
            // a second V12 identity, so the pak's IV12Gamepack would not match.
            // Point both pak loaders at this runtime's own context instead.
            var rendererLoadContext = AssemblyLoadContext.GetLoadContext(typeof(V12Runtime).Assembly);
            if (rendererLoadContext != null)
            {
                GamepackLoader.AssemblyLoader = path => rendererLoadContext.LoadFromAssemblyPath(path);
                GamepackLoader.AssemblyFinder = name => rendererLoadContext.Assemblies
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
                V12PakLoader.AssemblyLoader = path => rendererLoadContext.LoadFromAssemblyPath(path);
                V12PakLoader.AssemblyFinder = name => rendererLoadContext.Assemblies
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            }

            // ── Sibling .v12pak (the archive layout vcsdk ships) ──
            bool pakArchiveLoaded = false;
            if (!string.IsNullOrWhiteSpace(_options.PakPath) && File.Exists(_options.PakPath))
            {
                GD.Print($"[V12Runtime] Launching sibling pak '{Path.GetFileName(_options.PakPath)}'...");
                try
                {
                    _loadedPakArchive = Root.LoadPak(_options.PakPath, new V12PakOptions
                    {
                        LoadDlls = true,
                        LoadContractScripts = true,
                        LoadCSharpScripts = true,
                    });
                    pakArchiveLoaded = true;
                    if (_loadedPakArchive.LoadedWorlds.Count > 0)
                        Root.SelectWorld(_loadedPakArchive.LoadedWorlds[0]);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[V12Runtime] Failed to load '{_options.PakPath}': {ex.Message}");
                }
            }

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

            // ── Explicitly registered game paks (host handed them to us) ──
            if (_options.Gamepaks is { Count: > 0 })
            {
                foreach (var pak in _options.Gamepaks)
                {
                    Root.Gamepaks.Add(pak);
                    pak.Initialize();
                    pak.OnStart();
                    GD.Print($"[V12Runtime] Explicit gamepak '{pak.Name}' started.");
                }
                assemblyPakStarted = true;
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
            else if (!pakArchiveLoaded && Root.Gamepaks.Gamepaks.Count > 0 && _options.ShowLauncher)
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

            if (!GamepakMode && !pakArchiveLoaded)
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
            NetworkHandler = new NovaNetworkHandler(Root, worldSyncHandler, RemotePlayerManager);
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

            // The archive's gamepaks were added by LoadPak before Initialize;
            // start them now that platform services are ready.
            if (pakArchiveLoaded && !GamepakMode)
            {
                Root.Gamepaks.InitializeAll();
                Root.Gamepaks.StartAll();
                GamepakMode = true;
                AfterGamepakStart();
            }

            // Server mode: start listening immediately so clients can join the
            // headless server. Client mode stays idle until the game UI (or an
            // embedding host) triggers NetworkClient.ConnectAsync.
            if (Root.Registry.Get<NetworkHost>("NetworkHost") != null)
                Root.StartNetworkingAsync();

            if (!GamepakMode)
                AfterGamepakStart();

            _laserVisual.Initialize(_host);

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

            // ── Start Paper UI thread (layout + Quill tessellation) ──
            // Paper/Origami/Quill no longer runs inside ProcessPhysics.
            // The UI thread owns the Paper instance exclusively and produces
            // PaperFrames at ~60 Hz; the actual Godot mesh upload is deferred
            // to the main thread via GodotMainThread (see GodotPaperCanvasRenderer).
            if (V12.Core.UI.Paper.ProwlPaperRenderer.UseProwlPaper && _paperRenderer != null && _godotPaperRenderer != null)
            {
                _paperCts = new CancellationTokenSource();
                var paperToken = _paperCts.Token;
                _paperThread = new Thread(() => PaperUIThreadLoop(paperToken))
                {
                    Name = "V12PaperUI",
                    IsBackground = true
                };
                _paperThread.Start();
                // Seed viewport size so the first UI frame has correct dimensions
                try
                {
                    var vp = _host.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1280, 720);
                    if (vp.X >= 64) _pendingPaperViewportSize = vp;
                }
                catch { }
            }
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
            using var _zone = V12Profiler.BeginZone("V12Runtime.HandleInputEvent", V12Profiler.Colors.Runtime);
            if (_disposed) return;
            if (@event is global::Godot.InputEventMouseMotion motion)
                InputHandler?.AccumulateMouseLook(motion.Relative);

            // Paper input is now consumed on the dedicated Paper UI thread so
            // HandleInputEvent never contends on the Paper instance. Enqueue and
            // wake the UI thread — it drains the queue before each BeginFrame.
            if (_paperRenderer != null && V12.Core.UI.Paper.ProwlPaperRenderer.UseProwlPaper && @event != null)
            {
                _paperInputQueue.Enqueue(@event);
                try { _paperWake.Set(); } catch (ObjectDisposedException) { }
            }
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
            V12Profiler.SetThreadName("V12 Worker");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                float dt = (float)sw.Elapsed.TotalSeconds;
                sw.Restart();
                dt = Math.Clamp(dt, 0.001f, 0.05f);

                using var _workerFrame = V12Profiler.BeginZone("V12WorkerLoop.Tick", V12Profiler.Colors.World);
                try
                {
                    using (V12Profiler.BeginZone("ProcessPendingNetworkMessages", V12Profiler.Colors.Runtime))
                        NetworkHandler?.ProcessPendingNetworkMessages();
                    using (V12Profiler.BeginZone("Root.Update", V12Profiler.Colors.World))
                        Root.Update(dt);

                    // Only capture a fresh frame when something render-affecting
                    // changed since the last capture (transform, mesh, material,
                    // element add/remove). Static/idle scenes skip the full world
                    // walk and snapshot allocation; the main thread keeps
                    // re-applying the last snapshot (see Renderer.ApplySnapshot,
                    // which skips nodes whose snapshot is unchanged).
                    if (Root.ConsumeRenderDirty())
                    {
                        var capture = System.Diagnostics.Stopwatch.StartNew();
                        FrameSnapshot frame;
                        using (V12Profiler.BeginZone("CaptureFrame", V12Profiler.Colors.Capture))
                            frame = Root.CaptureFrame();
                        Interlocked.Exchange(ref _lastFrameCaptureTicks, capture.ElapsedTicks);
                        _frameQueue.Enqueue((frame, System.Diagnostics.Stopwatch.GetTimestamp()));
                        Interlocked.Increment(ref _pendingFrameCount);
                    }
                    V12Profiler.Plot("V12 PendingFrames", _frameQueue.Count);
                    V12Profiler.Plot("V12 Worker dt (ms)", dt * 1000.0);
                }
                catch (Exception ex)
                {
                    GD.Print($"[V12Worker] {ex.GetType().Name}: {ex.Message}");
                }

                // int sleepMs = Math.Max(1, (int)((1f / 60f - dt) * 1000));
                // Thread.Sleep(sleepMs);
            }
        }

        private void PaperUIThreadLoop(CancellationToken ct)
        {
            V12Profiler.SetThreadName("V12 Paper UI");
            while (!ct.IsCancellationRequested)
            {
                using var _frameZone = V12Profiler.BeginZone("PaperUI.ThreadTick", V12Profiler.Colors.Runtime);
                try
                {
                    // Drain Godot input events queued from HandleInputEvent (main thread).
                    int drained = 0;
                    while (_paperInputQueue.TryDequeue(out var evt))
                    {
                        drained++;
                        try
                        {
                            if (_paperRenderer != null && V12.Core.UI.Paper.ProwlPaperRenderer.UseProwlPaper)
                                V12TwoDog.Paper.PaperInput.Feed(_paperRenderer.PaperInstance, evt);
                        }
                        catch (Exception ex)
                        {
                            GD.PrintErr($"[PaperUI] Feed failed: {ex.Message}");
                        }
                    }
                    if (drained > 0)
                        V12Profiler.Plot("Paper Input Queued", _paperInputQueue.Count);

                    if (_paperRenderer != null && _godotPaperRenderer != null
                        && V12.Core.UI.Paper.ProwlPaperRenderer.UseProwlPaper
                        && !_disposed)
                    {
                        // Viewport size is sampled on the main thread and published here.
                        Vector2 vp = _pendingPaperViewportSize;
                        if (vp.X < 64) vp = new Vector2(1280, 720);

                        // Resize is cheap; keep the Paper canvas in sync.
                        _paperRenderer.Resize((int)vp.X, (int)vp.Y, 1f);

                        V12.Core.Rendering.UIFrame frame;
                        using (V12Profiler.BeginZone("PaperUI.CaptureUI", V12Profiler.Colors.Runtime))
                            frame = Root.CaptureUI();

                        using (V12Profiler.BeginZone("PaperUI.ApplyUI", V12Profiler.Colors.Runtime))
                            _paperRenderer.ApplyUI(frame);

                        using (V12Profiler.BeginZone("PaperUI.step", V12Profiler.Colors.Runtime))
                            _paperRenderer.step(); // off-main: headless capture + deferred Godot upload via GodotMainThread
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[PaperUI] tick failed: {ex.Message}");
                }

                // ~60 Hz, but wake immediately on new input or viewport change.
                // try { _paperWake.WaitOne(30); } catch { }
                Thread.Sleep(16);
                if (ct.IsCancellationRequested) break;

            }
        }

        /// <summary>
        /// Main-thread pump: call from the host node's <c>_PhysicsProcess</c>.
        /// Updates XR/input, flushes deferred Godot calls, reads physics back,
        /// tweens remote players, and applies the latest worker snapshot.
        /// </summary>
        public void ProcessPhysics(float delta)
        {
            using var _zone = V12Profiler.BeginZone("V12Runtime.ProcessPhysics", V12Profiler.Colors.Engine);
            if (_disposed) return;

            using (V12Profiler.BeginZone("XR.Update", V12Profiler.Colors.Runtime))
                XR?.Update();

            // ── Sync mouse capture with renderer option ──
            if (XrAvailable != true && RendererService?.LockMouse == true && !(InputHandler?.MouseCaptured ?? false))
                SetMouseCaptured(true);
            else if (RendererService?.LockMouse == false && (InputHandler?.MouseCaptured ?? false))
                SetMouseCaptured(false);

            // ── Input ──
            using (V12Profiler.BeginZone("Input", V12Profiler.Colors.Runtime))
            {
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
            }

            // ── Flush deferred Godot API calls from the V12 worker thread ──
            using (V12Profiler.BeginZone("FlushPending", V12Profiler.Colors.Runtime))
                GodotMainThread.FlushPending();

            // ── Apply queued viewport surface creates/resizes/releases ──
            _viewportSurfaces?.Pump();

            // ── Advance character controllers (move_and_slide) on the main thread ──
            using (V12Profiler.BeginZone("Physics.StepCharacterControllers", V12Profiler.Colors.World))
                _physics?.StepCharacterControllers(delta);

            // ── Read physics results back after Godot's physics tick ──
            using (V12Profiler.BeginZone("Physics.ReadbackAll", V12Profiler.Colors.World))
                _physics?.ReadbackAll();

            // ── Tween remote player positions BEFORE capturing the frame ──
            using (V12Profiler.BeginZone("RemotePlayerManager.UpdateTweens", V12Profiler.Colors.Runtime))
                RemotePlayerManager?.UpdateTweens(delta);

            // ── Consume queued frames in order on the main thread ──
            long oldestAgeTicks = 0;
            bool receivedFrame = false;
            using (V12Profiler.BeginZone("ConsumeFrameQueue", V12Profiler.Colors.Capture))
            {
                while (_frameQueue.TryDequeue(out var queued))
                {
                    Interlocked.Decrement(ref _pendingFrameCount);
                    if (!receivedFrame)
                        oldestAgeTicks = System.Diagnostics.Stopwatch.GetTimestamp() - queued.Timestamp;
                    receivedFrame = true;
                    _latestFrame = queued.Frame;
                }
                if (receivedFrame)
                    Interlocked.Exchange(ref _lastFrameAgeTicks, oldestAgeTicks);
            }

            if (_latestFrame != null)
            {
                var apply = System.Diagnostics.Stopwatch.StartNew();
                using (V12Profiler.BeginZone("ApplySnapshot", V12Profiler.Colors.Renderer))
                {
                    RendererService!.ApplySnapshot(_latestFrame);
                }
                Interlocked.Exchange(ref _lastFrameApplyTicks, apply.ElapsedTicks);
            }

            using (V12Profiler.BeginZone("DebugGameService.Update", V12Profiler.Colors.Runtime))
                _debug?.Update(delta);

            // ── Network timers (sync, heartbeat, status logging) ──
            using (V12Profiler.BeginZone("NetworkHandler.Update", V12Profiler.Colors.Runtime))
                NetworkHandler?.Update(delta, _debugMode);

            // ── Laser visual ──
            // Pass the live right-hand source so the laser is drawn from the hand's
            // actual tracked position (zero-latency) instead of a stale snapshot ray.
            using (V12Profiler.BeginZone("LaserVisual.Update", V12Profiler.Colors.Renderer))
            {
                if (_laserVisual != null && _pickup != null)
                    _laserVisual.Update(_pickup, XR?.RightHandSource);
            }

            // ── World/screen-space UI (CanvasComponent) ──
            using (V12Profiler.BeginZone("WorldCanvasSystem.Update", V12Profiler.Colors.Runtime))
                _worldCanvas?.Update(Root);

            // ── Paper/Origami/Quill UI — now on its own thread ──
            // Sample viewport size on the main thread (Godot API is main-thread only)
            // and publish it for the Paper UI thread. The UI thread does
            // CaptureUI -> ApplyUI -> step off the physics tick; its Godot mesh
            // upload is deferred via GodotMainThread and flushed at the top of
            // the next ProcessPhysics (so physics never blocks on Quill tessellation).
            if (_paperRenderer != null && V12.Core.UI.Paper.ProwlPaperRenderer.UseProwlPaper)
            {
                using (V12Profiler.BeginZone("Paper.PublishViewport", V12Profiler.Colors.Runtime))
                {
                    try
                    {
                        var vp = _host.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1280, 720);
                        if (vp.X >= 64 && (vp.X != _pendingPaperViewportSize.X || vp.Y != _pendingPaperViewportSize.Y))
                        {
                            _pendingPaperViewportSize = vp;
                            try { _paperWake.Set(); } catch { }
                        }
                    }
                    catch { }
                }
            }


            V12Profiler.Plot("V12 PendingFrames (main)", _frameQueue.Count);
            V12Profiler.FrameMark();
        }

        /// <summary>
        /// Initialize physics, locomotion, script, and pickup systems.
        /// Called after a game pak has set up its world.
        /// </summary>
        private void AfterGamepakStart()
        {
            using var _zone = V12Profiler.BeginZone("V12Runtime.AfterGamepakStart", V12Profiler.Colors.Engine);
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


            // A pak that hosts a game-view panel (the studio editor) exposes a
            // RenderWorld viewport element. Register a SubViewport for it so the
            // Paper UI can draw the real 3D texture instead of a placeholder.
            EnsureGameViewHost();

            if (RemotePlayerManager != null)
                RemotePlayerManager.LocalWorldName = Root.SelectedWorld?.WorldName ?? "";
        }

        /// <summary>
        /// Pre-create the surface for the RenderWorld viewport so the editor host
        /// can attach it to its game-view tab before the first Paper frame.
        /// Other viewport elements are created lazily when the Paper renderer
        /// asks for their texture.
        /// </summary>
        private void EnsureGameViewHost()
        {
            using var _zone = V12Profiler.BeginZone("V12Runtime.EnsureGameViewHost", V12Profiler.Colors.Runtime);
            if (_viewportSurfaces == null) return;
            var worldElement = Root.FindElement(e => e.GetComponent<V12.Components.UI.ViewportComponent>() is { RenderWorld: true });
            if (worldElement == null) return;
            _viewportSurfaces.EnsureSurface(worldElement.Id);
            Root.MarkRenderDirty();
        }

        /// <summary>Create or return the Godot SubViewport for a V12 viewport element.</summary>
        public SubViewport? EnsureViewportSurface(long viewportId) => _viewportSurfaces?.EnsureSurface(viewportId);

        /// <summary>Release a viewport surface (its UI node or element disappeared).</summary>
        public void ReleaseViewportSurface(long viewportId) => _viewportSurfaces?.Release(viewportId);

        /// <summary>
        /// Register and start an additional game pak on this runtime. Safe to call
        /// after the runtime is running (unlike the options-based path). No-op when a
        /// pak with the same name is already loaded.
        /// </summary>
        public void RegisterGamePak(IV12Gamepack pak)
        {
            if (pak == null) return;

            foreach (var existing in Root.Gamepaks.Gamepaks)
            {
                if (string.Equals(existing.Name, pak.Name, StringComparison.OrdinalIgnoreCase))
                {
                    GD.Print($"[V12Runtime] Gamepak '{pak.Name}' already loaded — skipping.");
                    return;
                }
            }

            Root.Gamepaks.Add(pak);
            pak.Initialize();
            pak.OnStart();
            GamepakMode = true;
            AfterGamepakStart();
            GD.Print($"[V12Runtime] Registered gamepak '{pak.Name}' after start.");
        }

        /// <summary>
        /// Initialize and start game paks that were added to <see cref="Root"/>'s
        /// GamepackLoader after this runtime was created (e.g. by loading a pak).
        /// </summary>
        public void StartLoadedGamepaks(int firstIndex)
        {
            if (firstIndex < 0 || firstIndex >= Root.Gamepaks.Gamepaks.Count)
                return;

            // V12.Pak API renamed InitializeAndStartFrom -> InitializeAll/StartAll in newer SDK.
            // InitializeAll is idempotent, so calling it for the new range is safe.
            Root.Gamepaks.InitializeAll();
            Root.Gamepaks.StartAll();
            GamepakMode = true;
            AfterGamepakStart();
        }

        /// <summary>Stop the worker thread and release XR resources.</summary>
        public void Shutdown()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop Paper UI thread first — it owns the Paper instance and may be
            // waiting on _paperWake. Wake it so it can observe cancellation.
            try { _paperCts?.Cancel(); } catch { }
            try { _paperWake.Set(); } catch { }
            try { _paperThread?.Join(1000); } catch { }
            _paperThread = null;
            _paperCts?.Dispose(); _paperCts = null;
            try { _paperWake.Dispose(); } catch { }

            XR?.Cleanup();
            _worldCanvas?.Dispose();
            _worldCanvas = null;
            try { _godotPaperRenderer?.Dispose(); } catch {}
            if (_paperLayer != null && GodotObject.IsInstanceValid(_paperLayer)) _paperLayer.QueueFree();
            _paperLayer = null; _paperRootControl = null; _paperRenderer = null; _godotPaperRenderer = null;
            _cts?.Cancel();
            _v12Thread?.Join(1000);

            try { _loadedPakArchive?.Dispose(); } catch { }
            _loadedPakArchive = null;
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
