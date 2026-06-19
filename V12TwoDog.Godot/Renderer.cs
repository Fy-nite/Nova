using System;
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

		private Dictionary<IRenderable, Node3D> _renderableNodes = new Dictionary<IRenderable, Node3D>(); // generalise this for the rest of time.

		public void step()
		{
			// Clean up nodes for renderables that are no longer queued
			var toRemove = new List<IRenderable>();
			foreach (var kvp in _renderableNodes)
			{
				if (!_renderables.Contains(kvp.Key))
				{
					if (GodotObject.IsInstanceValid(kvp.Value))
					{
						GD.Print($"[Renderer] QueueFree node '{kvp.Value.GetType().Name}' for renderable '{kvp.Key.Name}' (ID: {kvp.Key.Id})");
						kvp.Value.QueueFree();
					}
					toRemove.Add(kvp.Key);
				}
			}
			foreach (var r in toRemove)
			{
				_renderableNodes.Remove(r);
			}

			// Rendering logic using Godot
			foreach (var renderable in _renderables)
			{
				if (!_renderableNodes.TryGetValue(renderable, out var node) || !GodotObject.IsInstanceValid(node))
				{
					GD.Print($"[Renderer] No valid node found for renderable '{renderable.Name}' (ID: {renderable.Id}, Type: {renderable.GetType().Name}). Creating new node.");
					// Create new Godot node based on renderable type
					if (renderable is ILightRenderable light)
					{
						switch (light.Type)
						{
							case LightType.Point:
								node = new OmniLight3D();
								break;
							case LightType.Directional:
								node = new DirectionalLight3D();
								break;
							case LightType.Spot:
								node = new SpotLight3D();
								break;
							default:
								node = new OmniLight3D();
								break;
						}
					}
					else if (renderable is IMeshRenderable meshRenderable)
					{
						var item = new MeshInstance3D();
						if (meshRenderable is V12.Components.MeshComponent meshComp)
						{
							switch (meshComp.Shape)
							{
								case V12.Components.MeshShape.Box:
									item.Mesh = new BoxMesh();
									((BoxMesh)item.Mesh).Size = new Vector3(meshComp.Width, meshComp.Height, meshComp.Depth);
									break;
								case V12.Components.MeshShape.Sphere:
									item.Mesh = new SphereMesh();
									((SphereMesh)item.Mesh).Radius = meshComp.Width;
									break;
							}
						}
						node = item;
					}
					else if (renderable is ISpriteRenderable)
					{
						node = new Sprite3D();
					}
					else if (renderable is ITextRenderable)
					{
						node = new Label3D();
					}
					else if (renderable is ICameraRenderable)
					{
						node = new Camera3D();
					}

					if (node != null)
					{
						GD.Print($"[Renderer] Created node '{node.GetType().Name}' for renderable '{renderable.Name}' (ID: {renderable.Id}, Type: {renderable.GetType().Name})");
						root.CurrentScene.AddChild(node);
						_renderableNodes[renderable] = node;
					}
				}

				// Update properties and transform if node exists
				if (node != null)
				{
					if (renderable is ILightRenderable light && node is Light3D light3D)
					{
						var col = light.Color;
						light3D.LightColor = new Color(col.R / 255f, col.G / 255f, col.B / 255f);
						light3D.LightEnergy = light.Intensity;

						if (light3D is OmniLight3D omni)
						{
							omni.OmniRange = light.Range;
						}
						else if (light3D is SpotLight3D spot)
						{
							spot.SpotRange = light.Range;
						}
					}
					else if (renderable is ISpriteRenderable spriteRenderable && node is Sprite3D sprite3D)
					{
						if (spriteRenderable.Texture != null && !string.IsNullOrEmpty(spriteRenderable.Texture.Source))
						{
							sprite3D.Texture = GD.Load<Texture2D>(spriteRenderable.Texture.Source);
						}
						sprite3D.Scale = new Vector3(spriteRenderable.Size.X, spriteRenderable.Size.Y, 1.0f);
						var col = spriteRenderable.Tint;
						sprite3D.Modulate = new Color(col.R / 255f, col.G / 255f, col.B / 255f);
					}
					else if (renderable is ITextRenderable textRenderable && node is Label3D label3D)
					{
						label3D.Text = textRenderable.Text;
						var col = textRenderable.Color;
						label3D.Modulate = new Color(col.R / 255f, col.G / 255f, col.B / 255f);
						label3D.FontSize = (int)textRenderable.FontSize;
					}
					else if (renderable is ICameraRenderable cameraRenderable && node is Camera3D camera3D)
					{
						camera3D.Fov = cameraRenderable.FieldOfView;
						camera3D.Near = cameraRenderable.NearClip;
						camera3D.Far = cameraRenderable.FarClip;
					}

					// Update Transform (use Transform for ITransformRenderable, WorldTransform for others)
					System.Numerics.Matrix4x4 mat = renderable is ITransformRenderable tr
						? tr.Transform
						: renderable.WorldTransform;

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

		public void QueueItems(RenderPacket packet)
		{
			throw new NotImplementedException();
		}

		public void RemoveItems(RenderPacket packet)
		{
			throw new NotImplementedException();
		}
	}
}
