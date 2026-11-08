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
	readonly List<(GateId id, Vector3 pos)> _gates = new();
	readonly Dictionary<ShipId, Marker> _markers = new();

	Vector3 _vel;
	float _heading;
	float _tickAcc;
	float _jumpCooldown;
	double _time;

	sealed class Marker { public Node3D Node = null!; public Vector3 Center; public float Angle, Radius; }

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
		if (steps > 0) SyncMarkers();

		UpdateShip(delta);
		CheckGates();

		_cam.Position = _cam.Position.Lerp(_ship.Position + CamOffset, 1f - Mathf.Exp(-6f * delta));
		_cam.LookAt(_cam.Position - CamOffset);

		foreach (var r in _gateRings) r.RotateZ(delta * 0.8f);
		foreach (var a in _asteroids) { a.RotateY(delta * 0.2f); a.RotateX(delta * 0.1f); }
		foreach (var m in _markers.Values)
		{
			float ang = m.Angle + (float)_time * 0.3f;
			m.Node.Position = m.Center + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * m.Radius;
		}
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
		_ship.Rotation = new Vector3(0, _heading, Mathf.Lerp(_ship.Rotation.Z, -turn * 0.4f, 1f - Mathf.Exp(-8f * delta)));
		_flame.Visible = thrusting;
		_flame.Scale = new Vector3(1, 1, 0.8f + GD.Randf() * 0.5f);
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

		var rng = new RandomNumberGenerator { Seed = (ulong)sector.Id.Value * 7919UL };
		foreach (var f in sector.Fields)
			for (int i = 0; i < 14; i++)
				AddAsteroid(rng, new Vector3(f.Position.X, 0, f.Position.Y) + RandomOffset(rng, 25f));
		for (int i = 0; i < 25; i++)
		{
			var p = new Vector3(rng.RandfRange(-160, 160), 0, rng.RandfRange(-160, 160));
			if (p.Length() > 30f) AddAsteroid(rng, p);
		}
		SyncMarkers();
	}

	static Vector3 RandomOffset(RandomNumberGenerator rng, float r) =>
		new(rng.RandfRange(-r, r), rng.RandfRange(-2, 2), rng.RandfRange(-r, r));

	void AddAsteroid(RandomNumberGenerator rng, Vector3 pos)
	{
		float r = rng.RandfRange(1.5f, 6f), g = rng.RandfRange(0.3f, 0.5f);
		var mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 8, Rings = 5 };
		var a = Mesh(mesh, Mat(new Color(g + 0.1f, g, g - 0.1f), 0f, 0.1f), pos, _sectorRoot);
		a.Scale = new Vector3(rng.RandfRange(0.7f, 1.3f), rng.RandfRange(0.7f, 1.3f), rng.RandfRange(0.7f, 1.3f));
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

	/// <summary>Händler, die gerade im Sektor sind (nicht unterwegs), als kleine Marker um die erste Station.</summary>
	void SyncMarkers()
	{
		var sector = _u.Sectors[_u.PlayerSector];
		var wanted = sector.Ships
			.Select(id => _u.Ships[id])
			.Where(s => (s.Role == ShipRole.Trader || s.Role == ShipRole.Miner) && s.Transit == null)
			.ToList();

		foreach (var id in _markers.Keys.Where(id => wanted.All(s => s.Id != id)).ToList())
		{
			_markers[id].Node.QueueFree();
			_markers.Remove(id);
		}
		var stationCenter = sector.Stations.Count > 0
			? new Vector3(_u.Stations[sector.Stations[0]].Position.X, 0, _u.Stations[sector.Stations[0]].Position.Y)
			: Vector3.Zero;
		foreach (var s in wanted.Where(s => !_markers.ContainsKey(s.Id)))
		{
			// Miner beim Abbau schweben über dem Rohstofffeld, alle anderen kreisen um die Station
			bool mining = s.Role == ShipRole.Miner && s.MinePhase == MinePhase.Mining && sector.Fields.Count > 0;
			var center = mining ? new Vector3(sector.Fields[0].Position.X, 0, sector.Fields[0].Position.Y) : stationCenter;
			var color = s.Role == ShipRole.Miner ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.7f, 0.2f);
			var node = Mesh(new PrismMesh { Size = new Vector3(2f, 3f, 0.8f) }, Mat(color, 1f), center, _sectorRoot);
			node.RotationDegrees = new Vector3(-90, 0, 0);
			_markers[s.Id] = new Marker { Node = node, Center = center, Angle = s.Id.Value * 2.4f, Radius = mining ? 10f : 22f + s.Id.Value * 3f };
		}
	}

	// ───────── HUD ─────────

	void UpdateHud()
	{
		var sector = _u.Sectors[_u.PlayerSector];
		var sb = new StringBuilder();
		sb.AppendLine($"{sector.Name}   Tick {_u.Tick}   Speed {(int)_vel.Length()}   [T] Zeitraffer");
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
