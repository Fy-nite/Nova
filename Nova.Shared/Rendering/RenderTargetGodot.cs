using Godot;
using V12.Rendering;

namespace V12TwoDog.Godot.Rendering
{
    public class RenderTargetGodot : IRenderTarget
    {
        private SubViewport _viewport;

        public RenderTargetGodot(int width, int height)
        {
            _viewport = new SubViewport
            {
                Size = new Vector2I(width, height),
                TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                HandleInputLocally = false
            };
        }

        public int Width => _viewport.Size.X;
        public int Height => _viewport.Size.Y;

        public object NativeHandle => _viewport;

        public void Resize(int width, int height)
        {
            _viewport.Size = new Vector2I(width, height);
        }

        internal SubViewport SubViewport => _viewport;
    }
}