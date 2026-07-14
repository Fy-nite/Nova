using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Godot;

namespace V12TwoDog.Editor;

public static class V12WorldPacker
{
    public static void PackWorld(Node sceneRoot, string outputPath)
    {
        var worldName = Path.GetFileNameWithoutExtension(outputPath);
        var xml = WorldMLExporter.ExportWorld(sceneRoot, worldName);
        var collectedAssets = new HashSet<string>();
        CollectAssets(sceneRoot, collectedAssets);

        using var fs = new FileStream(outputPath, FileMode.Create);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        var entry = archive.CreateEntry("world.xml");
        using (var writer = new StreamWriter(entry.Open()))
            writer.Write(xml);

        foreach (var assetPath in collectedAssets)
        {
            if (!File.Exists(assetPath) && !assetPath.StartsWith("res://"))
                continue;

            if (assetPath.StartsWith("res://"))
            {
                var globalPath = ProjectSettings.GlobalizePath(assetPath);
                if (File.Exists(globalPath))
                {
                    var relPath = assetPath.Substring("res://".Length);
                    var assetEntry = archive.CreateEntry(relPath);
                    using var assetStream = File.OpenRead(globalPath);
                    using var entryStream = assetEntry.Open();
                    assetStream.CopyTo(entryStream);
                }
                continue;
            }

            if (File.Exists(assetPath))
            {
                var mdEntry = archive.CreateEntry(Path.GetFileName(assetPath));
                using var assetStream2 = File.OpenRead(assetPath);
                using var entryStream2 = mdEntry.Open();
                assetStream2.CopyTo(entryStream2);
            }
        }
    }

    private static void CollectAssets(Node node, HashSet<string> assets)
    {
        CollectMeshAssets(node, assets);
        CollectMaterialAndTextures(node, assets);

        if (node is AudioStreamPlayer3D au && au.Stream != null && !string.IsNullOrEmpty(au.Stream.ResourcePath))
            assets.Add(au.Stream.ResourcePath);

        if (node is TextureRect tr && tr.Texture != null && !string.IsNullOrEmpty(tr.Texture.ResourcePath))
            assets.Add(tr.Texture.ResourcePath);

        if (node is TextureButton tb && tb.TextureNormal != null && !string.IsNullOrEmpty(tb.TextureNormal.ResourcePath))
            assets.Add(tb.TextureNormal.ResourcePath);
        if (node is TextureButton tb2 && tb2.TexturePressed != null && !string.IsNullOrEmpty(tb2.TexturePressed.ResourcePath))
            assets.Add(tb2.TexturePressed.ResourcePath);
        if (node is TextureButton tb3 && tb3.TextureHover != null && !string.IsNullOrEmpty(tb3.TextureHover.ResourcePath))
            assets.Add(tb3.TextureHover.ResourcePath);

        if (node.HasMethod("_v12_component_xml"))
        {
            var mp = node.Get("asset_path").AsString();
            if (!string.IsNullOrEmpty(mp))
                assets.Add(mp);

            var skybox = node.Get("skybox_path").AsString();
            if (!string.IsNullOrEmpty(skybox))
                assets.Add(skybox);
        }

        foreach (Node child in node.GetChildren())
            CollectAssets(child, assets);
    }

    private static void CollectMeshAssets(Node node, HashSet<string> assets)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            var path = mi.Mesh.ResourcePath;
            if (!string.IsNullOrEmpty(path))
                assets.Add(path);

            CollectSurfaceTextures(mi.Mesh, mi, assets);
        }

        if (node.GetClass() == "CSGMesh3D")
        {
            var meshVar = node.Get("mesh");
            if (meshVar.VariantType != Variant.Type.Nil && meshVar.Obj is Mesh mesh)
            {
                var meshPath = mesh.ResourcePath;
                if (!string.IsNullOrEmpty(meshPath))
                    assets.Add(meshPath);

                CollectSurfaceTextures(mesh, null, assets);
            }
        }
    }

    private static void CollectSurfaceTextures(Mesh mesh, MeshInstance3D mi, HashSet<string> assets)
    {
        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
        {
            var mat = mi?.GetSurfaceOverrideMaterial(i);
            if (mat == null)
                mat = mesh.SurfaceGetMaterial(i);
            if (mat != null)
                CollectTexturesFromMat(mat, assets);
        }
    }

    private static void CollectMaterialAndTextures(Node node, HashSet<string> assets)
    {
        if (node.HasMethod("get_material"))
        {
            var matVar = node.Get("material");
            if (matVar.Obj is Material mat)
                CollectTexturesFromMat(mat, assets);
        }
        if (node.HasMethod("get_material_override"))
        {
            var ovVar = node.Get("material_override");
            if (ovVar.Obj is Material ov)
                CollectTexturesFromMat(ov, assets);
        }
    }

    private static void CollectTexturesFromMat(Material mat, HashSet<string> assets)
    {
        if (mat is not StandardMaterial3D sm) return;

        AddTexPath(assets, sm.AlbedoTexture);
        AddTexPath(assets, sm.NormalTexture);
        AddTexPath(assets, sm.MetallicTexture);
        AddTexPath(assets, sm.RoughnessTexture);
        AddTexPath(assets, sm.EmissionTexture);
    }

    private static void AddTexPath(HashSet<string> assets, Texture2D tex)
    {
        if (tex == null) return;
        var p = tex.ResourcePath;
        if (!string.IsNullOrEmpty(p))
            assets.Add(p);
    }
}
