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

	sealed class Beam { public Node3D Pivot = null!; public ShipId Shooter, Target; public Vector3? FromFixed, ToFixed; public Vector3 Miss; public double Until; public bool Hit; public int Slot = -1; public StationId FromStation; }
	readonly List<Beam> _beams = new();
	readonly Material _energy = Gfx.Mat(EnergyColor, 3f), _kinetic = Gfx.Mat(KineticColor, 3f), _missile = Gfx.Mat(MissileColor, 4f);
	static readonly Color MissileColor = new(1f, 0.45f, 0.65f);
	readonly List<Node3D> _missiles = new();

	/// <summary>Runde 11: Anteil der Flugzeit, in dem die Rakete aus dem Schacht steigt, und die Steighöhe.</summary>
	const float LaunchShare = 0.18f, LaunchRise = 4f;
	/// <summary>Selbsttest: Mündung (Schacht/Werfer) der zuletzt gezeichneten Rakete mit bekanntem Slot.</summary>
	public Vector3? LastMissileStart { get; private set; }
	/// <summary>Selbsttest: Schütze und Slot der Rakete zu <see cref="LastMissileStart"/>.</summary>
	public (ShipId Shooter, int Slot) LastMissileSlot { get; private set; }
	/// <summary>Selbsttest: letzte Raketenmündung je Schütze (Slot, Mündung).</summary>
	public readonly System.Collections.Generic.Dictionary<ShipId, (int Slot, Vector3 At)> MissileStarts = new();

	/// <summary>Phase 28: Zahl der zuletzt gezeigten Raketen im Flug (Selbsttest).</summary>
	public int MissilesShown { get; private set; }

	/// <summary>Zahl der gezeichneten Schüsse seit dem letzten Zurücksetzen (Selbsttest).</summary>
	public int ShotsSeen { get; private set; }

	/// <summary>Ein Schuss der Simulation (im Spielersektor): Strahl für kurze Zeit.</summary>
	public void Shot(ShipId shooter, ShipId target, DamageType type, bool hit, double now, int seed) => Shot(shooter, target, null, null, type, hit, now, seed);

	/// <summary>Phase 28: Schuss mit festem Start- oder Endpunkt (Stationsgeschütz bzw. Schuss auf eine Station).</summary>
	public void Shot(ShipId shooter, ShipId target, Vector3? fromFixed, Vector3? toFixed, DamageType type, bool hit, double now, int seed) =>
		Shot(shooter, -1, default, -1, target, fromFixed, toFixed, type, hit, now, seed);

	/// <summary>Runde 11: Schuss aus einer sichtbaren Stellung — Schiffsslot <paramref name="slot"/> bzw. Stationsgeschütz <paramref name="gun"/> (Mündung über
	/// <see cref="MuzzleOf"/>/<see cref="StationMuzzleOf"/>, ohne Stellung die Mitte).</summary>
	public void Shot(ShipId shooter, int slot, StationId fromStation, int gun, ShipId target, Vector3? fromFixed, Vector3? toFixed, DamageType type, bool hit, double now, int seed)
	{
		ShotsSeen++;
		Beam? beam = null;
		foreach (var b in _beams) if (now >= b.Until) { beam = b; break; }
		if (beam is null) { beam = new Beam { Pivot = MakeBeam() }; _beams.Add(beam); }
		beam.Shooter = shooter; beam.Target = target; beam.FromFixed = fromFixed; beam.ToFixed = toFixed; beam.Hit = hit; beam.Until = now + FlashSeconds;
		beam.Slot = fromStation.Value != 0 ? gun : slot; beam.FromStation = fromStation;
		float a = (seed % 360) * Mathf.Pi / 180f;
		beam.Miss = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 4f;   // Fehlschuss: knapp am Ziel vorbei (nur Darstellung)
		beam.Pivot.GetChild<MeshInstance3D>(0).MaterialOverride = type == DamageType.Energy ? _energy : type == DamageType.Explosive ? _missile : _kinetic;
	}

	Node3D MakeBeam()
	{
		var pivot = new Node3D { Visible = false };
		var cyl = Gfx.Mesh(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 1f, RadialSegments = 6 }, _energy, Vector3.Zero, pivot);
		cyl.RotationDegrees = new Vector3(90, 0, 0);
		AddChild(pivot);
		return pivot;
	}

	/// <summary>Runde 11: Mündung eines Schiffsslots in Weltkoordinaten (Main setzt sie: Sektoransicht und Spielerschiff).</summary>
	public System.Func<ShipId, int, Vector3?>? MuzzleOf;
	/// <summary>Runde 11: Mündung eines Stationsgeschützes.</summary>
	public System.Func<StationId, int, Vector3?>? StationMuzzleOf;
	/// <summary>Runde 11: Schussrichtung einer Schiffsstellung (Raketenschächte zeigen nach oben).</summary>
	public System.Func<ShipId, int, Vector3?>? MuzzleDirOf;
	/// <summary>Runde 11: Schussrichtung einer Stationsstellung.</summary>
	public System.Func<StationId, int, Vector3?>? StationMuzzleDirOf;
	/// <summary>Selbsttest: Startpunkt des zuletzt gezeichneten Strahls.</summary>
	public Vector3? LastBeamFrom { get; private set; }

	/// <summary>Strahlen nachführen (Schiffspositionen kommen aus der Sektoransicht).</summary>
	public void UpdateBeams(System.Func<ShipId, Vector3?> shipPosition, double now)
	{
		foreach (var b in _beams)
		{
			Vector3? start = b.FromStation.Value != 0 ? StationMuzzleOf?.Invoke(b.FromStation, b.Slot) ?? b.FromFixed
				: b.FromFixed ?? (b.Slot >= 0 ? MuzzleOf?.Invoke(b.Shooter, b.Slot) : null) ?? shipPosition(b.Shooter);
			if (now >= b.Until || start is not { } from || (b.ToFixed ?? shipPosition(b.Target)) is not { } to) { b.Pivot.Visible = false; continue; }
			LastBeamFrom = from;
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

	/// <summary>
	/// Phase 28: Raketen im Flug (räumliche Gefechte im Spielersektor) als leuchtende Kugeln — Position rein aus dem Core (<see cref="SpaceSim.Core.Combat.Missiles.PositionOf"/>).
	/// </summary>
	public void UpdateMissiles(Universe u)
	{
		int n = 0;
		foreach (var e in u.Fights.Encounters)
		{
			if (!e.Spatial || e.Sector != u.PlayerSector) continue;
			foreach (var f in e.Missiles)
			{
				if (n >= _missiles.Count)
				{
					var node = new Node3D();
					// Gerätetest Phase 28: größer und mit Leuchtschweif (vorher am Handy nicht zu sehen)
					Gfx.Mesh(new SphereMesh { Radius = 1.6f, Height = 3.2f, RadialSegments = 10, Rings = 5 }, _missile, Vector3.Zero, node);
					var trail = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.2f, BottomRadius = 1.2f, Height = 7f, RadialSegments = 8 }, MaterialOverride = _missile };
					trail.RotationDegrees = new Vector3(90f, 0f, 0f);
					trail.Position = new Vector3(0f, 0f, 3.5f);
					node.AddChild(trail);
					AddChild(node);
					_missiles.Add(node);
				}
				var p = SpaceSim.Core.Combat.Missiles.PositionOf(u, e, f);
				var np = new Vector3(p.X, 0.6f, p.Y);
				// Runde 11: Start sichtbar am Schacht/Werfer des feuernden Slots — erst entlang der Schachtrichtung (Senkrechtstart: „dir“ zeigt oft nach oben),
				// dann weich auf die Flugbahn aus dem Core einschwenken (nur Darstellung; Treffer und Flugzeit bestimmt der Core)
				if (f.Slot >= 0 && f.Total > 0.01f)
				{
					Vector3? at = f.FromStation.Value != 0 ? StationMuzzleOf?.Invoke(f.FromStation, f.Slot) : MuzzleOf?.Invoke(f.Shooter, f.Slot);
					Vector3? dir = f.FromStation.Value != 0 ? StationMuzzleDirOf?.Invoke(f.FromStation, f.Slot) : MuzzleDirOf?.Invoke(f.Shooter, f.Slot);
					if (at is { } a0)
					{
						float t = 1f - f.Left / f.Total;
						float rise = Mathf.Clamp(t / LaunchShare, 0f, 1f);
						var early = a0 + (dir ?? Vector3.Up) * (LaunchRise * Mathf.Sin(rise * Mathf.Pi * 0.5f));
						float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - LaunchShare * 0.4f) / (LaunchShare * 1.6f), 0f, 1f));
						np = early.Lerp(np, w);
						LastMissileStart = a0; LastMissileSlot = (f.Shooter, f.Slot);
						if (f.Shooter.Value != 0) MissileStarts[f.Shooter] = (f.Slot, a0);
					}
				}
				var prev = _missiles[n].Position;
				_missiles[n].Position = np;
				if ((np - prev).LengthSquared() > 0.01f && (np - prev).LengthSquared() < 400f) _missiles[n].LookAt(np + (np - prev), Vector3.Up);   // Schweif zeigt nach hinten
				_missiles[n].Visible = true;
				n++;
			}
		}
		for (int i = n; i < _missiles.Count; i++) _missiles[i].Visible = false;
		MissilesShown = n;
	}

	/// <summary>Beim Neuaufbau des Sektors: alle Strahlen aus.</summary>
	public void Reset()
	{
		foreach (var b in _beams) b.Pivot.Visible = false;
		foreach (var m in _missiles) m.Visible = false;
		MissileStarts.Clear();
	}
}
