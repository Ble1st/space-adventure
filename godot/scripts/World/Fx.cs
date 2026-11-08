using Godot;

namespace SpaceAdventure.World;

/// <summary>Einmalige Partikeleffekte (Treffer, Explosion). Dauerhafte Effekte gehören zu ihren Objekten (z.B. Triebwerk am Schiff).</summary>
public partial class Fx : Node3D
{
	public void Burst(Vector3 pos, Color color, int amount, float speed, float lifetime = 0.8f)
	{
		var p = new CpuParticles3D
		{
			Position = pos, Amount = amount, Lifetime = lifetime, OneShot = true, Explosiveness = 1f, Emitting = true,
			Direction = new Vector3(0, 1, 0), Spread = 180f, InitialVelocityMin = speed * 0.4f, InitialVelocityMax = speed,
			Gravity = Vector3.Zero, ScaleAmountMin = 0.4f, ScaleAmountMax = 1.2f,
			Mesh = new SphereMesh { Radius = 0.3f, Height = 0.6f, RadialSegments = 6, Rings = 3 },
			MaterialOverride = Gfx.Mat(color, 4f),
		};
		AddChild(p);
		var timer = GetTree().CreateTimer(lifetime + 0.3f);
		timer.Timeout += p.QueueFree;
	}
}
