using System;
using System.Collections.Generic;
using Godot;
using V12.Components.UI;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Networking;

namespace V12TwoDog
{
    /// <summary>
    /// Host-side renderer for V12's <c>CanvasComponent</c> UI. This mirrors the
    /// Locus <c>WorldCanvasSystem</c> described in <c>UI_OVERVIEW.md</c>: it walks
    /// the V12 element tree each frame, finds elements bearing a
    /// <c>CanvasComponent</c>, and creates/updates/destroys Godot scene nodes for
    /// each canvas.
    ///
    /// Every canvas is built from real Godot <c>Control</c> widgets. World-space
    /// canvases (<see cref="CanvasComponent.ScreenSpace"/> == false) render their
    /// widget tree into a <c>SubViewport</c> whose texture is applied to the 3D
    /// quad rooted at the element's world transform, so UI can be embedded on
    /// monitors, holograms, etc. Screen-space canvases place the same widgets
    /// inside an overlay <c>CanvasLayer</c>, exactly like a HUD/menu. Godot
    /// signals (pressed, toggled, value changed, text submitted) drive the V12
    /// UI components.
    ///
    /// Interaction: the player's aim ray is cast against world canvases each frame
    /// and the pointer is pushed into the hit canvas's <c>SubViewport</c>, so
    /// Godot's own Control hit-testing runs hover and press. Screen-space widgets
    /// take normal mouse/keyboard input directly.
    /// </summary>
    public class WorldCanvasSystem : IInputHandler
    {
        /// <summary>Pixel width of the SubViewport backing each world canvas.</summary>
        private const int ViewportPxWidth = 1280;
        private const float RayLength = 20f;

        private readonly Node3D _host;
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

        public WorldCanvasSystem(Node3D host)
        {
            _host = host;
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

        private void InvokeClick(WidgetNode wn)
        {
            var uiBtn = wn.Element.GetComponent<ButtonComponent>();
            if (uiBtn != null)
            {
                uiBtn.InvokeClick();
                RpcDispatcher.CallButtonPressed(_gameRoot, wn.Element);
                return;
            }
            var legacy = wn.Element.GetComponent<V12.Components.ButtonComponent>();
            if (legacy != null)
            {
                legacy.Pressed = true;
                legacy.OnPressed?.Invoke();
                RpcDispatcher.CallButtonPressed(_gameRoot, wn.Element);
            }
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
                cn.LayoutBox = new VBoxContainer { Name = "Layout" };
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
                    cn.Widgets[w.Id] = wn;
                }
                UpdateWidget(cn, wn, w);
            }

            var toRemove = new List<long>();
            foreach (var kvp in cn.Widgets)
            {
                if (!seen.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var id in toRemove)
            {
                if (_activeCanvas?.ElementId == id)
                    _activeCanvas = null;
                cn.Widgets[id].Free();
                cn.Widgets.Remove(id);
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
                || el.GetComponent<VLayoutComponent>() != null;
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
            if (el.GetComponent<HLayoutComponent>() != null) return "hlayout";
            if (el.GetComponent<VLayoutComponent>() != null) return "vlayout";
            return "container";
        }

        private void BuildVisuals(CanvasNode cn, WidgetNode wn, IWorldElement el)
        {
            BuildScreenVisuals(cn, wn, el);
        }

        // ── Screen-space visuals (Godot Control overlay) ──────────────────

        private void BuildScreenVisuals(CanvasNode cn, WidgetNode wn, IWorldElement el)
        {
            string kind = wn.Kind;
            var parent = WidgetParent(cn, el);

            switch (kind)
            {
                case "label":
                    wn.ScreenLabel = new Label();
                    parent?.AddChild(wn.ScreenLabel);
                    break;
                case "button":
                    wn.ScreenButton = new Button();
                    parent?.AddChild(wn.ScreenButton);
                    wn.ScreenButton.Pressed += () =>
                    {
                        try { InvokeClick(wn); }
                        catch (Exception e) { GD.PrintErr($"[WorldCanvas] button handler: {e}"); }
                    };
                    break;
                case "rect":
                    wn.ScreenRect = new ColorRect
                    {
                        Color = new Color(0.25f, 0.25f, 0.3f),
                        CustomMinimumSize = new Vector2(16, 8),
                        SizeFlagsHorizontal = Control.SizeFlags.Expand | Control.SizeFlags.Fill,
                        SizeFlagsVertical = Control.SizeFlags.Expand | Control.SizeFlags.Fill,
                    };
                    parent?.AddChild(wn.ScreenRect);
                    break;
                case "progress":
                    wn.ScreenProgress = new ProgressBar
                    {
                        CustomMinimumSize = new Vector2(160, 20),
                        ShowPercentage = false,
                    };
                    parent?.AddChild(wn.ScreenProgress);
                    break;
                case "toggle":
                    wn.ScreenCheck = new CheckButton();
                    parent?.AddChild(wn.ScreenCheck);
                    wn.ScreenCheck.Toggled += on =>
                    {
                        var t = wn.Element?.GetComponent<ToggleComponent>();
                        if (t == null || t.IsOn == on) return;
                        t.IsOn = on;
                        try { t.OnToggled?.Invoke(on); } catch { }
                    };
                    break;
                case "checkbox":
                    wn.ScreenCheck = new CheckBox();
                    parent?.AddChild(wn.ScreenCheck);
                    wn.ScreenCheck.Toggled += on =>
                    {
                        var c = wn.Element?.GetComponent<CheckboxComponent>();
                        if (c == null || c.Checked == on) return;
                        c.Checked = on;
                        try { c.OnChanged?.Invoke(on); } catch { }
                    };
                    break;
                case "slider":
                    wn.ScreenSlider = new HSlider { CustomMinimumSize = new Vector2(160, 20) };
                    parent?.AddChild(wn.ScreenSlider);
                    wn.ScreenSlider.ValueChanged += v =>
                    {
                        var s = wn.Element?.GetComponent<SliderComponent>();
                        if (s == null) return;
                        float value = (float)v;
                        if (Mathf.Abs(value - s.Value) < 0.0001f) return;
                        s.Value = value;
                        try { s.OnChanged?.Invoke(value); } catch { }
                    };
                    break;
                case "textinput":
                    wn.ScreenEdit = new LineEdit();
                    parent?.AddChild(wn.ScreenEdit);
                    wn.ScreenEdit.TextSubmitted += text =>
                    {
                        var ti = wn.Element?.GetComponent<TextInputComponent>();
                        if (ti == null || ti.Value == text) return;
                        ti.Value = text; // the setter raises OnChanged
                    };
                    break;
                case "image":
                    wn.ScreenRect = new ColorRect
                    {
                        Color = new Color(0.25f, 0.25f, 0.3f),
                        CustomMinimumSize = new Vector2(16, 16),
                        SizeFlagsHorizontal = Control.SizeFlags.Expand | Control.SizeFlags.Fill,
                        SizeFlagsVertical = Control.SizeFlags.Expand | Control.SizeFlags.Fill,
                    };
                    parent?.AddChild(wn.ScreenRect);
                    break;
                case "hlayout":
                case "vlayout":
                {
                    var box = kind == "hlayout"
                        ? (Container)new HBoxContainer()
                        : new VBoxContainer();
                    wn.Container = box;
                    box.SizeFlagsHorizontal = Control.SizeFlags.Expand | Control.SizeFlags.Fill;
                    box.SizeFlagsVertical = Control.SizeFlags.Expand | Control.SizeFlags.Fill;
                    parent?.AddChild(box);
                    break;
                }
                default:
                    break;
            }
        }

        /// <summary>
        /// The Control a widget's visuals are added to: the container of a
        /// parent widget when this element nests inside one, otherwise the
        /// canvas layout itself (screen overlay or world viewport).
        /// </summary>
        private Control? WidgetParent(CanvasNode cn, IWorldElement el)
        {
            var p = el.Parent;
            if (p != null && p.GetComponent<CanvasComponent>() == null
                && cn.Widgets.TryGetValue(p.Id, out var pwn) && pwn.Container != null)
                return pwn.Container;
            return cn.ScreenSpace ? (Control?)cn.LayoutBox : (Control?)cn.WorldLayout;
        }

        private void ApplyScreenVisualState(WidgetNode wn, IWorldElement el, string kind)
        {
            switch (kind)
            {
                case "label":
                    if (wn.ScreenLabel != null) wn.ScreenLabel.Text = el.GetComponent<LabelComponent>()?.Text ?? "";
                    break;
                case "button":
                {
                    var b = el.GetComponent<ButtonComponent>();
                    var legacy = el.GetComponent<V12.Components.ButtonComponent>();
                    if (wn.ScreenButton != null) wn.ScreenButton.Text = b?.Label ?? legacy?.Label ?? "Button";
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
                            if (wn.ScreenEdit.Text != ti.Value)
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
            public SubViewport Viewport;
            public Control ViewportRoot;
            public BoxContainer WorldLayout;
            public Control RootControl;
            public VBoxContainer LayoutBox;

            public readonly Dictionary<long, WidgetNode> Widgets = new();

            public void Free()
            {
                foreach (var w in Widgets.Values)
                    w.Free();
                Widgets.Clear();
                if (Root3D != null && GodotObject.IsInstanceValid(Root3D)) Root3D.QueueFree();
                if (RootControl != null && GodotObject.IsInstanceValid(RootControl)) RootControl.QueueFree();
                if (Viewport != null && GodotObject.IsInstanceValid(Viewport)) Viewport.QueueFree();
            }
        }

        private sealed class WidgetNode
        {
            public long ElementId;
            public string Kind = "";
            public IWorldElement Element;

            public Label ScreenLabel;
            public Button ScreenButton;
            public ColorRect ScreenRect;
            public ProgressBar ScreenProgress;
            public BaseButton ScreenCheck;
            public HSlider ScreenSlider;
            public LineEdit ScreenEdit;
            public Container Container;

            public void FreeVisuals()
            {
                foreach (var c in new Node?[] { ScreenLabel, ScreenButton, ScreenRect, ScreenProgress, ScreenCheck, ScreenSlider, ScreenEdit, Container })
                    if (c != null && GodotObject.IsInstanceValid(c)) c.QueueFree();
                ScreenLabel = null; ScreenButton = null; ScreenRect = null;
                ScreenProgress = null; ScreenCheck = null; ScreenSlider = null; ScreenEdit = null;
                Container = null;
            }

            public void Free()
            {
                FreeVisuals();
            }
        }
    }
}
