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
	/// <summary>NPC-Schiff in der Szene. Runde 11: <see cref="Visual"/> = Schiffsmodell (ShipModels), <see cref="Simple"/> = Grundkörper (ohne Modell oder weit weg).</summary>
	sealed class Marker { public Node3D Pivot = null!; public float Yaw; public Vector3 Offset; public Vector3 Recoil; public ShipVisual? Visual; public Node3D? Simple; public MeshInstance3D? Ring; public string ModelKey = ""; public Color Color; public bool Far; public Vector3? Base; }

	Universe? _u;
	SectorId _sector;
	int _structureVersion = -1;
	Node3D _root = null!;
	readonly List<MeshInstance3D> _gateRings = new();
	sealed class RockNode { public MeshInstance3D Node = null!; public float Scale; public float Spin; public Vector3 Pos; public float Radius; public Color Col; }
	readonly Dictionary<int, RockNode> _rocks = new();
	/// <summary>Runde 43: Zerbrechen eines leer abgebauten Felsens — Trümmer fliegen auseinander und verblassen (~2 s), gepoolt.</summary>
	sealed class Breakup { public float Age; public float Radius; public StandardMaterial3D Mat = null!; public readonly List<MeshInstance3D> Parts = new(); public Vector3[] Vel = System.Array.Empty<Vector3>(); public Vector3[] Spin = System.Array.Empty<Vector3>(); }
	readonly List<Breakup> _breakups = new();
	readonly Stack<MeshInstance3D> _debrisPool = new();
	/// <summary>Dauer des Zerbrechens in Sekunden.</summary>
	public const float BreakupSeconds = 2f;
	/// <summary>Selbsttest: Zahl der gerade zerbrechenden Felsen.</summary>
	public int TestBreakupCount => _breakups.Count;
	readonly Dictionary<ShipId, Marker> _markers = new();
	/// <summary>Phase 26: angezeigte Wracks und Frachtbehälter (Id → Knoten), abgeglichen mit <see cref="Universe.Wrecks"/>.</summary>
	readonly Dictionary<int, Node3D> _wrecks = new();
	/// <summary>Runde 8: Stationsmodell je Station (null = Grundkörper) und der zuletzt angewandte Look (Farbe, Zustand) für <see cref="SyncStations"/>.</summary>
	sealed class StationNode { public Node3D Root = null!; public Node3D? Model; public Color Accent; public StationModels.Look Look; public float Top; public HardpointSet? Guns; }
	readonly Dictionary<StationId, StationNode> _stations = new();

	public List<Obstacle> Obstacles { get; private set; } = new();
	public List<(GateId Id, Vector3 Pos)> Gates { get; } = new();
	public List<PickTarget> Targets { get; } = new();
	/// <summary>NPC-Miner, die laut Simulation gerade abbauen (angezeigte Position), für die Laser-Darstellung.</summary>
	public List<(ShipId Id, Vector3 Pos)> MiningShips { get; } = new();
	public SectorId Sector => _sector;

	/// <summary>Ein Asteroid ist leer abgebaut worden und verschwindet (Ort, Radius) – für Effekte.</summary>
	public event System.Action<Vector3, float>? AsteroidGone;

	/// <summary>True, wenn Sektor gewechselt oder Stationen hinzugekommen/entfernt wurden.</summary>
	public bool NeedsRebuild(Universe u) => _u != u || _sector != u.PlayerSector || _structureVersion != u.StructureVersion;

	/// <summary>Gerätetest Phase 27: alle 3D-Beschriftungen des Sektors (Stationen, Tore, Gürtel, Wracks) — Main blendet verdeckte aus.</summary>
	public List<Label3D> Labels()
	{
		var list = new List<Label3D>();
		void Walk(Node n) { foreach (var c in n.GetChildren()) { if (c is Label3D l) list.Add(l); Walk(c); } }
		if (_root is not null) Walk(_root);
		return list;
	}

	public void Rebuild(Universe u)
	{
		_u = u; _sector = u.PlayerSector; _structureVersion = u.StructureVersion;
		_root?.QueueFree();
		_root = new Node3D { Name = "Sector" };
		AddChild(_root);
		// Runde 43: Trümmer des Zerbrechens hängen am alten _root und werden mit ihm frei — aktive Zerbrechen und Pool zurücksetzen
		foreach (var b in _breakups) if (IsInstanceValid(b.Mat)) b.Mat.Dispose();
		_breakups.Clear();
		_debrisPool.Clear();
		_gateRings.Clear(); _gateNodes.Clear(); _gateStatus.Clear(); _rocks.Clear(); _markers.Clear(); _wrecks.Clear(); _stations.Clear(); Gates.Clear(); Targets.Clear();

		var sector = u.Sectors[_sector];
		foreach (var sid in sector.Stations) BuildStation(u.Stations[sid]);
		foreach (var gid in sector.Gates) BuildGate(u, u.Gates[gid]);
		SyncAsteroids(u, 1f, instant: true);
		foreach (var f in sector.Fields) BuildFieldMarker(u, f);
		BuildPlanet(sector);
		Obstacles = u.GetObstacles(_sector);
	}

	/// <summary>Phase 14: Kulissen-Planet des Sektors (nur Darstellung, keine Kollision); null ohne Planet.</summary>
	public MeshInstance3D? PlanetNode { get; private set; }
	/// <summary>Runde 44: Lage des Planeten relativ zur Kamera-Bodenposition (er folgt der Kamera mit Parallaxe, <see cref="FollowBackdrop"/>).</summary>
	public Vector3 PlanetBase { get; private set; }
	/// <summary>Runde 44: Anteil, mit dem der Planet der Kamera folgt (1 = steht fest im Bild, 0 = steht fest in der Welt).</summary>
	public const float PlanetParallax = 0.92f;

	/// <summary>
	/// Runde 44 (größere Sektoren): der Kulissen-Planet folgt der Kamera fast ganz (Parallaxe) — sonst läge er in einem Sektor von mehreren Kilometern
	/// nur nahe dem Ursprung im Bild.
	/// </summary>
	public void FollowBackdrop(Vector3 cam)
	{
		if (PlanetNode is null) return;
		PlanetNode.Position = PlanetBase + new Vector3(cam.X, 0f, cam.Z) * PlanetParallax;
	}

	/// <summary>
	/// Phase 14: große Kugel weit unterhalb der Spielfläche (Ebene y = 0) als Kulisse — sie liegt nie zwischen Kamera und Schiff, gehört nicht zu den
	/// Hindernissen (Collision/PathPlanner kennen sie nicht) und das Antippen rechnet weiter gegen die Ebene.
	/// </summary>
	void BuildPlanet(Sector sector)
	{
		PlanetNode = null;
		if (sector.Planet is not { } p) return;
		// Himmelskugel: zeigt der Sektorhimmel den Planeten schon, ersetzt er die Kugel (kein doppelter Planet); die Kugel bleibt Rückfall für Welten ohne Himmel
		if (SkyboxView.Resolves(sector.Skybox)) return;
		// Gerätetest Phase 27: der Planet füllte von oben gesehen das halbe Bild (HUD-Texte unlesbar) — jetzt dunkler, weit unten und hinten am Rand
		// des Blickfelds (die Kamera reicht dafür weiter, Main: Far 4000)
		var color = Color.FromHtml(p.Color).Darkened(0.55f);
		var mat = Gfx.Mat(color, 0.05f, 0f);
		mat.Roughness = 1f;
		var mesh = new SphereMesh { Radius = p.Radius, Height = p.Radius * 2f, RadialSegments = 48, Rings = 24 };
		PlanetBase = new Vector3(-p.Radius * 0.8f, -(p.Radius + 1300f), -(p.Radius + 1700f));
		PlanetNode = Gfx.Mesh(mesh, mat, PlanetBase, _root!);
		PlanetNode.Name = "Planet";
		PlanetNode.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
	}

	public void Animate(float delta)
	{
		foreach (var r in _gateRings) r.RotateZ(delta * 0.8f);
		foreach (var r in _rocks.Values) { r.Node.RotateY(delta * r.Spin); r.Node.RotateX(delta * r.Spin * 0.5f); }
		// Runde 43: Trümmer eines zerbrochenen Felsens auseinanderfliegen und verblassen lassen
		for (int i = _breakups.Count - 1; i >= 0; i--)
		{
			var b = _breakups[i];
			b.Age += delta;
			float k = Mathf.Clamp(b.Age / BreakupSeconds, 0f, 1f);
			var col = b.Mat.AlbedoColor; col.A = 1f - k; b.Mat.AlbedoColor = col;
			for (int p = 0; p < b.Parts.Count; p++)
			{
				var n = b.Parts[p];
				if (!IsInstanceValid(n)) continue;
				n.Position += b.Vel[p] * delta;
				b.Vel[p] += new Vector3(0f, -2.5f * delta, 0f);   // leichter Zug nach unten
				n.RotateX(b.Spin[p].X * delta); n.RotateY(b.Spin[p].Y * delta); n.RotateZ(b.Spin[p].Z * delta);
				n.Scale = Vector3.One * (1f - k * 0.85f);
			}
			if (k >= 1f)
			{
				foreach (var n in b.Parts) { if (IsInstanceValid(n)) { n.Visible = false; _debrisPool.Push(n); } }
				b.Parts.Clear();
				b.Mat.Dispose();
				_breakups.RemoveAt(i);
			}
		}
	}

	/// <summary>Runde 43: ein Fels ist leer abgebaut (AsteroidDepleted) — er zerbricht sichtbar: Trümmer fliegen auseinander und verblassen ~2 s.</summary>
	void SpawnBreakup(Vector3 pos, float radius, Color col)
	{
		var b = new Breakup { Age = 0f, Radius = radius };
		b.Mat = Gfx.Mat(col, 0.15f, 0.2f);
		b.Mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		int count = Mathf.Clamp((int)(radius * 2.2f), 6, 16);
		var rng = new RandomNumberGenerator { Seed = (ulong)(pos.X * 73856093f) ^ (ulong)(pos.Z * 19349663f) ^ (ulong)radius * 83492791UL };
		float size = Mathf.Max(0.35f, radius * 0.32f);
		for (int p = 0; p < count; p++)
		{
			MeshInstance3D n = new() { Mesh = new BoxMesh() };
			while (_debrisPool.Count > 0)
			{
				var c = _debrisPool.Pop();
				if (IsInstanceValid(c)) { n = c; break; }
			}
			if (n.Mesh is not BoxMesh) n.Mesh = new BoxMesh();
			((BoxMesh)n.Mesh).Size = new Vector3(size * rng.RandfRange(0.5f, 1.2f), size * rng.RandfRange(0.4f, 0.9f), size * rng.RandfRange(0.5f, 1.2f));
			n.MaterialOverride = b.Mat;
			n.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			float ang = rng.RandfRange(0f, Mathf.Tau);
			float rr = rng.RandfRange(0f, radius * 0.7f);
			n.Position = pos + new Vector3(Mathf.Cos(ang) * rr, rng.RandfRange(-0.3f, 0.3f) * radius, Mathf.Sin(ang) * rr);
			n.Rotation = new Vector3(rng.RandfRange(0f, 3f), rng.RandfRange(0f, 3f), rng.RandfRange(0f, 3f));
			n.Scale = Vector3.One;
			n.Visible = true;
			if (n.GetParent() is null) _root.AddChild(n);
			b.Parts.Add(n);
			float speed = rng.RandfRange(2.5f, 2.5f + radius * 1.1f);
			System.Array.Resize(ref b.Vel, p + 1);
			System.Array.Resize(ref b.Spin, p + 1);
			b.Vel[p] = new Vector3(Mathf.Cos(ang), rng.RandfRange(0.4f, 1.2f), Mathf.Sin(ang)) * speed;
			b.Spin[p] = new Vector3(rng.RandfRange(-4f, 4f), rng.RandfRange(-4f, 4f), rng.RandfRange(-4f, 4f));
		}
		_breakups.Add(b);
	}

	/// <summary>
	/// Gleicht die Asteroiden-Szene mit den Core-Daten ab: neue (Respawn) wachsen ein, abgebaute behalten ihren Radius (Runde 43: kein Schrumpfen),
	/// leere zerbrechen sichtbar (Trümmer) und verschwinden. Aktualisiert außerdem die Kollisionskreise.
	/// </summary>
	public void SyncAsteroids(Universe u, float delta, bool instant = false)
	{
		var sector = u.Sectors[_sector];
		var live = new HashSet<int>();
		foreach (var a in sector.Asteroids)
		{
			live.Add(a.Id);
			var minable = a.Minable;
			var color = minable && u.AsteroidClasses.TryGetValue(a.ClassKey, out var cls) ? Color.FromHtml(cls.Color) : new Color(0.4f, 0.4f, 0.42f);
			if (!_rocks.TryGetValue(a.Id, out var rock))
			{
				var mat = Gfx.Mat(color, minable ? 0.5f : 0f, 0.1f);
				var mesh = new SphereMesh { Radius = a.BaseRadius, Height = a.BaseRadius * 2f, RadialSegments = 8, Rings = 5 };
				var node = Gfx.Mesh(mesh, mat, new Vector3(a.Position.X, 0, a.Position.Y), _root);
				var rng = new RandomNumberGenerator { Seed = (ulong)a.Id * 2654435761UL };
				node.Rotation = new Vector3(rng.Randf() * Mathf.Tau, rng.Randf() * Mathf.Tau, 0);
				rock = new RockNode { Node = node, Scale = instant ? a.Radius / a.BaseRadius : 0.05f, Spin = rng.RandfRange(0.1f, 0.3f), Pos = node.Position, Radius = a.Radius, Col = color };
				node.Scale = Vector3.One * rock.Scale;
				_rocks[a.Id] = rock;
			}
			// Runde 43: Asteroiden schrumpfen nicht mehr — die Skalierung bleibt 1 (Restvorrat steckt in abfallenden Brocken, nicht im Radius)
			float target = a.Radius / a.BaseRadius;
			rock.Scale = instant ? target : Mathf.Lerp(rock.Scale, target, 1f - Mathf.Exp(-4f * delta));
			rock.Node.Scale = Vector3.One * rock.Scale;
			rock.Radius = a.Radius;
			rock.Col = color;
		}
		foreach (var id in _rocks.Keys.Where(id => !live.Contains(id)).ToList())
		{
			var r = _rocks[id];
			AsteroidGone?.Invoke(r.Pos, r.Radius);
			SpawnBreakup(r.Pos, r.Radius, r.Col);   // Runde 43: leer abgebaut → zerbricht sichtbar
			r.Node.QueueFree();
			_rocks.Remove(id);
		}
		Obstacles = u.GetObstacles(_sector);
	}

	/// <summary>Angezeigte Position eines NPC-Schiffs; null, wenn es nicht im Sektor ist.</summary>
	public Vector3? ShipPosition(ShipId id) => _markers.TryGetValue(id, out var m) ? m.Pivot.Position : null;

	/// <summary>Phase 25: angezeigte NPC-Schiffe mit Position (Antippen als Ziel).</summary>
	public IEnumerable<(ShipId Id, Vec2 Pos)> ShipMarkers() => _markers.Select(kv => (kv.Key, new Vec2(kv.Value.Pivot.Position.X, kv.Value.Pivot.Position.Z)));

	/// <summary>Rückstoß eines abgefeuerten Lasers: Das Schiff wird kurz weggestoßen und federt zurück (nur Darstellung).</summary>
	public void Kick(ShipId id, Vector3 impulse) { if (_markers.TryGetValue(id, out var m)) m.Recoil += impulse; }

	/// <summary>Aktuelle (angezeigte) Positionen der NPC-Schiffe als Kollisionskreise.</summary>
	public IEnumerable<Obstacle> ShipBodies() =>
		_markers.Values.Select(m => new Obstacle(new Vec2(m.Pivot.Position.X, m.Pivot.Position.Z), Collision.ShipRadius));

	/// <summary>
	/// NPC-Schiffe aus der Core-Materialisierung: erscheinen am Gate, fliegen ihre Teilstrecke und verschwinden am Ausgangsgate.
	/// Ausweichen (Schiff↔Schiff, Schiff↔Spieler) ist reine Darstellung und verändert die Simulation nicht.
	/// </summary>
	public void UpdateShips(Universe u, double tick, Vector3? playerPos, float delta, Vec2? playerHeading = null)
	{
		var snaps = Materializer.Snapshot(u, _sector, tick, playerHeading);   // Runde 12: Begleiter des Spielers in Formation
		var neighbours = snaps.Select(x => (x.Id, x.Position)).ToList();
		if (playerPos is { } pp) neighbours.Add((Avoidance.Player, new Vec2(pp.X, pp.Z)));
		var seen = new HashSet<ShipId>();
		MiningShips.Clear();

		foreach (var s in snaps)
		{
			seen.Add(s.Id);
			var target = new Vector3(s.Position.X, 0, s.Position.Y);
			u.Ships.TryGetValue(s.Id, out var ship);
			float collideR = Collision.ShipRadius;
			Vec2? faceRock = null;
			bool contact = false;
			if (s.Mining && ship is not null)
			{
				// Runde 43: abbauender NPC-Schürfer steht mit der Bohrerspitze an der Felsoberfläche, ausgerichtet auf den Fels
				// (die Simulation hält die Schiffsmitte am Kontaktpunkt, die Ansicht setzt das Modell mit seiner Werkzeugspitze auf die Oberfläche)
				var rock = u.Sectors[_sector].Asteroids.FirstOrDefault(a => a.Id == (ship.AnchorRock != 0 ? ship.AnchorRock : ship.MineAsteroid));
				var toR = rock is null ? default : rock.Position - s.Position;
				if (rock is not null && toR.Length > 1e-4f)
				{
					faceRock = toR;
					// nur im Kontakt laut Core (der Anflug zum Fels zeigt die echte Position — die Ansicht erfindet keinen Kontakt)
					contact = ship.Stats.UsesContact && SpaceSim.Core.Mining.ContactMining.InContact(s.Position, toR.Normalized(), rock,
						SpaceSim.Core.Mining.ContactMining.ToolReach(ship, u));
					if (contact)
					{
						var dn = (s.Position - rock.Position).Normalized();
						float tip = ShipModels.TipOffsetOf(ship.ModelKey);
						var at = rock.Position + dn * (rock.Radius + tip);
						target = new Vector3(at.X, 0f, at.Y);
						collideR = Mathf.Min(Collision.ShipRadius, tip);
					}
				}
			}
			if (!_markers.TryGetValue(s.Id, out var m))
			{
				// Phase 11: Polizei blau, Kaperschiffe rot
				var color = s.Role switch
				{
					ShipRole.Miner => new Color(0.4f, 1f, 0.4f),
					ShipRole.Police => new Color(0.35f, 0.55f, 1f),
					ShipRole.Pirate => new Color(1f, 0.25f, 0.25f),
					_ => new Color(1f, 0.7f, 0.2f),
				};
				var pivot = new Node3D { Position = target };
				// Gerätetest: flacher, leuchtender Ring unter jedem fremden Schiff — auch bei weiter Kamera gut sichtbar (vorher nur kleine Pfeile)
				var ring = Gfx.Mesh(new TorusMesh { InnerRadius = 2.6f, OuterRadius = 3.1f, Rings = 24, RingSegments = 6 }, Gfx.Mat(color, 1.2f, 0f), new Vector3(0, -0.3f, 0), pivot);
				ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
				_root.AddChild(pivot);
				_markers[s.Id] = m = new Marker { Pivot = pivot, Color = color, Ring = ring };
			}
			SyncShipBody(u, m, ship, playerPos);

			var desired = Avoidance.Desired(s.Id, s.Position, neighbours);
			m.Offset = m.Offset.Lerp(new Vector3(desired.X, 0, desired.Y), 1f - Mathf.Exp(-5f * delta));
			m.Recoil = m.Recoil.Lerp(Vector3.Zero, 1f - Mathf.Exp(-6f * delta));
			// Runde 43: der Anflug an den Fels läuft im Core in Schritten (Tempo je Tick) — Ansicht glättet nur die Anzeige dieser kurzen Strecke
			if (s.Mining && !s.Moving && m.Base is { } prev && prev.DistanceTo(target) < 60f) target = prev.Lerp(target, 1f - Mathf.Exp(-8f * delta));
			m.Base = target;
			var shown = target + m.Offset + m.Recoil;
			var shown2 = new Vec2(shown.X, shown.Z);
			Collision.Resolve(ref shown2, collideR, Obstacles, out _);   // Ausweichen führt nie in Hindernisse
			m.Pivot.Position = new Vector3(shown2.X, 0, shown2.Y);
			if (s.Mining) MiningShips.Add((s.Id, m.Pivot.Position));
			if (s.Moving)
			{
				m.Yaw = Mathf.LerpAngle(m.Yaw, Mathf.Atan2(-s.Direction.X, -s.Direction.Y), 1f - Mathf.Exp(-10f * delta));
				m.Pivot.Rotation = new Vector3(0, m.Yaw, 0);
			}
			else if (faceRock is { } fr)   // Runde 43: abbauender NPC blickt zum Fels
			{
				var fd = fr.Normalized();
				m.Yaw = Mathf.LerpAngle(m.Yaw, Mathf.Atan2(-fd.X, -fd.Y), 1f - Mathf.Exp(-10f * delta));
				m.Pivot.Rotation = new Vector3(0, m.Yaw, 0);
			}
			if (m.Visual is { } vis && !m.Far)
			{
				// Runde 11: Schub nach Bewegung, Werkzeuge drehen bei Kontaktabbau (ContactMining.Spin), Türme zum Gefechtsziel
				vis.SetEngine(s.Moving ? 1f : 0.15f);
				if (ship is not null)
				{
					vis.Spin(SpaceSim.Core.Mining.ContactMining.Spin(ship.Stats, s.Mining && (contact || !ship.Stats.UsesContact) ? SpaceSim.Core.Mining.ContactState.Mining : SpaceSim.Core.Mining.ContactState.NoContact), delta);
					if (vis.Hardpoints.TurretCount > 0 && u.Fights.IsEngaged(s.Id) && CombatTarget(u, s.Id) is { } aim) vis.Hardpoints.AimAll(aim, delta);   // Runde 13: nur im Gefecht
					vis.AnimateJaws(delta);
				}
			}
		}
		foreach (var id in _markers.Keys.Where(id => !seen.Contains(id)).ToList())
		{
			_markers[id].Pivot.QueueFree();
			_markers.Remove(id);
		}
	}

	/// <summary>
	/// Runde 11: Schiffskörper eines Markers — Modell aus <see cref="ShipModels"/> (Akzent in der Besitzerfarbe), weit weg oder ohne Modell der alte Grundkörper
	/// (Distanz-LOD für das Handy, mit Hysterese). Wechselt das Modell (Umbau), wird neu instanziert.
	/// </summary>
	void SyncShipBody(Universe u, Marker m, Ship? ship, Vector3? camera)
	{
		string key = ship?.ModelKey ?? "";
		if (key != m.ModelKey || (m.Visual is null && m.Simple is null))
		{
			m.ModelKey = key;
			m.Visual?.Root.QueueFree(); m.Visual = null;
			if (ship is not null && ShipModels.Create(key, ShipModels.AccentOf(u, ship.Owner)) is { } v) { m.Visual = v; m.Pivot.AddChild(v.Root); }
			if (m.Simple is null)
			{
				var hull = Gfx.Mesh(new PrismMesh { Size = new Vector3(2f, 3f, 0.8f) }, Gfx.Mat(m.Color, 1f), Vector3.Zero, m.Pivot);
				hull.RotationDegrees = new Vector3(-90, 0, 0);
				m.Simple = hull;
			}
		}
		if (m.Visual is not null && ship is not null) m.Visual.SetAccent(ShipModels.AccentOf(u, ship.Owner));
		float dist = camera is { } c ? (m.Pivot.Position - c).Length() : 0f;
		m.Far = m.Far ? dist > ShipModels.LodDistance * 0.9f : dist > ShipModels.LodDistance;
		bool model = m.Visual is not null && !m.Far;
		if (m.Visual is not null) m.Visual.Root.Visible = model;
		if (m.Simple is not null) m.Simple.Visible = !model;
	}

	/// <summary>Runde 11: Weltposition des Gefechtsziels eines Schiffs (Schiff oder Station), nur lesend aus dem Core.</summary>
	Vector3? CombatTarget(Universe u, ShipId id)
	{
		if (u.Fights.Of(id) is not { } e || e.Of(id) is not { Active: true } p) return null;
		if (p.Target.Value != 0) return p.Target == u.PlayerShip ? PlayerPosition : ShipPosition(p.Target);
		if (SpaceSim.Core.Combat.SiegeTargets.Resolve(u, p.TargetSiege) is { } st) return new Vector3(st.Position.X, 0, st.Position.Y);   // Phase 31: Station oder Tor
		return null;
	}

	/// <summary>Position des Spielerschiffs (setzt Main je Bild), für Turmziele.</summary>
	public Vector3? PlayerPosition { get; set; }

	/// <summary>Runde 11: Mündung des Slots <paramref name="slot"/> eines NPC-Schiffs (Welt); ohne Modell/Stellung die Schiffsposition.</summary>
	public Vector3? Muzzle(ShipId id, int slot)
	{
		if (!_markers.TryGetValue(id, out var m)) return null;
		if (slot >= 0 && m.Visual is { } v && !m.Far && v.Hardpoints.Muzzle(slot) is { } p) return p;
		return m.Pivot.Position;
	}

	/// <summary>Runde 11: Schussrichtung der Stellung (Raketenschächte zeigen nach oben); null ohne Stellung.</summary>
	public Vector3? MuzzleDirection(ShipId id, int slot) =>
		_markers.TryGetValue(id, out var m) && slot >= 0 && m.Visual is { } v && !m.Far ? v.Hardpoints.Direction(slot) : null;

	/// <summary>Runde 11: Mündung des Geschützes <paramref name="gun"/> einer Station (Welt); ohne Stellung die Stationsmitte.</summary>
	public Vector3? StationMuzzle(StationId id, int gun)
	{
		if (!_stations.TryGetValue(id, out var n)) return null;
		if (gun >= 0 && n.Guns?.Muzzle(gun) is { } p) return p;
		return n.Root.Position;
	}

	/// <summary>Runde 11: Schussrichtung einer Stationsstellung; null ohne Stellung.</summary>
	public Vector3? StationMuzzleDirection(StationId id, int gun) => _stations.TryGetValue(id, out var n) && gun >= 0 ? n.Guns?.Direction(gun) : null;

	/// <summary>Runde 11: Stationstürme zum Gefechtsziel drehen (je Bild; nur Stationen mit Türmen).</summary>
	public void AimStationGuns(Universe u, float delta)
	{
		if (!u.Fights.Encounters.Any(e => e.Sector == _sector)) return;   // Runde 13 (FPS): ohne Gefecht im Sektor nichts zu tun
		foreach (var (id, n) in _stations)
		{
			if (n.Guns is not { TurretCount: > 0 } guns) continue;
			Vector3? aim = null;
			foreach (var e in u.Fights.Encounters)
			{
				if (e.Sector != _sector) continue;
				foreach (var p in e.Participants)
				{
					if (!p.Active || !u.Ships.TryGetValue(p.Ship, out var ship) || !u.Stations.TryGetValue(id, out var st) || !SpaceSim.Core.Combat.StationCombat.Hostile(u, st, e, p, ship)) continue;
					aim = p.Ship == u.PlayerShip ? PlayerPosition : ShipPosition(p.Ship);
					if (aim is not null) break;
				}
				if (aim is not null) break;
			}
			if (aim is { } a) guns.AimAll(a, delta);
		}
	}

	/// <summary>Runde 12: Spaltbacken eines NPC-Schiffs bewegen (Ereignis AsteroidSplit).</summary>
	public void SnapJaws(ShipId id) { if (_markers.TryGetValue(id, out var m)) m.Visual?.SnapJaws(); }

	/// <summary>Selbsttest: Schiffsmodell eines Markers (null = Grundkörper).</summary>
	/// <summary>Runde 17 (Prüfung: Ringe rechts über den Laser-/Geschütztasten): Ringe von Schiffen unter HUD-Flächen ausblenden.</summary>
	public void HideRingsUnder(System.Func<Vector3, bool> covered)
	{
		foreach (var m in _markers.Values) if (m.Ring is { } r) r.Visible = !covered(m.Pivot.GlobalPosition);
	}
	/// <summary>Selbsttest: sichtbare Ringe.</summary>
	public int TestVisibleRings => _markers.Values.Count(m => m.Ring is { Visible: true });
	public ShipVisual? TestVisual(ShipId id) => _markers.TryGetValue(id, out var m) ? m.Visual : null;
	/// <summary>Phase 32: Körper eines NPC-Markers (Modell, sonst der Drehpunkt) für die Ankertrossen; null = nicht im Bild.</summary>
	public (ShipVisual? Visual, Node3D Body)? BodyOf(ShipId id) =>
		_markers.TryGetValue(id, out var m) ? (m.Visual is { } v && v.Root.Visible ? v : null, m.Pivot) : null;
	/// <summary>Phase 32: verankerte NPC-Schürfer drehen ihren Marker zum Fels (Darstellung; die Simulation kennt nur den Arbeitspunkt).</summary>
	public void FaceTo(ShipId id, Vector3 world, float delta)
	{
		if (!_markers.TryGetValue(id, out var m)) return;
		var d = world - m.Pivot.Position;
		if (d.LengthSquared() < 1e-4f) return;
		m.Yaw = Mathf.LerpAngle(m.Yaw, Mathf.Atan2(-d.X, -d.Z), 1f - Mathf.Exp(-6f * delta));
		m.Pivot.Rotation = new Vector3(0, m.Yaw, 0);
	}
	/// <summary>Selbsttest: Geschützstellungen einer Station.</summary>
	public HardpointSet? TestStationGuns(StationId id) => _stations.TryGetValue(id, out var n) ? n.Guns : null;
	/// <summary>Selbsttest: Zahl der Marker mit Modell bzw. mit Grundkörper (LOD).</summary>
	public (int Models, int Simple) TestBodies() => (_markers.Values.Count(m => m.Visual is not null && !m.Far), _markers.Values.Count(m => m.Visual is null || m.Far));

	// ───────── Aufbau aus Core-Daten ─────────

	/// <summary>Runde 8: Farbe des Akzents einer Station — Fraktionsfarbe des Besitzers, Gesetzlose rot, sonst (herrenlos, frei) grau.</summary>
	public static Color AccentOf(Universe u, Station st)
	{
		if (u.FactionOf(st) is { } f) return Ui.MapOverlay.FactionColor(f);
		if (u.Actors.TryGetValue(st.Owner, out var a) && a.Kind == ActorKind.Outlaw) return new Color(1f, 0.28f, 0.22f);
		return new Color(0.62f, 0.64f, 0.68f);
	}

	static StationModels.Look LookOf(Universe u, Station st) =>
		st.IsBuildSite && st.Site!.Abandoned ? StationModels.Look.Abandoned
		: SpaceSim.Core.Combat.StationCombat.IsDown(u, st) ? StationModels.Look.Disabled : StationModels.Look.Normal;

	/// <summary>Runde 8: Selbsttest — Stationen mit Modell (Id → Modellknoten, null = Grundkörper) und Oberkante.</summary>
	public IEnumerable<(StationId Id, Node3D? Model, float Top)> StationModelNodes() => _stations.Select(kv => (kv.Key, kv.Value.Model, kv.Value.Top));

	/// <summary>
	/// Runde 8: Besitzerwechsel, Ausschalten und Wiedereinschalten ändern keine Struktur (kein Neuaufbau) — die Einfärbung folgt hier je Bild (billig: nur
	/// ein Vergleich je Station, neu eingefärbt wird nur bei einer Änderung).
	/// </summary>
	public void SyncStations(Universe u)
	{
		foreach (var (id, n) in _stations)
		{
			if (n.Model is null || !u.Stations.TryGetValue(id, out var st)) continue;
			var accent = AccentOf(u, st); var look = LookOf(u, st);
			if (accent == n.Accent && look == n.Look) continue;
			n.Accent = accent; n.Look = look;
			StationModels.Apply(n.Model, accent, look);
		}
		SyncGates(u);   // Jumpgate-Modelle: Zustand und Sektorfarbe
	}

	void BuildStation(Station st)
	{
		var pos = new Vector3(st.Position.X, 0, st.Position.Y);
		var root = new Node3D { Position = pos, Name = $"Station{st.Id.Value}" };
		_root.AddChild(root);
		// Runde 8: Modell des Typs (Baustellen: build_site), Radius in der Ebene = Kollisionskreis (Station.BodyRadius = Obstacle.Radius)
		var u = _u!;
		string key = st.IsBuildSite ? "build_site" : st.TypeKey;
		var accent = AccentOf(u, st); var look = LookOf(u, st);
		if (StationModels.Create(key, st.Radius, accent, look, out float top, out _) is { } model)   // Phase 30: Maßstab = Radius der Größenklasse
		{
			root.AddChild(model);
			_stations[st.Id] = new StationNode { Root = root, Model = model, Accent = accent, Look = look, Top = top, Guns = HardpointSet.Bind(model, ShipModels.StationGuns(key)) };
			string label = st.IsBuildSite && !st.Site!.Abandoned
				? $"Baustelle: {(u.StationTypes.TryGetValue(st.Site.TypeKey, out var tt) ? tt.Name : st.Site.TypeKey)}" : st.Name;
			root.AddChild(Gfx.Label(ActorNames.Tagged(u, st.Owner, label), new Vector3(0, top + 2.5f, 0)));   // Phase 29: „[UNI] Werft Vesta“
			Targets.Add(new PickTarget("station", st.Id.Value, pos, st.Name));
			return;
		}
		_stations[st.Id] = new StationNode { Root = root, Top = 6f };
		if (st.IsBuildSite) { BuildSiteBody(st, root, pos); return; }
		Gfx.Mesh(new CylinderMesh { TopRadius = 4, BottomRadius = 4, Height = 14 }, Gfx.Mat(new Color(0.55f, 0.6f, 0.65f), 0f, 0.9f), Vector3.Zero, root);
		Gfx.Mesh(new TorusMesh { InnerRadius = 11, OuterRadius = 14 }, Gfx.Mat(new Color(0.4f, 0.45f, 0.55f), 0f, 0.9f), Vector3.Zero, root);
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.Tau * i / 6f;
			Gfx.Mesh(new SphereMesh { Radius = 0.6f, Height = 1.2f }, Gfx.Mat(new Color(1f, 0.8f, 0.3f), 3f),
				new Vector3(Mathf.Cos(a) * 12.5f, 0.8f, Mathf.Sin(a) * 12.5f), root);
		}
		root.AddChild(Gfx.Label(_u is { } uu ? ActorNames.Tagged(uu, st.Owner, st.Name) : st.Name, new Vector3(0, 6, 0)));
		Targets.Add(new PickTarget("station", st.Id.Value, pos, st.Name));
	}

	/// <summary>
	/// Phase 8: Baustelle — Gerüst statt Station (orangefarbener Ring, vier Streben, Plattform), gleicher Kollisionskörper und Andockpunkt wie
	/// eine Station; das Etikett nennt den Zieltyp.
	/// </summary>
	void BuildSiteBody(Station st, Node3D root, Vector3 pos)
	{
		bool abandoned = st.Site!.Abandoned;
		var steel = Gfx.Mat(abandoned ? new Color(0.45f, 0.42f, 0.38f) : new Color(1f, 0.62f, 0.2f), abandoned ? 0f : 1.2f, 0.8f);
		Gfx.Mesh(new TorusMesh { InnerRadius = 12, OuterRadius = 13.2f }, steel, Vector3.Zero, root);
		Gfx.Mesh(new CylinderMesh { TopRadius = 6, BottomRadius = 6, Height = 1.2f }, Gfx.Mat(new Color(0.35f, 0.37f, 0.42f), 0f, 0.9f), new Vector3(0, -2, 0), root);
		for (int i = 0; i < 4; i++)
		{
			float a = Mathf.Tau * i / 4f + 0.4f;
			Gfx.Mesh(new BoxMesh { Size = new Vector3(0.8f, 10f, 0.8f) }, steel, new Vector3(Mathf.Cos(a) * 8f, 2f, Mathf.Sin(a) * 8f), root);
		}
		string type = _u?.StationTypes.TryGetValue(st.Site.TypeKey, out var t) == true ? t!.Name : st.Site.TypeKey;
		root.AddChild(Gfx.Label(abandoned ? st.Name : $"Baustelle: {type}", new Vector3(0, 7, 0)));
		Targets.Add(new PickTarget("station", st.Id.Value, pos, st.Name));
	}

	void BuildGate(Universe u, Gate g)
	{
		var pos = new Vector3(g.Position.X, 0, g.Position.Y);
		var root = new Node3D { Position = pos };
		_root.AddChild(root);
		float labelY = 8f;
		if (BuildGateModel(u, g, root) is { } top) labelY = top + 2.5f;   // Tormodell (GateModels), sonst der alte Ring
		else
		{
			var ring = Gfx.Mesh(new TorusMesh { InnerRadius = 12, OuterRadius = 14 }, Gfx.Mat(new Color(0.2f, 0.7f, 1f), 3f), Vector3.Zero, root);
			ring.RotationDegrees = new Vector3(90, 0, 0);
			_gateRings.Add(ring);
			var core = Gfx.Mat(new Color(0.3f, 0.6f, 1f, 0.25f), 1f);
			core.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			Gfx.Mesh(new SphereMesh { Radius = 8, Height = 16 }, core, Vector3.Zero, root);
		}
		var target = u.Sectors[u.Gates[g.Target].Sector];
		// Runde 11: Schild wie die Stationen (feste Bildschirmgröße, HUD-Ausblendung über Labels()), mit Kürzel des Besitzers des Zielsektors
		string targetName = target.Owner.Value != 0 ? ActorNames.Tagged(u, target.Owner, target.Name) : target.Name;
		// Runde 17 (Handy-Test: „Jumpgate → …“ vom Tormodell angeschnitten): Torschilder immer vor dem Modell (ohne Tiefentest, höhere Zeichenreihenfolge)
		var name = Gfx.Label($"Jumpgate → {targetName}", new Vector3(0, labelY, 0));
		name.NoDepthTest = true; name.RenderPriority = 10;
		root.AddChild(name);
		// Phase 31: Zustandsschild unter dem Namen (beschädigt / außer Betrieb mit Reparaturfortschritt), sonst ausgeblendet
		var status = Gfx.Label("", new Vector3(0, labelY - 3f, 0));
		status.NoDepthTest = true; status.RenderPriority = 10;
		status.Visible = false;
		root.AddChild(status);
		_gateStatus[g.Id] = status;
		Gates.Add((g.Id, pos));
		Targets.Add(new PickTarget("gate", g.Id.Value, pos, $"Gate → {target.Name}"));
	}

	/// <summary>Phase 26: Zahl der angezeigten Wracks/Behälter (Selbsttest).</summary>
	public int WreckCount => _wrecks.Count;

	/// <summary>
	/// Phase 26: gleicht Wracks (dunkle, schräg liegende Trümmer mit Beschriftung) und Frachtbehälter (kleine orange Kisten) mit den Core-Daten des Sektors ab.
	/// Keine Kollision (kein Hindernis), nur Darstellung.
	/// </summary>
	public void SyncWrecks(Universe u)
	{
		var live = new HashSet<int>();
		foreach (var w in u.Wrecks)
		{
			if (w.Sector != _sector) continue;
			// Gerätetest Phase 28: ein Wrack, aus dem nichts mehr zu holen ist (nur Schrott unter der Bergungsgrenze), wird nicht mehr gezeichnet — die Bucht sagt dann
			// „kein Wrack“, das Bild zeigte es weiter bis zum Ablauf der Frist
			bool useful = w.Cargo.Values.Any(v => v > 0.001f) || w.Modules.Exists(m => m.Condition >= u.Rules.SalvageMinCondition);
			if (!useful) continue;
			live.Add(w.Id);
			if (_wrecks.ContainsKey(w.Id)) continue;
			bool container = w.Kind == WreckKind.Container;
			var node = new Node3D { Name = $"Wreck{w.Id}", Position = new Vector3(w.Position.X, 0, w.Position.Y) };
			_root.AddChild(node);
			var mat = Gfx.Mat(container ? new Color(1f, 0.6f, 0.15f) : new Color(0.35f, 0.33f, 0.3f), container ? 0.6f : 0.05f, 0.4f);
			if (container) Gfx.Mesh(new BoxMesh { Size = new Vector3(1.4f, 1.4f, 1.4f) }, mat, Vector3.Zero, node);
			else
			{
				Gfx.Mesh(new BoxMesh { Size = new Vector3(3.2f, 0.8f, 1.4f) }, mat, Vector3.Zero, node).Rotation = new Vector3(0.3f, w.Id * 0.7f, 0.2f);
				Gfx.Mesh(new BoxMesh { Size = new Vector3(1.4f, 0.6f, 2.2f) }, mat, new Vector3(1.6f, 0.2f, 0.8f), node).Rotation = new Vector3(-0.4f, w.Id * 1.3f, 0f);
			}
			node.AddChild(Gfx.Label(container ? "Frachtbehälter" : "Wrack", new Vector3(0, 3, 0)));
			_wrecks[w.Id] = node;
		}
		foreach (var id in _wrecks.Keys.Where(id => !live.Contains(id)).ToList())
		{
			_wrecks[id].QueueFree();
			_wrecks.Remove(id);
		}
	}

	void BuildFieldMarker(Universe u, ResourceField f)
	{
		var label = Gfx.Label(u.AsteroidClasses.TryGetValue(f.ClassKey, out var cls) ? $"{cls.Name}-Gürtel" : "Rohstoffgürtel", new Vector3(f.Position.X, 4, f.Position.Y));
		_root.AddChild(label);
	}
}
