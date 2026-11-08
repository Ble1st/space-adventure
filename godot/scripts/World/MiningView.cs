using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;

namespace SpaceAdventure.World;

/// <summary>
/// Zeichnet Laser, Traktorstrahl, Erzbrocken und die Zielmarkierung. Enthält keine Abbau-Logik: Spielerabbau kommt aus
/// <see cref="OreChunks"/>, NPC-Abbau ist reine Darstellung des abstrakten Zustands "Miner arbeitet am Feld".
/// </summary>
public partial class MiningView : Node3D
{
	static readonly Color LaserColor = new(1f, 0.35f, 0.15f);
	static readonly Color TractorColor = new(0.3f, 0.9f, 1f);
	static readonly Color OreColor = new(1f, 0.8f, 0.25f);

	readonly Node3D _laser, _targetRing;
	readonly List<Node3D> _tractorBeams = new();
	readonly List<MeshInstance3D> _chunks = new();
	readonly Dictionary<ShipId, NpcRig> _npc = new();
	readonly SphereMesh _chunkMesh = new() { Radius = 0.7f, Height = 1.4f, RadialSegments = 8, Rings = 4 };
	readonly Material _oreMat = Gfx.Mat(OreColor, 2.5f);

	sealed class NpcRig { public Node3D Root = null!; public Node3D Laser = null!; public MeshInstance3D[] Chunks = null!; public int Asteroid = -1; }

	public MiningView()
	{
		_laser = MakeBeam(LaserColor, 0.2f, 4f);
		_targetRing = new Node3D { Visible = false };
		Gfx.Mesh(new TorusMesh { InnerRadius = 0.96f, OuterRadius = 1f, Rings = 24, RingSegments = 6 }, Gfx.Mat(new Color(1f, 0.9f, 0.3f), 3f), Vector3.Zero, _targetRing);
		AddChild(_targetRing);
		for (int i = 0; i < 6; i++) _tractorBeams.Add(MakeBeam(TractorColor, 0.08f, 2.5f, alpha: 0.55f));
	}

	Node3D MakeBeam(Color color, float radius, float emissive, float alpha = 1f)
	{
		var pivot = new Node3D { Visible = false };
		var c = color; c.A = alpha;
		var mat = Gfx.Mat(c, emissive);
		if (alpha < 1f) mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		var cyl = Gfx.Mesh(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 1f, RadialSegments = 6 }, mat, Vector3.Zero, pivot);
		cyl.RotationDegrees = new Vector3(90, 0, 0);   // Zylinderachse entlang Z; die Länge kommt aus der Skalierung des Pivots
		AddChild(pivot);
		return pivot;
	}

	static void Place(Node3D beam, Vector3 a, Vector3 b)
	{
		var d = b - a;
		float len = d.Length();
		if (len < 0.05f) { beam.Visible = false; return; }
		beam.Visible = true;
		beam.Position = (a + b) / 2f;
		beam.LookAt(b, Vector3.Up);
		beam.Scale = new Vector3(1, 1, len);
	}

	// ───────── Spieler ─────────

	/// <param name="laserHit">Punkt auf dem Asteroiden, auf den gefeuert wird (null = Laser aus).</param>
	public void UpdatePlayer(Vector3 ship, Vector3? laserHit, bool tractor, IReadOnlyList<OreChunk> chunks, double time)
	{
		if (laserHit is { } hit)
		{
			Place(_laser, ship + (hit - ship).Normalized() * 2.5f, hit);
			float pulse = 0.8f + 0.2f * Mathf.Sin((float)time * 40f);
			_laser.Scale = new Vector3(pulse, pulse, _laser.Scale.Z);
		}
		else _laser.Visible = false;

		// Brocken
		while (_chunks.Count < chunks.Count) _chunks.Add(Gfx.Mesh(_chunkMesh, _oreMat, Vector3.Zero, this));
		for (int i = 0; i < _chunks.Count; i++)
		{
			bool used = i < chunks.Count;
			_chunks[i].Visible = used;
			if (used) _chunks[i].Position = new Vector3(chunks[i].Position.X, 0, chunks[i].Position.Y);
		}

		// Traktorstrahlen zu den nächsten Brocken in Reichweite
		var near = tractor
			? chunks.Select(c => (c, d: Vec2.Distance(c.Position, new Vec2(ship.X, ship.Z)))).Where(x => x.d <= MiningRules.TractorRange)
				.OrderBy(x => x.d).Take(_tractorBeams.Count).ToList()
			: new();
		for (int i = 0; i < _tractorBeams.Count; i++)
		{
			if (i < near.Count) Place(_tractorBeams[i], ship, new Vector3(near[i].c.Position.X, 0, near[i].c.Position.Y));
			else _tractorBeams[i].Visible = false;
		}
	}

	public void SetTarget(Vector3? center, float radius)
	{
		_targetRing.Visible = center != null;
		if (center is { } c) { _targetRing.Position = c + new Vector3(0, 0.5f, 0); _targetRing.Scale = Vector3.One * (radius + 2.5f); }
	}

	public void ClearPlayer()
	{
		_laser.Visible = false;
		foreach (var b in _tractorBeams) b.Visible = false;
		foreach (var c in _chunks) c.Visible = false;
		_targetRing.Visible = false;
	}

	// ───────── NPC-Miner (nur Optik) ─────────

	/// <summary>Miner, die laut Simulation gerade abbauen, bekommen Laser auf den nächsten Asteroiden und fliegende Brocken zum Schiff.</summary>
	public void UpdateNpc(IReadOnlyList<(ShipId Id, Vector3 Pos)> miners, Sector sector, double time)
	{
		var active = new HashSet<ShipId>();
		foreach (var (id, pos) in miners)
		{
			active.Add(id);
			if (!_npc.TryGetValue(id, out var rig)) _npc[id] = rig = MakeNpcRig();
			if (rig.Asteroid < 0 || rig.Asteroid >= sector.Asteroids.Count) rig.Asteroid = NearestAsteroid(sector, pos);
			if (rig.Asteroid < 0) { rig.Root.Visible = false; continue; }

			var a = sector.Asteroids[rig.Asteroid];
			var center = new Vector3(a.Position.X, 0, a.Position.Y);
			var surface = center + (pos - center).Normalized() * a.Radius;
			rig.Root.Visible = true;
			Place(rig.Laser, pos + (surface - pos).Normalized() * 2.2f, surface);
			float pulse = 0.7f + 0.3f * Mathf.Sin((float)time * 30f + id.Value);
			rig.Laser.Scale = new Vector3(pulse, pulse, rig.Laser.Scale.Z);
			for (int i = 0; i < rig.Chunks.Length; i++)
			{
				float t = Mathf.PosMod((float)time * 0.7f + i / (float)rig.Chunks.Length + id.Value * 0.13f, 1f);
				rig.Chunks[i].Position = surface.Lerp(pos, t);
			}
		}
		foreach (var id in _npc.Keys.Where(id => !active.Contains(id)).ToList())
		{
			_npc[id].Root.QueueFree();
			_npc.Remove(id);
		}
	}

	NpcRig MakeNpcRig()
	{
		var root = new Node3D();
		AddChild(root);
		var laser = new Node3D { Visible = false };
		var mat = Gfx.Mat(LaserColor, 2.5f);
		var cyl = Gfx.Mesh(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 1f, RadialSegments = 6 }, mat, Vector3.Zero, laser);
		cyl.RotationDegrees = new Vector3(90, 0, 0);
		root.AddChild(laser);
		var chunks = Enumerable.Range(0, 3).Select(_ => Gfx.Mesh(new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 6, Rings = 3 }, _oreMat, Vector3.Zero, root)).ToArray();
		return new NpcRig { Root = root, Laser = laser, Chunks = chunks };
	}

	static int NearestAsteroid(Sector sector, Vector3 pos)
	{
		int best = -1; float bestD = 70f;
		for (int i = 0; i < sector.Asteroids.Count; i++)
		{
			var a = sector.Asteroids[i];
			if (sector.FieldFor(a.Position) == null) continue;
			float d = new Vector2(pos.X - a.Position.X, pos.Z - a.Position.Y).Length() - a.Radius;
			if (d < bestD) { bestD = d; best = i; }
		}
		return best;
	}

	/// <summary>Beim Neuaufbau des Sektors: NPC-Gerüste verwerfen (Asteroiden-Indizes gelten nicht mehr).</summary>
	public void ResetNpc()
	{
		foreach (var rig in _npc.Values) rig.Root.QueueFree();
		_npc.Clear();
	}
}
