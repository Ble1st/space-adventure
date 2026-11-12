using System.Collections.Generic;
using Godot;
using SpaceSim.Core.Model;

namespace SpaceAdventure.World;

/// <summary>
/// Phase 24: zeichnet die Schüsse der NPC-Gefechte im Spielersektor als kurze Linien vom Schützen zum Ziel (wie die Laserblitze der NPC-Miner). Enthält keine
/// Kampflogik: jeder Strahl stammt aus einem <c>ShotFired</c>-Ereignis der Simulation (Treffer am Ziel, Fehlschuss knapp daneben; Energie rot, kinetisch gelb).
/// </summary>
public partial class CombatView : Node3D
{
	const double FlashSeconds = 0.18;
	static readonly Color EnergyColor = new(1f, 0.25f, 0.2f);
	static readonly Color KineticColor = new(1f, 0.85f, 0.35f);

	sealed class Beam { public Node3D Pivot = null!; public ShipId Shooter, Target; public Vector3 Miss; public double Until; public bool Hit; }
	readonly List<Beam> _beams = new();
	readonly Material _energy = Gfx.Mat(EnergyColor, 3f), _kinetic = Gfx.Mat(KineticColor, 3f);

	/// <summary>Zahl der gezeichneten Schüsse seit dem letzten Zurücksetzen (Selbsttest).</summary>
	public int ShotsSeen { get; private set; }

	/// <summary>Ein Schuss der Simulation (im Spielersektor): Strahl für kurze Zeit.</summary>
	public void Shot(ShipId shooter, ShipId target, DamageType type, bool hit, double now, int seed)
	{
		ShotsSeen++;
		Beam? beam = null;
		foreach (var b in _beams) if (now >= b.Until) { beam = b; break; }
		if (beam is null) { beam = new Beam { Pivot = MakeBeam() }; _beams.Add(beam); }
		beam.Shooter = shooter; beam.Target = target; beam.Hit = hit; beam.Until = now + FlashSeconds;
		float a = (seed % 360) * Mathf.Pi / 180f;
		beam.Miss = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 4f;   // Fehlschuss: knapp am Ziel vorbei (nur Darstellung)
		beam.Pivot.GetChild<MeshInstance3D>(0).MaterialOverride = type == DamageType.Energy ? _energy : _kinetic;
	}

	Node3D MakeBeam()
	{
		var pivot = new Node3D { Visible = false };
		var cyl = Gfx.Mesh(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 1f, RadialSegments = 6 }, _energy, Vector3.Zero, pivot);
		cyl.RotationDegrees = new Vector3(90, 0, 0);
		AddChild(pivot);
		return pivot;
	}

	/// <summary>Strahlen nachführen (Schiffspositionen kommen aus der Sektoransicht).</summary>
	public void UpdateBeams(System.Func<ShipId, Vector3?> shipPosition, double now)
	{
		foreach (var b in _beams)
		{
			if (now >= b.Until || shipPosition(b.Shooter) is not { } from || shipPosition(b.Target) is not { } to) { b.Pivot.Visible = false; continue; }
			var end = b.Hit ? to : to + b.Miss;
			var d = end - from;
			float len = d.Length();
			if (len < 0.5f) { b.Pivot.Visible = false; continue; }
			b.Pivot.Visible = true;
			b.Pivot.Position = (from + end) / 2f;
			b.Pivot.LookAt(end, Vector3.Up);
			float fade = (float)((b.Until - now) / FlashSeconds);
			b.Pivot.Scale = new Vector3(0.6f + fade, 0.6f + fade, len);
		}
	}

	/// <summary>Beim Neuaufbau des Sektors: alle Strahlen aus.</summary>
	public void Reset()
	{
		foreach (var b in _beams) b.Pivot.Visible = false;
	}
}
