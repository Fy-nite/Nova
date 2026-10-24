using System;
using System.Collections.Generic;
using Godot;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using V12.Core.UI.Paper;

namespace V12TwoDog.Paper
{
    /// <summary>
    /// Godot host for Paper/Quill. Subclasses the engine-level
    /// <see cref="PaperCanvasRenderer"/> and uploads each Quill <see cref="DrawCall"/>
    /// as a Godot <c>ArrayMesh</c> drawn through <see cref="CanvasShader"/>, a
    /// <c>canvas_item</c> port of Quill's canonical <c>Canvas.slang</c>. This is the
    /// same pipeline the OpenTK sample runs (<c>GL.DrawElements</c> per draw call),
    /// with Godot's canvas transform replacing the projection matrix.
    ///
    /// Per draw call: slice the shared index buffer, build the mesh from the shared
    /// vertex buffer, then push the draw call's scissor/brush/texture/text uniforms
    /// onto a cached <c>ShaderMaterial</c> and bind the brush texture + font atlas.
    /// </summary>
    public sealed class GodotPaperCanvasRenderer : PaperCanvasRenderer
    {
        private readonly CanvasLayer _layer;
        private readonly Control _root;
        private readonly Shader _shader;
        private readonly ShaderMaterial _whiteMaterial;   // used only as a fallback
        private readonly ImageTexture _whiteTexture;
        private readonly List<MeshInstance2D> _pool = new();
        private readonly Dictionary<object, GodotTexture> _textures = new();
        private readonly Dictionary<int, ShaderMaterial> _materials = new();

        private sealed class GodotTexture
        {
            public Image Image = null!;
            public ImageTexture Texture = null!;
            public int W, H;
        }

        public GodotPaperCanvasRenderer(CanvasLayer layer, Control root)
        {
            _layer = layer ?? throw new ArgumentNullException(nameof(layer));
            _root = root ?? throw new ArgumentNullException(nameof(root));

            _shader = new Shader { Code = CanvasShader.Code };

            // 1x1 opaque white so shape fills that carry no texture still sample something sane.
            var white = Image.Create(1, 1, false, Image.Format.Rgba8);
            white.Fill(new global::Godot.Color(1, 1, 1, 1));
            _whiteTexture = ImageTexture.CreateFromImage(white);
            _whiteMaterial = new ShaderMaterial { Shader = _shader };
            _whiteMaterial.SetShaderParameter("tex0", _whiteTexture);
            _whiteMaterial.SetShaderParameter("fontTex", _whiteTexture);

            GD.Print("[GodotPaperCanvasRenderer] Quill canvas shader bridge ready (canvas_item port of Canvas.slang)");
        }

        // ── Textures ────────────────────────────────────────────────────────

        public override object CreateTexture(uint width, uint height)
        {
            var img = Image.Create((int)width, (int)height, false, Image.Format.Rgba8);
            img.Fill(new global::Godot.Color(0, 0, 0, 0));
            var tex = ImageTexture.CreateFromImage(img);
            var wrapper = new GodotTexture { Image = img, Texture = tex, W = (int)width, H = (int)height };
            _textures[tex] = wrapper;
            return wrapper;
        }

        public override Int2 GetTextureSize(object texture)
        {
            if (texture is GodotTexture gt) return new Int2(gt.W, gt.H);
            return base.GetTextureSize(texture);
        }

        public override void SetTextureData(object texture, IntRect bounds, byte[] data)
        {
            if (texture is GodotTexture gt)
            {
                try
                {
                    // Quill writes glyphs as sub-rects into the SDF atlas; blit them into the full-size image.
                    var (bx, by, bw, bh) = GetBounds(bounds);

                    if (data.Length == gt.W * gt.H * 4)
                    {
                        var full = Image.CreateFromData(gt.W, gt.H, false, Image.Format.Rgba8, data);
                        gt.Image = full;
                        gt.Texture.Update(full);
                        return;
                    }

                    // Defensive: if the rect and payload disagree, infer a square glyph tile.
                    if (bw <= 0 || bh <= 0 || bw * bh * 4 != data.Length)
                    {
                        int n = data.Length / 4;
                        int s = (int)Math.Sqrt(n);
                        if (s <= 0 || s * s != n) return;
                        bx = 0; by = 0; bw = s; bh = s;
                    }

                    var sub = Image.CreateFromData(bw, bh, false, Image.Format.Rgba8, data);
                    gt.Image.BlitRect(sub, new Rect2I(0, 0, bw, bh), new Vector2I(bx, by));
                    gt.Texture.Update(gt.Image);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[GodotPaperCanvasRenderer] SetTextureData failed: {ex.Message}");
                }
                return;
            }
            base.SetTextureData(texture, bounds, data);
        }

        private static (int x, int y, int w, int h) GetBounds(IntRect r)
        {
            // Quill passes IntRect as (Min, Max) in Prowl.Vector (namespace Prowl.Vector).
            int x = r.Min.X, y = r.Min.Y;
            int w = Math.Max(0, r.Max.X - x);
            int h = Math.Max(0, r.Max.Y - y);
            return (x, y, w, h);
        }

        private global::Godot.Texture2D ResolveTexture(object? o)
        {
            if (o is GodotTexture gt) return gt.Texture;
            // Host-owned textures (e.g. a SubViewport's ViewportTexture) are drawn
            // straight by DrawImage, so bind them as-is.
            if (o is global::Godot.Texture2D external) return external;
            return _whiteTexture;
        }

        // ── Draw ────────────────────────────────────────────────────────────

        public override void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
        {
            base.RenderCalls(canvas, drawCalls); // keep PaperFrame capture for headless inspection

            if (!GodotObject.IsInstanceValid(_root)) return;

            try
            {
                var verts = canvas.VertexBuffer;
                var idx = canvas.IndexBuffer;
                if (drawCalls == null || drawCalls.Count == 0 || verts == null || idx == null || verts.Length == 0 || idx.Length == 0)
                {
                    HideFrom(0);
                    return;
                }

                // Vertex attributes are shared by every draw call (each call slices the index buffer).
                var gVerts = new Vector3[verts.Length];
                var gUvs = new Vector2[verts.Length];
                var gColors = new global::Godot.Color[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    gVerts[i] = new Vector3(verts[i].Position.X, verts[i].Position.Y, 0);
                    gUvs[i] = new Vector2(verts[i].UV.X, verts[i].UV.Y);
                    gColors[i] = new global::Godot.Color(verts[i].r / 255f, verts[i].g / 255f, verts[i].b / 255f, verts[i].a / 255f);
                }

                float fbScale = canvas.FramebufferScale <= 0f ? 1f : canvas.FramebufferScale;
                float sdfRange = 4f;
                global::Godot.Texture2D fontAtlas = _whiteTexture;
                Vector2 atlasTexel = new Vector2(1f / 1024f, 1f / 1024f);
                try
                {
                    var engine = canvas.Text?.FontEngine;
                    if (engine != null)
                    {
                        sdfRange = engine.DistanceRange;
                        if (engine.Texture is GodotTexture gt)
                        {
                            fontAtlas = gt.Texture;
                            atlasTexel = new Vector2(1f / Math.Max(1, gt.W), 1f / Math.Max(1, gt.H));
                        }
                    }
                }
                catch { }

                int indexOffset = 0;
                int used = 0;

                for (int di = 0; di < drawCalls.Count; di++)
                {
                    var dc = drawCalls[di];
                    int count = dc.ElementCount;
                    if (count <= 0) { indexOffset += count; continue; }
                    if (indexOffset + count > idx.Length) count = idx.Length - indexOffset;
                    if (count <= 0) break;

                    var gIndices = new int[count];
                    for (int i = 0; i < count; i++) gIndices[i] = (int)idx[indexOffset + i];

                    var arrays = new global::Godot.Collections.Array();
                    arrays.Resize((int)Mesh.ArrayType.Max);
                    arrays[(int)Mesh.ArrayType.Vertex] = gVerts;
                    arrays[(int)Mesh.ArrayType.TexUV] = gUvs;
                    arrays[(int)Mesh.ArrayType.Color] = gColors;
                    arrays[(int)Mesh.ArrayType.Index] = gIndices;

                    var mesh = new ArrayMesh();
                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

                    var inst = Acquire(used++);
                    inst.Mesh = mesh;
                    inst.Material = BuildMaterial(di, dc, fbScale, sdfRange, fontAtlas, atlasTexel);
                    inst.Visible = true;

                    indexOffset += dc.ElementCount;
                }

                HideFrom(used);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GodotPaperCanvasRenderer] RenderCalls failed: {ex}");
            }
        }

        private MeshInstance2D Acquire(int index)
        {
            if (index < _pool.Count) return _pool[index];
            var inst = new MeshInstance2D { Name = $"PaperMesh_{index}" };
            _root.AddChild(inst);
            _pool.Add(inst);
            return inst;
        }

        private void HideFrom(int index)
        {
            for (int i = index; i < _pool.Count; i++)
                _pool[i].Visible = false;
        }

        private ShaderMaterial BuildMaterial(int slot, in DrawCall dc, float fbScale, float sdfRange, global::Godot.Texture2D fontAtlas, Vector2 atlasTexel)
        {
            if (!_materials.TryGetValue(slot, out var mat))
            {
                mat = new ShaderMaterial { Shader = _shader };
                _materials[slot] = mat;
            }

            var brush = dc.Brush;

            dc.GetScissor(fbScale, out var sT, out var sTr, out var sExt);
            dc.GetBrushTransform(fbScale, out var bT, out var bTr);
            dc.GetTextureTransform(fbScale, out var tT, out var tTr);

            mat.SetShaderParameter("scissorTransform", new Vector4(sT.X, sT.Y, sT.Z, sT.W));
            mat.SetShaderParameter("scissorTranslation", new Vector2(sTr.X, sTr.Y));
            mat.SetShaderParameter("scissorExt", new Vector2(sExt.X, sExt.Y));

            mat.SetShaderParameter("brushTransform", new Vector4(bT.X, bT.Y, bT.Z, bT.W));
            mat.SetShaderParameter("brushTranslation", new Vector2(bTr.X, bTr.Y));
            mat.SetShaderParameter("brushType", (int)brush.Type);
            mat.SetShaderParameter("brushColor1", ToVec4(brush.Color1));
            mat.SetShaderParameter("brushColor2", ToVec4(brush.Color2));
            mat.SetShaderParameter("brushParams", new Vector4(brush.Point1.X, brush.Point1.Y, brush.Point2.X, brush.Point2.Y));
            mat.SetShaderParameter("brushParams2", new Vector2(brush.CornerRadii, brush.Feather));

            mat.SetShaderParameter("textureTransform", new Vector4(tT.X, tT.Y, tT.Z, tT.W));
            mat.SetShaderParameter("textureTranslation", new Vector2(tTr.X, tTr.Y));

            mat.SetShaderParameter("sdfPxRange", sdfRange);
            mat.SetShaderParameter("atlasTexelSize", atlasTexel);

            mat.SetShaderParameter("tex0", ResolveTexture(dc.Texture));
            mat.SetShaderParameter("fontTex", fontAtlas);

            return mat;
        }

        private static Vector4 ToVec4(Prowl.Vector.Color32 c) =>
            new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

        public override void Dispose()
        {
            base.Dispose();
            foreach (var inst in _pool)
                if (GodotObject.IsInstanceValid(inst)) inst.QueueFree();
            _pool.Clear();
            _materials.Clear();
        }
    }
}
