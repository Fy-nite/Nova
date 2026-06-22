using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace V12TwoDog.Editor.Nodes;

[Tool]
[Icon("res://addons/at-icons/node3d/diamond.svg")]
public partial class V12CustomMesh : Node, IV12ComponentNode
{
    [Export(PropertyHint.MultilineText)]
    public string VertexData { get; set; } = "";

    [Export(PropertyHint.MultilineText)]
    public string IndexData { get; set; } = "";

    public string GetV12ComponentXml(string indent)
    {
        var verts = new List<Vector3>();
        var tris = new List<Vector3I>();

        if (string.IsNullOrWhiteSpace(VertexData) || string.IsNullOrWhiteSpace(IndexData))
        {
            var extracted = ExtractFromParentMesh();
            if (extracted.Item1.Count > 0)
            {
                verts = extracted.Item1;
                tris = extracted.Item2;
            }
        }

        if (verts.Count == 0)
        {
            verts = ParseVerts(VertexData);
            tris = ParseTris(IndexData);
        }

        if (verts.Count == 0) return "";

        var outStr = $"{indent}\t<Component type=\"MeshComponent\" Shape=\"Custom\">\n";
        foreach (var v in verts)
            outStr += $"{indent}\t\t<vert x=\"{Fmt(v.X)}\" y=\"{Fmt(v.Y)}\" z=\"{Fmt(v.Z)}\" />\n";
        foreach (var t in tris)
            outStr += $"{indent}\t\t<tri a=\"{t.X}\" b=\"{t.Y}\" c=\"{t.Z}\" />\n";
        outStr += $"{indent}\t</Component>\n";

        return outStr;
    }

    private (List<Vector3>, List<Vector3I>) ExtractFromParentMesh()
    {
        var parent = GetParent();
        if (parent is not MeshInstance3D mi || mi.Mesh == null)
            return (new List<Vector3>(), new List<Vector3I>());

        var arrays = mi.Mesh.SurfaceGetArrays(0);
        if (arrays.Count == 0) return (new List<Vector3>(), new List<Vector3I>());

        var vertData = arrays[(int)Mesh.ArrayType.Vertex];
        if (vertData.VariantType != Variant.Type.PackedVector3Array) return (new List<Vector3>(), new List<Vector3I>());

        var verts = vertData.AsVector3Array();
        var outVerts = new List<Vector3>(verts);

        var idxData = arrays[(int)Mesh.ArrayType.Index];
        var indices = idxData.VariantType == Variant.Type.PackedInt32Array
            ? idxData.AsInt32Array()
            : new int[verts.Length];

        var outTris = new List<Vector3I>();
        if (indices.Length == 0)
        {
            for (int i = 0; i < verts.Length; i++)
                indices = new int[verts.Length];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = i;
        }

        for (int i = 0; i + 2 < indices.Length; i += 3)
            outTris.Add(new Vector3I(indices[i], indices[i + 1], indices[i + 2]));

        return (outVerts, outTris);
    }

    private static List<Vector3> ParseVerts(string text)
    {
        var result = new List<Vector3>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                result.Add(new Vector3(x, y, z));
        }
        return result;
    }

    private static List<Vector3I> ParseTris(string text)
    {
        var result = new List<Vector3I>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            if (int.TryParse(parts[0], out var a) &&
                int.TryParse(parts[1], out var b) &&
                int.TryParse(parts[2], out var c))
                result.Add(new Vector3I(a, b, c));
        }
        return result;
    }

    private static string Fmt(float val)
    {
        return val.ToString("F6", CultureInfo.InvariantCulture);
    }
}
