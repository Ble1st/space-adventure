using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Sicht auf SpaceSim.Core: Die Simulation läuft für alle Sektoren, hier wird nur der Spielersektor
/// als 3D-Szene materialisiert (Spielebene XZ, schräge Perspektivkamera = 2.5D).
/// Steuerung: Pfeile/WASD, oder Maus/Touch gedrückt halten = zum Zeiger fliegen. T = Zeitraffer.
/// </summary>
public partial class Main : Node3D
{
	const float Thrust = 40f, MaxSpeed = 45f, TurnSpeed = 3.5f;
	const float TickInterval = 0.25f;
	const float GateRadius = 10f;
	static readonly Vector3 CamOffset = new(0, 38, 24);

	Universe _u = null!;
	SimulationRunner _sim = null!;
	Ship _playerData = null!;

	Node3D _sectorRoot = null!;
	Node3D _ship = null!;
	Camera3D _cam = null!;
	Label _hud = null!;
	MeshInstance3D _flame = null!;
	readonly List<MeshInstance3D> _gateRings = new();
	readonly List<MeshInstance3D> _asteroids = new();
	List<Obstacle> _obstacles = new();
	float _hitCooldown;
	float _flash;
	readonly List<(GateId id, Vector3 pos)> _gates = new();
	readonly Dictionary<ShipId, Marker> _markers = new();

	Vector3 _vel;
	float _heading;
	float _tickAcc;
	float _jumpCooldown;
	double _time;

	sealed class Marker { public Node3D Pivot = null!; public float Yaw; public Vector3 Offset; }

	public override void _Ready()
	{
		_u = DemoUniverse.Create();
		_sim = new SimulationRunner();
		_playerData = _u.Ships[_u.PlayerShip];

		BuildEnvironment();
		BuildStars();
		BuildShip();
		BuildHud();
		_cam = new Camera3D { Fov = 50f, Far = 1000f };
		AddChild(_cam);
		BuildSector();
		_cam.Position = _ship.Position + CamOffset;
	}

	public override void _Process(double deltaD)
	{
		float delta = (float)deltaD;
		_time += deltaD;
		_jumpCooldown = Mathf.Max(0f, _jumpCooldown - delta);

		// Simulation: feste Zeitschritte, unabhängig von der Framerate
		_tickAcc += delta * (Input.IsKeyPressed(Key.T) ? 10f : 1f);
		int steps = 0;
		while (_tickAcc >= TickInterval && steps < 50) { _sim.Step(_u); _tickAcc -= TickInterval; steps++; }

		UpdateShip(delta);
		CheckGates();

		_cam.Position = _cam.Position.Lerp(_ship.Position + CamOffset, 1f - Mathf.Exp(-6f * delta));
		_cam.LookAt(_cam.Position - CamOffset);

		foreach (var r in _gateRings) r.RotateZ(delta * 0.8f);
		foreach (var a in _asteroids) { a.RotateY(delta * 0.2f); a.RotateX(delta * 0.1f); }
		UpdateShips(delta);
		UpdateHud();
	}

	// ───────── Schiff & Gates ─────────

	void UpdateShip(float delta)
	{
		bool thrusting = false;
		float turn = Input.GetAxis("ui_left", "ui_right");
		if (Input.IsKeyPressed(Key.A)) turn -= 1f;
		if (Input.IsKeyPressed(Key.D)) turn += 1f;
		_heading -= turn * TurnSpeed * delta;
		if (Input.IsActionPressed("ui_up") || Input.IsKeyPressed(Key.W)) thrusting = true;

		if (Input.IsMouseButtonPressed(MouseButton.Left))
		{
			var mp = GetViewport().GetMousePosition();
			var from = _cam.ProjectRayOrigin(mp);
			var dir = _cam.ProjectRayNormal(mp);
			if (Mathf.Abs(dir.Y) > 0.001f)
			{
				var target = from + dir * (-from.Y / dir.Y);
				var d = target - _ship.Position;
				if (d.Length() > 3f)
				{
					_heading = Mathf.LerpAngle(_heading, Mathf.Atan2(-d.X, -d.Z), 1f - Mathf.Exp(-6f * delta));
					thrusting = true;
				}
			}
		}

		var forward = Vector3.Forward.Rotated(Vector3.Up, _heading);
		if (thrusting) _vel += forward * Thrust * delta;
		_vel = _vel.LimitLength(MaxSpeed);
		_vel *= 1f - 0.4f * delta;
		_ship.Position += _vel * delta;
		ResolveCollisions(delta);
		_ship.Rotation = new Vector3(0, _heading, Mathf.Lerp(_ship.Rotation.Z, -turn * 0.4f, 1f - Mathf.Exp(-8f * delta)));
		_flame.Visible = thrusting;
		_flame.Scale = new Vector3(1, 1, 0.8f + GD.Randf() * 0.5f);
	}

	/// <summary>
	/// Spielerschiff gegen Asteroiden, Stationen und NPC-Schiffe (Kreise in der Spielebene): herausschieben,
	/// Geschwindigkeit an der Berührungsnormalen reflektieren, bei hartem Aufprall Hüllenschaden.
	/// </summary>
	void ResolveCollisions(float delta)
	{
		_hitCooldown = Mathf.Max(0f, _hitCooldown - delta);
		_flash = Mathf.Max(0f, _flash - delta);

		var all = new List<Obstacle>(_obstacles);
		foreach (var m in _markers.Values)
			all.Add(new Obstacle(new Vec2(m.Pivot.Position.X, m.Pivot.Position.Z), Collision.ShipRadius));

		var pos = new Vec2(_ship.Position.X, _ship.Position.Z);
		if (!Collision.Resolve(ref pos, Collision.ShipRadius, all, out var n)) return;

		_ship.Position = new Vector3(pos.X, 0, pos.Y);
		var normal = new Vector3(n.X, 0, n.Y);
		float vn = _vel.Dot(normal);
		if (vn >= 0f) return;
		_vel -= normal * (1.4f * vn);   // Rückprall mit 40 % Restenergie
		float impact = -vn;
		if (impact > 4f && _hitCooldown <= 0f)
		{
			_playerData.Hull = Mathf.Max(0f, _playerData.Hull - (impact - 4f) * 0.8f);
			_hitCooldown = 0.4f;
			_flash = 0.6f;
			if (_playerData.Hull <= 0f) Respawn();
		}
	}

	/// <summary>Hülle zerstört: Reparatur und Neustart an der Station des Sektors.</summary>
	void Respawn()
	{
		var sector = _u.Sectors[_u.PlayerSector];
		var spot = sector.Stations.Count > 0 ? _u.Stations[sector.Stations[0]].DockPoint(_playerData.Id) : new Vec2(0, 0);
		_ship.Position = new Vector3(spot.X, 0, spot.Y);
		_vel = Vector3.Zero;
		_playerData.Hull = Ship.MaxHull;
	}

	void CheckGates()
	{
		if (_jumpCooldown > 0f) return;
		foreach (var (id, pos) in _gates)
			if (_ship.Position.DistanceTo(pos) < GateRadius) { Jump(id); return; }
	}

	/// <summary>Sektorwechsel: Daten im Core umhängen, Szene des alten Sektors verwerfen, neue aufbauen.</summary>
	void Jump(GateId gateId)
	{
		var far = _u.Gates[_u.Gates[gateId].Target];
		_u.MoveShip(_playerData, far.Sector);
		_u.PlayerSector = far.Sector;
		BuildSector();

		var gpos = new Vector3(far.Position.X, 0, far.Position.Y);
		var dir = (-gpos).Normalized();
		_ship.Position = gpos + dir * 22f;
		_heading = Mathf.Atan2(-dir.X, -dir.Z);
		_vel = dir * 10f;
		_jumpCooldown = 2f;
		_cam.Position = _ship.Position + CamOffset;
	}

	// ───────── Sektor aus Core-Daten aufbauen ─────────

	void BuildSector()
	{
		_sectorRoot?.QueueFree();
		_sectorRoot = new Node3D { Name = "Sector" };
		AddChild(_sectorRoot);
		_gateRings.Clear(); _asteroids.Clear(); _gates.Clear(); _markers.Clear();

		var sector = _u.Sectors[_u.PlayerSector];
		foreach (var sid in sector.Stations) BuildStation(_u.Stations[sid]);
		foreach (var gid in sector.Gates) BuildGate(_u.Gates[gid]);

		for (int i = 0; i < sector.Asteroids.Count; i++) AddAsteroid(sector.Asteroids[i], i);
		_obstacles = _u.GetObstacles(sector.Id);
	}

	/// <summary>Asteroiden kommen aus den Core-Daten (gleiche Radien wie für Kollision und Ausweichpfade).</summary>
	void AddAsteroid(Obstacle o, int index)
	{
		float g = 0.3f + (index * 37 % 20) / 100f;
		var mesh = new SphereMesh { Radius = o.Radius, Height = o.Radius * 2f, RadialSegments = 8, Rings = 5 };
		var a = Mesh(mesh, Mat(new Color(g + 0.1f, g, g - 0.1f), 0f, 0.1f), new Vector3(o.Position.X, 0, o.Position.Y), _sectorRoot);
		_asteroids.Add(a);
	}

	void BuildStation(Station st)
	{
		var root = new Node3D { Position = new Vector3(st.Position.X, 0, st.Position.Y) };
		_sectorRoot.AddChild(root);
		var metal = Mat(new Color(0.55f, 0.6f, 0.65f), 0f, 0.9f);
		Mesh(new CylinderMesh { TopRadius = 4, BottomRadius = 4, Height = 14 }, metal, Vector3.Zero, root);
		Mesh(new TorusMesh { InnerRadius = 11, OuterRadius = 14 }, Mat(new Color(0.4f, 0.45f, 0.55f), 0f, 0.9f), Vector3.Zero, root);
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.Tau * i / 6f;
			Mesh(new SphereMesh { Radius = 0.6f, Height = 1.2f }, Mat(new Color(1f, 0.8f, 0.3f), 3f),
				new Vector3(Mathf.Cos(a) * 12.5f, 0.8f, Mathf.Sin(a) * 12.5f), root);
		}
		root.AddChild(MakeLabel(st.Name, new Vector3(0, 6, 0)));
	}

	void BuildGate(Gate g)
	{
		var pos = new Vector3(g.Position.X, 0, g.Position.Y);
		var root = new Node3D { Position = pos };
		_sectorRoot.AddChild(root);
		var ring = Mesh(new TorusMesh { InnerRadius = 12, OuterRadius = 14 }, Mat(new Color(0.2f, 0.7f, 1f), 3f), Vector3.Zero, root);
		ring.RotationDegrees = new Vector3(90, 0, 0);
		_gateRings.Add(ring);
		var core = Mat(new Color(0.3f, 0.6f, 1f, 0.25f), 1f);
		core.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		Mesh(new SphereMesh { Radius = 8, Height = 16 }, core, Vector3.Zero, root);
		var target = _u.Sectors[_u.Gates[g.Target].Sector];
		root.AddChild(MakeLabel($"Jumpgate → {target.Name}", new Vector3(0, 8, 0)));
		_gates.Add((g.Id, pos));
	}

	/// <summary>
	/// NPC-Schiffe aus der Core-Materialisierung: erscheinen am Gate, fliegen ihre Teilstrecke
	/// (Position zum gebrochenen Tick) und verschwinden am Ausgangsgate. Stehende Schiffe parken um ihr Ziel.
	/// </summary>
	void UpdateShips(float delta)
	{
		double tick = _u.Tick + _tickAcc / TickInterval;
		var snaps = Materializer.Snapshot(_u, _u.PlayerSector, tick);
		var seen = new HashSet<ShipId>();
		var neighbours = snaps.Select(x => (x.Id, x.Position)).ToList();
		neighbours.Add((Avoidance.Player, new Vec2(_ship.Position.X, _ship.Position.Z)));
		foreach (var s in snaps)
		{
			seen.Add(s.Id);
			var target = new Vector3(s.Position.X, 0, s.Position.Y);
			if (!_markers.TryGetValue(s.Id, out var m))
			{
				var color = s.Role == ShipRole.Miner ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.7f, 0.2f);
				var pivot = new Node3D { Position = target };
				var hull = Mesh(new PrismMesh { Size = new Vector3(2f, 3f, 0.8f) }, Mat(color, 1f), Vector3.Zero, pivot);
				hull.RotationDegrees = new Vector3(-90, 0, 0);
				_sectorRoot.AddChild(pivot);
				_markers[s.Id] = m = new Marker { Pivot = pivot };
			}
			// Ausweichen (nur Darstellung): Schiffe weichen einander und dem Spieler seitlich aus,
			// die Simulation bleibt unberührt. Der Versatz darf nicht in Hindernisse führen.
			var desired = Avoidance.Desired(s.Id, s.Position, neighbours);
			m.Offset = m.Offset.Lerp(new Vector3(desired.X, 0, desired.Y), 1f - Mathf.Exp(-5f * delta));
			var shown = target + m.Offset;
			var shown2 = new Vec2(shown.X, shown.Z);
			Collision.Resolve(ref shown2, Collision.ShipRadius, _obstacles, out _);
			m.Pivot.Position = new Vector3(shown2.X, 0, shown2.Y);
			if (s.Moving)
			{
				float want = Mathf.Atan2(-s.Direction.X, -s.Direction.Y);
				m.Yaw = Mathf.LerpAngle(m.Yaw, want, 1f - Mathf.Exp(-10f * delta));
				m.Pivot.Rotation = new Vector3(0, m.Yaw, 0);
			}
		}
		foreach (var id in _markers.Keys.Where(id => !seen.Contains(id)).ToList())
		{
			_markers[id].Pivot.QueueFree();
			_markers.Remove(id);
		}
	}

	// ───────── HUD ─────────

	void UpdateHud()
	{
		var sector = _u.Sectors[_u.PlayerSector];
		var sb = new StringBuilder();
		sb.AppendLine($"{sector.Name}   Tick {_u.Tick}   Speed {(int)_vel.Length()}   Hülle {(int)_playerData.Hull}%{(_flash > 0f ? "   KOLLISION!" : "")}   [T] Zeitraffer");
		foreach (var sid in sector.Stations)
		{
			var st = _u.Stations[sid];
			sb.Append(st.Name).Append(":");
			foreach (var (gid, e) in st.Market)
				sb.Append($"  {_u.Goods[gid].Name} {(int)e.Amount} @ {e.Price:0.0}");
			sb.AppendLine();
		}
		foreach (var role in new[] { ShipRole.Trader, ShipRole.Miner })
		{
			var all = _u.Ships.Values.Where(s => s.Role == role).ToList();
			sb.AppendLine($"{role}: {all.Count(s => s.Transit != null)} unterwegs / {all.Count}   Credits {(int)all.Sum(s => s.Credits)}");
		}
		foreach (var f in sector.Fields) sb.Append($"Feld {_u.Goods[f.Good].Name}: {(int)f.Amount}   ");
		_hud.Text = sb.ToString();
	}

	void BuildHud()
	{
		var layer = new CanvasLayer();
		AddChild(layer);
		_hud = new Label { Position = new Vector2(16, 12) };
		_hud.AddThemeFontSizeOverride("font_size", 20);
		layer.AddChild(_hud);
	}

	// ───────── Statische Szene ─────────

	void BuildEnvironment()
	{
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.01f, 0.01f, 0.04f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.25f, 0.28f, 0.4f),
			GlowEnabled = true,
		};
		AddChild(new WorldEnvironment { Environment = env });
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), LightEnergy = 1.3f });
	}

	void BuildStars()
	{
		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.5f) },
			InstanceCount = 500,
		};
		var rng = new RandomNumberGenerator { Seed = 42 };
		for (int i = 0; i < 500; i++)
		{
			float s = rng.RandfRange(0.4f, 1.6f);
			var basis = Basis.Identity.Scaled(new Vector3(s, s, s));
			mm.SetInstanceTransform(i, new Transform3D(basis,
				new Vector3(rng.RandfRange(-400, 400), rng.RandfRange(-40, -8), rng.RandfRange(-400, 400))));
		}
		AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = Mat(new Color(0.9f, 0.95f, 1f), 2f) });
	}

	void BuildShip()
	{
		_ship = new Node3D();
		AddChild(_ship);
		var hull = Mesh(new PrismMesh { Size = new Vector3(3f, 5f, 1.2f) }, Mat(new Color(0.7f, 0.75f, 0.85f), 0f, 0.8f), Vector3.Zero, _ship);
		hull.RotationDegrees = new Vector3(-90, 0, 0);
		Mesh(new BoxMesh { Size = new Vector3(5.5f, 0.2f, 1.4f) }, Mat(new Color(0.45f, 0.5f, 0.65f), 0f, 0.8f), new Vector3(0, 0, 1f), _ship);
		Mesh(new SphereMesh { Radius = 0.5f, Height = 1f }, Mat(new Color(0.3f, 0.8f, 1f), 1.5f), new Vector3(0, 0.5f, -0.6f), _ship);
		_flame = Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.6f, Height = 2.5f }, Mat(new Color(1f, 0.55f, 0.15f), 4f), new Vector3(0, 0, 3.6f), _ship);
		_flame.RotationDegrees = new Vector3(90, 0, 0);
	}

	// ───────── Helfer ─────────

	static StandardMaterial3D Mat(Color color, float emissive = 0f, float metallic = 0.3f)
	{
		var m = new StandardMaterial3D { AlbedoColor = color, Metallic = metallic, Roughness = 0.5f };
		if (emissive > 0f)
		{
			m.EmissionEnabled = true;
			m.Emission = color;
			m.EmissionEnergyMultiplier = emissive;
		}
		return m;
	}

	static MeshInstance3D Mesh(Mesh mesh, Material mat, Vector3 pos, Node parent)
	{
		var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
		parent.AddChild(mi);
		return mi;
	}

	static Label3D MakeLabel(string text, Vector3 pos) => new()
	{
		Text = text,
		Position = pos,
		Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
		PixelSize = 0.04f,
	};
}
