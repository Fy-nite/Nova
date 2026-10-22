using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using S = V12TwoDog.Godot.UI.ColorSlot;

namespace V12TwoDog.Godot.UI
{
    // ------------------------------------------------------------------
    // ColorSlot: every semantic color the studio can ask for.
    // Core slots (before OnFocus) are required in every palette.
    // Everything from OnFocus down has a fallback.
    // ------------------------------------------------------------------
    public enum ColorSlot
    {
        // Core (required)
        Canvas,     // inputs, trees, deepest background
        Panel,      // panels, docks
        Raised,     // buttons, cards
        Hover,
        Selected,
        Border,
        Focus,      // accent
        Text,
        Muted,
        Disabled,

        // Optional (fallbacks exist)
        OnFocus,    // text drawn ON the accent color (AccentButton label)
        Success,
        Warning,
        Danger,

        // Asset browser types (chips, thumbnail underlines, tree icons)
        AssetScene,
        AssetScript,
        AssetModel,
        AssetImage,
        AssetAudio,
        AssetShader,
    }

    // ------------------------------------------------------------------
    // Palette: immutable slot -> color table. Indexed by ColorSlot.
    // ------------------------------------------------------------------
    public sealed class Palette
    {
        private static readonly int SlotCount = Enum.GetValues<ColorSlot>().Length;

        private static readonly Dictionary<ColorSlot, string> Fallbacks = new()
        {
            [S.Success] = "#6fcf97", [S.Warning] = "#e8b84a", [S.Danger] = "#eb6b6b",
            [S.AssetScene] = "#73b7dc", [S.AssetScript] = "#e8b84a", [S.AssetModel] = "#e58a5b",
            [S.AssetImage] = "#7bd88f", [S.AssetAudio] = "#c58af0", [S.AssetShader] = "#ef7fb0",
        };

        private readonly Color[] _c = new Color[SlotCount];

        public string Name { get; }
        public bool IsLight { get; }
        public int Corner { get; }

        public Color this[ColorSlot slot] => _c[(int)slot];

        private Palette(string name, bool isLight, int corner)
        {
            Name = name; IsLight = isLight; Corner = corner;
        }

        /// Resolution order per slot: explicit hex -> basedOn palette -> built-in fallback.
        /// Missing core slots with no basedOn throw.
        public static Palette Create(string name, Dictionary<ColorSlot, string> hex,
            Palette? basedOn = null, bool isLight = false, int corner = 0)
        {
            var p = new Palette(name, isLight, corner);
            foreach (var slot in Enum.GetValues<ColorSlot>())
            {
                Color c;
                if (hex.TryGetValue(slot, out var h)) c = new Color(h);
                else if (basedOn != null) c = basedOn[slot];
                else if (Fallbacks.TryGetValue(slot, out var f)) c = new Color(f);
                else if (slot == S.OnFocus) c = p[S.Canvas];
                else throw new ArgumentException($"Palette '{name}' is missing core slot {slot}");
                p._c[(int)slot] = c;
            }
            return p;
        }
    }

    public static class Palettes
    {
        // The one you already have.
        public static readonly Palette Slate = Palette.Create("Slate", new()
        {
            [S.Canvas] = "#17191d", [S.Panel] = "#202329", [S.Raised] = "#292d34",
            [S.Hover] = "#353b44", [S.Selected] = "#344b5d", [S.Border] = "#393e47",
            [S.Focus] = "#73b7dc", [S.Text] = "#e2e5e9", [S.Muted] = "#a4aab3", [S.Disabled] = "#737983",
        });

        // Deep blue-black.
        public static readonly Palette Midnight = Palette.Create("Midnight", new()
        {
            [S.Canvas] = "#0e1117", [S.Panel] = "#151a23", [S.Raised] = "#1c2330",
            [S.Hover] = "#263044", [S.Selected] = "#26406b", [S.Border] = "#2b3446",
            [S.Focus] = "#5aa2ff", [S.Text] = "#dfe6f2", [S.Muted] = "#8f9bb3", [S.Disabled] = "#5d6880",
        });

        // Warm charcoal + orange accent.
        public static readonly Palette Ember = Palette.Create("Ember", new()
        {
            [S.Canvas] = "#1a1614", [S.Panel] = "#231e1b", [S.Raised] = "#2d2723",
            [S.Hover] = "#3a322c", [S.Selected] = "#5a3a28", [S.Border] = "#443a33",
            [S.Focus] = "#ff9a4d", [S.Text] = "#ece4dc", [S.Muted] = "#b0a396", [S.Disabled] = "#7a6f64",
            [S.AssetScene] = "#ffb36b",
        });

        // Green-tinted dark.
        public static readonly Palette Moss = Palette.Create("Moss", new()
        {
            [S.Canvas] = "#121714", [S.Panel] = "#1a211d", [S.Raised] = "#222b26",
            [S.Hover] = "#2d3832", [S.Selected] = "#2f5240", [S.Border] = "#33403a",
            [S.Focus] = "#6fd39a", [S.Text] = "#dfeae3", [S.Muted] = "#9aaba1", [S.Disabled] = "#66756c",
            [S.AssetImage] = "#a5e86f",
        });

        // Purple dusk. Static Between Stars energy.
        public static readonly Palette Static = Palette.Create("Static", new()
        {
            [S.Canvas] = "#15121c", [S.Panel] = "#1d1928", [S.Raised] = "#262033",
            [S.Hover] = "#322a44", [S.Selected] = "#4a3a73", [S.Border] = "#3a3150",
            [S.Focus] = "#b48cff", [S.Text] = "#e8e2f5", [S.Muted] = "#a79fbd", [S.Disabled] = "#6f6785",
            [S.AssetAudio] = "#8cc9ff",
        });

        // Light. Semantic/asset colors darkened for contrast.
        public static readonly Palette Paper = Palette.Create("Paper", new()
        {
            [S.Canvas] = "#f6f7f9", [S.Panel] = "#eaecf0", [S.Raised] = "#ffffff",
            [S.Hover] = "#dfe3e9", [S.Selected] = "#cfe1ee", [S.Border] = "#c3c8d0",
            [S.Focus] = "#2f7fb0", [S.Text] = "#1d2026", [S.Muted] = "#5b626d", [S.Disabled] = "#9aa0a9",
            [S.OnFocus] = "#ffffff",
            [S.Success] = "#2e8f55", [S.Warning] = "#b57f10", [S.Danger] = "#c93c3c",
            [S.AssetScene] = "#2f7fb0", [S.AssetScript] = "#b57f10", [S.AssetModel] = "#c2602b",
            [S.AssetImage] = "#2e8f55", [S.AssetAudio] = "#8a47c2", [S.AssetShader] = "#c23c7c",
        }, isLight: true);

        private static readonly List<Palette> _all = new() { Slate, Midnight, Ember, Moss, Static, Paper };

        public static IReadOnlyList<Palette> All => _all;

        /// Fires when a palette is added or replaced (e.g. user palettes loaded from disk).
        public static event Action? ListChanged;

        public static Palette Find(string name) =>
            _all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Slate;

        /// Like Find, but tells you whether the name actually exists (Find silently falls back to Slate).
        public static bool TryFind(string name, out Palette palette)
        {
            var found = _all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            palette = found ?? Slate;
            return found != null;
        }

        /// Adds a palette, replacing any existing one with the same name.
        public static void Register(Palette p)
        {
            var i = _all.FindIndex(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) _all[i] = p; else _all.Add(p);
            ListChanged?.Invoke();
        }
    }

    // ------------------------------------------------------------------
    // JSON palettes: user://palettes/*.json. Keys are ColorSlot names, so a
    // file only needs the slots it wants to change (rest come from basedOn,
    // default Slate). Unknown keys are ignored so old builds don't choke on
    // newer palette files.
    //
    // { "name": "Mine", "basedOn": "Ember", "corner": 3,
    //   "slots": { "Focus": "#ff5577", "Canvas": "#101010" } }
    // ------------------------------------------------------------------
    public static class PaletteIO
    {
        private const string Dir = "user://palettes";

        private sealed class Dto
        {
            public string Name { get; set; } = "";
            public bool IsLight { get; set; }
            public int Corner { get; set; }
            public string? BasedOn { get; set; }
            public Dictionary<string, string> Slots { get; set; } = new();
        }

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static string ToJson(Palette p) => JsonSerializer.Serialize(new Dto
        {
            Name = p.Name,
            IsLight = p.IsLight,
            Corner = p.Corner,
            Slots = Enum.GetValues<ColorSlot>().ToDictionary(s => s.ToString(), s => p[s].ToHtml(false)),
        }, Json);

        public static Palette FromJson(string json)
        {
            var dto = JsonSerializer.Deserialize<Dto>(json, Json) ?? throw new FormatException("empty palette file");
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new FormatException("palette has no name");

            var hex = new Dictionary<ColorSlot, string>();
            foreach (var (key, value) in dto.Slots)
                if (Enum.TryParse<ColorSlot>(key, true, out var slot)) hex[slot] = value;

            var basis = dto.BasedOn != null ? Palettes.Find(dto.BasedOn) : Palettes.Slate;
            return Palette.Create(dto.Name, hex, basis, dto.IsLight, dto.Corner);
        }

        public static void Save(Palette p)
        {
            DirAccess.MakeDirRecursiveAbsolute(Dir);
            var file = string.Concat(p.Name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            using var f = global::Godot.FileAccess.Open($"{Dir}/{file}.json",global::Godot.FileAccess.ModeFlags.Write);
            if (f == null) { GD.PushWarning($"Couldn't save palette '{p.Name}'"); return; }
            f.StoreString(ToJson(p));
        }

        public static void LoadAll()
        {
            foreach (var file in DirAccess.GetFilesAt(Dir))
            {
                if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                try { Palettes.Register(FromJson(global::Godot.FileAccess.GetFileAsString($"{Dir}/{file}"))); }
                catch (Exception e) { GD.PushWarning($"Palette '{file}' skipped: {e.Message}"); }
            }
        }
    }

    // ------------------------------------------------------------------
    // Theme builder: a rule table (slot-based), applied to a palette.
    // Adding a control = adding rows. Nothing here mentions a hex value.
    // ------------------------------------------------------------------
    public static class GodotStudioTheme
    {
        private readonly record struct ColorRule(string Type, string Item, S Slot);

        private readonly record struct ConstRule(string Type, string Item, int Value);

        private readonly record struct BoxRule(
            string Type, string Item, S? Fill, S Border, int Width, int Margin,
            bool Round = true, float Lift = 0f)
        {
            public StyleBoxFlat Build(Palette p)
            {
                var fill = Fill is { } f ? p[f] : Colors.Transparent;
                if (Lift > 0) fill = fill.Lightened(Lift);
                else if (Lift < 0) fill = fill.Darkened(-Lift);

                var s = new StyleBoxFlat
                {
                    BgColor = fill,
                    BorderColor = p[Border],
                    ContentMarginLeft = Margin,
                    ContentMarginTop = Margin,
                    ContentMarginRight = Margin,
                    ContentMarginBottom = Margin,
                };
                s.SetBorderWidthAll(Width);
                s.SetCornerRadiusAll(Round ? p.Corner : 0);
                return s;
            }
        }

        // Custom type variations: set ThemeTypeVariation = "AccentButton" on a control.
        private static readonly (string Name, string Base)[] Variations =
        {
            ("AccentButton", "Button"), ("DangerButton", "Button"),
            ("MutedLabel", "Label"), ("ErrorLabel", "Label"),
            ("SectionLabel", "Label"),                 // "Colour", "UV", "Textures" sub-headers
            ("ComponentCard", "PanelContainer"),       // backing behind one component in the inspector
            ("ComponentHeader", "Button"),             // flat clickable header row (if not using FoldableContainer)
        };

        private static readonly ColorRule[] ColorRules = BuildColorRules();
        private static readonly BoxRule[] BoxRules = BuildBoxRules();

        private static readonly ConstRule[] ConstRules =
        {
            new("VBoxContainer", "separation", 4), new("HBoxContainer", "separation", 4),
            new("Tree", "h_separation", 6), new("Tree", "v_separation", 2),
        };

        public static Theme Create(Palette p)
        {
            var theme = new Theme { DefaultFontSize = 13 };
            foreach (var (name, @base) in Variations) theme.SetTypeVariation(name, @base);
            foreach (var r in ColorRules) theme.SetColor(r.Item, r.Type, p[r.Slot]);
            foreach (var r in BoxRules) theme.SetStylebox(r.Item, r.Type, r.Build(p));
            foreach (var r in ConstRules) theme.SetConstant(r.Item, r.Type, r.Value);

            // Slider grabbers are icons, so generate them from the palette
            var grabber = MakeGrabber(p[S.Focus], p[S.Canvas]);
            var grabberHi = MakeGrabber(p[S.Focus].Lightened(0.2f), p[S.Canvas]);
            var grabberOff = MakeGrabber(p[S.Disabled], p[S.Canvas]);
            foreach (var t in new[] { "HSlider", "VSlider" })
            {
                theme.SetIcon("grabber", t, grabber);
                theme.SetIcon("grabber_highlight", t, grabberHi);
                theme.SetIcon("grabber_disabled", t, grabberOff);
            }
            return theme;
        }

        /// Anti-aliased filled circle with a 1.5px rim.
        private static ImageTexture MakeGrabber(Color fill, Color rim, int size = 16)
        {
            var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            var center = (size - 1) / 2f;
            var radius = size / 2f - 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = new Vector2(x - center, y - center).Length();
                    var alpha = Mathf.Clamp(radius + 0.5f - d, 0f, 1f);
                    if (alpha <= 0f) continue;
                    var rimMix = Mathf.Clamp(d - (radius - 1.5f) + 0.5f, 0f, 1f);
                    var c = fill.Lerp(rim, rimMix);
                    c.A = alpha;
                    img.SetPixel(x, y, c);
                }
            }
            return ImageTexture.CreateFromImage(img);
        }

        private static ColorRule[] BuildColorRules()
        {
            var l = new List<ColorRule>();
            void Add(string type, S slot, params string[] items)
            {
                foreach (var i in items) l.Add(new(type, i, slot));
            }

            Add("Control", S.Text, "font_color", "font_hover_color", "font_pressed_color", "font_focus_color");
            Add("Control", S.Disabled, "font_disabled_color");
            Add("Label", S.Text, "font_color");
            Add("TooltipLabel", S.Text, "font_color");

            Add("LineEdit", S.Text, "font_color", "font_selected_color");
            Add("LineEdit", S.Muted, "font_placeholder_color");
            Add("LineEdit", S.Selected, "selection_color");
            Add("LineEdit", S.Focus, "caret_color");

            Add("Tree", S.Text, "font_color", "font_selected_color", "font_hovered_color");
            Add("ItemList", S.Text, "font_color", "font_hovered_color", "font_selected_color");

            Add("TabBar", S.Text, "font_selected_color", "font_hovered_color");
            Add("TabBar", S.Muted, "font_unselected_color");

            Add("PopupMenu", S.Text, "font_color", "font_hover_color");
            Add("PopupMenu", S.Disabled, "font_disabled_color");
            Add("PopupMenu", S.Muted, "font_separator_color");

            Add("AccentButton", S.OnFocus, "font_color", "font_hover_color", "font_pressed_color");
            Add("DangerButton", S.Danger, "font_color");
            Add("MutedLabel", S.Muted, "font_color");
            Add("ErrorLabel", S.Danger, "font_color");
            Add("SectionLabel", S.Muted, "font_color");
            Add("ComponentHeader", S.Text, "font_color", "font_hover_color", "font_pressed_color", "font_focus_color");

            // FoldableContainer (Godot 4.5+): collapsible component sections
            Add("FoldableContainer", S.Text, "font_color", "hover_font_color", "collapsed_font_color");
            return l.ToArray();
        }

        private static BoxRule[] BuildBoxRules()
        {
            var l = new List<BoxRule>();
            void Box(string type, string item, S? fill, S border, int w, int m, bool round = true, float lift = 0f)
                => l.Add(new(type, item, fill, border, w, m, round, lift));

            // Buttons share a look
            foreach (var t in new[] { "Button", "OptionButton" })
            {
                Box(t, "normal", S.Raised, S.Border, 1, 3);
                Box(t, "hover", S.Hover, S.Focus, 1, 3);
                Box(t, "pressed", S.Selected, S.Focus, 1, 3);
                Box(t, "disabled", S.Panel, S.Border, 1, 3);
                Box(t, "focus", null, S.Focus, 2, 3);
            }

            // Containers
            Box("PanelContainer", "panel", S.Panel, S.Border, 1, 5);
            Box("PopupPanel", "panel", S.Panel, S.Border, 1, 4);
            Box("TooltipPanel", "panel", S.Raised, S.Border, 1, 4);

            // Inputs
            Box("LineEdit", "normal", S.Canvas, S.Border, 1, 3);
            Box("LineEdit", "focus", S.Canvas, S.Focus, 2, 3);
            Box("LineEdit", "read_only", S.Panel, S.Border, 1, 3);

            // Tree + ItemList (asset grid): square rows
            foreach (var t in new[] { "Tree", "ItemList" })
            {
                Box(t, "panel", S.Canvas, S.Border, 1, 0, round: false);
                Box(t, "selected", S.Selected, S.Focus, 1, 0, round: false);
                Box(t, "selected_focus", S.Selected, S.Focus, 1, 0, round: false);
                Box(t, "cursor", null, S.Focus, 1, 0, round: false);
                Box(t, "cursor_unfocused", null, S.Border, 1, 0, round: false);
            }
            Box("ItemList", "hovered", S.Hover, S.Border, 1, 0, round: false);

            // Tabs
            Box("TabBar", "tab_selected", S.Selected, S.Focus, 1, 0, round: false);
            Box("TabBar", "tab_unselected", S.Panel, S.Border, 1, 0, round: false);
            Box("TabBar", "tab_hovered", S.Hover, S.Border, 1, 0, round: false);
            Box("TabBar", "tab_focus", null, S.Focus, 2, 0, round: false);

            // Popup menus
            Box("PopupMenu", "panel", S.Panel, S.Border, 1, 4);
            Box("PopupMenu", "hover", S.Selected, S.Focus, 1, 3);

            // Variations
            Box("AccentButton", "normal", S.Focus, S.Focus, 1, 3);
            Box("AccentButton", "hover", S.Focus, S.Focus, 1, 3, lift: 0.12f);
            Box("AccentButton", "pressed", S.Focus, S.Focus, 1, 3, lift: -0.12f);
            Box("DangerButton", "normal", S.Raised, S.Danger, 1, 3);
            Box("DangerButton", "hover", S.Hover, S.Danger, 1, 3);
            Box("DangerButton", "pressed", S.Danger, S.Danger, 1, 3);

            // Sliders: Canvas track, accent-filled portion, brighter fill on hover
            foreach (var t in new[] { "HSlider", "VSlider" })
            {
                Box(t, "slider", S.Canvas, S.Border, 1, 2);
                Box(t, "grabber_area", S.Focus, S.Focus, 1, 2);
                Box(t, "grabber_area_highlight", S.Focus, S.Focus, 1, 2, lift: 0.12f);
            }

            // Component backing: card body + flat header button
            Box("ComponentCard", "panel", S.Raised, S.Border, 1, 6, round: false);
            Box("ComponentHeader", "normal", null, S.Border, 0, 4);
            Box("ComponentHeader", "hover", S.Hover, S.Border, 0, 4);
            Box("ComponentHeader", "pressed", S.Selected, S.Border, 0, 4);
            Box("ComponentHeader", "focus", null, S.Focus, 1, 4);

            // FoldableContainer: header bar (open/collapsed, normal/hover) + body
            Box("FoldableContainer", "title_panel", S.Raised, S.Border, 1, 4, round: false);
            Box("FoldableContainer", "title_hover_panel", S.Hover, S.Border, 1, 4, round: false);
            Box("FoldableContainer", "title_collapsed_panel", S.Raised, S.Border, 1, 4, round: false);
            Box("FoldableContainer", "title_collapsed_hover_panel", S.Hover, S.Border, 1, 4, round: false);
            Box("FoldableContainer", "panel", S.Panel, S.Border, 1, 6, round: false);
            Box("FoldableContainer", "focus", null, S.Focus, 1, 0, round: false);
            return l.ToArray();
        }
    }

    // ------------------------------------------------------------------
    // Manager: current palette/theme, persistence, Bind().
    // ------------------------------------------------------------------
    public static class StudioThemeManager
    {
        private const string ConfigPath = "user://v12studio_ui.cfg";

        public static Palette Current { get; private set; } = Palettes.Slate;
        public static Theme Theme { get; private set; } = GodotStudioTheme.Create(Palettes.Slate);

        /// Fires after Current/Theme are swapped. Custom-drawing code: QueueRedraw() here.
        public static event Action<Palette>? Changed;

        /// Call once at startup, before building UI. Loads user palettes, then restores the saved choice.
        public static void Load()
        {
            PaletteIO.LoadAll();
            var cfg = new ConfigFile();
            if (cfg.Load(ConfigPath) != Error.Ok) return;
            var name = (string)cfg.GetValue("ui", "palette", Palettes.Slate.Name);
            Set(Palettes.Find(name), save: false);
        }

        public static void Set(Palette palette, bool save = true)
        {
            if (palette == Current) return;
            Current = palette;
            Theme = GodotStudioTheme.Create(palette);
            Changed?.Invoke(palette);
            if (!save) return;

            var cfg = new ConfigFile();
            cfg.Load(ConfigPath); // fine if it doesn't exist yet
            cfg.SetValue("ui", "palette", palette.Name);
            cfg.Save(ConfigPath);
        }

        /// Convenience for code that only knows the slot: StudioThemeManager.Color(ColorSlot.AssetShader)
        public static Color Color(ColorSlot slot) => Current[slot];

        public static void Bind(Control root)
        {
            root.Theme = Theme;
            void Handler(Palette _) { if (GodotObject.IsInstanceValid(root)) root.Theme = Theme; }
            Changed += Handler;
            root.TreeExited += () => Changed -= Handler;
        }

        /// Only needed for windows that aren't descendants of an already-bound root
        /// (a Window parented under a themed Control/Window inherits its theme).
        public static void Bind(Window window)
        {
            window.Theme = Theme;
            void Handler(Palette _) { if (GodotObject.IsInstanceValid(window)) window.Theme = Theme; }
            Changed += Handler;
            window.TreeExited += () => Changed -= Handler;
        }
    }

    // ------------------------------------------------------------------
    // Drop-in switcher. Swatch = Canvas | Selected | Focus.
    // ------------------------------------------------------------------
    [GlobalClass]
    public partial class PaletteSwitcher : OptionButton
    {
        public override void _Ready()
        {
            TooltipText = "UI palette";
            Rebuild();
            ItemSelected += OnItemSelected;
            StudioThemeManager.Changed += SyncSelection;
            Palettes.ListChanged += Rebuild;
        }

        public override void _ExitTree()
        {
            ItemSelected -= OnItemSelected;
            StudioThemeManager.Changed -= SyncSelection;
            Palettes.ListChanged -= Rebuild;
        }

        private void Rebuild()
        {
            Clear();
            foreach (var p in Palettes.All) AddIconItem(MakeSwatch(p), p.Name);
            SyncSelection(StudioThemeManager.Current);
        }

        private void OnItemSelected(long index) => StudioThemeManager.Set(Palettes.All[(int)index]);

        private void SyncSelection(Palette p)
        {
            for (var i = 0; i < Palettes.All.Count; i++)
                if (Palettes.All[i] == p) { Select(i); return; }
        }

        private static ImageTexture MakeSwatch(Palette p)
        {
            var img = Image.CreateEmpty(24, 12, false, Image.Format.Rgba8);
            img.Fill(p[S.Border]);
            img.FillRect(new Rect2I(1, 1, 7, 10), p[S.Canvas]);
            img.FillRect(new Rect2I(8, 1, 8, 10), p[S.Selected]);
            img.FillRect(new Rect2I(16, 1, 7, 10), p[S.Focus]);
            return ImageTexture.CreateFromImage(img);
        }
    }
}
