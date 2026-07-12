using Godot;
using V12.Rendering;
using V12.UI;
using V12TwoDog.Godot.Rendering;

namespace V12TwoDog.Godot.UI
{
    public class GodotViewportHost : IViewportHost
    {
        private SubViewportContainer _container;

        public GodotViewportHost(SubViewportContainer container)
        {
            _container = container;
        }

        public object NativeControl => _container;

        public void SetRenderTarget(IRenderTarget target)
        {
            foreach (var child in _container.GetChildren())
                _container.RemoveChild(child);

            if (target is RenderTargetGodot godotTarget)
            {
                _container.AddChild(godotTarget.SubViewport);
            }
            else
            {
                throw new System.ArgumentException("Unsupported render target type");
            }
        }

        public void ClearRenderTarget()
        {
            foreach (var child in _container.GetChildren())
                _container.RemoveChild(child);
        }
    }
}