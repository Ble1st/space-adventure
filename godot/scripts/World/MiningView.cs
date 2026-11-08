using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;

namespace SpaceAdventure.World;

/// <summary>
/// Zeichnet Laserblitze, Traktorstrahl, Erzbrocken und die Zielmarkierung. Enthält keine Abbau-Logik: Spielerschüsse kommen aus
/// <see cref="OreChunks"/>, NPC-Schüsse aus den <c>MinerShot</c>-Ereignissen der Simulation. Jeder Schuss erscheint als kurzer Blitz.
/// </summary>
public partial class MiningView : Node3D
{
	const double FlashSeconds = 0.14;
	static readonly Color LaserColor = new(1f, 0.35f, 0.15f);
	static readonly Color TractorColor = new(0.3f, 0.9f, 1f);
	static readonly Color OreColor = new(1f, 0.8f, 0.25f);

	readonly Node3D _laser, _targetRing;
	readonly List<Node3D> _tractorBeams = new();
	readonly List<MeshInstance3D> _chunks = new();
	readonly SphereMesh _chunkMesh = new() { Radius = 0.7f, Height = 1.4f, RadialSegments = 8, Rings = 4 };
	readonly Material _oreMat = Gfx.Mat(OreColor, 2.5f);

	double _laserUntil;
	Vector3 _laserTarget;

	sealed class NpcBeam { public Node3D Pivot = null!; public double Until; public Vector3 Target; }
	sealed class FlyingChunk { public MeshInstance3D Node = null!; public Vector3 From, Last; public ShipId Ship; public double Start, Duration; }
	readonly Dictionary<ShipId, NpcBeam> _npcBeams = new();
	readonly List<FlyingChunk> _flying = new();

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

	static void Place(Node3D beam, Vector3 a, Vector3 b, float thickness = 1f)
	{
		var d = b - a;
		float len = d.Length();
		if (len < 0.05f) { beam.Visible = false; return; }
		beam.Visible = true;
		beam.Position = (a + b) / 2f;
		beam.LookAt(b, Vector3.Up);
		beam.Scale = new Vector3(thickness, thickness, len);
	}

	// ───────── Spieler ─────────

	/// <summary>Ein Schuss des Spielers: kurzer Laserblitz zum Ziel.</summary>
	public void FirePlayer(Vector3 target, double now) { _laserTarget = target; _laserUntil = now + FlashSeconds; }

	public void UpdatePlayer(Vector3 ship, bool tractor, IReadOnlyList<OreChunk> chunks, double now)
	{
		if (now < _laserUntil)
		{
			float fade = (float)((_laserUntil - now) / FlashSeconds);
			Place(_laser, ship + (_laserTarget - ship).Normalized() * 2.5f, _laserTarget, 0.5f + 1.3f * fade);
		}
		else _laser.Visible = false;

		while (_chunks.Count < chunks.Count) _chunks.Add(Gfx.Mesh(_chunkMesh, _oreMat, Vector3.Zero, this));
		for (int i = 0; i < _chunks.Count; i++)
		{
			bool used = i < chunks.Count;
			_chunks[i].Visible = used;
			if (!used) continue;
			_chunks[i].Position = new Vector3(chunks[i].Position.X, 0, chunks[i].Position.Y);
			_chunks[i].Scale = Vector3.One * ChunkScale(chunks[i].Units);   // größere Brocken sind sichtbar größer
		}

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

	static float ChunkScale(float units) => 0.55f + 0.14f * units;

	public void SetTarget(Vector3? center, float radius)
	{
		_targetRing.Visible = center != null;
		if (center is { } c) { _targetRing.Position = c + new Vector3(0, 0.5f, 0); _targetRing.Scale = Vector3.One * (radius + 2.5f); }
	}

	public void ClearPlayer()
	{
		_laser.Visible = false;
		_laserUntil = 0;
		foreach (var b in _tractorBeams) b.Visible = false;
		foreach (var c in _chunks) c.Visible = false;
		_targetRing.Visible = false;
	}

	// ───────── NPC-Miner (aus Simulationsereignissen) ─────────

	/// <summary>Ein NPC-Miner hat geschossen: Laserblitz zum Asteroiden; bei einem Treffer fliegt ein Brocken zu seinem Schiff.</summary>
	public void NpcShot(ShipId ship, Vector3 target, float units, double now)
	{
		if (!_npcBeams.TryGetValue(ship, out var beam)) _npcBeams[ship] = beam = new NpcBeam { Pivot = MakeBeam(LaserColor, 0.14f, 3f) };
		beam.Target = target;
		beam.Until = now + FlashSeconds;
		if (units <= 0f) return;

		var chunk = Gfx.Mesh(_chunkMesh, _oreMat, target, this);
		chunk.Scale = Vector3.One * ChunkScale(units);
		_flying.Add(new FlyingChunk { Node = chunk, From = target, Last = target, Ship = ship, Start = now, Duration = 1.1 });
	}

	/// <param name="shipPosition">Aktuelle angezeigte Position eines NPC-Schiffs (null, wenn es nicht mehr im Sektor ist).</param>
	public void UpdateNpc(Func<ShipId, Vector3?> shipPosition, double now)
	{
		foreach (var (id, beam) in _npcBeams.ToList())
		{
			var pos = shipPosition(id);
			if (pos is { } p && now < beam.Until)
			{
				float fade = (float)((beam.Until - now) / FlashSeconds);
				Place(beam.Pivot, p + (beam.Target - p).Normalized() * 2.2f, beam.Target, 0.5f + 1.2f * fade);
			}
			else beam.Pivot.Visible = false;
			if (pos == null && now > beam.Until + 3) { beam.Pivot.QueueFree(); _npcBeams.Remove(id); }
		}

		for (int i = _flying.Count - 1; i >= 0; i--)
		{
			var f = _flying[i];
			double t = (now - f.Start) / f.Duration;
			if (shipPosition(f.Ship) is { } dest) f.Last = dest;
			if (t >= 1.0) { f.Node.QueueFree(); _flying.RemoveAt(i); continue; }
			f.Node.Position = f.From.Lerp(f.Last, (float)(t * t));   // beschleunigt zum Schiff, wie vom Traktorstrahl gezogen
		}
	}

	/// <summary>Beim Neuaufbau des Sektors: NPC-Strahlen und fliegende Brocken verwerfen.</summary>
	public void ResetNpc()
	{
		foreach (var b in _npcBeams.Values) b.Pivot.QueueFree();
		_npcBeams.Clear();
		foreach (var f in _flying) f.Node.QueueFree();
		_flying.Clear();
	}
}
