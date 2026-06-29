using System;
using System.Collections.Generic;
using System.IO;
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
			var snapshot = new FrameSnapshot();
			snapshot.Renderables.Capacity = _renderables.Count;
			var parentChainIds = new HashSet<long>();

			foreach (var r in _renderables)
			{
				var rs = new RenderableSnapshot();
				rs.ElementId = r.Id;
				rs.Name = r.Name ?? "";
				rs.ParentId = 0;

				if (r is ComponentBase cb && cb.Owner != null)
				{
					var owner = cb.Owner;

					// Set ParentId from element parent
					if (owner.Parent != null)
						rs.ParentId = owner.Parent.Id;

					// Walk up the parent chain and add entries for any non-renderable ancestors
					var ancestor = owner.Parent;
					while (ancestor != null)
					{
						if (!parentChainIds.Add(ancestor.Id)) break;

						// Only add as placeholder if this ancestor has no IRenderable
						// (all renderable components produce their own entries in _renderables)
						bool hasRenderable = false;
						foreach (var comp in ancestor.Components)
						{
							if (comp is IRenderable)
							{
								hasRenderable = true;
								break;
							}
						}

						if (!hasRenderable)
						{
							var parentRs = new RenderableSnapshot();
							parentRs.ElementId = ancestor.Id;
							parentRs.ParentId = ancestor.Parent?.Id ?? 0;
							parentRs.Name = ancestor.Name ?? "";
							parentRs.NodeType = SnapshotNodeType.RawElement;
							var tc = ancestor.GetComponent<TransformComponent>();
							parentRs.Transform = tc?.Transform ?? System.Numerics.Matrix4x4.Identity;
							parentRs.LocalTransform = parentRs.Transform;
							parentRs.HasLocalTransform = true;
							snapshot.Renderables.Add(parentRs);
						}

						ancestor = ancestor.Parent;
					}

					// Use local transform (hierarchy accumulates through scene tree)
					var localTc = owner.GetComponent<TransformComponent>();
					rs.Transform = localTc?.Transform ?? System.Numerics.Matrix4x4.Identity;
					rs.LocalTransform = rs.Transform;
					rs.HasLocalTransform = true;
					rs.IsWorldLocked = false;
				}
				else if (r is ITransformRenderable tr)
				{
					rs.Transform = tr.Transform;
					rs.LocalTransform = rs.Transform;
					rs.HasLocalTransform = true;
					rs.IsWorldLocked = tr.IsWorldLocked;
				}
				else
				{
					rs.Transform = r.WorldTransform;
				}

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
							case MeshShape.Box:      rs.NodeType = SnapshotNodeType.MeshBox; break;
							case MeshShape.Sphere:   rs.NodeType = SnapshotNodeType.MeshSphere; break;
							case MeshShape.Capsule:  rs.NodeType = SnapshotNodeType.MeshCapsule; break;
							case MeshShape.Cylinder: rs.NodeType = SnapshotNodeType.MeshCylinder; break;
							case MeshShape.Plane:    rs.NodeType = SnapshotNodeType.MeshPlane; break;
							case MeshShape.Custom:   rs.NodeType = SnapshotNodeType.MeshCustom; break;
							default:                 rs.NodeType = SnapshotNodeType.MeshBox; break;
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

			// ── Pass 1: Create all nodes (temporarily parented to root) ──
			foreach (var rs in snapshot.Renderables)
			{
				currentIds.Add(rs.ElementId);

				if (!_nodesByElementId.TryGetValue(rs.ElementId, out var node) || !GodotObject.IsInstanceValid(node))
				{
					node = CreateNode(rs);
					if (node == null) continue;

					root.CurrentScene.AddChild(node);
					_nodesByElementId[rs.ElementId] = node;
				}
			}

			// ── Pass 2: Set parent-child hierarchy and local transforms ──
			foreach (var rs in snapshot.Renderables)
			{
				if (!_nodesByElementId.TryGetValue(rs.ElementId, out var node)) continue;

				// Re-parent to element parent's node (or root)
				Node targetParent = root.CurrentScene;
				if (rs.ParentId != 0
					&& _nodesByElementId.TryGetValue(rs.ParentId, out var parentNode)
					&& GodotObject.IsInstanceValid(parentNode))
				{
					targetParent = parentNode;
				}

				if (node.GetParent() != targetParent)
					node.Reparent(targetParent);

				// Use local transform for hierarchy; fall back to world transform if unset
				var t = rs.HasLocalTransform ? rs.LocalTransform : rs.Transform;
				var basis = new Basis(
					new Vector3(t.M11, t.M12, t.M13),
					new Vector3(t.M21, t.M22, t.M23),
					new Vector3(t.M31, t.M32, t.M33)
				);
				var origin = new Vector3(t.M41, t.M42, t.M43);
				node.Transform = new Transform3D(basis, origin);

				// Update properties
				UpdateNodeProperties(node, rs);
			}

			// ── Remove stale nodes ──
			var toRemove = new List<long>();
			foreach (var kvp in _nodesByElementId)
			{
				if (!currentIds.Contains(kvp.Key))
				{
					if (GodotObject.IsInstanceValid(kvp.Value))
					{
						// Reparent children to root before removing parent
						var children = kvp.Value.GetChildren();
						foreach (Node child in children)
							child.Reparent(root.CurrentScene);
						kvp.Value.QueueFree();
					}
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
				case SnapshotNodeType.MeshCapsule:
				case SnapshotNodeType.MeshCylinder:
				case SnapshotNodeType.MeshPlane:
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
							((SphereMesh)item.Mesh).Height = rs.MeshHeight;
							break;
						case SnapshotNodeType.MeshCapsule:
							item.Mesh = new CapsuleMesh();
							((CapsuleMesh)item.Mesh).Radius = rs.MeshWidth;
							((CapsuleMesh)item.Mesh).Height = rs.MeshHeight;
							break;
						case SnapshotNodeType.MeshCylinder:
							item.Mesh = new CylinderMesh();
							((CylinderMesh)item.Mesh).TopRadius = rs.MeshWidth;
							((CylinderMesh)item.Mesh).BottomRadius = rs.MeshWidth;
							((CylinderMesh)item.Mesh).Height = rs.MeshHeight;
							break;
						case SnapshotNodeType.MeshPlane:
							item.Mesh = new PlaneMesh();
							((PlaneMesh)item.Mesh).Size = new Vector2(rs.MeshWidth, rs.MeshDepth);
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

								var normals = new Vector3[verts.Length];
								if (rs.MeshIndices != null && rs.MeshIndices.Length > 0)
								{
									var idx = new int[rs.MeshIndices.Length];
									for (int i = 0; i < idx.Length; i++)
										idx[i] = (int)rs.MeshIndices[i];
									arrays[12] = idx;

									for (int i = 0; i < idx.Length; i += 3)
									{
										var v0 = verts[idx[i]];
										var v1 = verts[idx[i + 1]];
										var v2 = verts[idx[i + 2]];
										var n = (v1 - v0).Cross(v2 - v0).Normalized();
										normals[idx[i]] += n;
										normals[idx[i + 1]] += n;
										normals[idx[i + 2]] += n;
									}
								}
								else
								{
									for (int i = 0; i < verts.Length; i += 3)
									{
										var v0 = verts[i];
										var v1 = verts[i + 1];
										var v2 = verts[i + 2];
										var n = (v1 - v0).Cross(v2 - v0).Normalized();
										normals[i] += n;
										normals[i + 1] += n;
										normals[i + 2] += n;
									}
								}
								for (int i = 0; i < normals.Length; i++)
									normals[i] = normals[i].Normalized();
								arrays[1] = normals;

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
				case SnapshotNodeType.MeshCapsule:
				case SnapshotNodeType.MeshCylinder:
				case SnapshotNodeType.MeshPlane:
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
            // Skip if no material data (RenderableSnapshot struct defaults all floats to 0)
            if (rs.MatA == 0f && rs.MatR == 0f && rs.MatG == 0f && rs.MatB == 0f
                && rs.MatMetallic == 0f && rs.MatRoughness == 0f
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

            if (!string.IsNullOrEmpty(rs.MatTexturePath))
            {
                var tex2D = LoadTextureFromAnywhere(rs.MatTexturePath);
                if (tex2D != null)
                    mat.AlbedoTexture = tex2D;
            }

            mat.Uv1Offset = new Vector3(rs.MatUvOffsetX, rs.MatUvOffsetY, 0f);
            mat.Uv1Scale = new Vector3(rs.MatUvScaleX, rs.MatUvScaleY, 1f);

            mi.MaterialOverride = mat;
        }

        private Texture2D LoadTextureFromAnywhere(string texturePath)
        {
            if (string.IsNullOrEmpty(texturePath)) return null;
            if (_textureCache.TryGetValue(texturePath, out var cached))
                return cached;

            Texture2D tex2D = null;

            // Strategy 1: Direct v12:// resolution via the asset resolver
            if (tex2D == null && texturePath.StartsWith("v12://", StringComparison.OrdinalIgnoreCase))
            {
                var root = V12.Core.GameRoot.Instance;
                if (root == null)
                    GD.Print($"[Tex] S1 v12:// — GameRoot.Instance is NULL");
                else
                {
                    var resolver = root.Registry?.Get<V12.Core.Interfaces.IAssetResolver>();
                    if (resolver != null)
                    {
                        string phys = resolver.Resolve(texturePath);
                        GD.Print($"[Tex] S1 v12:// → resolved: '{phys}' exists={File.Exists(phys)}");
                        if (!string.IsNullOrEmpty(phys) && phys != texturePath && File.Exists(phys))
                            tex2D = LoadTextureFile(phys);
                    }
                    else
                    {
                        int regCount = root.Registry?.GetServices()?.Count ?? -1;
                        GD.Print($"[Tex] S1 v12:// — IAssetResolver NOT in registry (registered services: {regCount})");
                    }
                }
            }

            // Strategy 2: Resolve via asset system and load from physical path
            if (tex2D == null)
            {
                string resolved = ResolveAssetPath(texturePath);
                GD.Print($"[Tex] S2 ResolveAssetPath('{texturePath}') → '{resolved}' exists={File.Exists(resolved)}");
                if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                    tex2D = LoadTextureFile(resolved);
            }

            // Strategy 3: Search all world extract paths by filename
            if (tex2D == null)
            {
                string cleanName = Path.GetFileName(StripResPrefix(texturePath));
                GD.Print($"[Tex] S3 searching worlds for '{cleanName}'");
                if (!string.IsNullOrEmpty(cleanName))
                {
                    var root = V12.Core.GameRoot.Instance;
                    if (root != null)
                    {
                        int worldCount = root.Worlds.Count;
                        GD.Print($"[Tex] S3 GameRoot.Worlds count={worldCount}");
                        foreach (var w in root.Worlds)
                        {
                            if (string.IsNullOrEmpty(w.ExtractPath) || !Directory.Exists(w.ExtractPath))
                                continue;
                            foreach (var f in Directory.EnumerateFiles(w.ExtractPath, cleanName, SearchOption.AllDirectories))
                            {
                                GD.Print($"[Tex] S3 found candidate: '{f}'");
                                tex2D = LoadTextureFile(f);
                                if (tex2D != null) break;
                            }
                            if (tex2D != null) break;
                        }
                    }
                }
            }

            // Strategy 4: Last resort, try GD.Load (only for res:// paths, not v12://)
            if (tex2D == null && !string.IsNullOrEmpty(texturePath)
                && texturePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                GD.Print($"[Tex] S4 GD.Load('{texturePath}')");
                tex2D = GD.Load<Texture2D>(texturePath);
            }

            if (tex2D != null)
                GD.Print($"[Tex] Loaded OK: '{texturePath}'");
            else
                GD.Print($"[Tex] FAILED: '{texturePath}'");

            if (tex2D != null)
                _textureCache[texturePath] = tex2D;

            return tex2D;
        }

        private static Texture2D LoadTextureFile(string filePath)
        {
            try
            {
                var bytes = File.ReadAllBytes(filePath);
                var img = new Image();
                Error err;
                switch (Path.GetExtension(filePath).ToLowerInvariant())
                {
                    case ".png":  err = img.LoadPngFromBuffer(bytes); break;
                    case ".jpg":
                    case ".jpeg": err = img.LoadJpgFromBuffer(bytes); break;
                    case ".webp": err = img.LoadWebpFromBuffer(bytes); break;
                    case ".ktx":  err = img.LoadKtxFromBuffer(bytes); break;
                    case ".bmp":  err = img.LoadBmpFromBuffer(bytes); break;
                    case ".tga":  err = img.LoadTgaFromBuffer(bytes); break;
                    default:      err = img.Load(filePath); break;
                }
                if (err == Error.Ok)
                    return ImageTexture.CreateFromImage(img);
                GD.Print($"[Tex] LoadTextureFile '{filePath}' Image.Load returned {err}");
            }
            catch (Exception ex)
            {
                GD.Print($"[Tex] LoadTextureFile '{filePath}' threw {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        private static string ResolveAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // 1. Resolve via global v12:// handler
            var resolved = V12.Core.V12AssetResolver.ResolveGlobal(path);
            if (resolved != path) return resolved;

            var root = V12.Core.GameRoot.Instance;

            // 2. Resolve via the globally registered asset resolver (has mount info)
            var resolver = root?.Registry?.Get<V12.Core.Interfaces.IAssetResolver>();
            if (resolver is V12.Core.V12AssetResolver vr)
            {
                var rel = vr.ResolveRelative(path);
                if (rel != path) return rel;
            }

            // 3. Fallback: walk worlds and try their ExtractPath directly
            if (root != null)
            {
                string cleanPath = StripResPrefix(path);
                foreach (var w in root.Worlds)
                {
                    if (!string.IsNullOrEmpty(w.ExtractPath))
                    {
                        var combined = Path.Combine(w.ExtractPath, cleanPath);
                        if (File.Exists(combined))
                            return combined;
                    }
                }
            }

            // 4. Last resort: strip res:// and try as raw filesystem path
            string stripped = StripResPrefix(path);
            if (stripped != path && File.Exists(stripped))
                return stripped;

            return resolved;
        }

        private static string StripResPrefix(string p)
        {
            if (p.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
                return p.Substring(6).TrimStart('/');
            return p;
        }
	}
}
