using System;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure.World;

/// <summary>
/// Spielerschiff: Darstellung, Flugmodell und Kollision. Bekommt pro Frame nur Steuerwunsch (Richtung + Schub)
/// und weiß nichts über Eingabegeräte, Autopilot oder Menüs.
/// </summary>
public partial class PlayerShip : Node3D
{
	const float Thrust = 40f, MaxSpeed = 45f, TurnRate = 7f;

	public Vector3 Velocity { get; set; }
	public bool Locked { get; set; }
	public float Heading { get; set; }
	public bool Thrusting { get; private set; }

	/// <summary>Aufprallstärke und Ort; nur bei hartem Aufprall.</summary>
	public event Action<float, Vector3>? Hit;
	float _hitCooldown;
	MeshInstance3D _flame = null!;
	CpuParticles3D _trail = null!;

	public override void _Ready()
	{
		var hull = Gfx.Mesh(new PrismMesh { Size = new Vector3(3f, 5f, 1.2f) }, Gfx.Mat(new Color(0.7f, 0.75f, 0.85f), 0f, 0.8f), Vector3.Zero, this);
		hull.RotationDegrees = new Vector3(-90, 0, 0);
		Gfx.Mesh(new BoxMesh { Size = new Vector3(5.5f, 0.2f, 1.4f) }, Gfx.Mat(new Color(0.45f, 0.5f, 0.65f), 0f, 0.8f), new Vector3(0, 0, 1f), this);
		Gfx.Mesh(new SphereMesh { Radius = 0.5f, Height = 1f }, Gfx.Mat(new Color(0.3f, 0.8f, 1f), 1.5f), new Vector3(0, 0.5f, -0.6f), this);
		_flame = Gfx.Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.6f, Height = 2.5f }, Gfx.Mat(new Color(1f, 0.55f, 0.15f), 4f), new Vector3(0, 0, 3.6f), this);
		_flame.RotationDegrees = new Vector3(90, 0, 0);

		// Triebwerksspur bleibt in der Welt zurück (LocalCoords = false)
		_trail = new CpuParticles3D
		{
			Emitting = false, Amount = 40, Lifetime = 0.7f, LocalCoords = false, Position = new Vector3(0, 0, 4.2f),
			Direction = new Vector3(0, 0, 1), Spread = 12f, InitialVelocityMin = 6f, InitialVelocityMax = 12f,
			Gravity = Vector3.Zero, ScaleAmountMin = 0.5f, ScaleAmountMax = 1.0f,
			Mesh = new SphereMesh { Radius = 0.35f, Height = 0.7f, RadialSegments = 6, Rings = 3 },
			MaterialOverride = Gfx.Mat(new Color(1f, 0.6f, 0.2f), 3f),
		};
		AddChild(_trail);
	}

	/// <param name="dir">Gewünschte Flugrichtung in der Spielebene (x → Welt-X, y → Welt-Z), beliebige Länge.</param>
	/// <param name="throttle">0..1</param>
	/// <param name="speedScale">Begrenzt die Höchstgeschwindigkeit (z.B. beim Anflug).</param>
	public void Step(float delta, Vector2 dir, float throttle, float speedScale, SectorView view)
	{
		_hitCooldown = Mathf.Max(0f, _hitCooldown - delta);
		if (Locked) { Velocity = Velocity.Lerp(Vector3.Zero, 1f - Mathf.Exp(-6f * delta)); Thrusting = false; Show(false); return; }

		float turn = 0f;
		bool thrust = throttle > 0.05f && dir.LengthSquared() > 1e-4f;
		if (thrust)
		{
			float want = Mathf.Atan2(-dir.X, -dir.Y);
			float diff = Mathf.AngleDifference(Heading, want);
			float before = Heading;
			Heading = Mathf.LerpAngle(Heading, want, 1f - Mathf.Exp(-TurnRate * delta));
			turn = Mathf.AngleDifference(before, Heading) / Mathf.Max(delta, 0.001f);
			float align = Mathf.Max(0f, Mathf.Cos(diff));   // nicht quer zur Flugrichtung beschleunigen
			Velocity += Vector3.Forward.Rotated(Vector3.Up, Heading) * Thrust * throttle * align * delta;
		}
		Thrusting = thrust;
		Velocity = Velocity.LimitLength(MaxSpeed * speedScale);
		Velocity *= 1f - 0.4f * delta;
		Position += Velocity * delta;
		ResolveCollisions(view);

		Rotation = new Vector3(0, Heading, Mathf.Lerp(Rotation.Z, Mathf.Clamp(turn * 0.12f, -0.5f, 0.5f), 1f - Mathf.Exp(-8f * delta)));
		Show(thrust);
		_flame.Scale = new Vector3(1, 1, 0.8f + GD.Randf() * 0.5f);
	}

	void Show(bool thrust) { _flame.Visible = thrust; _trail.Emitting = thrust; }

	void ResolveCollisions(SectorView view)
	{
		var all = new System.Collections.Generic.List<Obstacle>(view.Obstacles);
		all.AddRange(view.ShipBodies());
		var pos = new Vec2(Position.X, Position.Z);
		if (!Collision.Resolve(ref pos, Collision.ShipRadius, all, out var n)) return;

		Position = new Vector3(pos.X, 0, pos.Y);
		var normal = new Vector3(n.X, 0, n.Y);
		float vn = Velocity.Dot(normal);
		if (vn >= 0f) return;
		Velocity -= normal * (1.4f * vn);   // Rückprall mit 40 % Restenergie
		float impact = -vn;
		if (impact > 4f && _hitCooldown <= 0f) { _hitCooldown = 0.4f; Hit?.Invoke(impact, Position); }
	}
}
