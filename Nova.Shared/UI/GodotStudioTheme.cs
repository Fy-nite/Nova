using Godot;

namespace V12TwoDog.Godot.UI
{
    public static class GodotStudioTheme
    {
        private static readonly Color Canvas = new("#17191d");
        private static readonly Color Panel = new("#202329");
        private static readonly Color Raised = new("#292d34");
        private static readonly Color Hover = new("#353b44");
        private static readonly Color Selected = new("#344b5d");
        private static readonly Color Border = new("#393e47");
        private static readonly Color Focus = new("#73b7dc");
        private static readonly Color Text = new("#e2e5e9");
        private static readonly Color Muted = new("#a4aab3");
        private static readonly Color Disabled = new("#737983");

        public static Theme Create()
        {
            var theme = new Theme();
            theme.DefaultFontSize = 13;
            theme.SetColor("font_color", "Control", Text);
            theme.SetColor("font_hover_color", "Control", Text);
            theme.SetColor("font_pressed_color", "Control", Text);
            theme.SetColor("font_focus_color", "Control", Text);
            theme.SetColor("font_disabled_color", "Control", Disabled);
            theme.SetColor("font_color", "Label", Text);
            theme.SetColor("font_color", "Tree", Text);
            theme.SetColor("font_selected_color", "Tree", Text);
            theme.SetColor("font_hovered_color", "Tree", Text);
            theme.SetColor("font_placeholder_color", "LineEdit", Muted);
            theme.SetColor("font_color", "LineEdit", Text);
            theme.SetColor("font_selected_color", "LineEdit", Text);
            theme.SetColor("selection_color", "LineEdit", Selected);

            theme.SetStylebox("panel", "PanelContainer", Box(Panel, Border, 1, 5));
            theme.SetStylebox("normal", "Button", Box(Raised, Border, 1, 3));
            theme.SetStylebox("hover", "Button", Box(Hover, Focus, 1, 3));
            theme.SetStylebox("pressed", "Button", Box(Selected, Focus, 1, 3));
            theme.SetStylebox("disabled", "Button", Box(Panel, Border, 1, 3));
            theme.SetStylebox("focus", "Button", Outline(Focus, 2, 3));
            theme.SetStylebox("normal", "OptionButton", Box(Raised, Border, 1, 3));
            theme.SetStylebox("hover", "OptionButton", Box(Hover, Focus, 1, 3));
            theme.SetStylebox("pressed", "OptionButton", Box(Selected, Focus, 1, 3));
            theme.SetStylebox("disabled", "OptionButton", Box(Panel, Border, 1, 3));
            theme.SetStylebox("focus", "OptionButton", Outline(Focus, 2, 3));
            theme.SetStylebox("normal", "LineEdit", Box(Canvas, Border, 1, 3));
            theme.SetStylebox("focus", "LineEdit", Box(Canvas, Focus, 2, 3));
            theme.SetStylebox("read_only", "LineEdit", Box(Panel, Border, 1, 3));
            theme.SetStylebox("panel", "Tree", Box(Canvas, Border, 1, 0));
            theme.SetStylebox("selected", "Tree", Box(Selected, Focus, 1, 0));
            theme.SetStylebox("selected_focus", "Tree", Box(Selected, Focus, 1, 0));
            theme.SetStylebox("cursor", "Tree", Outline(Focus, 1, 0));
            theme.SetStylebox("cursor_unfocused", "Tree", Outline(Border, 1, 0));
            theme.SetStylebox("tab_selected", "TabBar", Box(Selected, Focus, 1, 0));
            theme.SetStylebox("tab_unselected", "TabBar", Box(Panel, Border, 1, 0));
            theme.SetStylebox("tab_hovered", "TabBar", Box(Hover, Border, 1, 0));
            theme.SetStylebox("tab_focus", "TabBar", Outline(Focus, 2, 0));
            theme.SetColor("font_selected_color", "TabBar", Text);
            theme.SetColor("font_unselected_color", "TabBar", Muted);
            theme.SetColor("font_hovered_color", "TabBar", Text);

            theme.SetConstant("separation", "VBoxContainer", 4);
            theme.SetConstant("separation", "HBoxContainer", 4);
            theme.SetConstant("h_separation", "Tree", 6);
            theme.SetConstant("v_separation", "Tree", 2);
            return theme;
        }

        private static StyleBoxFlat Box(Color fill, Color border, int width, int contentMargin)
        {
            var style = new StyleBoxFlat
            {
                BgColor = fill,
                ContentMarginLeft = contentMargin,
                ContentMarginTop = contentMargin,
                ContentMarginRight = contentMargin,
                ContentMarginBottom = contentMargin,
            };
            style.SetBorderWidthAll(width);
            style.BorderColor = border;
            style.SetCornerRadiusAll(0);
            return style;
        }

        private static StyleBoxFlat Outline(Color color, int width, int contentMargin)
        {
            var style = new StyleBoxFlat
            {
                BgColor = Colors.Transparent,
                ContentMarginLeft = contentMargin,
                ContentMarginTop = contentMargin,
                ContentMarginRight = contentMargin,
                ContentMarginBottom = contentMargin,
                BorderColor = color,
            };
            style.SetBorderWidthAll(width);
            style.SetCornerRadiusAll(0);
            return style;
        }
    }
}
