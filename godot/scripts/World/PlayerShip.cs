using System;
using System.Linq;
using Godot;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure.World;

/// <summary>
/// Spielerschiff: Darstellung, Flugmodell und Kollision. Bekommt pro Frame nur Steuerwunsch (Drehung, Richtung,
/// Solltempo) und weiß nichts über Eingabegeräte, Autopilot oder Menüs.
/// </summary>
public partial class PlayerShip : Node3D
{
	const float Thrust = 40f, BrakeAccel = 60f;
	/// <summary>Höchstgeschwindigkeit in Einheiten/s (Volle Pulle am Tempo-Slider).</summary>
	public const float MaxSpeed = Collision.PlayerMaxSpeed;
	const float TurnRate = 7f;
	/// <summary>Drehgeschwindigkeit der Wendetasten (Halten) in rad/s.</summary>
	const float TurnSpeed = SpaceSim.Core.Flight.AimAssist.PlayerTurnRate;   // Gerätetest Phase 28: dieselbe Drehrate wie die Zielhilfe
	/// <summary>Erst ab dieser Aufprallgeschwindigkeit an Asteroiden/Stationen gibt es Schaden; darunter nur Abprallen.</summary>
	public const float HardImpact = Collision.HardImpact;
	/// <summary>Seitliche Restgeschwindigkeit wird abgebaut (Flugassistent): das Schiff folgt seiner Nase und driftet nicht in Hindernisse.</summary>
	const float LateralDamping = 3.2f;

	public Vector3 Velocity { get; set; }
	public bool Locked { get; set; }
	public float Heading { get; set; }
	public bool Thrusting { get; private set; }

	/// <summary>Restzeit, in der Treffer keinen Schaden machen (nach Abdocken, Neustart, Sprung).</summary>
	public float Grace { get; set; }
	public void GiveGrace(float seconds) => Grace = Mathf.Max(Grace, seconds);

	/// <summary>Nur bei hartem Aufprall auf ein festes Hindernis (Aufprallgeschwindigkeit, Ort); nie bei NPC-Schiffen.</summary>
	public event Action<float, Vector3>? Hit;
	float _hitCooldown;
	MeshInstance3D _flame = null!;
	CpuParticles3D _trail = null!;

	/// <summary>Runde 11: Schiffsmodell des aktiven Schiffs (ShipModels); null = alter Grundkörper.</summary>
	public ShipVisual? Visual { get; private set; }
	string _modelKey = "\u0000";
	readonly System.Collections.Generic.List<Node3D> _simple = new();

	/// <summary>Runde 11: Modell wechseln (Schiffswechsel, Umbau); ohne Modell bleibt der Grundkörper.</summary>
	public void SetModel(string modelKey, Color accent)
	{
		if (modelKey == _modelKey) { Visual?.SetAccent(accent); return; }
		_modelKey = modelKey;
		Visual?.Root.QueueFree();
		Visual = ShipModels.Create(modelKey, accent);
		if (Visual is not null) AddChild(Visual.Root);
		foreach (var n in _simple) n.Visible = Visual is null;
		float half = Visual is not null ? ShipModels.ScaleOf(modelKey) : 2.5f;
		if (_trail is not null) _trail.Position = new Vector3(0, 0, half + 0.6f);
	}

	/// <summary>Runde 11: Schub (Tempoanteil) und Werkzeugdrehung je Bild.</summary>
	public void Animate(float spin, float delta)
	{
		if (Visual is null) return;
		Visual.SetEngine(Thrusting ? 1f : Mathf.Clamp(Velocity.Length() / MaxSpeed, 0.1f, 0.6f));
		Visual.Spin(spin, delta);
	}

	public override void _Ready()
	{
		var hull = Gfx.Mesh(new PrismMesh { Size = new Vector3(3f, 5f, 1.2f) }, Gfx.Mat(new Color(0.7f, 0.75f, 0.85f), 0f, 0.8f), Vector3.Zero, this);
		hull.RotationDegrees = new Vector3(-90, 0, 0);
		_simple.Add(hull);
		_simple.Add(Gfx.Mesh(new BoxMesh { Size = new Vector3(5.5f, 0.2f, 1.4f) }, Gfx.Mat(new Color(0.45f, 0.5f, 0.65f), 0f, 0.8f), new Vector3(0, 0, 1f), this));
		_simple.Add(Gfx.Mesh(new SphereMesh { Radius = 0.5f, Height = 1f }, Gfx.Mat(new Color(0.3f, 0.8f, 1f), 1.5f), new Vector3(0, 0.5f, -0.6f), this));
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

	/// <param name="turn">Dreheingang -1..1 (Wendetasten/Tasten); das Schiff dreht nur, solange gehalten wird.</param>
	/// <param name="dir">Gewünschte Flugrichtung in der Spielebene (x → Welt-X, y → Welt-Z); bei manuellem Sliderflug die Schiffsnase.</param>
	/// <param name="throttle">Solltempo als Anteil der <see cref="MaxSpeed"/> (-1..1, negativ = rückwärts, über SpaceSim.Core.Flight.Throttle begrenzt).</param>
	/// <param name="speedScale">Begrenzt die Höchstgeschwindigkeit (z.B. beim Anflug).</param>
	/// <param name="brake">Haltemodus (z.B. Automatik arbeitet am Asteroiden): Restgeschwindigkeit stark abbauen.</param>
	/// <summary>
	/// Kursänderung für einen Dreheingang (+1 = rechts, -1 = links). Godot dreht positiv gegen den Uhrzeigersinn
	/// (von oben gesehen, Nase = -Z), also nach LINKS – „rechts“ muss deshalb den Kurs verkleinern.
	/// </summary>
	public static float HeadingChange(float turn, float delta) => -turn * TurnSpeed * delta;

	public void Step(float delta, float turn, Vector2 dir, float throttle, float speedScale, SectorView view, bool brake = false)
	{
		_hitCooldown = Mathf.Max(0f, _hitCooldown - delta);
		Grace = Mathf.Max(0f, Grace - delta);
		if (Locked) { Velocity = Velocity.Lerp(Vector3.Zero, 1f - Mathf.Exp(-6f * delta)); Thrusting = false; Show(false); return; }

		bool thrust = dir.LengthSquared() > 1e-4f && Mathf.Abs(throttle) > 0.001f;
		float turnVis = 0f;
		if (turn != 0f)
		{
			float dh = HeadingChange(turn, delta);
			Heading += dh;
			turnVis = dh / Mathf.Max(delta, 0.001f);
		}
		else if (thrust)
		{
			float want = Mathf.Atan2(-dir.X, -dir.Y);
			float before = Heading;
			Heading = Mathf.LerpAngle(Heading, want, 1f - Mathf.Exp(-TurnRate * delta));
			turnVis = Mathf.AngleDifference(before, Heading) / Mathf.Max(delta, 0.001f);
		}
		var forward = Vector3.Forward.Rotated(Vector3.Up, Heading);
		if (thrust)
		{
			float align = Mathf.Max(0f, Mathf.Cos(Mathf.AngleDifference(Heading, Mathf.Atan2(-dir.X, -dir.Y))));   // nicht quer zur Flugrichtung beschleunigen
			if (align > 0.01f)
			{
				// Solltempo entlang der Nase regeln (Slider/Autopilot geben den Anteil vor, negativ = rückwärts)
				float target = throttle * MaxSpeed * speedScale;
				float f = Velocity.Dot(forward);
				float accel = Mathf.Clamp((target - f) * 5f, -BrakeAccel, Thrust);
				Velocity += forward * accel * align * delta;
			}
		}
		Thrusting = thrust;
		// Flugassistent: Geschwindigkeit quer zur Blickrichtung abbauen
		var lateral = Velocity - forward * Velocity.Dot(forward);
		Velocity -= lateral * (1f - Mathf.Exp(-LateralDamping * delta));
		// Grenzen: vorwärts höchstens MaxSpeed, rückwärts höchstens 20 % (Throttle.ReverseShare)
		float fNow = Velocity.Dot(forward);
		var lat = Velocity - forward * fNow;
		fNow = Mathf.Clamp(fNow, -MaxSpeed * Throttle.ReverseShare * speedScale, MaxSpeed * speedScale);
		Velocity = forward * fNow + lat;
		Velocity = Velocity.LimitLength(MaxSpeed * speedScale);
		Velocity *= 1f - 0.4f * delta;
		if (!thrust) Velocity *= Mathf.Exp(-(brake ? 3.0f : 0.8f) * delta);   // kein Schub bremst sanft, im Haltemodus kräftig
		Position += Velocity * delta;
		ResolveCollisions(view);

		Rotation = new Vector3(0, Heading, Mathf.Lerp(Rotation.Z, Mathf.Clamp(turnVis * 0.12f, -0.5f, 0.5f), 1f - Mathf.Exp(-8f * delta)));
		Show(thrust);
		_flame.Scale = new Vector3(1, 1, 0.8f + GD.Randf() * 0.5f);
	}

	void Show(bool thrust) { _flame.Visible = thrust && Visual is null; _trail.Emitting = thrust; }

	/// <summary>
	/// Feste Hindernisse (Asteroiden, Stationen): Abprallen, bei hartem Aufprall Schaden (außer in der Schonfrist).
	/// NPC-Schiffe: nur sanft auseinanderschieben, nie Schaden – sie weichen ohnehin aus.
	/// </summary>
	void ResolveCollisions(SectorView view)
	{
		var pos = new Vec2(Position.X, Position.Z);

		if (Collision.Resolve(ref pos, Collision.ShipRadius, view.Obstacles, out var n))
		{
			Position = new Vector3(pos.X, 0, pos.Y);
			// Runde 11: Abprallregel aus dem Core (Collision.Bounce/IsHard, getestet): 40 % Restenergie, Schaden erst ab HardImpact
			var v2 = new Vec2(Velocity.X, Velocity.Z);
			float impact = Collision.Bounce(ref v2, n);
			Velocity = new Vector3(v2.X, Velocity.Y, v2.Y);
			if (Collision.IsHard(impact) && _hitCooldown <= 0f && Grace <= 0f) { _hitCooldown = 0.6f; Hit?.Invoke(impact, Position); }
		}

		pos = new Vec2(Position.X, Position.Z);
		if (Collision.Resolve(ref pos, Collision.ShipRadius, view.ShipBodies().ToList(), out var m))
		{
			Position = new Vector3(pos.X, 0, pos.Y);
			var normal = new Vector3(m.X, 0, m.Y);
			float vn = Velocity.Dot(normal);
			if (vn < 0f) Velocity -= normal * (0.6f * vn);   // weich abbremsen statt abprallen
		}
	}
}
