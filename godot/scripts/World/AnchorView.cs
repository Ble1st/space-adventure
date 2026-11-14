using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;

namespace SpaceAdventure.World;

/// <summary>
/// Phase 32 (Runde 17): Ankertrossen der Kontaktschürfer im Spielersektor — nur Darstellung des Core-Zustands (<see cref="Ship.AnchorRock"/>,
/// <see cref="Ship.AnchorProgress"/>) und der Ereignisse <see cref="AnchorReleased"/>. Die Harpunen fliegen aus den <c>Harpoon_*</c>-Knoten des Modells (die
/// Spitze im Modell wird ausgeblendet) zum Einschlagpunkt auf der Felsoberfläche, die Trosse ist ein dünner Zylinder von der Mündung zur Spitze, gestrafft;
/// beim Lösen/Reißen schnappt sie zurück (Riss: Funken). Während des Abbaus: Abraumfahne am Kontaktpunkt und Stöße der Manövrierdüsen. Ohne Harpunenknoten
/// (eigene Modelle, Grundkörper) gehen zwei Trossen vom Bug aus. Handy: Linien, Spitzen und Partikel werden gepoolt (wenige Knoten).
/// </summary>
public partial class AnchorView : Node3D
{
	sealed class Tether { public MeshInstance3D Line = null!; public MeshInstance3D Head = null!; }
	sealed class Entry
	{
		public ShipId Ship; public int Rock;
		public readonly List<Tether> Tethers = new();
		public CpuParticles3D Plume = null!, Puff = null!;
		public float Shown;                 // angezeigter Flugfortschritt 0..1 (folgt dem Core weich)
		public float Snap = -1f;            // ≥ 0: Zurückschnappen läuft (Sekunden)
		public bool Violent;
		public Vector3[] Muzzles = Array.Empty<Vector3>();
		public Vector3[] Impacts = Array.Empty<Vector3>();
		public List<Node3D> Hidden = new();
		public float PuffClock; public int PuffSide = 1;
	}

	const float SnapSeconds = 0.35f, TetherRadius = 0.08f;
	readonly Dictionary<ShipId, Entry> _live = new();
	readonly List<Entry> _snapping = new();
	readonly Stack<Tether> _tetherPool = new();
	readonly Stack<(CpuParticles3D Plume, CpuParticles3D Puff)> _fxPool = new();
	static StandardMaterial3D? _lineMat, _headMat, _plumeMat, _puffMat;

	/// <summary>Effekte für Funken beim Riss (setzt Main).</summary>
	public Fx? Fx { get; set; }

	// ── Selbsttest ──
	public int ActiveCount => _live.Count;
	public int SnappingCount => _snapping.Count;
	public int VisibleTethers => _live.Values.Sum(e => e.Tethers.Count(t => t.Line.Visible));
	public IReadOnlyList<Vector3> MuzzlesOf(ShipId id) => _live.TryGetValue(id, out var e) ? e.Muzzles : Array.Empty<Vector3>();
	public IReadOnlyList<Vector3> TipsOf(ShipId id) => _live.TryGetValue(id, out var e) ? e.Tethers.Select(t => t.Head.GlobalPosition).ToList() : Array.Empty<Vector3>();
	public bool HarpoonsHidden(ShipId id) => _live.TryGetValue(id, out var e) && e.Hidden.Count > 0 && e.Hidden.All(h => !h.Visible);
	public bool PlumeOn(ShipId id) => _live.TryGetValue(id, out var e) && e.Plume.Emitting;
	public int TotalNodes => GetChildCount();

	/// <summary>Sektorwechsel/neue Sitzung: alles in den Pool.</summary>
	public void Clear()
	{
		foreach (var e in _live.Values.Concat(_snapping).ToList()) Recycle(e);
		_live.Clear(); _snapping.Clear();
	}

	/// <summary>Core meldet Lösen/Reißen: die Trossen schnappen zurück.</summary>
	public void OnReleased(AnchorReleased ar)
	{
		if (!_live.Remove(ar.Ship, out var e)) return;
		e.Snap = 0f;
		e.Violent = ar.Reason is "thrust" or "turn" or "impact" or "hit" or "split" or "combat" or "lost_contact";
		e.Plume.Emitting = false; e.Puff.Emitting = false;
		if (e.Violent && Fx is { } fx) foreach (var p in e.Impacts) fx.Burst(p, new Color(1f, 0.75f, 0.3f), 8, 9f, 0.4f);
		_snapping.Add(e);
	}

	/// <summary>Je Bild: Trossen aller verankerten Schiffe im Spielersektor nach dem Core-Zustand.</summary>
	public void Update(Universe u, Func<ShipId, (ShipVisual? Visual, Node3D Body)?> bodyOf, float delta)
	{
		var sector = u.Sectors[u.PlayerSector];
		var seen = new HashSet<ShipId>();
		foreach (var id in u.Tracker.ParkedIn(u.PlayerSector).Append(u.PlayerShip).Distinct())
		{
			if (!u.Ships.TryGetValue(id, out var ship) || ship.AnchorRock == 0 || ship.Sector != u.PlayerSector || ship.Transit is not null) continue;
			if (sector.Asteroids.FirstOrDefault(a => a.Id == ship.AnchorRock) is not { } rock) continue;
			if (bodyOf(id) is not { } body) continue;
			seen.Add(id);
			if (!_live.TryGetValue(id, out var e) || e.Rock != rock.Id)
			{
				if (e is not null) { _live.Remove(id); e.Snap = 0f; _snapping.Add(e); }
				e = Take(id, rock.Id, body.Visual);
				_live[id] = e;
			}
			UpdateEntry(u, e, ship, rock, body, delta);
		}
		foreach (var id in _live.Keys.Where(k => !seen.Contains(k)).ToList())
		{
			var e = _live[id]; _live.Remove(id);
			e.Snap = 0f; e.Plume.Emitting = false; e.Puff.Emitting = false;
			_snapping.Add(e);
		}
		for (int i = _snapping.Count - 1; i >= 0; i--)
		{
			var e = _snapping[i];
			e.Snap += delta;
			float k = Mathf.Clamp(e.Snap / SnapSeconds, 0f, 1f);
			var muzzles = bodyOf(e.Ship) is { } b ? Muzzles(b.Visual, b.Body, e.Tethers.Count) : e.Muzzles;
			for (int t = 0; t < e.Tethers.Count; t++)
			{
				var a = t < muzzles.Length ? muzzles[t] : e.Muzzles[Math.Min(t, e.Muzzles.Length - 1)];
				var tip = e.Impacts[t].Lerp(a, 1f - (1f - k) * (1f - k));   // schnappt schnell zurück
				Place(e.Tethers[t], a, tip);
			}
			if (k >= 1f) { _snapping.RemoveAt(i); Recycle(e); }
		}
	}

	void UpdateEntry(Universe u, Entry e, Ship ship, Asteroid rock, (ShipVisual? Visual, Node3D Body) body, float delta)
	{
		var center = new Vector3(rock.Position.X, 0f, rock.Position.Y);
		e.Muzzles = Muzzles(body.Visual, body.Body, e.Tethers.Count);
		if (e.Impacts.Length != e.Tethers.Count) e.Impacts = new Vector3[e.Tethers.Count];
		for (int t = 0; t < e.Tethers.Count; t++)
		{
			var d = e.Muzzles[t] - center;
			e.Impacts[t] = center + (d.LengthSquared() < 1e-4f ? Vector3.Back : d.Normalized()) * rock.Radius * 0.92f;
		}
		var state = MiningAnchor.State(u, ship);
		float want = state == AnchorState.Held ? 1f : Mathf.Clamp(ship.AnchorProgress / Mathf.Max(1, u.Rules.AnchorTicks) * 1.4f, 0f, 1f);
		e.Shown = Mathf.MoveToward(e.Shown, want, delta * 2.5f);
		for (int t = 0; t < e.Tethers.Count; t++) Place(e.Tethers[t], e.Muzzles[t], e.Muzzles[t].Lerp(e.Impacts[t], e.Shown));
		// Abbau: Abraumfahne am Kontaktpunkt (nach hinten-seitlich weg vom Schiff), Stöße der Manövrierdüsen gegen die Werkzeugdrehung
		bool mining = state == AnchorState.Held && e.Shown >= 0.99f;
		var shipPos = body.Body.GlobalPosition;
		var toShip = (shipPos - center); toShip.Y = 0f;
		var n = toShip.LengthSquared() < 1e-4f ? Vector3.Back : toShip.Normalized();
		var side = new Vector3(-n.Z, 0f, n.X);
		e.Plume.GlobalPosition = center + n * rock.Radius + Vector3.Up * 0.4f;
		e.Plume.Direction = (side * 0.8f + Vector3.Up * 0.6f - n * 0.2f).Normalized();
		if (e.Plume.Emitting != mining) e.Plume.Emitting = mining;
		e.PuffClock -= delta;
		if (mining && e.PuffClock <= 0f)
		{
			e.PuffClock = 0.45f + 0.1f * (ship.Id.Value % 3);
			e.PuffSide = -e.PuffSide;
			e.Puff.GlobalPosition = shipPos + side * (1.4f * e.PuffSide) + Vector3.Up * 0.3f;
			e.Puff.Direction = side * e.PuffSide;
			e.Puff.Restart();
		}
		if (!mining && e.Puff.Emitting) e.Puff.Emitting = false;
		if (mining && body.Visual is { } v) v.SetEngine(0.35f);   // leichtes Triebwerksglühen gegen den Zug
	}

	/// <summary>Mündungen in Weltkoordinaten: Harpunenknoten, sonst zwei Punkte am Bug.</summary>
	static Vector3[] Muzzles(ShipVisual? v, Node3D body, int count)
	{
		var list = new Vector3[count];
		if (v is { Harpoons.Count: > 0 } && v.Root.IsInsideTree())
		{
			for (int i = 0; i < count; i++) list[i] = v.Harpoons[Math.Min(i, v.Harpoons.Count - 1)].GlobalPosition;
			return list;
		}
		var tr = body.GlobalTransform;
		for (int i = 0; i < count; i++) list[i] = tr * new Vector3(i % 2 == 0 ? 0.7f : -0.7f, 0.2f, -2.2f);
		return list;
	}

	static void Place(Tether t, Vector3 a, Vector3 b)
	{
		var d = b - a;
		float len = d.Length();
		t.Line.Visible = len > 0.05f;
		t.Head.Visible = len > 0.05f;
		if (len <= 0.05f) return;
		var y = d / len;
		var q = Mathf.Abs(y.Dot(Vector3.Up)) > 0.9999f ? (y.Y > 0 ? Quaternion.Identity : new Quaternion(Vector3.Right, Mathf.Pi)) : new Quaternion(Vector3.Up, y);
		t.Line.GlobalTransform = new Transform3D(new Basis(q) * Basis.FromScale(new Vector3(1f, len, 1f)), (a + b) * 0.5f);
		t.Head.GlobalTransform = new Transform3D(new Basis(q), b);
	}

	Entry Take(ShipId id, int rock, ShipVisual? vis)
	{
		int count = vis is { Harpoons.Count: > 0 } ? vis.Harpoons.Count : 2;
		var e = new Entry { Ship = id, Rock = rock };
		for (int i = 0; i < count; i++) e.Tethers.Add(_tetherPool.Count > 0 ? _tetherPool.Pop() : NewTether());
		var fx = _fxPool.Count > 0 ? _fxPool.Pop() : NewFx();
		e.Plume = fx.Plume; e.Puff = fx.Puff;
		if (vis is not null) foreach (var h in vis.Harpoons) { h.Visible = false; e.Hidden.Add(h); }
		return e;
	}

	void Recycle(Entry e)
	{
		foreach (var t in e.Tethers) { t.Line.Visible = false; t.Head.Visible = false; _tetherPool.Push(t); }
		e.Tethers.Clear();
		foreach (var h in e.Hidden) if (IsInstanceValid(h)) h.Visible = true;
		e.Hidden.Clear();
		e.Plume.Emitting = false; e.Puff.Emitting = false;
		_fxPool.Push((e.Plume, e.Puff));
	}

	Tether NewTether()
	{
		_lineMat ??= Gfx.Mat(new Color(0.75f, 0.78f, 0.82f), 0.6f, 0.6f);
		_headMat ??= Gfx.Mat(new Color(1f, 0.6f, 0.2f), 1.5f, 0.6f);
		var line = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = TetherRadius, BottomRadius = TetherRadius, Height = 1f, RadialSegments = 5, Rings = 1 }, MaterialOverride = _lineMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
		var head = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.18f, Height = 0.5f, RadialSegments = 5, Rings = 1 }, MaterialOverride = _headMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
		AddChild(line); AddChild(head);
		return new Tether { Line = line, Head = head };
	}

	(CpuParticles3D Plume, CpuParticles3D Puff) NewFx()
	{
		_plumeMat ??= Gfx.Mat(new Color(0.62f, 0.55f, 0.48f), 0.4f, 0f);
		_puffMat ??= Gfx.Mat(new Color(0.75f, 0.9f, 1f), 2.5f, 0f);
		var plume = new CpuParticles3D
		{
			Amount = 14, Lifetime = 1.1f, Emitting = false, LocalCoords = false, Spread = 25f, Gravity = Vector3.Zero,
			InitialVelocityMin = 3f, InitialVelocityMax = 7f, ScaleAmountMin = 0.3f, ScaleAmountMax = 0.9f, Direction = Vector3.Up,
			Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.35f, 0.35f) }, MaterialOverride = _plumeMat,
		};
		var puff = new CpuParticles3D
		{
			Amount = 6, Lifetime = 0.3f, OneShot = true, Explosiveness = 0.9f, Emitting = false, LocalCoords = false, Spread = 12f, Gravity = Vector3.Zero,
			InitialVelocityMin = 5f, InitialVelocityMax = 9f, ScaleAmountMin = 0.3f, ScaleAmountMax = 0.6f, Direction = Vector3.Right,
			Mesh = new SphereMesh { Radius = 0.18f, Height = 0.36f, RadialSegments = 5, Rings = 2 }, MaterialOverride = _puffMat,
		};
		AddChild(plume); AddChild(puff);
		return (plume, puff);
	}
}
