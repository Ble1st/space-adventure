using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure.World;

/// <summary>Ziel, das der Spieler antippen kann.</summary>
public readonly record struct PickTarget(string Kind, int Id, Vector3 Position, string Label);

/// <summary>
/// Materialisiert den Spielersektor aus Core-Daten: Stationen, Gates, Asteroiden und NPC-Schiffe.
/// Enthält keine Spiellogik, sondern rendert nur, was die Simulation vorgibt.
/// </summary>
public partial class SectorView : Node3D
{
	sealed class Marker { public Node3D Pivot = null!; public float Yaw; public Vector3 Offset; }

	Universe? _u;
	SectorId _sector;
	int _structureVersion = -1;
	Node3D _root = null!;
	readonly List<MeshInstance3D> _gateRings = new();
	readonly List<MeshInstance3D> _asteroids = new();
	readonly Dictionary<ShipId, Marker> _markers = new();

	public List<Obstacle> Obstacles { get; private set; } = new();
	public List<(GateId Id, Vector3 Pos)> Gates { get; } = new();
	public List<PickTarget> Targets { get; } = new();
	public SectorId Sector => _sector;

	/// <summary>True, wenn Sektor gewechselt oder Stationen hinzugekommen/entfernt wurden.</summary>
	public bool NeedsRebuild(Universe u) => _u != u || _sector != u.PlayerSector || _structureVersion != u.StructureVersion;

	public void Rebuild(Universe u)
	{
		_u = u; _sector = u.PlayerSector; _structureVersion = u.StructureVersion;
		_root?.QueueFree();
		_root = new Node3D { Name = "Sector" };
		AddChild(_root);
		_gateRings.Clear(); _asteroids.Clear(); _markers.Clear(); Gates.Clear(); Targets.Clear();

		var sector = u.Sectors[_sector];
		foreach (var sid in sector.Stations) BuildStation(u.Stations[sid]);
		foreach (var gid in sector.Gates) BuildGate(u, u.Gates[gid]);
		for (int i = 0; i < sector.Asteroids.Count; i++) BuildAsteroid(sector.Asteroids[i], i);
		foreach (var f in sector.Fields) BuildFieldMarker(u, f);
		Obstacles = u.GetObstacles(_sector);
	}

	public void Animate(float delta)
	{
		foreach (var r in _gateRings) r.RotateZ(delta * 0.8f);
		foreach (var a in _asteroids) { a.RotateY(delta * 0.2f); a.RotateX(delta * 0.1f); }
	}

	/// <summary>Aktuelle (angezeigte) Positionen der NPC-Schiffe als Kollisionskreise.</summary>
	public IEnumerable<Obstacle> ShipBodies() =>
		_markers.Values.Select(m => new Obstacle(new Vec2(m.Pivot.Position.X, m.Pivot.Position.Z), Collision.ShipRadius));

	/// <summary>
	/// NPC-Schiffe aus der Core-Materialisierung: erscheinen am Gate, fliegen ihre Teilstrecke und verschwinden am Ausgangsgate.
	/// Ausweichen (Schiff↔Schiff, Schiff↔Spieler) ist reine Darstellung und verändert die Simulation nicht.
	/// </summary>
	public void UpdateShips(Universe u, double tick, Vector3 playerPos, float delta)
	{
		var snaps = Materializer.Snapshot(u, _sector, tick);
		var neighbours = snaps.Select(x => (x.Id, x.Position)).ToList();
		neighbours.Add((Avoidance.Player, new Vec2(playerPos.X, playerPos.Z)));
		var seen = new HashSet<ShipId>();

		foreach (var s in snaps)
		{
			seen.Add(s.Id);
			var target = new Vector3(s.Position.X, 0, s.Position.Y);
			if (!_markers.TryGetValue(s.Id, out var m))
			{
				var color = s.Role == ShipRole.Miner ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.7f, 0.2f);
				var pivot = new Node3D { Position = target };
				var hull = Gfx.Mesh(new PrismMesh { Size = new Vector3(2f, 3f, 0.8f) }, Gfx.Mat(color, 1f), Vector3.Zero, pivot);
				hull.RotationDegrees = new Vector3(-90, 0, 0);
				_root.AddChild(pivot);
				_markers[s.Id] = m = new Marker { Pivot = pivot };
			}
			var desired = Avoidance.Desired(s.Id, s.Position, neighbours);
			m.Offset = m.Offset.Lerp(new Vector3(desired.X, 0, desired.Y), 1f - Mathf.Exp(-5f * delta));
			var shown = target + m.Offset;
			var shown2 = new Vec2(shown.X, shown.Z);
			Collision.Resolve(ref shown2, Collision.ShipRadius, Obstacles, out _);   // Ausweichen führt nie in Hindernisse
			m.Pivot.Position = new Vector3(shown2.X, 0, shown2.Y);
			if (s.Moving)
			{
				m.Yaw = Mathf.LerpAngle(m.Yaw, Mathf.Atan2(-s.Direction.X, -s.Direction.Y), 1f - Mathf.Exp(-10f * delta));
				m.Pivot.Rotation = new Vector3(0, m.Yaw, 0);
			}
		}
		foreach (var id in _markers.Keys.Where(id => !seen.Contains(id)).ToList())
		{
			_markers[id].Pivot.QueueFree();
			_markers.Remove(id);
		}
	}

	// ───────── Aufbau aus Core-Daten ─────────

	void BuildStation(Station st)
	{
		var pos = new Vector3(st.Position.X, 0, st.Position.Y);
		var root = new Node3D { Position = pos };
		_root.AddChild(root);
		Gfx.Mesh(new CylinderMesh { TopRadius = 4, BottomRadius = 4, Height = 14 }, Gfx.Mat(new Color(0.55f, 0.6f, 0.65f), 0f, 0.9f), Vector3.Zero, root);
		Gfx.Mesh(new TorusMesh { InnerRadius = 11, OuterRadius = 14 }, Gfx.Mat(new Color(0.4f, 0.45f, 0.55f), 0f, 0.9f), Vector3.Zero, root);
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.Tau * i / 6f;
			Gfx.Mesh(new SphereMesh { Radius = 0.6f, Height = 1.2f }, Gfx.Mat(new Color(1f, 0.8f, 0.3f), 3f),
				new Vector3(Mathf.Cos(a) * 12.5f, 0.8f, Mathf.Sin(a) * 12.5f), root);
		}
		root.AddChild(Gfx.Label(st.Name, new Vector3(0, 6, 0)));
		Targets.Add(new PickTarget("station", st.Id.Value, pos, st.Name));
	}

	void BuildGate(Universe u, Gate g)
	{
		var pos = new Vector3(g.Position.X, 0, g.Position.Y);
		var root = new Node3D { Position = pos };
		_root.AddChild(root);
		var ring = Gfx.Mesh(new TorusMesh { InnerRadius = 12, OuterRadius = 14 }, Gfx.Mat(new Color(0.2f, 0.7f, 1f), 3f), Vector3.Zero, root);
		ring.RotationDegrees = new Vector3(90, 0, 0);
		_gateRings.Add(ring);
		var core = Gfx.Mat(new Color(0.3f, 0.6f, 1f, 0.25f), 1f);
		core.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		Gfx.Mesh(new SphereMesh { Radius = 8, Height = 16 }, core, Vector3.Zero, root);
		var target = u.Sectors[u.Gates[g.Target].Sector];
		root.AddChild(Gfx.Label($"Jumpgate → {target.Name}", new Vector3(0, 8, 0)));
		Gates.Add((g.Id, pos));
		Targets.Add(new PickTarget("gate", g.Id.Value, pos, $"Gate → {target.Name}"));
	}

	void BuildAsteroid(Obstacle o, int index)
	{
		float g = 0.3f + (index * 37 % 20) / 100f;
		var mesh = new SphereMesh { Radius = o.Radius, Height = o.Radius * 2f, RadialSegments = 8, Rings = 5 };
		_asteroids.Add(Gfx.Mesh(mesh, Gfx.Mat(new Color(g + 0.1f, g, g - 0.1f), 0f, 0.1f), new Vector3(o.Position.X, 0, o.Position.Y), _root));
	}

	void BuildFieldMarker(Universe u, ResourceField f)
	{
		var label = Gfx.Label($"{u.Goods[f.Good].Name}-Feld", new Vector3(f.Position.X, 4, f.Position.Y));
		_root.AddChild(label);
	}
}
