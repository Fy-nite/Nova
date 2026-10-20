namespace V12TwoDog.Godot.UI
{
    /// <summary>
    /// Node metadata keys shared between the widget wrappers and host-side
    /// layout passes (e.g. StudioHost.GrowAll).
    /// </summary>
    public static class LayoutHints
    {
        /// <summary>
        /// Bool. Set by a box wrapper when its widget requests
        /// <c>Expand = false</c>: GrowAll must leave the control's
        /// horizontal size flags alone so the box hugs its content
        /// instead of sharing spare space with its siblings.
        /// </summary>
        public const string ExpandMetaKey = "v12_expand";
    }
}
