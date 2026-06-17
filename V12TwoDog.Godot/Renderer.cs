using System.Collections.Generic;
using Godot;
using V12.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12TwoDog
{
	public class Renderer : IRenderer
	{
		private readonly List<IRenderable> _renderables = new List<IRenderable>();
		public SceneTree root;

		public Renderer(SceneTree t)
		{
			root = t;
		}

		public int GetFPS() => 60; //TODO: replace soon with (int)V12TwoDog.Godot.Globals.GetFPS()

		public RendererInfo GetAllInfo()
		{
			return new RendererInfo();
		}

		public void QueueItem(IRenderable item)
		{
			if (!_renderables.Contains(item))
			{
				_renderables.Add(item);
			}
		}

		public void RemoveItem(IRenderable item)
		{
			_renderables.Remove(item);
		}

		public int GetScreenWidth() => (int)DisplayServer.WindowGetSize().X;
		
		public int GetScreenHeight() => (int)DisplayServer.WindowGetSize().Y;

		private Dictionary<IRenderable, MeshInstance3D> _renderableNodes = new Dictionary<IRenderable, MeshInstance3D>();

		public void step()
		{
			// Rendering logic using Godot
			foreach (var renderable in _renderables)
			{
				if (renderable is IMeshRenderable meshRenderable)
				{
					if (!_renderableNodes.TryGetValue(renderable, out var node))
					{
						// Create new Godot node for the mesh
						node = new MeshInstance3D();
						
						// Set mesh type
						if (meshRenderable is V12.Components.MeshComponent meshComp)
						{
							switch (meshComp.Shape)
							{
								case V12.Components.MeshShape.Box:
									node.Mesh = new BoxMesh();
									((BoxMesh)node.Mesh).Size = new Vector3(meshComp.Width, meshComp.Height, meshComp.Depth);
									break;
								case V12.Components.MeshShape.Sphere:
									node.Mesh = new SphereMesh();
									((SphereMesh)node.Mesh).Radius = meshComp.Width;
									break;
						
							}
						}
						
						root.CurrentScene.AddChild(node);
						_renderableNodes[renderable] = node;
					}
							// get transform, scale or whatever we need

					// Update transform
					if (renderable is V12.Core.Interfaces.Renderer.ITransformRenderable transformRenderable)
					{
						var mat = transformRenderable.Transform;
						// Convert Matrix4x4 to Godot Transform3D
						var basis = new Basis(
							new Vector3(mat.M11, mat.M12, mat.M13),
							new Vector3(mat.M21, mat.M22, mat.M23),
							new Vector3(mat.M31, mat.M32, mat.M33)
						);
						var origin = new Vector3(mat.M41, mat.M42, mat.M43);
						node.Transform = new Transform3D(basis, origin);
					}
				}
			}
		}
	}
}
