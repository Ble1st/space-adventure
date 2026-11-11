using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Game;
using SpaceAdventure.Ui;
using SpaceAdventure.Ui.Kit;
using SpaceAdventure.Ui.Screens;
using SpaceAdventure.World;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;

namespace SpaceAdventure;

/// <summary>
/// Kompositionswurzel: verdrahtet Session, Sektoransicht, Spielerschiff und Oberfläche und übersetzt Eingaben in
/// Steuerwünsche. Neue Features hängen sich als eigene Komponenten an, statt diese Klasse aufzublähen.
/// Lenkpriorität: Tempo-Slider und Wendetasten/Tasten > Zeiger (gedrückt halten = zum Zeiger fliegen) > Autopilot;
/// manuelle Bedienung bricht Autopilot und Auto-Mining ab. Antippen eines Ziels = Autopilot-Ziel.
/// Kamera: frei um das Schiff schwenkbar (Rechnung in SpaceSim.Core.Flight.OrbitCamera), bleibt immer auf das
/// Schiff ausgerichtet; Standardansicht = früherer fester Offset (0, 38, 24). Zwei Finger schwenken/zoomen,
/// rechte/mittlere Maustaste + Rad am Desktop; „Kamera ↺“ setzt zurück. Wird nicht im Spielstand gespeichert.
/// </summary>
public partial class Main : Node3D
{
	const float GateRadius = 10f;
	const float AutosaveSeconds = 30f;
	const float WheelZoomIn = 0.88f, WheelZoomOut = 1f / 0.88f;

	GameSession _session = null!;
	SectorView _view = null!;
	PlayerShip _player = null!;
	Camera3D _cam = null!;
	Hud _hud = null!;
	Fx _fx = null!;
	Sfx _sfx = null!;
	MiningView _mining = null!;

	// Kamera: Winkel/Abstand als reine Core-Rechnung, Ist-Offset folgt weich (Standard = früherer CamOffset 0,38,24)
	readonly OrbitCamera _orbit = new();
	CamVec3 _camOffset;

	// Abbau: Spieler-Mikrosimulation (flüchtig), Automodus und manuelle Bedienung
	readonly OreChunks _chunks = new();
	readonly MiningAutopilot _miner = new();
	bool _tractorOn;
	bool _fireRequested;   // Einzelschuss angefordert (Tipp auf "Feuer" oder Taste F)
	Asteroid? _mineTarget;
	float _sinceHit = 99f;
	int _hitCount;
	string _lastHit = "";
	float _lowestHull = 100f;
	bool _transition;   // Ein-/Ausdocken läuft (Schiff animiert, keine Kollision)
	string _mineHint = "";
	float _hintCooldown;
	MiningStep? _miningStep;
	int _sales;

	NavTarget? _target;
	StationId? _targetStation;
	StationId? _docked;
	bool _autopilot, _fast;
	float _jumpCooldown, _autosave, _dockRefresh, _ccRefresh;
	float _hitFlash;

	// Zeiger in der Welt (nicht über Oberfläche): Wischen = Kamera schwenken, Antippen = Ziel wählen (kein Fliegen per Touch)
	int _pointerFinger = -1;
	bool _pointerDragging;
	Vector2 _pointerPos, _pointerStart;
	double _pointerDownAt;

	// Kamera-Gesten: alle aktiven Touch-Finger (ab zweien schwenkt/zoomt die Kamera), Mauszug mit rechter/mittlerer Taste
	readonly Dictionary<int, Vector2> _touch = new();
	bool _camMouseDrag;
	float _pinchLast;

	Ship PlayerData => _session.Player;
	/// <summary>Phase 10: Meldung des letzten Sprungversuchs (Grund einer Ablehnung oder Zollquittung; Selbsttest).</summary>
	public string LastJumpMessage { get; private set; } = "";

	public override void _Ready()
	{
		BuildEnvironment();
		BuildStars();

		_view = new SectorView(); AddChild(_view);
		_mining = new MiningView { GoodColor = id => GoodColor(id) }; AddChild(_mining);
		_player = new PlayerShip(); AddChild(_player);
		_fx = new Fx(); AddChild(_fx);
		_sfx = new Sfx(); AddChild(_sfx);
		_cam = new Camera3D { Fov = 50f, Far = 1000f }; AddChild(_cam);
		_hud = new Hud(); AddChild(_hud);

		_hud.TargetScreenPosition = ScreenPositionOfTarget;
		_hud.Flight.MapPressed += () => { _sfx.Play(SfxId.Click); _hud.Map.Open(_session.U, _session.U.PlayerSector, _target?.Sector); };
		_hud.Flight.HamburgerPressed += () => { _sfx.Play(SfxId.Click); ToggleCommand(); };
		_hud.Flight.FastPressed += () => { _fast = _hud.Flight.FastOn; _sfx.Play(SfxId.Click); };
		_hud.Flight.AutopilotPressed += ToggleAutopilot;
		_hud.Flight.AutoMinePressed += () => SetAutoMining(_hud.Flight.AutoMineOn);
		_hud.Flight.ContextPressed += ContextAction;
		_hud.Flight.CommandPressed += () => { _hud.Map.Visible = false; OpenCcArea(CommandAreas.Universe); };
		_hud.Flight.SetCommandBadge(UiPlaceholders.CommsUnread);
		_hud.Flight.BayDown += OnBayDown;
		_hud.Flight.AllFire += FireReadyBays;
		_hud.Command.Action += OnCommandAction;
		_hud.Map.SectorSelected += sector => SetTarget(Navigator.ForSector(_session.U, sector, _session.U.PlayerShip), null);
		_hud.DockMenu.AreaSelected += OnDockArea;
		_hud.DockMenu.UndockPressed += Undock;
		_hud.DockTrade.AreaSelected += OnDockArea;
		_hud.DockTrade.BackToMenu += ShowDockMenu;
		_hud.DockTrade.UndockPressed += Undock;
		_hud.DockTrade.Executed += ok => _sfx.Play(ok ? SfxId.Pickup : SfxId.Click);
		_hud.DockInfo.AreaSelected += OnDockArea;
		_hud.DockInfo.BackToMenu += ShowDockMenu;
		_hud.DockInfo.UndockPressed += Undock;
		_hud.DockRepair.AreaSelected += OnDockArea;
		_hud.DockRepair.BackToMenu += ShowDockMenu;
		_hud.DockRepair.UndockPressed += Undock;
		_hud.DockRepair.Executed += ok => _sfx.Play(ok ? SfxId.Pickup : SfxId.Click);
		_hud.DockOutfitter.AreaSelected += OnDockArea;
		_hud.DockOutfitter.BackToMenu += ShowDockMenu;
		_hud.DockOutfitter.UndockPressed += Undock;
		_hud.DockOutfitter.Executed += ok => _sfx.Play(ok ? SfxId.Pickup : SfxId.Click);
		_hud.DockShipyard.AreaSelected += OnDockArea;
		_hud.DockShipyard.BackToMenu += ShowDockMenu;
		_hud.DockShipyard.UndockPressed += Undock;
		_hud.DockShipyard.Executed += ok => _sfx.Play(ok ? SfxId.Dock : SfxId.Click);
		_hud.DockStorage.AreaSelected += OnDockArea;
		_hud.DockStorage.BackToMenu += ShowDockMenu;
		_hud.DockStorage.UndockPressed += Undock;
		_hud.DockStorage.Executed += ok => _sfx.Play(ok ? SfxId.Pickup : SfxId.Click);
		_hud.DockHangar.SwitchRequested += SwitchShip;
		_hud.DockHangar.AreaSelected += OnDockArea;
		_hud.DockHangar.BackToMenu += ShowDockMenu;
		_hud.DockHangar.UndockPressed += Undock;
		_hud.DockPlaceholder.AreaSelected += OnDockArea;
		_hud.DockPlaceholder.BackToMenu += ShowDockMenu;
		_hud.DockPlaceholder.UndockPressed += Undock;
		// Kommandozentrale (UI.md Abschnitt 4): Bereiche der linken Leiste, „← Flug“, echte Aktionen
		_hud.CcUniverse.AreaSelected += OpenCcArea;
		_hud.CcSector.AreaSelected += OpenCcArea;
		_hud.CcFactions.AreaSelected += OpenCcArea;
		_hud.CcMyFaction.AreaSelected += OpenCcArea;
		_hud.CcFleet.AreaSelected += OpenCcArea;
		_hud.CcAssets.AreaSelected += OpenCcArea;
		_hud.CcComms.AreaSelected += OpenCcArea;
		_hud.CcSystem.AreaSelected += OpenCcArea;
		_hud.CcUniverse.BackToFlight += CloseCommand;
		_hud.CcSector.BackToFlight += CloseCommand;
		_hud.CcFactions.BackToFlight += CloseCommand;
		_hud.CcMyFaction.BackToFlight += CloseCommand;
		_hud.CcFleet.BackToFlight += CloseCommand;
		_hud.CcAssets.BackToFlight += CloseCommand;
		_hud.CcComms.BackToFlight += CloseCommand;
		_hud.CcSystem.BackToFlight += CloseCommand;
		_hud.CcUniverse.OpenSectorMap += sec => { _hud.CcSector.SelectedSector = sec; OpenCcArea(CommandAreas.Sector); };
		_hud.CcUniverse.PlanRoute += sec =>
		{
			var uu = _session.U;
			int hops = uu.FindRoute(uu.PlayerSector, sec)?.Count ?? -1;
			SetTarget(Navigator.ForSector(uu, sec, uu.PlayerShip), null);
			_hud.Toast($"Autopilot: Route nach {uu.Sectors[sec].Name} ({hops} {(hops == 1 ? "Sprung" : "Sprünge")})");
		};
		_hud.CcSector.AutopilotRequested += (nav, st) => SetTarget(nav, st);
		_hud.CcFactions.CommsRequested += () => OpenCcArea(CommandAreas.Comms);
		_hud.CcFactions.TerritoryRequested += f =>
		{
			var uu = _session.U;
			_hud.CcUniverse.CurrentLayer = UniverseScreen.Layer.Faction;
			_hud.CcUniverse.MarkFaction = f;
			var terr = uu.Sectors.Values.FirstOrDefault(s => s.Owner == f);
			_hud.CcUniverse.SelectedSector = terr?.Id ?? uu.PlayerSector;
			OpenCcArea(CommandAreas.Universe);
		};
		_hud.CcFleet.AutoMiningToggled += on => SetAutoMining(on);
		_hud.CcSystem.SystemAction += a => OnMenu(a);
		_player.Hit += OnPlayerHit;
		_view.AsteroidGone += (pos, r) => { _fx.Burst(pos, new Color(1f, 0.7f, 0.3f), 22, 10f); _sfx.Play(SfxId.Crack); };
		_chunks.Shot += OnShot;

		var args = OS.GetCmdlineUserArgs();
		bool selftest = args.Contains("--selftest");
		int shotIdx = Array.IndexOf(args, "--ui-shots");
		string? shotDir = shotIdx >= 0 && shotIdx + 1 < args.Length ? args[shotIdx + 1] : null;
		if (selftest || shotDir != null)
		{
			SaveService.UseFile(selftest ? "selftest.json" : "ui-shots.json");
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(selftest ? "user://selftest.json" : "user://ui-shots.json"));   // immer frisch beginnen
		}
		StartSession();
		if (selftest) AddChild(new SelfTest(this));
		if (shotDir != null)
		{
			if (!System.IO.Path.IsPathRooted(shotDir)) shotDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), shotDir);
			AddChild(new UiShots(this, shotDir));
		}
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
		_hud.Flight.SetAutopilot(false);
		_hud.Flight.SetSlider(0f);
		_hud.Screens.Hide();
		SetAutoMining(false, announce: false);
		_chunks.Clear(); _mineTarget = null; _mining.ClearPlayer(); _mining.ResetNpc();
		_transition = false; _player.Visible = true; _player.Scale = Vector3.One; _player.GiveGrace(2f);
		_hud.Flight.Visible = true;   // Dock-Bildschirme wurden über Screens.Hide() geschlossen
		_view.Rebuild(u);
		var p = PlayerData;
		_player.Position = new Vector3(p.Position.X, 0, p.Position.Y);
		_player.Velocity = Vector3.Zero; _player.Locked = false;
		_camOffset = _orbit.Offset();   // gewählte Ansicht bleibt erhalten (nicht im Spielstand gespeichert)
		SnapCam();
	}

	/// <summary>Setzt die Kamera hart auf Schiff + aktuellen Orbit-Offset und richtet sie auf das Schiff aus (Neustart, Sprung, Teleport).</summary>
	void SnapCam()
	{
		var o = _camOffset;
		_cam.Position = _player.Position + new Vector3(o.X, o.Y, o.Z);
		_cam.LookAt(_player.Position);
	}

	/// <summary>„Kamera ↺“: Yaw/Pitch/Abstand auf die Standardansicht zurücksetzen (weicher Übergang über Follow).</summary>
	void ResetCamera()
	{
		_orbit.Reset();
		_sfx.Play(SfxId.Click);
		_hud.Toast("Kamera zurückgesetzt");
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
			case var a when a.StartsWith("new:", StringComparison.Ordinal):   // Phase 5c: Startpaket (player.startOptions)
			{
				string key = a[4..];
				var name = SpaceSim.Core.Content.WorldBuilder.DefaultStartOptions().FirstOrDefault(o => o.Key == key)?.Name ?? key;
				_session.Use(SpaceSim.Core.Content.WorldBuilder.BuildDefault(startOption: key));
				AttachSession();
				_hud.Toast($"Neues Spiel – Startpaket {name}");
				break;
			}
		}
		if (action != "save") _hud.Screens.Hide();
	}

	/// <summary>Aktionen des Hamburger-Menüs: Bereiche der Kommandozentrale öffnen Vollbild-Bildschirme, dazu Karte/Wirtschaft/Spielstand plus Schließen.</summary>
	void OnCommandAction(string action)
	{
		switch (action)
		{
			case "map": _hud.Screens.Hide(); _sfx.Play(SfxId.Click); _hud.Map.Open(_session.U, _session.U.PlayerSector, _target?.Sector); break;
			case "economy": _hud.Screens.Hide(); _sfx.Play(SfxId.Click); _hud.Economy.Visible = !_hud.Economy.Visible; _hud.Economy.Refresh(_session, force: true); break;
			case "save": case "load": case "new": OnMenu(action); break;
			// Kamera-Einstellungen: Menü bleibt offen, damit man die Wirkung sieht
			case "zoom_in": _sfx.Play(SfxId.Click); _orbit.Zoom(MenuZoomStep); RefreshCameraMenu(); break;
			case "zoom_out": _sfx.Play(SfxId.Click); _orbit.Zoom(1f / MenuZoomStep); RefreshCameraMenu(); break;
			case "cam_free": _sfx.Play(SfxId.Click); _orbit.Mode = CameraMode.Free; RefreshCameraMenu(); break;
			case "cam_lock":
				_sfx.Play(SfxId.Click); _orbit.Mode = CameraMode.TargetLock; RefreshCameraMenu();
				if (LockTargetPosition() == null) _hud.Toast("Target-Lock: Wähle ein Ziel (Station, Gate, Asteroid antippen)");
				break;
			case "cam_invert": _sfx.Play(SfxId.Click); _orbit.InvertDrag = !_orbit.InvertDrag; RefreshCameraMenu(); break;
			case "cam_reset": ResetCamera(); RefreshCameraMenu(); break;
			case "emergency": CallEmergency(); break;
			case "close": _sfx.Play(SfxId.Click); _hud.Screens.Hide(); if (_docked == null) _hud.Flight.Visible = true; break;
			default:
				if (CommandAreas.All.Any(a => a.Id == action)) OpenCcArea(action);
				break;
		}
	}

	/// <summary>Notruf (Phase 4c): Treibstoff zum Aufpreis bis zur nächsten Tankstelle, nur bei leerem Tank im Flug.</summary>
	void CallEmergency()
	{
		SyncPlayerData();
		var r = _docked != null
			? new Refuel.EmergencyResult(false, 0f, 0f, "Angedockt – Notruf nur im Flug.")
			: Refuel.PlayerEmergency(_session.U, PlayerData);
		_sfx.Play(r.Success ? SfxId.Pickup : SfxId.Click);
		_hud.Screens.Hide();
		if (_docked == null) _hud.Flight.Visible = true;
		_hud.Toast(r.Message, 5f);
	}

	/// <summary>Öffnet einen Bereich der Kommandozentrale im Vollbild (wie ein Tipp in der linken Leiste).</summary>
	void OpenCcArea(string areaId)
	{
		var u = _session.U;
		_hud.CcCtx.U = u;
		_hud.CcCtx.Player = PlayerData;
		_hud.CcCtx.FractionalTick = () => _session.FractionalTick;
		_hud.CcCtx.AutoMiningOn = () => _miner.Enabled;
		_hud.CcCtx.Docked = _docked;
		if (areaId == CommandAreas.Sector && _hud.CcSector.SelectedSector == null) _hud.CcSector.SelectedSector = u.PlayerSector;
		IScreen screen = areaId switch
		{
			CommandAreas.Universe => _hud.CcUniverse,
			CommandAreas.Sector => _hud.CcSector,
			CommandAreas.Factions => _hud.CcFactions,
			CommandAreas.MyFaction => _hud.CcMyFaction,
			CommandAreas.Fleet => _hud.CcFleet,
			CommandAreas.Assets => _hud.CcAssets,
			CommandAreas.Comms => _hud.CcComms,
			CommandAreas.System => _hud.CcSystem,
			_ => _hud.CcUniverse,
		};
		if (screen is CommandScreen cs) cs.AreaId = areaId;
		_hud.Flight.Visible = false;
		_hud.Screens.Show(screen);
		_sfx.Play(SfxId.Click);
	}

	/// <summary>„← Flug“: Kommandozentrale schließen, zurück zum Flug-HUD.</summary>
	void CloseCommand()
	{
		_sfx.Play(SfxId.Click);
		_hud.Screens.Hide();
		if (_docked == null) _hud.Flight.Visible = true;
	}

	void ToggleCommand()
	{
		if (_hud.Screens.Current == _hud.Command) _hud.Screens.Hide();
		else { _hud.Map.Visible = false; RefreshCameraMenu(); _hud.Screens.Show(_hud.Command); }
	}

	/// <summary>Zielposition für den Target-Lock (XZ): gewählter Asteroid, sonst Autopilot-Ziel im selben Sektor; sonst null.</summary>
	Vector3? LockTargetPosition()
	{
		if (_mineTarget is { Minable: true } rock) return new Vector3(rock.Position.X, 0f, rock.Position.Y);
		if (_target is { } t && t.Sector == _session.U.PlayerSector) return new Vector3(t.Position.X, 0f, t.Position.Y);
		return null;
	}

	/// <summary>Überträgt den Kamerazustand (Modus, Invertierung, Abstand) in die Anzeige des Hamburger-Menüs.</summary>
	void RefreshCameraMenu() => _hud.Command.SetCameraState(_orbit.Mode == CameraMode.TargetLock, _orbit.InvertDrag, _orbit.Distance, LockTargetPosition() != null);

	const float MenuZoomStep = 0.85f;

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
			case ConstructionStarted cs when cs.Sector == _session.U.PlayerSector: _hud.Toast($"Neue Baustelle: {_session.U.Stations[cs.Site].Name}"); break;
			case EmergencyConstruction ec when _session.U.Stations.TryGetValue(ec.Site, out var es) && es.Sector == _session.U.PlayerSector: _hud.Toast($"Notbau: {es.Name}"); break;
			case PlayerSold: _sales++; break;
			case MinerShot shot when shot.Sector == _session.U.PlayerSector: OnMinerShot(shot); break;
			case StationRemoved r when r.Reason == "bankrupt": _hud.Toast("Station aufgegeben (Pleite)"); break;
			case ActiveShipChanged changed: OnActiveShipChanged(changed); break;
		}
	}

	// ───────── Frame ─────────

	public override void _Process(double deltaD)
	{
		float delta = (float)deltaD;
		_jumpCooldown = Mathf.Max(0f, _jumpCooldown - delta);
		_splitReload = Mathf.Max(0f, _splitReload - delta);
		_hitFlash = Mathf.Max(0f, _hitFlash - delta);

		_session.Advance(delta, _fast ? 10f : 1f);
		if (_view.NeedsRebuild(_session.U)) { _view.Rebuild(_session.U); _mining.ResetNpc(); _chunks.Clear(); _mineTarget = null; }
		_view.SyncAsteroids(_session.U, delta);

		// Hülle regeneriert langsam, wenn eine Weile nichts getroffen hat
		_sinceHit += delta;
		if (_sinceHit > 5f && PlayerData.Hull < PlayerData.MaxHull) PlayerData.Hull = Mathf.Min(PlayerData.MaxHull, PlayerData.Hull + 3f * delta);

		bool frozen = _hud.AnyModalOpen || _docked != null;
		_miningStep = _miner.Enabled && !frozen
			? _miner.Step(_session.U, _chunks, PlayerData, new Vec2(_player.Position.X, _player.Position.Z))
			: null;
		if (!_miner.Enabled && _hud.Flight.AutoMineOn) { _hud.Flight.SetAutoMine(false); _hud.Toast(_miner.Status, 3.5f); }   // Automatik hat sich selbst beendet
		var (dir, throttle, speedScale, turn) = frozen ? (Vector2.Zero, 0f, 1f, 0f) : ReadSteering(delta);
		_player.Locked = _docked != null || _transition;
		bool hold = _miner.Enabled && _miningStep is { Nav: null, Waypoint: null };   // Automatik arbeitet an Ort und Stelle
		// Treibstoff (Phase 4c): leerer Tank deckelt das Tempo (Notantrieb), die geflogene Strecke wird abgebucht
		float fuelCap = PlayerFuel.ThrottleCap(PlayerData);
		var before = _player.Position;
		_player.Step(delta, turn, dir, throttle, speedScale * fuelCap, _view, brake: hold);
		if (!_player.Locked)
		{
			PlayerFuel.ConsumeFlown(PlayerData, new Vector2(_player.Position.X - before.X, _player.Position.Z - before.Z).Length());
			if (fuelCap >= 1f && PlayerFuel.ThrottleCap(PlayerData) < 1f)
				_hud.Toast("Tank leer – Notantrieb. Notruf im Menü oder zur nächsten Tankstelle schleichen.", 5f);
		}
		_sfx.SetThrust(_player.Thrusting ? Mathf.Clamp(Mathf.Abs(throttle), 0.3f, 1f) : 0f);

		if (!frozen) CheckGates();
		UpdateMining(delta, frozen);
		SyncPlayerData();

		_view.Animate(delta);
		_view.UpdateShips(_session.U, _session.FractionalTick, _docked != null ? null : _player.Position, delta);   // angedockt: niemand weicht dem unsichtbaren Schiff aus
		// Kamera: Ist-Offset folgt weich dem Orbit-Soll-Offset, Kamera sitzt auf Schiff + Offset und blickt auf das Schiff
		if (_orbit.Mode == CameraMode.TargetLock && LockTargetPosition() is { } lt)
			_orbit.FollowTarget(lt.X - _player.Position.X, lt.Z - _player.Position.Z, delta);   // Yaw führt das Ziel vor das Schiff
		_camOffset = OrbitCamera.Follow(_camOffset, _orbit.Offset(), delta);
		var co = _camOffset;
		_cam.Position = _player.Position + new Vector3(co.X, co.Y, co.Z);
		_cam.LookAt(_player.Position);

		UpdateHud();
		_hud.Economy.Refresh(_session);
		if (_docked != null)   // Dock-Bildschirme aktualisieren sich selbst, gedrosselt (Marktpreise bewegen sich)
		{
			_dockRefresh += delta;
			if (_dockRefresh >= 1f) { _dockRefresh = 0f; _hud.Screens.Current?.Refresh(); }
		}
		if (_hud.Screens.Current is CommandScreen cc)   // Kommandozentrale likewise
		{
			_ccRefresh += delta;
			if (_ccRefresh >= 0.5f) { _ccRefresh = 0f; cc.Refresh(); }
		}
		_autosave += delta;
		if (_autosave >= AutosaveSeconds) { _autosave = 0f; SaveNow(manual: false); }
	}

	/// <summary>
	/// Steuerwunsch nach Lenkpriorität: Tempo-Slider und Wendetasten/Tasten (manuell) > Zeiger (halten) > Autopilot.
	/// Manuelle Bedienung bricht Autopilot und Auto-Mining ab. Rückgabe: Richtung, Solltempo (-1..1), Skalierung, Dreheingang.
	/// </summary>
	(Vector2 dir, float throttle, float speedScale, float turn) ReadSteering(float delta)
	{
		var flight = _hud.Flight;
		float turnKeys = (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) ? 1 : 0) - (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) ? 1 : 0);
		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) flight.SetSlider(flight.SliderValue + delta * 0.8f);   // W bzw. Pfeil hoch erhöht den Slider
		if (Input.IsKeyPressed(Key.X)) flight.SetSlider(0f);                                                              // X = STOPP
		bool reverse = _testReverse || flight.ReverseHeld || Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down);                    // eigene Rückwärts-Haltetaste
		float turn = turnKeys + (flight.TurnRightHeld ? 1f : 0f) - (flight.TurnLeftHeld ? 1f : 0f);
		float slider = flight.SliderValue;

		if (turn != 0f || reverse || Mathf.Abs(slider) > 0.001f)   // manuell: Slider, Rückwärts und/oder Wenden
		{
			CancelAutopilot();
			if (_miner.Enabled) SetAutoMining(false, "Auto-Mining aus (manuelle Steuerung)");
			var fwd = new Vector2(-Mathf.Sin(_player.Heading), -Mathf.Cos(_player.Heading));
			return (fwd, Throttle.Fraction(slider, reverse), 1f, turn);
		}

		if (_miner.Enabled && _miningStep is { } ms)
		{
			if (ms.Nav is { } nav) { _targetStation = ms.DockAt; _target = nav; return WithTurn(Autopilot(nav)); }
			if (ms.Waypoint is { } wp)
				return (new Vector2(wp.X - _player.Position.X, wp.Y - _player.Position.Z), ms.Throttle, ClearanceScale(), 0f);
			return (Vector2.Zero, 0f, ClearanceScale(), 0f);
		}
		if (_autopilot && _target is { } target) return WithTurn(Autopilot(target));
		return (Vector2.Zero, 0f, 1f, 0f);
	}

	static (Vector2, float, float, float) WithTurn((Vector2 dir, float throttle, float speedScale) s) => (s.dir, s.throttle, s.speedScale, 0f);

	(Vector2, float, float) Autopilot(NavTarget target)
	{
		var u = _session.U;
		// Phase 10: Route nach dem Mobilitätsprofil des Spielerschiffs (gesperrte Gates werden umflogen, sonst Meldung mit Grund)
		var step = Navigator.Next(u, u.PlayerSector, new Vec2(_player.Position.X, _player.Position.Z), target, PlayerData);
		switch (step.Status)
		{
			case NavStatus.Unreachable:
				_hud.Toast("Kein Weg zum Ziel: " + (GateAccess.BlockReason(u, PlayerData, u.PlayerSector, target.Sector) ?? "Gates gesperrt"));
				CancelAutopilot(); return (Vector2.Zero, 0f, 1f);
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
		float scale = Mathf.Min(Mathf.Clamp(final / 40f, 0.25f, 1f), ClearanceScale());
		return (to, 1f, scale);
	}

	/// <summary>Drosselt den Autopiloten in der Nähe fester Hindernisse, damit er mit Schwung nicht in Asteroiden oder Stationen läuft.</summary>
	float ClearanceScale()
	{
		float edge = float.MaxValue;
		foreach (var o in _view.Obstacles)
			edge = Mathf.Min(edge, new Vector2(_player.Position.X - o.Position.X, _player.Position.Z - o.Position.Y).Length() - o.Radius - Collision.ShipRadius);
		return Mathf.Clamp(edge / 30f, 0.22f, 1f);   // bei Berührungsnähe ~10 u/s, unter der Schadensschwelle
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
		// Phase 10: Kontrollpunkt am Gate — Sperre (mit Grund) oder Zoll aus der Spielerkasse; abgewiesen prallt das Schiff ab
		var check = GateAccess.TryPlayerJump(u, PlayerData, u.Gates[gateId]);
		if (!check.Allowed)
		{
			LastJumpMessage = check.Message;
			var gp = u.Gates[gateId].Position;
			var away = new Vector3(_player.Position.X - gp.X, 0, _player.Position.Z - gp.Y);
			away = away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Back;
			_player.Position = new Vector3(gp.X, 0, gp.Y) + away * (GateRadius + 4f);
			_player.Velocity = away * 6f;
			_jumpCooldown = 2f;
			CancelAutopilot();
			_sfx.Play(SfxId.Click);
			_hud.Toast(check.Message);
			return;
		}
		LastJumpMessage = check.Message;
		if (check.Message.Length > 0) _hud.Toast(check.Message);
		var far = u.Gates[u.Gates[gateId].Target];
		u.MoveShip(PlayerData, far.Sector);
		u.PlayerSector = far.Sector;
		_view.Rebuild(u);
		_mining.ResetNpc(); _mining.ClearPlayer(); _chunks.Clear(); _mineTarget = null;
		_player.GiveGrace(1.5f);

		var gpos = new Vector3(far.Position.X, 0, far.Position.Y);
		var dir = (-gpos).Normalized();
		_player.Position = gpos + dir * 22f;
		_player.Heading = Mathf.Atan2(-dir.X, -dir.Z);
		_player.Velocity = dir * 12f;
		_jumpCooldown = 2f;
		_camOffset = _orbit.Offset();   // gewählte Ansicht bleibt nach dem Sprung erhalten
		SnapCam();
		_hud.Flash(new Color(0.6f, 0.85f, 1f));
		_sfx.Play(SfxId.Jump);
		_hud.Toast(u.Sectors[far.Sector].Name);
	}

	// ───────── Ziele, Autopilot, Andocken ─────────

	void SetTarget(NavTarget target, StationId? station)
	{
		_target = target; _targetStation = station;
		_autopilot = true; _hud.Flight.SetAutopilot(true);
		_sfx.Play(SfxId.Click);
		_hud.Toast($"Autopilot: {target.Label}");
	}

	void ToggleAutopilot()
	{
		_sfx.Play(SfxId.Click);
		if (_target == null) { _hud.Flight.SetAutopilot(false); _hud.Toast("Kein Ziel – Station oder Gate antippen, oder Karte öffnen"); return; }
		_autopilot = _hud.Flight.AutopilotOn;
	}

	void CancelAutopilot()
	{
		if (!_autopilot) return;
		_autopilot = false; _hud.Flight.SetAutopilot(false);
	}

	Vector2? ScreenPositionOfTarget()
	{
		if (_target is not { } t || _session == null) return null;
		var u = _session.U;
		Vector3 world;
		if (t.Sector == u.PlayerSector) world = new Vector3(t.Position.X, 0, t.Position.Y);
		else
		{
			var route = u.FindRoute(u.PlayerSector, t.Sector, GateAccess.ProfileOf(u, PlayerData));
			if (route is not { Count: > 0 }) return null;
			var g = u.Gates[route[0]].Position;
			world = new Vector3(g.X, 0, g.Y);
		}
		if (_cam.IsPositionBehind(world)) return null;
		return _cam.UnprojectPosition(world);
	}

	/// <summary>Nähestes Gate innerhalb von <paramref name="maxDist"/> (für die Kontexttaste „Springen“).</summary>
	GateId? NearestGate(float maxDist)
	{
		GateId? best = null; float bestD = maxDist;
		foreach (var (id, pos) in _view.Gates)
		{
			float d = _player.Position.DistanceTo(pos);
			if (d < bestD) { bestD = d; best = id; }
		}
		return best;
	}

	/// <summary>Kontexttaste: nahe einer Station „Andocken“, am Gate „Springen“ (vorhandene Logik).</summary>
	void ContextAction()
	{
		if (NearestStation(out _) is { } sid) { _sfx.Play(SfxId.Click); Dock(sid); return; }
		if (NearestGate(60f) is { } gid) { _sfx.Play(SfxId.Click); Jump(gid); return; }
		_hud.Toast("Nichts in Reichweite – näher an Station oder Gate fliegen");
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

	/// <summary>Setzt den Dock-Kontext und öffnet das Andock-Menü (erscheint beim Andocken wie bisher das Panel).</summary>
	void ShowDockMenu()
	{
		if (_docked is not { } id || !_session.U.Stations.TryGetValue(id, out var st)) return;
		_hud.DockCtx.U = _session.U;
		_hud.DockCtx.Station = st;
		_hud.DockCtx.Ship = PlayerData;
		_hud.DockCtx.Services = _session.Services;
		_hud.Flight.Visible = false;
		_hud.Screens.Show(_hud.DockMenu);
	}

	/// <summary>Öffnet einen Dock-Bereich: echte Bereiche nur mit Dienst aus <see cref="SpaceSim.Core.Services.StationServices.ForStation"/>, sonst die Platzhalterseite.</summary>
	void OnDockArea(string areaId)
	{
		if (_docked is null) return;
		var u = _session.U;
		var st = u.Stations[_docked.Value];
		var def = DockAreas.Build(u, st, _session.Services).FirstOrDefault(d => d.Id == areaId);
		if (def == null) return;
		if (!def.Available)
		{
			_hud.DockPlaceholder.OpenWith(def);
			_hud.Screens.Show(_hud.DockPlaceholder);
			return;
		}
		IScreen screen = areaId switch
		{
			DockAreas.Trade => _hud.DockTrade,
			DockAreas.Info => _hud.DockInfo,
			DockAreas.Hangar => _hud.DockHangar,
			DockAreas.Repair => _hud.DockRepair,
			DockAreas.Outfitter => _hud.DockOutfitter,
			DockAreas.Shipyard => _hud.DockShipyard,
			DockAreas.Storage => _hud.DockStorage,
			_ => _hud.DockMenu,
		};
		if (screen is DockAreaScreen area) area.AreaId = areaId;
		_hud.Screens.Show(screen);
		_sfx.Play(SfxId.Click);
	}

	/// <summary>Hangar „Übernehmen“ (Phase 5c): wechselt an der angedockten Station in ein dort geparktes eigenes Schiff (Core: <see cref="PlayerFleet.SwitchTo"/>).</summary>
	void SwitchShip(ShipId id)
	{
		if (_docked is not { } sid || !_session.U.Stations.TryGetValue(sid, out var st)) return;
		SyncPlayerData();
		bool ok = PlayerFleet.SwitchTo(_session.U, st, id, out var message);
		_sfx.Play(ok ? SfxId.Dock : SfxId.Click);
		_hud.Toast(message);
		_hud.Screens.Current?.Refresh();
	}

	/// <summary>
	/// Das aktive Schiff hat gewechselt (Kauf an der Werft oder „Übernehmen“ im Hangar): Dock-Kontext auf das neue Schiff, Abbau-
	/// Mikrosimulation und Automatik zurücksetzen. Die Ansicht liest das Schiff ohnehin über <see cref="GameSession.Player"/>; das alte
	/// Schiff liegt jetzt geparkt an der Station und erscheint wie andere Schiffe in der Sektoransicht.
	/// </summary>
	void OnActiveShipChanged(ActiveShipChanged e)
	{
		_hud.DockCtx.Ship = PlayerData;
		_chunks.Clear(); _mineTarget = null; _mining.ClearPlayer();
		SetAutoMining(false, announce: false);
		_lowestHull = PlayerData.Hull;
	}

	/// <summary>Andocken: Das Schiff fliegt in die Station hinein und verschwindet (unsichtbar, keine Kollision); die Hülle wird repariert.</summary>
	void Dock(StationId id)
	{
		var u = _session.U;
		var st = u.Stations[id];
		_docked = id;
		_transition = true;
		_player.Velocity = Vector3.Zero;
		_miningStep = null;

		var center = new Vector3(st.Position.X, 0, st.Position.Y);
		var tw = CreateTween();
		tw.TweenProperty(_player, "position", center, 0.7);
		tw.Parallel().TweenProperty(_player, "scale", Vector3.One * 0.1f, 0.7);
		tw.TweenCallback(Callable.From(() => { _player.Visible = false; _transition = false; }));

		bool repaired = PlayerData.Hull < PlayerData.MaxHull - 0.5f;
		PlayerData.Hull = PlayerData.MaxHull;
		_sfx.Play(SfxId.Dock);
		ShowDockMenu();
		if (repaired) _hud.Toast("Hülle repariert");
		if (_miner.Enabled)
		{
			var sale = _miner.OnDocked(u, PlayerData, st);
			if (sale is { Revenue: > 0 }) { _sfx.Play(SfxId.Pickup); _hud.Toast($"Verkauft für {sale.Revenue:0} Credits"); }
			_hud.DockMenu.Refresh();   // Anzeige aktualisieren (Credits/Fracht nach dem Auto-Verkauf)
			// Nach kurzer Pause automatisch abdocken und weiterarbeiten
			var timer = GetTree().CreateTimer(1.8);
			timer.Timeout += () => { if (_docked == id) Undock(); };
			if (!_miner.Enabled) { _hud.Flight.SetAutoMine(false); _hud.Toast(_miner.Status, 3.5f); }
		}
	}

	/// <summary>Abdocken: Das Schiff taucht aus der Station auf und fliegt zum Parkpunkt; danach kurze Schonfrist ohne Schaden. Funktioniert aus jedem Dock-Bereich.</summary>
	void Undock()
	{
		if (_docked is not { } id || !_session.U.Stations.TryGetValue(id, out var st)) return;
		_sfx.Play(SfxId.Click);
		_docked = null;
		_transition = true;
		_hud.Screens.Hide();          // schließt Andock-Menü und jeden offenen Bereich
		_hud.Flight.Visible = true;

		var spot = st.DockPoint(_session.U.PlayerShip);
		_player.Position = new Vector3(st.Position.X, 0, st.Position.Y);
		_player.Scale = Vector3.One * 0.1f;
		_player.Visible = true;
		var outward = new Vector3(spot.X - st.Position.X, 0, spot.Y - st.Position.Y).Normalized();
		_player.Heading = Mathf.Atan2(-outward.X, -outward.Z);

		var tw = CreateTween();
		tw.TweenProperty(_player, "position", new Vector3(spot.X, 0, spot.Y), 0.7);
		tw.Parallel().TweenProperty(_player, "scale", Vector3.One, 0.7);
		tw.TweenCallback(Callable.From(() => { _transition = false; _player.Velocity = outward * 8f; _player.GiveGrace(2.5f); }));
	}

	// ───────── Zeiger in der Welt und Kamera-Gesten ─────────

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_hud == null || _hud.AnyModalOpen) return;   // Menüs/Dock: Eingaben gehören der Oberfläche, Kamera bleibt stehen
		if (@event is InputEventKey { Pressed: true, Echo: false } key)
		{
			if (key.Keycode == Key.F) _fireRequested = true;
			else if (key.Keycode == Key.G) { _tractorOn = !_tractorOn; _sfx.Play(SfxId.Click); }
			else if (key.Keycode == Key.M) SetAutoMining(!_miner.Enabled);
			return;
		}

		if (!Pointer.TouchMode)
		{
			// Maus: Ziehen (linke, rechte oder mittlere Taste) = Kamera schwenken, Mausrad = Zoom; kurzer Linksklick = Ziel wählen
			if (@event is InputEventMouseButton mb && (mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle))
			{
				_camMouseDrag = mb.Pressed;
				return;
			}
			if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
			{
				_orbit.Zoom(wheel.ButtonIndex == MouseButton.WheelUp ? WheelZoomIn : WheelZoomOut);
				return;
			}
			if (@event is InputEventMouseMotion mm && _camMouseDrag)
			{
				_orbit.OrbitDrag(mm.Relative.X, mm.Relative.Y);
				return;
			}
			if (!Pointer.TryRead(@event, out int idx, out var pos, out bool pressed, out bool motion)) return;
			HandlePointer(idx, pos, pressed, motion);
			return;
		}

		// Touch: alle Finger verfolgen. Ein Finger: Wischen = Kamera schwenken, kurzes Antippen = Ziel wählen (Touch steuert nie das Schiff).
		// Zwei Finger: Pinch-Zoom (und Schwenken über den Schwerpunkt)
		if (@event is InputEventScreenTouch t)
		{
			if (t.Pressed)
			{
				_touch[t.Index] = t.Position;
				if (_touch.Count >= 2) { _pointerFinger = -1; _pointerDragging = false; _pinchLast = PinchDistance(); }   // zweiter Finger: Kamera-Geste, Welt-Zeiger endet
				else HandlePointer(t.Index, t.Position, pressed: true, motion: false);
			}
			else
			{
				_touch.Remove(t.Index);
				if (_pointerFinger == t.Index) HandlePointer(t.Index, t.Position, pressed: false, motion: false);
				if (_touch.Count < 2) _pinchLast = 0f;
			}
			return;
		}
		if (@event is InputEventScreenDrag d && _touch.TryGetValue(d.Index, out var prev))
		{
			var delta = d.Position - prev;
			_touch[d.Index] = d.Position;
			if (_touch.Count >= 2)
			{
				_orbit.OrbitDrag(delta.X / _touch.Count, delta.Y / _touch.Count);   // Schwerpunkt der aktiven Finger
				float pinch = PinchDistance();
				if (_pinchLast > 0f) _orbit.Zoom(_pinchLast / Mathf.Max(pinch, 1f));   // Finger auseinander = hineinzoomen
				_pinchLast = pinch;
			}
			else HandlePointer(d.Index, d.Position, pressed: true, motion: true);
		}
	}

	/// <summary>Abstand der ersten beiden aktiven Touch-Finger (Pinch); 0 bei weniger als zwei Fingern.</summary>
	float PinchDistance()
	{
		if (_touch.Count < 2) return 0f;
		var e = _touch.Values.GetEnumerator();
		e.MoveNext(); var a = e.Current;
		e.MoveNext();
		return a.DistanceTo(e.Current);
	}

	/// <summary>Maximale Bewegung (px) und Dauer (s), bis ein Druck kein Antippen mehr ist, sondern ein Wisch.</summary>
	const float TapMaxMove = 14f;
	const double TapMaxTime = 0.45;

	/// <summary>
	/// Welt-Zeiger (ein Finger / linke Maustaste): Wischen über <see cref="TapMaxMove"/> schwenkt die Kamera, ein kurzes
	/// Antippen ohne Bewegung wählt das Ziel unter dem Zeiger. Der Zeiger gibt dem Schiff nie eine Flugrichtung.
	/// </summary>
	void HandlePointer(int idx, Vector2 pos, bool pressed, bool motion)
	{
		if (!motion && pressed && _pointerFinger < 0)
		{
			_pointerFinger = idx; _pointerPos = _pointerStart = pos; _pointerDragging = false;
			_pointerDownAt = Time.GetTicksMsec() / 1000.0;
		}
		else if (idx == _pointerFinger && motion)
		{
			if (!_pointerDragging && pos.DistanceTo(_pointerStart) > TapMaxMove) _pointerDragging = true;
			if (_pointerDragging) { var d = pos - _pointerPos; _orbit.OrbitDrag(d.X, d.Y); }
			_pointerPos = pos;
		}
		else if (idx == _pointerFinger && !pressed)
		{
			bool tap = !_pointerDragging && Time.GetTicksMsec() / 1000.0 - _pointerDownAt < TapMaxTime;
			_pointerFinger = -1; _pointerDragging = false;
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
		var sector = _session.U.Sectors[_session.U.PlayerSector];
		Asteroid? rock = null; float rockD = 3f;
		foreach (var a in sector.Asteroids)
		{
			float d = new Vector2(ground.X - a.Position.X, ground.Z - a.Position.Y).Length() - a.Radius;
			if (d < rockD) { rockD = d; rock = a; }
		}
		if (rock != null)
		{
			if (rock.Minable) { _mineTarget = rock; _sfx.Play(SfxId.Click); _hud.Toast($"Laserziel gesetzt (Erz {rock.Ore:0}) – jeder Tipp auf Feuer (Taste F) ist ein Schuss"); }
			else _hud.Toast("Kein Erz in diesem Asteroiden");
			return;
		}
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

	/// <summary>Harter Aufprall auf Asteroid/Station (Schwelle <see cref="PlayerShip.HardImpact"/>); leichte Stöße prallen nur ab.</summary>
	void OnPlayerHit(float impact, Vector3 point)
	{
		_hitCount++;
		var uu = _session.U;
		var near = _view.Obstacles.OrderBy(o => new Vector2(point.X - o.Position.X, point.Z - o.Position.Y).Length() - o.Radius).First();
		_lastHit = $"Aufprall {impact:0.0} u/s bei ({point.X:0},{point.Z:0}) in {uu.Sectors[uu.PlayerSector].Name}, Tempo {_player.Velocity.Length():0}, " +
		           $"Auto-Mining={_miner.Enabled} ({_miner.Status}), Hindernis r={near.Radius:0.0} bei ({near.Position.X:0},{near.Position.Y:0}), Grace={_player.Grace:0.0}, Step={(_miningStep is { } ms ? $"nav={ms.Nav?.Label} wp={ms.Waypoint?.X:0},{ms.Waypoint?.Y:0} thr={ms.Throttle:0.0} fire={ms.Fire}" : "keiner")}, Autopilot={_autopilot}, Ziel={_target?.Label}, Transition={_transition}, Docked={_docked}, Clear={ClearanceScale():0.00}";
		_lowestHull = Mathf.Min(_lowestHull, PlayerData.Hull);
		PlayerData.Hull = Mathf.Max(0f, PlayerData.Hull - (impact - PlayerShip.HardImpact) * 0.5f - 2f);
		_sinceHit = 0f;
		_hitFlash = 0.4f;
		_sfx.Play(SfxId.Hit);
		_fx.Burst(point, new Color(1f, 0.7f, 0.3f), 14, 14f);
		_hud.Flash(new Color(1f, 0.2f, 0.1f), 0.35f);
		if (PlayerData.Hull <= 0f) Respawn();
	}

	/// <summary>Hülle zerstört: Explosion, Reparatur und Neustart an der Station des Sektors (mit Schonfrist).</summary>
	void Respawn()
	{
		var u = _session.U;
		_fx.Burst(_player.Position, new Color(1f, 0.5f, 0.2f), 60, 28f, 1.2f);
		_sfx.Play(SfxId.Explosion);
		var sector = u.Sectors[u.PlayerSector];
		var spot = sector.Stations.Count > 0 ? u.Stations[sector.Stations[0]].DockPoint(u.PlayerShip) : new Vec2(0, 0);
		_player.Position = new Vector3(spot.X, 0, spot.Y);
		_player.Velocity = Vector3.Zero;
		_player.GiveGrace(3f);
		PlayerData.Hull = PlayerData.MaxHull;
		_chunks.Clear();
		CancelAutopilot();
		if (_miner.Enabled) SetAutoMining(false, "Auto-Mining unterbrochen (Schiff zerstört)");
		_hud.Toast("Schiff zerstört – repariert an der Station", 3.5f);
	}

	/// <summary>
	/// Ein Spielerschuss ist gefallen: Ton, Laserblitz, bei Treffer Funken – und Rückstoß: Das Schiff wird
	/// vom Asteroiden weggestoßen (echte Geschwindigkeitsänderung, deshalb bremst die Automatik im Haltemodus aktiv).
	/// </summary>
	void OnShot(Asteroid rock, LaserShot.Result result)
	{
		float units = result.Units;
		var pos = new Vec2(_player.Position.X, _player.Position.Z);
		var toShip = (pos - rock.Position).Normalized();
		var surface = new Vector3(rock.Position.X + toShip.X * rock.Radius, 0, rock.Position.Y + toShip.Y * rock.Radius);

		_sfx.Play(SfxId.Zap);
		_mining.FirePlayer(surface, Time.GetTicksMsec() / 1000.0);
		_player.Velocity += new Vector3(toShip.X, 0, toShip.Y) * MiningRules.RecoilImpulse;
		if (units > 0f) _fx.Burst(surface, GoodColor(result.Good), 5 + (int)units, 7f, 0.5f);
	}

	Color GoodColor(GoodId good) => _session.U.Goods.TryGetValue(good, out var g) ? Color.FromHtml(g.Color) : new Color(1f, 0.75f, 0.3f);

	/// <summary>Ein NPC-Miner hat laut Simulation geschossen. Im Spielersektor zeigen wir Blitz, fliegenden Brocken und Rückstoß.</summary>
	void OnMinerShot(MinerShot shot)
	{
		var shipPos = _view.ShipPosition(shot.Ship);
		if (shipPos is not { } sp) return;
		var rockPos = new Vector3(shot.AsteroidPosition.X, 0, shot.AsteroidPosition.Y);
		var away = (sp - rockPos).Normalized();
		var surface = rockPos + away * shot.AsteroidRadius;

		_mining.NpcShot(shot.Ship, surface, shot.Units, shot.Good, Time.GetTicksMsec() / 1000.0);
		_view.Kick(shot.Ship, away * (MiningRules.RecoilImpulse * 0.4f));   // Rückstoß, verhältnismäßig zum Spielerschiff
		if (shot.Units > 0f) _fx.Burst(surface, GoodColor(shot.Good), 4 + (int)shot.Units, 6f, 0.4f);
	}

	// ───────── Flug-HUD: Daten aus der Simulation an die Oberfläche ─────────

	BayView[] _bayViews = Array.Empty<BayView>();
	BayState[] _bayStates = Array.Empty<BayState>();
	/// <summary>Schächte des aktiven Schiffs (Phase 8: mit Spaltlaser kommt der Schacht „Spalter“ dazu).</summary>
	IReadOnlyList<WeaponBay> _bays = WeaponBays.PlayerDefault;
	/// <summary>Nachladeuhr des Spaltlasers in Sekunden (0 = bereit).</summary>
	float _splitReload;
	string _lastSplitIssue = "";
	Vec2? _bayAimPos;      // aktuelles Laserziel (für Winkel/Anzeigen)
	float _bayAimRadius;

	void UpdateHud()
	{
		var u = _session.U;
		var sector = u.Sectors[u.PlayerSector];
		var pos = new Vec2(_player.Position.X, _player.Position.Z);
		float headingDeg = Mathf.RadToDeg(_player.Heading);
		UpdateBays(sector, pos, headingDeg);

		var d = new FlightHudData
		{
			Hull = PlayerData.Hull,
			Fuel = PlayerData.TotalFuel,
			FuelCapacity = PlayerData.Stats.FuelCapacity,
			Credits = _session.U.WalletOf(PlayerData).Credits,   // Phase 5b: Spielerkasse
			SectorName = sector.Name,
			FactionName = u.Factions.TryGetValue(sector.Owner, out var f) ? f.Name : "",
			AutopilotOn = _autopilot,
			AutoMineOn = _miner.Enabled,
			TractorOn = _tractorOn,
			FastOn = _fast,
			SliderValue = _hud.Flight.SliderValue,
			Speed = _player.Velocity.Length(),
			Bays = _bayViews,
		};

		// Zielkarte: Autopilot-Ziel, sonst Laserziel
		Vec2? targetWorld = null;
		if (_target is { } t)
		{
			d.TargetName = t.Label;
			d.TargetHasInfo = true;
			if (t.Sector == u.PlayerSector)
			{
				targetWorld = t.Position;
				d.TargetDistance = Vec2.Distance(pos, t.Position);
				d.TargetInfo = $"Entfernung {d.TargetDistance:0} m";
			}
			else d.TargetInfo = "anderer Sektor – über Gate fliegen";
		}
		else if (_mineTarget is { } rock && _bayAimPos is { } aim)
		{
			targetWorld = aim;
			d.TargetName = "Laserziel: Asteroid";
			d.TargetDistance = Vec2.Distance(pos, aim) - rock.Radius;
			d.TargetInfo = $"Erz {rock.Ore:0}/{rock.MaxOre:0} · Kante {d.TargetDistance:0} m";
			d.TargetHasInfo = true;
		}
		if (targetWorld is { } tw)
		{
			float rel = FiringArc.Normalize180(FiringArc.Bearing(tw - pos) - headingDeg);
			d.TargetRelBearing = rel;
			d.TargetScreen = _cam.IsPositionBehind(new Vector3(tw.X, 0, tw.Y)) ? null : _cam.UnprojectPosition(new Vector3(tw.X, 0, tw.Y));
			d.TargetInArc = _bayStates.Length > 0 && _bayStates[0] is BayState.Ready or BayState.Reloading;
			var bay = WeaponBays.PlayerDefault[0];
			d.TargetArc = _bayStates.Length > 0 && _bayStates[0] == BayState.OutsideArc && _bayAimPos is { } a
				? TurnHintText(bay, headingDeg, pos, a)
				: _bayStates.Length > 0 && _bayStates[0] == BayState.Ready ? "Im Feuerwinkel: Laser" : "";
		}

		// Kontexttaste: nahe Station „Andocken“, am Gate „Springen“
		if (NearestStation(out float sd) is { } sid && sd < 55f)
			d.ContextLabel = $"Andocken: {u.Stations[sid].Name}";
		else if (NearestGate(60f) is { } gid)
		{
			var far = u.Gates[u.Gates[gid].Target];
			d.ContextLabel = $"Springen: {u.Sectors[far.Sector].Name}";
		}

		// Feuerwinkel-Kegel um das Schiff: Bildschirmwinkel der Nase direkt projiziert (stimmt mit gedrehter Kamera)
		d.ShipScreen = _cam.IsPositionBehind(_player.Position) ? null : _cam.UnprojectPosition(_player.Position);
		if (d.ShipScreen is { } sp0)
		{
			var nose = Vector3.Forward.Rotated(Vector3.Up, _player.Heading);
			var sp1 = _cam.UnprojectPosition(_player.Position + nose * 10f);
			d.ShipScreenNoseDeg = Mathf.RadToDeg(Mathf.Atan2(sp1.Y - sp0.Y, sp1.X - sp0.X));
		}

		int ready = 0, fire = 0;
		var bays = _bays;
		for (int i = 0; i < _bayViews.Length; i++)
		{
			if (bays[i].Key is "tractor" or "split") continue;   // Traktor ist Umschalter, der Spalter feuert nur einzeln
			fire++;
			if (_bayStates[i] == BayState.Ready) ready++;
		}
		d.ReadyBays = ready;
		d.FireBays = fire;

		_hud.Flight.Refresh(d);
	}

	/// <summary>Zustände der Waffenschächte als reine Core-Funktion (FiringArc) auswerten.</summary>
	void UpdateBays(Sector sector, Vec2 pos, float headingDeg)
	{
		_bays = WeaponBays.For(PlayerData.Stats);
		var bays = _bays;
		_bayViews = new BayView[bays.Count];
		_bayStates = new BayState[bays.Count];
		for (int i = 0; i < bays.Count; i++)
		{
			var bay = bays[i];
			Vec2? target = null;
			float radius = 0f;
			bool reloaded = true;
			if (bay.Key == "laser")
			{
				var aim = _mineTarget ?? _chunks.Nearest(sector, pos, MiningRules.LaserRange);
				target = aim?.Position;
				radius = aim?.Radius ?? 0f;
				reloaded = _chunks.Ready;
				if (i == 0) { _bayAimPos = target; _bayAimRadius = radius; }
			}
			else if (bay.Key == WeaponBays.Splitter.Key)
			{
				// Spalter: gewähltes oder nächstes Erz-Ziel in Reichweite, eigene Nachladeuhr
				var aim = _mineTarget ?? _chunks.Nearest(sector, pos, MiningRules.LaserRange);
				target = aim?.Position;
				radius = aim?.Radius ?? 0f;
				reloaded = _splitReload <= 0f;
			}
			else   // Traktor: Ziel ist die nächste Beute in Reichweite
			{
				OreChunk? best = null; float bd = bay.Range;
				foreach (var c in _chunks.Chunks)
				{
					float dd = Vec2.Distance(c.Position, pos);
					if (dd <= bd) { bd = dd; best = c; }
				}
				target = best?.Position;
			}
			float progress = bay.Key == WeaponBays.Splitter.Key ? 1f - _splitReload / WeaponBays.SplitReloadSeconds : _chunks.ReloadProgress;
			var st = FiringArc.State(bay, headingDeg, pos, target, radius, reloaded, progress);
			_bayStates[i] = st;
			string place = bay.IsTurret ? "Turm · 360°" : $"{FacingName(bay.Facing)} · ±{(int)bay.ArcDegrees}°";
			string detail = st switch
			{
				BayState.Ready => bay.Key == "tractor" ? (_tractorOn ? "AN" : "aus") : "FEUER",
				BayState.OutsideArc => TurnHintText(bay, headingDeg, pos, target!.Value),
				BayState.Reloading => "lädt nach",
				_ => bay.Key == "tractor" ? "keine Beute" : "kein Ziel",
			};
			_bayViews[i] = new BayView { Name = bay.Name, Place = place, Detail = bay.Key == WeaponBays.Splitter.Key && st == BayState.Ready ? "SPALTEN" : detail, State = st, Reload = progress, Facing = bay.Facing, ArcDegrees = bay.ArcDegrees };
		}
	}

	static string FacingName(Facing f) => f switch
	{
		Facing.Bow => "Bug", Facing.Port => "Backbord", Facing.Starboard => "Steuerbord", _ => "Heck",
	};

	/// <summary>Drehhinweis als Text: „◀ 41° drehen“ (links/Backbord) bzw. „▶ 41° drehen“.</summary>
	static string TurnHintText(WeaponBay bay, float headingDeg, Vec2 pos, Vec2 target)
	{
		float h = FiringArc.TurnHintDegrees(bay, headingDeg, pos, target);
		if (h > 0.5f) return $"◀ {h:0}° drehen";
		if (h < -0.5f) return $"▶ {-h:0}° drehen";
		return "drehen";
	}

	/// <summary>Finger auf einer Waffenschacht-Taste: Laser feuert einen Schuss (nur bereit und im Winkel), Traktor ist Umschalter.</summary>
	void OnBayDown(int index)
	{
		var bays = _bays;
		if (index < 0 || index >= bays.Count) return;
		if (_docked != null || _hud.AnyModalOpen) return;
		if (bays[index].Key == WeaponBays.Splitter.Key) { FireSplitter(index); return; }
		if (bays[index].Key == "tractor")
		{
			_tractorOn = !_tractorOn;
			_sfx.Play(SfxId.Click);
			_hud.Toast(_tractorOn ? "Traktorstrahl an" : "Traktorstrahl aus");
			return;
		}
		switch (_bayStates[index])
		{
			case BayState.Ready: _fireRequested = true; break;
			case BayState.OutsideArc:
				if (_bayAimPos is { } a) Hint(TurnHintText(bays[index], Mathf.RadToDeg(_player.Heading), new Vec2(_player.Position.X, _player.Position.Z), a));
				break;
			case BayState.Reloading: break;   // lädt noch – ein weiterer Schuss ist nicht möglich
			default: Hint("Kein Ziel in Reichweite"); break;
		}
	}

	/// <summary>
	/// Phase 8: Spaltschuss des Spaltlasers (Asteroidenbrecher) auf das Laserziel — gleiche Core-Regel wie bei NPC-Brechern
	/// (<see cref="AsteroidSplitter.Fire"/>): Fragmente erscheinen über den gespiegelten Asteroiden der Sektoransicht.
	/// </summary>
	public bool FireSplitter(int index)
	{
		_lastSplitIssue = "";
		if (index < 0 || index >= _bayStates.Length) { _lastSplitIssue = "kein Schacht"; return false; }
		if (_bayStates[index] == BayState.Reloading) { _lastSplitIssue = "lädt nach"; return false; }
		var aim = _mineTarget ?? _chunks.Nearest(_session.U.Sectors[_session.U.PlayerSector], new Vec2(_player.Position.X, _player.Position.Z), MiningRules.LaserRange);
		if (aim is null) { Hint("Kein Ziel in Reichweite"); return false; }
		SyncPlayerData();
		var (frags, issue) = AsteroidSplitter.Fire(_session.U, PlayerData, aim.Id);
		if (frags is null) { _lastSplitIssue = issue ?? "Spalten nicht möglich"; Hint(_lastSplitIssue); _sfx.Play(SfxId.Click); return false; }
		_splitReload = WeaponBays.SplitReloadSeconds;
		_mineTarget = frags.OrderByDescending(f => f.Ore).First();
		_fx.Burst(new Vector3(aim.Position.X, 0, aim.Position.Y), new Color(1f, 0.6f, 0.25f), 24, 10f, 0.6f);
		_sfx.Play(SfxId.Pickup);
		_hud.Toast($"Asteroid gespalten: {frags.Count} Fragmente");
		return true;
	}

	/// <summary>„Alle Feuer“: löst nur bereite Waffen im Winkel aus (hier: der Laser).</summary>
	void FireReadyBays()
	{
		var bays = _bays;
		bool any = false;
		for (int i = 0; i < bays.Count && i < _bayStates.Length; i++)
			if (bays[i].Key is not ("tractor" or "split") && _bayStates[i] == BayState.Ready) { _fireRequested = true; any = true; }
		if (!any) Hint("Keine Waffe bereit und im Winkel");
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

	// ───────── Abbau ─────────

	void SetAutoMining(bool on, string? message = null, bool announce = true)
	{
		if (on)
		{
			CancelAutopilot(); _target = null; _targetStation = null;
			_miner.Start();
			_hud.Flight.SetAutoMine(true);
			if (announce) _hud.Toast("Auto-Mining an – Slider oder Wenden bewegen zum Abbrechen");
		}
		else
		{
			if (_miner.Enabled) _miner.Stop(message ?? "");
			_hud.Flight.SetAutoMine(false);
			if (announce && message != null) _hud.Toast(message);
		}
	}

	void Hint(string text)
	{
		if (_hintCooldown > 0f) return;
		_hintCooldown = 1.5f;
		_hud.Toast(text, 1.5f);
	}

	/// <summary>Laser und Traktorstrahl des Spielers (manuell oder durch den Automodus), Brocken, Strahldarstellung, NPC-Laser.</summary>
	void UpdateMining(float delta, bool frozen)
	{
		var u = _session.U;
		var ship = PlayerData;
		var sector = u.Sectors[u.PlayerSector];
		var pos = new Vec2(_player.Position.X, _player.Position.Z);
		_hintCooldown = Mathf.Max(0f, _hintCooldown - delta);
		if (_mineTarget != null && !_chunks.IsMinable(sector, _mineTarget)) _mineTarget = null;

		bool fire = false, tractor = _tractorOn;
		Asteroid? aim = null;
		if (!frozen)
		{
			if (_miningStep is { } ms) { fire = ms.Fire; tractor |= ms.Tractor; aim = ms.Aim; }   // Automatik: feuert, sobald nachgeladen
			else
			{
				fire = _fireRequested;   // Spieler: ein Tipp / Tastendruck = ein Schuss
				aim = _mineTarget ?? _chunks.Nearest(sector, pos, MiningRules.LaserRange);
			}
		}
		_fireRequested = false;

		if (fire)
		{
			if (aim == null) Hint("Kein abbaubarer Asteroid in Reichweite");
			else switch (_chunks.TryFire(u, ship, sector, aim, pos))
			{
				case MineResult.OutOfRange: Hint("Zu weit entfernt – näher heranfliegen"); break;
				case MineResult.CargoFull: Hint("Laderaum voll"); break;
				case MineResult.NotMinable: Hint("Kein Erz mehr in diesem Asteroiden"); break;
				case MineResult.NoLaser: Hint("Kein Mining-Laser eingebaut – Ausrüster an Werft oder Handelsstation"); break;
				// Fired: OnShot erledigt Ton, Blitz und Rückstoß; Cooldown: Waffe lädt noch – stillschweigend ignorieren
			}
		}

		tractor &= !frozen;
		if (_chunks.Update(u, ship, pos, tractor, delta) > 0f) _sfx.Play(SfxId.Pickup);

		var shown = fire && aim != null ? aim : _mineTarget;
		if (shown != null && sector.Asteroids.Contains(shown))
			_mining.SetTarget(new Vector3(shown.Position.X, 0, shown.Position.Y), shown.Radius);
		else _mining.SetTarget(null, 0f);

		double time = Time.GetTicksMsec() / 1000.0;
		_mining.UpdatePlayer(_player.Position, tractor, _chunks.Chunks, time);
		_mining.UpdateNpc(_view.ShipPosition, time);
	}

	// ───────── Zugriff für den Selbsttest ─────────

	internal GameSession Session => _session;
	internal Hud HudRef => _hud;
	internal PlayerShip Player => _player;
	internal SectorView View => _view;
	internal bool Docked => _docked != null;
	internal StationId? TestDockedStation => _docked;
	/// <summary>Bereichsliste der angedockten Station (Daten aus ForStation) – Selbsttest prüft Handel/Platzhalter.</summary>
	internal IReadOnlyList<DockAreaDef> TestDockAreas() =>
		_docked is { } id && _session.U.Stations.TryGetValue(id, out var st)
			? DockAreas.Build(_session.U, st, _session.Services)
			: Array.Empty<DockAreaDef>();
	/// <summary>Öffnet einen Dock-Bereich wie ein Tipp im Menü (Selbsttest/Screenshots).</summary>
	internal void TestOpenDockArea(string areaId) => OnDockArea(areaId);
	internal void TestCommand(string action) => OnCommandAction(action);
	/// <summary>Dockt an der nächsten Station an (Screenshots).</summary>
	internal void TestDockNearest() => DockAtNearest();
	/// <summary>Ist der Bildschirm mit dieser Id gerade exklusiv geöffnet?</summary>
	internal bool TestScreenOpen(string id) => _hud.Screens.TryGet(id, out var s) && _hud.Screens.Current == s;
	internal void TestSetStationTarget(StationId id) { var u = _session.U; SetTarget(Navigator.ForStation(u, id, u.PlayerShip), id); }
	internal void TestUndock() => Undock();
	internal void TestSwitchShip(ShipId id) => SwitchShip(id);
	internal bool TestSave() { SaveNow(manual: false); return SaveService.Exists; }
	internal void TestMenu(string a) => OnMenu(a);
	internal void TestStartMining() => SetAutoMining(true);
	internal float TestCargoUnits => PlayerData.Cargo.Values.Sum();
	internal int TestSales => _sales;
	internal int TestHits => _hitCount;
	internal string TestLastHit => _lastHit;
	internal float TestLowestHull => _lowestHull;
	internal bool TestMinerEnabled => _miner.Enabled;
	internal string TestMinerStatus => _miner.Status;

	// ── Hooks für Slider-, Waffen- und Screenshot-Prüfungen ──

	/// <summary>Setzt den Tempo-Slider (Test: 50 % beschleunigt, -1 = Rückwärtsgrenze 20 %).</summary>
	internal void TestSetSlider(float v) => _hud.Flight.SetSlider(v);
	internal float TestSliderValue => _hud.Flight.SliderValue;
	/// <summary>Zielfluggeschwindigkeit des aktuellen Sliderwerts (Core-Funktion Throttle).</summary>
	internal float TestTargetSpeed => Throttle.TargetSpeed(_hud.Flight.SliderValue, _testReverse, PlayerShip.MaxSpeed);
	bool _testReverse;
	internal void TestSetReverse(bool held) => _testReverse = held;
	/// <summary>Nase-X nach einer Rechts-Drehung aus Kurs 0: muss positiv (rechts auf dem Bildschirm) sein.</summary>
	internal float TestRightTurnNoseX => Vector3.Forward.Rotated(Vector3.Up, PlayerShip.HeadingChange(1f, 0.1f)).X;
	/// <summary>Geschwindigkeit entlang der Schiffsnase (negativ = rückwärts).</summary>
	internal float TestForwardSpeed => _player.Velocity.Dot(Vector3.Forward.Rotated(Vector3.Up, _player.Heading));
	/// <summary>Teleportiert das Schiff neben den nächsten abbaubaren Asteroiden und setzt das Laserziel.</summary>
	internal void TestAimAtNearestRock()
	{
		var u = _session.U;
		var sector = u.Sectors[u.PlayerSector];
		var rock = sector.Asteroids.FirstOrDefault(a => a.Minable);
		if (rock == null) return;
		var pos = rock.Position + new Vec2(rock.Radius + 15f, 0);
		_player.Position = new Vector3(pos.X, 0, pos.Y);
		_player.Velocity = Vector3.Zero;
		_player.GiveGrace(5f);
		_mineTarget = rock;
		CancelAutopilot();
		if (_miner.Enabled) SetAutoMining(false);
		_camOffset = _orbit.Offset();
		SnapCam();
	}
	/// <summary>Teleportiert das Schiff in Andocknähe der nächsten Station (Kontexttaste wird sichtbar).</summary>
	internal void TestTeleportNearStation()
	{
		var u = _session.U;
		Station? best = null; float bd = float.MaxValue;
		var here = new Vec2(_player.Position.X, _player.Position.Z);
		foreach (var sid in u.Sectors[u.PlayerSector].Stations)
		{
			var st = u.Stations[sid];
			if (st.Market.Count == 0) continue;   // Phase 10: Regierungsstationen ohne Markt überspringen (Test/Bildschirmfotos wollen Handel)
			float d = Vec2.Distance(here, st.Position);
			if (d < bd) { bd = d; best = st; }
		}
		if (best == null) return;
		var st2 = best;
		float r = 12f;
		foreach (var o in _view.Obstacles)
			if (new Vector2(o.Position.X - st2.Position.X, o.Position.Y - st2.Position.Y).Length() < 1f) r = o.Radius;
		var pos = st2.Position + new Vec2(r + 25f, 0);
		_player.Position = new Vector3(pos.X, 0, pos.Y);
		_player.Velocity = Vector3.Zero;
		_player.GiveGrace(5f);
		_mineTarget = null;
		CancelAutopilot();
		_camOffset = _orbit.Offset();
		SnapCam();
	}
	/// <summary>Ein Schuss aus der Waffenschacht-Taste (Test: Zustand wechselt danach auf Nachladen).</summary>
	internal void TestFireWeapon() => _fireRequested = true;
	/// <summary>Phase 8: Setzt das Schiff in einen anderen Sektor (wie ein Sprung, ohne Gate) – Test des Lagerhauses in Gamma.</summary>
	internal void TestEnterSector(SectorId sector)
	{
		var u = _session.U;
		if (u.PlayerSector == sector) return;
		u.MoveShip(PlayerData, sector);
		u.PlayerSector = sector;
		_view.Rebuild(u);
		_mining.ResetNpc(); _mining.ClearPlayer(); _chunks.Clear(); _mineTarget = null;
	}
	/// <summary>Phase 10: Sprung durch ein Gate des aktuellen Sektors auslösen (wie das Einfliegen ins Gate); false = abgewiesen.</summary>
	internal bool TestJumpThrough(GateId gate)
	{
		var before = _session.U.PlayerSector;
		_jumpCooldown = 0f;
		Jump(gate);
		return _session.U.PlayerSector != before;
	}
	/// <summary>Phase 8: Teleport in Andocknähe einer bestimmten Station (auch Baustelle) und Andocken.</summary>
	internal void TestDockAt(StationId id) { TestTeleportNear(id); Dock(id); }
	/// <summary>Phase 8: Teleport in Andocknähe einer bestimmten Station (Bildschirmfotos der Baustelle im Flug).</summary>
	internal void TestTeleportNear(StationId id)
	{
		var st = _session.U.Stations[id];
		var pos = st.Position + new Vec2(Station.BodyRadius + 20f, 0);
		_player.Position = new Vector3(pos.X, 0, pos.Y);
		_player.Velocity = Vector3.Zero;
		_player.GiveGrace(5f);
		_mineTarget = null;
		CancelAutopilot();
		_camOffset = _orbit.Offset();
		SnapCam();
	}
	/// <summary>Phase 8: Schiff neben den größten Erz-Asteroiden eines Gürtels setzen und ihn als Ziel wählen (Spalter-Test).</summary>
	internal Asteroid? TestAimAtBiggestRock()
	{
		var u = _session.U;
		var sector = u.Sectors[u.PlayerSector];
		var rock = sector.Asteroids.Where(a => a.Minable && sector.FieldFor(a.Position) is { } f && f.ClassKey == a.ClassKey)
			.OrderByDescending(a => a.Radius).ThenBy(a => a.Id).FirstOrDefault();
		if (rock == null) return null;
		var pos = rock.Position + new Vec2(rock.Radius + 15f, 0);
		_player.Position = new Vector3(pos.X, 0, pos.Y);
		_player.Velocity = Vector3.Zero;
		_player.GiveGrace(5f);
		_mineTarget = rock;
		CancelAutopilot();
		if (_miner.Enabled) SetAutoMining(false);
		return rock;
	}
	/// <summary>Phase 8: Schächte des aktiven Schiffs (Schlüssel) und Spaltschuss über die Schacht-Taste.</summary>
	internal IReadOnlyList<string> TestBayKeys => _bays.Select(b => b.Key).ToList();
	internal void TestPressBay(int index) { _lastSplitIssue = "nicht ausgelöst"; OnBayDown(index); }
	internal string TestLastSplitIssue => _lastSplitIssue;
	internal bool TestLaserReady => _chunks.Ready;
	internal string TestBayState(int i) => i < _bayStates.Length ? _bayStates[i].ToString() : "?";
	/// <summary>Öffnet das Hamburger-Menü (Screenshot-Werkzeug).</summary>
	internal void TestOpenCommand() { if (_hud.Screens.Current != _hud.Command) ToggleCommand(); }

	// ── Hooks für die Kommandozentrale (Selbsttest/Screenshots) ──

	/// <summary>Öffnet einen Kommandozentralen-Bereich wie ein Tipp in der Leiste (Selbsttest/Screenshots).</summary>
	internal void TestOpenCcArea(string areaId) => OpenCcArea(areaId);
	/// <summary>„← Flug“: schließt die Kommandozentrale (Selbsttest).</summary>
	internal void TestCloseCommand() => CloseCommand();
	/// <summary>Sektor in der Universumskarte wählen (Selbsttest/Screenshots).</summary>
	internal void TestCcSelectSector(SectorId s) => _hud.CcUniverse.TestSelect(s);
	/// <summary>Sprungzahl der Route zum gewählten Sektor, passend zu Universe.FindRoute.</summary>
	internal int TestCcRouteHops => _hud.CcUniverse.TestRouteHops;
	/// <summary>„Route planen/Autopilot“ für den gewählten Sektor auslösen (Selbsttest).</summary>
	internal void TestCcPlanRoute() => _hud.CcUniverse.TestPlanRoute();
	/// <summary>Fraktion in der Fraktionsliste wählen (Screenshots).</summary>
	internal void TestCcSelectFaction(FactionId f) => _hud.CcFactions.TestSelect(f);
	/// <summary>Schiffsdetail im Besitz öffnen (Screenshots).</summary>
	internal void TestCcOpenShipDetail() => _hud.CcAssets.TestOpenShipDetail();
	/// <summary>Sektoransicht der Kommandozentrale auf einen Sektor setzen (Screenshots).</summary>
	internal void TestCcSelectSectorView(SectorId s) => _hud.CcSector.TestSelectSector(s);
	/// <summary>Aktuelles Autopilot-Ziel (Selbsttest: „Route planen“/„Autopilot hierhin“ setzt ein Ziel).</summary>
	internal NavTarget? TestAutopilotTarget => _target;

	// ── Hooks für die Kamera-Prüfungen (Selbsttest/Screenshots) ──

	/// <summary>Setzt Kamera-Winkel/-Abstand direkt (geklemmt im Core) und schnappt die Kamera darauf.</summary>
	internal void TestSetCam(float yawDeg, float pitchDeg, float distance)
	{
		_orbit.SetAngles(yawDeg, pitchDeg, distance);
		_camOffset = _orbit.Offset();
		SnapCam();
	}
	/// <summary>„Kamera ↺“ wie ein Tipp auf den Knopf (Selbsttest).</summary>
	internal void TestResetCam() => ResetCamera();
	internal void TestCameraAction(string action) => OnCommandAction(action);
	/// <summary>Schaltet Autopilot und Auto-Mining ab, damit Lenkungstests nur die Handeingabe messen.</summary>
	internal void TestStopAutomation() { CancelAutopilot(); if (_miner.Enabled) SetAutoMining(false); }
	internal CameraMode TestCamMode => _orbit.Mode;
	internal bool TestCamInvert => _orbit.InvertDrag;
	/// <summary>Simuliert einen Ein-Finger-Wisch und liefert das Solltempo aus der Lenkung (muss 0 sein: Touch steuert das Schiff nicht).</summary>
	internal float TestSwipe(Vector2 from, Vector2 to)
	{
		HandlePointer(0, from, pressed: true, motion: false);
		HandlePointer(0, to, pressed: true, motion: true);
		float throttle = ReadSteering(0.016f).throttle;
		HandlePointer(0, to, pressed: false, motion: false);
		return throttle;
	}
	internal bool TestHasLockTarget => LockTargetPosition() != null;
	/// <summary>Horizontale Richtung Schiff → Ziel (XZ, normiert) und Schiff → Kamera (normiert) für den Target-Lock-Test.</summary>
	internal (Vector2 ToTarget, Vector2 ToCamera)? TestLockGeometry()
	{
		if (LockTargetPosition() is not { } t) return null;
		var a = new Vector2(t.X - _player.Position.X, t.Z - _player.Position.Z);
		var c = new Vector2(_camOffset.X, _camOffset.Z);
		return (a.Normalized(), c.Normalized());
	}
	/// <summary>Aktuelle Kamera-Winkel und -Abstand (Selbsttest).</summary>
	internal (float Yaw, float Pitch, float Dist) TestCamAngles => (_orbit.YawDeg, _orbit.PitchDeg, _orbit.Distance);
	/// <summary>Aktueller Ist-Offset der Kamera relativ zum Schiff (Selbsttest).</summary>
	internal Vector3 TestCamOffset => new(_camOffset.X, _camOffset.Y, _camOffset.Z);
	internal Camera3D TestCam => _cam;
	internal Vector3 TestCamPos => _cam.Position;
	/// <summary>Bildschirm → Welt: Schnitt des Kamerastrahls durch diesen Pixel mit der XZ-Ebene (wie „Halten fliegt zum Zeiger“).</summary>
	internal Vector3? TestGroundPoint(Vector2 screen) => GroundPoint(screen);
	/// <summary>Welt → Bildschirm: projiziert einen Weltpunkt (Roundtrip-Prüfung mit gedrehter Kamera).</summary>
	internal Vector2 TestProject(Vector3 world) => _cam.UnprojectPosition(world);
	/// <summary>Blickrichtung der Kamera (Einheitsvektor); Selbsttest prüft „blickt aufs Schiff“.</summary>
	internal Vector3 TestCamForward => -_cam.GlobalTransform.Basis.Z;
}
