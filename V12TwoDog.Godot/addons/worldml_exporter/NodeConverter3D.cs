using System;
using System.Collections.Generic;
using Godot;

namespace V12TwoDog.Editor;

public static class NodeConverter3D
{
    public static string BuildTransformXml(Node3D node, string ind)
    {
        var gp = node.Position;
        var rotDeg = node.RotationDegrees;
        var s = $"{ind}\t<TransformComponent x=\"{gp.X:F3}\" y=\"{gp.Y:F3}\" z=\"{gp.Z:F3}\" rotation=\"{rotDeg.Y:F3}\" rotationX=\"{rotDeg.X:F3}\" rotationY=\"{rotDeg.Y:F3}\" rotationZ=\"{rotDeg.Z:F3}\" />\n";

        var sc = node.Scale;
        if (!(Mathf.IsEqualApprox(sc.X, 1f) && Mathf.IsEqualApprox(sc.Y, 1f) && Mathf.IsEqualApprox(sc.Z, 1f)))
            s += $"{ind}\t<ScaleComponent scaleX=\"{sc.X:F3}\" scaleY=\"{sc.Y:F3}\" scaleZ=\"{sc.Z:F3}\" />\n";

        return s;
    }

	public static string BuildMeshXml(Node3D node, string ind, string worldName = null)
	{
		var cls = node.GetClass();
		var s = "";

		if (cls == "CSGBox3D")
		{
			var sz = (Vector3)node.Get("size");
			s += MeshComponents(ind, "csg_mesh", "Box", sz.X, sz.Y, sz.Z);
			s += CsgCollider(node, ind, "Box", sz.X, sz.Y, sz.Z);
			s += CsgMaterialXml(node, ind, worldName);
		}
		else if (cls == "CSGSphere3D")
		{
			var r = (float)node.Get("radius");
			s += MeshComponents(ind, "csg_mesh", "Sphere", r * 2f, r * 2f, r * 2f);
			s += CsgCollider(node, ind, "Sphere", r * 2f, r * 2f, r * 2f);
			s += CsgMaterialXml(node, ind, worldName);
		}
		else if (cls == "CSGCylinder3D")
		{
			var cr = (float)node.Get("radius");
			var ch = (float)node.Get("height");
			s += MeshComponents(ind, "csg_mesh", "Cylinder", cr * 2f, ch, cr * 2f);
			s += CsgCollider(node, ind, "Cylinder", cr * 2f, ch, cr * 2f);
			s += CsgMaterialXml(node, ind, worldName);
		}
		else if (cls == "CSGCapsule3D")
		{
			var r2 = (float)node.Get("radius");
			var h2 = (float)node.Get("height");
			s += MeshComponents(ind, "csg_mesh", "Capsule", r2 * 2f, h2, r2 * 2f);
			s += CsgCollider(node, ind, "Capsule", r2 * 2f, h2, r2 * 2f);
			s += CsgMaterialXml(node, ind, worldName);
		}
		else if (cls == "CSGPlane3D")
		{
			var psz = (Vector2)node.Get("size");
			s += $"{ind}\t<ColliderComponent Shape=\"Plane\" Width=\"{psz.X:F3}\" Height=\"0.000\" Depth=\"{psz.Y:F3}\" />\n";
			s += CsgCollider(node, ind, "Plane", psz.X, 0f, psz.Y);
			s += CsgMaterialXml(node, ind, worldName);
		}
		else if (cls == "CSGMesh3D")
		{
			s += CsgMesh3DComponents(node, ind, worldName);
		}
		else if (cls == "CSGCombiner3D" || cls == "CSGTorus3D")
		{
			s += BakeCsgNode(node, ind, worldName);
		}

		if (node is MeshInstance3D mi)
			s += MeshInstanceComponents(mi, ind, worldName);

		return s;
	}

	private static string CsgMesh3DComponents(Node3D node, string ind, string worldName = null)
	{
		var meshVar = node.Get("mesh");
		if (meshVar.VariantType == Variant.Type.Nil) return "";

		var mesh = meshVar.AsGodotObject() as Mesh;
		if (mesh == null) return "";

		var mcls = mesh.GetClass();
		var shapeName = "";
		var w = 1.0; var h = 1.0; var d = 1.0;

		if (mcls == "BoxMesh")
		{
			var sz = ((BoxMesh)mesh).Size;
			shapeName = "Box"; w = sz.X; h = sz.Y; d = sz.Z;
		}
		else if (mcls == "SphereMesh")
		{
			var sm = (SphereMesh)mesh;
			shapeName = "Sphere"; w = sm.Radius * 2.0; h = sm.Height; d = sm.Radius * 2.0;
		}
		else if (mcls == "CapsuleMesh")
		{
			var cm = (CapsuleMesh)mesh;
			shapeName = "Capsule"; w = cm.Radius * 2.0; h = cm.Height; d = cm.Radius * 2.0;
		}
		else if (mcls == "CylinderMesh")
		{
			var cym = (CylinderMesh)mesh;
			shapeName = "Cylinder"; w = cym.TopRadius * 2.0; h = cym.Height; d = cym.TopRadius * 2.0;
		}
		else if (mcls == "PlaneMesh")
		{
			var sz2 = ((PlaneMesh)mesh).Size;
			shapeName = "Plane"; w = sz2.X; h = 0.0; d = sz2.Y;
		}

		var out_ = "";
		if (shapeName != "")
			out_ += MeshComponents(ind, "csg_mesh", shapeName, w, h, d);
		else
			out_ += BuildCustomMeshXml(mesh, ind, "csg_mesh");

		// Material from CSG node override or mesh surface
		var matVar = node.Get("material");
		StandardMaterial3D csgMat = null;
		if (matVar.VariantType != Variant.Type.Nil && matVar.Obj is StandardMaterial3D smOverride)
			csgMat = smOverride;
		else if (mesh.GetSurfaceCount() > 0)
		{
			var surfMat = mesh.SurfaceGetMaterial(0);
			if (surfMat is StandardMaterial3D smSurf)
				csgMat = smSurf;
		}

		if (csgMat != null)
			out_ += BuildMaterialXml(csgMat, ind, csgMat.ResourcePath, worldName);

		return out_;
	}

	private static string CsgMaterialXml(Node3D node, string ind, string worldName = null)
	{
		var matVar = node.Get("material");
		if (matVar.VariantType == Variant.Type.Nil) return "";

		if (matVar.Obj is StandardMaterial3D sm)
			return BuildMaterialXml(sm, ind, sm.ResourcePath, worldName);

		return "";
	}

    public static string BuildLightXml(Node3D node, string ind)
    {
        var s = "";
        if (node is OmniLight3D ol)
        {
            var lc = ol.LightColor;
            s += $"{ind}\t<GenericLightComponent Type=\"Point\" colorR=\"{lc.R:F3}\" colorG=\"{lc.G:F3}\" colorB=\"{lc.B:F3}\" range=\"{ol.OmniRange:F3}\" energy=\"{ol.LightEnergy:F3}\" shadowEnabled=\"{ol.ShadowEnabled}\" />\n";
        }
        else if (node is SpotLight3D sl)
        {
            var lc = sl.LightColor;
            s += $"{ind}\t<GenericLightComponent Type=\"Spot\" colorR=\"{lc.R:F3}\" colorG=\"{lc.G:F3}\" colorB=\"{lc.B:F3}\" range=\"{sl.SpotRange:F3}\" energy=\"{sl.LightEnergy:F3}\" angle=\"{sl.SpotAngle:F2}\" spotSoftness=\"{sl.SpotAngleAttenuation:F3}\" shadowEnabled=\"{sl.ShadowEnabled}\" />\n";
        }
        else if (node is DirectionalLight3D dl)
        {
            var lc = dl.LightColor;
            s += $"{ind}\t<GenericLightComponent colorR=\"{lc.R:F3}\" colorG=\"{lc.G:F3}\" colorB=\"{lc.B:F3}\" energy=\"{dl.LightEnergy:F3}\" shadowEnabled=\"{dl.ShadowEnabled}\" />\n";
        }
        return s;
    }

    public static string BuildCameraXml(Camera3D cam, string ind)
    {
        return $"{ind}\t<CameraComponent fov=\"{cam.Fov:F1}\" isCurrent=\"{cam.Current}\" />\n";
    }

    public static string BuildParticleXml(GpuParticles3D p, string ind)
    {
        var dirX = 0.0; var dirY = 1.0; var dirZ = 0.0;
        var spread = 45.0; var speedMin = 1.0; var speedMax = 3.0;
        var emitRadius = 0.0;

        if (p.ProcessMaterial is ParticleProcessMaterial pm)
        {
            var d = pm.Direction.Normalized();
            dirX = d.X; dirY = d.Y; dirZ = d.Z;
            spread = pm.Spread;
            speedMin = pm.InitialVelocityMin;
            speedMax = pm.InitialVelocityMax;
            if (pm.EmissionShape == ParticleProcessMaterial.EmissionShapeEnum.Sphere)
                emitRadius = pm.EmissionSphereRadius;
        }

        return $"{ind}\t<ParticleEmitterComponent amount=\"{p.Amount}\" lifetime=\"{p.Lifetime:F3}\" emissionRadius=\"{emitRadius:F4}\" speedMin=\"{speedMin:F3}\" speedMax=\"{speedMax:F3}\" dirX=\"{dirX:F4}\" dirY=\"{dirY:F4}\" dirZ=\"{dirZ:F4}\" spreadAngle=\"{spread:F2}\" emitting=\"{p.Emitting}\" oneShot=\"{p.OneShot}\" />\n";
    }

    public static string BuildFogVolumeXml(Node3D node, string ind)
    {
        if (node.GetClass() != "FogVolume") return "";

        var cr = 1.0; var cg = 1.0; var cb = 1.0; var dens = 0.1;
        var mat = node.Get("material");
        if (mat.AsGodotObject() is FogMaterial fm)
        {
            cr = fm.Albedo.R; cg = fm.Albedo.G; cb = fm.Albedo.B;
            dens = fm.Density;
        }

        var fh = 10.0;
        if (node.HasMethod("get_size"))
            fh = ((Vector3)node.Get("size")).Y;

        return $"{ind}\t<FogComponent colorR=\"{cr:F3}\" colorG=\"{cg:F3}\" colorB=\"{cb:F3}\" density=\"{dens:F4}\" fogHeight=\"{fh:F3}\" heightFalloff=\"1.000\" />\n";
    }

    public static string BuildLabel3DXml(Node3D node, string ind)
    {
        if (node.GetClass() != "Label3D") return "";
        var lc = ((Label3D)node).Modulate;
        var text = ((Label3D)node).Text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        var fontSize = ((Label3D)node).FontSize;
        var pixelSize = ((Label3D)node).PixelSize;
        var billboard = BillboardName((int)((Label3D)node).Billboard);
        return $"{ind}\t<UILabelComponent text=\"{text}\" fontSize=\"{fontSize:F1}\" pixelSize=\"{pixelSize:F5}\" colorR=\"{lc.R:F3}\" colorG=\"{lc.G:F3}\" colorB=\"{lc.B:F3}\" colorA=\"{lc.A:F3}\" billboard=\"{billboard}\" offsetY=\"0.000\" />\n";
    }

    public static string BuildAudioXml(AudioStreamPlayer3D au, string ind)
    {
        var rpath = au.Stream?.ResourcePath ?? "";
        var linVol = Math.Pow(10.0, au.VolumeDb / 20.0);
        return $"{ind}\t<AudioSourceComponent AudioClipPath=\"{rpath}\" Volume=\"{linVol:F2}\" Pitch=\"{au.PitchScale:F3}\" Loop=\"true\" Autoplay=\"{au.Autoplay}\" MaxDistance=\"{au.MaxDistance:F2}\" />\n";
    }

    public static string HarvestCollider(Node3D node, string ind)
    {
        var bodyClasses = new HashSet<string> { "StaticBody3D", "RigidBody3D", "AnimatableBody3D", "CharacterBody3D", "Area3D" };

        foreach (Node child in node.GetChildren())
        {
            if (bodyClasses.Contains(child.GetClass()))
            {
                var result = ColliderXmlFromBody(child, ind);
                if (result != "") return result;
            }
        }

        var parent = node.GetParent();
        if (parent != null && bodyClasses.Contains(parent.GetClass()))
            return ColliderXmlFromBody(parent, ind);

        return "";
    }

    private static string ColliderXmlFromBody(Node body, string ind)
    {
        var isTrigger = body.GetClass() == "Area3D";
        foreach (Node child in body.GetChildren())
        {
            if (child is not CollisionShape3D cs) continue;
            if (cs.Shape == null) continue;

            var shapeCls = cs.Shape.GetClass();
            var w = 1.0; var h = 1.0; var d = 1.0;
            var shapeName = "Box";

            if (shapeCls == "BoxShape3D")
            {
                var sz = ((BoxShape3D)cs.Shape).Size;
                w = sz.X; h = sz.Y; d = sz.Z;
            }
            else if (shapeCls == "SphereShape3D")
            {
                w = ((SphereShape3D)cs.Shape).Radius * 2.0; h = w; d = w;
                shapeName = "Sphere";
            }
            else if (shapeCls == "CapsuleShape3D")
            {
                w = ((CapsuleShape3D)cs.Shape).Radius * 2.0; h = ((CapsuleShape3D)cs.Shape).Height; d = w;
                shapeName = "Capsule";
            }
            else if (shapeCls == "CylinderShape3D")
            {
                w = ((CylinderShape3D)cs.Shape).Radius * 2.0; h = ((CylinderShape3D)cs.Shape).Height; d = w;
                shapeName = "Cylinder";
            }

            return $"{ind}\t<ColliderComponent Shape=\"{shapeName}\" Width=\"{w:F3}\" Height=\"{h:F3}\" Depth=\"{d:F3}\" isTrigger=\"{isTrigger}\" />\n";
        }
        return "";
    }

    public static string BuildPhysicsBodyXml(Node3D node, string ind)
    {
        var parent = node.GetParent();
        if (parent == null) return "";
        var pcls = parent.GetClass();
        if (pcls == "RigidBody3D")
        {
            var mass = (float)parent.Get("mass");
            var gravityScale = (float)parent.Get("gravity_scale");
            var frozen = (bool)parent.Get("freeze");
            return $"{ind}\t<RigidBodyComponent mass=\"{mass:F3}\" gravityScale=\"{gravityScale:F3}\" isKinematic=\"{frozen}\" />\n";
        }
        if (pcls == "StaticBody3D")
            return $"{ind}\t<PhysicsBodyComponent IsKinematic=\"true\" />\n";
        return "";
    }

    public static string BuildStaticBodySelfXml(StaticBody3D body, string ind)
    {
        var out_ = $"{ind}\t<PhysicsBodyComponent IsKinematic=\"true\" />\n";
        foreach (Node child in body.GetChildren())
        {
            if (child is CollisionShape3D cs && cs.Shape != null)
                out_ += ColliderShapeXml(cs.Shape, ind);
        }
        return out_;
    }

    private static string ColliderShapeXml(Shape3D shape, string ind)
    {
        var cls = shape.GetClass();
        var w = 1.0; var h = 1.0; var d = 1.0;
        var name = "Box";

        if (cls == "BoxShape3D")
        {
            var sz = ((BoxShape3D)shape).Size;
            w = sz.X; h = sz.Y; d = sz.Z;
        }
        else if (cls == "SphereShape3D")
        {
            w = ((SphereShape3D)shape).Radius * 2.0; h = w; d = w;
            name = "Sphere";
        }
        else if (cls == "CapsuleShape3D")
        {
            w = ((CapsuleShape3D)shape).Radius * 2.0; h = ((CapsuleShape3D)shape).Height; d = w;
            name = "Capsule";
        }
        else if (cls == "CylinderShape3D")
        {
            w = ((CylinderShape3D)shape).Radius * 2.0; h = ((CylinderShape3D)shape).Height; d = w;
            name = "Cylinder";
        }

        return $"{ind}\t<ColliderComponent Shape=\"{name}\" Width=\"{w:F3}\" Height=\"{h:F3}\" Depth=\"{d:F3}\" />\n";
    }

	private static string BuildCustomMeshXml(Mesh mesh, string ind, string meshName)
	{
		var allVerts = new List<Vector3>();
		var allTris = new List<Vector3I>();
		var vertOffset = 0;

		for (int surfIdx = 0; surfIdx < mesh.GetSurfaceCount(); surfIdx++)
		{
			var arrays = mesh.SurfaceGetArrays(surfIdx);
			var vertData = arrays[(int)Mesh.ArrayType.Vertex];
			if (vertData.VariantType != Variant.Type.PackedVector3Array)
				continue;

			var verts = vertData.AsVector3Array();
			foreach (var v in verts)
				allVerts.Add(v);

			var idxData = arrays[(int)Mesh.ArrayType.Index];
			var indices = idxData.VariantType == Variant.Type.PackedInt32Array
				? idxData.AsInt32Array()
				: null;

			if (indices == null || indices.Length == 0)
			{
				indices = new int[verts.Length];
				for (int i = 0; i < verts.Length; i++)
					indices[i] = i;
			}

			for (int i = 0; i + 2 < indices.Length; i += 3)
				allTris.Add(new Vector3I(
					indices[i] + vertOffset,
					indices[i + 1] + vertOffset,
					indices[i + 2] + vertOffset
				));

			vertOffset += verts.Length;
		}

		if (allVerts.Count == 0)
			return "";

		var out_ = $"{ind}\t<Component type=\"MeshComponent\" name=\"{meshName}\" Shape=\"Custom\">\n";
		foreach (var v in allVerts)
			out_ += $"{ind}\t\t<vert x=\"{v.X:F6}\" y=\"{v.Y:F6}\" z=\"{v.Z:F6}\" />\n";
		foreach (var t in allTris)
			out_ += $"{ind}\t\t<tri a=\"{t.X}\" b=\"{t.Y}\" c=\"{t.Z}\" />\n";
		out_ += $"{ind}\t</Component>\n";
		out_ += $"{ind}\t<Component type=\"MeshRenderer\" Mesh=\"{meshName}\" />\n";

		return out_;
	}

	public static string MeshComponents(string ind, string meshName, string shape, double w, double h, double d)
    {
        return $"{ind}\t<Component type=\"MeshComponent\" name=\"{meshName}\" Shape=\"{shape}\" Width=\"{w:F3}\" Height=\"{h:F3}\" Depth=\"{d:F3}\" />\n{ind}\t<Component type=\"MeshRenderer\" Mesh=\"{meshName}\" />\n";
    }

	private static string MeshInstanceComponents(MeshInstance3D node, string ind, string worldName = null)
    {
        var mesh = node.Mesh;
        if (mesh == null) return "";

        var mcls = mesh.GetClass();
        var shapeName = "";
        var w = 1.0; var h = 1.0; var d = 1.0;

        if (mcls == "BoxMesh")
        {
            var sz = ((BoxMesh)mesh).Size;
            shapeName = "Box"; w = sz.X; h = sz.Y; d = sz.Z;
        }
        else if (mcls == "SphereMesh")
        {
            var sphereMesh = (SphereMesh)mesh;
            shapeName = "Sphere"; w = sphereMesh.Radius * 2.0; h = sphereMesh.Height; d = sphereMesh.Radius * 2.0;
        }
        else if (mcls == "CapsuleMesh")
        {
            var cm = (CapsuleMesh)mesh;
            shapeName = "Capsule"; w = cm.Radius * 2.0; h = cm.Height; d = cm.Radius * 2.0;
        }
        else if (mcls == "CylinderMesh")
        {
            var cym = (CylinderMesh)mesh;
            shapeName = "Cylinder"; w = cym.TopRadius * 2.0; h = cym.Height; d = cym.TopRadius * 2.0;
        }
        else if (mcls == "PlaneMesh")
        {
            var sz2 = ((PlaneMesh)mesh).Size;
            shapeName = "Plane"; w = sz2.X; h = 0.0; d = sz2.Y;
        }

        var out_ = "";
        if (shapeName != "")
            out_ += MeshComponents(ind, "mesh_data", shapeName, w, h, d);
        else
            out_ += BuildCustomMeshXml(mesh, ind, "mesh_data");

        var mat = node.GetSurfaceOverrideMaterial(0);
        if (mat == null && mesh.GetSurfaceCount() > 0)
            mat = mesh.SurfaceGetMaterial(0);

        if (mat is StandardMaterial3D sm)
            out_ += BuildMaterialXml(sm, ind, mat.ResourcePath, worldName);

        return out_;
    }

    public static string BuildMaterialXml(StandardMaterial3D sm, string ind, string fallbackPath, string worldName = null)
    {
        var c = sm.AlbedoColor;
        var out_ = $"{ind}\t<MaterialComponent R=\"{c.R:F3}\" G=\"{c.G:F3}\" B=\"{c.B:F3}\" A=\"{c.A:F3}\" Metallic=\"{sm.Metallic:F3}\" Roughness=\"{sm.Roughness:F3}\"";

        var albedoTexPath = TextureRelPath(sm.AlbedoTexture, worldName);
        if (albedoTexPath != null)
            out_ += $" AlbedoTexture=\"{albedoTexPath}\"";

        var normalTexPath = TextureRelPath(sm.NormalTexture, worldName);
        if (normalTexPath != null)
            out_ += $" NormalTexture=\"{normalTexPath}\"";

        var metalTexPath = TextureRelPath(sm.MetallicTexture, worldName);
        if (metalTexPath != null)
            out_ += $" MetallicTexture=\"{metalTexPath}\"";

        var roughTexPath = TextureRelPath(sm.RoughnessTexture, worldName);
        if (roughTexPath != null)
            out_ += $" RoughnessTexture=\"{roughTexPath}\"";

        var emissionTexPath = TextureRelPath(sm.EmissionTexture, worldName);
        if (emissionTexPath != null)
            out_ += $" EmissionTexture=\"{emissionTexPath}\"";

        // UV1 offset & scale
        var uvOffset = sm.Uv1Offset;
        var uvScale = sm.Uv1Scale;
        if (uvOffset.X != 0f || uvOffset.Y != 0f)
            out_ += $" Uv1OffsetX=\"{uvOffset.X:F3}\" Uv1OffsetY=\"{uvOffset.Y:F3}\"";
        if (uvScale.X != 1f || uvScale.Y != 1f)
            out_ += $" Uv1ScaleX=\"{uvScale.X:F3}\" Uv1ScaleY=\"{uvScale.Y:F3}\"";

        var ec = sm.Emission;
        if (ec.R > 0f || ec.G > 0f || ec.B > 0f)
            out_ += $" EmissionR=\"{ec.R:F3}\" EmissionG=\"{ec.G:F3}\" EmissionB=\"{ec.B:F3}\"";

        out_ += " />\n";
        return out_;
    }

    private static string TextureRelPath(Texture2D tex, string worldName = null)
    {
        if (tex == null) return null;
        var texPath = tex.ResourcePath;
        if (string.IsNullOrEmpty(texPath)) return null;
        if (!texPath.StartsWith("res://")) return null;
        var rel = texPath.Substring("res://".Length);
        if (!string.IsNullOrEmpty(worldName))
            return $"v12://{worldName}/{rel}";
        return rel;
    }

	private static string BakeCsgNode(Node3D node, string ind, string worldName = null)
	{
		var meshesVar = node.Call("get_meshes");
		if (meshesVar.VariantType != Variant.Type.Array)
			return "";

		var meshesArray = meshesVar.AsGodotArray();
		if (meshesArray.Count == 0)
			return "";

		var allVerts = new List<Vector3>();
		var allTris = new List<Vector3I>();
		var vertOffset = 0;

		foreach (var entry in meshesArray)
		{
			if (entry.VariantType != Variant.Type.Array)
				continue;

			var entryArr = entry.AsGodotArray();
			if (entryArr.Count < 2)
				continue;

			var xform = entryArr[0].AsTransform3D();
			var mesh = entryArr[1].AsGodotObject() as Mesh;
			if (mesh == null || mesh.GetSurfaceCount() == 0)
				continue;

			for (int surfIdx = 0; surfIdx < mesh.GetSurfaceCount(); surfIdx++)
			{
				var arrays = mesh.SurfaceGetArrays(surfIdx);
				var vertData = arrays[(int)Mesh.ArrayType.Vertex];
				if (vertData.VariantType != Variant.Type.PackedVector3Array)
					continue;

				var verts = vertData.AsVector3Array();
				foreach (var v in verts)
					allVerts.Add(xform * v);

				var idxData = arrays[(int)Mesh.ArrayType.Index];
				var indices = idxData.VariantType == Variant.Type.PackedInt32Array
					? idxData.AsInt32Array()
					: null;

				if (indices == null || indices.Length == 0)
				{
					indices = new int[verts.Length];
					for (int i = 0; i < verts.Length; i++)
						indices[i] = i;
				}

				for (int i = 0; i + 2 < indices.Length; i += 3)
					allTris.Add(new Vector3I(
						indices[i] + vertOffset,
						indices[i + 1] + vertOffset,
						indices[i + 2] + vertOffset
					));

				vertOffset += verts.Length;
			}
		}

		if (allVerts.Count == 0)
			return "";

		var out_ = "";
		out_ += $"{ind}\t<Component type=\"MeshComponent\" name=\"csg_mesh\" Shape=\"Custom\">\n";

		foreach (var v in allVerts)
			out_ += $"{ind}\t\t<vert x=\"{v.X:F6}\" y=\"{v.Y:F6}\" z=\"{v.Z:F6}\" />\n";

		foreach (var t in allTris)
			out_ += $"{ind}\t\t<tri a=\"{t.X}\" b=\"{t.Y}\" c=\"{t.Z}\" />\n";

		out_ += $"{ind}\t</Component>\n";
		out_ += $"{ind}\t<Component type=\"MeshRenderer\" Mesh=\"csg_mesh\" />\n";

		// Try material from child meshes first, then fall back to node override
		var collectedMat = false;
		foreach (var entry in meshesArray)
		{
			if (collectedMat) break;
			if (entry.VariantType != Variant.Type.Array) continue;

			var entryArr = entry.AsGodotArray();
			if (entryArr.Count < 2) continue;

			var mesh = entryArr[1].AsGodotObject() as Mesh;
			if (mesh == null) continue;

			for (int surfIdx = 0; surfIdx < mesh.GetSurfaceCount(); surfIdx++)
			{
				var surfMat = mesh.SurfaceGetMaterial(surfIdx);
				if (surfMat is StandardMaterial3D sm)
				{
					out_ += BuildMaterialXml(sm, ind, sm.ResourcePath, worldName);
					collectedMat = true;
					break;
				}
			}
		}

		if (!collectedMat)
		{
			var matVar = node.Get("material");
			if (matVar.VariantType != Variant.Type.Nil && matVar.Obj is StandardMaterial3D smOverride)
				out_ += BuildMaterialXml(smOverride, ind, smOverride.ResourcePath, worldName);
		}

		// Handle collision from the baked CSG node
		if (node.HasMethod("get_use_collision") && (bool)node.Get("use_collision"))
		{
			var isTrig = false;
			if (node.HasMethod("get_collision_layer"))
			{
				var layer = (uint)node.Get("collision_layer");
				var mask = (uint)node.Get("collision_mask");
				isTrig = layer == 0 && mask == 0;
			}
			Aabb combinedAabb = new Aabb(allVerts[0], Vector3.Zero);
			foreach (var v in allVerts)
				combinedAabb = combinedAabb.Expand(v);
			out_ += $"{ind}\t<ColliderComponent Shape=\"Box\" Width=\"{combinedAabb.Size.X:F3}\" Height=\"{combinedAabb.Size.Y:F3}\" Depth=\"{combinedAabb.Size.Z:F3}\" isTrigger=\"{isTrig}\" />\n";
		}

		return out_;
	}

	private static string CsgCollider(Node3D node, string ind, string shape, double w, double h, double d)
    {
        var useCol = false;
        if (node.HasMethod("get_use_collision"))
            useCol = (bool)node.Get("use_collision");

        if (!useCol) return "";

        var isTrig = false;
        if (node.HasMethod("get_collision_layer"))
        {
            var layer = (uint)node.Get("collision_layer");
            var mask = (uint)node.Get("collision_mask");
            isTrig = layer == 0 && mask == 0;
        }

        return $"{ind}\t<ColliderComponent Shape=\"{shape}\" Width=\"{w:F3}\" Height=\"{h:F3}\" Depth=\"{d:F3}\" isTrigger=\"{isTrig}\" />\n";
    }

    private static string BillboardName(int mode)
    {
        return mode switch
        {
            1 => "Enabled",
            2 => "YAxis",
            _ => "Disabled"
        };
    }
}
