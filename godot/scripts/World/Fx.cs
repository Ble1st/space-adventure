using Godot;

namespace SpaceAdventure.World;

/// <summary>Einmalige Partikeleffekte (Treffer, Explosion). Dauerhafte Effekte gehören zu ihren Objekten (z.B. Triebwerk am Schiff).</summary>
public partial class Fx : Node3D
{
	// Runde 17 (Speicher am Gerät): Kugel und Material je Farbe geteilt statt je Ausbruch neu (Gefechte erzeugen Hunderte Ausbrüche)
	static SphereMesh? _mesh;
	static readonly System.Collections.Generic.Dictionary<Color, StandardMaterial3D> _mats = new();
	static StandardMaterial3D MatOf(Color c) { if (!_mats.TryGetValue(c, out var m)) { m = Gfx.Mat(c, 4f); if (_mats.Count < 64) _mats[c] = m; } return m; }
	/// <summary>Selbsttest: Zahl der geteilten Effektmaterialien.</summary>
	public static int MaterialCount => _mats.Count;

	public void Burst(Vector3 pos, Color color, int amount, float speed, float lifetime = 0.8f)
	{
		var p = new CpuParticles3D
		{
			Position = pos, Amount = amount, Lifetime = lifetime, OneShot = true, Explosiveness = 1f, Emitting = true,
			Direction = new Vector3(0, 1, 0), Spread = 180f, InitialVelocityMin = speed * 0.4f, InitialVelocityMax = speed,
			Gravity = Vector3.Zero, ScaleAmountMin = 0.4f, ScaleAmountMax = 1.2f,
			Mesh = _mesh ??= new SphereMesh { Radius = 0.3f, Height = 0.6f, RadialSegments = 6, Rings = 3 },
			MaterialOverride = MatOf(color),
		};
		AddChild(p);
		var timer = GetTree().CreateTimer(lifetime + 0.3f);
		timer.Timeout += p.QueueFree;
	}
}
