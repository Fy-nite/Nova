using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.OrigamiUI;
using V12.UI;
using PPaper = Prowl.PaperUI.Paper;

namespace V12TwoDog.Paper
{
    /// <summary>
    /// Origami-backed context menu. Items are collected with the <see cref="IContextMenu"/>
    /// API and rendered by Origami's modal context-menu system, so the editor's right-click
    /// menus use the same Paper/Origami widgets as the rest of the UI (no Godot PopupMenu).
    ///
    /// When <see cref="Searchable"/> is requested the menu becomes a searchable, drill-down
    /// popover: a search field filters every leaf entry in the tree, and an empty query
    /// shows the current branch's items (categories navigate in place).
    /// </summary>
    public sealed class ProwlContextMenu : IContextMenu
    {
        private sealed class Entry
        {
            public string Label = "";
            public string Icon = "";
            public Action? OnClick;
            public bool Enabled = true;
            public IContextMenu? Sub;
            public bool Separator;
        }

        private sealed record FlatEntry(string Label, string Path, string Icon, Entry Entry);

        private readonly List<Entry> _entries = new();
        private readonly Func<PPaper?> _paper;
        private readonly Control _dummy = new() { MouseFilter = Control.MouseFilterEnum.Ignore };

        private string? _searchPlaceholder;
        private string _query = "";
        private ProwlContextMenu? _branch;
        private Prowl.OrigamiUI.IModal? _modal;
        private long _blockActivateUntil;
        private long _enterLatch;
        private float _x, _y;

        public ProwlContextMenu(Func<PPaper?> paper) => _paper = paper;

        public object NativeControl => _dummy;
        public bool Visible { get; set; } = true;

        public void AddItem(string label, Action onClick, bool enabled = true, string icon = "")
            => _entries.Add(new Entry { Label = label, OnClick = onClick, Enabled = enabled, Icon = icon });

        public void AddSeparator() => _entries.Add(new Entry { Separator = true });

        public void AddSubmenu(string label, IContextMenu submenu, string icon = "")
            => _entries.Add(new Entry { Label = label, Sub = submenu, Icon = icon });

        public void Searchable(string placeholder = "Search...")
            => _searchPlaceholder = string.IsNullOrWhiteSpace(placeholder) ? "Search..." : placeholder;

        public void Show(float screenX, float screenY)
        {
            _x = screenX;
            _y = screenY;

            if (_searchPlaceholder == null)
            {
                // Defer by one frame: chained menus open from inside an item click,
                // and pushing the new modal during the dismissing click closes it.
                Defer(() => ContextMenu.Show(_x, _y, BuildMenu));
                return;
            }

            _query = "";
            _branch = this;
            Defer(() =>
            {
                _modal = new CustomDrawModal((paper, layer, _) => DrawSearch(paper, layer))
                {
                    CloseOnBackdrop = true,
                    CloseOnEscape = true,
                    OnDismissed = _ => Cleanup(),
                };
                Modal.Push(_modal);
            });
        }

        public void ShowAt(IWidget anchor)
        {
            // Paper reports the live pointer; that is also where a right-click
            // landed, so anchoring to the pointer keeps the menu at the click.
            var paper = _paper();
            if (paper != null)
                Show((float)paper.PointerPos.X, (float)paper.PointerPos.Y);
        }

        // ── Classic menu ───────────────────────────────────────────────────

        private void BuildMenu(ContextBuilder builder)
        {
            foreach (var entry in _entries)
            {
                if (entry.Separator)
                {
                    builder.Separator();
                }
                else if (entry.Sub is ProwlContextMenu sub)
                {
                    builder.Submenu(entry.Label, sub.BuildMenu);
                }
                else if (entry.OnClick != null)
                {
                    builder.Item(entry.Label, entry.OnClick, entry.Enabled,
                        iconDraw: ComponentIcons.Resolve(entry.Icon));
                }
            }
        }

        // ── Searchable popover ─────────────────────────────────────────────

        private void DrawSearch(PPaper paper, int layer)
        {
            var theme = Origami.Current;
            var font = theme.Font;
            if (font == null) return;

            const float width = 300f;
            float x = Math.Clamp(_x, 4f, Math.Max(4f, paper.Width - width - 4f));
            float y = Math.Clamp(_y, 4f, Math.Max(4f, paper.Height - 96f));

            var panel = paper.Box("searchmenu_panel")
                .PositionType(PositionType.SelfDirected)
                .Position(x, y)
                .Width(width).Height(UnitValue.Auto)
                .BackgroundColor(theme.Popover)
                .BorderColor(theme.BorderStrong).BorderWidth(1)
                .Rounded(theme.Metrics.ContainerRounding)
                .DropShadow(0, 14, 40, 0, theme.Shadow)
                .Layer(layer + 1)
                .ClampToScreen()
                .StopEventPropagation();

            using (panel.Enter())
            using (paper.Column("searchmenu_col").Padding(6, 6, 6, 6)
                .Height(UnitValue.Auto).Gap(4).Enter())
            {
                Origami.TextField(paper, "searchmenu_query", _query, v => _query = v)
                    .Search(_searchPlaceholder!)
                    .AutoFocus()
                    .Show();

                Origami.Separator(paper, "searchmenu_sep").Show();

                if (string.IsNullOrWhiteSpace(_query))
                    DrawBranch(paper, theme, font);
                else
                    DrawMatches(paper, theme, font);
            }
        }

        private void DrawBranch(PPaper paper, OrigamiTheme theme, Prowl.Scribe.FontFile font)
        {
            var branch = _branch ?? this;
            int row = 0;

            if (!ReferenceEquals(branch, this))
                DrawRow(paper, theme, font, row++, "Back", "", "chevron-left",
                    () => _branch = this, chevronRight: false);

            foreach (var entry in branch._entries)
            {
                if (entry.Separator) continue;
                if (entry.Sub is ProwlContextMenu sub)
                {
                    var captured = sub;
                    DrawRow(paper, theme, font, row++, entry.Label, "", entry.Icon,
                        () =>
                        {
                            _branch = captured;
                            // The same gesture's release can land on the first row of the
                            // new branch; ignore activations briefly so drill-down is safe.
                            _blockActivateUntil = System.Environment.TickCount64 + 250;
                        }, chevronRight: true);
                }
                else if (entry.OnClick != null)
                {
                    var captured = entry;
                    DrawRow(paper, theme, font, row++, entry.Label, "", entry.Icon,
                        () => Activate(captured), chevronRight: false);
                }
            }
        }

        private void DrawMatches(PPaper paper, OrigamiTheme theme, Prowl.Scribe.FontFile font)
        {
            string query = _query.Trim();
            var matches = Flatten(this, "")
                .Where(m => m.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || m.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Label.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(m => m.Label, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            if (matches.Count == 0)
            {
                paper.Box("searchmenu_empty").Width(UnitValue.Stretch()).Height(28f).Padding(8, 8, 0, 0)
                    .Text("No components match.", font).FontSize(12f).TextColor(theme.Ink.C300);
                return;
            }

            // Enter activates the best match (command-palette behaviour). Deferred so
            // the modal stack isn't mutated while it is being drawn.
            if (paper.IsKeyPressed(PaperKey.Enter)
                && System.Environment.TickCount64 >= _enterLatch)
            {
                _enterLatch = System.Environment.TickCount64 + 250;
                var first = matches[0].Entry;
                Defer(() => Activate(first));
            }

            int row = 0;
            foreach (var match in matches)
            {
                var captured = match;
                DrawRow(paper, theme, font, row++, match.Label, match.Path, match.Icon,
                    () => Activate(captured.Entry), chevronRight: false);
            }
        }

        private IEnumerable<FlatEntry> Flatten(ProwlContextMenu menu, string path)
        {
            foreach (var entry in menu._entries)
            {
                if (entry.Separator) continue;
                if (entry.Sub is ProwlContextMenu sub)
                {
                    string childPath = string.IsNullOrEmpty(path) ? entry.Label : $"{path} · {entry.Label}";
                    foreach (var nested in Flatten(sub, childPath)) yield return nested;
                }
                else if (entry.OnClick != null)
                {
                    yield return new FlatEntry(entry.Label, path, entry.Icon, entry);
                }
            }
        }

        private void DrawRow(PPaper paper, OrigamiTheme theme, Prowl.Scribe.FontFile font,
            int index, string label, string hint, string iconKey, Action onClick, bool chevronRight)
        {
            string id = $"searchmenu_r{index}";
            var row = paper.Row(id)
                .Width(UnitValue.Stretch()).Height(28f)
                .Padding(8, 8, 0, 0).Gap(8)
                .Rounded(theme.Metrics.Rounding)
                .Hovered.BackgroundColor(theme.Hover).End()
                .Cursor(PaperCursor.Pointer)
                .OnClick(_ => onClick());

            using (row.Enter())
            {
                var icon = ComponentIcons.Resolve(iconKey);
                paper.Box($"{id}_i").Width(18f).Height(28f)
                    .Icon(paper, icon, theme.Ink.C400, size: 14f);

                paper.Box($"{id}_l").Width(UnitValue.Auto).Height(28f)
                    .Text(label, font).FontSize(13f).TextColor(theme.Ink.C500);

                if (!string.IsNullOrEmpty(hint))
                {
                    paper.Box($"{id}_h").Width(UnitValue.Auto).Height(28f).Padding(2, 0, 0, 0)
                        .Text(hint, font).FontSize(11f).TextColor(theme.Ink.C300);
                }

                if (chevronRight)
                {
                    paper.Box($"{id}_c").Width(12f).Height(28f)
                        .Icon(paper, OrigamiGlyphs.ChevronRight_I, theme.Ink.C300, size: 11f);
                }
            }
        }

        private void Activate(Entry entry)
        {
            if (System.Environment.TickCount64 < _blockActivateUntil) return;
            Cleanup();
            if (entry.Enabled)
            {
                try { entry.OnClick?.Invoke(); } catch { }
            }
        }

        private void Cleanup()
        {
            if (_modal != null)
            {
                Modal.Remove(_modal);
                _modal = null;
            }
            _query = "";
            _branch = null;
        }

        private static void Defer(Action action)
        {
            try { Callable.From(action).CallDeferred(); }
            catch { action(); }
        }
    }
}
