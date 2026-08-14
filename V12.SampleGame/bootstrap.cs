using System.Numerics;
using System.Reflection;
using V12.Basic;
using V12.Basic.Components;
using V12.Components;
using V12.Components.Renderables;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.NetworkCable;
using V12.WorldML;
using V12.Bindings;
// UI components live in V12.Components.UI but share names with physics/render
// components (ButtonComponent, ProgressBarComponent), so alias them here.
using UICanvas = V12.Components.UI.CanvasComponent;
using UILabel = V12.Components.UI.LabelComponent;
using UIButton = V12.Components.UI.ButtonComponent;
using UIProgressBar = V12.Components.UI.ProgressBarComponent;
using ButtonComponent = V12.Components.ButtonComponent;

namespace V12.SampleGame
{
    /// <summary>
    /// this is V12's Sample game pak
    /// this is the entry point for the game pak, and is called by the V12 engine when the game pak is loaded
    /// 
    /// for making a proper game, please don't use the existing SampleGamePack, but instead create your own game pak and implement the IV12Gamepack interface
    /// this code is horrible lOL
    /// </summary>
    public class SampleGamePack : IV12Gamepack
    {
        private GameRoot _gameroot = default!;

        public string Name => "Sample Game";

        /// <summary>
        /// Phase 1: called after platform services are registered but before
        /// the game loop begins. Register gamepak-specific services here.
        /// </summary>
        public void Initialize()
        {
            Console.WriteLine("[SampleGamePack] Initialize called");
            _gameroot = GameRoot.Instance;
            BasicRegistry.RegisterAll(_gameroot);
            V12ScriptRuntimeRegistration.RegisterAll(_gameroot);
        }

        /// <summary>
        /// Phase 2: called when this gamepak is launched. Build the scene.
        /// </summary>
        public void OnStart()
        {
            Console.WriteLine("[SampleGamePack] OnStart called");

            // Gamepak mode starts with NO selected world — create + select one
            // or nothing ever appears on screen.
            var world = _gameroot.CreateWorld("SampleWorld");
            _gameroot.SelectWorld(world);
            Console.WriteLine($"[SampleGamePack] Created + selected world '{world.WorldName}'");

            var xrService = _gameroot.Registry.Get("XRTrackingService");
            bool xrMode = xrService != null;
            Console.WriteLine(xrMode
                ? "[SampleGamePack] XR mode — spawning XR player (rig attached)"
                : "[SampleGamePack] Desktop mode — spawning player (XR rig attached so hands track when XR comes up)");

            SpawnPhysicsWorldOnly();

            // Demo world-space UI canvas (CanvasComponent system, rendered by
            // WorldCanvasSystem): title, animated progress bar, button, status text.
            SpawnWorldUi(world);

            // Always build the player WITH the XR rig (head + hands), and always
            // into PersistentWorld:
            //  - GameRoot.Player only searches PersistentWorld. The XR player
            //    used to go into SelectedWorld, so player sync (incl. hand
            //    poses) never sent — nothing for V12 to read back.
            //  - The rig existing from the start means XRTrackingService can
            //    write head/hand poses into it the moment OpenXR comes up, and
            //    CapturePlayerRig can send them over the network.
            var player = BuildPlayer(xrMode);
            _gameroot.PersistentWorld.AddElement(player);
            Console.WriteLine("[SampleGamePack] Player (with XR rig) added to PersistentWorld (survives world switches).");

            SpawnSpawnButton(world);

            try
            {
                Console.WriteLine("Loading World");
                // var loadedWorld = WorldLoader.LoadFromArchive("tbg.V12World");
                Console.WriteLine("World loading is disabled for client systems because the world archive is not included in the client build.");
                // _gameroot.SelectedWorld?.Root.AddRange(loadedWorld.Root);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception loading world: {ex}");
                Console.WriteLine(ex.Message);
            }

            Console.WriteLine($"[SampleGamePack] SelectedWorld is null? {_gameroot.SelectedWorld == null}");
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private string ReadResource(string name)
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null)
            {
                Console.WriteLine($"Resource '{name}' not found.");
                return "";
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Build the local Player element. In both modes the full XR rig
        /// (XR_Root → XR_Head / XR_LeftHand / XR_RightHand) is attached so the
        /// head/hand poses are always available to XRTrackingService and the
        /// network sync, even if OpenXR comes up after boot.
        /// </summary>
        private Element BuildPlayer(bool xrMode)
        {
            var player = new Element
            {
                Name = "Player",
                LocalTransform = new TRS { Position = new Vector3(0, 0f, 0), Rotation = Quaternion.Identity, Scale = Vector3.One }
            };
            player.AddComponent(new PlayerComponent { IsXrMode = xrMode });

            if (xrMode)
            {
                player.AddComponent(new VRPlayerComponent());

                // XR character controller: a kinematic capsule driven by
                // LocomotionComponent.Velocity — head-relative movement with
                // gravity, jump and wall-sliding (see PlayerComponent +
                // PhysicsLocomotionSystem).
                player.AddComponent(new LocomotionComponent
                {
                    MoveSpeed = 4f,
                    JumpStrength = 6f,
                    Gravity = 20f,
                    CanJump = true,
                    Acceleration = 12f
                });
                player.AddComponent(new ColliderComponent(MeshShape.Capsule, 0.6f, 1f, 0.6f));
                player.AddComponent(new PhysicsBodyComponent { IsKinematic = true });
            }
            else
            {
                player.AddComponent(new LocomotionComponent
                {
                    MoveSpeed = 5f,
                    JumpStrength = 6f,
                    Gravity = 20f
                });
                player.AddComponent(new ColliderComponent(MeshShape.Capsule, 0.6f, 1f, 0.6f));
                player.AddComponent(new PhysicsBodyComponent { IsKinematic = false });

                var playerMesh = new MeshComponent(MeshShape.Capsule, 0.6f, 1.8f, 0.6f);
                player.AddComponent(playerMesh);
                player.AddComponent(new MeshRenderer { Mesh = playerMesh });

                player.AddComponent(new ScriptComponent
                {
                    ScriptText = "function on_init()\n    print(\"Hello from Lua!\")\nend"
                });
            }

            // ── XR rig: ALWAYS present ──
            var xrRoot = new Element { Name = "XR_Root" };
            xrRoot.AddComponent(new XRRootComponent());
            player.AddChild(xrRoot);

            var xrHead = MakeBox("XR_Head", new Vector3(0.1f, 0.1f, 0.06f));
            xrHead.AddComponent(new XRHeadComponent());
            xrHead.AddComponent(new XRVisualizerComponent { Target = XRPoseTarget.Head });
            xrRoot.AddChild(xrHead);

            var xrLeft = MakeBox("XR_LeftHand", new Vector3(0.08f, 0.08f, 0.1f));
            xrLeft.AddComponent(new XRHandComponent(HandSide.Left));
            xrLeft.AddComponent(new XRVisualizerComponent { Target = XRPoseTarget.LeftHand });
            xrRoot.AddChild(xrLeft);

            var xrRight = MakeBox("XR_RightHand", new Vector3(0.08f, 0.08f, 0.1f));
            xrRight.AddComponent(new XRHandComponent(HandSide.Right));
            xrRight.AddComponent(new XRVisualizerComponent { Target = XRPoseTarget.RightHand });
            xrRoot.AddChild(xrRight);

            return player;
        }

        /// <summary>
        /// Demo button that spawns a physics box when pressed — a handy test
        /// target for the XR interact/pickup systems and desktop raycast pickup. The button is a kinematic box with a ButtonComponent that calls back into this gamepak to spawn a dynamic box in front of the player.
        /// </summary>
        private void SpawnSpawnButton(World world)
        {
            var spawnCount = 0;
            var button = new Element
            {
                Name = "SpawnBoxButton",
                LocalTransform = new TRS { Position = new Vector3(0, 1f, -2), Rotation = Quaternion.Identity, Scale = Vector3.One }
            };
            button.AddComponent(new ColliderComponent(MeshShape.Box, 0.3f, 0.3f, 0.3f));
            button.AddComponent(new PhysicsBodyComponent { IsKinematic = true });
            var buttonMesh = new MeshComponent(MeshShape.Box, 0.3f, 0.3f, 0.3f);
            button.AddComponent(buttonMesh);
            button.AddComponent(new MeshRenderer { Mesh = buttonMesh });
            button.AddComponent(new ButtonComponent
            {
                Label = "Spawn Box",
                OnPressed = () =>
                {
                    // Host-authoritative spawning: the RpcDispatcher broadcast runs this
                    // handler on EVERY peer, so clients must not spawn locally — the host
                    // spawns once (with its sequential element id) and the WorldDelta
                    // replicate carries the same element + id to all clients.
                    if (_gameroot.Registry.Get<NetworkClient>("NetworkClient") != null)
                    {
                        Console.WriteLine("[SampleGamePack] Client: skipping local spawn — waiting for host WorldDelta.");
                        return;
                    }

                    spawnCount++;
                    Console.WriteLine($"[SampleGamePack] Spawning box #{spawnCount}");
                    var box = new Element
                    {
                        Name = $"SpawnedBox_{spawnCount}",
                        LocalTransform = new TRS { Position = new Vector3(0, 2.5f, -4), Rotation = Quaternion.Identity, Scale = Vector3.One }
                    };
                    box.AddComponent(new ColliderComponent(MeshShape.Box, 0.4f, 0.4f, 0.4f));
                    box.AddComponent(new PhysicsBodyComponent { IsKinematic = false });
                    var boxMesh = new MeshComponent(MeshShape.Box, 0.4f, 0.4f, 0.4f);
                    box.AddComponent(boxMesh);
                    box.AddComponent(new MeshRenderer { Mesh = boxMesh });
                    _gameroot.SelectedWorld?.AddElement(box);
                }
            });
            world.AddElement(button);
        }

        private void SpawnPhysicsWorldOnly()
        {
            // ---- Ground ----
            var ground = new Element
            {
                Name = "Ground",
                LocalTransform = new TRS { Position = new Vector3(0, -1, 0), Rotation = Quaternion.Identity, Scale = Vector3.One }
            };
            ground.AddComponent(new ColliderComponent(MeshShape.Box, 40f, 1f, 40f));
            ground.AddComponent(new PhysicsBodyComponent { IsKinematic = true });
            var groundMesh = new MeshComponent { Shape = MeshShape.Box, Width = 40f, Height = 1f, Depth = 40f };
            ground.AddComponent(groundMesh);
            ground.AddComponent(new MeshRenderer { Mesh = groundMesh });
            _gameroot.SelectedWorld?.AddElement(ground);
        }

        private static Element MakeBox(string name, Vector3 scale)
        {
            var b = new Element { Name = name };
            b.AddComponent(new ColliderComponent(MeshShape.Box, scale.X, scale.Y, scale.Z));
            var mesh = new MeshComponent(MeshShape.Box, scale.X, scale.Y, scale.Z);
            b.AddComponent(mesh);
            b.AddComponent(new MeshRenderer { Mesh = mesh });
            return b;
        }

        /// <summary>
        /// Demo world-space UI canvas (CanvasComponent system). A monitor-style
        /// surface at the given world transform with a title, an animated progress
        /// bar, a button, and a status label. Rendered by WorldCanvasSystem.
        /// </summary>
        private void SpawnWorldUi(World world)
        {
            var canvas = new Element
            {
                Name = "StatusMonitor",
                LocalTransform = new TRS
                {
                    Position = new Vector3(2.5f, 1.6f, -3),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One
                }
            };
            canvas.AddComponent(new UICanvas());
            canvas.AddComponent(new ScaleComponent(2f, 1.3f, 1f));
            world.AddElement(canvas);

            var title = new Element("MonitorTitle");
            title.AddComponent(new UILabel { Text = "Status Monitor", FontSize = 22f });
            canvas.AddChild(title);

            var hpBar = new Element("HPBar");
            hpBar.AddComponent(new UIProgressBar { Value = 0.75f });
            canvas.AddChild(hpBar);

            var refreshBtn = new Element("RefreshBtn");
            refreshBtn.AddComponent(new UIButton("Refresh", () => Console.WriteLine("[UI] Refresh clicked")));
            canvas.AddChild(refreshBtn);

            var statusLabel = new Element("StatusLabel");
            statusLabel.AddComponent(new UILabel { Text = "System: nominal", FontSize = 14f });
            canvas.AddChild(statusLabel);

            // Animate the progress bar to prove live updates flow through.
            _gameroot.Registry.Register("DemoUiAnimator", new DemoUiAnimator(hpBar.GetComponent<UIProgressBar>()));
        }

        /// <summary>Bumps the demo progress bar every frame so UI updates are visible.</summary>
        private sealed class DemoUiAnimator : IGameService
        {
            private readonly UIProgressBar _bar;
            private float _t;
            public DemoUiAnimator(UIProgressBar bar) { _bar = bar; }
            public void Initialize(GameRoot g) { }
            public void Update(GameRoot gameRoot) { }
            public void Update(float deltaTime)
            {
                _t = (_t + deltaTime * 0.25f) % 1f;
                if (_bar != null)
                    _bar.Value = 0.25f + 0.75f * (0.5f + 0.5f * (float)Math.Sin(_t * Math.PI * 2));
            }
        }
    }
}
