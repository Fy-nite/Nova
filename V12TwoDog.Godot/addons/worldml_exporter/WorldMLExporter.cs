using System.Collections.Generic;
using Godot;

namespace V12TwoDog.Editor;

public static class WorldMLExporter
{
    private static readonly HashSet<string> SkipClasses = new()
    {
        "StaticBody3D", "RigidBody3D", "AnimatableBody3D",
        "CharacterBody3D", "Area3D", "CollisionShape3D", "CollisionPolygon3D"
    };

    public static string ExportWorld(Node root)
    {
        var s = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n";
        s += $"<World name=\"{root.Name}\">\n";
        foreach (Node child in root.GetChildren())
            s += AppendElementForNode(child, 1);
        s += "</World>\n";
        return s;
    }

    private static bool IsComponentNode(Node node)
    {
        return node is IV12ComponentNode || node.HasMethod("_v12_component_xml");
    }

    private static string GetComponentXml(Node node, string indent)
    {
        if (node is IV12ComponentNode cv)
            return cv.GetV12ComponentXml(indent);
        if (node.HasMethod("_v12_component_xml"))
            return node.Call("_v12_component_xml", indent).AsString();
        return "";
    }

    private static string AppendElementForNode(Node node, int indent)
    {
        var out_ = "";
        var ind = new string('\t', indent);
        var cls = node.GetClass();

        if (SkipClasses.Contains(cls))
        {
            foreach (Node c in node.GetChildren())
                out_ += AppendElementForNode(c, indent);
            return out_;
        }

        if (node is Node3D n3d)
        {
            out_ += $"{ind}<Element name=\"{node.Name}\">\n";
            out_ += NodeConverter3D.BuildTransformXml(n3d, ind);

            out_ += GetComponentXml(node, ind);

            foreach (Node c in node.GetChildren())
            {
                if (IsComponentNode(c) && c is not Node3D)
                    out_ += GetComponentXml(c, ind);
            }

            if (node.IsInGroup("SpawnPoint") && !IsComponentNode(node))
                out_ += $"{ind}\t<SpawnPointComponent />\n";

            out_ += NodeConverter3D.BuildMeshXml(n3d, ind);
            out_ += NodeConverter3D.HarvestCollider(n3d, ind);
            out_ += NodeConverter3D.BuildRigidBodyXml(n3d, ind);
            out_ += NodeConverter3D.BuildLightXml(n3d, ind);

            if (node is Camera3D cam)
                out_ += NodeConverter3D.BuildCameraXml(cam, ind);

            if (node is GpuParticles3D gpu)
                out_ += NodeConverter3D.BuildParticleXml(gpu, ind);

            out_ += NodeConverter3D.BuildFogVolumeXml(n3d, ind);
            out_ += NodeConverter3D.BuildLabel3DXml(n3d, ind);

            if (node is AudioStreamPlayer3D au)
                out_ += NodeConverter3D.BuildAudioXml(au, ind);

            foreach (Node c in node.GetChildren())
            {
                var ccls = c.GetClass();
                if (SkipClasses.Contains(ccls)) continue;
                if (IsComponentNode(c) && c is not Node3D) continue;
                out_ += AppendElementForNode(c, indent + 1);
            }

            out_ += $"{ind}</Element>\n";
        }
        else if (node is Control control)
        {
            out_ += $"{ind}<Element name=\"{node.Name}\">\n";
            out_ += NodeConverterUI.BuildControlComponentXml(control, ind);

            foreach (Node c in node.GetChildren())
            {
                if (IsComponentNode(c) && c is not Control && c is not Node3D)
                    out_ += GetComponentXml(c, ind);
            }

            foreach (Node c in node.GetChildren())
            {
                if (IsComponentNode(c) && c is not Control && c is not Node3D) continue;
                out_ += AppendElementForNode(c, indent + 1);
            }

            out_ += $"{ind}</Element>\n";
        }
        else if (cls == "CanvasLayer")
        {
            out_ += $"{ind}<Element name=\"{node.Name}\">\n";
            out_ += $"{ind}\t<CanvasComponent />\n";

            foreach (Node c in node.GetChildren())
            {
                if (IsComponentNode(c) && c is not Node3D)
                    out_ += GetComponentXml(c, ind);
            }

            foreach (Node c in node.GetChildren())
            {
                if (IsComponentNode(c) && c is not Node3D) continue;
                out_ += AppendElementForNode(c, indent + 1);
            }

            out_ += $"{ind}</Element>\n";
        }
        else if (cls == "WorldEnvironment")
        {
            out_ += $"{ind}<Element name=\"{node.Name}\">\n";

            var env = node.Get("environment").AsGodotObject() as global::Godot.Environment;
            if (env != null)
            {
                var skyCol = new Color(0.5f, 0.6f, 0.8f);
                var ambCol = new Color(0.2f, 0.2f, 0.25f);
                var ambEnergy = 1.0f;
                var skyboxPath = "";
                var modeStr = "SolidColor";

                if (env.BackgroundMode == global::Godot.Environment.BGMode.Sky)
                {
                    modeStr = "Skybox";
                    if (env.Sky?.SkyMaterial != null)
                        skyboxPath = env.Sky.SkyMaterial.ResourcePath;
                }
                else if (env.BackgroundMode == global::Godot.Environment.BGMode.Color)
                    skyCol = env.BackgroundColor;

                ambCol = env.AmbientLightColor;
                ambEnergy = env.AmbientLightEnergy;

                out_ += $"{ind}\t<EnvironmentComponent mode=\"{modeStr}\" skyR=\"{skyCol.R:F3}\" skyG=\"{skyCol.G:F3}\" skyB=\"{skyCol.B:F3}\" ambientR=\"{ambCol.R:F3}\" ambientG=\"{ambCol.G:F3}\" ambientB=\"{ambCol.B:F3}\" ambientEnergy=\"{ambEnergy:F3}\" skyboxPath=\"{skyboxPath}\" />\n";
            }

            foreach (Node c in node.GetChildren())
            {
                if (IsComponentNode(c) && c is not Node3D)
                    out_ += GetComponentXml(c, ind);
            }

            out_ += $"{ind}</Element>\n";
        }
        else
        {
            foreach (Node c in node.GetChildren())
                out_ += AppendElementForNode(c, indent);
        }

        return out_;
    }
}
