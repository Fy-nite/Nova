// using Godot;
// using System.Collections.Generic;
// using V12.Basic.Components;
// using V12.Components;
// using V12.Core;
// using V12.Core.Core.Interfaces;
// using V12.Core.Interfaces.Physics;
// using V12.Core.Interfaces.Renderer;
// using NumVec3 = System.Numerics.Vector3;

// namespace V12TwoDog
// {
//     public partial class PortalBinding : Node3D
//     {
//         private GameRoot _gameRoot;
//         private readonly Dictionary<long, Node3D> _portalNodes = new();
//         private readonly Dictionary<long, long> _linkedPairs = new();
//         private Area3D _playerArea;
//         private static Script _portalScript;

//         private static Script LoadPortalScript()
//         {
//             if (_portalScript == null)
//                 _portalScript = GD.Load<Script>("res://addons/portals/scripts/portal_3d.gd");
//             return _portalScript;
//         }

//         public void Initialize(GameRoot root)
//         {
//             _gameRoot = root;
//             LoadPortalScript();
//         }

//         public void Update()
//         {
//             if (_gameRoot?.SelectedWorld == null) return;

//             // Wait until Godot has a current camera before creating portal nodes.
//             // The V12 Renderer creates the Camera3D in ApplySnapshot; on the very first
//             // frame the snapshot may not be available yet and Portal3D._ready() will assert.
//             if (GetViewport()?.GetCamera3D() == null)
//                 return;

//             var world = _gameRoot.SelectedWorld;

//             world.Lock.EnterReadLock();
//             try
//             {
//                 var currentIds = new HashSet<long>();

//                 void ScanElement(IWorldElement element)
//                 {
//                     if (element?.Components == null) return;

//                     var portalComp = element.GetComponent<PortalComponent>();
//                     if (portalComp != null && portalComp.Active)
//                     {
//                         currentIds.Add(element.Id);
//                         EnsurePortalNode(element, portalComp);
//                     }

//                     if (element.Children != null)
//                         foreach (var child in element.Children)
//                             ScanElement(child);
//                 }

//                 foreach (var root in world.Root.ToArray())
//                     ScanElement(root);

//                 // Remove stale portal nodes
//                 var toRemove = new List<long>();
//                 foreach (var kvp in _portalNodes)
//                 {
//                     if (!currentIds.Contains(kvp.Key))
//                     {
//                         if (GodotObject.IsInstanceValid(kvp.Value))
//                             kvp.Value.QueueFree();
//                         toRemove.Add(kvp.Key);
//                     }
//                 }
//                 foreach (var id in toRemove)
//                 {
//                     _portalNodes.Remove(id);
//                     _linkedPairs.Remove(id);
//                 }

//                 // Second pass: link exit_portal and setup cameras
//                 foreach (var kvp in _portalNodes)
//                 {
//                     var element = FindElementById(world, kvp.Key);
//                     if (element == null) continue;

//                     var portalComp = element.GetComponent<PortalComponent>();
//                     if (portalComp == null) continue;

//                     var portalNode = kvp.Value;
//                     UpdatePortalNode(portalNode, portalComp);

//                     if (portalComp.ExitPortalElementId != 0)
//                     {
//                         if (!_linkedPairs.TryGetValue(kvp.Key, out var currentExit) || currentExit != portalComp.ExitPortalElementId)
//                         {
//                             _linkedPairs[kvp.Key] = portalComp.ExitPortalElementId;

//                             if (_portalNodes.TryGetValue(portalComp.ExitPortalElementId, out var exitNode))
//                             {
//                                 portalNode.Set("exit_portal", exitNode);

//                                 // _ready() skipped _setup_cameras because exit_portal was null.
//                                 // Trigger it now so SubViewport + Camera3D are created.
//                                 var hasCam = portalNode.Get("portal_camera").Obj != null;
//                                 if (!hasCam)
//                                     portalNode.Call("_setup_cameras");
//                             }
//                         }
//                     }
//                     else
//                     {
//                         portalNode.Set("exit_portal", new Variant());
//                     }
//                 }

//                 // Ensure the player has an Area3D for portal teleport detection
//                 var playerElement = FindPlayer();
//                 if (playerElement != null && _portalNodes.Count > 0)
//                     EnsurePlayerArea(playerElement);
//             }
//             finally { world.Lock.ExitReadLock(); }

//             // Teleport check runs after the lock is released
//             if (_gameRoot?.SelectedWorld != null)
//                 ManualTeleportCheck(_gameRoot.SelectedWorld);
//         }

//         private void EnsurePortalNode(IWorldElement element, PortalComponent portalComp)
//         {
//             if (_portalNodes.ContainsKey(element.Id))
//                 return;

//             var script = _portalScript;
//             var portal3D = new Node3D();
//             portal3D.SetScript(script);
//             portal3D.Set("name", $"{element.Name ?? "Portal"}_{element.Id}");

//             // Set up internal children before _ready() fires
//             portal3D.Call("_setup_mesh");
//             portal3D.Call("_setup_teleport");

//             // Set initial properties
//             portal3D.Set("portal_size", new Vector2(portalComp.Width, portalComp.Height));
//             portal3D.Set("is_teleport", portalComp.IsTeleport);
//             portal3D.Set("teleport_tolerance", portalComp.TeleportTolerance);

//             // Match element transform
//             var tc = element.GetComponent<TransformComponent>();
//             if (tc != null)
//             {
//                 portal3D.Set("position", new Vector3(tc.X, tc.Y, tc.Z));
//                 portal3D.Set("rotation", new Vector3(tc.RX, tc.RY, tc.RZ));
//             }

//             // Add to the scene tree — triggers _ready() which now finds mesh + teleport area
//             var tree = GetTree();
//             if (tree?.CurrentScene != null)
//             {
//                 tree.CurrentScene.AddChild(portal3D);
//             }

//             _portalNodes[element.Id] = portal3D;
//         }

//         private void UpdatePortalNode(Node3D portalNode, PortalComponent portalComp)
//         {
//             portalNode.Set("portal_size", new Vector2(portalComp.Width, portalComp.Height));
//             portalNode.Set("is_teleport", portalComp.IsTeleport);
//             portalNode.Set("teleport_tolerance", portalComp.TeleportTolerance);

//             var tpDir = portalComp.TeleportDirection switch
//             {
//                 PortalTeleportDirection.Front => 0,
//                 PortalTeleportDirection.Back => 1,
//                 _ => 2
//             };
//             portalNode.Set("teleport_direction", tpDir);

//             var viewDir = portalComp.ViewDirection switch
//             {
//                 PortalViewDirection.OnlyFront => 1,
//                 PortalViewDirection.OnlyBack => 2,
//                 _ => 0
//             };
//             portalNode.Set("view_direction", viewDir);

//             if (!portalComp.IsActive)
//             {
//                 portalNode.Call("hide");
//                 portalNode.Set("process_mode", (int)Node.ProcessModeEnum.Disabled);
//             }
//         }

//         private void EnsurePlayerArea(IWorldElement playerElement)
//         {
//             if (_playerArea != null && GodotObject.IsInstanceValid(_playerArea))
//                 return;

//             var tree = GetTree();
//             var playerNode = tree?.CurrentScene?.FindChild("Player", true, false) as Node3D;
//             if (playerNode == null) return;

//             var area = new Area3D();
//             area.Name = "PlayerPortalDetector";
//             area.CollisionLayer = 1;

//             var shape = new CollisionShape3D();
//             shape.Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.8f };
//             area.AddChild(shape);

//             // Teleport the player's root Node3D instead of the Area3D
//             area.SetMeta("teleport_root", new NodePath(".."));

//             playerNode.AddChild(area);
//             _playerArea = area;
//         }

//         private void ManualTeleportCheck(World world)
//         {
//             var playerElement = FindPlayer();
//             if (playerElement == null || _portalNodes.Count == 0) return;

//             var curPos = playerElement.LocalTransform.Position;
//             var curGodot = new Vector3(curPos.X, curPos.Y, curPos.Z);

//             // Decrement cooldowns
//             var cdKeys = new List<long>(_teleportCooldown.Keys);
//             foreach (var k in cdKeys)
//             {
//                 if (--_teleportCooldown[k] <= 0)
//                     _teleportCooldown.Remove(k);
//             }

//             foreach (var kvp in _portalNodes)
//             {
//                 if (_teleportCooldown.TryGetValue(kvp.Key, out var cd) && cd > 0)
//                     continue;

//                 if (!_linkedPairs.TryGetValue(kvp.Key, out var exitId)) continue;
//                 if (!_portalNodes.TryGetValue(exitId, out var exitNode)) continue;

//                 var portal3D = kvp.Value;
//                 var portalComp = FindElementById(world, kvp.Key)?.GetComponent<PortalComponent>();
//                 if (portalComp == null || !portalComp.IsTeleport || !portalComp.IsActive) continue;

//                 var portalPos = portal3D.GlobalPosition;
//                 var portalNormal = -portal3D.GlobalBasis.Z;
//                 var offset = curGodot - portalPos;
//                 var curDist = portalNormal.Dot(offset);

//                 if (Mathf.Abs(curDist) >= portalComp.TeleportTolerance) continue;

//                 // // Retrieve stored previous distance, or initialize it
//                 // if (!_prevPortalDist.TryGetValue(kvp.Key, out var prevDist))
//                 // {
//                 //     _prevPortalDist[kvp.Key] = curDist;
//                 //     continue;
//                 // }
//                 //_prevPortalDist[kvp.Key] = curDist;

//                 if (!(Mathf.Sign(prevDist) != Mathf.Sign(curDist))) continue;

//                 // Player crossed the portal plane — teleport
//                 var playerTransform = new Transform3D(Basis.Identity, curGodot);
//                 var exitTransform = (Transform3D)portal3D.Call("to_exit_transform", playerTransform);

//                 var exitPortal3D = exitNode;
//                 var forwardOffset = -exitPortal3D.GlobalBasis.Z * 1.2f;
//                 var teleportOrigin = exitTransform.Origin + forwardOffset;

//                 var newPos = new NumVec3(
//                     teleportOrigin.X, teleportOrigin.Y, teleportOrigin.Z);

//                 // Update V12 element position
//                 playerElement.LocalTransform = new TRS
//                 {
//                     Position = newPos,
//                     Rotation = playerElement.LocalTransform.Rotation,
//                     Scale = playerElement.LocalTransform.Scale
//                 };

//                 // Sync physics body position if present
//                 var physComp = playerElement.GetComponent<PhysicsBodyComponent>();
//                 if (physComp?.Body != null)
//                     physComp.Body.Position = newPos;

//                 // Move the Godot player node so the portal experience is immediate
//                 if (_playerArea != null && GodotObject.IsInstanceValid(_playerArea))
//                 {
//                     var playerNode = _playerArea.GetParent<Node3D>();
//                     if (playerNode != null)
//                         playerNode.GlobalPosition = teleportOrigin;
//                 }

//                 // Zero velocity on the Godot CharacterBody3D to prevent sliding
//                 var godotPlayer = _playerArea?.GetParent<Node3D>();
//                 if (godotPlayer is CharacterBody3D charBody)
//                     charBody.Velocity = Vector3.Zero;

//                 // Apply cooldown to both source and exit portals so the check
//                 // doesn't re-trigger before the V12 worker thread has a chance to
//                 // converge with the new position (~0.5s at 60 fps).
//                 _teleportCooldown[kvp.Key] = 30;
//                 _teleportCooldown[exitId] = 30;

//                 GD.Print($"[PortalBinding] Teleported player to ({newPos.X:F2}, {newPos.Y:F2}, {newPos.Z:F2})");
//             }
//         }

//         private readonly Dictionary<long, int> _teleportCooldown = new();

//         private IWorldElement? FindPlayer()
//         {
//             return _gameRoot.FindElementWithComponent<PlayerComponent>();
//         }

//         private static IWorldElement FindElementById(World world, long id)
//         {
//             IWorldElement result = null;
//             void Search(IWorldElement e)
//             {
//                 if (result != null) return;
//                 if (e.Id == id) { result = e; return; }
//                 if (e.Children != null)
//                     foreach (var child in e.Children)
//                         Search(child);
//             }
//             foreach (var root in world.Root.ToArray())
//             {
//                 Search(root);
//                 if (result != null) break;
//             }
//             return result;
//         }

//         public void Cleanup()
//         {
//             foreach (var kvp in _portalNodes)
//             {
//                 if (GodotObject.IsInstanceValid(kvp.Value))
//                     kvp.Value.QueueFree();
//             }
//             _portalNodes.Clear();
//             _linkedPairs.Clear();
//             _prevPortalDist.Clear();
//             _teleportCooldown.Clear();
//             _playerArea = null;
//         }
//     }
// }
