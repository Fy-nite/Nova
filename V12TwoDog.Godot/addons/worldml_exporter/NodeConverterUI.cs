using Godot;

namespace V12TwoDog.Editor;

public static class NodeConverterUI
{
    public static string BuildControlComponentXml(Control node, string ind)
    {
        var ccls = node.GetClass();

        if (ccls is "Button" or "LinkButton")
            return $"{ind}\t<ButtonComponent label=\"{node.Get("text").AsString().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")}\" />\n";

        if (ccls == "CheckBox")
            return $"{ind}\t<CheckboxComponent label=\"{node.Get("text").AsString().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")}\" checked=\"{node.Get("button_pressed").AsBool()}\" />\n";

        if (ccls == "CheckButton")
            return $"{ind}\t<ToggleComponent label=\"{node.Get("text").AsString().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")}\" isOn=\"{node.Get("button_pressed").AsBool()}\" />\n";

        if (ccls is "HSlider" or "VSlider")
        {
            var rn = (global::Godot.Range)node;
            var span = Mathf.Max(rn.MaxValue - rn.MinValue, 0.0001f);
            var norm = (rn.Value - rn.MinValue) / span;
            return $"{ind}\t<SliderComponent value=\"{norm:F4}\" min=\"{rn.MinValue:F4}\" max=\"{rn.MaxValue:F4}\" step=\"{rn.Step:F4}\" />\n";
        }

        if (ccls == "SpinBox")
        {
            var sb = (SpinBox)node;
            return $"{ind}\t<SliderComponent value=\"{sb.Value:F4}\" min=\"{sb.MinValue:F4}\" max=\"{sb.MaxValue:F4}\" step=\"{sb.Step:F4}\" />\n";
        }

        if (ccls == "LineEdit")
        {
            var le = (LineEdit)node;
            return $"{ind}\t<TextInputComponent value=\"{le.Text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")}\" placeholder=\"{le.PlaceholderText.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")}\" />\n";
        }

        if (ccls == "TextEdit")
            return $"{ind}\t<TextInputComponent value=\"{node.Get("text").AsString().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")}\" placeholder=\"\" />\n";

        if (ccls == "ProgressBar")
        {
            var pb = (ProgressBar)node;
            var span2 = Mathf.Max(pb.MaxValue - pb.MinValue, 0.0001f);
            var norm2 = (pb.Value - pb.MinValue) / span2;
            return $"{ind}\t<ProgressBarComponent value=\"{norm2:F4}\" indeterminate=\"false\" />\n";
        }

        if (ccls is "Label" or "RichTextLabel")
            return $"{ind}\t<LabelComponent text=\"{node.Get("text").AsString().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")}\" fontSize=\"0.000\" />\n";

        if (ccls == "VBoxContainer")
        {
            var sep = (float)node.Get("theme_constant_separation");
            return $"{ind}\t<VLayoutComponent spacing=\"{sep:F3}\" padding=\"0.000\" />\n";
        }

        if (ccls == "HBoxContainer")
        {
            var sep2 = (float)node.Get("theme_constant_separation");
            return $"{ind}\t<HLayoutComponent spacing=\"{sep2:F3}\" padding=\"0.000\" />\n";
        }

        if (ccls == "TextureRect")
        {
            var tr = (TextureRect)node;
            var src = tr.Texture?.ResourcePath ?? "";
            return $"{ind}\t<ImageComponent source=\"{src}\" preserveAspect=\"true\" tint=\"\" />\n";
        }

        if (ccls is "Panel" or "ColorRect")
            return $"{ind}\t<RectComponent width=\"{node.Get("size").AsVector2().X:F3}\" height=\"{node.Get("size").AsVector2().Y:F3}\" backgroundColor=\"\" cornerRadius=\"0.000\" />\n";

        if (ccls == "TextureButton")
            return $"{ind}\t<ButtonComponent label=\"\" />\n";

        return "";
    }
}
