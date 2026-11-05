using Godot;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using V12;
using V12.Core;
using V12.Core.Diagnostics;
using V12.Pak;
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
        /// Path to a compiled <c>.v12pak</c> to launch as the game, mirroring the
        /// Pulsar.GL host: its first world is selected and its game paks are
        /// initialized/started. The pak is trusted fully (DLLs, Contract and C#
        /// scripts enabled). When set, directory scanning of
        /// <see cref="GamepakDirectory"/> is skipped; <see cref="Gamepaks"/>
        /// registered explicitly by the host still win.
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
    public sealed class V12Runtime : IDisposable, V12.Core.Rendering.IViewportRegistry, V12.Core.Input.IInputSource
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
        public long PendingFrameCount => Interlocked.Read(ref _pendingFrameCount);
        public long LastFrameCaptureTicks => Interlocked.Read(ref _lastFrameCaptureTicks);
        public long LastFrameApplyTicks => Interlocked.Read(ref _lastFrameApplyTicks);
        public long LastFrameAgeTicks => Interlocked.Read(ref _lastFrameAgeTicks);

        private GodotPhysicsBackend? _physics;
        private DebugGameService? _debug;
        private LaserVisual? _laserVisual;
        private WorldCanvasSystem? _worldCanvas;
        private V12.Core.UI.Paper.ProwlPaperRenderer? _paperRenderer;
        private CanvasLayer? _paperLayer;
        private Control? _paperRootControl;
        private V12TwoDog.Paper.GodotPaperCanvasRenderer? _godotPaperRenderer;
        private readonly Dictionary<long, V12TwoDog.Paper.GodotWorldPaperCanvasRenderer> _paperWorldRenderers = new();
        private readonly Dictionary<long, SubViewport> _paperViewports = new();
        private IReadOnlyList<ViewportInfo> _novaViewports = Array.Empty<ViewportInfo>();
        private InputSnapshot _novaInput;
        private int _novaWheel;
        private bool _uiDirtyInput;
        private int _paperW = -1, _paperH = -1;

        IReadOnlyList<ViewportInfo> V12.Core.Rendering.IViewportRegistry.Viewports => _novaViewports;
        InputSnapshot V12.Core.Input.IInputSource.GetSnapshot() => _novaInput;
        private DefaultEnvironment? _defaultEnv;
        private GamepakLauncher? _launcher;
        private PickupSystem? _pickup;
        private V12PakLoadResult? _pakResult;

        // ── Threading ──
        private readonly ConcurrentQueue<(FrameSnapshot Frame, long Timestamp)> _frameQueue = new();
        private long _pendingFrameCount;
        private long _lastFrameCaptureTicks;
        private long _lastFrameApplyTicks;
        private long _lastFrameAgeTicks;
        private FrameSnapshot? _latestFrame;
        private Thread? _v12Thread;
        private CancellationTokenSource? _cts;
        private bool _debugMode;
        private bool _disposed;

        private V12Runtime(Node3D host, V12RuntimeOptions options)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _options = options ?? new V12RuntimeOptions();

#if TRACY
            V12Profiler.SetThreadName("Godot Main");
            V12Profiler.AppInfo("Nova (Godot 4.7) / V12");
#endif

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
            AudioPlayer = new GodotAudioPlayer();
            AudioPlayer.Initialize(Root);
            Root.Registry.Register("IAudioPlayer", AudioPlayer);
            host.AddChild(AudioPlayer);
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
                    _godotPaperRenderer.ViewportTextureResolver = EnsurePaperViewportTexture;
                    if (_worldCanvas != null)
                    {
                        _paperRenderer.WorldCanvasRendererFactory = (id, width, height) =>
                        {
                            var renderer = new V12TwoDog.Paper.GodotWorldPaperCanvasRenderer(
                                _host, id, width, height,
                                disposedId => _paperWorldRenderers.Remove(disposedId));
                            _paperWorldRenderers[id] = renderer;
                            return renderer;
                        };
                        _paperRenderer.WorldCanvasSizeSelector = node =>
                        {
                            int width = node.Width > 0f ? (int)MathF.Round(node.Width) : 0;
                            int height = node.Height > 0f ? (int)MathF.Round(node.Height) : 0;
                            if (width > 0 && height > 0) return (width, height);

                            var element = Root.FindElement(e => e.Id == node.Id);
                            var scale = element?.GetComponent<V12.Components.ScaleComponent>();
                            float worldWidth = scale != null && scale.ScaleX > 0.01f ? scale.ScaleX : 2f;
                            float worldHeight = scale != null && scale.ScaleY > 0.01f ? scale.ScaleY : 1.2f;
                            width = Math.Clamp((int)MathF.Round(worldWidth * 256f), 64, 4096);
                            height = Math.Clamp((int)MathF.Round(worldHeight * 256f), 64, 4096);
                            return (width, height);
                        };
                        _worldCanvas.PaperCanvasTexture = id =>
                            _paperWorldRenderers.TryGetValue(id, out var worldRenderer) &&
                            GodotObject.IsInstanceValid(worldRenderer.Viewport)
                                ? worldRenderer.Texture
                                : null;
                    }
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
            Root.Registry.Register("EditorFilePicker", new Godot.UI.GodotFilePicker());

            var inputService = Root.Registry.Get<InputService>();
            if (inputService == null)
            {
                inputService = new InputService();
                Root.Registry.Register("InputService", inputService);
            }

            // Renderer-owned viewport contracts + the engine's viewport interaction
            // service. Game paks (including the studio) resolve these from the
            // registry instead of each host re-implementing input/camera wiring.
            Root.Registry.Register("IViewportRegistry", this);
            Root.Registry.Register("IInputSource", this);
            var viewportInteraction = new V12.Core.UI.ViewportInteractionService(Root, this, this);
            Root.Registry.Register("IViewportInteraction", viewportInteraction);

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

            // Baked-in scene environment (sky/ambient + default sun until the
            // game provides its own lights). Worlds load below; AfterGamepakStart
            // applies the first real evaluation.
            _defaultEnv = new DefaultEnvironment(host);

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

            // ── Discover game paks ──
            // A .v12pak handed to the runtime is the game (Pulsar.GL semantics),
            // so skip the legacy gamepaks/ directory scan in that case.
            if (string.IsNullOrEmpty(_options.PakPath))
                Root.LoadGamepacks(_options.GamepakDirectory);

            // ── Load a compiled .v12pak (e.g. the executable's sibling pak) ──
            // Worlds are loaded now and the pak's game paks are initialized and
            // started after Root.Initialize(), matching Pulsar.GL's host.
            bool pakLoaded = false;
            int pakFirstIndex = -1;
            if (!string.IsNullOrEmpty(_options.PakPath) && _options.Gamepaks is not { Count: > 0 })
            {
                pakFirstIndex = Root.Gamepaks.Gamepaks.Count;
                GD.Print($"[V12Runtime] Launching '{Path.GetFileName(_options.PakPath)}'...");

                try
                {
                    _pakResult = Root.LoadPak(_options.PakPath, new V12PakOptions
                    {
                        LoadDlls = true,
                        LoadContractScripts = true,
                        LoadCSharpScripts = true,
                    });
                    pakLoaded = true;

                    // Prefer the pak's first world over any host fallback.
                    if (_pakResult.LoadedWorlds.Count > 0)
                        Root.SelectWorld(_pakResult.LoadedWorlds[0]);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[V12Runtime] Failed to load '{_options.PakPath}': {ex.Message}");
                }
            }

            // ── Direct-referenced gamepaks (ProjectReference, e.g. the Sample
            // Game so it runs when pressing Play in the editor / on a headset):
            // load + initialize + start them now, exactly like the forced path.
            bool assemblyPakStarted = false;
            if (!pakLoaded && _options.GamepakAssemblies != null)
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

            // ── Pick a gamepak: loaded pak, direct assembly, forced, launcher, or none ──
            string? gamepakName = _options.ForceGamepak ?? ParseGamepakArg();
            IV12Gamepack? selectedPak = null;

            if (pakLoaded)
            {
                GD.Print($"[V12Runtime] {Root.Gamepaks.Gamepaks.Count - pakFirstIndex} game pak(s) loaded from '{Path.GetFileName(_options.PakPath!)}'.");
                GamepakMode = true;
            }
            else if (assemblyPakStarted)
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

            // Server mode: start listening immediately so clients can join the
            // headless server. Client mode stays idle until the game UI (or an
            // embedding host) triggers NetworkClient.ConnectAsync.
            if (Root.Registry.Get<NetworkHost>("NetworkHost") != null)
                Root.StartNetworkingAsync();

            // Pulsar.GL starts a loaded pak's game paks after the root is
            // initialized, once platform services are fully registered.
            if (pakLoaded)
            {
                Root.Gamepaks.InitializeAndStartFrom(pakFirstIndex);
                AfterGamepakStart();
            }

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
            _uiDirtyInput = true;
            if (@event is global::Godot.InputEventMouseMotion motion)
                InputHandler?.AccumulateMouseLook(motion.Relative);
            else if (@event is global::Godot.InputEventMouseButton button && button.Pressed)
            {
                if (button.ButtonIndex == MouseButton.WheelUp) _novaWheel++;
                else if (button.ButtonIndex == MouseButton.WheelDown) _novaWheel--;
            }

            // Feed the engine-level Paper canvas (screen-space CanvasComponent HUD).
            if (_paperRenderer != null && V12.Core.UI.Paper.ProwlPaperRenderer.UseProwlPaper)
                V12TwoDog.Paper.PaperInput.Feed(_paperRenderer.PaperInstance, @event);
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
#if TRACY
            V12Profiler.SetThreadName("V12 Worker");
#endif
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
#if TRACY
                using var zone = V12Profiler.BeginZone("V12.WorkerTick", V12Profiler.Colors.Runtime);
#endif
                float dt = (float)sw.Elapsed.TotalSeconds;
                sw.Restart();
                dt = Math.Clamp(dt, 0.001f, 0.05f);
#if TRACY
                V12Profiler.Plot("V12/WorkerTickMs", dt * 1000.0);
#endif

                try
                {
                    NetworkHandler?.ProcessPendingNetworkMessages();
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
                        var frame = Root.CaptureFrame();
                        Interlocked.Exchange(ref _lastFrameCaptureTicks, capture.ElapsedTicks);
                        _frameQueue.Enqueue((frame, System.Diagnostics.Stopwatch.GetTimestamp()));
                        Interlocked.Increment(ref _pendingFrameCount);
                    }
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

#if TRACY
            using var zone = V12Profiler.BeginZone("V12.ProcessPhysics", V12Profiler.Colors.Runtime);
#endif

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

            // ── Advance character controllers (move_and_slide) on the main thread ──
            _physics?.StepCharacterControllers(delta);

            // ── Read physics results back after Godot's physics tick ──
            _physics?.ReadbackAll();

            // ── Tween remote player positions BEFORE capturing the frame ──
            RemotePlayerManager?.UpdateTweens(delta);

            // ── Consume queued frames in order on the main thread ──
            long oldestAgeTicks = 0;
            bool receivedFrame = false;
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

            if (_latestFrame != null)
            {
                var apply = System.Diagnostics.Stopwatch.StartNew();
                RendererService!.ApplySnapshot(_latestFrame);
                AudioPlayer.ApplySnapshot(_latestFrame);
                Interlocked.Exchange(ref _lastFrameApplyTicks, apply.ElapsedTicks);
#if TRACY
                V12Profiler.Plot("V12/FrameApplyMs", apply.Elapsed.TotalMilliseconds);
#endif
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

            // ── Paper/Origami/Quill UI (engine-level default, Godot mesh bridge) ──
            if (_paperRenderer != null && V12.Core.UI.Paper.ProwlPaperRenderer.UseProwlPaper)
            {
                try
                {
                    var vp = _host.GetViewport().GetVisibleRect().Size;
                    int w = (int)vp.X, h = (int)vp.Y;
                    bool resized = w != _paperW || h != _paperH;
                    if (resized)
                    {
                        _paperW = w;
                        _paperH = h;
                        _paperRenderer.Resize(w, h, 1f);
                    }

                    bool uiDirty = Root.ConsumeUIDirty() || _uiDirtyInput || resized;
                    _uiDirtyInput = false;
                    if (uiDirty)
                    {
                        var frame = Root.CaptureUI();
                        _paperRenderer.ApplyUI(frame);
                        _paperRenderer.step();
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[V12Runtime] Paper tick failed: {ex.Message}");
                }
            }

            CaptureNovaViewports();
            CaptureNovaInput();

            // �"?�"? Default environment (sky/ambient/sun until the game defines them) �"?�"?
            _defaultEnv?.Update(Root);

            V12Profiler.FrameMark();
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

            // Worlds exist now — evaluate the default environment once up front
            // (the ~2 Hz poll in ProcessPhysics keeps it correct after that).
            _defaultEnv?.Ensure(Root);

            if (RemotePlayerManager != null)
                RemotePlayerManager.LocalWorldName = Root.SelectedWorld?.WorldName ?? "";
        }

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

            Root.Gamepaks.InitializeAndStartFrom(firstIndex);
            GamepakMode = true;
            AfterGamepakStart();
        }

        private void CaptureNovaViewports()
        {
            if (_paperRenderer == null)
            {
                _novaViewports = Array.Empty<ViewportInfo>();
                return;
            }

            var list = new List<ViewportInfo>();
            foreach (var pair in _paperRenderer.ViewportRects)
            {
                var rect = pair.Value;
                float width = Math.Max(0f, rect.Max.X - rect.Min.X);
                float height = Math.Max(0f, rect.Max.Y - rect.Min.Y);
                bool hasCamera = false;
                var view = default(System.Numerics.Matrix4x4);
                var projection = default(System.Numerics.Matrix4x4);

                if (_paperViewports.TryGetValue(pair.Key, out var viewport) && GodotObject.IsInstanceValid(viewport))
                {
                    var camera = viewport.GetCamera3D();
                    if (camera != null && GodotObject.IsInstanceValid(camera))
                    {
                        hasCamera = true;
                        view = ViewFromGodot(camera.GlobalTransform);
                        projection = ProjectionFromGodot(camera.GetCameraProjection());
                    }
                }

                list.Add(new ViewportInfo(
                    pair.Key, rect.Min.X, rect.Min.Y, width, height,
                    hasCamera, view, projection));
            }

            _novaViewports = list;
        }

        private void CaptureNovaInput()
        {
            var mouse = _host.GetViewport().GetMousePosition();
            var keys = new HashSet<int>();
            AddIf(keys, Input.IsKeyPressed(Key.W), InputKeys.W);
            AddIf(keys, Input.IsKeyPressed(Key.A), InputKeys.A);
            AddIf(keys, Input.IsKeyPressed(Key.S), InputKeys.S);
            AddIf(keys, Input.IsKeyPressed(Key.D), InputKeys.D);
            AddIf(keys, Input.IsKeyPressed(Key.Q), InputKeys.Q);
            AddIf(keys, Input.IsKeyPressed(Key.E), InputKeys.E);
            AddIf(keys, Input.IsKeyPressed(Key.T), InputKeys.T);
            AddIf(keys, Input.IsKeyPressed(Key.R), InputKeys.R);
            AddIf(keys, Input.IsKeyPressed(Key.Escape), InputKeys.Escape);
            AddIf(keys, Input.IsKeyPressed(Key.Shift), InputKeys.Shift);
            AddIf(keys, Input.IsKeyPressed(Key.Alt), InputKeys.Alt);
            AddIf(keys, Input.IsKeyPressed(Key.Ctrl), InputKeys.Ctrl);

            _novaInput = new InputSnapshot
            {
                MousePosition = new System.Numerics.Vector2(mouse.X, mouse.Y),
                LeftDown = Input.IsMouseButtonPressed(MouseButton.Left),
                RightDown = Input.IsMouseButtonPressed(MouseButton.Right),
                MiddleDown = Input.IsMouseButtonPressed(MouseButton.Middle),
                Wheel = _novaWheel,
                PressedKeys = keys,
                WindowActive = DisplayServer.WindowIsFocused(),
                MouseLocked = Input.MouseMode == Input.MouseModeEnum.Captured,
            };
        }

        private static void AddIf(HashSet<int> keys, bool down, int key)
        {
            if (down) keys.Add(key);
        }

        private static System.Numerics.Matrix4x4 ViewFromGodot(Transform3D worldFromLocal)
        {
            var t = worldFromLocal.Inverse();
            return new System.Numerics.Matrix4x4(
                t.Basis.X.X, t.Basis.X.Y, t.Basis.X.Z, 0f,
                t.Basis.Y.X, t.Basis.Y.Y, t.Basis.Y.Z, 0f,
                t.Basis.Z.X, t.Basis.Z.Y, t.Basis.Z.Z, 0f,
                t.Origin.X, t.Origin.Y, t.Origin.Z, 1f);
        }

        private static System.Numerics.Matrix4x4 ProjectionFromGodot(Projection p)
        {
            return new System.Numerics.Matrix4x4(
                p.X.X, p.X.Y, p.X.Z, p.X.W,
                p.Y.X, p.Y.Y, p.Y.Z, p.Y.W,
                p.Z.X, p.Z.Y, p.Z.Z, p.Z.W,
                p.W.X, p.W.Y, p.W.Z, p.W.W);
        }

        /// <summary>
        /// Lazily creates the SubViewport backing an engine Paper viewport node
        /// (the studio's Scene panel) and returns its render texture. The
        /// renderer reparents every scene element whose snapshot carries this
        /// viewport id into the SubViewport.
        /// </summary>
        private global::Godot.Texture2D? EnsurePaperViewportTexture(long viewportId)
        {
            if (_paperViewports.TryGetValue(viewportId, out var existing) && GodotObject.IsInstanceValid(existing))
            {
                UpdatePaperViewportSize(viewportId, existing);
                return existing.GetTexture();
            }

            var viewport = new SubViewport
            {
                Name = "PaperViewport_" + viewportId,
                TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                HandleInputLocally = false,
            };

            var env = new WorldEnvironment
            {
                Environment = new global::Godot.Environment
                {
                    BackgroundMode = global::Godot.Environment.BGMode.Color,
                    BackgroundColor = new Color(0.09f, 0.10f, 0.13f),
                    AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = new Color(0.55f, 0.58f, 0.65f),
                    AmbientLightEnergy = 1.0f,
                },
            };
            viewport.AddChild(env);

            viewport.AddChild(new DirectionalLight3D
            {
                RotationDegrees = new Vector3(-50f, -30f, 0f),
                LightEnergy = 1.1f,
                ShadowEnabled = true,
            });
            viewport.AddChild(new Camera3D { Current = true });

            _host.AddChild(viewport);
            _paperViewports[viewportId] = viewport;
            UpdatePaperViewportSize(viewportId, viewport);

            if (RendererService is global::V12TwoDog.Renderer renderer)
                renderer.RegisterSceneViewport(viewportId, viewport);
            Root.MarkRenderDirty();

            GD.Print($"[V12Runtime] Paper viewport {viewportId} -> SubViewport {viewport.Size}");
            return viewport.GetTexture();
        }

        private void UpdatePaperViewportSize(long viewportId, SubViewport viewport)
        {
            if (_paperRenderer == null || !_paperRenderer.ViewportRects.TryGetValue(viewportId, out var rect))
                return;

            int width = Math.Max(1, (int)MathF.Round(rect.Max.X - rect.Min.X));
            int height = Math.Max(1, (int)MathF.Round(rect.Max.Y - rect.Min.Y));
            var size = new Vector2I(width, height);
            if (viewport.Size != size)
                viewport.Size = size;
        }

        /// <summary>Stop the worker thread and release XR resources.</summary>
        public void Shutdown()
        {
            if (_disposed) return;
            _disposed = true;

            XR?.Cleanup();
            _worldCanvas?.Dispose();
            _worldCanvas = null;
            if (RendererService is global::V12TwoDog.Renderer sceneRenderer)
            {
                foreach (var id in _paperViewports.Keys)
                    sceneRenderer.UnregisterSceneViewport(id);
            }
            foreach (var viewport in _paperViewports.Values)
                if (GodotObject.IsInstanceValid(viewport)) viewport.QueueFree();
            _paperViewports.Clear();
            try { _godotPaperRenderer?.Dispose(); } catch {}
            if (_paperLayer != null && GodotObject.IsInstanceValid(_paperLayer)) _paperLayer.QueueFree();
            _paperLayer = null; _paperRootControl = null; _paperRenderer = null; _godotPaperRenderer = null;
            _cts?.Cancel();
            _v12Thread?.Join(1000);

            try { Root.Shutdown(); } catch { }
            try { _pakResult?.Dispose(); } catch { }
            _pakResult = null;
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






