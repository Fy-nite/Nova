using System;
using System.Collections.Generic;
using Godot;
using V12.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.Rendering;
using V12.Components;

namespace V12TwoDog
{
		public class Renderer : IRenderer
	{
		private readonly List<IRenderable> _renderables = new List<IRenderable>();
		public SceneTree root;
		private Dictionary<long, Node3D> _nodesByElementId = new();
		private Dictionary<string, StandardMaterial3D> _materialCache = new();
		private Dictionary<string, Texture2D> _textureCache = new();

		public Renderer(SceneTree t)
		{
			root = t;
		}

		public int GetFPS() => 60;

		public RendererInfo GetAllInfo()
		{
			return new RendererInfo();
		}

		public void QueueItem(IRenderable item)
		{
			if (!_renderables.Contains(item))
				_renderables.Add(item);
		}

		public void RemoveItem(IRenderable item)
		{
			_renderables.Remove(item);
		}

		public int GetScreenWidth() => (int)DisplayServer.WindowGetSize().X;

		public int GetScreenHeight() => (int)DisplayServer.WindowGetSize().Y;

		public void step()
		{
			// Legacy path - kept for backwards compat, delegates to ApplySnapshot
			var snapshot = new FrameSnapshot();
			snapshot.Renderables.Capacity = _renderables.Count;
			foreach (var r in _renderables)
			{
				var rs = new RenderableSnapshot();
				rs.ElementId = r.Id;
				rs.Name = r.Name ?? "";
				rs.ParentId = 0;
				if (r is ComponentBase cb && cb.Owner != null)
					rs.Transform = cb.Owner.WorldTransform;
				else
					rs.Transform = r is ITransformRenderable tr ? tr.Transform : r.WorldTransform;
				rs.IsWorldLocked = r is ITransformRenderable itr && itr.IsWorldLocked;

				if (r is ILightRenderable light)
				{
					switch (light.Type)
					{
						case LightType.Directional: rs.NodeType = SnapshotNodeType.LightDirectional; break;
						case LightType.Spot:        rs.NodeType = SnapshotNodeType.LightSpot; break;
						default:                    rs.NodeType = SnapshotNodeType.LightPoint; break;
					}
					rs.LightColor = light.Color;
					rs.LightIntensity = light.Intensity;
					rs.LightRange = light.Range;
					rs.LightAngle = light.Angle;
					rs.LightSpotSoftness = light.SpotSoftness;
				}
				else if (r is IMeshRenderable mesh)
				{
					MeshComponent mc = null;
					if (mesh is MeshComponent mcDirect)
						mc = mcDirect;
					else if (mesh is V12.Components.Renderables.MeshRenderer mr && mr.Mesh is MeshComponent mcWrap)
						mc = mcWrap;
					if (mc != null)
					{
						switch (mc.Shape)
						{
							case MeshShape.Box:    rs.NodeType = SnapshotNodeType.MeshBox; break;
							case MeshShape.Sphere: rs.NodeType = SnapshotNodeType.MeshSphere; break;
							case MeshShape.Custom: rs.NodeType = SnapshotNodeType.MeshCustom; break;
							default:               rs.NodeType = SnapshotNodeType.MeshBox; break;
						}
						rs.MeshWidth = mc.Width;
						rs.MeshHeight = mc.Height;
						rs.MeshDepth = mc.Depth;
						rs.MeshPoints = mc.MeshPoints;
						rs.MeshIndices = mc.Indices;
					}
					else
					{
						rs.NodeType = SnapshotNodeType.RawElement;
					}
				}
				else if (r is ICameraRenderable cam)
				{
					rs.NodeType = SnapshotNodeType.Camera;
					rs.Fov = cam.FieldOfView;
					rs.NearClip = cam.NearClip;
					rs.FarClip = cam.FarClip;
					rs.IsCurrentCamera = cam.IsCurrent;
				}
				else if (r is ISpriteRenderable sprite)
				{
					rs.NodeType = SnapshotNodeType.Sprite;
					rs.TextureSource = sprite.Texture?.Source ?? "";
					rs.SizeX = sprite.Size.X;
					rs.SizeY = sprite.Size.Y;
					rs.Tint = sprite.Tint;
				}
				else if (r is ISvgRenderable svg)
				{
					rs.NodeType = SnapshotNodeType.Svg;
					rs.SvgContent = svg.SvgContent ?? "";
					rs.SizeX = svg.Size.X;
					rs.SizeY = svg.Size.Y;
					rs.Tint = svg.Tint;
				}
				else if (r is ITextRenderable text)
				{
					rs.NodeType = SnapshotNodeType.Text;
					rs.TextContent = text.Text ?? "";
					rs.TextColor = text.Color;
					rs.FontSize = text.FontSize;
				}
				else
				{
					rs.NodeType = SnapshotNodeType.RawElement;
				}
				snapshot.Renderables.Add(rs);
			}
			ApplySnapshot(snapshot);
		}

		public void ApplySnapshot(FrameSnapshot snapshot)
		{
			if (snapshot == null) return;

			var currentIds = new HashSet<long>();
			var elementNodeMap = new Dictionary<long, Node3D>();

			// Build parent map from existing nodes
			foreach (var kvp in _nodesByElementId)
			{
				elementNodeMap[kvp.Key] = kvp.Value;
			}

			// Process every renderable in the snapshot
			foreach (var rs in snapshot.Renderables)
			{
				currentIds.Add(rs.ElementId);

				if (!_nodesByElementId.TryGetValue(rs.ElementId, out var node) || !GodotObject.IsInstanceValid(node))
				{
					node = CreateNode(rs);
					if (node == null) continue;

					root.CurrentScene.AddChild(node);
					_nodesByElementId[rs.ElementId] = node;
					elementNodeMap[rs.ElementId] = node;
				}
				else if (node.GetParent() != root.CurrentScene)
				{
					node.Reparent(root.CurrentScene);
				}

				// Update properties
				UpdateNodeProperties(node, rs);

				// System.Numerics.Matrix4x4 uses row-vector convention (v * M),
				// so rows ARE the basis vectors. Map row-i → Basis column-i.
				var basis = new Basis(
					new Vector3(rs.Transform.M11, rs.Transform.M12, rs.Transform.M13),
					new Vector3(rs.Transform.M21, rs.Transform.M22, rs.Transform.M23),
					new Vector3(rs.Transform.M31, rs.Transform.M32, rs.Transform.M33)
				);
				var origin = new Vector3(rs.Transform.M41, rs.Transform.M42, rs.Transform.M43);
				node.Transform = new Transform3D(basis, origin);
			}

			// Remove stale nodes
			var toRemove = new List<long>();
			foreach (var kvp in _nodesByElementId)
			{
				if (!currentIds.Contains(kvp.Key))
				{
					if (GodotObject.IsInstanceValid(kvp.Value))
						kvp.Value.QueueFree();
					toRemove.Add(kvp.Key);
				}
			}
			foreach (var id in toRemove)
				_nodesByElementId.Remove(id);
		}

		private Node3D CreateNode(RenderableSnapshot rs)
		{
			switch (rs.NodeType)
			{
				case SnapshotNodeType.LightPoint:
					return new OmniLight3D();
				case SnapshotNodeType.LightDirectional:
					return new DirectionalLight3D();
				case SnapshotNodeType.LightSpot:
					return new SpotLight3D();
				case SnapshotNodeType.MeshBox:
				case SnapshotNodeType.MeshSphere:
				case SnapshotNodeType.MeshCustom:
				{
					var item = new MeshInstance3D();
					switch (rs.NodeType)
					{
						case SnapshotNodeType.MeshBox:
							item.Mesh = new BoxMesh();
							((BoxMesh)item.Mesh).Size = new Vector3(rs.MeshWidth, rs.MeshHeight, rs.MeshDepth);
							break;
						case SnapshotNodeType.MeshSphere:
							item.Mesh = new SphereMesh();
							((SphereMesh)item.Mesh).Radius = rs.MeshWidth;
							break;
						case SnapshotNodeType.MeshCustom:
						{
							if (rs.MeshPoints != null && rs.MeshPoints.Length >= 3)
							{
								var verts = new Vector3[rs.MeshPoints.Length / 3];
								for (int i = 0; i < verts.Length; i++)
								{
									verts[i] = new Vector3(
										(float)rs.MeshPoints[i * 3],
										(float)rs.MeshPoints[i * 3 + 1],
										(float)rs.MeshPoints[i * 3 + 2]
									);
								}
								var arrays = new Variant[13];
								arrays[0] = verts;
								if (rs.MeshIndices != null && rs.MeshIndices.Length > 0)
								{
									var idx = new int[rs.MeshIndices.Length];
									for (int i = 0; i < idx.Length; i++)
										idx[i] = (int)rs.MeshIndices[i];
									arrays[12] = idx;
								}
								item.Mesh = new ArrayMesh();
								((ArrayMesh)item.Mesh).AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, new global::Godot.Collections.Array(arrays));
							}
							break;
						}
					}
					return item;
				}
				case SnapshotNodeType.Sprite:
				case SnapshotNodeType.Svg:
					return new Sprite3D();
				case SnapshotNodeType.Text:
					return new Label3D();
				case SnapshotNodeType.Camera:
					return new Camera3D();
				default:
				{
					var n = new Node3D();
					n.Name = string.IsNullOrEmpty(rs.Name) ? $"Node_{rs.ElementId}" : rs.Name;
					return n;
				}
			}
		}

		private void UpdateNodeProperties(Node3D node, RenderableSnapshot rs)
		{
			switch (rs.NodeType)
			{
				case SnapshotNodeType.LightPoint:
				case SnapshotNodeType.LightDirectional:
				case SnapshotNodeType.LightSpot:
				{
					if (node is Light3D light3D)
					{
						var col = rs.LightColor;
						light3D.LightColor = new Color(col.R / 255f, col.G / 255f, col.B / 255f);
						light3D.LightEnergy = rs.LightIntensity;
						if (light3D is OmniLight3D omni)
							omni.OmniRange = rs.LightRange;
						else if (light3D is SpotLight3D spot)
						{
							spot.SpotRange = rs.LightRange;
							spot.SpotAngle = rs.LightAngle;
							spot.SpotAngleAttenuation = rs.LightSpotSoftness;
						}
					}
					break;
				}
				case SnapshotNodeType.MeshBox:
				case SnapshotNodeType.MeshSphere:
				case SnapshotNodeType.MeshCustom:
				{
					if (node is MeshInstance3D mi)
						ApplyMeshMaterial(mi, rs);
					break;
				}
				case SnapshotNodeType.Sprite:
				{
					if (node is Sprite3D sprite3D)
					{
						if (!string.IsNullOrEmpty(rs.TextureSource))
							sprite3D.Texture = GD.Load<Texture2D>(ResolveAssetPath(rs.TextureSource));
						sprite3D.Scale = new Vector3(rs.SizeX, rs.SizeY, 1.0f);
						var col = rs.Tint;
						sprite3D.Modulate = new Color(col.R / 255f, col.G / 255f, col.B / 255f);
					}
					break;
				}
				case SnapshotNodeType.Svg:
				{
					if (node is Sprite3D svgSprite)
					{
						if (!string.IsNullOrEmpty(rs.SvgContent))
						{
							var svgBytes = System.Text.Encoding.UTF8.GetBytes(rs.SvgContent);
							var img = new Image();
							img.LoadSvgFromBuffer(svgBytes, 1.0f);
							svgSprite.Texture = ImageTexture.CreateFromImage(img);
						}
						svgSprite.Scale = new Vector3(rs.SizeX, rs.SizeY, 1.0f);
						var tint = rs.Tint;
						svgSprite.Modulate = new Color(tint.R / 255f, tint.G / 255f, tint.B / 255f);
					}
					break;
				}
				case SnapshotNodeType.Text:
				{
					if (node is Label3D label3D)
					{
						label3D.Text = rs.TextContent;
						var col = rs.TextColor;
						label3D.Modulate = new Color(col.R / 255f, col.G / 255f, col.B / 255f);
						label3D.FontSize = (int)rs.FontSize;
					}
					break;
				}
				case SnapshotNodeType.Camera:
				{
					if (node is Camera3D camera3D)
					{
						camera3D.Fov = rs.Fov;
						camera3D.Near = rs.NearClip;
						camera3D.Far = rs.FarClip;
						camera3D.Current = rs.IsCurrentCamera;
					}
					break;
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

        private void ApplyMeshMaterial(MeshInstance3D mi, RenderableSnapshot rs)
        {
            // Skip if no material data
            if (rs.MatA == 0f && rs.MatR == 0f && rs.MatG == 0f && rs.MatB == 0f
                && rs.MatMetallic == 0f && rs.MatRoughness == 0.5f
                && string.IsNullOrEmpty(rs.MatTexturePath))
                return;

            var cacheKey = $"{rs.ElementId}";
            if (!_materialCache.TryGetValue(cacheKey, out var mat))
            {
                mat = new StandardMaterial3D();
                _materialCache[cacheKey] = mat;
            }

            mat.AlbedoColor = new Color(rs.MatR, rs.MatG, rs.MatB, rs.MatA);
            mat.Metallic = rs.MatMetallic;
            mat.Roughness = rs.MatRoughness;

            // Load and set texture
            if (!string.IsNullOrEmpty(rs.MatTexturePath))
            {
                var texPath = ResolveAssetPath(rs.MatTexturePath);
                Texture2D tex2D = null;
                if (!string.IsNullOrEmpty(texPath))
                {
                    if (!_textureCache.TryGetValue(texPath, out tex2D))
                    {
                        if (System.IO.File.Exists(texPath))
                        {
                            var img = new Image();
                            if (img.Load(texPath) == Error.Ok)
                                tex2D = ImageTexture.CreateFromImage(img);
                        }
                        else
                        {
                            tex2D = GD.Load<Texture2D>(texPath);
                        }
                        if (tex2D != null)
                            _textureCache[texPath] = tex2D;
                    }
                }
                if (tex2D != null)
                    mat.AlbedoTexture = tex2D;
            }

            mi.MaterialOverride = mat;
        }

        private static string ResolveAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var resolved = V12.Core.V12AssetResolver.ResolveGlobal(path);
            if (resolved != path) return resolved;
            // Try relative resolution against the default mount point
            var root = V12.Core.GameRoot.Instance;
            var resolver = root?.Registry?.Get<V12.Core.Interfaces.IAssetResolver>();
            if (resolver is V12.Core.V12AssetResolver vr)
                return vr.ResolveRelative(path);
            return resolved;
        }
	}
}
