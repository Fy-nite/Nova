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
    /// become 3D nodes — coloured quads + <c>Label3D</c> text — rooted at the
    /// element's world transform, so UI can be embedded on monitors, holograms,
    /// etc. Screen-space canvases become Godot <c>Control</c> nodes inside an
    /// overlay <c>CanvasLayer</c>, exactly like a HUD/menu.
    ///
    /// Interaction: the player's aim ray is cast against world canvases each frame;
    /// pressing "interact" while pointing at a UI button invokes its click.
    /// </summary>
    public class WorldCanvasSystem : IInputHandler
    {
        /// <summary>Label3D pixel-to-world scale (world units per font pixel).</summary>
        private const float PixelScale = 0.005f;
        private const float RayLength = 20f;

        private readonly Node3D _host;
        private CanvasLayer? _screenLayer;
        private readonly Dictionary<long, CanvasNode> _canvases = new();
        private GameRoot _gameRoot;
        private bool _inputRegistered;
        private volatile bool _interactRequested;

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
            HandleInteract();
        }

        public void OnInputEvent(V12.Core.Input.InputEvent evt)
        {
            if (evt.Type == InputEventType.ButtonDown && evt.Name == "interact")
                _interactRequested = true;
        }

        private void EnsureInputRegistered()
        {
            if (_inputRegistered || _gameRoot == null) return;
            var input = _gameRoot.Registry.Get<InputService>();
            if (input == null) return;
            input.RegisterHandler(this);
            _inputRegistered = true;
        }

        private void HandleInteract()
        {
            if (!_interactRequested) return;
            _interactRequested = false;

            var playerEl = _gameRoot.FindElementWithComponent<V12.Basic.Components.PlayerComponent>();
            if (playerEl == null) return;
            var aim = playerEl.GetComponent<V12.Basic.Components.PlayerComponent>().GetAimRay();
            var origin = new Vector3(aim.origin.X, aim.origin.Y, aim.origin.Z);
            var dir = new Vector3(aim.direction.X, aim.direction.Y, aim.direction.Z);

            foreach (var cn in _canvases.Values)
            {
                if (cn.ScreenSpace || cn.Root3D == null) continue;
                if (!RaycastCanvas(cn, origin, dir, RayLength, out var local)) continue;
                var btn = HitTestButton(cn, local);
                if (btn != null)
                {
                    InvokeClick(btn);
                    break;
                }
            }
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

        private static WidgetNode? HitTestButton(CanvasNode cn, Vector2 localPoint)
        {
            foreach (var wn in cn.Widgets.Values)
            {
                if (wn.Kind != "button" || wn.Root3D == null || wn.Element == null) continue;
                var pos = wn.Root3D.Position;
                var size = Measure(wn.Element);
                if (Mathf.Abs(localPoint.X - pos.X) <= size.X / 2f && Mathf.Abs(localPoint.Y - pos.Y) <= size.Y / 2f)
                    return wn;
            }
            return null;
        }

        private static void InvokeClick(WidgetNode wn)
        {
            var uiBtn = wn.Element.GetComponent<ButtonComponent>();
            if (uiBtn != null)
            {
                uiBtn.InvokeClick();
                return;
            }
            var legacy = wn.Element.GetComponent<V12.Components.ButtonComponent>();
            if (legacy != null)
            {
                legacy.Pressed = true;
                legacy.OnPressed?.Invoke();
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
                        wn.RootControl = new Control { Name = "UI_" + w.Id };
                        cn.LayoutBox.AddChild(wn.RootControl);
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

            var toRemove = new List<long>();
            foreach (var kvp in cn.Widgets)
            {
                if (!seen.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var id in toRemove)
            {
                cn.Widgets[id].Free(cn.ScreenSpace);
                cn.Widgets.Remove(id);
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
                        wn.RectMat.AlbedoColor = pressed ? new Color(0.1f, 0.3f, 0.6f) : new Color(0.22f, 0.45f, 0.85f);
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

        private void BuildScreenVisuals(CanvasNode cn, WidgetNode wn, IWorldElement el)
        {
            string kind = wn.Kind;
            switch (kind)
            {
                case "label":
                    wn.ScreenLabel = new Label();
                    break;
                case "button":
                    wn.ScreenButton = new Button();
                    break;
                case "rect":
                    wn.ScreenRect = new ColorRect();
                    break;
                case "progress":
                    wn.ScreenProgress = new ProgressBar();
                    break;
                case "toggle":
                    wn.ScreenCheck = new CheckButton();
                    break;
                case "checkbox":
                    wn.ScreenCheck = new CheckBox();
                    break;
                case "slider":
                    wn.ScreenSlider = new HSlider();
                    break;
                case "textinput":
                    wn.ScreenEdit = new LineEdit();
                    break;
                case "image":
                    wn.ScreenRect = new ColorRect { Color = new Color(0.25f, 0.25f, 0.3f) };
                    break;
                default:
                    break;
            }

            if (wn.RootControl != null)
            {
                if (wn.ScreenLabel != null) wn.RootControl.AddChild(wn.ScreenLabel);
                if (wn.ScreenButton != null) wn.RootControl.AddChild(wn.ScreenButton);
                if (wn.ScreenRect != null) wn.RootControl.AddChild(wn.ScreenRect);
                if (wn.ScreenProgress != null) wn.RootControl.AddChild(wn.ScreenProgress);
                if (wn.ScreenCheck != null) wn.RootControl.AddChild(wn.ScreenCheck);
                if (wn.ScreenSlider != null) wn.RootControl.AddChild(wn.ScreenSlider);
                if (wn.ScreenEdit != null) wn.RootControl.AddChild(wn.ScreenEdit);
            }
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

            public Node3D Root3D;
            public Control RootControl;

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

            public void FreeVisuals(bool screenSpace)
            {
                if (screenSpace)
                {
                    foreach (var c in new[] { ScreenLabel as Node, ScreenButton, ScreenRect, ScreenProgress, ScreenCheck, ScreenSlider, ScreenEdit })
                        if (c != null && GodotObject.IsInstanceValid(c)) c.QueueFree();
                    ScreenLabel = null; ScreenButton = null; ScreenRect = null;
                    ScreenProgress = null; ScreenCheck = null; ScreenSlider = null; ScreenEdit = null;
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
