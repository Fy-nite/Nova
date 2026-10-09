using System;
using System.Collections.Generic;
using Godot;
using V12.Components.UI;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces.Renderer;
using V12.Core.UI;

namespace V12TwoDog
{
    /// <summary>
    /// Host-side renderer for V12's <c>CanvasComponent</c> UI. This mirrors the
    /// Locus <c>WorldCanvasSystem</c> described in <c>UI_OVERVIEW.md</c>: it walks
    /// the V12 element tree each frame, finds elements bearing a
    /// <c>CanvasComponent</c>, and creates/updates/destroys Godot scene nodes for
    /// each canvas.
    ///
    /// World-space canvases (<see cref="CanvasComponent.ScreenSpace"/> == false)
    /// become 3D nodes — coloured quads + <c>Label3D</c> text — rooted at the
    /// element's world transform, so UI can be embedded on monitors, holograms,
    /// etc. Screen-space canvases become Godot <c>Control</c> nodes inside an
    /// overlay <c>CanvasLayer</c>, exactly like a HUD/menu.
    ///
    /// Interaction: the player's aim ray is cast against world canvases each frame;
    /// pressing "interact" while pointing at a UI button invokes its click.
    /// </summary>
    public class WorldCanvasSystem : IInputHandler, V12.Core.UI.IViewportInteraction
    {
        /// <summary>Label3D pixel-to-world scale (world units per font pixel).</summary>
        private const float PixelScale = 0.005f;
        private const float RayLength = 20f;

        private readonly Node3D _host;
        private readonly Renderer _renderer;
        private CanvasLayer? _screenLayer;
        private readonly Dictionary<long, CanvasNode> _canvases = new();
        private GameRoot _gameRoot;
        private bool _inputRegistered;
        private volatile bool _interactRequested;

        // Continuous hover/drag interaction state for world-space canvases.
        private WidgetNode? _hoveredWidget;
        private WidgetNode? _grabbedWidget;   // slider being dragged, if any
        private CanvasNode? _grabCanvas;
        private bool _interactHeld;

        // 3D viewport picking + translation gizmo (editor).
        private readonly SceneGizmo _gizmo;
        private Action<IWorldElement?>? _selectHandler;
        // The viewport widget currently receiving editor input (picking/orbit).
        private WidgetNode? _pickViewport;

        // Editor orbit camera: drives the camera element the editor registered
        // via SetEditorCamera. Right-drag orbits, wheel dollies, and selecting
        // an object recenters the focus on it.
        private IWorldElement? _editorCam;
        private float _camYaw = -90f;
        private float _camPitch = -14f;
        private float _camDist = 7f;
        private Vector3 _camFocus = new(0f, 0.9f, 0f);
        private bool _orbiting;
        private Vector2 _lastOrbitPos;

        public WorldCanvasSystem(Node3D host, Renderer renderer)
        {
            _host = host;
            _renderer = renderer;
            _gizmo = new SceneGizmo(renderer);
        }

        /// <summary>Register the element the orbit camera should drive (the
        /// editor's ViewportCamera). Pass null to stop driving.</summary>
        public void SetEditorCamera(IWorldElement? cameraElement)
        {
            if (_editorCam != null && _editorCam.Id != cameraElement?.Id)
                _renderer.SetEditorDriven(_editorCam.Id, false);
            _editorCam = cameraElement;
            if (_editorCam != null)
                _renderer.SetEditorDriven(_editorCam.Id, true);
        }

        /// <summary>Switch the transform gizmo between translate / rotate / scale.</summary>
        public void SetGizmoMode(GizmoMode mode) => _gizmo.SetMode(mode);

        /// <summary>
        /// Open a Save/Open file dialog (Godot's embedded FileDialog, which
        /// renders fine in the 2dog host — the native OS dialogs cannot
        /// initialize here). The dialog is a transient window above the editor
        /// UI; <paramref name="onComplete"/> fires on the main thread with the
        /// chosen path, or null on cancel.
        /// </summary>
        public void ShowFileDialog(bool save, string title, string initialPath, Action<string?> onComplete)
        {
            try
            {
                var dlg = new FileDialog
                {
                    Title = title,
                    Access = FileDialog.AccessEnum.Filesystem,
                    FileMode = save ? FileDialog.FileModeEnum.SaveFile : FileDialog.FileModeEnum.OpenFile,
                    UseNativeDialog = false, // OS dialogs broken in this host; embedded renders in-engine
                    Size = new Vector2I(920, 600)
                };
                if (!string.IsNullOrEmpty(initialPath))
                {
                    // Resolve to an absolute path so CurrentDir/CurrentFile get
                    // valid values (a bare relative name would throw).
                    string full;
                    try { full = System.IO.Path.GetFullPath(initialPath); }
                    catch { full = initialPath; }
                    string dir = System.IO.Path.GetDirectoryName(full) ?? "/";
                    string file = System.IO.Path.GetFileName(full);
                    if (dir.Length > 0 && dir != file)
                    {
                        try { dlg.CurrentDir = dir; } catch { }
                        if (file.Length > 0)
                        {
                            try { dlg.CurrentFile = file; } catch { }
                        }
                    }
                }

                dlg.FileSelected += path =>
                {
                    dlg.QueueFree();
                    try { onComplete?.Invoke(path); } catch { }
                };
                dlg.Canceled += () => dlg.QueueFree();

                _host.AddChild(dlg);
                dlg.PopupCentered();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[WorldCanvasSystem] FileDialog failed: {ex.Message}");
                try { onComplete?.Invoke(null); } catch { }
            }
        }

        /// <summary>Register the callback invoked when the user picks an element
        /// in a 3D viewport (the editor's Select).</summary>
        public void SetSelectHandler(Action<IWorldElement?> handler) => _selectHandler = handler;

        /// <summary>Show the translation gizmo around <paramref name="el"/> in the
        /// viewport it renders in (null hides it). Also recenters the orbit
        /// camera focus on the newly selected object (editor standard).</summary>
        public void SetGizmoTarget(IWorldElement? el)
        {
            if (el == null)
            {
                _gizmo.SetTarget(null, null!);
                return;
            }
            if (!_orbiting)
                _camFocus = ElementWorldPosGodot(el);
            long vpId = _renderer.GetElementViewportId(el.Id);
            var vp = _renderer.GetSceneViewport(vpId);
            if (vp == null)
            {
                foreach (var cn in _canvases.Values)
                {
                    foreach (var wn in cn.Widgets.Values)
                    {
                        if (wn.ScreenViewport != null && GodotObject.IsInstanceValid(wn.ScreenViewport))
                        {
                            vp = wn.ScreenViewport;
                            break;
                        }
                    }
                    if (vp != null) break;
                }
            }
            if (vp == null)
            {
                _gizmo.SetTarget(null, null!);
                return;
            }
            _gizmo.SetTarget(el, vp);
        }

        /// <summary>
        /// Release all UI nodes (canvases + overlay layer). Call on shutdown so
        /// Godot doesn't leak the overlay's Canvas RID.
        /// </summary>
        public void Dispose()
        {
            _gizmo.Free();
            SetHover(null);
            _grabbedWidget = null;
            _grabCanvas = null;
            foreach (var cn in _canvases.Values)
                cn.Free();
            _canvases.Clear();
            if (_screenLayer != null && GodotObject.IsInstanceValid(_screenLayer))
            {
                _screenLayer.QueueFree();
                _screenLayer = null;
            }
        }

        private CanvasLayer ScreenLayer()
        {
            if (_screenLayer == null)
            {
                _screenLayer = new CanvasLayer { Layer = 90 };
                _host.AddChild(_screenLayer);
            }
            return _screenLayer;
        }

        /// <summary>
        /// Call once per frame from the main thread (after the worker has updated
        /// the element tree). Reconciles every canvas in the active worlds.
        /// </summary>
        public void Update(GameRoot root)
        {
            _gameRoot = root;

            var found = new HashSet<long>();

            foreach (var world in root.ActiveWorlds)
            {
                world.Lock.EnterReadLock();
                try
                {
                    foreach (var el in world.Root)
                        CollectCanvases(el, found);
                }
                finally { world.Lock.ExitReadLock(); }
            }

            var toRemove = new List<long>();
            foreach (var kvp in _canvases)
            {
                if (!found.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var id in toRemove)
            {
                _canvases[id].Free();
                _canvases.Remove(id);
            }
            // ── Interaction: raycast the aim ray against world canvases ──
            EnsureInputRegistered();
            UpdateInteraction();

            // ── Per-frame viewport input polling ──
            // Orbit and gizmo drags are driven by polling the container-local
            // mouse position every frame instead of reacting to motion events.
            // This stays responsive even when the host batches motion events or
            // the cursor leaves the small viewport mid-drag. Buttons (press/
            // release) and wheel still come from the gui_input handler.
            if (_pickViewport?.ScreenViewportContainer != null
                && GodotObject.IsInstanceValid(_pickViewport.ScreenViewportContainer))
            {
                var vp = _pickViewport.ScreenViewportContainer;
                if (_orbiting && !Input.IsMouseButtonPressed(MouseButton.Right))
                    _orbiting = false;               // released outside the viewport
                if (_gizmo.IsDragging && !Input.IsMouseButtonPressed(MouseButton.Left))
                    _gizmo.HandleMouseUp();          // released outside the viewport

                Vector2 local = vp.GetLocalMousePosition();
                if (_orbiting)
                {
                    Vector2 delta = local - _lastOrbitPos;
                    _lastOrbitPos = local;
                    _camYaw -= delta.X * 0.25f;
                    _camPitch = Mathf.Clamp(_camPitch + delta.Y * 0.25f, -89f, 89f);
                }
                else if (_gizmo.IsDragging)
                {
                    _gizmo.HandleMouseMove(local);
                }
            }

            // ── Editor gizmo follows its target when not being dragged ──
            _gizmo.Update();
            UpdateCamera();
        }

        // ── 3D viewport picking + orbit camera (editor) ───────────────────

        /// <summary>Route container-local input events (clicks/drags/wheel) to
        /// picking, the gizmo, and the orbit camera. Called via the
        /// SubViewportContainer's gui_input signal — more reliable in this
        /// embedded host than SubViewport input forwarding.</summary>
        private void HandleViewportContainerInput(WidgetNode wn, global::Godot.InputEvent evt)
        {
            switch (evt)
            {
                case global::Godot.InputEventKey key when key.Pressed && !key.Echo:
                    // T / R / S switch gizmo mode (editor convention).
                    switch (key.Keycode)
                    {
                        case Key.T: _gizmo.SetMode(GizmoMode.Translate); break;
                        case Key.R: _gizmo.SetMode(GizmoMode.Rotate); break;
                        case Key.S: _gizmo.SetMode(GizmoMode.Scale); break;
                    }
                    break;
                case InputEventMouseButton mb:
                    // Use GetLocalMousePosition() rather than mb.Position — some
                    // host bindings deliver gui_input positions in window (not
                    // container-local) coordinates, which would make picking and
                    // gizmo grabs miss everything ("click through").
                    Vector2 localPos = wn.ScreenViewportContainer != null
                        ? wn.ScreenViewportContainer.GetLocalMousePosition()
                        : mb.Position;
                    switch (mb.ButtonIndex)
                    {
                        case MouseButton.Left:
                            if (mb.Pressed) HandleViewportPress(wn, localPos);
                            else HandleViewportRelease(wn, localPos);
                            break;
                        case MouseButton.Right:
                            _orbiting = mb.Pressed;
                            _lastOrbitPos = localPos;
                            break;
                        case MouseButton.WheelUp:
                            _camDist = Mathf.Clamp(_camDist * 0.9f, 1f, 200f);
                            break;
                        case MouseButton.WheelDown:
                            _camDist = Mathf.Clamp(_camDist * 1.1f, 1f, 200f);
                            break;
                    }
                    break;
                case InputEventMouseMotion mm:
                    // Continuous orbit / gizmo drags are driven by per-frame
                    // polling (see Update) for responsiveness — motion events
                    // here are unnecessary and can be batched by the host.
                    break;
            }
        }

        /// <summary>Recompute the editor camera element's transform from the
        /// orbit state (yaw / pitch / distance / focus). The world owns truth —
        /// the element is a V12 element with a CameraComponent — and the render
        /// node is driven directly below so orbit input renders immediately
        /// instead of waiting on the 60Hz worker snapshot.</summary>
        private void UpdateCamera()
        {
            if (_editorCam == null) return;
            float cp = Mathf.DegToRad(_camPitch);
            float cy = Mathf.DegToRad(_camYaw);
            var dir = new Vector3(Mathf.Cos(cp) * Mathf.Cos(cy), Mathf.Sin(cp), Mathf.Cos(cp) * Mathf.Sin(cy));
            var pos = _camFocus - dir * _camDist;
            var t = new Transform3D(new Basis(), pos).LookingAt(_camFocus, Vector3.Up);
            var q = t.Basis.GetRotationQuaternion();
            _editorCam.LocalTransform = new TRS
            {
                Position = new System.Numerics.Vector3(pos.X, pos.Y, pos.Z),
                Rotation = new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W),
                Scale = System.Numerics.Vector3.One
            };

            // Drive the render node directly (zero latency): the worker's 60Hz
            // snapshot + interpolation pipeline lags fast orbit input, so the
            // camera trails behind the mouse unless you move slowly. The world
            // still owns truth (LocalTransform above); this just renders it
            // immediately instead of batching it through the snapshot.
            if (_renderer.GetNode(_editorCam.Id) is Camera3D camNode)
                camNode.Transform = t;
        }

        /// <summary>World-space position of an element (System.Numerics →
        /// Godot).</summary>
        private static Vector3 ElementWorldPosGodot(IWorldElement el)
        {
            var m = el.WorldTransform;
            return new Vector3(m.Translation.X, m.Translation.Y, m.Translation.Z);
        }

        private void HandleViewportPress(WidgetNode wn, Vector2 pos)
        {
            // Grab a gizmo handle first — a gizmo drag consumes the press.
            if (_gizmo.HandleMouseDown(pos, wn.ScreenViewport)) return;

            // Gizmo parts are grabbable, never selectable: clicks pass through.
            long id = _renderer.PickElement(wn.ElementId, pos, _gizmo.IsGizmoPart);
            if (id != 0)
            {
                var el = _gameRoot?.FindElement(e => e.Id == id);
                if (el != null)
                {
                    _selectHandler?.Invoke(el);
                    SetGizmoTarget(el);
                }
            }
            else
            {
                _selectHandler?.Invoke(null);
                SetGizmoTarget(null);
            }
        }

        private void HandleViewportRelease(WidgetNode wn, Vector2 pos) => _gizmo.HandleMouseUp();

        public void OnInputEvent(V12.Core.Input.InputEvent evt)
        {
            if (evt.Name != "interact") return;
            if (evt.Type == InputEventType.ButtonDown)
            {
                _interactRequested = true;
                _interactHeld = true;
            }
            else if (evt.Type == InputEventType.ButtonUp)
            {
                _interactHeld = false;
                _grabbedWidget = null;
                _grabCanvas = null;
            }
        }

        private void EnsureInputRegistered()
        {
            if (_inputRegistered || _gameRoot == null) return;
            var input = _gameRoot.Registry.Get<InputService>();
            if (input == null) return;
            input.RegisterHandler(this);
            _inputRegistered = true;
        }

        /// <summary>
        /// Called once per frame. Casts the player's aim ray against every
        /// world-space canvas, tracks the hovered widget (with visual feedback)
        /// and handles presses/drags.
        /// </summary>
        private void UpdateInteraction()
        {
            if (_gameRoot == null) return;

            var playerEl = _gameRoot.FindElementWithComponent<V12.Basic.Components.PlayerComponent>();
            if (playerEl == null)
            {
                SetHover(null);
                return;
            }
            var aim = playerEl.GetComponent<V12.Basic.Components.PlayerComponent>().GetAimRay();
            var origin = new Vector3(aim.origin.X, aim.origin.Y, aim.origin.Z);
            var dir = new Vector3(aim.direction.X, aim.direction.Y, aim.direction.Z);

            // While a slider is grabbed, keep dragging it even if the aim ray
            // drifts off the widget (same feel as a mouse-held scrollbar).
            if (_interactHeld && _grabbedWidget != null && _grabCanvas != null)
            {
                if (RaycastCanvas(_grabCanvas, origin, dir, RayLength, out var dragLocal))
                    SetSliderFromLocal(_grabbedWidget, dragLocal);
                else if (Mathf.Abs(_grabCanvas.Root3D.GlobalPosition.DistanceTo(origin)) > RayLength)
                    SetSliderFromLocal(_grabbedWidget, new Vector2(_grabCanvas.Width, 0));
                return;
            }

            var hovered = PickWidget(origin, dir, out var localPoint, out var canvas);
            SetHover(hovered);

            if (!_interactRequested || hovered == null) return;
            _interactRequested = false;
            PressWidget(hovered, canvas, localPoint);
        }

        /// <summary>
        /// Raycast the aim ray against all world canvases and return the first
        /// interactive widget hit (button, toggle, checkbox or slider).
        /// </summary>
        private WidgetNode? PickWidget(Vector3 origin, Vector3 dir, out Vector2 localPoint, out CanvasNode? canvas)
        {
            localPoint = default;
            canvas = null;
            foreach (var cn in _canvases.Values)
            {
                if (cn.ScreenSpace || cn.Root3D == null) continue;
                if (!RaycastCanvas(cn, origin, dir, RayLength, out var local)) continue;
                var wn = HitTestWidget(cn, local);
                if (wn != null)
                {
                    canvas = cn;
                    localPoint = local;
                    return wn;
                }
            }
            return null;
        }

        private static bool RaycastCanvas(CanvasNode cn, Vector3 origin, Vector3 dir, float maxDist, out Vector2 localPoint)
        {
            localPoint = default;
            var xform = cn.Root3D.GlobalTransform;
            var normal = xform.Basis.Z; // +Z local → world
            float denom = normal.Dot(dir);
            if (Mathf.Abs(denom) < 1e-6f) return false;
            float t = (xform.Origin - origin).Dot(normal) / denom;
            if (t < 0f || t > maxDist) return false;
            var hit = origin + dir * t;
            var local = xform.Basis.Inverse() * (hit - xform.Origin);
            if (Mathf.Abs(local.X) > cn.Width / 2f || Mathf.Abs(local.Y) > cn.Height / 2f) return false;
            localPoint = new Vector2(local.X, local.Y);
            return true;
        }

        private static bool IsInteractive(string kind) =>
            kind is "button" or "toggle" or "checkbox" or "slider";

        private static WidgetNode? HitTestWidget(CanvasNode cn, Vector2 localPoint)
        {
            foreach (var wn in cn.Widgets.Values)
            {
                if (!IsInteractive(wn.Kind) || wn.Root3D == null || wn.Element == null) continue;
                var pos = wn.Root3D.Position;
                var size = Measure(wn.Element);
                if (Mathf.Abs(localPoint.X - pos.X) <= size.X / 2f && Mathf.Abs(localPoint.Y - pos.Y) <= size.Y / 2f)
                    return wn;
            }
            return null;
        }

        private void SetHover(WidgetNode? wn)
        {
            if (_hoveredWidget == wn) return;
            if (_hoveredWidget != null)
                ApplyHoverVisual(_hoveredWidget, false);
            _hoveredWidget = wn;
            if (_hoveredWidget != null)
                ApplyHoverVisual(_hoveredWidget, true);
        }

        private static void ApplyHoverVisual(WidgetNode wn, bool hovered)
        {
            switch (wn.Kind)
            {
                case "button":
                    if (wn.RectMat != null)
                        wn.RectMat.AlbedoColor = hovered
                            ? new Color(0.35f, 0.58f, 0.98f)
                            : new Color(0.22f, 0.45f, 0.85f);
                    break;
                case "toggle":
                case "checkbox":
                    if (wn.TrackMat != null)
                        wn.TrackMat.AlbedoColor = hovered
                            ? new Color(0.38f, 0.38f, 0.46f)
                            : new Color(0.25f, 0.25f, 0.3f);
                    break;
                case "slider":
                    if (wn.HandleMat != null)
                        wn.HandleMat.AlbedoColor = hovered
                            ? new Color(1f, 1f, 1f)
                            : new Color(0.85f, 0.85f, 0.9f);
                    break;
            }
        }

        private void PressWidget(WidgetNode wn, CanvasNode? cn, Vector2 localPoint)
        {
            switch (wn.Kind)
            {
                case "button":
                    InvokeClick(wn);
                    break;
                case "toggle":
                {
                    var t = wn.Element?.GetComponent<ToggleComponent>();
                    if (t != null)
                    {
                        t.IsOn = !t.IsOn;
                        try { t.OnToggled?.Invoke(t.IsOn); } catch { }
                        if (cn != null) ApplyVisualState(cn, wn, wn.Element);
                    }
                    break;
                }
                case "checkbox":
                {
                    var c = wn.Element?.GetComponent<CheckboxComponent>();
                    if (c != null)
                    {
                        c.Checked = !c.Checked;
                        try { c.OnChanged?.Invoke(c.Checked); } catch { }
                        if (cn != null) ApplyVisualState(cn, wn, wn.Element);
                    }
                    break;
                }
                case "slider":
                    _grabbedWidget = wn;
                    _grabCanvas = cn;
                    SetSliderFromLocal(wn, localPoint);
                    break;
            }
        }

        private void SetSliderFromLocal(WidgetNode wn, Vector2 localPoint)
        {
            var s = wn.Element?.GetComponent<SliderComponent>();
            if (s == null) return;
            var size = Measure(wn.Element);
            float t = Mathf.Clamp((localPoint.X + size.X / 2f) / Mathf.Max(size.X, 0.0001f), 0f, 1f);
            float value = s.Min + t * (s.Max - s.Min);
            if (s.Step > 0f)
                value = Mathf.Round(value / s.Step) * s.Step;
            value = Mathf.Clamp(value, s.Min, s.Max);
            if (Mathf.Abs(value - s.Value) > 0.0001f)
            {
                s.Value = value;
                try { s.OnChanged?.Invoke(value); } catch { }
            }
        }

        private static void InvokeClick(WidgetNode wn)
        {
            if (wn?.Element != null)
                InvokeClick(wn.Element);
        }

        /// <summary>Invoke the click handler of an element's ButtonComponent
        /// (UI or legacy). Used by buttons and tree rows.</summary>
        private static void InvokeClick(IWorldElement? el)
        {
            if (el == null) return;
            var uiBtn = el.GetComponent<ButtonComponent>();
            if (uiBtn != null)
            {
                uiBtn.InvokeClick();
                return;
            }
            var legacy = el.GetComponent<V12.Components.ButtonComponent>();
            if (legacy != null)
            {
                legacy.Pressed = true;
                legacy.OnPressed?.Invoke();
            }
        }

        // ── Tree reconciliation (TreeComponent → Godot Tree) ──────────────

        /// <summary>
        /// Mirror an element's child tree onto a Godot Tree control: each
        /// descendant element becomes a TreeItem nested by its element nesting,
        /// so the V12 hierarchy shows with native collapse arrows and
        /// indentation. A signature over ids/names/styles gates the rebuild —
        /// when the structure is unchanged the existing items (and their
        /// collapse/scroll state) are left alone.
        /// </summary>
        private void ReconcileTree(WidgetNode wn, IWorldElement el)
        {
            var tree = wn.ScreenTree;
            if (tree == null) return;

            var sb = new System.Text.StringBuilder();
            foreach (var child in el.Children)
                TreeSig(sb, child);
            string sig = sb.ToString();
            if (sig == wn.TreeSignature) return;
            wn.TreeSignature = sig;

            tree.Clear();
            wn.TreeItems.Clear();
            wn.TreeElements.Clear();
            foreach (var child in el.Children)
                BuildTreeItem(wn, tree, child, null);
        }

        private static void TreeSig(System.Text.StringBuilder sb, IWorldElement el)
        {
            if (el.GetComponent<CanvasComponent>() != null) return;
            // Editor-transient subtrees (gizmo handles) never appear.
            if (V12.Components.EditorTransientComponent.IsTransient(el)) return;
            sb.Append(el.Id).Append('|').Append(TreeRowText(el)).Append('|')
              .Append(el.GetComponent<UIStyleComponent>()?.StyleHint ?? "").Append(';');
            foreach (var child in el.Children)
                TreeSig(sb, child);
        }

        private void BuildTreeItem(WidgetNode wn, Tree tree, IWorldElement el, TreeItem? parent)
        {
            if (el.GetComponent<CanvasComponent>() != null) return;
            // Editor-transient subtrees (gizmo handles) never appear.
            if (V12.Components.EditorTransientComponent.IsTransient(el)) return;
            var item = parent != null ? tree.CreateItem(parent) : tree.CreateItem();
            wn.TreeItems[el.Id] = item;
            wn.TreeElements[el.Id] = el;
            item.SetMetadata(0, el.Id);
            item.SetText(0, TreeRowText(el));
            var hint = el.GetComponent<UIStyleComponent>()?.StyleHint;
            item.SetCustomColor(0, hint switch
            {
                "selected" or "accent" => new Color(0.45f, 0.72f, 1f),
                "muted" => UIStyles.TextDim,
                "danger" => UIStyles.Danger,
                _ => UIStyles.Text
            });
            foreach (var child in el.Children)
                BuildTreeItem(wn, tree, child, item);
        }

        private static string TreeRowText(IWorldElement el)
        {
            var lbl = el.GetComponent<LabelComponent>();
            if (lbl != null && !string.IsNullOrEmpty(lbl.Text)) return lbl.Text;
            var btn = el.GetComponent<ButtonComponent>();
            if (btn != null && !string.IsNullOrEmpty(btn.Label)) return btn.Label;
            var legacy = el.GetComponent<V12.Components.ButtonComponent>();
            if (legacy != null && !string.IsNullOrEmpty(legacy.Label)) return legacy.Label;
            return el.Name ?? "<unnamed>";
        }

        private void OnTreeItemSelected(WidgetNode wn)
        {
            var sel = wn.ScreenTree?.GetSelected();
            if (sel == null) return;
            long id = sel.GetMetadata(0).AsInt64();
            if (wn.TreeElements.TryGetValue(id, out var el) && el != null)
                InvokeClick(el);
        }

        private void CollectCanvases(IWorldElement el, HashSet<long> found)
        {
            var canvas = el.GetComponent<CanvasComponent>();
            if (canvas != null)
            {
                found.Add(el.Id);
                if (_canvases.TryGetValue(el.Id, out var cn))
                    UpdateCanvas(cn, el);
                else
                    _canvases[el.Id] = CreateCanvas(el);
            }

            foreach (var child in el.Children)
                CollectCanvases(child, found);
        }

        // ── Canvas lifecycle ──────────────────────────────────────────────

        private CanvasNode CreateCanvas(IWorldElement el)
        {
            var cn = new CanvasNode { ElementId = el.Id };
            var canvas = el.GetComponent<CanvasComponent>();

            if (canvas.ScreenSpace)
            {
                cn.ScreenSpace = true;
                cn.RootControl = new Control { Name = "UICanvas_" + el.Id };
                cn.RootControl.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
                ScreenLayer().AddChild(cn.RootControl);

                // Opaque backdrop: hides the 3D world (Godot's fallback camera
                // renders the sky when no Camera3D is current) behind the canvas
                // until a world camera exists. Toggled each frame in UpdateCanvas.
                cn.ScreenBackground = new ColorRect { Color = new Color(0.09f, 0.095f, 0.115f) };
                cn.ScreenBackground.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                cn.RootControl.AddChild(cn.ScreenBackground);

                cn.LayoutBox = new VBoxContainer { Name = "Layout" };
                // The canvas RootControl gets an explicit Size in UpdateCanvas;
                // FullRect anchors make the layout box fill it so widgets inside
                // actually stack instead of piling at (0,0).
                cn.LayoutBox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                cn.RootControl.AddChild(cn.LayoutBox);
            }
            else
            {
                cn.ScreenSpace = false;
                cn.Root3D = new Node3D { Name = "UICanvas_" + el.Id };
                _host.AddChild(cn.Root3D);

                // Subtle backing panel so an otherwise-floating canvas reads as a "screen".
                cn.Panel = MakeQuad(new Vector2(cn.Width, cn.Height), new Color(0.1f, 0.1f, 0.12f, 0.55f));
                cn.Panel.Position = new Vector3(0, 0, -0.01f);
                cn.Root3D.AddChild(cn.Panel);
            }

            GD.Print($"[WorldCanvas] {(cn.ScreenSpace ? "screen-space" : "world-space")} canvas '{el.Name}' (id={el.Id}) attached.");

            UpdateCanvas(cn, el);
            return cn;
        }

        private void UpdateCanvas(CanvasNode cn, IWorldElement el)
        {
            var canvas = el.GetComponent<CanvasComponent>();
            if (canvas == null) return;

            if (!canvas.ScreenSpace)
            {
                // World-space canvases size from the element's scale (in metres).
                cn.Width = 2f;
                cn.Height = 1.2f;
                var sc = el.GetComponent<V12.Components.ScaleComponent>();
                if (sc != null && sc.ScaleX > 0.01f && sc.ScaleY > 0.01f)
                {
                    cn.Width = sc.ScaleX;
                    cn.Height = sc.ScaleY;
                }
                cn.Root3D.Transform = WorldTransformNoScale(el);

                if (cn.Panel != null)
                {
                    cn.Panel.Scale = new Vector3(cn.Width, cn.Height, 1);
                    if (cn.Panel.MaterialOverride is StandardMaterial3D mat)
                    {
                        mat.AlbedoColor = new Color(0.1f, 0.1f, 0.12f, 0.55f);
                        mat.AlbedoTexture = null;
                    }
                }
            }
            else
            {
                if (cn.RootControl != null)
                {
                    var viewportSize = _host.GetViewport().GetVisibleRect().Size;
                    cn.Width = canvas.Width > 0 ? canvas.Width : viewportSize.X;
                    cn.Height = canvas.Height > 0 ? canvas.Height : viewportSize.Y;
                    cn.RootControl.Position = new Vector2(
                        viewportSize.X * canvas.AnchorX - cn.Width * canvas.AnchorX,
                        viewportSize.Y * canvas.AnchorY - cn.Height * canvas.AnchorY);
                    cn.RootControl.Size = new Vector2(cn.Width, cn.Height);
                }
                // Show the opaque backdrop only while the MAIN screen (viewport 0)
                // has no current camera. Cameras inside UI SubViewports (an editor
                // GameView) don't count — their scene renders in the panel, and the
                // main screen still needs the backdrop to hide Godot's fallback
                // camera/sky behind the editor chrome.
                if (cn.ScreenBackground != null)
                    cn.ScreenBackground.Visible = !_renderer.HasCurrentCamera(0);
            }

            // Reconcile widgets under this canvas.
            var widgets = new List<IWorldElement>();
            CollectWidgets(el, widgets);

            var seen = new HashSet<long>();
            foreach (var w in widgets)
            {
                seen.Add(w.Id);
                if (!cn.Widgets.TryGetValue(w.Id, out var wn))
                {
                    wn = new WidgetNode { ElementId = w.Id, Element = w };
                    if (cn.ScreenSpace)
                    {
                        // RootControl is assigned by BuildScreenVisuals (it IS the
                        // real Label/Button/… or the HBox/VBox layout container),
                        // then the reparent pass below inserts it into the right
                        // container.
                    }
                    else
                    {
                        wn.Root3D = new Node3D { Name = "UI_" + w.Id };
                        cn.Root3D.AddChild(wn.Root3D);
                    }
                    cn.Widgets[w.Id] = wn;
                }
                UpdateWidget(cn, wn, w);
            }

            // ── Screen-space reparent pass ──
            // Now that all layout containers (ScreenBox) have been built,
            // reparent each widget's RootControl under the correct container.
            if (cn.ScreenSpace)
            {
                foreach (var w in widgets)
                {
                    if (!cn.Widgets.TryGetValue(w.Id, out var wn)) continue;
                    if (wn.RootControl == null) continue;
                    if (wn.RootControl.GetParent() != null) continue; // already parented

                    // Find the nearest ancestor that is a layout widget with a ScreenBox.
                    Container targetContainer = cn.LayoutBox;
                    var ancestor = w.Parent;
                    while (ancestor != null)
                    {
                        if (cn.Widgets.TryGetValue(ancestor.Id, out var ancestorWn)
                            && ancestorWn.ScreenBox != null)
                        {
                            targetContainer = ancestorWn.ScreenBox;
                            break;
                        }
                        ancestor = ancestor.Parent;
                    }
                    targetContainer.AddChild(wn.RootControl);
                }
            }

            var toRemove = new List<long>();
            foreach (var kvp in cn.Widgets)
            {
                if (!seen.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var id in toRemove)
            {
                if (_hoveredWidget?.ElementId == id)
                    SetHover(null);
                if (_grabbedWidget?.ElementId == id)
                {
                    _grabbedWidget = null;
                    _grabCanvas = null;
                }
                var freed = cn.Widgets[id];
                if (freed.ScreenViewport != null)
                {
                    _renderer.UnregisterSceneViewport(freed.ElementId);
                    // Re-capture so nodes previously inside the viewport reparent
                    // back to the main scene.
                    _gameRoot?.MarkRenderDirty();
                }
                freed.Free(cn.ScreenSpace);
                cn.Widgets.Remove(id);
            }

            // ── Per-frame viewport upkeep ──
            // The fallback editor camera is Current only while the world doesn't
            // provide its own current camera for this viewport.
            if (cn.ScreenSpace)
            {
                foreach (var w in widgets)
                {
                    if (!cn.Widgets.TryGetValue(w.Id, out var wn)) continue;
                    if (wn.ScreenFallbackCamera == null) continue;
                    wn.ScreenFallbackCamera.Current = !_renderer.HasCurrentCamera(w.Id);
                }
            }

            // ── Per-frame tree upkeep: mirror the tree element's children onto
            // the Godot Tree control (signature-gated inside).
            if (cn.ScreenSpace)
            {
                foreach (var w in widgets)
                {
                    if (!cn.Widgets.TryGetValue(w.Id, out var wn)) continue;
                    if (wn.ScreenTree == null) continue;
                    ReconcileTree(wn, w);
                }
            }

            // Lay out widgets once their sizes are known.
            if (!cn.ScreenSpace)
                LayoutElement(cn, el, Vector2.Zero);
            else
                cn.LayoutBox.QueueSort();
        }

        private void CollectWidgets(IWorldElement el, List<IWorldElement> outWidgets)
        {
            foreach (var child in el.Children)
            {
                // A nested canvas is an independent surface, not a widget of this one.
                if (child.GetComponent<CanvasComponent>() != null)
                    continue;
                if (HasAnyUiComponent(child))
                    outWidgets.Add(child);
                // Tree containers own their children as TreeItems — they are not
                // widgets and must not be collected (or reparented) as controls.
                if (child.GetComponent<TreeComponent>() != null)
                    continue;
                CollectWidgets(child, outWidgets);
            }
        }

        private static bool HasAnyUiComponent(IWorldElement el)
        {
            return el.GetComponent<LabelComponent>() != null
                || el.GetComponent<ButtonComponent>() != null
                || el.GetComponent<V12.Components.ButtonComponent>() != null
                || el.GetComponent<RectComponent>() != null
                || el.GetComponent<ProgressBarComponent>() != null
                || el.GetComponent<ToggleComponent>() != null
                || el.GetComponent<CheckboxComponent>() != null
                || el.GetComponent<SliderComponent>() != null
                || el.GetComponent<TextInputComponent>() != null
                || el.GetComponent<ImageComponent>() != null
                || el.GetComponent<IconComponent>() != null
                || el.GetComponent<HLayoutComponent>() != null
                || el.GetComponent<VLayoutComponent>() != null
                || el.GetComponent<ScrollComponent>() != null
                || el.GetComponent<ViewportComponent>() != null
                || el.GetComponent<SplitterComponent>() != null
                || el.GetComponent<TreeComponent>() != null;
        }

        // ── Widget rendering ──────────────────────────────────────────────

        private void UpdateWidget(CanvasNode cn, WidgetNode wn, IWorldElement el)
        {
            string kind = KindOf(el);
            if (wn.Kind != kind)
            {
                wn.FreeVisuals(cn.ScreenSpace);
                wn.Kind = kind;
                BuildVisuals(cn, wn, el);
            }
            ApplyVisualState(cn, wn, el);
        }

        private static string KindOf(IWorldElement el)
        {
            if (el.GetComponent<LabelComponent>() != null) return "label";
            if (el.GetComponent<ButtonComponent>() != null) return "button";
            if (el.GetComponent<V12.Components.ButtonComponent>() != null) return "button";
            if (el.GetComponent<RectComponent>() != null) return "rect";
            if (el.GetComponent<ProgressBarComponent>() != null) return "progress";
            if (el.GetComponent<ToggleComponent>() != null) return "toggle";
            if (el.GetComponent<CheckboxComponent>() != null) return "checkbox";
            if (el.GetComponent<SliderComponent>() != null) return "slider";
            if (el.GetComponent<TextInputComponent>() != null) return "textinput";
            if (el.GetComponent<ImageComponent>() != null) return "image";
            if (el.GetComponent<IconComponent>() != null) return "icon";
            // Tree containers own their children (TreeItems), not widgets.
            if (el.GetComponent<TreeComponent>() != null) return "tree";
            // Viewport must win over layout components — an element like an
            // editor's GameView carries both a VLayoutComponent (panel sizing)
            // and a ViewportComponent (3D scene). Only the viewport build path
            // creates the SubViewport; treating it as a layout container would
            // render its camera/content onto the main screen.
            if (el.GetComponent<ViewportComponent>() != null) return "viewport";
            if (el.GetComponent<SplitterComponent>() != null) return "splitter";
            if (el.GetComponent<ScrollComponent>() != null) return "scroll";
            if (el.GetComponent<HLayoutComponent>() != null) return "hlayout";
            if (el.GetComponent<VLayoutComponent>() != null) return "vlayout";
            return "container";
        }

        private void BuildVisuals(CanvasNode cn, WidgetNode wn, IWorldElement el)
        {
            if (cn.ScreenSpace)
                BuildScreenVisuals(cn, wn, el);
            else
                BuildWorldVisuals(cn, wn, el);
        }

        // ── World-space visuals ───────────────────────────────────────────

        private void BuildWorldVisuals(CanvasNode cn, WidgetNode wn, IWorldElement el)
        {
            string kind = wn.Kind;
            var size = Measure(el);

            switch (kind)
            {
                case "label":
                {
                    var label = new Label3D { PixelSize = PixelScale, Position = new Vector3(0, 0, 0.005f) };
                    wn.Label3D = label;
                    wn.Root3D.AddChild(label);
                    break;
                }
                case "rect":
                {
                    var mat = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0.2f, 0.2f, 0.25f)
                    };
                    wn.Rect = MakeQuad(size, Colors.White);
                    wn.Rect.MaterialOverride = mat;
                    wn.RectMat = mat;
                    wn.Root3D.AddChild(wn.Rect);
                    break;
                }
                case "button":
                {
                    wn.RectMat = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0.22f, 0.45f, 0.85f)
                    };
                    wn.Rect = MakeQuad(size, Colors.White);
                    wn.Rect.MaterialOverride = wn.RectMat;
                    wn.Root3D.AddChild(wn.Rect);

                    wn.Label3D = new Label3D { PixelSize = PixelScale, Position = new Vector3(0, 0, 0.005f) };
                    wn.Root3D.AddChild(wn.Label3D);
                    break;
                }
                case "progress":
                {
                    wn.TrackMat = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0.18f, 0.18f, 0.2f)
                    };
                    wn.Track = MakeQuad(size, Colors.White);
                    wn.Track.MaterialOverride = wn.TrackMat;
                    wn.Root3D.AddChild(wn.Track);

                    wn.FillMat = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0.25f, 0.8f, 0.35f)
                    };
                    wn.Fill = MakeQuad(size, Colors.White);
                    wn.Fill.MaterialOverride = wn.FillMat;
                    wn.Root3D.AddChild(wn.Fill);
                    break;
                }
                case "toggle":
                case "checkbox":
                {
                    var boxSize = new Vector2(0.05f, 0.05f);
                    wn.TrackMat = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0.25f, 0.25f, 0.3f)
                    };
                    wn.Track = MakeQuad(boxSize, Colors.White);
                    wn.Track.MaterialOverride = wn.TrackMat;
                    wn.Track.Position = new Vector3(-size.X / 2 + 0.05f, 0, 0);
                    wn.Root3D.AddChild(wn.Track);

                    wn.FillMat = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0.25f, 0.8f, 0.35f)
                    };
                    wn.Fill = MakeQuad(boxSize, Colors.White);
                    wn.Fill.MaterialOverride = wn.FillMat;
                    wn.Fill.Position = new Vector3(-size.X / 2 + 0.05f, 0, 0.001f);
                    wn.Root3D.AddChild(wn.Fill);

                    wn.Label3D = new Label3D { PixelSize = PixelScale };
                    wn.Label3D.Position = new Vector3(0.09f, 0, 0.005f);
                    wn.Root3D.AddChild(wn.Label3D);
                    break;
                }
                case "slider":
                {
                    wn.TrackMat = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0.2f, 0.2f, 0.25f)
                    };
                    wn.Track = MakeQuad(size, Colors.White);
                    wn.Track.MaterialOverride = wn.TrackMat;
                    wn.Root3D.AddChild(wn.Track);

                    wn.HandleMat = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        AlbedoColor = new Color(0.85f, 0.85f, 0.9f)
                    };
                    wn.Handle = MakeQuad(new Vector2(0.05f, size.Y + 0.02f), Colors.White);
                    wn.Handle.MaterialOverride = wn.HandleMat;
                    wn.Root3D.AddChild(wn.Handle);
                    break;
                }
                case "textinput":
                {
                    wn.RectMat = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0.15f, 0.15f, 0.2f)
                    };
                    wn.Rect = MakeQuad(size, Colors.White);
                    wn.Rect.MaterialOverride = wn.RectMat;
                    wn.Root3D.AddChild(wn.Rect);

                    wn.Label3D = new Label3D { PixelSize = PixelScale, Position = new Vector3(0, 0, 0.005f) };
                    wn.Root3D.AddChild(wn.Label3D);
                    break;
                }
                case "image":
                case "icon":
                {
                    wn.Rect = MakeQuad(size, Colors.White);
                    wn.Root3D.AddChild(wn.Rect);
                    break;
                }
                default:
                    break;
            }
        }

        private void ApplyWorldVisualState(WidgetNode wn, IWorldElement el, string kind)
        {
            switch (kind)
            {
                case "label":
                {
                    var l = el.GetComponent<LabelComponent>();
                    if (wn.Label3D != null)
                    {
                        wn.Label3D.Text = l?.Text ?? "";
                        ApplyStyle(el, wn.Label3D, l?.FontSize ?? 16f);
                    }
                    break;
                }
                case "rect":
                {
                    var r = el.GetComponent<RectComponent>();
                    if (wn.RectMat != null)
                        wn.RectMat.AlbedoColor = ParseColor(r?.BackgroundColor, new Color(0.25f, 0.25f, 0.3f));
                    break;
                }
                case "button":
                {
                    var b = el.GetComponent<ButtonComponent>();
                    var legacy = el.GetComponent<V12.Components.ButtonComponent>();
                    string label = b?.Label ?? legacy?.Label ?? "Button";
                    if (wn.Label3D != null) wn.Label3D.Text = label;
                    if (wn.RectMat != null)
                    {
                        bool pressed = legacy?.Pressed ?? false;
                        if (wn.Hovered)
                            wn.RectMat.AlbedoColor = new Color(0.35f, 0.58f, 0.98f);
                        else if (pressed)
                            wn.RectMat.AlbedoColor = new Color(0.1f, 0.3f, 0.6f);
                        else
                            wn.RectMat.AlbedoColor = new Color(0.22f, 0.45f, 0.85f);
                    }
                    break;
                }
                case "progress":
                {
                    var p = el.GetComponent<ProgressBarComponent>();
                    float v = Mathf.Clamp(p?.Value ?? 0f, 0f, 1f);
                    var size = Measure(el);
                    if (wn.Fill != null)
                        wn.Fill.Scale = new Vector3(size.X * v, size.Y, 1);
                    if (wn.Fill != null)
                        wn.Fill.Position = new Vector3(-size.X / 2 + size.X * v / 2, 0, 0.001f);
                    break;
                }
                case "toggle":
                case "checkbox":
                {
                    bool on = kind == "toggle"
                        ? (el.GetComponent<ToggleComponent>()?.IsOn ?? false)
                        : (el.GetComponent<CheckboxComponent>()?.Checked ?? false);
                    if (wn.Fill != null)
                        wn.Fill.Visible = on;
                    var text = kind == "toggle"
                        ? (el.GetComponent<ToggleComponent>()?.Label ?? "")
                        : (el.GetComponent<CheckboxComponent>()?.Label ?? "");
                    if (wn.Label3D != null) wn.Label3D.Text = text;
                    break;
                }
                case "slider":
                {
                    var s = el.GetComponent<SliderComponent>();
                    var size = Measure(el);
                    float t = s != null && s.Max > s.Min ? (s.Value - s.Min) / (s.Max - s.Min) : 0f;
                    t = Mathf.Clamp(t, 0f, 1f);
                    if (wn.Handle != null)
                        wn.Handle.Position = new Vector3(-size.X / 2 + size.X * t, 0, 0.001f);
                    if (wn.HandleMat != null)
                        wn.HandleMat.AlbedoColor = wn.Hovered
                            ? new Color(1f, 1f, 1f)
                            : new Color(0.85f, 0.85f, 0.9f);
                    break;
                }
                case "textinput":
                {
                    var ti = el.GetComponent<TextInputComponent>();
                    string shown = string.IsNullOrEmpty(ti?.Value) ? (ti?.Placeholder ?? "") : ti.Value;
                    if (wn.Label3D != null) wn.Label3D.Text = shown;
                    if (wn.Label3D != null)
                        wn.Label3D.Modulate = string.IsNullOrEmpty(ti?.Value)
                            ? new Color(0.6f, 0.6f, 0.6f)
                            : Colors.White;
                    break;
                }
                case "image":
                case "icon":
                {
                    string src = kind == "image"
                        ? (el.GetComponent<ImageComponent>()?.Source ?? "")
                        : "";
                    if (!string.IsNullOrEmpty(src) && wn.Rect != null
                        && wn.Rect.MaterialOverride is StandardMaterial3D imgMat && imgMat.AlbedoTexture == null)
                    {
                        var tex = LoadTexture(src);
                        if (tex != null)
                            imgMat.AlbedoTexture = tex;
                    }
                    break;
                }
            }
        }

        // ── Screen-space visuals (Godot Control overlay) ──────────────────

        /// <summary>Shared dark palette for the screen-space UI.</summary>
        private static class UIStyles
        {
            public static readonly Color PanelBg    = new(0.13f, 0.135f, 0.16f);
            public static readonly Color PanelBorder= new(0.24f, 0.245f, 0.29f);
            public static readonly Color RowBg      = new(0.16f, 0.165f, 0.195f);
            public static readonly Color RowHover   = new(0.20f, 0.21f, 0.25f);
            public static readonly Color Accent     = new(0.30f, 0.55f, 1.0f);
            public static readonly Color AccentHover= new(0.38f, 0.62f, 1.0f);
            public static readonly Color Danger     = new(0.78f, 0.32f, 0.32f);
            public static readonly Color DangerHover= new(0.88f, 0.4f, 0.4f);
            public static readonly Color Text       = new(0.88f, 0.89f, 0.93f);
            public static readonly Color TextDim    = new(0.55f, 0.57f, 0.63f);

            public static StyleBoxFlat Panel(float corner = 4f)
            {
                return new StyleBoxFlat
                {
                    BgColor = PanelBg,
                    BorderColor = PanelBorder,
                    BorderWidthLeft = 1, BorderWidthRight = 1,
                    BorderWidthTop = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = (int)corner, CornerRadiusTopRight = (int)corner,
                    CornerRadiusBottomLeft = (int)corner, CornerRadiusBottomRight = (int)corner
                };
            }

            public static StyleBoxFlat Button(Color bg, Color? hover = null, float corner = 3f)
            {
                var h = hover ?? bg;
                return new StyleBoxFlat
                {
                    BgColor = bg,
                    BorderColor = PanelBorder,
                    BorderWidthLeft = 1, BorderWidthRight = 1,
                    BorderWidthTop = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = (int)corner, CornerRadiusTopRight = (int)corner,
                    CornerRadiusBottomLeft = (int)corner, CornerRadiusBottomRight = (int)corner,
                    ContentMarginLeft = 10, ContentMarginRight = 10,
                    ContentMarginTop = 4, ContentMarginBottom = 4
                };
            }

            public static StyleBoxFlat Input()
            {
                return new StyleBoxFlat
                {
                    BgColor = new Color(0.09f, 0.095f, 0.115f),
                    BorderColor = new Color(0.2f, 0.22f, 0.27f),
                    BorderWidthLeft = 1, BorderWidthRight = 1,
                    BorderWidthTop = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                    CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
                    ContentMarginLeft = 8, ContentMarginRight = 8,
                    ContentMarginTop = 4, ContentMarginBottom = 4
                };
            }
        }

        /// <summary>Default flat look for a screen-space Button.</summary>
        private static void StyleButton(Button btn)
        {
            btn.AddThemeStyleboxOverride("normal", UIStyles.Button(UIStyles.RowBg));
            btn.AddThemeStyleboxOverride("hover", UIStyles.Button(UIStyles.RowHover));
            btn.AddThemeStyleboxOverride("pressed", UIStyles.Button(UIStyles.RowBg));
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            btn.AddThemeColorOverride("font_color", UIStyles.Text);
            btn.AddThemeColorOverride("font_hover_color", Colors.White);
            btn.AddThemeColorOverride("font_pressed_color", UIStyles.Text);
            btn.AddThemeColorOverride("font_focus_color", UIStyles.Text);
            btn.AddThemeConstantOverride("outline_size", 0);
            btn.ClipText = true;
        }

        /// <summary>
        /// Re-apply a Button's style from UIStyleComponent hints: "selected"
        /// (accent fill, used by hierarchy rows), "accent" (primary action),
        /// "danger" (destructive action). Called on build and every frame so
        /// toggling a hint (e.g. selecting a row) restyles immediately.
        /// </summary>
        private static void ApplyButtonStyle(Button btn, IWorldElement el)
        {
            if (btn == null || el == null) return;
            var hint = el.GetComponent<UIStyleComponent>()?.StyleHint;
            switch (hint)
            {
                case "selected":
                case "accent":
                    btn.AddThemeStyleboxOverride("normal", UIStyles.Button(UIStyles.Accent));
                    btn.AddThemeStyleboxOverride("hover", UIStyles.Button(UIStyles.AccentHover));
                    btn.AddThemeStyleboxOverride("pressed", UIStyles.Button(UIStyles.Accent));
                    btn.AddThemeColorOverride("font_color", Colors.White);
                    break;
                case "danger":
                    btn.AddThemeStyleboxOverride("normal", UIStyles.Button(UIStyles.Danger));
                    btn.AddThemeStyleboxOverride("hover", UIStyles.Button(UIStyles.DangerHover));
                    btn.AddThemeStyleboxOverride("pressed", UIStyles.Button(UIStyles.Danger));
                    btn.AddThemeColorOverride("font_color", Colors.White);
                    break;
                default:
                    btn.AddThemeStyleboxOverride("normal", UIStyles.Button(UIStyles.RowBg));
                    btn.AddThemeStyleboxOverride("hover", UIStyles.Button(UIStyles.RowHover));
                    btn.AddThemeStyleboxOverride("pressed", UIStyles.Button(UIStyles.RowBg));
                    btn.AddThemeColorOverride("font_color", UIStyles.Text);
                    break;
            }
        }

        private static void StyleLineEdit(LineEdit edit)
        {
            edit.AddThemeStyleboxOverride("normal", UIStyles.Input());
            edit.AddThemeStyleboxOverride("focus", UIStyles.Input());
            edit.AddThemeColorOverride("font_color", UIStyles.Text);
            edit.AddThemeColorOverride("font_placeholder_color", UIStyles.TextDim);
            edit.AddThemeColorOverride("caret_color", UIStyles.Accent);
        }

        private static void StyleProgress(ProgressBar bar)
        {
            bar.AddThemeStyleboxOverride("background", UIStyles.Button(new Color(0.09f, 0.095f, 0.115f)));
            bar.AddThemeStyleboxOverride("fill", UIStyles.Button(new Color(0.22f, 0.42f, 0.85f), corner: 2f));
        }

        private static void StyleToggle(BaseButton check)
        {
            check.AddThemeColorOverride("font_color", UIStyles.Text);
        }

        private static void StyleSlider(HSlider slider)
        {
            slider.AddThemeStyleboxOverride("slider", UIStyles.Button(new Color(0.2f, 0.22f, 0.27f), corner: 2f));
            slider.AddThemeStyleboxOverride("grabber_area", UIStyles.Button(new Color(0.3f, 0.55f, 1f), corner: 2f));
            slider.AddThemeStyleboxOverride("grabber_area_highlight", UIStyles.Button(new Color(0.38f, 0.62f, 1f), corner: 2f));
        }

        /// <summary>Apply LayoutElementComponent flexibility to any widget so a
        /// label/button can act as an expanding spacer inside an H/V box.</summary>
        private static void ApplySizeHints(Control control, IWorldElement el)
        {
            if (control == null || el == null) return;
            var le = el.GetComponent<LayoutElementComponent>();
            if (le == null) return;
            if (le.FlexibleWidth > 0)
                control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (le.FlexibleHeight > 0)
                control.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            if (le.MinWidth > 0 || le.PreferredWidth > 0)
                control.CustomMinimumSize = new Vector2(Mathf.Max(le.MinWidth, le.PreferredWidth), control.CustomMinimumSize.Y);
            if (le.MinHeight > 0 || le.PreferredHeight > 0)
                control.CustomMinimumSize = new Vector2(control.CustomMinimumSize.X, Mathf.Max(le.MinHeight, le.PreferredHeight));
        }

        private void BuildScreenVisuals(CanvasNode cn, WidgetNode wn, IWorldElement el)
        {
            string kind = wn.Kind;
            switch (kind)
            {
                case "label":
                    wn.ScreenLabel = new Label();
                    wn.RootControl = wn.ScreenLabel;
                    break;
                case "button":
                {
                    wn.ScreenButton = new Button();
                    StyleButton(wn.ScreenButton);
                    // Wire the native click to the V12 ButtonComponent — without
                    // this, screen-space buttons render but never invoke OnClick.
                    var btnEl = el;
                    wn.ScreenButton.Pressed += () => InvokeClick(btnEl);
                    wn.RootControl = wn.ScreenButton;
                    break;
                }
                case "rect":
                    wn.ScreenRect = new ColorRect();
                    wn.RootControl = wn.ScreenRect;
                    break;
                case "progress":
                    wn.ScreenProgress = new ProgressBar();
                    StyleProgress(wn.ScreenProgress);
                    wn.RootControl = wn.ScreenProgress;
                    break;
                case "toggle":
                {
                    wn.ScreenCheck = new CheckButton();
                    StyleToggle(wn.ScreenCheck);
                    // Wire UI → component so user input isn't clobbered by the
                    // per-frame component → UI push (same rule as text inputs).
                    var tg = el.GetComponent<ToggleComponent>();
                    if (tg != null)
                        wn.ScreenCheck.Toggled += on =>
                        {
                            if (tg.IsOn != on) tg.IsOn = on;
                            try { tg.OnToggled?.Invoke(on); } catch { }
                        };
                    wn.RootControl = wn.ScreenCheck;
                    break;
                }
                case "checkbox":
                {
                    wn.ScreenCheck = new CheckBox();
                    StyleToggle(wn.ScreenCheck);
                    var cb = el.GetComponent<CheckboxComponent>();
                    if (cb != null)
                        wn.ScreenCheck.Toggled += on =>
                        {
                            if (cb.Checked != on) cb.Checked = on;
                            try { cb.OnChanged?.Invoke(on); } catch { }
                        };
                    wn.RootControl = wn.ScreenCheck;
                    break;
                }
                case "slider":
                {
                    wn.ScreenSlider = new HSlider();
                    StyleSlider(wn.ScreenSlider);
                    var sl = el.GetComponent<SliderComponent>();
                    if (sl != null)
                        wn.ScreenSlider.ValueChanged += v =>
                        {
                            float f = (float)v;
                            if (Mathf.Abs(sl.Value - f) > 0.0001f) sl.Value = f;
                            try { sl.OnChanged?.Invoke(f); } catch { }
                        };
                    wn.RootControl = wn.ScreenSlider;
                    break;
                }
                case "textinput":
                {
                    wn.ScreenEdit = new LineEdit();
                    StyleLineEdit(wn.ScreenEdit);
                    var ti = el.GetComponent<TextInputComponent>();
                    if (ti != null)
                    {
                        // UI → component on every change and on blur: without
                        // this, the per-frame component → UI push (below) resets
                        // whatever the user typed because the component never
                        // learns about it.
                        wn.ScreenEdit.TextChanged += text => ti.InvokeChanged(text);
                        wn.ScreenEdit.FocusExited += () => ti.InvokeChanged(wn.ScreenEdit.Text);
                    }
                    wn.RootControl = wn.ScreenEdit;
                    break;
                }
                case "image":
                    wn.ScreenRect = new ColorRect { Color = new Color(0.25f, 0.25f, 0.3f) };
                    wn.RootControl = wn.ScreenRect;
                    break;
                case "viewport":
                {
                    var vp = el.GetComponent<ViewportComponent>();
                    // Plain SubViewport: all mouse interaction is routed through
                    // the container's gui_input (see HandleViewportContainerInput)
                    // — reliable in the embedded host, and gives us picking,
                    // gizmo drags and the orbit camera from one code path.
                    wn.ScreenViewport = new SubViewport
                    {
                        Name = "Viewport_" + el.Id,
                        TransparentBg = false,
                        RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                        HandleInputLocally = false
                    };

                    var bg = ParseColor(vp?.BackgroundColor, new Color(0.10f, 0.10f, 0.13f));

                    var env = new WorldEnvironment();
                    env.Environment = new global::Godot.Environment
                    {
                        BackgroundMode = global::Godot.Environment.BGMode.Color,
                        BackgroundColor = bg,
                        AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
                        AmbientLightColor = new Color(0.55f, 0.58f, 0.65f),
                        AmbientLightEnergy = 1.0f
                    };
                    wn.ScreenViewport.AddChild(env);

                    // Default directional light so unlit-ish scenes read clearly
                    // even before the world supplies its own lights.
                    var light = new DirectionalLight3D
                    {
                        RotationDegrees = new Vector3(-50f, -30f, 0f),
                        LightEnergy = 1.1f,
                        ShadowEnabled = true
                    };
                    wn.ScreenViewport.AddChild(light);

                    // Editor fallback camera: current only while the world has no
                    // camera of its own for this viewport (see UpdateCanvas).
                    wn.ScreenFallbackCamera = new Camera3D { Current = true };
                    wn.ScreenViewport.AddChild(wn.ScreenFallbackCamera);

                    wn.ScreenViewportContainer = new SubViewportContainer
                    {
                        Name = "ViewportContainer_" + el.Id,
                        Stretch = true,
                        MouseFilter = Control.MouseFilterEnum.Stop
                    };
                    wn.ScreenViewportContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    wn.ScreenViewportContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                    wn.ScreenViewportContainer.AddChild(wn.ScreenViewport);
                    // All viewport interaction (pick / gizmo / orbit) comes
                    // through here — positions are container-local pixels,
                    // matching the viewport's render size when Stretch is on.
                    wn.ScreenViewportContainer.GuiInput += evt => HandleViewportContainerInput(wn, evt);
                    wn.RootControl = wn.ScreenViewportContainer;
                    _pickViewport = wn;

                    _renderer.RegisterSceneViewport(el.Id, wn.ScreenViewport);
                    // Force a re-capture so static worlds reparent their nodes
                    // into the newly-registered viewport instead of waiting for
                    // the next dirty event.
                    _gameRoot?.MarkRenderDirty();
                    break;
                }
                case "hlayout":
                {
                    var hbox = new HBoxContainer { Name = "HBox_" + el.Id };
                    ConfigureScreenLayout(hbox, el);
                    wn.ScreenBox = hbox;
                    wn.RootControl = hbox;
                    break;
                }
                case "vlayout":
                {
                    var vbox = new VBoxContainer { Name = "VBox_" + el.Id };
                    ConfigureScreenLayout(vbox, el);
                    wn.ScreenBox = vbox;
                    wn.RootControl = vbox;
                    break;
                }
                case "splitter":
                {
                    var sp = el.GetComponent<SplitterComponent>();
                    bool horizontal = sp?.Orientation == SplitterOrientation.Horizontal;
                    SplitContainer split = horizontal
                        ? new HSplitContainer { Name = "HSplit_" + el.Id }
                        : new VSplitContainer { Name = "VSplit_" + el.Id };
                    split.AddThemeConstantOverride("separation", (int)(sp?.Spacing > 0 ? sp.Spacing : 4f));
                    // Splitters always fill their parent so child panels can
                    // stretch along both axes.
                    split.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    split.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                    wn.ScreenBox = split;
                    wn.RootControl = split;
                    break;
                }
                case "scroll":
                {
                    // Scrollable region: a fixed-size viewport Control (sized by
                    // the element's layout hints) wraps the ScrollContainer,
                    // which wraps the inner VBox that children attach to. A bare
                    // ScrollContainer inside a VBox would grow to its content's
                    // minimum height and never scroll.
                    var sv = el.GetComponent<ScrollComponent>();
                    var vcomp = el.GetComponent<VLayoutComponent>();
                    var hcomp = el.GetComponent<HLayoutComponent>();
                    float spacing = vcomp?.Spacing > 0 ? vcomp.Spacing : (hcomp?.Spacing > 0 ? hcomp.Spacing : 4f);
                    float padding = vcomp?.Padding > 0 ? vcomp.Padding : (hcomp?.Padding > 0 ? hcomp.Padding : 0f);

                    var viewport = new Control { Name = "ScrollBox_" + el.Id };
                    viewport.SetAnchorsPreset(Control.LayoutPreset.FullRect);

                    var scroll = new ScrollContainer { Name = "Scroll_" + el.Id };
                    scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                    if (sv == null || sv.Vertical)
                        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
                    else
                        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;

                    // Same panel look as the layout containers: dark rounded
                    // fill, padding as content margins.
                    var style = UIStyles.Panel();
                    if (padding > 0f)
                    {
                        style.ContentMarginLeft = padding;
                        style.ContentMarginRight = padding;
                        style.ContentMarginTop = padding;
                        style.ContentMarginBottom = padding;
                    }
                    scroll.AddThemeStyleboxOverride("panel", style);

                    var inner = new VBoxContainer { Name = "ScrollContent_" + el.Id };
                    inner.AddThemeConstantOverride("separation", (int)spacing);
                    inner.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    scroll.AddChild(inner);
                    viewport.AddChild(scroll);

                    wn.ScreenBox = inner;
                    wn.RootControl = viewport;
                    break;
                }
                case "tree":
                {
                    var tree = new Tree { Name = "Tree_" + el.Id };
                    tree.HideRoot = true;
                    tree.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    tree.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                    tree.AddThemeColorOverride("font_color", UIStyles.Text);
                    tree.AddThemeColorOverride("font_selected_color", Colors.White);
                    tree.AddThemeColorOverride("tree_guide_color", new Color(0.28f, 0.29f, 0.34f));
                    // Native Tree selection → invoke the row element's click
                    // (the editor's selection callback) via the shared path.
                    tree.ItemSelected += () => OnTreeItemSelected(wn);
                    wn.ScreenTree = tree;
                    wn.RootControl = tree;
                    break;
                }
                default:
                    // Unreachable for collected widgets; safe fallback.
                    wn.RootControl = new Control { Name = "UI_" + el.Id };
                    break;
            }

            // RootControl IS the visual control itself (or the layout container).
            // It has no parent yet — the reparent pass in UpdateCanvas inserts it
            // into the correct container so Godot's containers can size it.

            // LayoutElementComponent hints apply to any widget (spacers, rows…).
            ApplySizeHints(wn.RootControl, el);
        }

        /// <summary>
        /// Configure a screen-space layout container from its V12 element:
        /// separation from Spacing, an opaque panel StyleBox (with the element's
        /// Padding as content margins) so containers read as editor panels, and
        /// Expand → Godot size flags so the panel absorbs leftover space along
        /// the axis its parent lays out. LayoutElementComponent hints (min /
        /// preferred size) become Godot CustomMinimumSize so sidebars keep a
        /// usable width instead of collapsing to their text.
        /// </summary>
        private static void ConfigureScreenLayout(Container box, IWorldElement el)
        {
            var vcomp = el.GetComponent<VLayoutComponent>();
            var hcomp = el.GetComponent<HLayoutComponent>();
            float spacing = vcomp?.Spacing > 0 ? vcomp.Spacing : (hcomp?.Spacing > 0 ? hcomp.Spacing : 4f);
            float padding = vcomp?.Padding > 0 ? vcomp.Padding : (hcomp?.Padding > 0 ? hcomp.Padding : 0f);
            bool expand = (vcomp?.Expand ?? false) || (hcomp?.Expand ?? false);

            box.AddThemeConstantOverride("separation", (int)spacing);

            // Panel look: dark rounded fill; padding becomes content margins.
            var style = UIStyles.Panel();
            if (padding > 0f)
            {
                style.ContentMarginLeft = padding;
                style.ContentMarginRight = padding;
                style.ContentMarginTop = padding;
                style.ContentMarginBottom = padding;
            }
            box.AddThemeStyleboxOverride("panel", style);

            // ── Sizing hints from LayoutElementComponent ──
            var le = el.GetComponent<LayoutElementComponent>();
            float minW = le?.MinWidth > 0 ? le.MinWidth : 0f;
            float minH = le?.MinHeight > 0 ? le.MinHeight : 0f;
            float prefW = le?.PreferredWidth > 0 ? le.PreferredWidth : 0f;
            float prefH = le?.PreferredHeight > 0 ? le.PreferredHeight : 0f;
            if (le?.FlexibleWidth > 0) expand = true;
            if (le?.FlexibleHeight > 0) expand = true;

            // CustomMinimumSize drives both the box's own minimum and the
            // proportion of leftover space HSplitContainer/BoxContainer grants.
            // Prefer min size; a preferred size acts as a larger minimum so a
            // sidebar (e.g. 240px) holds its width before the viewport expands.
            float cw = Mathf.Max(minW, prefW);
            float ch = Mathf.Max(minH, prefH);
            if (cw > 0f || ch > 0f)
                box.CustomMinimumSize = new Vector2(cw, ch);

            if (!expand) return;

            // Fill the axis the parent container distributes, plus the cross
            // axis so the panel stretches to its full slot (HBox children get
            // full height; VBox children get full width). ExpandFill on both
            // makes sidebars inside a splitter consume the leftover too.
            bool parentHorizontal = el.Parent != null
                && (el.Parent.GetComponent<HLayoutComponent>() != null
                    || el.Parent.GetComponent<SplitterComponent>() is { Orientation: SplitterOrientation.Horizontal })
                && el.Parent.GetComponent<VLayoutComponent>() == null;
            if (parentHorizontal)
            {
                box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                box.SizeFlagsVertical = Control.SizeFlags.Fill;
                if (cw <= 0f) box.CustomMinimumSize = new Vector2(160f, box.CustomMinimumSize.Y);
            }
            else
            {
                box.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                box.SizeFlagsHorizontal = Control.SizeFlags.Fill;
                if (ch <= 0f) box.CustomMinimumSize = new Vector2(box.CustomMinimumSize.X, 100f);
            }
        }

        private void ApplyScreenVisualState(WidgetNode wn, IWorldElement el, string kind)
        {
            switch (kind)
            {
                case "label":
                {
                    if (wn.ScreenLabel != null)
                    {
                        wn.ScreenLabel.Text = el.GetComponent<LabelComponent>()?.Text ?? "";
                        ApplyScreenStyle(wn.ScreenLabel, el);
                    }
                    break;
                }
                case "button":
                {
                    var b = el.GetComponent<ButtonComponent>();
                    var legacy = el.GetComponent<V12.Components.ButtonComponent>();
                    if (wn.ScreenButton != null) wn.ScreenButton.Text = b?.Label ?? legacy?.Label ?? "Button";
                    // Re-apply button colours from style hints so rows/primary
                    // actions restyle when the hint changes (e.g. selection).
                    ApplyButtonStyle(wn.ScreenButton, el);
                    break;
                }
                case "rect":
                    if (wn.ScreenRect != null)
                        wn.ScreenRect.Color = ParseColor(el.GetComponent<RectComponent>()?.BackgroundColor, new Color(0.25f, 0.25f, 0.3f));
                    break;
                case "progress":
                    if (wn.ScreenProgress != null) wn.ScreenProgress.Value = Mathf.Clamp(el.GetComponent<ProgressBarComponent>()?.Value ?? 0f, 0f, 1f) * 100f;
                    break;
                case "toggle":
                case "checkbox":
                    if (wn.ScreenCheck != null)
                    {
                        bool on = kind == "toggle"
                            ? (el.GetComponent<ToggleComponent>()?.IsOn ?? false)
                            : (el.GetComponent<CheckboxComponent>()?.Checked ?? false);
                        wn.ScreenCheck.ButtonPressed = on;
                        string text = kind == "toggle"
                            ? (el.GetComponent<ToggleComponent>()?.Label ?? "")
                            : (el.GetComponent<CheckboxComponent>()?.Label ?? "");
                        if (wn.ScreenCheck is Button checkBtn)
                            checkBtn.Text = text;
                    }
                    break;
                case "slider":
                    if (wn.ScreenSlider != null)
                    {
                        var s = el.GetComponent<SliderComponent>();
                        if (s != null)
                        {
                            wn.ScreenSlider.MinValue = s.Min;
                            wn.ScreenSlider.MaxValue = s.Max;
                            wn.ScreenSlider.Step = s.Step;
                            // Don't fight the user mid-drag: only push while not
                            // focused (input already flows UI → component).
                            if (!wn.ScreenSlider.HasFocus() && Mathf.Abs(wn.ScreenSlider.Value - s.Value) > 0.0001f)
                                wn.ScreenSlider.Value = s.Value;
                        }
                    }
                    break;
                case "textinput":
                    if (wn.ScreenEdit != null)
                    {
                        var ti = el.GetComponent<TextInputComponent>();
                        if (ti != null)
                        {
                            wn.ScreenEdit.PlaceholderText = ti.Placeholder;
                            // Only push component → UI when the user isn't editing
                            // the field; input already flows UI → component via
                            // TextChanged/FocusExited wiring, so pushing while
                            // focused would clobber keystrokes.
                            if (!wn.ScreenEdit.HasFocus() && wn.ScreenEdit.Text != ti.Value)
                                wn.ScreenEdit.Text = ti.Value;
                        }
                    }
                    break;
            }
        }

        private void ApplyVisualState(CanvasNode cn, WidgetNode wn, IWorldElement el)
        {
            if (cn.ScreenSpace)
                ApplyScreenVisualState(wn, el, wn.Kind);
            else
                ApplyWorldVisualState(wn, el, wn.Kind);
        }

        // ── Layout (world-space) ──────────────────────────────────────────

        private static Vector2 Measure(IWorldElement el)
        {
            switch (KindOf(el))
            {
                case "label":
                {
                    var l = el.GetComponent<LabelComponent>();
                    float px = l?.FontSize > 0 ? l.FontSize : 16f;
                    float h = px * PixelScale;
                    float w = (l?.Text.Length ?? 0) * px * PixelScale * 0.6f;
                    return new Vector2(Mathf.Max(w, 0.1f), h);
                }
                case "rect":
                {
                    var r = el.GetComponent<RectComponent>();
                    return new Vector2(r?.Width > 0 ? r.Width : 1f, r?.Height > 0 ? r.Height : 0.3f);
                }
                case "button":
                {
                    var b = el.GetComponent<ButtonComponent>();
                    var legacy = el.GetComponent<V12.Components.ButtonComponent>();
                    string label = b?.Label ?? legacy?.Label ?? "Button";
                    float w = Mathf.Max(0.6f, label.Length * 18f * PixelScale * 0.6f + 0.12f);
                    return new Vector2(w, 0.12f);
                }
                case "progress": return new Vector2(1.5f, 0.1f);
                case "toggle":
                case "checkbox": return new Vector2(0.9f, 0.12f);
                case "slider": return new Vector2(1.2f, 0.08f);
                case "textinput": return new Vector2(1.2f, 0.12f);
                case "image": return new Vector2(0.5f, 0.5f);
                case "icon": return new Vector2(0.1f, 0.1f);
                default: return new Vector2(0.5f, 0.25f);
            }
        }

        /// <summary>
        /// Position the direct UI children of <paramref name="el"/> inside the
        /// canvas, honouring H/V layout components. Returns the subtree size so
        /// nested containers lay out correctly. <paramref name="origin"/> is the
        /// centre of <paramref name="el"/> in canvas-local coordinates.
        /// </summary>
        private Vector2 LayoutElement(CanvasNode cn, IWorldElement el, Vector2 origin)
        {
            var children = new List<IWorldElement>();
            foreach (var c in el.Children)
            {
                if (c.GetComponent<CanvasComponent>() != null) continue;
                if (HasAnyUiComponent(c)) children.Add(c);
            }
            if (children.Count == 0)
                return Measure(el);

            var vcomp = el.GetComponent<VLayoutComponent>();
            var hcomp = el.GetComponent<HLayoutComponent>();
            float spacing = (vcomp?.Spacing ?? hcomp?.Spacing ?? 3f) * 0.01f;
            bool horizontal = hcomp != null && vcomp == null;

            float contentW = 0, contentH = 0;
            var sizes = new List<Vector2>();
            foreach (var c in children)
            {
                var s = Measure(c);
                sizes.Add(s);
                if (horizontal) contentW += s.X;
                else contentH += s.Y;
            }
            if (horizontal) contentW += spacing * (children.Count - 1);
            else contentH += spacing * (children.Count - 1);

            float tx = origin.X - (horizontal ? contentW / 2 : 0);
            float ty = origin.Y + (horizontal ? 0 : contentH / 2);

            for (int i = 0; i < children.Count; i++)
            {
                var c = children[i];
                var s = sizes[i];
                var center = new Vector2(tx + (horizontal ? s.X / 2 : 0), ty - (horizontal ? 0 : s.Y / 2));
                if (cn.Widgets.TryGetValue(c.Id, out var wn))
                    wn.Root3D.Position = new Vector3(center.X, center.Y, 0);

                LayoutElement(cn, c, center);

                if (horizontal) tx += s.X + spacing;
                else ty -= s.Y + spacing;
            }

            return horizontal
                ? new Vector2(contentW, Measure(el).Y)
                : new Vector2(Measure(el).X, contentH);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static void ApplyStyle(IWorldElement el, Label3D label, float baseFontSize)
        {
            label.FontSize = (int)(baseFontSize > 0 ? baseFontSize : 16f);
            label.Modulate = Colors.White;
            var style = el.GetComponent<UIStyleComponent>();
            if (style?.StyleHint == null) return;
            switch (style.StyleHint)
            {
                case "title": label.FontSize = 24; break;
                case "muted": label.Modulate = new Color(0.7f, 0.7f, 0.72f); break;
                case "danger": label.Modulate = new Color(0.95f, 0.35f, 0.35f); break;
                case "accent": label.Modulate = new Color(0.4f, 0.7f, 1f); break;
                default: break;
            }
        }

        /// <summary>
        /// Apply UIStyleComponent hints to a screen-space Label so panel headers
        /// read as headers: "title" → larger bold caps, "muted" → dimmed,
        /// "titlebar"/"compheader" → smaller bold with a subtle underline bar.
        /// </summary>
        private static void ApplyScreenStyle(Label label, IWorldElement el)
        {
            label.Modulate = Colors.White;
            var style = el.GetComponent<UIStyleComponent>();
            if (style?.StyleHint == null) return;
            switch (style.StyleHint)
            {
                case "title":
                case "titlebar":
                    label.AddThemeFontSizeOverride("font_size", 20);
                    label.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.98f));
                    break;
                case "compheader":
                    label.AddThemeFontSizeOverride("font_size", 15);
                    label.AddThemeColorOverride("font_color", new Color(0.8f, 0.83f, 0.9f));
                    break;
                case "muted":
                    label.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f));
                    break;
                case "danger":
                    label.AddThemeColorOverride("font_color", new Color(0.95f, 0.35f, 0.35f));
                    break;
                case "accent":
                    label.AddThemeColorOverride("font_color", new Color(0.45f, 0.72f, 1f));
                    break;
                default: break;
            }
        }

        private static MeshInstance3D MakeQuad(Vector2 size, Color color)
        {
            var mesh = new QuadMesh { Size = Vector2.One };
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = color
            };
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Scale = new Vector3(size.X, size.Y, 1) };
            mi.Name = "Quad";
            return mi;
        }

        private static Color ParseColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            hex = hex.Trim().TrimStart('#');
            if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v))
            {
                if (hex.Length == 6)
                    return new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, 1f);
                if (hex.Length == 8)
                    return new Color(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
            }
            return fallback;
        }

        private static Texture2D? LoadTexture(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            try
            {
                if (source.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
                    return GD.Load<Texture2D>(source);
                if (System.IO.File.Exists(source))
                {
                    var img = new Image();
                    if (img.Load(source) == Error.Ok)
                        return ImageTexture.CreateFromImage(img);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// World transform (position + rotation, ignoring scale — scale is used
        /// only to size the canvas plane) of an element, accumulated from its
        /// parent chain. Read on the main thread; UI transforms are effectively
        /// static so a brief snapshot of the hierarchy is fine.
        /// </summary>
        private static Transform3D WorldTransformNoScale(IWorldElement el)
        {
            var t = ElementTransformNoScale(el);
            while (el.Parent != null)
            {
                el = el.Parent;
                t = ElementTransformNoScale(el) * t;
            }
            return t;
        }

        private static Transform3D ElementTransformNoScale(IWorldElement el)
        {
            var pos = el.LocalTransform.Position;
            var rot = el.LocalTransform.Rotation;
            var basis = new Basis(new Quaternion(rot.X, rot.Y, rot.Z, rot.W));
            return new Transform3D(basis, new Vector3(pos.X, pos.Y, pos.Z));
        }

        // ── Node bookkeeping ──────────────────────────────────────────────

        private sealed class CanvasNode
        {
            public long ElementId;
            public bool ScreenSpace;
            public float Width = 2f;
            public float Height = 1.2f;

            public Node3D Root3D;
            public MeshInstance3D Panel;
            public Control RootControl;
            public ColorRect ScreenBackground;
            public VBoxContainer LayoutBox;

            public readonly Dictionary<long, WidgetNode> Widgets = new();

            public void Free()
            {
                foreach (var w in Widgets.Values)
                    w.Free(ScreenSpace);
                Widgets.Clear();
                if (Root3D != null && GodotObject.IsInstanceValid(Root3D)) Root3D.QueueFree();
                if (RootControl != null && GodotObject.IsInstanceValid(RootControl)) RootControl.QueueFree();
            }
        }

        private sealed class WidgetNode
        {
            public long ElementId;
            public string Kind = "";
            public IWorldElement Element;
            public bool Hovered;

            public Node3D Root3D;
            public Control RootControl;

            /// <summary>For layout containers (hlayout/vlayout), the inner HBoxContainer/VBoxContainer
            /// that child widgets should be added to. Null for non-layout widgets.</summary>
            public Container ScreenBox;

            public Label3D Label3D;
            public MeshInstance3D Rect;
            public MeshInstance3D Fill;
            public MeshInstance3D Track;
            public MeshInstance3D Handle;
            public StandardMaterial3D RectMat;
            public StandardMaterial3D FillMat;
            public StandardMaterial3D TrackMat;
            public StandardMaterial3D HandleMat;

            public Label ScreenLabel;
            public Button ScreenButton;
            public ColorRect ScreenRect;
            public ProgressBar ScreenProgress;
            public BaseButton ScreenCheck;
            public HSlider ScreenSlider;
            public LineEdit ScreenEdit;

            // 3D scene viewport (ViewportComponent)
            public SubViewportContainer ScreenViewportContainer;
            public SubViewport ScreenViewport;
            public Camera3D ScreenFallbackCamera;

            // Tree container (TreeComponent)
            public Tree ScreenTree;
            public string TreeSignature = "";
            public readonly Dictionary<long, TreeItem> TreeItems = new();
            public readonly Dictionary<long, IWorldElement> TreeElements = new();

            public void FreeVisuals(bool screenSpace)
            {
                if (screenSpace)
                {
                    foreach (var c in new[] { ScreenLabel as Node, ScreenButton, ScreenRect, ScreenProgress, ScreenCheck, ScreenSlider, ScreenEdit, ScreenViewportContainer, ScreenTree })
                        if (c != null && GodotObject.IsInstanceValid(c)) c.QueueFree();
                    if (ScreenBox != null && GodotObject.IsInstanceValid(ScreenBox)) ScreenBox.QueueFree();
                    ScreenLabel = null; ScreenButton = null; ScreenRect = null;
                    ScreenProgress = null; ScreenCheck = null; ScreenSlider = null; ScreenEdit = null;
                    ScreenBox = null;
                    ScreenViewportContainer = null;
                    ScreenViewport = null;
                    ScreenFallbackCamera = null;
                    ScreenTree = null;
                    TreeItems.Clear();
                    TreeElements.Clear();
                    // RootControl aliases one of the freed visuals above.
                    RootControl = null;
                    return;
                }

                foreach (var c in new[] { Label3D as Node, Rect, Fill, Track, Handle })
                    if (c != null && GodotObject.IsInstanceValid(c)) c.QueueFree();
                Label3D = null; Rect = null; Fill = null; Track = null; Handle = null;
                RectMat = null; FillMat = null; TrackMat = null; HandleMat = null;
            }

            public void Free(bool screenSpace)
            {
                FreeVisuals(screenSpace);
                if (Root3D != null && GodotObject.IsInstanceValid(Root3D)) Root3D.QueueFree();
                if (RootControl != null && GodotObject.IsInstanceValid(RootControl)) RootControl.QueueFree();
            }
        }
    }
}
