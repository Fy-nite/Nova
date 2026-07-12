using V12.Rendering;

namespace V12TwoDog.Godot.Rendering
{
    public class GodotRenderTargetFactory : IRenderTargetFactory
    {
        public IRenderTarget Create(int width, int height)
        {
            return new RenderTargetGodot(width, height);
        }
    }
}