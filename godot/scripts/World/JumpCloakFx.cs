using Godot;

namespace SpaceAdventure.World;

/// <summary>
/// Sprungantrieb und Tarnfeld — nur Darstellung (Werte aus dem Core): Ladeleuchten um das Schiff (Kugel und Ring, wachsen und leuchten mit dem
/// Ladefortschritt) und die Durchsichtigkeit getarnter Schiffe (Spielermodell und NPC-Marker).
/// </summary>
public sealed class JumpChargeFx
{
	readonly Node3D _root = new() { Name = "JumpCharge", Visible = false };
	readonly MeshInstance3D _sphere, _ring;
	readonly StandardMaterial3D _sphereMat, _ringMat;

	/// <summary>Selbsttest: sichtbar und Fortschritt der Anzeige.</summary>
	public bool Visible => _root.Visible;
	public float Shown { get; private set; }

	public JumpChargeFx(Node3D parent)
	{
		parent.AddChild(_root);
		_sphereMat = Mat(new Color(0.4f, 0.85f, 1f, 0.25f));
		_ringMat = Mat(new Color(0.6f, 0.95f, 1f, 0.8f));
		_sphere = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 }, MaterialOverride = _sphereMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		_ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 1f, OuterRadius = 1.08f, Rings = 40, RingSegments = 6 }, MaterialOverride = _ringMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		_root.AddChild(_sphere);
		_root.AddChild(_ring);
	}

	static StandardMaterial3D Mat(Color c) => new()
	{
		AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		BlendMode = BaseMaterial3D.BlendModeEnum.Add, EmissionEnabled = true, Emission = new Color(c.R, c.G, c.B), EmissionEnergyMultiplier = 2f,
	};

	/// <summary>Fortschritt 0..1 (negativ = aus); <paramref name="size"/> = halbe Schiffslänge; <paramref name="time"/> für das Pulsieren.</summary>
	public void Update(float progress, float size, float time)
	{
		_root.Visible = progress >= 0f;
		Shown = progress;
		if (progress < 0f) return;
		float pulse = 1f + 0.06f * Mathf.Sin(time * (6f + 14f * progress));
		float r = size * (1.4f + 1.2f * progress) * pulse;
		_sphere.Scale = Vector3.One * r;
		_sphereMat.AlbedoColor = new Color(0.4f, 0.85f, 1f, 0.08f + 0.3f * progress);
		float rr = size * (2.6f - 1.1f * progress);
		_ring.Scale = new Vector3(rr, 1f, rr);
		_ring.RotationDegrees = new Vector3(0f, time * 90f, 0f);
		_ringMat.AlbedoColor = new Color(0.6f, 0.95f, 1f, 0.3f + 0.6f * progress);
	}
}

/// <summary>Tarnfeld: Durchsichtigkeit eines Schiffsknotens (alle Geometrien darunter). Nur bei Änderung setzen.</summary>
public static class CloakLook
{
	/// <summary>Durchsichtigkeit getarnter Schiffe (0 = deckend, 1 = unsichtbar).</summary>
	public const float Transparency = 0.72f;

	public static void Apply(Node root, bool cloaked)
	{
		float t = cloaked ? Transparency : 0f;
		void Walk(Node n)
		{
			if (n is GeometryInstance3D g && n is not GpuParticles3D && n is not CpuParticles3D) g.Transparency = t;
			foreach (var c in n.GetChildren()) Walk(c);
		}
		Walk(root);
	}

	/// <summary>Selbsttest: größte Durchsichtigkeit unter einem Knoten.</summary>
	public static float Max(Node root)
	{
		float m = 0f;
		void Walk(Node n)
		{
			if (n is GeometryInstance3D g) m = Mathf.Max(m, g.Transparency);
			foreach (var c in n.GetChildren()) Walk(c);
		}
		Walk(root);
		return m;
	}
}
