using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Game;
using SpaceAdventure.Ui;
using SpaceAdventure.World;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Kompositionswurzel: verdrahtet Session, Sektoransicht, Spielerschiff und Oberfläche und übersetzt Eingaben in
/// Steuerwünsche. Neue Features hängen sich als eigene Komponenten an, statt diese Klasse aufzublähen.
/// Steuerung: Stick/WASD/Pfeile, Maus oder Finger gedrückt halten = zum Zeiger fliegen, Antippen eines Ziels = Autopilot-Ziel.
/// </summary>
public partial class Main : Node3D
{
	const float GateRadius = 10f;
	const float AutosaveSeconds = 30f;
	static readonly Vector3 CamOffset = new(0, 38, 24);

	GameSession _session = null!;
	SectorView _view = null!;
	PlayerShip _player = null!;
	Camera3D _cam = null!;
	Hud _hud = null!;
	Fx _fx = null!;
	Sfx _sfx = null!;

	NavTarget? _target;
	StationId? _targetStation;
	StationId? _docked;
	bool _autopilot, _fast;
	float _jumpCooldown, _autosave;
	float _hitFlash;

	// Zeiger in der Welt (nicht über Oberfläche): Halten = fliegen, Antippen = Ziel wählen
	int _pointerFinger = -1;
	Vector2 _pointerPos, _pointerStart;
	double _pointerDownAt;

	Ship PlayerData => _session.Player;

	public override void _Ready()
	{
		BuildEnvironment();
		BuildStars();

		_view = new SectorView(); AddChild(_view);
		_player = new PlayerShip(); AddChild(_player);
		_fx = new Fx(); AddChild(_fx);
		_sfx = new Sfx(); AddChild(_sfx);
		_cam = new Camera3D { Fov = 50f, Far = 1000f }; AddChild(_cam);
		_hud = new Hud(); AddChild(_hud);

		_hud.TargetScreenPosition = ScreenPositionOfTarget;
		_hud.MapPressed += () => { _sfx.Play(SfxId.Click); _hud.Map.Open(_session.U, _session.U.PlayerSector, _target?.Sector); };
		_hud.Map.SectorSelected += sector => SetTarget(Navigator.ForSector(_session.U, sector, _session.U.PlayerShip), null);
		_hud.EconomyPressed += () => { _sfx.Play(SfxId.Click); _hud.Economy.Visible = !_hud.Economy.Visible; _hud.Economy.Refresh(_session, force: true); };
		_hud.MenuPressed += () => { _sfx.Play(SfxId.Click); _hud.Menu.Visible = true; };
		_hud.Menu.Action += OnMenu;
		_hud.AutopilotPressed += ToggleAutopilot;
		_hud.FastPressed += () => _fast = _hud.FastButton.On;
		_hud.DockPressed += DockAtNearest;
		_hud.Dock.Undock += Undock;
		_player.Hit += OnPlayerHit;

		bool selftest = OS.GetCmdlineUserArgs().Contains("--selftest");
		if (selftest)
		{
			SaveService.UseFile("selftest.json");
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://selftest.json"));   // immer frisch beginnen
		}
		StartSession();
		if (selftest) AddChild(new SelfTest(this));
	}

	// ───────── Spielstand ─────────

	void StartSession()
	{
		if (SaveService.TryLoad(out var u, out var savedAt, out var error) && u != null)
		{
			_session = new GameSession();
			_session.Use(u);
			var result = OfflineCatchUp.Run(u, _session.Runner, DateTimeOffset.UtcNow - savedAt);
			AttachSession();
			if (result.TicksRun > 0)
				_hud.Toast($"Willkommen zurück – {result.TicksRun / SimClock.TicksPerSecond / 60} Min Spielzeit nachgeholt{(result.Capped ? " (gedeckelt)" : "")}", 4f);
		}
		else
		{
			if (SaveService.Exists) GD.PushWarning("Spielstand nicht ladbar, starte neu: " + error);
			_session = GameSession.New();
			AttachSession();
			if (SaveService.Exists) _hud.Toast("Spielstand beschädigt – neues Spiel gestartet", 4f);
		}
	}

	/// <summary>Hängt Ansicht und Ereignisse an das aktuelle Universum und setzt das Spielerschiff.</summary>
	void AttachSession()
	{
		var u = _session.U;
		u.Events.Raised += OnSimEvent;
		_target = null; _targetStation = null; _docked = null; _autopilot = false;
		_hud.AutopilotButton.On = false;
		_hud.Dock.Visible = false;
		_view.Rebuild(u);
		var p = PlayerData;
		_player.Position = new Vector3(p.Position.X, 0, p.Position.Y);
		_player.Velocity = Vector3.Zero; _player.Locked = false;
		_cam.Position = _player.Position + CamOffset;
	}

	void OnMenu(string action)
	{
		_sfx.Play(SfxId.Click);
		switch (action)
		{
			case "save": SaveNow(manual: true); break;
			case "load":
				if (SaveService.TryLoad(out var u, out _, out var error) && u != null) { _session.Use(u); AttachSession(); _hud.Toast("Spielstand geladen"); }
				else _hud.Toast("Laden fehlgeschlagen: " + error, 4f);
				break;
			case "new": _session.Use(SpaceSim.Core.Content.WorldBuilder.BuildDefault()); AttachSession(); _hud.Toast("Neues Spiel"); break;
		}
		if (action != "save") _hud.Menu.Visible = false;
	}

	void SaveNow(bool manual)
	{
		SyncPlayerData();
		bool ok = SaveService.Save(_session.U, out var error);
		if (manual) _hud.Toast(ok ? "Gespeichert" : "Speichern fehlgeschlagen: " + error, ok ? 2f : 4f);
		else if (!ok) GD.PushWarning("Autosave fehlgeschlagen: " + error);
	}

	void SyncPlayerData()
	{
		PlayerData.Position = new Vec2(_player.Position.X, _player.Position.Z);
	}

	public override void _Notification(int what)
	{
		if (_session != null && (what == NotificationWMCloseRequest || what == NotificationApplicationPaused)) SaveNow(manual: false);
	}

	void OnSimEvent(SimEvent e)
	{
		switch (e)
		{
			case StationBuilt b: _hud.Toast($"Neue Station: {_session.U.Stations[b.Station].Name}"); break;
			case StationRemoved r when r.Reason == "bankrupt": _hud.Toast("Station aufgegeben (Pleite)"); break;
		}
	}

	// ───────── Frame ─────────

	public override void _Process(double deltaD)
	{
		float delta = (float)deltaD;
		_jumpCooldown = Mathf.Max(0f, _jumpCooldown - delta);
		_hitFlash = Mathf.Max(0f, _hitFlash - delta);

		_session.Advance(delta, _fast ? 10f : 1f);
		if (_view.NeedsRebuild(_session.U)) _view.Rebuild(_session.U);

		bool frozen = _hud.AnyModalOpen || _docked != null;
		var (dir, throttle, speedScale) = frozen ? (Vector2.Zero, 0f, 1f) : ReadSteering();
		_player.Locked = _docked != null;
		_player.Step(delta, dir, throttle, speedScale, _view);
		_sfx.SetThrust(_player.Thrusting ? Mathf.Clamp(throttle, 0.3f, 1f) : 0f);

		if (!frozen) { CheckGates(); UpdateDockButton(); }
		SyncPlayerData();

		_view.Animate(delta);
		_view.UpdateShips(_session.U, _session.FractionalTick, _player.Position, delta);
		_cam.Position = _cam.Position.Lerp(_player.Position + CamOffset, 1f - Mathf.Exp(-6f * delta));
		_cam.LookAt(_cam.Position - CamOffset);

		UpdateHud();
		_hud.Economy.Refresh(_session);
		_autosave += delta;
		if (_autosave >= AutosaveSeconds) { _autosave = 0f; SaveNow(manual: false); }
	}

	/// <summary>Steuerwunsch aus Stick/Tastatur (Vorrang), Zeiger oder Autopilot.</summary>
	(Vector2 dir, float throttle, float speedScale) ReadSteering()
	{
		var manual = _hud.Stick.Value;
		var keys = new Vector2(
			(Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) ? 1 : 0) - (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) ? 1 : 0),
			(Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down) ? 1 : 0) - (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) ? 1 : 0));
		if (keys != Vector2.Zero) manual = keys.Normalized();

		if (manual.Length() > 0.1f) { CancelAutopilot(); return (manual, manual.Length(), 1f); }

		if (_pointerFinger >= 0 && Time.GetTicksMsec() / 1000.0 - _pointerDownAt > 0.2)
		{
			var ground = GroundPoint(_pointerPos);
			if (ground is { } g)
			{
				var d = g - _player.Position;
				if (d.Length() > 3f) { CancelAutopilot(); return (new Vector2(d.X, d.Z), 1f, 1f); }
			}
		}

		if (_autopilot && _target is { } target) return Autopilot(target);
		return (Vector2.Zero, 0f, 1f);
	}

	(Vector2, float, float) Autopilot(NavTarget target)
	{
		var u = _session.U;
		var step = Navigator.Next(u, u.PlayerSector, new Vec2(_player.Position.X, _player.Position.Z), target);
		switch (step.Status)
		{
			case NavStatus.Unreachable: _hud.Toast("Kein Weg zum Ziel (Gates gesperrt)"); CancelAutopilot(); return (Vector2.Zero, 0f, 1f);
			case NavStatus.Arrived:
				_hud.Toast($"Ziel erreicht: {target.Label}");
				CancelAutopilot();
				if (_targetStation is { } sid && u.Stations.ContainsKey(sid)) Dock(sid);
				_target = null; _targetStation = null;
				return (Vector2.Zero, 0f, 1f);
		}
		var to = new Vector2(step.Waypoint.X - _player.Position.X, step.Waypoint.Y - _player.Position.Z);
		// Im Zielsektor sanft abbremsen, damit das Schiff nicht über den Parkpunkt hinausschießt
		float final = u.PlayerSector == target.Sector ? Vec2.Distance(new Vec2(_player.Position.X, _player.Position.Z), target.Position) : 999f;
		float scale = Mathf.Clamp(final / 40f, 0.25f, 1f);
		return (to, 1f, scale);
	}

	void CheckGates()
	{
		if (_jumpCooldown > 0f) return;
		foreach (var (id, pos) in _view.Gates)
			if (_player.Position.DistanceTo(pos) < GateRadius) { Jump(id); return; }
	}

	/// <summary>Sprung: Core hängt das Schiff um, Szene des alten Sektors wird verworfen, der Zielsektor neu aufgebaut.</summary>
	void Jump(GateId gateId)
	{
		var u = _session.U;
		var far = u.Gates[u.Gates[gateId].Target];
		u.MoveShip(PlayerData, far.Sector);
		u.PlayerSector = far.Sector;
		_view.Rebuild(u);

		var gpos = new Vector3(far.Position.X, 0, far.Position.Y);
		var dir = (-gpos).Normalized();
		_player.Position = gpos + dir * 22f;
		_player.Heading = Mathf.Atan2(-dir.X, -dir.Z);
		_player.Velocity = dir * 12f;
		_jumpCooldown = 2f;
		_cam.Position = _player.Position + CamOffset;
		_hud.Flash(new Color(0.6f, 0.85f, 1f));
		_sfx.Play(SfxId.Jump);
		_hud.Toast(u.Sectors[far.Sector].Name);
	}

	// ───────── Ziele, Autopilot, Andocken ─────────

	void SetTarget(NavTarget target, StationId? station)
	{
		_target = target; _targetStation = station;
		_autopilot = true; _hud.AutopilotButton.On = true;
		_sfx.Play(SfxId.Click);
		_hud.Toast($"Autopilot: {target.Label}");
	}

	void ToggleAutopilot()
	{
		_sfx.Play(SfxId.Click);
		if (_target == null) { _hud.AutopilotButton.On = false; _hud.Toast("Kein Ziel – Station oder Gate antippen, oder Karte öffnen"); return; }
		_autopilot = _hud.AutopilotButton.On;
	}

	void CancelAutopilot()
	{
		if (!_autopilot) return;
		_autopilot = false; _hud.AutopilotButton.On = false;
	}

	Vector2? ScreenPositionOfTarget()
	{
		if (_target is not { } t || _session == null) return null;
		var u = _session.U;
		Vector3 world;
		if (t.Sector == u.PlayerSector) world = new Vector3(t.Position.X, 0, t.Position.Y);
		else
		{
			var route = u.FindRoute(u.PlayerSector, t.Sector);
			if (route is not { Count: > 0 }) return null;
			var g = u.Gates[route[0]].Position;
			world = new Vector3(g.X, 0, g.Y);
		}
		if (_cam.IsPositionBehind(world)) return null;
		return _cam.UnprojectPosition(world);
	}

	void UpdateDockButton()
	{
		_hud.DockButton.Visible = NearestStation(out _) != null;
	}

	StationId? NearestStation(out float distance)
	{
		StationId? best = null; distance = 55f;
		var u = _session.U;
		foreach (var sid in u.Sectors[u.PlayerSector].Stations)
		{
			var st = u.Stations[sid];
			float d = new Vector2(_player.Position.X - st.Position.X, _player.Position.Z - st.Position.Y).Length();
			if (d < distance) { distance = d; best = sid; }
		}
		return best;
	}

	void DockAtNearest() { if (NearestStation(out _) is { } sid) Dock(sid); }

	void Dock(StationId id)
	{
		var u = _session.U;
		var st = u.Stations[id];
		_docked = id;
		_player.Locked = true;
		var spot = st.DockPoint(u.PlayerShip);
		// Sanft zum Parkpunkt gleiten
		var tw = CreateTween();
		tw.TweenProperty(_player, "position", new Vector3(spot.X, 0, spot.Y), 0.6);
		_sfx.Play(SfxId.Dock);
		_hud.Dock.Open(u, st);
	}

	void Undock()
	{
		_sfx.Play(SfxId.Click);
		_docked = null; _player.Locked = false;
		_hud.Dock.Visible = false;
	}

	// ───────── Zeiger in der Welt ─────────

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_hud == null || _hud.AnyModalOpen) return;
		if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion)) return;

		if (!motion && pressed && _pointerFinger < 0)
		{
			_pointerFinger = idx; _pointerPos = _pointerStart = pos;
			_pointerDownAt = Time.GetTicksMsec() / 1000.0;
		}
		else if (idx == _pointerFinger && motion) _pointerPos = pos;
		else if (idx == _pointerFinger && !pressed)
		{
			bool tap = Time.GetTicksMsec() / 1000.0 - _pointerDownAt < 0.25 && pos.DistanceTo(_pointerStart) < 20f;
			_pointerFinger = -1;
			if (tap) TryPickTarget(pos);
		}
	}

	Vector3? GroundPoint(Vector2 screen)
	{
		var from = _cam.ProjectRayOrigin(screen);
		var dir = _cam.ProjectRayNormal(screen);
		if (Mathf.Abs(dir.Y) < 0.001f) return null;
		return from + dir * (-from.Y / dir.Y);
	}

	void TryPickTarget(Vector2 screen)
	{
		if (GroundPoint(screen) is not { } ground) return;
		var best = _view.Targets.Where(t => t.Position.DistanceTo(ground) < 22f).OrderBy(t => t.Position.DistanceTo(ground)).FirstOrDefault();
		if (best.Label == null) return;
		var u = _session.U;
		if (best.Kind == "station")
		{
			var sid = new StationId(best.Id);
			SetTarget(Navigator.ForStation(u, sid, u.PlayerShip), sid);
		}
		else
		{
			var gate = u.Gates[new GateId(best.Id)];
			SetTarget(Navigator.ForSector(u, u.Gates[gate.Target].Sector, u.PlayerShip), null);
		}
	}

	// ───────── Rückmeldung ─────────

	void OnPlayerHit(float impact, Vector3 point)
	{
		PlayerData.Hull = Mathf.Max(0f, PlayerData.Hull - (impact - 4f) * 0.8f);
		_hitFlash = 0.4f;
		_sfx.Play(SfxId.Hit);
		_fx.Burst(point, new Color(1f, 0.7f, 0.3f), 14, 14f);
		_hud.Flash(new Color(1f, 0.2f, 0.1f), 0.35f);
		if (PlayerData.Hull <= 0f) Respawn();
	}

	/// <summary>Hülle zerstört: Explosion, Reparatur und Neustart an der Station des Sektors.</summary>
	void Respawn()
	{
		var u = _session.U;
		_fx.Burst(_player.Position, new Color(1f, 0.5f, 0.2f), 60, 28f, 1.2f);
		_sfx.Play(SfxId.Explosion);
		var sector = u.Sectors[u.PlayerSector];
		var spot = sector.Stations.Count > 0 ? u.Stations[sector.Stations[0]].DockPoint(u.PlayerShip) : new Vec2(0, 0);
		_player.Position = new Vector3(spot.X, 0, spot.Y);
		_player.Velocity = Vector3.Zero;
		PlayerData.Hull = Ship.MaxHull;
		CancelAutopilot();
		_hud.Toast("Schiff zerstört – repariert an der Station", 3.5f);
	}

	void UpdateHud()
	{
		var u = _session.U;
		string status = $"{u.Sectors[u.PlayerSector].Name}   Tick {u.Tick}   Speed {(int)_player.Velocity.Length()}   Credits {(int)PlayerData.Credits}";
		string? target = null;
		if (_target is { } t)
		{
			float d = t.Sector == u.PlayerSector
				? new Vector2(_player.Position.X - t.Position.X, _player.Position.Z - t.Position.Y).Length()
				: float.NaN;
			target = float.IsNaN(d) ? $"Ziel: {t.Label} (anderer Sektor)" : $"Ziel: {t.Label}  {d:0} m";
		}
		_hud.SetStatus(status, PlayerData.Hull, target);
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
			mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(s, s, s)),
				new Vector3(rng.RandfRange(-400, 400), rng.RandfRange(-40, -8), rng.RandfRange(-400, 400))));
		}
		AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = Gfx.Mat(new Color(0.9f, 0.95f, 1f), 2f) });
	}

	// ───────── Zugriff für den Selbsttest ─────────

	internal GameSession Session => _session;
	internal Hud HudRef => _hud;
	internal PlayerShip Player => _player;
	internal SectorView View => _view;
	internal bool Docked => _docked != null;
	internal void TestSetStationTarget(StationId id) { var u = _session.U; SetTarget(Navigator.ForStation(u, id, u.PlayerShip), id); }
	internal void TestUndock() => Undock();
	internal bool TestSave() { SaveNow(manual: false); return SaveService.Exists; }
	internal void TestMenu(string a) => OnMenu(a);
}
