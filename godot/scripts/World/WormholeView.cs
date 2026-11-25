using Godot;

namespace SpaceAdventure.World;

/// <summary>
/// Wurmloch-Portal (ERKUNDUNG.md „Wurmlöcher“): flache, wirbelnde Scheibe auf der Spielebene (unbeleuchteter Shader mit Spiralarmen und dunklem Kern,
/// mobil billig: ein Quad, keine Texturen) plus ein leuchtender Ring und ein stehender Schimmer. Stabile Wurmlöcher violett, instabile rot-orange und
/// flackernd. Nur Darstellung — Ort und Ziel kommen aus dem Core.
/// </summary>
public static class WormholeView
{
	/// <summary>Sichtbarer Radius des Portals (Einflugradius des Cores ist kleiner; der Sog reicht sichtbar etwas weiter).</summary>
	public const float VisualRadius = 16f;

	const string Code = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec3 arm_color : source_color = vec3(0.65, 0.35, 1.0);
uniform vec3 core_color : source_color = vec3(0.02, 0.0, 0.06);
uniform float spin = 1.0;
uniform float unstable = 0.0;
varying vec2 p;
void vertex() { p = VERTEX.xy; }
void fragment() {
	float r = length(p);
	float a = atan(p.y, p.x);
	float t = TIME * spin;
	float swirl = a * 3.0 + log(max(r, 0.02)) * 6.0 - t * 2.5;
	float arms = 0.5 + 0.5 * sin(swirl);
	arms = pow(arms, 3.0);
	float flick = 1.0 - unstable * (0.35 + 0.35 * sin(TIME * 13.0 + r * 9.0));
	float ring = smoothstep(0.62, 0.9, r) * (1.0 - smoothstep(0.9, 1.0, r));
	float core = 1.0 - smoothstep(0.0, 0.38, r);
	float fade = 1.0 - smoothstep(0.85, 1.0, r);
	vec3 col = mix(arm_color * (0.4 + 1.4 * arms) * flick, core_color, core);
	col += arm_color * ring * 1.6;
	ALBEDO = col;
	ALPHA = clamp((0.35 + 0.65 * arms) * fade + core * 0.95 + ring, 0.0, 1.0) * fade;
}";

	static Shader? s_shader;

	/// <summary>Baut ein Portal unter <paramref name="parent"/> (Mittelpunkt = Ursprung des Elternknotens).</summary>
	public static Node3D Build(Node3D parent, bool unstable)
	{
		s_shader ??= new Shader { Code = Code };
		var root = new Node3D { Name = unstable ? "UnstableWormhole" : "Wormhole" };
		parent.AddChild(root);
		var arm = unstable ? new Color(1f, 0.45f, 0.2f) : new Color(0.65f, 0.35f, 1f);
		var mat = new ShaderMaterial { Shader = s_shader };
		mat.SetShaderParameter("arm_color", arm);
		mat.SetShaderParameter("spin", unstable ? 1.7f : 1f);
		mat.SetShaderParameter("unstable", unstable ? 1f : 0f);
		var disc = new MeshInstance3D { Name = "Disc", Mesh = new QuadMesh { Size = new Vector2(2f, 2f) }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		disc.Scale = new Vector3(VisualRadius, VisualRadius, VisualRadius);
		disc.RotationDegrees = new Vector3(-90f, 0f, 0f);   // flach auf der Ebene
		disc.Position = new Vector3(0f, 0.3f, 0f);
		root.AddChild(disc);
		// aufrechter Schimmer, damit das Portal auch in flachen Ansichten sichtbar ist
		var halo = new MeshInstance3D { Name = "Halo", Mesh = new QuadMesh { Size = new Vector2(2f, 2f) }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		halo.Scale = new Vector3(VisualRadius * 0.7f, VisualRadius * 0.7f, 1f);
		halo.Position = new Vector3(0f, VisualRadius * 0.55f, 0f);
		var bill = (ShaderMaterial)mat.Duplicate();
		halo.MaterialOverride = bill;
		root.AddChild(halo);
		var ringMat = Gfx.Mat(arm, 2.5f);
		var ring = Gfx.Mesh(new TorusMesh { InnerRadius = VisualRadius - 0.8f, OuterRadius = VisualRadius + 0.4f, Rings = 32, RingSegments = 8 }, ringMat, new Vector3(0f, 0.3f, 0f), root);
		ring.Name = "Rim";
		return root;
	}

	/// <summary>Dreht den aufrechten Schimmer zur Kamera (Billboard nur um die Hochachse).</summary>
	public static void FaceCamera(Node3D portal, Vector3 camPos)
	{
		if (portal.GetNodeOrNull<Node3D>("Halo") is not { } halo) return;
		var d = new Vector3(camPos.X - portal.GlobalPosition.X, 0f, camPos.Z - portal.GlobalPosition.Z);
		if (d.LengthSquared() < 0.01f) return;
		halo.Rotation = new Vector3(0f, Mathf.Atan2(d.X, d.Z), 0f);
	}
}
