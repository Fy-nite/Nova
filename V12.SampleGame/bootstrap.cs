using System.Numerics;
using System.Reflection;
using V12.Basic;
using V12.Basic.Components;
using V12.Components;
using V12.Components.Renderables;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.WorldML;

namespace V12.SampleGame
{
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
        }

        /// <summary>
        /// Phase 2: called when this gamepak is launched. Build the scene.
        /// </summary>
        public void OnStart()
        {
            Console.WriteLine("[SampleGamePack] OnStart called");

            var xrService = _gameroot.Registry.Get("XRTrackingService");
            if (xrService != null)
            {
                Console.WriteLine("[SampleGamePack] XR mode — spawning XR player");
                SpawnPhysicsWorldOnly();
                SpawnXrScene();
            }
            else
            {
                Console.WriteLine("[SampleGamePack] Desktop mode — spawning physics test world");
                SpawnPhysicsTestWorld();
            }

            try
            {
                Console.WriteLine("Loading World");
                var world = WorldLoader.LoadFromArchive("tbg.V12World");
                _gameroot.SelectedWorld?.Root.AddRange(world.Root);
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

        private void SpawnXrScene()
        {
            var world = _gameroot.SelectedWorld;
            if (world == null) return;

            var xrPlayer = new Element { Name = "Player" };
            xrPlayer.AddComponent(new PlayerComponent { IsXrMode = true });
            xrPlayer.AddComponent(new VRPlayerComponent());
            world.AddElement(xrPlayer);

            var xrRoot = new Element { Name = "XR_Root" };
            xrRoot.AddComponent(new XRRootComponent());
            xrPlayer.AddChild(xrRoot);

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

            // ── Demo button: spawns a physics box when pressed ──
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

        private void SpawnPhysicsTestWorld()
        {
            SpawnPhysicsWorldOnly();
            SpawnPlayer();
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

        private void SpawnPlayer()
        {
            var player = new Element
            {
                Name = "Player",
                LocalTransform = new TRS { Position = new Vector3(0, 1.5f, 0), Rotation = Quaternion.Identity, Scale = Vector3.One }
            };
            player.AddComponent(new PlayerComponent());
            player.AddComponent(new LocomotionComponent
            {
                MoveSpeed = 5f,
                JumpStrength = 6f,
                Gravity = 20f
            });
            player.AddComponent(new ColliderComponent(MeshShape.Capsule, 0.6f, 1.8f, 0.6f));
            player.AddComponent(new PhysicsBodyComponent { IsKinematic = false });

            var playerMesh = new MeshComponent(MeshShape.Capsule, 0.6f, 1.8f, 0.6f);
            player.AddComponent(playerMesh);
            player.AddComponent(new MeshRenderer { Mesh = playerMesh });

            player.AddComponent(new ScriptComponent
            {
                ScriptText = "function on_init()\n    print(\"Hello from Lua!\")\nend"
            });
            _gameroot.PersistentWorld.AddElement(player);
            Console.WriteLine("[SampleGamePack] Player added to PersistentWorld (survives world switches).");
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
    }
}
