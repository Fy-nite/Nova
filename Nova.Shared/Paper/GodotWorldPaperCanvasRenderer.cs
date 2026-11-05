using System;
using Godot;

namespace V12TwoDog.Paper
{
    /// <summary>
    /// Paper canvas backend for one world-space canvas. It owns a hidden
    /// <see cref="SubViewport"/> whose texture is drawn on the canvas's world quad.
    /// The Paper surface itself is managed by the engine's
    /// <see cref="V12.Core.UI.Paper.PaperWorldCanvasSystem"/>.
    /// </summary>
    public sealed class GodotWorldPaperCanvasRenderer : GodotPaperCanvasRenderer
    {
        private readonly SubViewport _viewport;
        private readonly Action<long>? _onDisposed;

        public GodotWorldPaperCanvasRenderer(
            Node parent, long canvasId, int width, int height, Action<long>? onDisposed)
            : this(Build(parent, canvasId, width, height), canvasId, onDisposed)
        {
        }

        private GodotWorldPaperCanvasRenderer(
            (CanvasLayer Layer, Control Root, SubViewport Viewport) built,
            long canvasId, Action<long>? onDisposed)
            : base(built.Layer, built.Root)
        {
            CanvasId = canvasId;
            _viewport = built.Viewport;
            _onDisposed = onDisposed;
        }

        public long CanvasId { get; }

        public SubViewport Viewport => _viewport;

        public Texture2D? Texture =>
            GodotObject.IsInstanceValid(_viewport) ? _viewport.GetTexture() : null;

        private static (CanvasLayer, Control, SubViewport) Build(
            Node parent, long canvasId, int width, int height)
        {
            var viewport = new SubViewport
            {
                Name = $"PaperWorld_{canvasId}",
                Size = new Vector2I(Math.Max(1, width), Math.Max(1, height)),
                TransparentBg = true,
                Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                HandleInputLocally = false,
            };

            var layer = new CanvasLayer { Name = "PaperLayer" };
            var root = new Control { Name = "PaperRoot" };
            root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

            layer.AddChild(root);
            viewport.AddChild(layer);
            parent.AddChild(viewport);
            return (layer, root, viewport);
        }

        public override void Dispose()
        {
            base.Dispose();
            _onDisposed?.Invoke(CanvasId);
            if (GodotObject.IsInstanceValid(_viewport)) _viewport.QueueFree();
        }
    }
}
