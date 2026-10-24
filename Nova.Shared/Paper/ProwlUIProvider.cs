using System;
using System.Collections.Generic;
using Godot;
using V12.UI;
using PVectorColor = global::Prowl.Vector.Color;
using PColor32 = global::Prowl.Vector.Color32;
using PPaper = global::Prowl.PaperUI.Paper;
using PLayout = global::Prowl.PaperUI.LayoutType;

namespace V12TwoDog.Paper
{
    /// <summary>
    /// Prowl-backed <see cref="IUIProvider"/> for the Studio editor shell.
    ///
    /// The editor's retained widget tree (IButton/ILabel/ITreeView/...) is mapped to
    /// Paper immediate-mode boxes each frame and rasterised through Quill + the
    /// <see cref="CanvasShader"/> mesh bridge, so the editor chrome renders with the
    /// same UI stack game code uses (Paper/Quill/Origami, no Godot Controls).
    ///
    /// Two host-native pieces stay on Godot because Paper cannot own them:
    ///  - the 3D game view (<c>SubViewportContainer</c>), positioned onto the Paper
    ///    GameView rect via the element's post-layout callback;
    ///  - file dialogs / context menus, which are OS-shell surfaces.
    ///
    /// Layout follows the same structure as the Godot provider: a root column of
    /// toolbar / split panes / status, with the same nested split ratios.
    /// </summary>
        public sealed class ProwlUIProvider : IUIProvider, IInspectorHost, IDockProvider, IPaletteProvider, IDisposable
    {
        private readonly Window _window;
        private readonly CanvasLayer _paperLayer;
        private readonly Control _paperRoot;
        private readonly GodotPaperCanvasRenderer _renderer;
        private readonly ProwlWindow _rootWindow;
        private readonly List<ProwlWidget> _all = new();

        private PPaper _paper;
        private Prowl.Scribe.FontFile? _font;
        private int _paperW = -1, _paperH = -1;
        private bool _disposed;
        private int _dbgFrame;

        private ProwlGameViewPanel? _gameView;
        private global::Godot.Texture2D? _gameTexture;
        private global::Godot.Control? _viewportProxy;
        private global::Prowl.Vector.Rect _gameRect;

        private static int _nextId;

        public ProwlUIProvider(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));

            _paperLayer = new CanvasLayer { Layer = 110, Name = "ProwlUILayer" };
            _paperRoot = new Control { Name = "ProwlUIRoot", MouseFilter = Control.MouseFilterEnum.Ignore };
            _paperRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _paperLayer.AddChild(_paperRoot);

            // The window is still wiring children during _Ready; defer the attach.
            _window.CallDeferred(Node.MethodName.AddChild, _paperLayer);

            _renderer = new GodotPaperCanvasRenderer(_paperLayer, _paperRoot);

            var vp = _window.Size;
            int w = vp.X >= 64 ? vp.X : 1600;
            int h = vp.Y >= 64 ? vp.Y : 900;
            _renderer.SetViewport(w, h);
            _paper = NewPaper(w, h);
            _rootWindow = new ProwlWindow(_window, this);
            ApplyOrigamiTheme();

            // User palettes (user://palettes/*.json) join the built-ins before the first apply.
            try { V12TwoDog.Godot.UI.PaletteIO.LoadAll(); } catch { }
            ApplyPalette(V12TwoDog.Godot.UI.StudioThemeManager.Current.Name);

            GD.Print("[ProwlUIProvider] Editor UI: Paper/Quill (Paper 3.9.3 + Quill canvas shader)");
        }

        /// <summary>
        /// Square the Origami chrome (tabs, splitters, dock borders) and make its type
        /// match the rest of the editor. Origami ships rounded (8px) with 16px text.
        /// </summary>
        private void ApplyOrigamiTheme()
        {
            try
            {
                var theme = global::Prowl.OrigamiUI.Origami.Root;
                theme.Metrics.Rounding = 0f;
                theme.Metrics.ContainerRounding = 0f;
                theme.Metrics.SmallRounding = 0f;
                if (_font != null)
                {
                    theme.Font ??= _font;
                    theme.FontMedium ??= _font;
                    theme.FontSemiBold ??= _font;
                    theme.FontBold ??= _font;
                    theme.FontMono ??= _font;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ProwlUIProvider] Origami theme setup failed: {ex.Message}");
            }
        }

        // ── Palette ────────────────────────────────────────────────────────

        public IReadOnlyList<string> PaletteNames =>
            V12TwoDog.Godot.UI.Palettes.All.Select(p => p.Name).ToList();

        public string CurrentPalette { get; private set; } = "Slate";

        public void SetPalette(string name) => ApplyPalette(name);

        /// <summary>
        /// Retint both the Paper chrome and Origami's dock widget theme from one of the
        /// Studio palettes (Slate / Midnight / Ember / Moss / Static / Paper / user JSON).
        /// </summary>
        private void ApplyPalette(string name)
        {
            V12TwoDog.Godot.UI.Palettes.TryFind(name, out var p);
            if (p == null) p = V12TwoDog.Godot.UI.Palettes.Slate;
            CurrentPalette = p.Name;

            var canvas = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Canvas]);
            var panel = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Panel]);
            var raised = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Raised]);
            var hover = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Hover]);
            var selected = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Selected]);
            var border = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Border]);
            var accent = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Focus]);
            var text = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Text]);
            var muted = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Muted]);
            var disabled = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Disabled]);
            var danger = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Danger]);
            var success = ToPaper(p[V12TwoDog.Godot.UI.ColorSlot.Success]);

            V12.Core.UI.Paper.PaperPalette.Set(
                canvas, panel, raised, hover, selected, border, accent, text, muted, disabled,
                p.IsLight, danger, success);

            ApplyOrigamiColors(panel, raised, hover, selected, border, accent, text, muted, disabled, canvas);
        }

        private static PVectorColor ToPaper(global::Godot.Color c)
            => new PVectorColor(c.R, c.G, c.B, c.A);

        private static void ApplyOrigamiColors(
            PVectorColor panel, PVectorColor raised, PVectorColor hover, PVectorColor selected,
            PVectorColor border, PVectorColor accent, PVectorColor text, PVectorColor muted,
            PVectorColor disabled, PVectorColor canvas)
        {
            try
            {
                var th = global::Prowl.OrigamiUI.Origami.Root;

                FillRamp(th.Neutral,
                    ToDrawing(canvas), ToDrawing(panel), ToDrawing(raised), ToDrawing(hover),
                    ToDrawing(border), ToDrawing(muted), ToDrawing(text));

                FillRamp(th.Primary,
                    Shade(accent, 0.35f), Shade(accent, 0.55f), Shade(accent, 0.75f), Shade(accent, 0.9f),
                    ToDrawing(accent), Shade(accent, 1.15f), Shade(accent, 1.3f));

                FillRamp(th.Ink,
                    ToDrawing(disabled), ToDrawing(disabled), ToDrawing(muted), ToDrawing(muted),
                    ToDrawing(text), Shade(text, 1.08f), Shade(text, 1.18f));

                th.Glass = ToDrawing(panel);
                th.Popover = ToDrawing(raised);
                th.BorderSoft = ToDrawing(border);
                th.BorderStrong = ToDrawing(Shade(border, 1.25f));
                th.Shadow = global::System.Drawing.Color.FromArgb(90, 0, 0, 0);

                th.HoverOverride = ToDrawing(hover);
                th.SelectedOverride = ToDrawing(selected);
                th.FocusOverride = ToDrawing(accent);
                th.SplitterOverride = ToDrawing(border);
                th.ScrollbarOverride = ToDrawing(muted);
                th.TooltipOverride = ToDrawing(raised);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ProwlUIProvider] Origami palette failed: {ex.Message}");
            }
        }

        private static global::System.Drawing.Color ToDrawing(PVectorColor c)
            => global::System.Drawing.Color.FromArgb(
                (int)(Math.Clamp(c.A, 0f, 1f) * 255),
                (int)(Math.Clamp(c.R, 0f, 1f) * 255),
                (int)(Math.Clamp(c.G, 0f, 1f) * 255),
                (int)(Math.Clamp(c.B, 0f, 1f) * 255));

        private static PVectorColor Shade(PVectorColor c, float k)
            => new PVectorColor(
                Math.Clamp(c.R * k, 0f, 1f),
                Math.Clamp(c.G * k, 0f, 1f),
                Math.Clamp(c.B * k, 0f, 1f),
                c.A);

        private static void FillRamp(
            global::Prowl.OrigamiUI.OrigamiRamp ramp,
            global::System.Drawing.Color c100, global::System.Drawing.Color c200,
            global::System.Drawing.Color c300, global::System.Drawing.Color c400,
            global::System.Drawing.Color c500, global::System.Drawing.Color c600,
            global::System.Drawing.Color c700)
        {
            ramp.C100 = c100; ramp.C200 = c200; ramp.C300 = c300; ramp.C400 = c400;
            ramp.C500 = c500; ramp.C600 = c600; ramp.C700 = c700;
        }

        internal Prowl.Scribe.FontFile? Font => _font;

        /// <summary>The live Paper canvas, for host input forwarding.</summary>
        public PPaper Paper => _paper;

        internal Control PaperRoot => _paperRoot;
        internal Window HostWindow => _window;

        private PPaper NewPaper(int w, int h)
        {
            _paperW = w;
            _paperH = h;
            _renderer.SetViewport(w, h);
            var paper = new PPaper(_renderer, w, h, new global::Prowl.Quill.FontAtlasSettings());
            LoadFont(paper);
            return paper;
        }

        private void LoadFont(PPaper paper)
        {
            if (_font != null)
            {
                try { paper.AddFallbackFont(_font); return; } catch { }
            }

            string[] candidates =
            {
                @"C:\Windows\Fonts\segoeui.ttf",
                @"C:\Windows\Fonts\arial.ttf",
                @"C:\Windows\Fonts\calibri.ttf",
                @"/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                @"/usr/share/fonts/TTF/DejaVuSans.ttf",
                @"/System/Library/Fonts/SFNS.ttf",
            };
            foreach (var path in candidates)
            {
                try
                {
                    if (!System.IO.File.Exists(path)) continue;
                    _font = new Prowl.Scribe.FontFile(path);
                    paper.AddFallbackFont(_font);
                    return;
                }
                catch { }
            }
            try
            {
                foreach (var f in paper.EnumerateSystemFonts())
                {
                    if (f is Prowl.Scribe.FontFile ff)
                    {
                        paper.AddFallbackFont(ff);
                        _font = ff;
                        return;
                    }
                }
            }
            catch { }
        }

        // -- Window / factory -----------------------------------------------

        public IWindow RootWindow => _rootWindow;

        public IWindow CreateWindow(string title, WindowOptions opts)
        {
            var win = new Window
            {
                Title = title,
                Size = new Vector2I(opts.Width, opts.Height),
                Unresizable = !opts.Resizable,
                Borderless = opts.Borderless,
            };
            _window.CallDeferred(Node.MethodName.AddChild, win);
            return new ProwlWindow(win, this);
        }

        public IViewportHost CreateViewportHost() =>
            new Godot.UI.GodotViewportHost(new SubViewportContainer { Stretch = true });

        private T Reg<T>(T w) where T : ProwlWidget { w.Owner = this; _all.Add(w); return w; }

        public IButton CreateButton(string text, Action onClick) => Reg(new ProwlButton(text, onClick));
        public ILabel CreateLabel(string text) => Reg(new ProwlLabel(text));
        public ITextField CreateTextField(string placeholder, Action<string> onChanged) => Reg(new ProwlTextField(placeholder, onChanged));
        public IDropdown CreateDropdown(IEnumerable<string> options, Action<int> onSelected) => Reg(new ProwlDropdown(options, onSelected));
        public IVBox CreateVBox() => Reg(new ProwlVBox());
        public IHBox CreateHBox() => Reg(new ProwlHBox());
        public IScrollView CreateScrollView() => Reg(new ProwlScrollView());
        public IDock CreateDock(string title, DockPosition pos) => Reg(new ProwlDock(title));
        public ITabControl CreateTabControl() => Reg(new ProwlTabControl());
        public IToolbar CreateToolbar(string name) => Reg(new ProwlToolbar(name));
        public ITreeView CreateTreeView() => Reg(new ProwlTreeView());
        public IAssetGrid CreateAssetGrid() => Reg(new ProwlAssetGrid());
        public ICheckbox CreateCheckbox(string text, bool value) => Reg(new ProwlCheckbox(text, value));
        public IToggle CreateToggle(string text, bool toggled) => Reg(new ProwlToggle(text, toggled));
        public ISlider CreateSlider(float min, float max, float step, float value) => Reg(new ProwlSlider(min, max, step, value));
        public INumericField CreateNumericField(float value, string format = "F2") => Reg(new ProwlNumericField(value, format));
        public ISplitter CreateSplitter(SplitterOrientation o) => Reg(new ProwlSplitter(o));
        public IContextMenu CreateContextMenu() => new ProwlContextMenu(() => _paper);
        public IFilePicker CreateFilePicker() => new Godot.UI.GodotFilePicker();
        public void ApplyVariation(IWidget widget, string variation) { if (widget is ProwlWidget pw) pw.Variation = variation; }

        public IPanel CreatePanel(string name)
        {
            if (name == "GameView")
            {
                // Paper owns the pane: the 3D view is the SubViewport's texture
                // drawn inside the Paper canvas (no Godot Control overlay).
                var pw = Reg(new ProwlGameViewPanel(this));
                _gameView = pw;
                return pw;
            }
            return Reg(new ProwlPanel(name));
        }

        /// <summary>
        /// Hosts a 3D SubViewport inside the Paper UI: the SubViewport lives in this
        /// window and its texture is drawn by the game-view panel, while an invisible
        /// proxy Control carries the pane rect for viewport picking/input.
        /// </summary>
        public void AttachGameView(SubViewport viewport)
        {
            if (viewport.GetParent() == null)
                _paperRoot.AddChild(viewport);

            _gameTexture = viewport.GetTexture();

            _viewportProxy = new Control
            {
                Name = "ViewportInputProxy",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = true,
            };
            _paperRoot.AddChild(_viewportProxy);
            ApplyGameViewRect();
        }

        /// <summary>Rect of the game-view pane in window (Paper) coordinates.</summary>
        public global::Prowl.Vector.Rect GameViewRect => _gameRect;

        /// <summary>Invisible Control under the game view used for pointer/pick mapping.</summary>
        public Control? ViewportProxy => _viewportProxy;

        private void SetGameViewRect(global::Prowl.Vector.Rect rect)
        {
            _gameRect = rect;
            ApplyGameViewRect();
        }

        private void ApplyGameViewRect()
        {
            if (_viewportProxy == null || !GodotObject.IsInstanceValid(_viewportProxy)) return;
            _viewportProxy.Position = new Vector2(_gameRect.Min.X, _gameRect.Min.Y);
            _viewportProxy.Size = new Vector2(
                Math.Max(1f, _gameRect.Size.X),
                Math.Max(1f, _gameRect.Size.Y));
        }

        public IDockSpaceWidget CreateDockSpace() => Reg(new ProwlDockSpace(this));

        public ICodeEditor CreateCodeEditor() => Reg(new ProwlCodeEditor());

        /// <summary>
        /// Editor inspector, rendered by the same Paper/Origami adapter the engine uses
        /// in-game (see <see cref="V12.Core.UI.Paper.PaperInspectorAdapter"/>).
        /// </summary>
        public IWidget CreateInspectorView(object target, string? projectDir = null)
            => Reg(new ProwlInspectorView(target, projectDir));

        public IModal ShowModal(string title, string content)
        {
            try
            {
                var dlg = new AcceptDialog { Title = title, DialogText = content };
                _window.CallDeferred(Node.MethodName.AddChild, dlg);
                dlg.CallDeferred(Window.MethodName.PopupCentered);
                return new Godot.UI.GodotModal(dlg);
            }
            catch
            {
                return new NullModal(title, content);
            }
        }

        // -- Frame ----------------------------------------------------------

        public void ProcessFrame(double delta)
        {
            if (_disposed) return;
            try
            {
                var vp = _window.Size;
                int w = Math.Max(64, vp.X), h = Math.Max(64, vp.Y);
                if (w != _paperW || h != _paperH)
                    _paper = NewPaper(w, h);

                _paper.BeginFrame((float)delta, 1f);
                global::Prowl.OrigamiUI.Origami.BeginFrame(_paper, (float)delta);

                var childSet = new HashSet<ProwlWidget>();
                foreach (var wt in _all)
                    foreach (var c in wt.Children)
                        if (c is ProwlWidget cw) childSet.Add(cw);

                // BeginFrame leaves the root as the current parent, so roots attach directly.
                // Widgets owned by a dock tab render inside the dock, never as roots.
                foreach (var wt in _all)
                    if (!childSet.Contains(wt) && !wt.DockOwned && wt.Visible)
                        wt.Emit(_paper);

                global::Prowl.OrigamiUI.Origami.EndFrame(_paper);
                _paper.EndFrame();

                if (System.Environment.GetEnvironmentVariable("V12_PAPER_DEBUG") == "1" && ++_dbgFrame % 120 == 0)
                {
                    GD.Print($"[ProwlUIProvider] paper={_paper.Width}x{_paper.Height} window={vp} gameview={_gameRect.Min.X:F0},{_gameRect.Min.Y:F0} {_gameRect.Size.X:F0}x{_gameRect.Size.Y:F0}");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ProwlUIProvider] ProcessFrame: {ex}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _renderer.Dispose(); } catch { }
            if (GodotObject.IsInstanceValid(_paperLayer)) _paperLayer.QueueFree();
        }

        // -- Widget base ----------------------------------------------------

        public abstract class ProwlWidget : IWidget
        {
            internal ProwlUIProvider? Owner;
            internal int Id = ++_nextId;

            /// <summary>Flex share on the parent's main axis (null = intrinsic/fill).</summary>
            internal float? FlexW;
            internal float? FlexH;
            internal bool CrossAutoW;
            internal bool CrossAutoH;

            /// <summary>Owned by a dock tab: rendered inside the dock, not as a root.</summary>
            internal bool DockOwned;
            internal bool Fill = true;

            public string Variation { get; set; } = "";
            public bool Visible { get; set; } = true;
            public abstract object NativeControl { get; }
            public List<IWidget> Children { get; } = new();

            public abstract void Emit(PPaper paper);

            protected global::Prowl.PaperUI.ElementBuilder Box(PPaper paper, string kind, PLayout? layout = null, bool stretchW = true, bool stretchH = true)
            {
                var el = paper.Box($"{kind}#{Id}", Id, 0);
                if (layout.HasValue) el.LayoutType(layout.Value);

                if (FlexW.HasValue) el.Width(paper.Stretch(FlexW.Value));
                else if (CrossAutoW) el.Width(paper.Auto);
                else if (stretchW) el.Width(paper.Stretch());
                else el.Width(paper.Auto);

                if (FlexH.HasValue) el.Height(paper.Stretch(FlexH.Value));
                else if (CrossAutoH) el.Height(paper.Auto);
                else if (stretchH) el.Height(paper.Stretch());
                else el.Height(paper.Auto);

                return el;
            }

            /// <summary>Intrinsic-sized element for leaf widgets (buttons, labels, fields).
            /// Paper's default style is Stretch on both axes, so Auto must be explicit.</summary>
            protected global::Prowl.PaperUI.ElementBuilder Leaf(PPaper paper, string kind, PLayout? layout = null)
                => Box(paper, kind, layout, stretchW: false, stretchH: false);

            protected bool Text(global::Prowl.PaperUI.ElementBuilder el, string? s, float size = 13f, PVectorColor? color = null)
            {
                var font = Owner?.Font;
                if (font == null || string.IsNullOrEmpty(s)) return false;
                try
                {
                    el.Text(s, font).FontSize(size).TextColor(color ?? V12.Core.UI.Paper.PaperPalette.Text);
                    return true;
                }
                catch { return false; }
            }

            protected void EmitChildren(PPaper paper)
            {
                foreach (var c in Children.ToArray())
                    if (c is ProwlWidget cw && cw.Visible) cw.Emit(paper);
            }
        }

        // All chrome colors come from the shared Paper palette so a theme switch
        // retints the editor without touching widget code.
        private static PVectorColor PanelBg => V12.Core.UI.Paper.PaperPalette.Panel;
        private static PVectorColor PanelBgAlt => V12.Core.UI.Paper.PaperPalette.Raised;
        private static PVectorColor BarBg => V12.Core.UI.Paper.PaperPalette.Canvas;
        private static PVectorColor Accent => V12.Core.UI.Paper.PaperPalette.Accent;
        private static PVectorColor AccentDim => V12.Core.UI.Paper.PaperPalette.Selected;
        private static PVectorColor Border => V12.Core.UI.Paper.PaperPalette.Border;
        private static PVectorColor TextCol => V12.Core.UI.Paper.PaperPalette.Text;
        private static PVectorColor MutedCol => V12.Core.UI.Paper.PaperPalette.Muted;

        private static PVectorColor OnAccentCol => V12.Core.UI.Paper.PaperPalette.OnAccent;
        private static PVectorColor DisabledCol => V12.Core.UI.Paper.PaperPalette.Disabled;
        private static PVectorColor DangerCol => V12.Core.UI.Paper.PaperPalette.Danger;

        // -- Containers -----------------------------------------------------

        private sealed class ProwlWindow : IWindow
        {
            private readonly Window _win;
            public ProwlWindow(Window win, ProwlUIProvider _) { _win = win; }
            public object NativeControl => _win;
            public bool Visible { get => _win.Visible; set => _win.Visible = value; }
            public string Title { get => _win.Title; set => _win.Title = value; }
            public int Width { get => _win.Size.X; set => _win.Size = new Vector2I(value, _win.Size.Y); }
            public int Height { get => _win.Size.Y; set => _win.Size = new Vector2I(_win.Size.X, value); }
            public void Close() { if (GodotObject.IsInstanceValid(_win)) _win.QueueFree(); }
        }

        private sealed class ProwlPanel : ProwlWidget, IPanel
        {
            private readonly Control _dummy = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
            public ProwlPanel(string name) { Name = name; }
            public override object NativeControl => _dummy;
            public string Name { get; set; }
            public bool UseScrollableContent { get; set; }
            public float MinWidth { get; set; }
            private float _scrollY;
            public void AddChild(IWidget child) => Children.Add(child);
            public void RemoveChild(IWidget child) => Children.Remove(child);

            public override void Emit(PPaper paper)
            {
                var el = Box(paper, "panel", PLayout.Column)
                    .BackgroundColor(PanelBg)
                    .Padding(6)
                    .Clip()
                    .AlignItems(global::Prowl.PaperUI.LayoutAlignment.Stretch);

                // A component card stacks inside the inspector's scroll column; a
                // stretch height would split the visible pane between cards and clip
                // the fields past the first screenful.
                if (Variation == "ComponentCard")
                    el.Height(paper.Auto);
                if (UseScrollableContent)
                    el.OnScroll(e => { _scrollY = Math.Clamp(_scrollY - e.Delta * 24f, 0f, 5000f); });

                // The editor root spans the canvas: size it to the canvas in pixels.
                // Relying on Stretch alone collapses when the pane content is shorter
                // than the viewport (Paper only shares stretch space within a definite parent).
                if (string.Equals(Name, "Root", StringComparison.Ordinal))
                {
                    el.Width(paper.Pixels(paper.Width)).Height(paper.Pixels(paper.Height)).Padding(0);
                }

                if (MinWidth > 0) el.MinWidth(MinWidth);
                using (el.Enter())
                {
                    if (UseScrollableContent)
                    {
                        var inner = paper.Box($"panel_inner#{Id}", Id ^ 0x5a5a, 0)
                            .LayoutType(PLayout.Column).Gap(4f)
                            .AlignItems(global::Prowl.PaperUI.LayoutAlignment.Stretch)
                            .Translate(0, -_scrollY);
                        using (inner.Enter()) EmitChildren(paper);
                    }
                    else
                    {
                        EmitChildren(paper);
                    }
                }
            }
        }

        private sealed class ProwlGameViewPanel : ProwlWidget, IPanel
        {
            private readonly ProwlUIProvider _owner;
            private readonly Control _dummy = new() { MouseFilter = Control.MouseFilterEnum.Ignore };

            public ProwlGameViewPanel(ProwlUIProvider owner)
            {
                _owner = owner;
                Name = "GameView";
            }

            public override object NativeControl => _dummy;
            public string Name { get; set; }
            public bool UseScrollableContent { set { } }
            public float MinWidth { get; set; }
            public void AddChild(IWidget child) => Children.Add(child);
            public void RemoveChild(IWidget child) => Children.Remove(child);

            public override void Emit(PPaper paper)
            {
                var el = Box(paper, "gameview", PLayout.Column)
                    .AlignItems(global::Prowl.PaperUI.LayoutAlignment.Stretch)
                    .BackgroundColor(new PVectorColor(0.055f, 0.055f, 0.07f, 1f))
                    .Padding(0)
                    .Clip()
                    .OnPostLayout((_, r) => _owner.SetGameViewRect(r));

                using (el.Enter())
                {
                    var texture = _owner._gameTexture;
                    paper.Draw((canvas, rect) =>
                    {
                        if (texture == null) return;
                        canvas.DrawImage(texture,
                            (float)rect.Min.X, (float)rect.Min.Y,
                            (float)rect.Size.X, (float)rect.Size.Y);
                    });
                    EmitChildren(paper);
                }
            }
        }
        private sealed class ProwlVBox : ProwlWidget, IVBox
        {
            private readonly Control _dummy = new();
            public override object NativeControl => _dummy;
            public bool Expand { get; set; } = true;
            public void AddChild(IWidget child) => Children.Add(child);
            public void RemoveChild(IWidget child) => Children.Remove(child);

            public override void Emit(PPaper paper)
            {
                if (!Expand) CrossAutoW = true;
                var el = Box(paper, "vbox", PLayout.Column).Gap(4f).AlignItems(global::Prowl.PaperUI.LayoutAlignment.Stretch);
                // Content stacks (inspector fields): a stretch height collapses the
                // children inside an auto-sized card.
                if (Variation == "Content") el.Height(paper.Auto);
                using (el.Enter()) EmitChildren(paper);
            }
        }

        private sealed class ProwlInspectorView : ProwlWidget
        {
            private readonly Control _dummy = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
            private readonly object _target;
            private readonly string? _projectDir;

            public ProwlInspectorView(object target, string? projectDir)
            {
                _target = target;
                _projectDir = projectDir;
            }

            public override object NativeControl => _dummy;

            public override void Emit(PPaper paper)
            {
                // Auto height: the adapter's rows are the only content and must
                // stack inside the component card instead of filling the panel.
                var el = Box(paper, "inspector", PLayout.Column, stretchW: true, stretchH: false);
                using (el.Enter())
                {
                    var adapter = new V12.Core.UI.Paper.PaperInspectorAdapter(
                        paper,
                        $"inspector#{Id}",
                        filePicker: PickFile,
                        font: Owner?.Font)
                    {
                        LabelWidth = 110f,
                    };
                    adapter.Render(_target);
                }
            }

            private bool PickFile(string[] extensions, Action<string> onPicked)
            {
                try
                {
                    Owner?.CreateFilePicker().ShowOpen("Select asset", "Asset file", extensions,
                        picked =>
                        {
                            if (string.IsNullOrEmpty(picked)) return;
                            onPicked(ToProjectUri(picked!));
                        });
                    return true;
                }
                catch { return false; }
            }

            /// <summary>Absolute picked path inside the project becomes a v12:// URI,
            /// matching what the engine's asset loaders expect.</summary>
            private string ToProjectUri(string fullPath)
            {
                if (string.IsNullOrEmpty(_projectDir)) return fullPath;
                try
                {
                    var root = System.IO.Path.GetFullPath(_projectDir)
                        .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                    var full = System.IO.Path.GetFullPath(fullPath);
                    if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !full.StartsWith(root + System.IO.Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return fullPath;
                    var rel = full.Substring(root.Length)
                        .TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    return "v12://" + rel;
                }
                catch { return fullPath; }
            }
        }

        private sealed class ProwlHBox : ProwlWidget, IHBox
        {
            private readonly Control _dummy = new();
            public override object NativeControl => _dummy;
            public bool Expand { get; set; } = true;
            public void AddChild(IWidget child) => Children.Add(child);
            public void RemoveChild(IWidget child) => Children.Remove(child);
            public void AddChild(IWidget child, bool expand) { if (child is ProwlWidget pw) pw.FlexW = expand ? 1f : null; Children.Add(child); }

            public override void Emit(PPaper paper)
            {
                if (!Expand) CrossAutoH = true;

                // In a Row the cross axis (height) is resolved through AlignItems; a child
                // that asks for Height(Stretch) collapses to zero there, so hand children
                // Auto height and let the stretch alignment fill the row.
                foreach (var c in Children)
                    if (c is ProwlWidget cw) cw.CrossAutoH = true;

                var el = Box(paper, "hbox", PLayout.Row, stretchW: true, stretchH: false)
                    .Gap(6f)
                    .AlignItems(global::Prowl.PaperUI.LayoutAlignment.Stretch);
                using (el.Enter()) EmitChildren(paper);
            }
        }

        private sealed class ProwlSplitter : ProwlWidget, ISplitter
        {
            private readonly Control _dummy = new();
            private readonly SplitterOrientation _o;
            private IWidget? _first, _second;

            public ProwlSplitter(SplitterOrientation o) { _o = o; }
            public override object NativeControl => _dummy;
            public float SplitRatio { get; set; } = 0.5f;
            public void SetInitialMinimumPane(int paneIndex) { }
            public void SetOnSplitChanged(Action<float> cb) { }

            public void SetFirst(IWidget child)
            {
                _first = child;
                Rebuild();
            }

            public void SetSecond(IWidget child)
            {
                _second = child;
                Rebuild();
            }

            private void Rebuild()
            {
                Children.Clear();
                if (_first != null) Children.Add(_first);
                if (_second != null) Children.Add(_second);
                ApplyFlex();
            }

            private void ApplyFlex()
            {
                float r = Math.Clamp(SplitRatio, 0.02f, 0.98f);
                bool row = _o == SplitterOrientation.SideBySide;
                if (_first is ProwlWidget a)
                {
                    if (row) { a.FlexW = r; a.FlexH = null; a.CrossAutoH = true; a.CrossAutoW = false; }
                    else { a.FlexH = r; a.FlexW = null; a.CrossAutoW = true; a.CrossAutoH = false; }
                }
                if (_second is ProwlWidget b)
                {
                    if (row) { b.FlexW = 1f - r; b.FlexH = null; b.CrossAutoH = true; b.CrossAutoW = false; }
                    else { b.FlexH = 1f - r; b.FlexW = null; b.CrossAutoW = true; b.CrossAutoH = false; }
                }
            }

            public override void Emit(PPaper paper)
            {
                ApplyFlex();
                bool row = _o == SplitterOrientation.SideBySide;
                var el = Box(paper, "split", row ? PLayout.Row : PLayout.Column).Gap(3f).AlignItems(global::Prowl.PaperUI.LayoutAlignment.Stretch);
                using (el.Enter()) EmitChildren(paper);
            }
        }

        private sealed class ProwlToolbar : ProwlWidget, IToolbar
        {
            private readonly Control _dummy = new();
            public ProwlToolbar(string name) { Name = name; }
            public override object NativeControl => _dummy;
            private readonly string Name;
            public void AddItem(IWidget item) => Children.Add(item);
            public void RemoveItem(IWidget item) => Children.Remove(item);

            public override void Emit(PPaper paper)
            {
                var el = Box(paper, "toolbar", PLayout.Row, stretchW: true, stretchH: false)
                    .BackgroundColor(BarBg)
                    .Padding(5)
                    .Gap(6f)
                    .AlignItems(global::Prowl.PaperUI.LayoutAlignment.Center);
                el.MinHeight(34f);
                using (el.Enter()) EmitChildren(paper);
            }
        }

        private sealed class ProwlTabControl : ProwlWidget, ITabControl
        {
            private readonly Control _dummy = new();
            private readonly List<(string Name, IWidget Content)> _tabs = new();
            public override object NativeControl => _dummy;
            public int SelectedTabIndex { get; set; }
            public void AddTab(string name, IWidget content) { _tabs.Add((name, content)); Children.Add(content); }
            public void RemoveTab(string name)
            {
                int i = _tabs.FindIndex(t => t.Name == name);
                if (i < 0) return;
                var t = _tabs[i];
                _tabs.RemoveAt(i);
                Children.Remove(t.Content);
                if (SelectedTabIndex >= _tabs.Count) SelectedTabIndex = Math.Max(0, _tabs.Count - 1);
            }

            public override void Emit(PPaper paper)
            {
                var el = Box(paper, "tabs", PLayout.Column, stretchW: true, stretchH: true).Gap(0f);
                using (el.Enter())
                {
                    var bar = paper.Box($"tabbar#{Id}", Id, 0).LayoutType(PLayout.Row).Gap(2f)
                        .Width(paper.Stretch()).Height(18f)
                        .AlignItems(global::Prowl.PaperUI.LayoutAlignment.Center)
                        .BackgroundColor(BarBg).Padding(3);
                    using (bar.Enter())
                    {
                        for (int i = 0; i < _tabs.Count; i++)
                        {
                            int idx = i;
                            bool sel = i == SelectedTabIndex;
                            var tab = paper.Box($"tab#{Id}_{i}", Id ^ (i * 7919), 0)
                                .Width(paper.Auto).Height(paper.Auto)
                                .BackgroundColor(sel ? AccentDim : PanelBgAlt)
                                .Padding(6)
                                .OnClick(_ => SelectedTabIndex = idx);
                            Text(tab, _tabs[i].Name, 15f, sel ? OnAccentCol : MutedCol);
                        }
                    }

                    var body = paper.Box($"tabbody#{Id}", Id ^ 0x5151, 0)
                        .LayoutType(PLayout.Column).Width(paper.Stretch()).Height(paper.Stretch())
                        .BackgroundColor(PanelBgAlt).Padding(6).Clip();
                    using (body.Enter())
                    {
                        if (SelectedTabIndex >= 0 && SelectedTabIndex < _tabs.Count
                            && _tabs[SelectedTabIndex].Content is ProwlWidget pw && pw.Visible)
                            pw.Emit(paper);
                    }
                }
            }
        }

        private sealed class ProwlScrollView : ProwlWidget, IScrollView
        {
            private readonly Control _dummy = new();
            private IWidget? _content;
            public override object NativeControl => _dummy;
            public void SetContent(IWidget content) { _content = content; Children.Clear(); if (content != null) Children.Add(content); }

            public override void Emit(PPaper paper)
            {
                var el = Box(paper, "scroll", PLayout.Column).BackgroundColor(PanelBg).Clip().AlignItems(global::Prowl.PaperUI.LayoutAlignment.Stretch);
                using (el.Enter()) EmitChildren(paper);
            }
        }

        private sealed class ProwlDock : ProwlWidget, IDock
        {
            private readonly Control _dummy = new();
            public ProwlDock(string title) { Title = title; }
            public override object NativeControl => _dummy;
            public string Title { get; set; }
            public void AddChild(IWidget child) => Children.Add(child);
            public void RemoveChild(IWidget child) => Children.Remove(child);

            public override void Emit(PPaper paper)
            {
                var el = Box(paper, "dock", PLayout.Column).BackgroundColor(PanelBg).Padding(4).Clip().AlignItems(global::Prowl.PaperUI.LayoutAlignment.Stretch);
                using (el.Enter()) EmitChildren(paper);
            }
        }

        // -- Leaf widgets ---------------------------------------------------

        private sealed class ProwlLabel : ProwlWidget, ILabel
        {
            private readonly Control _dummy = new();
            public ProwlLabel(string t) { Text = t; }
            public override object NativeControl => _dummy;
            public string Text { get; set; }
            public float MinWidth { get; set; }

            public override void Emit(PPaper paper)
            {
                var el = Leaf(paper, "label").Padding(2);
                if (MinWidth > 0) el.MinWidth(MinWidth);
                Text(el, Text, 15f, MutedCol);
            }
        }

        private sealed class ProwlButton : ProwlWidget, IButton
        {
            private readonly Control _dummy = new();
            private Action _onClick;
            public ProwlButton(string t, Action onClick) { Text = t; _onClick = onClick; }
            public override object NativeControl => _dummy;
            public string Text { get; set; }
            public bool Enabled { get; set; } = true;
            public void SetOnClick(Action onClick) => _onClick = onClick;

            public override void Emit(PPaper paper)
            {
                var bg = Enabled ? Accent : PanelBgAlt;
                var fg = Enabled ? OnAccentCol : DisabledCol;
                if (Variation == "ComponentHeader") { bg = PanelBgAlt; fg = TextCol; }
                else if (Variation == "DangerButton") { bg = DangerCol; fg = OnAccentCol; }

                var el = Leaf(paper, "button").Height(26f)
                    .BackgroundColor(bg)
                    .Padding(7)
                    .OnClick(_ => { if (Enabled) _onClick?.Invoke(); });
                Text(el, Text, 15f, fg);
            }
        }

        private sealed class ProwlTextField : ProwlWidget, ITextField
        {
            private readonly Control _dummy = new();
            private Action<string> _onChanged;
            public ProwlTextField(string placeholder, Action<string> onChanged) { Placeholder = placeholder; _onChanged = onChanged; }
            public override object NativeControl => _dummy;
            public string Text { get; set; } = "";
            public string Placeholder { get; set; }
            public bool Enabled { get; set; } = true;
            public void SetOnChanged(Action<string> cb) => _onChanged = cb;

            public override void Emit(PPaper paper)
            {
                var field = global::Prowl.OrigamiUI.Origami
                    .TextField(paper, $"{Id}_field", Text ?? "", v =>
                    {
                        Text = v;
                        _onChanged?.Invoke(v);
                    });

                if (Variation == "SearchField")
                    field.Search(string.IsNullOrEmpty(Placeholder) ? "Search..." : Placeholder);
                else
                    field.Placeholder(Placeholder ?? "");

                if (!Enabled) field.ReadOnly();
                field.Height(26f).Show();
            }
        }

        private sealed class ProwlCodeEditor : ProwlWidget, ICodeEditor
        {
            private readonly Control _dummy = new();
            private readonly global::Prowl.OrigamiUI.CodeEditor _editor;
            private Action<string>? _onChanged;
            private Action? _onSave;

            public ProwlCodeEditor()
            {
                _editor = new global::Prowl.OrigamiUI.CodeEditor
                {
                    Language = global::V12.Core.UI.V12CodeLanguages.CSharp,
                };
                _editor.TextChanged += _ => _onChanged?.Invoke(_editor.Text);
                _editor.SaveRequested += () => _onSave?.Invoke();
            }

            public override object NativeControl => _dummy;

            public string Text
            {
                get => _editor.Text;
                set => _editor.Text = value ?? "";
            }

            public string FilePath
            {
                get => _editor.FilePath ?? "";
                set => _editor.FilePath = value;
            }

            public bool ReadOnly
            {
                get => _editor.Options.ReadOnly;
                set => _editor.Options.ReadOnly = value;
            }

            public void SetLanguage(string languageId)
                => _editor.Language = global::V12.Core.UI.V12CodeLanguages.ForId(languageId);

            public void SetCompilerLanguage(global::Prowl.OrigamiUI.CodeLanguage language)
                => _editor.Language = language;

            public void SetOnChanged(Action<string> onChanged) => _onChanged = onChanged;
            public void SetOnSaveRequested(Action onSave) => _onSave = onSave;
            public void Focus() { }

            public override void Emit(PPaper paper)
            {
                var el = Box(paper, "codeeditor", PLayout.Column).Clip();
                using (el.Enter())
                {
                    global::Prowl.OrigamiUI.Origami
                        .CodeEditor(paper, $"{Id}_code", _editor)
                        .Size(paper.Stretch(), paper.Stretch())
                        .Show();
                }
            }
        }
        private sealed class ProwlDropdown : ProwlWidget, IDropdown
        {
            private readonly Control _dummy = new();
            private List<string> _options;
            private Action<int> _onSelected;
            public ProwlDropdown(IEnumerable<string> options, Action<int> onSelected)
            {
                _options = new List<string>(options);
                _onSelected = onSelected;
            }
            public override object NativeControl => _dummy;
            public int SelectedIndex { get; set; } = -1;
            public void SetOptions(IEnumerable<string> options) => _options = new List<string>(options);
            public void SetOnSelected(Action<int> cb) => _onSelected = cb;

            public override void Emit(PPaper paper)
            {
                string label = SelectedIndex >= 0 && SelectedIndex < _options.Count ? _options[SelectedIndex] : "--";
                var el = Leaf(paper, "dropdown").Height(24f)
                    .BackgroundColor(PanelBgAlt)
                    .Padding(6).BorderWidth(1f).BorderColor(Border)
                    .OnClick(_ => ShowMenu());
                Text(el, label + "  v", 14f, TextCol);
            }

            /// <summary>
            /// Host-native popup list (Godot PopupMenu) anchored at the pointer. Paper has no
            /// popup layer, so the menu is a real OS/window popup and reuses the studio theme.
            /// </summary>
            private void ShowMenu()
            {
                if (_options.Count == 0 || Owner == null) return;
                try
                {
                    var menu = new PopupMenu { Name = "ProwlDropdownMenu" };
                    for (int i = 0; i < _options.Count; i++)
                        menu.AddItem(_options[i], i);
                    if (SelectedIndex >= 0 && SelectedIndex < _options.Count)
                        menu.SetItemChecked(SelectedIndex, true);

                    menu.IdPressed += id =>
                    {
                        SelectedIndex = (int)id;
                        try { _onSelected?.Invoke(SelectedIndex); } catch { }
                        menu.QueueFree();
                    };
                    menu.PopupHide += () => menu.QueueFree();

                    Owner.PaperRoot.AddChild(menu);
                    var pos = Owner.HostWindow.GetMousePosition();
                    menu.Popup(new Rect2I((int)pos.X, (int)pos.Y, 1, 1));
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[ProwlUIProvider] dropdown popup failed: {ex.Message}");
                }
            }
        }

        private sealed class ProwlCheckbox : ProwlWidget, ICheckbox
        {
            private readonly Control _dummy = new();
            private Action<bool>? _cb;
            public ProwlCheckbox(string text, bool value) { Text = text; Checked = value; }
            public override object NativeControl => _dummy;
            public string Text { get; set; }
            public bool Checked { get; set; }
            public bool Enabled { get; set; } = true;
            public void SetOnChanged(Action<bool> cb) => _cb = cb;

            public override void Emit(PPaper paper)
            {
                var row = Leaf(paper, "checkbox", PLayout.Row).Height(20f).LayoutType(PLayout.Row).Gap(6f).Padding(2)
                    .OnClick(_ => { if (Enabled) { Checked = !Checked; _cb?.Invoke(Checked); } });
                using (row.Enter())
                {
                    paper.Box($"checkmark#{Id}", Id ^ 0x77, 0)
                        .Width(14f).Height(14f)
                        .BackgroundColor(Checked ? Accent : PanelBgAlt)
                        .BorderWidth(1f).BorderColor(Border);
                    var lbl = paper.Box($"checklabel#{Id}", Id ^ 0x78, 0).Padding(1);
                    Text(lbl, Text, 15f, TextCol);
                }
            }
        }

        private sealed class ProwlToggle : ProwlWidget, IToggle
        {
            private readonly Control _dummy = new();
            private Action<bool>? _cb;
            public ProwlToggle(string text, bool toggled) { Text = text; Toggled = toggled; }
            public override object NativeControl => _dummy;
            public string Text { get; set; }
            public bool Toggled { get; set; }
            public bool Enabled { get; set; } = true;
            public void SetOnToggled(Action<bool> cb) => _cb = cb;

            public override void Emit(PPaper paper)
            {
                var el = Leaf(paper, "toggle").Height(24f)
                    .BackgroundColor(Toggled ? Accent : PanelBgAlt)
                    .Padding(6)
                    .OnClick(_ => { if (Enabled) { Toggled = !Toggled; _cb?.Invoke(Toggled); } });
                Text(el, Text, 15f, Toggled ? OnAccentCol : TextCol);
            }
        }

        private sealed class ProwlSlider : ProwlWidget, ISlider
        {
            private readonly Control _dummy = new();
            private Action<float>? _cb;
            public ProwlSlider(float min, float max, float step, float value)
            {
                Min = min; Max = max; Step = step; Value = value;
            }
            public override object NativeControl => _dummy;
            public float Min { get; set; }
            public float Max { get; set; }
            public float Step { get; set; }
            public float Value { get; set; }
            public bool Enabled { get; set; } = true;
            public void SetOnChanged(Action<float> cb) => _cb = cb;

            public override void Emit(PPaper paper)
            {
                float t = Max > Min ? Math.Clamp((Value - Min) / (Max - Min), 0f, 1f) : 0f;
                var el = paper.Box($"slider#{Id}", Id, 0)
                    .Width(paper.Stretch()).Height(20f)
                    .OnClick(_ =>
                    {
                        // Click-to-set approximation; drag scrubbing lives in Origami.
                        Value = Min + (Max - Min) * 0.5f;
                        _cb?.Invoke(Value);
                    });
                using (el.Enter())
                {
                    paper.Draw((canvas, rect) =>
                    {
                        float rx = rect.Min.X, ry = rect.Min.Y;
                        float rw = rect.Max.X - rect.Min.X, rh = rect.Max.Y - rect.Min.Y;
                        float y = ry + rh * 0.5f;
                        canvas.BeginPath();
                        canvas.Rect(rx, y - 2f, rw, 4f);
                        canvas.SetFillColor(new PColor32(45, 45, 55, 255));
                        canvas.Fill();
                        canvas.BeginPath();
                        canvas.Rect(rx, y - 2f, rw * t, 4f);
                        canvas.SetFillColor(new PColor32(70, 120, 220, 255));
                        canvas.Fill();
                        canvas.BeginPath();
                        canvas.Circle(rx + rw * t, y, 7f, 20);
                        canvas.SetFillColor(new PColor32(210, 210, 225, 255));
                        canvas.Fill();
                    });
                }
            }
        }

        private sealed class ProwlNumericField : ProwlWidget, INumericField
        {
            private readonly Control _dummy = new();
            private Action<float>? _cb;
            public ProwlNumericField(float value, string format) { Value = value; Format = format; }
            public override object NativeControl => _dummy;
            public float Value { get; set; }
            public string Format { get; set; }
            public bool Enabled { get; set; } = true;
            public void SetOnChanged(Action<float> cb) => _cb = cb;

            public override void Emit(PPaper paper)
            {
                var el = Leaf(paper, "numfield").Height(22f)
                    .BackgroundColor(PanelBgAlt)
                    .Padding(5).BorderWidth(1f).BorderColor(Border);
                Text(el, Value.ToString(Format), 14f, TextCol);
            }
        }

        private sealed class ProwlAssetGrid : ProwlWidget, IAssetGrid
        {
            private readonly Control _dummy = new();
            private readonly List<AssetGridEntry> _items = new();
            private Action<object?>? _onSel;
            private Action<object>? _onActivate;
            public override object NativeControl => _dummy;
            public object? SelectedUserData { get; private set; }
            public void SetItems(IEnumerable<AssetGridEntry> items) { _items.Clear(); _items.AddRange(items); }
            public void SetOnSelectionChanged(Action<object?> cb) => _onSel = cb;
            public void SetOnItemActivated(Action<object> cb) => _onActivate = cb;

            public override void Emit(PPaper paper)
            {
                var el = Box(paper, "assetgrid", PLayout.Column).BackgroundColor(PanelBg).Padding(4).Clip();
                using (el.Enter())
                {
                    int i = 0;
                    foreach (var item in _items)
                    {
                        int idx = i++;
                        var row = paper.Box($"assetrow#{Id}_{idx}", Id ^ (idx * 104729), 0)
                            .LayoutType(PLayout.Row).Gap(6f).Padding(3)
                            .Width(paper.Stretch()).Height(20f)
                            .AlignItems(global::Prowl.PaperUI.LayoutAlignment.Center)
                            .BackgroundColor(item.UserData == SelectedUserData ? AccentDim : PanelBgAlt)
                            .OnClick(_ =>
                            {
                                SelectedUserData = item.UserData;
                                _onSel?.Invoke(item.UserData);
                            })
                            .OnDoubleClick(_ => _onActivate?.Invoke(item.UserData));

                        var icon = ComponentIcons.Resolve(item.IconKey);
                        if (icon != null)
                        {
                            var ico = paper.Box($"assetico#{Id}_{idx}", Id ^ (idx * 104729) ^ 0x21, 0)
                                .Width(16f).Height(20f).IsNotInteractable();
                            global::Prowl.OrigamiUI.OrigamiIconDraw.Icon(
                                ico, paper, icon,
                                global::Prowl.OrigamiUI.Origami.Current.Ink.C400, size: 13f);
                        }
                        Text(row, item.Name, 14f, TextCol);
                    }
                }
            }
        }

        // -- Tree -----------------------------------------------------------

        private sealed class ProwlTreeItem : ITreeItem
        {
            private static int _seq;

            public ProwlTreeItem(ITreeItem? parent, string label, object? userData)
            {
                Parent = parent as ProwlTreeItem;
                Label = label;
                UserData = userData;
            }
            public ProwlTreeItem? Parent { get; }
            public readonly List<ProwlTreeItem> Kids = new();
            public object? UserData { get; }
            public string Label { get; set; }
            public string? IconKey { get; set; }
            public string? TrailingIconKey { get; set; }
            public bool TrailingActive { get; set; }
            public bool Expanded { get; set; } = true;
            public bool Visible { get; set; } = true;
            internal readonly int Seq = System.Threading.Interlocked.Increment(ref _seq);
        }

        private sealed class ProwlTreeView : ProwlWidget, ITreeView
        {
            private readonly Control _dummy = new();
            private readonly List<ProwlTreeItem> _roots = new();
            private readonly List<ProwlTreeItem> _all = new();
            private ProwlTreeItem? _selected;
            private Action<ITreeItem?>? _onSel;
            private Action<ITreeItem, string>? _onRenamed;
            private Action<ITreeItem, ITreeItem?, TreeDropPosition>? _onDropped;
            private Func<ITreeItem, IContextMenu?>? _onMenu;
            private Action<ITreeItem>? _onTrailing;
            public override object NativeControl => _dummy;

            private float _w, _h;
            private ProwlTreeItem? _dragItem;
            private ProwlTreeItem? _hoverItem;
            private float _hoverY;
            private global::Prowl.Vector.Float2 _dragStart;
            private ProwlTreeItem? _renaming;
            private bool _dropEnabled;
            private string _emptyMessage = "Scene is empty.";

            private const float TreeRowHeight = 24f;

            public ITreeItem AddItem(ITreeItem? parent, string label, object? userData = null)
            {
                var item = new ProwlTreeItem(parent, label, userData);
                if (item.Parent != null) item.Parent.Kids.Add(item);
                else _roots.Add(item);
                _all.Add(item);
                return item;
            }

            public bool RemoveItem(ITreeItem item)
            {
                if (item is not ProwlTreeItem ti) return false;
                bool removed = ti.Parent != null ? ti.Parent.Kids.Remove(ti) : _roots.Remove(ti);
                if (removed) RemoveRecursive(ti);
                return removed;
            }

            private void RemoveRecursive(ProwlTreeItem ti)
            {
                _all.Remove(ti);
                foreach (var k in ti.Kids.ToArray()) RemoveRecursive(k);
            }

            public void Clear() { _roots.Clear(); _all.Clear(); _selected = null; _renaming = null; }
            public ITreeItem? SelectedItem => _selected;
            public void SetSelected(ITreeItem? item) => _selected = item as ProwlTreeItem;

            public void ScrollTo(ITreeItem item)
            {
                if (item is not ProwlTreeItem ti) return;
                int index = 0;
                if (!IndexOfVisible(ti, ref index)) return;
                try
                {
                    global::Prowl.OrigamiUI.Origami.ScrollTo(
                        $"{Id}_tree_scroll",
                        new global::Prowl.Vector.Float2(0f, index * TreeRowHeight - 40f));
                }
                catch { }
            }

            private bool IndexOfVisible(ProwlTreeItem target, ref int index)
            {
                foreach (var root in _roots)
                    if (Walk(root, ref index, target)) return true;
                return false;
            }

            private static bool Walk(ProwlTreeItem item, ref int index, ProwlTreeItem target)
            {
                if (!item.Visible) return false;
                if (ReferenceEquals(item, target)) return true;
                index++;
                if (item.Expanded)
                    foreach (var kid in item.Kids)
                        if (Walk(kid, ref index, target)) return true;
                return false;
            }

            public void SetOnSelectionChanged(Action<ITreeItem?> cb) => _onSel = cb;

            public void BeginRename(ITreeItem item)
            {
                if (item is not ProwlTreeItem ti) return;
                for (var p = ti.Parent; p != null; p = p.Parent) p.Expanded = true;
                _renaming = ti;
            }

            public void SetOnRenamed(Action<ITreeItem, string> cb) => _onRenamed = cb;
            public void SetDropEnabled(bool enabled) => _dropEnabled = enabled;
            public void SetOnItemDropped(Action<ITreeItem, ITreeItem?, TreeDropPosition> cb) => _onDropped = cb;
            public void SetOnContextMenuRequested(Func<ITreeItem, IContextMenu?> cb) => _onMenu = cb;
            public void SetOnTrailingIconClicked(Action<ITreeItem> cb) => _onTrailing = cb;
            public void SetEmptyMessage(string message) => _emptyMessage = string.IsNullOrEmpty(message) ? "Scene is empty." : message;

            public override void Emit(PPaper paper)
            {
                var el = Box(paper, "tree", PLayout.Column).BackgroundColor(PanelBg).Padding(2);
                el.OnPostLayout((_, r) => { _w = (float)r.Size.X; _h = (float)r.Size.Y; });
                using (el.Enter())
                {
                    // The panel lays the tree out at its final size after the first
                    // layout pass; draw on the next frame rather than at 0x0.
                    if (_w < 24f || _h < 24f) return;

                    if (_dragItem != null
                        && paper.IsPointerReleased(global::Prowl.PaperUI.PaperMouseBtn.Left))
                        FinishDrag(paper);

                    var nodes = new List<global::Prowl.OrigamiUI.TreeNode>(_all.Count);
                    foreach (var r in _roots) Collect(r, 0, nodes);

                    global::Prowl.OrigamiUI.Origami
                        .Tree(paper, $"{Id}_tree", _w - 4f, _h - 4f)
                        .Nodes(nodes)
                        .RowHeight(TreeRowHeight)
                        .IndentSize(14f)
                        .BaseIndent(4f)
                        .IsSelected(n => ReferenceEquals(n.UserData, _selected))
                        .OnSelect(e =>
                        {
                            _selected = e.Node.UserData as ProwlTreeItem;
                            _onSel?.Invoke(_selected);
                        })
                        .OnExpandChanged((n, expanded) =>
                        {
                            if (n.UserData is ProwlTreeItem it) it.Expanded = expanded;
                        })
                        .OnRightClick(e =>
                        {
                            if (e.Node.UserData is not ProwlTreeItem it) return;
                            _selected = it;
                            _onSel?.Invoke(it);
                            _onMenu?.Invoke(it)?.ShowAt(new NullWidget());
                        })
                        .OnDragStart(n =>
                        {
                            if (!_dropEnabled || n.UserData is not ProwlTreeItem it) return;
                            _dragItem = it;
                            _hoverItem = null;
                            _dragStart = paper.PointerPos;
                        })
                        .OnHover((n, normY) =>
                        {
                            if (_dragItem == null || n.UserData is not ProwlTreeItem it) return;
                            _hoverItem = it;
                            _hoverY = normY;
                        })
                        .CustomRowContent((p, n, sel, _) => DrawRow(p, n, sel))
                        .EmptyMessage(_emptyMessage)
                        .Show();
                }
            }

            private void FinishDrag(PPaper paper)
            {
                var dragged = _dragItem;
                var target = _hoverItem;
                var where = TreeDropPosition.Root;
                if (target != null)
                {
                    where = _hoverY < 0.28f ? TreeDropPosition.Before
                          : _hoverY > 0.72f ? TreeDropPosition.After
                          : TreeDropPosition.Child;
                }

                _dragItem = null;
                _hoverItem = null;

                if (!_dropEnabled || dragged == null || _onDropped == null) return;
                var delta = paper.PointerPos - _dragStart;
                if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 4f) return;
                if (ReferenceEquals(target, dragged)) return;
                if (target != null && IsAncestor(dragged, target)) return;

                if (System.Environment.GetEnvironmentVariable("V12_PAPER_DEBUG") == "1")
                    System.Console.WriteLine($"[TreeDrag] {dragged.Label} -> {(target?.Label ?? "<root>")} {where}");
                try { _onDropped(dragged, target, where); } catch { }
            }

            private static bool IsAncestor(ProwlTreeItem ancestor, ProwlTreeItem node)
            {
                for (var p = node.Parent; p != null; p = p.Parent)
                    if (ReferenceEquals(p, ancestor)) return true;
                return false;
            }

            private void Collect(ProwlTreeItem item, int depth, List<global::Prowl.OrigamiUI.TreeNode> nodes)
            {
                if (!item.Visible) return;

                var node = new global::Prowl.OrigamiUI.TreeNode
                {
                    Id = NodeKey(item),
                    Label = item.Label,
                    Depth = depth,
                    HasChildren = item.Kids.Count > 0,
                    DefaultExpanded = item.Expanded,
                    OverrideExpanded = item.Expanded ? true : null,
                    UserData = item,
                    IconDraw = ComponentIcons.Resolve(item.IconKey ?? "element"),
                    IconColor = IconColor(item.IconKey),
                    IsRenaming = ReferenceEquals(item, _renaming),
                };

                if (_dragItem != null && ReferenceEquals(_hoverItem, item)
                    && !ReferenceEquals(_dragItem, item) && !IsAncestor(_dragItem, item))
                {
                    node.DropIndicator = _hoverY < 0.28f
                        ? global::Prowl.OrigamiUI.TreeDropPosition.Above
                        : _hoverY > 0.72f
                            ? global::Prowl.OrigamiUI.TreeDropPosition.Below
                            : global::Prowl.OrigamiUI.TreeDropPosition.Into;
                }

                nodes.Add(node);
                foreach (var k in item.Kids) Collect(k, depth + 1, nodes);
            }

            private static string NodeKey(ProwlTreeItem item)
                => item.UserData is V12.Core.Core.Interfaces.IWorldElement el
                    ? $"el{el.Id}"
                    : $"n{item.Seq}";

            private static System.Drawing.Color? IconColor(string? key)
            {
                var t = global::Prowl.OrigamiUI.Origami.Current;
                return key switch
                {
                    "camera" => t.Blue.C400,
                    "light" => t.Amber.C400,
                    "audio" => t.Green.C400,
                    "script" => t.Green.C400,
                    "ui" => t.Primary.C400,
                    _ => t.Ink.C400,
                };
            }

            private void DrawRow(PPaper paper, global::Prowl.OrigamiUI.TreeNode node, bool isSelected)
            {
                var theme = global::Prowl.OrigamiUI.Origami.Current;
                var font = theme.Font;
                if (font == null || node.UserData is not ProwlTreeItem item) return;

                if (node.IsRenaming)
                {
                    using (paper.Box("tree_rename")
                        .Width(global::Prowl.PaperUI.LayoutEngine.UnitValue.StretchOne)
                        .Height(TreeRowHeight).Enter())
                    {
                        global::Prowl.OrigamiUI.Origami.TextField(
                                paper, "tree_rename_field", item.Label,
                                v =>
                                {
                                    if (_renaming == null) return;
                                    _renaming = null;
                                    item.Label = v;
                                    _onRenamed?.Invoke(item, v);
                                })
                            .AutoFocus()
                            .CommitOnEnter()
                            .Show();
                    }
                    return;
                }

                var icon = ComponentIcons.Resolve(item.IconKey ?? "element");
                var iconBox = paper.Box("tree_ico").Width(18f).Height(TreeRowHeight).IsNotInteractable();
                if (icon != null)
                    global::Prowl.OrigamiUI.OrigamiIconDraw.Icon(
                        iconBox, paper, icon, node.IconColor ?? theme.Ink.C400, size: 14f);

                paper.Box("tree_name")
                    .Height(TreeRowHeight).PaddingLeft(4)
                    .Text(item.Label, font)
                    .FontSize(13f)
                    .TextColor(isSelected ? theme.Ink.C700 : theme.Ink.C500)
                    .Alignment(global::Prowl.PaperUI.TextAlignment.MiddleLeft);

                bool active = item.TrailingActive;
                var eye = ComponentIcons.Resolve(active ? "eye" : "eye-slash");
                var vis = paper.Box("tree_vis")
                    .Width(18f).Height(TreeRowHeight)
                    .Cursor(global::Prowl.PaperUI.PaperCursor.Pointer);
                if (eye != null)
                    global::Prowl.OrigamiUI.OrigamiIconDraw.Icon(
                        vis, paper, eye, active ? theme.Ink.C400 : theme.Ink.C200, size: 13f);
                vis.OnClick(e =>
                {
                    e.StopPropagation();
                    _onTrailing?.Invoke(item);
                });
            }
        }
        // -- Docking --------------------------------------------------------

        private sealed class DockTabsNode : IDockNode
        {
            public readonly List<DockTab> Tabs = new();
        }

        private sealed class DockSplitNode : IDockNode
        {
            public DockOrientation Orientation;
            public float Ratio;
            public IDockNode First = null!;
            public IDockNode Second = null!;
        }

        /// <summary>
        /// Editor dock surface backed by Origami's real <c>DockSpace</c>. The tree is
        /// rebuilt only when its shape changes, so user drags / splits / torn-out tabs
        /// survive; each tab body renders its retained V12 widget.
        /// </summary>
        private sealed class ProwlDockSpace : ProwlWidget, IDockSpaceWidget
        {
            private readonly Control _dummy = new();
            private readonly ProwlUIProvider _owner;

            public override object NativeControl => _dummy;
            private readonly Dictionary<IWidget, EditorDockPanel> _adapters = new();

            private IDockNode? _root;
            private string _signature = "";
            private string? _pendingTab;
            private global::Prowl.OrigamiUI.DockSpace? _space;
            private global::Prowl.Vector.Rect _rect;
            private bool _hasRect;

            public ProwlDockSpace(ProwlUIProvider owner) { _owner = owner; }

            public IDockNode Tabs(params DockTab[] tabs)
            {
                var node = new DockTabsNode();
                node.Tabs.AddRange(tabs);
                foreach (var tab in tabs)
                    if (tab.Content is ProwlWidget pw) pw.DockOwned = true;
                return node;
            }

            public IDockNode Split(DockOrientation orientation, float ratio, IDockNode first, IDockNode second)
                => new DockSplitNode { Orientation = orientation, Ratio = ratio, First = first, Second = second };

            public void SetRoot(IDockNode root) => _root = root;

            public void ShowTab(string title)
            {
                _pendingTab = title;
                ApplyPendingTab();
            }

            private void ApplyPendingTab()
            {
                if (_pendingTab == null || _space?.Root == null) return;
                if (ActivateIn(_space.Root, _pendingTab)) _pendingTab = null;
            }

            private static bool ActivateIn(global::Prowl.OrigamiUI.DockNode node, string title)
            {
                if (node.Tabs != null)
                {
                    for (int i = 0; i < node.Tabs.Count; i++)
                    {
                        if (!string.Equals(node.Tabs[i].Title, title, StringComparison.Ordinal)) continue;
                        node.ActiveTabIndex = i;
                        return true;
                    }
                    return false;
                }

                return (node.ChildA != null && ActivateIn(node.ChildA, title))
                    || (node.ChildB != null && ActivateIn(node.ChildB, title));
            }

            public override void Emit(PPaper paper)
            {
                var el = Box(paper, "dockspace", PLayout.Column).Clip().Padding(0)
                    .OnPostLayout((_, r) => { _rect = r; _hasRect = true; });

                float w = _hasRect ? Math.Max(1f, _rect.Max.X - _rect.Min.X) : paper.Width;
                float h = _hasRect ? Math.Max(1f, _rect.Max.Y - _rect.Min.Y) : paper.Height;

                // Origami builds Paper elements for the dock chrome; they must be children
                // of this box so (0,0) is the pane origin, not the canvas origin.
                using (el.Enter())
                {
                    Sync();
                    ApplyPendingTab();
                    _space?.Draw(paper, 0f, 0f, w, h);
                }
            }

            private void Sync()
            {
                if (_root == null) return;
                string sig = Signature(_root);
                if (_space != null && sig == _signature) return;
                _signature = sig;
                _space = new global::Prowl.OrigamiUI.DockSpace(Build(_root));
            }

            private static string Signature(IDockNode node) => node switch
            {
                DockTabsNode t => "[" + string.Join(",", t.Tabs.Select(x => x.Title)) + "]",
                DockSplitNode s => $"({s.Orientation},{s.Ratio:F3},{Signature(s.First)},{Signature(s.Second)})",
                _ => "?",
            };

            private global::Prowl.OrigamiUI.DockNode Build(IDockNode node)
            {
                if (node is DockTabsNode tabs)
                {
                    var panels = tabs.Tabs.Select(tab =>
                    {
                        if (!_adapters.TryGetValue(tab.Content, out var adapter))
                        {
                            adapter = new EditorDockPanel(_owner, (ProwlWidget)tab.Content);
                            _adapters[tab.Content] = adapter;
                        }
                        adapter.Configure(tab.Title, tab.Closable);
                        return (global::Prowl.OrigamiUI.DockPanel)adapter;
                    }).ToArray();

                    return global::Prowl.OrigamiUI.DockNode.Leaf(panels);
                }

                var split = (DockSplitNode)node;
                return global::Prowl.OrigamiUI.DockNode.Split(
                    split.Orientation == DockOrientation.Horizontal
                        ? global::Prowl.OrigamiUI.SplitDirection.Horizontal
                        : global::Prowl.OrigamiUI.SplitDirection.Vertical,
                    Math.Clamp(split.Ratio, 0.05f, 0.95f),
                    Build(split.First),
                    Build(split.Second));
            }
        }

        /// <summary>Origami dock tab whose body renders a retained V12 widget.</summary>
        private sealed class EditorDockPanel : global::Prowl.OrigamiUI.DockPanel
        {
            private readonly ProwlWidget _content;
            private string _title = "Panel";

            public EditorDockPanel(ProwlUIProvider _, ProwlWidget content) { _content = content; }

            public void Configure(string title, bool closable)
            {
                _title = string.IsNullOrEmpty(title) ? "Panel" : title;
                IsOpen = true;
            }

            public override string Title => _title;

            public override void OnGUI(PPaper paper, float width, float height)
            {
                using (paper.Column($"dockbody#{_content.Id}", _content.Id, 0)
                           .Width(paper.Stretch()).Height(paper.Stretch()).Clip().Enter())
                {
                    _content.Emit(paper);
                }
            }
        }

        // -- Fallbacks ------------------------------------------------------

        private sealed class NullWidget : IWidget
        {
            public object NativeControl => new Control();
            public bool Visible { get; set; } = true;
        }

        private sealed class NullModal : IModal
        {
            private readonly Control _c = new();
            public NullModal(string title, string content) { Title = title; Content = content; }
            public object NativeControl => _c;
            public bool Visible { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
            public void Show() { }
            public void Close() { }
        }
    }
}



























