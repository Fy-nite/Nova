using System;
using System.Collections.Generic;
using Godot;
using V12.Components.UI;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;

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
    /// render their widget tree into a <c>SubViewport</c> whose texture is
    /// applied to the 3D quad rooted at the element's world transform, so real
    /// Godot <c>Control</c> widgets can be embedded on monitors, holograms,
    /// etc. Screen-space canvases place the same widgets inside an overlay
    /// <c>CanvasLayer</c>, exactly like a HUD/menu.
    ///
    /// Interaction: the player's aim ray is cast against world canvases each
    /// frame and the pointer is pushed into the hit canvas's <c>SubViewport</c>,
    /// so Godot's own Control hit-testing drives hover and press. Screen-space
    /// widgets take normal mouse/keyboard input directly.
    /// </summary>
    public class WorldCanvasSystem : IInputHandler
    {
        /// <summary>Pixel width of the SubViewport backing each world canvas.</summary>
        private const int ViewportPxWidth = 1280;
        private const float RayLength = 20f;

        private readonly Node3D _host;
        private readonly Renderer _renderer;
        private CanvasLayer? _screenLayer;
        private readonly Dictionary<long, CanvasNode> _canvases = new();
        private GameRoot _gameRoot;
        private bool _inputRegistered;

        // Aim-ray pointer state for world-space viewport canvases. Pointer motion
        // is pushed into the hit canvas's SubViewport so Godot's own Control
        // hit-testing drives hover and press (real UI behaviour, no manual picking).
        private bool _interactHeld;
        private bool _prevInteract;
        private CanvasNode? _activeCanvas;
        private Vector2 _pointerViewportPos;

        public WorldCanvasSystem(Node3D host, Renderer renderer)
        {
            _host = host;
            _renderer = renderer;
        }

        /// <summary>
        /// Release all UI nodes (canvases + overlay layer). Call on shutdown so
        /// Godot doesn't leak the overlay's Canvas RID.
        /// </summary>
        public void Dispose()
        {
            _activeCanvas = null;
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
        }

        public void OnInputEvent(V12.Core.Input.InputEvent evt)
        {
            if (evt.Name != "interact") return;
            _interactHeld = evt.Type == InputEventType.ButtonDown;
        }

        /// <summary>
        /// Forward a Godot keyboard event to the active world-space canvas's
        /// SubViewport so focused text fields (LineEdit) and other controls
        /// receive key input. Only forwards when a control inside the viewport
        /// already has focus (e.g. after a pointer click focused a LineEdit).
        /// Called on the main thread from V12Runtime.HandleInputEvent.
        /// </summary>
        public void ForwardKeyEvent(global::Godot.InputEvent evt)
        {
            if (evt is not global::Godot.InputEventKey) return;
            var vp = _activeCanvas?.Viewport;
            if (vp == null) return;
            if (vp.GetFocusedControl() == null) return;
            vp.PushInput(evt);
        }

        /// <summary>
        /// Returns true when a world-space canvas SubViewport currently has a
        /// focused control (e.g. a LineEdit the player clicked into). Used to
        /// suppress game movement/actions while typing so keystrokes don't
        /// double up as game input.
        /// </summary>
        public bool HasKeyboardFocus()
        {
            var vp = _activeCanvas?.Viewport;
            return vp != null && vp.GetFocusedControl() != null;
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
        /// world-space viewport canvas and pushes Godot pointer events into the
        /// hit canvas's SubViewport, so Godot's own Control hit-testing drives
        /// hover and press. All SubViewport manipulation happens here on the
        /// main thread; OnInputEvent only records the button state.
        /// </summary>
        private void UpdateInteraction()
        {
            if (_gameRoot == null) return;

            var playerEl = _gameRoot.FindElementWithComponent<V12.Basic.Components.PlayerComponent>();
            if (playerEl == null)
            {
                ReleasePointer();
                _prevInteract = _interactHeld;
                return;
            }
            var aim = playerEl.GetComponent<V12.Basic.Components.PlayerComponent>().GetAimRay();
            var origin = new Vector3(aim.origin.X, aim.origin.Y, aim.origin.Z);
            var dir = new Vector3(aim.direction.X, aim.direction.Y, aim.direction.Z);

            var localPoint = RaycastWorldCanvases(origin, dir, out var canvas);
            if (canvas == null)
            {
                ReleasePointer();
                _prevInteract = _interactHeld;
                return;
            }

            // Switch active viewport when the aim moves between canvases.
            if (_activeCanvas != canvas)
            {
                if (_activeCanvas != null)
                {
                    if (_interactHeld) PushPointerButton(_activeCanvas, false);
                    PushPointerMotion(_activeCanvas, new Vector2(-1f, -1f));
                }
                _activeCanvas = canvas;
            }

            bool pressed = _interactHeld && !_prevInteract;
            bool released = !_interactHeld && _prevInteract;
            _prevInteract = _interactHeld;

            PushPointerMotion(canvas, localPoint);
            if (pressed) PushPointerButton(canvas, true);
            else if (released) PushPointerButton(canvas, false);
        }

        /// <summary>
        /// Ends all pointer activity on the currently active canvas (releases a
        /// held press and clears hover). Used when the aim leaves every canvas
        /// or the player disappears.
        /// </summary>
        private void ReleasePointer()
        {
            if (_activeCanvas == null) return;
            if (_interactHeld) PushPointerButton(_activeCanvas, false);
            PushPointerMotion(_activeCanvas, new Vector2(-9999f, -9999f));
            _activeCanvas = null;
        }

        private void PushPointerMotion(CanvasNode canvas, Vector2 localPoint)
        {
            var viewport = canvas.Viewport;
            if (viewport == null) return;
            _pointerViewportPos = LocalToViewport(canvas, localPoint);
            viewport.PushInput(new InputEventMouseMotion
            {
                Position = _pointerViewportPos,
                GlobalPosition = _pointerViewportPos,
                ButtonMask = _interactHeld ? MouseButtonMask.Left : 0,
            });
        }

        private void PushPointerButton(CanvasNode canvas, bool pressed)
        {
            var viewport = canvas.Viewport;
            if (viewport == null) return;
            viewport.PushInput(new InputEventMouseButton
            {
                Position = _pointerViewportPos,
                GlobalPosition = _pointerViewportPos,
                ButtonIndex = MouseButton.Left,
                Pressed = pressed,
                ButtonMask = pressed ? MouseButtonMask.Left : 0,
            });
        }

        /// <summary>
        /// Converts a canvas-local point (local +Y up, origin at the canvas
        /// centre) into SubViewport pixel coordinates (origin top-left, +Y
        /// down) so a pushed mouse event lands on the same pixel the aim ray
        /// hit on the quad.
        /// </summary>
        private static Vector2 LocalToViewport(CanvasNode canvas, Vector2 localPoint)
        {
            var viewport = canvas.Viewport;
            if (viewport == null) return default;
            return new Vector2(
                (localPoint.X + canvas.Width / 2f) / canvas.Width * viewport.Size.X,
                (canvas.Height / 2f - localPoint.Y) / canvas.Height * viewport.Size.Y);
        }

        /// <summary>
        /// Raycast the aim ray against all world viewport canvases and return
        /// the first one hit, with the hit point in canvas-local units.
        /// </summary>
        private Vector2 RaycastWorldCanvases(Vector3 origin, Vector3 dir, out CanvasNode? canvas)
        {
            canvas = null;
            foreach (var cn in _canvases.Values)
            {
                if (cn.ScreenSpace || cn.Root3D == null || cn.Viewport == null) continue;
                if (RaycastCanvas(cn, origin, dir, RayLength, out var local))
                {
                    canvas = cn;
                    return local;
                }
            }
            return default;
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

        private static void InvokeClick(WidgetNode wn)
        {
            if (wn?.Element != null)
                InvokeClick(wn.Element);
        }

        /// <summary>Invoke the click handler of an element's ButtonComponent
        /// (UI or legacy). Used by buttons and tree rows.</summary>
        private static void InvokeClick(IWorldElement el)
        {
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
            sb.Append(el.Id).Append('|').Append(TreeRowText(el)).Append('|')
              .Append(el.GetComponent<UIStyleComponent>()?.StyleHint ?? "").Append(';');
            foreach (var child in el.Children)
                TreeSig(sb, child);
        }

        private void BuildTreeItem(WidgetNode wn, Tree tree, IWorldElement el, TreeItem? parent)
        {
            if (el.GetComponent<CanvasComponent>() != null) return;
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

                // The canvas is a real Godot Control tree rendered into a
                // SubViewport and textured onto the panel quad. Godot's own
                // hit-testing (driven by pushed pointer events) runs the UI.
                cn.Viewport = new SubViewport
                {
                    Name = "Viewport_" + el.Id,
                    Size = new Vector2I(ViewportPxWidth, ViewportPxWidth / 2),
                    TransparentBg = true,
                    HandleInputLocally = false,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                };
                _host.AddChild(cn.Viewport);

                cn.ViewportRoot = new Control { Name = "Root" };
                cn.ViewportRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                cn.Viewport.AddChild(cn.ViewportRoot);

                cn.WorldLayout = el.GetComponent<HLayoutComponent>() != null && el.GetComponent<VLayoutComponent>() == null
                    ? new HBoxContainer { Name = "Layout" }
                    : new VBoxContainer { Name = "Layout" };
                cn.ViewportRoot.AddChild(cn.WorldLayout);
                cn.WorldLayout.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                cn.WorldLayout.Alignment = BoxContainer.AlignmentMode.Center;

                // Subtle backing panel so an otherwise-floating canvas reads as a "screen".
                cn.Panel = MakeQuad(new Vector2(cn.Width, cn.Height), new Color(0.1f, 0.1f, 0.12f, 0.55f));
                cn.Panel.Position = new Vector3(0, 0, -0.01f);
                cn.Root3D.AddChild(cn.Panel);

                if (cn.Panel.MaterialOverride is StandardMaterial3D panelMat)
                    panelMat.AlbedoTexture = cn.Viewport.GetTexture();
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

                // Keep the SubViewport resolution proportional to the canvas aspect.
                if (cn.Viewport != null && cn.Width > 0.001f)
                {
                    var target = new Vector2I(
                        ViewportPxWidth,
                        Math.Max(1, (int)Math.Round(ViewportPxWidth * cn.Height / cn.Width)));
                    if (cn.Viewport.Size != target)
                        cn.Viewport.Size = target;
                }

                if (cn.Panel != null)
                {
                    cn.Panel.Scale = new Vector3(cn.Width, cn.Height, 1);
                    if (cn.Panel.MaterialOverride is StandardMaterial3D mat)
                    {
                        mat.AlbedoColor = new Color(0.1f, 0.1f, 0.12f, 0.55f);
                        mat.AlbedoTexture ??= cn.Viewport?.GetTexture();
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
                    // RootControl is assigned by BuildScreenVisuals (it IS the
                    // real Label/Button/… or the HBox/VBox layout container),
                    // then the reparent pass below inserts it into the right
                    // container.
                    cn.Widgets[w.Id] = wn;
                }
                UpdateWidget(cn, wn, w);
            }

            // ── Reparent pass ──
            // Now that all layout containers (ScreenBox) have been built,
            // reparent each widget's RootControl under the correct container.
            // Screen canvases hang off the overlay LayoutBox; world canvases
            // hang off the SubViewport's WorldLayout (both real Godot Containers).
            {
                Container baseContainer = cn.ScreenSpace ? (Container)cn.LayoutBox : (Container)cn.WorldLayout;
                foreach (var w in widgets)
                {
                    if (!cn.Widgets.TryGetValue(w.Id, out var wn)) continue;
                    if (wn.RootControl == null) continue;
                    if (wn.RootControl.GetParent() != null) continue; // already parented

                    // Find the nearest ancestor that is a layout widget with a ScreenBox.
                    Container targetContainer = baseContainer;
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
                var freed = cn.Widgets[id];
                if (freed.ScreenViewport != null)
                {
                    _renderer.UnregisterSceneViewport(freed.ElementId);
                    // Re-capture so nodes previously inside the viewport reparent
                    // back to the main scene.
                    _gameRoot?.MarkRenderDirty();
                }
                freed.Free();
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
            if (cn.ScreenSpace)
                cn.LayoutBox.QueueSort();
            else if (cn.WorldLayout != null)
                cn.WorldLayout.QueueSort();
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
                wn.FreeVisuals();
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
            if (el.GetComponent<HLayoutComponent>() != null) return "hlayout";
            if (el.GetComponent<VLayoutComponent>() != null) return "vlayout";
            return "container";
        }

        private void BuildVisuals(CanvasNode cn, WidgetNode wn, IWorldElement el)
        {
            // Both screen-space and world-space canvases render real Godot
            // Control widgets — world canvases draw theirs into a SubViewport
            // that is textured onto the panel quad.
            BuildScreenVisuals(cn, wn, el);
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
                    wn.ScreenButton = new Button();
                    StyleButton(wn.ScreenButton);
                    // Wire the Godot press so both viewport-canvas buttons and
                    // screen-space buttons invoke the element's click handler.
                    wn.ScreenButton.Pressed += () => InvokeClick(wn);
                    wn.RootControl = wn.ScreenButton;
                    break;
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
                        Stretch = true
                    };
                    wn.ScreenViewportContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    wn.ScreenViewportContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                    wn.ScreenViewportContainer.AddChild(wn.ScreenViewport);
                    wn.RootControl = wn.ScreenViewportContainer;

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
            ApplyScreenVisualState(wn, el, wn.Kind);
        }

        // ── Helpers ───────────────────────────────────────────────────────

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

            // World-space canvases render their widget tree into a SubViewport
            // whose texture is drawn on the panel quad.
            public SubViewport Viewport;
            public Control ViewportRoot;
            public BoxContainer WorldLayout;

            public readonly Dictionary<long, WidgetNode> Widgets = new();

            public void Free()
            {
                foreach (var w in Widgets.Values)
                    w.Free();
                Widgets.Clear();
                if (Root3D != null && GodotObject.IsInstanceValid(Root3D)) Root3D.QueueFree();
                if (RootControl != null && GodotObject.IsInstanceValid(RootControl)) RootControl.QueueFree();
                if (Viewport != null && GodotObject.IsInstanceValid(Viewport)) Viewport.QueueFree();
                Viewport = null;
                ViewportRoot = null;
                WorldLayout = null;
            }
        }

        private sealed class WidgetNode
        {
            public long ElementId;
            public string Kind = "";
            public IWorldElement Element;

            public Control RootControl;

            /// <summary>For layout containers (hlayout/vlayout), the inner HBoxContainer/VBoxContainer
            /// that child widgets should be added to. Null for non-layout widgets.</summary>
            public Container ScreenBox;

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

            public void FreeVisuals()
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
            }

            public void Free()
            {
                FreeVisuals();
                if (RootControl != null && GodotObject.IsInstanceValid(RootControl)) RootControl.QueueFree();
            }
        }
    }
}
