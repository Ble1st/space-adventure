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
	bool _allSystems;   // Phase 14: Weltaufbau mit eingeschaltetem Treibstoff und Forschung (Selbsttest, Bildschirmfotos)
	SectorView _view = null!;
	MissionView _missions = null!;   // Runde 41: Leuchtfeuer der Missionsziele
	PlayerShip _player = null!;
	Camera3D _cam = null!;
	Hud _hud = null!;
	Fx _fx = null!;
	AnchorView _anchors = null!;   // Phase 32: Ankertrossen der Kontaktschürfer
	Sfx _sfx = null!;
	/// <summary>Runde 12: Musikregler (Zustand aus dem Core, Schichten/Stimmungen/Signale aus music.json).</summary>
	SpaceAdventure.Audio.MusicDirector _music = null!;
	internal SpaceAdventure.Audio.MusicDirector TestMusic => _music;
	MiningView _mining = null!;
	CombatView _combat = null!;   // Phase 24: Schüsse der NPC-Gefechte im Spielersektor
	/// <summary>Phase 25: Spielerkampf (Zielwahl, Schüsse über die Core-Regel, Dauerfeuer, Beschusswarnung).</summary>
	readonly CombatController _fight = new();
	/// <summary>Phase 26: Bergung (gewähltes Wrack, Traktor zieht Fracht, Schacht „Bergen“ birgt Module).</summary>
	readonly SalvageController _salvage = new();

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
	/// <summary>Phase 31: gewähltes Tor (Tipp auf ein Tor) — Ziel für „Tor angreifen“ und die Zielkarte.</summary>
	GateId? _targetGate;
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
	/// <summary>Phase 11: letzte Meldung zu Überfall, Kontrolle oder Vorwarnung (Selbsttest).</summary>
	public string LastSecurityMessage { get; private set; } = "";

	public override void _Ready()
	{
		Ui.InputLog.Init();
		AddChild(new Ui.InputLogNode());   // zweiter Handytest A15: Eingabe-Protokoll „[input]“ (Debug-Build)
		BuildEnvironment();
		BuildStars();

		_view = new SectorView(); AddChild(_view);
		_missions = new MissionView(); AddChild(_missions);   // Runde 41
		_mining = new MiningView { GoodColor = id => GoodColor(id) }; AddChild(_mining);
		_combat = new CombatView(); AddChild(_combat);
		// Runde 11: Schüsse und Raketen starten an der sichtbaren Stellung des feuernden Slots (Schiff) bzw. Geschützes (Station)
		_combat.MuzzleOf = (id, slot) => id == _session.U.PlayerShip ? PlayerMuzzle(slot) : _view.Muzzle(id, slot);
		_combat.MuzzleDirOf = (id, slot) => id == _session.U.PlayerShip ? _player.Visual?.Hardpoints.Direction(slot) : _view.MuzzleDirection(id, slot);
		_combat.StationMuzzleOf = _view.StationMuzzle;
		_combat.StationMuzzleDirOf = _view.StationMuzzleDirection;
		_player = new PlayerShip(); AddChild(_player);
		_fx = new Fx(); AddChild(_fx);
		_anchors = new AnchorView { Name = "Anchors", Fx = _fx }; AddChild(_anchors);
		SpaceAdventure.Audio.AudioSettings.Init();   // Runde 12: Busse „Musik“/„Effekte“ und gespeicherte Lautstärken (user://settings.cfg)
		SpaceAdventure.Game.CameraSettings.Load().ApplyTo(_orbit);   // Runde 44: Kameramodus/Rückansicht aus user://settings.cfg
		Ui.Kit.UiScale.Init(GetTree().Root);   // Runde 31: UI-Größe aus DPI und Stufe; Runde 33: wirkt je Schicht (Flug-HUD), Wurzel bleibt 1
		_sfx = new Sfx(); AddChild(_sfx);
		_music = new SpaceAdventure.Audio.MusicDirector(); AddChild(_music);
		_cam = new Camera3D { Fov = 50f, Far = 4000f }; AddChild(_cam);
		_hud = new Hud(); AddChild(_hud);

		_hud.TargetScreenPosition = ScreenPositionOfTarget;
		_hud.StationScreenPositions = StationScreenPositions;
		_hud.MissionEdges = MissionScreenPositions;   // Runde 41: Gold-Randpfeile der Missionsziele
		_hud.FreeField = () => _session != null ? FreeFieldPixels() : new Rect2();   // Runde 16
		_hud.TargetEdge = TargetEdge;
		_hud.Flight.MapPressed += () => { _sfx.Play(SfxId.Click); _hud.Map.Open(_session.U, _session.U.PlayerSector, _target?.Sector); };
		_hud.Flight.HamburgerPressed += () => { _sfx.Play(SfxId.Click); ToggleCommand(); };
		_hud.Flight.AutopilotPressed += ToggleAutopilot;
		_hud.Flight.AutoMinePressed += () => SetAutoMining(_hud.Flight.AutoMineOn);
		_hud.Flight.ContextPressed += ContextAction;
		_hud.Flight.SetCommandBadge(0);   // Runde 40: echter Comms-Zähler folgt in UpdateHud je Bild
		_hud.Flight.BayDown += OnBayDown;
		_hud.Flight.AllFire += () => FireReadyBays();
		_hud.Flight.GroupDown += OnGroupDown;   // HUD-Neu: Gruppenknopf im Daumenbogen
		// Phase 25: Dauerfeuer und „Nächster Gegner“
		_hud.Flight.AutoFirePressed += () =>
		{
			_fight.AutoFire = !_fight.AutoFire;
			if (_fight.AutoFire) EnableAimAssist();   // Gerätetest Phase 28: Dauerfeuer schaltet die Zielhilfe mit ein (nicht nach bewusstem Aus)
			_sfx.Play(SfxId.Click);
			_hud.Toast(_fight.AutoFire ? "Dauerfeuer an · Ziel folgen an" : "Dauerfeuer aus");
		};
		_hud.Flight.NextEnemyPressed += SelectNextEnemy;
		_hud.Flight.AimAssistPressed += () =>
		{
			_aimAssist = !_aimAssist;
			_aimOffFor = _aimAssist ? null : AimKey();   // Runde 4: bewusstes Aus gilt bis zum Zielwechsel
			_sfx.Play(SfxId.Click);
			_hud.Toast(_aimAssist ? "Ziel folgen an: die Nase folgt dem Ziel (◀ ▶ übersteuern)" : "Ziel folgen aus");
		};
		_hud.Flight.StationAttackPressed += ArmStationAttack;   // Phase 28
		_hud.Flight.GateAttackPressed += ArmGateAttack;   // Phase 31
		_hud.Flight.SurrenderPressed += Surrender;
		// Phase 23: Leistungsverteilung (Core-Regel; die Taste liefert schon die gewünschte Stellung, erneutes Tippen = ausgeglichen)
		_hud.Flight.PowerModePressed += m =>
		{
			_sfx.Play(SfxId.Click);
			SpaceSim.Core.Combat.ShipSystems.SetMode(_session.U, PlayerData, m);
			_hud.Toast(SpaceSim.Core.Combat.ShipSystems.ModeEffect(PlayerData.PowerMode, PlayerData.Stats.PowerStorage > 0f));   // Gerätetest: spürbare Rückmeldung
		};
		_hud.Command.Action += OnCommandAction;
		_hud.Map.SectorSelected += sector => SetTarget(Navigator.ForSector(_session.U, sector, _session.U.PlayerShip), null);
		_hud.Map.JumpSelected += StartJumpTo;   // Sprungantrieb: Ziel auf der Karte
		// Runde 37b (Handytest r37 N1, Blocker: Dock → „≡ Menü“ → „Karte“ → „Schließen“ ließ nur die nackte 3D-Szene zurück — die Karte meldete das Schließen,
		// niemand hörte zu, angedockt war weder Dock-Menü noch Flug-HUD sichtbar): jedes Schließen der Karte führt zur Grundansicht zurück
		_hud.Map.Closed += ShowBase;
		_hud.DockMenu.AreaSelected += OnDockArea;
		_hud.DockMenu.UndockPressed += Undock;
		_hud.DockMenu.MenuPressed += ToggleCommand;   // Runde 23
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
		_hud.DockBlackMarket.AreaSelected += OnDockArea;
		_hud.DockBlackMarket.BackToMenu += ShowDockMenu;
		_hud.DockBlackMarket.UndockPressed += Undock;
		_hud.DockBlackMarket.Executed += ok => _sfx.Play(ok ? SfxId.Pickup : SfxId.Click);
		_hud.DockAuthority.AreaSelected += OnDockArea;
		_hud.DockAuthority.BackToMenu += ShowDockMenu;
		_hud.DockAuthority.UndockPressed += Undock;
		_hud.DockAuthority.Executed += ok => _sfx.Play(ok ? SfxId.Pickup : SfxId.Click);
		_hud.DockMissions.AreaSelected += OnDockArea;
		_hud.DockMissions.BackToMenu += ShowDockMenu;
		_hud.DockMissions.UndockPressed += Undock;
		_hud.DockMissions.Executed += ok => _sfx.Play(ok ? SfxId.Pickup : SfxId.Click);
		_hud.DockResearch.AreaSelected += OnDockArea;
		_hud.DockResearch.BackToMenu += ShowDockMenu;
		_hud.DockResearch.UndockPressed += Undock;
		_hud.DockResearch.Executed += ok => _sfx.Play(ok ? SfxId.Pickup : SfxId.Click);
		_hud.DockHangar.Executed += (svc, ok) => _sfx.Play(ok ? SfxId.Dock : SfxId.Click);
		_hud.DockHangar.CommandsRequested += id => { _hud.CcFleet.Select(id); OpenCcArea(CommandAreas.Fleet); };
		_hud.DockHangar.AreaSelected += OnDockArea;
		// Phase 12: Taxi — die Fahrt ist gebucht (Spieler sitzt am Ziel im neuen Schiff), die Reisezeit vergeht im Zeitraffer
		_hud.DockTaxi.AreaSelected += OnDockArea;
		_hud.DockTaxi.BackToMenu += ShowDockMenu;
		_hud.DockTaxi.UndockPressed += Undock;
		_hud.DockTaxi.Booked += OnTaxiBooked;
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
		_hud.CcUniverse.OpenSectorMap += sec => { _hud.CcSector.SelectedSector = sec; _sectorChosen = true; OpenCcArea(CommandAreas.Sector); };
		_hud.CcUniverse.PlanRoute += sec =>
		{
			var uu = _session.U;
			int hops = uu.FindRoute(uu.PlayerSector, sec)?.Count ?? -1;
			SetTarget(Navigator.ForSector(uu, sec, uu.PlayerShip), null);
			CloseCommand();   // Gerätetest Phase 27: Rückmeldung sichtbar — zurück in die Flugansicht, wo der Autopilot fliegt
			_hud.Toast($"Autopilot: Route nach {uu.Sectors[sec].Name} ({hops} {(hops == 1 ? "Sprung" : "Sprünge")})", 4f);
		};
		_hud.CcSector.AutopilotRequested += (nav, st) => { SetTarget(nav, st); CloseCommand(); };
		_hud.CcSector.GateTargetRequested += g => { SelectGate(g); CloseCommand(); };   // Runde 16: Tor über die Sektoransicht wie angetippt
		_hud.CcSector.TargetRequested += (nav, st) => { SelectTarget(nav, st); CloseCommand(); };   // Runde 4: nur als Ziel wählen   // Gerätetest Phase 27: Rückmeldung in der Flugansicht
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
		_hud.CcFleet.SwitchRequested += SwitchRemote;
		_hud.CcAssets.SwitchRequested += SwitchRemote;
		_hud.CcAssets.CommandsRequested += id => { _hud.CcFleet.Select(id); OpenCcArea(CommandAreas.Fleet); };
		_hud.CcUniverse.TaxiRequested += to =>
		{
			if (_docked is not { } sid || !_session.U.Stations.TryGetValue(sid, out var st)) return;
			_hud.DockCtx.U = _session.U; _hud.DockCtx.Station = st; _hud.DockCtx.Ship = PlayerData; _hud.DockCtx.Services = _session.Services; _hud.DockCtx.Prices = _session.Prices;
			_hud.DockTaxi.Book($"{SpaceSim.Core.Services.TaxiService.Ride}:{to.Value}");
		};
		_hud.CcSystem.SystemAction += a => OnMenu(a);
		_player.Hit += OnPlayerHit;
		_view.AsteroidGone += (pos, r) => { _fx.Burst(pos, new Color(1f, 0.7f, 0.3f), 22, 10f); _sfx.Play(SfxId.Crack); };
		_chunks.Shot += OnShot;
		_chunks.Contacted += OnContact;   // Phase 29: Kontaktabbau

		var args = OS.GetCmdlineUserArgs();
		bool selftest = args.Contains("--selftest");
		_camDebug = args.Contains("--camdebug");   // Runde 16: Diagnosezeile der Gefechtskamera
		int shotIdx = Array.IndexOf(args, "--ui-shots");
		string? shotDir = shotIdx >= 0 && shotIdx + 1 < args.Length ? args[shotIdx + 1] : null;
		if (selftest || shotDir != null)
		{
			_allSystems = true;   // Phase 14: Selbsttest/Bildschirmfotos mit Treibstoff und Forschung (Standardwelt: beide aus); der Selbsttest prüft die Standardwelt am Ende
			SaveService.UseFile(selftest ? "selftest.json" : "ui-shots.json");
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(selftest ? "user://selftest.json" : "user://ui-shots.json"));   // immer frisch beginnen
		}
		// Oberflächen-Texturen: --no-surface-detail = Modelle ohne die gemeinsamen Texturen (Vergleichsbilder)
		if (args.Contains("--no-surface-detail")) World.SurfaceDetail.Enabled = false;
		if (args.Contains("--model-shots")) { SaveService.UseFile("model-shots.json"); UiScale.PinnedFactor = 1f; }   // eigener Spielstand, nie der echte
		StartSession();
		if (selftest) AddChild(new SelfTest(this));
		int modelIdx = Array.IndexOf(args, "--model-shots");
		if (modelIdx >= 0 && modelIdx + 1 < args.Length) AddChild(new ModelShots(args[modelIdx + 1], args.Contains("--model-shots-all")));
		// Runde 16: Handyformat-Fotos aus dem echten Spielablauf (PhoneShots)
		// Runde 31 (UI-ANPASSUNG.md Etappe 0): Prüfwerkzeug aller Bildschirme in sechs Fenstergrößen und drei UI-Faktoren
		int checkIdx = Array.IndexOf(args, "--ui-check");
		if (checkIdx >= 0 && checkIdx + 1 < args.Length)
		{
			SaveService.UseFile("ui-check.json");
			string dir = args[checkIdx + 1];
			if (!System.IO.Path.IsPathRooted(dir)) dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), dir);
			string? Opt(string k) => args.FirstOrDefault(a => a.StartsWith(k + "="))?[(k.Length + 1)..];
			var check = new UiCheck(this, dir) { FlightOnly = Array.IndexOf(args, "--ui-check-flight") >= 0, NoFlight = Array.IndexOf(args, "--ui-check-noflight") >= 0 };   // Runde 33/35b
			check.Select(Opt("--ui-check-sizes"), Opt("--ui-check-factors"));   // Runde 35b: Auswahl für schnelle Durchläufe
			// Himmelskugeln (ui-check-Befund „Hinweis verdeckt „Waferfabrik Beta · 10 m“ (gezeichnet)“, nur in einem Lauf): die Prüfung lief nach Main/Hud im selben
			// Bild — sie las die Lage des Hinweises dieses Bildes, die selbst gezeichneten Randnamen aber vom vorigen (ein Bild Versatz, sobald der Hinweis
			// seine Lage wechselt). Jetzt misst sie zuerst im Bild: alles, was sie liest, ist der Stand, der zuletzt gezeichnet wurde.
			check.ProcessPriority = -1000;
			AddChild(check);
		}
		int phoneIdx = Array.IndexOf(args, "--phone-shots");
		if (phoneIdx >= 0 && phoneIdx + 1 < args.Length) { SaveService.UseFile("phone-shots.json"); AddChild(new PhoneShots(this, args[phoneIdx + 1]) { Mining = Array.IndexOf(args, "--phone-mining") >= 0 }); }
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
			// Phase 28 (Prüfpunkt f): das Nachholen läuft über mehrere Bilder mit Fortschrittsanzeige (vorher synchron in _Ready → langer Splash am Gerät)
			_catchUp = OfflineCatchUp.Begin(u, DateTimeOffset.UtcNow - savedAt);
			if (_catchUp.Total <= 0) { _catchUp.Finish(); _catchUp = null; AttachSession(); }
			else _hud.ShowLoading("Spielzeit wird nachgeholt …", 0f);
		}
		else
		{
			if (SaveService.Exists) GD.PushWarning("Spielstand nicht ladbar, starte neu: " + error);
			_session = GameSession.New(_allSystems ? def => { def.Rules ??= new EconomyRules(); def.Rules.FuelActive = true; def.Rules.ResearchActive = true; } : null);
			AttachSession();
			if (SaveService.Exists) _hud.Toast("Spielstand beschädigt – neues Spiel gestartet", 4f);
		}
	}

	/// <summary>Hängt Ansicht und Ereignisse an das aktuelle Universum und setzt das Spielerschiff.</summary>
	/// <summary>Runde 17 (Speicher am Gerät wächst mit „Neues Spiel“): schwache Verweise auf alle angehängten Universen — der Selbsttest zählt die lebenden.</summary>
	internal static readonly List<WeakReference> AttachedUniverses = new();
	internal static readonly List<string> AttachedLabels = new();
	static int _attachCount;
	void AttachSession()
	{
		var u = _session.U;
		AttachedUniverses.Add(new WeakReference(u));
		AttachedLabels.Add($"#{++_attachCount} {u.Ships[u.PlayerShip].ModelKey}");
		// Runde 17 (Speicher wächst mit „Neues Spiel“): keine Ansicht hält das alte Universum fest
		_hud.CcCtx.U = u; _hud.CcCtx.Player = u.Ships[u.PlayerShip]; _hud.CcCtx.Comms = _session.Comms;   // Runde 40: Comms-Registry der Sitzung
		_hud.DockCtx.U = u; _hud.DockCtx.Ship = u.Ships[u.PlayerShip]; _hud.DockCtx.Station = null!;
		_hud.Map.Forget();
		foreach (var sc in _hud.Screens.All)
		{
			if (sc is Ui.Screens.CommandScreen cs) cs.DropContent();
			else if (sc is Ui.Screens.DockAreaScreen ds) ds.DropContent();
		}
		u.Events.Raised += OnSimEvent;
		_target = null; _targetStation = null; _targetGate = null; _docked = null; _autopilot = false;   // Runde 16: auch das Tor (Taste „Tor angreifen“ blieb nach „Neues Spiel“)
		_hud.Flight.SetAutopilot(false);
		_hud.Flight.SetSlider(0f);
		_hud.Screens.Hide();
		_hud.HideAlert();   // zweiter Gerätetest Phase 28: keine Meldung der alten Sitzung (Verlustmeldung blieb nach „Neues Spiel“ stehen)
		_fast = false;      // Runde 4: neue Sitzung beginnt mit Zeit ×1 (der Zeitraffer blieb über „Neues Spiel“ erhalten)
		_aimAssist = true; _aimOffFor = null; _fight.AutoFire = false; _lastDepleted = "";
		foreach (var sc in _hud.Screens.All.OfType<ISessionScreen>()) sc.ResetSession();   // Gerätetest Phase 27: kein Zustand der alten Sitzung
		SetAutoMining(false, announce: false);
		_chunks.Clear(); _mineTarget = null; _mining.ClearPlayer(); _mining.ResetNpc(); _combat.Reset(); _fight.Reset(); _anchors.Clear();
		_transition = false; _playerTween?.Kill(); _player.Visible = true; _player.Scale = Vector3.One; _player.GiveGrace(2f);
		_hud.Flight.Visible = true;   // Dock-Bildschirme wurden über Screens.Hide() geschlossen
		_view.Rebuild(u);
		var p = PlayerData;
		_player.Position = new Vector3(p.Position.X, 0, p.Position.Y);
		_player.Velocity = Vector3.Zero; _player.Locked = false;
		_camOffset = _orbit.Offset();   // gewählte Ansicht bleibt erhalten (nicht im Spielstand gespeichert)
		SnapCam();
		_reenter = false;
		_player.Heading = 0f;   // zweiter Gerätetest Phase 28: neue Sitzung = Nase nach Norden (vorher blieb die Richtung der alten Sitzung stehen)
		// Runde 19 („Ankertest“): startet ein Kontaktschürfer am Fels, zeigt der Bug zum Fels (er verankert ohne Eingabe)
		if (p.Stats.UsesContact && _chunks.Nearest(u.Sectors[u.PlayerSector], p.Position, SpaceSim.Core.Mining.ContactMining.ContactRange + SpaceSim.Core.Mining.ContactMining.BowOffset + 1f) is { } startRock)
			_player.Heading = AimAssist.Bearing(p.Position, startRock.Position);
		if (u.Stations.ContainsKey(p.DockedAt)) EnterActiveShip();   // Phase 12: gespeichert angedockt (oder in ein angedocktes Schiff gewechselt) → Andock-Menü
		else AnnouncePending();
	}

	/// <summary>
	/// Zweiter Gerätetest Phase 28: ein schon angekündigter Überfall beim Sitzungsbeginn (Gefechtstest) — die Nase zeigt auf den Kaperer und die Warnung
	/// steht die volle Frist (vorher entstand die Ankündigung vor dem Anschluss der Ansicht und wurde nie gezeigt; die Nase zeigte in die alte Richtung).
	/// </summary>
	void AnnouncePending()
	{
		var u = _session.U;
		var warn = u.PendingEncounters.Where(e => e.Target == u.PlayerShip && e.Kind == EncounterKind.Raid).OrderBy(e => e.DueTick).FirstOrDefault();
		if (warn is null || !u.Ships.TryGetValue(warn.Source, out var raider) || raider.Sector != u.PlayerSector) return;
		var d = new Vector2(raider.Position.X - _player.Position.X, raider.Position.Y - _player.Position.Z);
		if (d.LengthSquared() > 0.01f) _player.Heading = Mathf.Atan2(-d.X, -d.Y);
		_pendingTargeted = (raider.Id, warn.DueTick);
		TakeShipTarget(raider.Id);   // Runde 15: Ziel schon in der Warnphase (Gefechtskamera, Randpfeil); Runde 21: gemeinsamer Weg
		float secs = Mathf.Max(1f, (warn.DueTick - u.Tick) / (float)SimClock.TicksPerSecond);
		_hud.Alert($"Piraten! {u.ActorName(raider.Owner)} nimmt Kurs auf dich — Angriff in {secs:0} s. Andocken oder kämpfen („Nächster Gegner“, „Dauerfeuer“).", secs + 1f);
		LastSecurityMessage = _hud.TestAlert;
		StartCountdown(warn.DueTick);
	}

	// Runde 21 (Handy-Test c01_w1..w3: „Angriff in 15 s“ blieb stehen): die Vorwarnung zählt herunter (Restzeit aus PendingEncounter.DueTick)
	string? _warnText, _warnShown; long _warnDue;
	(ShipId, long) _pendingTargeted;   // Runde 22
	float WarnSeconds(long due) => SpaceSim.Core.Combat.WarningCountdown.SecondsLeft(due, _session.U.Tick) + 1f;
	void StartCountdown(long due) { _warnText = _hud.TestAlert.Length > 0 ? _hud.TestAlert : null; _warnShown = _warnText; _warnDue = due; }
	void TickCountdown()
	{
		if (_warnText is null || _warnShown is null) return;
		var u = _session.U;
		if (SpaceSim.Core.Combat.WarningCountdown.Pending(u, u.PlayerShip, _warnDue) is null)
		{
			// Runde 41 (A2): Ursache abgelaufen (angedockt, Sektor verlassen, Waffenstillstand/Frieden) — die Warnmeldung verschwindet sofort
			if (_hud.TestAlert == _warnShown) _hud.HideAlert();
			_warnText = null; return;
		}
		string text = SpaceSim.Core.Combat.WarningCountdown.Apply(_warnText, SpaceSim.Core.Combat.WarningCountdown.SecondsLeft(_warnDue, u.Tick));
		if (text == _warnShown) return;
		if (_hud.ReplaceAlertText(_warnShown, text)) _warnShown = text; else _warnText = null;   // Meldung ersetzt oder geschlossen → nicht weiter zählen
	}

	/// <summary>Gerätetest Phase 27: Hindernisse des Sektors plus die Tore (beim Abdocken nicht geradewegs in ein Tor fliegen).</summary>
	List<Obstacle> ObstaclesWithGates(SectorId sector)
	{
		var list = new List<Obstacle>(_view.Obstacles);
		foreach (var g in _session.U.Gates.Values) if (g.Sector == sector) list.Add(new Obstacle(g.Position, GateRadius + 4f));
		return list;
	}

	float _labelCheck;
	/// <summary>
	/// Gerätetest Phase 27: 3D-Beschriftungen, die hinter einem HUD-Element liegen (Drehknöpfe, Regler, Statusleiste), werden fast durchsichtig — sie liefen
	/// sonst unter den Tasten durch und waren unlesbar. Fünfmal je Sekunde geprüft.
	/// </summary>
	/// <summary>Selbsttest (Runde 12): sichtbare 3D-Schilder überlappen sich auf dem Bildschirm nicht.</summary>
	internal bool TestLabelsOverlap()
	{
		var boxes = new List<Rect2>();
		foreach (var l in _view.Labels().Where(x => x.IsInsideTree() && x.Visible && !_cam.IsPositionBehind(x.GlobalPosition)))
		{
			var box = LabelBox(l, _cam.UnprojectPosition(l.GlobalPosition));
			if (boxes.Any(b => b.Intersects(box))) return true;
			boxes.Add(box);
		}
		return false;
	}

	/// <summary>
	/// Runde 16 (Handy-Test: Zustandsschild eines Tors lag halb unter der Statusleiste): Bildschirmfläche eines 3D-Schilds — mehrzeilige Schilder
	/// (Tor: Name + „AUSSER BETRIEB · …“) zählen mit allen Zeilen und der längsten Zeile, nicht als eine 22 px hohe Zeile.
	/// </summary>
	static Rect2 LabelBox(Label3D l, Vector2 sp)
	{
		var lines = l.Text.Split('\n');
		float half = Mathf.Max(20f, lines.Max(x => x.Length) * 4.5f);
		float h = 22f * lines.Length;
		return new Rect2(sp - new Vector2(half, h / 2f), new Vector2(half * 2f, h));
	}

	void HideCoveredLabels(float delta)
	{
		_labelCheck -= delta;
		if (_labelCheck > 0f) return;
		_labelCheck = 0.2f;
		var rects = _hud.Flight.OccupiedRects();
		// Runde 17: Schiffsringe nicht unter/über HUD-Tasten
		_view.HideRingsUnder(w => !_cam.IsPositionBehind(w) && rects.Any(r => r.Intersects(new Rect2(_cam.UnprojectPosition(w) - new Vector2(30f, 30f), new Vector2(60f, 60f)))));
		var shown = new List<Rect2>();
		// Runde 12 (Handy-Test: „[LTR] Elektronikfab…“ über „Wasserwerk Borealis“): nähere Schilder zuerst; ein Schild, das ein näheres überdeckt, wird ausgeblendet
		foreach (var l in _view.Labels().Where(x => x.IsInsideTree()).OrderBy(x => x.GlobalPosition.DistanceSquaredTo(_cam.GlobalPosition)))
		{
			var p = l.GlobalPosition;
			// Runde 4: ganze Beschriftung prüfen (vorher nur ihr Mittelpunkt — „Wrack“ lag halb unter dem rechten HUD)
			var sp = _cam.UnprojectPosition(p);
			var box = LabelBox(l, sp);
			bool behind = _cam.IsPositionBehind(p);
			bool covered = !behind && (rects.Any(r => r.Intersects(box)) || shown.Any(r => r.Intersects(box)));
			// Phase 29 (Handy-Test der Stationsmodelle): unter HUD-Flächen ganz ausblenden (vorher nur abgedunkelt — hohe Modelle schoben Schilder unter Sektor-Chip und Zielkarte)
			l.Visible = !covered;
			if (!covered && !behind) shown.Add(box);
		}
	}

	float _musicClock;
	float _sparkClock;
	double _lastWingToast = -99;
	/// <summary>Phase 31: beschädigte Tore (≤ 50 % Hülle) im Sektor sprühen alle 0,6 s Funken an zufälliger Stelle des Rings (nur Darstellung).</summary>
	void GateSparks(float delta)
	{
		_sparkClock += delta;
		if (_sparkClock < 0.6f) return;
		_sparkClock = 0f;
		var u = _session.U;
		if (!u.Rules.GateCombat) return;
		foreach (var gid in u.Sectors[u.PlayerSector].Gates)
			if (u.Gates.TryGetValue(gid, out var g) && SpaceSim.Core.Combat.GateInfo.State(u, g) == "beschädigt")
			{
				float a = GD.Randf() * Mathf.Tau;
				var at = new Vector3(g.Position.X + Mathf.Cos(a) * 9f, 4f + GD.Randf() * 6f, g.Position.Y + Mathf.Sin(a) * 9f);
				_fx.Burst(at, new Color(1f, 0.75f, 0.3f), 6, 6f, 0.35f);
			}
	}
	readonly Dictionary<string, (double Sum, int N)> _profile = new();
	/// <summary>Runde 13 (FPS): Rechenzeit je Ansichtsteil (Mittel in ms je Bild, Selbsttest-Ausgabe).</summary>
	void Profile(string part, long ticks)
	{
		_profile.TryGetValue(part, out var p);
		_profile[part] = (p.Sum + ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency, p.N + 1);
	}
	internal string TestProfile() => string.Join(", ", _profile.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value.Sum / Mathf.Max(1, kv.Value.N):0.000} ms"));
	internal void TestResetProfile() => _profile.Clear();
	bool _framingActive, _manualZoomInFight;
	Vector3 _camFocus;
	/// <summary>Runde 13: manuelles Zoomen während der Gefechtsrahmung übersteuert sie bis zum Ende des Gefechts.</summary>
	void ManualZoom() { if (_framingActive) _manualZoomInFight = true; }
	/// <summary>Selbsttest: ist die Gefechtsrahmung aktiv?</summary>
	internal bool TestFraming => _framingActive && !_manualZoomInFight;
	SpaceSim.Core.Flight.CombatFraming.Frame _lastFrame;
	internal string TestFrameDebug => $"Soll-Abstand {_lastFrame.Distance:0} Blick ({_lastFrame.FocusX:0},{_lastFrame.FocusZ:0}) Kamera {_cam.Position} Fokus {_player.Position + _camFocus} Orbit {_orbit.Distance:0} Pitch {_orbit.PitchDeg:0} Yaw {_orbit.YawDeg:0}";
	internal string TestFramingDebug => $"aktiv {_framingActive} manuell {_manualZoomInFight} Ziel {_fight.Target.Value} feindlich {(_fight.Target.Value != 0 && SpaceSim.Core.Combat.PlayerCombat.IsHostile(_session.U, PlayerData, _fight.Target))} Welt {ShipWorld(_fight.Target)} Spieler {_player.Position} angedockt {_docked != null}";

	/// <summary>
	/// Runde 14 (Handy-Test 2340×1080): freies Bildfeld der Gefechtskamera aus den tatsächlichen HUD-Flächen der aktuellen Auflösung — oben Sektor-Chip/Lagezeile,
	/// rechts Zielkarte und Kampftasten, links Statusleiste/Drehtasten, unten Regler, Rücktaste und die Meldung. Normalkoordinaten (x −1 … 1, y −1 unten … 1 oben).
	/// </summary>
	internal SpaceSim.Core.Flight.CombatFraming.FreeRect FramingField()
	{
		var b = FreeFieldPixels();
		var size = GetViewport().GetVisibleRect().Size;
		float w = size.X, h = size.Y;
		return new(b.Position.X / w * 2f - 1f, b.End.X / w * 2f - 1f, 1f - b.End.Y / h * 2f, 1f - b.Position.Y / h * 2f);
	}

	Rect2 _freeField; ulong _freeFieldAt; Vector2 _freeFieldSize;
	/// <summary>
	/// Runde 16: freies Feld in Bildpunkten — größtes Rechteck um die Bildmitte ohne HUD-Fläche (Core: <see cref="SpaceSim.Core.Flight.FreeField.Largest"/>; vorher schnitt
	/// eine „billigste Seite“-Regel bei der Tastenspalte rechts oben die obere Kante ab und ließ nur ein Band von 35 % Höhe). Höchstens alle 0,2 s neu.
	/// Auch die Randpfeile liegen an seinem Rand.
	/// </summary>
	internal Rect2 FreeFieldPixels()
	{
		var size = GetViewport().GetVisibleRect().Size;
		ulong now = Time.GetTicksMsec();
		if (_freeFieldSize == size && now - _freeFieldAt < 200 && _freeField.Size.X > 0f) return _freeField;
		var rects = _hud.Flight.OccupiedRects();
		if (_hud.AlertRect is { } ar) rects.Add(ar);
		var boxes = rects.Select(r => new SpaceSim.Core.Flight.FreeField.Box(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y)).ToList();
		var b = SpaceSim.Core.Flight.FreeField.LargestNear(size.X, size.Y, boxes, size.X / 2f, size.Y / 2f);   // Runde 33b: auch wenn die Bildmitte belegt ist
		_freeField = new Rect2(b.X, b.Y, b.W, b.H); _freeFieldAt = now; _freeFieldSize = size;
		return _freeField;
	}

	/// <summary>
	/// Runde 14 (Handy-Test: XL-Station lag unter den HUD-Tasten): nahe einer großen Station (L/XL) zoomt die Standardansicht etwas heraus — Faktor
	/// 1 + (Radius − 15) ÷ 40 innerhalb von Radius + 50 m, nach außen weich auslaufend; sonst 1.
	/// </summary>
	float NearLargeStationZoom()
	{
		if (_session is null || _docked != null) return 1f;
		var u = _session.U;
		float best = 1f;
		foreach (var id in u.Sectors[u.PlayerSector].Stations)
		{
			if (!u.Stations.TryGetValue(id, out var st) || st.Radius <= Station.BodyRadius + 0.5f) continue;
			float d = new Vector2(_player.Position.X - st.Position.X, _player.Position.Z - st.Position.Y).Length() - st.Radius;
			if (d > 50f) continue;
			float k = 1f + (st.Radius - Station.BodyRadius) / 40f;
			best = Mathf.Max(best, Mathf.Lerp(k, 1f, Mathf.Clamp((d - 20f) / 30f, 0f, 1f)));
		}
		return best;
	}

	/// <summary>
	/// Runde 13: Ziel der Gefechtskamera — gewähltes feindliches Schiff im Sektor (bis 160 m), sonst der Kaperer eines angekündigten Überfalls auf den Spieler.
	/// </summary>
	Vector3? FramingTarget() => FramingTargetR()?.Pos;

	bool _framingBoth;
	string _camReason = "";
	SpaceSim.Core.Flight.CombatFraming.FreeRect _camField;
	/// <summary>
	/// Runde 16 (dritter Handy-Test der Gefechtskamera): Diagnosezeile — „Kamera: Gefecht aktiv ja/nein · Grund · freies Feld · Abstand · Bild“; sichtbar mit dem
	/// Schalter im Hamburger-Menü („Kamera-Diagnose“) oder dem Startargument <c>--camdebug</c>.
	/// </summary>
	internal string CameraDebugLine()
	{
		var vs = GetViewport().GetVisibleRect().Size;
		var f = _camField;
		return $"Takt {_session.TickRate:0.0} Ticks/s{(_fast ? " (×10)" : "")} · Kamera: Gefecht {(_framingActive && !_manualZoomInFight ? "aktiv" : "aus")} · {(_camReason.Length > 0 ? _camReason : "–")} · Feld x {f.X0:0.00}..{f.X1:0.00} y {f.Y0:0.00}..{f.Y1:0.00} · Abstand {_camOffset.Length:0} (Soll {_lastFrame.Distance:0}) · Ziel {_fight.Target.Value} · Bild {vs.X:0}×{vs.Y:0} · Fenster {GetWindow().Size.X}×{GetWindow().Size.Y} · Faktoren Flug {_hud.Flight.Scale.X:0.00}/Dock {_hud.DockFactor:0.00}/Zentrale {_hud.CcFactor:0.00} · {Ui.Kit.UiScale.InsetsText}";
	}
	bool _camDebug;
	/// <summary>Runde 15: Rahmt die Gefechtskamera Schiff und Ziel (sonst nur das Schiff, Ziel per Randpfeil)?</summary>
	internal bool TestFramingBoth => _framingBoth;

	/// <summary>Runde 15: liegt das eigene Schiff im freien Feld (Normalkoordinaten mit kleiner Toleranz)?</summary>
	bool ShipInFreeField()
	{
		if (_cam.IsPositionBehind(_player.Position)) return false;
		var size = GetViewport().GetVisibleRect().Size;
		var sp = _cam.UnprojectPosition(_player.Position);
		var f = FramingField();
		float nx = sp.X / size.X * 2f - 1f, ny = 1f - sp.Y / size.Y * 2f;
		return nx >= f.X0 - 0.05f && nx <= f.X1 + 0.05f && ny >= f.Y0 - 0.05f && ny <= f.Y1 + 0.05f;
	}

	/// <summary>
	/// Runde 15: Ziel der Gefechtskamera mit Körperradius — angegriffene Station/Tor (mit Radius), gewähltes feindliches Schiff, sonst der Kaperer eines
	/// angekündigten Überfalls (schon in der Warnphase, bis 400 m — weiter weg rahmt die Kamera nur das Schiff und ein Randpfeil zeigt das Ziel).
	/// </summary>
	/// <summary>
	/// Runde 18 (Handy-Test: eigenes Schiff bei 191 m nur ≈ 40 px, der Kaperer unter dem Warnkasten): Warnphasen-Zoom höchstens 200 × Größe und nie so weit, dass
	/// das eigene Schiff kleiner als <see cref="SpaceSim.Core.Flight.CombatFraming.MinShipScreenShare"/> der Bildhöhe wird — passt der Kaperer dann nicht ins Feld,
	/// zeigt ihn der Zielpfeil.
	/// </summary>
	float WarningCap(float sz)
	{
		float len = 2f * ShipModels.ScaleOf(PlayerData.ModelKey);
		return Mathf.Min(200f * sz, SpaceSim.Core.Flight.CombatFraming.MaxDistanceForSize(len, _cam.Fov, SpaceSim.Core.Flight.CombatFraming.MinShipScreenShare));
	}
	internal float TestWarningCap => WarningCap(ShipSizeZoom());
	internal float TestCamDistance => _camOffset.Length;

	/// <summary>Runde 17: die Rahmung gilt einem angekündigten Angreifer (Warnphase, noch kein Gefecht).</summary>
	bool _framingWarning;
	(Vector3 Pos, float Radius)? FramingTargetR()
	{
		_framingWarning = false;
		if (_docked != null || _session is null) return null;
		var u = _session.U;
		// Runde 16: Position aus der Ansicht, sonst aus dem Core (ein fernes Schiff hat ggf. noch keinen Marker)
		Vector3? Where(ShipId id) => ShipWorld(id) ?? (SpaceSim.Core.Combat.PlayerCombat.PositionOf(u, id) is { } cp && cp.Sector == u.PlayerSector ? new Vector3(cp.Pos.X, 0f, cp.Pos.Y) : null);
		Vector3? Near(ShipId id) => Where(id) is { } p && (p - _player.Position).Length() <= 400f ? p : null;
		if (_fight.AtStation && u.Stations.TryGetValue(_fight.StationTarget, out var ast) && ast.Sector == u.PlayerSector) return (new Vector3(ast.Position.X, 0f, ast.Position.Y), ast.Radius);
		if (_fight.AtGate && u.Gates.TryGetValue(_fight.GateTarget, out var agt) && agt.Sector == u.PlayerSector) return (new Vector3(agt.Position.X, 0f, agt.Position.Y), SpaceSim.Core.Combat.GateCombat.BodyRadius);
		if (_fight.Target.Value != 0 && u.Ships.TryGetValue(_fight.Target, out var t) && SpaceSim.Core.Combat.PlayerCombat.IsHostile(u, PlayerData, t.Id) && Near(t.Id) is { } tp)
		{
			_framingWarning = !u.Fights.IsEngaged(PlayerData.Id) && SpaceSim.Core.Combat.PlayerCombat.Announced(u, PlayerData).Contains(t.Id);   // Runde 17
			return (tp, 0f);
		}
		// Runde 17: nur ein angekündigter Überfall (Warnzeit läuft) — ein späterer (Verstärkung in 60 s) rahmt nicht schon nach dem Kampf
		foreach (var src in SpaceSim.Core.Combat.PlayerCombat.Announced(u, PlayerData)) if (Near(src) is { } sp) { _framingWarning = !u.Fights.IsEngaged(PlayerData.Id); return (sp, 0f); }
		return null;
	}

	/// <summary>
	/// Runde 15 (Handy-Test: Fregatte füllte das Bild): Kameraabstand nach Schiffsgröße — S 1, M 1,25, L 1,6, XL 2,1 (Standardansicht, Gefechtsrahmung).
	/// </summary>
	float ShipSizeZoom()
	{
		if (_session is null || !_session.U.Ships.TryGetValue(_session.U.PlayerShip, out var me)) return 1f;
		return SpaceSim.Core.Simulation.Docking.SizeOf(_session.U, me) switch { SlotSize.M => 1.25f, SlotSize.L => 1.6f, SlotSize.XL => 2.1f, _ => 1f };
	}

	/// <summary>Setzt die Kamera hart auf Schiff + aktuellen Orbit-Offset und richtet sie auf das Schiff aus (Neustart, Sprung, Teleport).</summary>
	void SnapCam()
	{
		if (_orbit.Mode == CameraMode.Chase) { _orbit.SnapHeading(_player.Heading); _camOffset = _orbit.Offset(); }   // Runde 44: Rückansicht ohne Schwenk
		var o = _camOffset;
		_camFocus = Vector3.Zero;
		if (PlayerData.AnchorRock == 0) _anchorZoomNow = 1f;   // Runde 43b: harter Schnitt (Teleport, Sitzung) ohne Nachgleiten des Anker-Zooms
		_cam.Position = _player.Position + new Vector3(o.X, o.Y, o.Z);
		_cam.LookAt(_player.Position);
	}

	/// <summary>„Kamera ↺“: Yaw/Pitch/Abstand auf die Standardansicht zurücksetzen (weicher Übergang über Follow).</summary>
	void ResetCamera()
	{
		_orbit.Reset();
		if (_orbit.Mode == CameraMode.Chase) _orbit.ResetChase();   // Runde 44: Rückansicht auf Höhe 45 / Abstand 35
		PersistCamera();
		_sfx.Play(SfxId.Click);
		_hud.Toast("Kamera zurückgesetzt");
	}

	void OnMenu(string action)
	{
		_sfx.Play(SfxId.Click);
		switch (action)
		{
			case "music_mute": SpaceAdventure.Audio.AudioSettings.ToggleMusicMute(); _hud.Command.SetAudioState(); _hud.Toast(SpaceAdventure.Audio.AudioSettings.MusicText); return;   // Runde 12; Runde 38a: Menü bleibt offen (wie „UI-Größe“)
			case "effects_mute": SpaceAdventure.Audio.AudioSettings.ToggleEffectsMute(); _hud.Command.SetAudioState(); _hud.Toast(SpaceAdventure.Audio.AudioSettings.EffectsText); return;   // Runde 38a: schloss das Menü
			case "ui_size": Ui.Kit.UiScale.Cycle(); _hud.UpdateLayerScales(); _hud.Command.SetAudioState(); _hud.Toast(Ui.Kit.UiScale.ShortText(), 4f); return;   // Runde 31/32: echte Tastengröße; Menü bleibt offen
			case "save": SaveNow(manual: true); break;
			case "load":
				if (SaveService.TryLoad(out var u, out _, out var error) && u != null) { _session.Use(u); AttachSession(); _hud.Toast("Spielstand geladen"); }
				else _hud.Toast("Laden fehlgeschlagen: " + error, 4f);
				break;
			case "new": _session.Use(SpaceSim.Core.Content.WorldBuilder.BuildDefault()); _orbit.Reset(); AttachSession(); _hud.Toast("Neues Spiel"); break;   // Runde 19: neue Sitzung = Standardkamera
			case var a when a.StartsWith("new:", StringComparison.Ordinal):   // Phase 5c: Startpaket (player.startOptions)
			{
				string key = a[4..];
				var name = SpaceSim.Core.Content.WorldBuilder.DefaultStartOptions().FirstOrDefault(o => o.Key == key)?.Name ?? key;
				_session.Use(SpaceSim.Core.Content.WorldBuilder.BuildDefault(startOption: key));
				_orbit.Reset();   // Runde 19 (Handy-Test: der Gefechtstest begann mit der zuvor verstellten Kamera flach und weit weg); Andocken/Sprung behalten sie
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
			case "economy": _sfx.Play(SfxId.Click); _hud.Economy.Visible = !_hud.Economy.Visible; _hud.Economy.Refresh(_session, force: true); ShowBase(); break;
			case "save": case "load": OnMenu(action); break;
			// Runde 13 (Handy-Test „Musik reagiert nicht“): die Ton-Schalter des Hamburger-Menüs kamen hier an, behandelt wurden sie nur in OnMenu
			case "music_mute": case "effects_mute": _sfx.Play(SfxId.Click); OnMenu(action); break;
			// Runde 33 (Handytest A15: „UI-Größe“ im Menü ohne Wirkung): dieselbe Ursache — die Aktion kam hier an und fiel in den default-Zweig (kein
			// Kommandozentralen-Bereich); jetzt umgeschaltet, Menü bleibt offen (Beschriftung zeigt die neue Stufe), Hinweis liegt über dem Menü
			case "ui_size": _sfx.Play(SfxId.Click); _hud.Command.HoldRow("ui_size"); Ui.Kit.UiScale.Cycle(); _hud.UpdateLayerScales(); _hud.Command.SetAudioState(); _hud.Toast(Ui.Kit.UiScale.ShortText(), 4f); break;   // Runde 34: kurz (Handytest r33)
			// HUD-Neu: Kommandozentrale und Zeitraffer stehen im Menü (vorher eigene Tasten oben rechts)
			case "command": _sfx.Play(SfxId.Click); _hud.Map.Visible = false; OpenCcArea(CommandAreas.Universe); break;
			case "fast": _sfx.Play(SfxId.Click); _fast = !_fast; _hud.Command.SetFast(_fast); _hud.Toast(_fast ? "Zeitraffer ×10 an" : "Zeitraffer aus (×1)"); break;
			case "cam_debug": _camDebug = !_camDebug; _hud.SetDebugLine(_camDebug ? CameraDebugLine() : null); _hud.Toast(_camDebug ? "Kamera-Diagnose an" : "Kamera-Diagnose aus"); break;   // Runde 16
			// Gerätetest Phase 27: „Neues Spiel“ startet nicht sofort ein Standardspiel — es führt zur Startpaket-Auswahl (System)
			case "new": _sfx.Play(SfxId.Click); OpenCcArea(CommandAreas.System); _hud.CcSystem.TestChoosing = true; break;
			// Kamera-Einstellungen: Menü bleibt offen, damit man die Wirkung sieht
			case "zoom_in": _sfx.Play(SfxId.Click); _orbit.ZoomStep(-1); ManualZoom(); PersistCamera(); RefreshCameraMenu(); break;   // Runde 12: gleichmäßige Stufen
			case "zoom_out": _sfx.Play(SfxId.Click); _orbit.ZoomStep(+1); ManualZoom(); PersistCamera(); RefreshCameraMenu(); break;
			case "cam_free": _sfx.Play(SfxId.Click); _orbit.Mode = CameraMode.Free; PersistCamera(); RefreshCameraMenu(); break;
			case "cam_chase": _sfx.Play(SfxId.Click); _orbit.Mode = CameraMode.Chase; _orbit.SnapHeading(_player.Heading); PersistCamera(); RefreshCameraMenu(); break;   // Runde 44
			case "chase_up": _sfx.Play(SfxId.Click); _orbit.ChaseRaise(+OrbitCamera.ChaseHeightStep); PersistCamera(); RefreshCameraMenu(); break;
			case "chase_down": _sfx.Play(SfxId.Click); _orbit.ChaseRaise(-OrbitCamera.ChaseHeightStep); PersistCamera(); RefreshCameraMenu(); break;
			case "cam_lock":
				_sfx.Play(SfxId.Click); _orbit.Mode = CameraMode.TargetLock; PersistCamera(); RefreshCameraMenu();
				if (LockTargetPosition() == null) _hud.Toast("Target-Lock: Wähle ein Ziel (Station, Gate, Asteroid antippen)");
				break;
			case "cam_invert": _sfx.Play(SfxId.Click); _orbit.InvertDrag = !_orbit.InvertDrag; PersistCamera(); RefreshCameraMenu(); break;
			case "cam_reset": ResetCamera(); RefreshCameraMenu(); break;
			case "emergency": CallEmergency(); break;
			case "close": _sfx.Play(SfxId.Click); ShowBase(); break;   // Runde 23: angedockt zurück ins Dock-Menü
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
		ShowBase();
		_hud.Toast(r.Message, 5f);
	}

	/// <summary>Öffnet einen Bereich der Kommandozentrale im Vollbild (wie ein Tipp in der linken Leiste).</summary>
	bool _sectorChosen;
	void OpenCcArea(string areaId)
	{
		var u = _session.U;
		_hud.CcCtx.U = u;
		_hud.CcCtx.Player = PlayerData;
		_hud.CcCtx.FractionalTick = () => _session.FractionalTick;
		_hud.CcCtx.AutoMiningOn = () => _miner.Enabled;
		_hud.CcCtx.Docked = _docked;
		_hud.CcCtx.Services = _session.Services;
		_hud.CcCtx.Income = () => _session.Income;   // Runde 38
		// Runde 17: die Sektoransicht zeigt beim Öffnen den Spielersektor, außer die Universumskarte hat bewusst einen anderen gewählt
		if (areaId == CommandAreas.Sector && (!_sectorChosen || _hud.CcSector.SelectedSector == null)) _hud.CcSector.ShowPlayerSector(u.PlayerSector);
		_sectorChosen = false;
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
		ShowBase();   // Runde 23: Kommandozentrale/Menü angedockt geöffnet → zurück ins Dock-Menü
	}

	/// <summary>
	/// Runde 37b (N1): Grundansicht — alle Bildschirme und die Karte zu, angedockt das Dock-Menü, sonst das Flug-HUD. Der eine Rückweg für Schließen, „← Flug“,
	/// Karte und Zurück-Taste (vorher an jeder Stelle einzeln, die Karte hatte keinen).
	/// </summary>
	void ShowBase()
	{
		if (_hud.Screens.Current is CommandScreen) CommandScreen.ForgetRailArea();   // Runde 37c (Prüfung M2): beim Schließen der Zentrale
		_hud.Map.Visible = false;
		_hud.Screens.Hide();
		if (_docked == null) _hud.Flight.Visible = true;
		else ShowDockMenu();
	}

	/// <summary>
	/// Runde 37b (Handytest r37: die Android-Zurück-Taste beendete die App): schließt die oberste Ebene — Karte, Hamburger-Menü, Kommandozentrale, Dock-Bereich
	/// (→ Dock-Menü); im Dock-Menü ein Hinweis (Abdocken bleibt eine bewusste Taste), im Flug öffnet sie das Hamburger-Menü. Nie die App beenden
	/// (project.godot: application/config/quit_on_go_back=false). Escape am Desktop wirkt gleich.
	/// </summary>
	/// <summary>
	/// Runde 37c (Handytest r37b, Blocker: ein Druck der Android-Zurück-Taste löste HandleBack zweimal aus, 2–16 ms Abstand — Menü schloss und öffnete sich,
	/// im Flug passierte nichts): Godot stellt einen Druck auf manchen Geräten doppelt zu (Tasten-Ab und -Auf rufen je onBackPressed; beide kommen als
	/// WM_GO_BACK_REQUEST an). Ein Druck wird nur einmal verarbeitet: jede weitere Zustellung innerhalb von <see cref="BackDebounceFrames"/> Bildern
	/// (gleich, ob Benachrichtigung oder Taste) wird verworfen und protokolliert.
	/// </summary>
	internal string BackRequested(string source)
	{
		ulong frame = Engine.GetProcessFrames();
		// Runde 37d: nur verwerfen, wenn BEIDES gilt — höchstens 6 Bilder UND höchstens 150 ms (bei unter 20 Bildern/s wären 6 Bilder sonst mehr als ein
		// echter schneller Doppeldruck; die Doppelzustellung am Gerät kommt nach 2–16 ms)
		ulong nowMs = BackClock();
		if (_lastBackFrame != ulong.MaxValue && frame - _lastBackFrame <= BackDebounceFrames && nowMs - _lastBackMs <= BackDebounceMs)
		{
			GD.Print($"[ui] Zurück-Taste ({source}) verworfen: zweite Zustellung desselben Drucks ({frame - _lastBackFrame} Bilder)");
			Ui.InputLog.Log($"zurück ({source}) verworfen: zweite Zustellung ({frame - _lastBackFrame} Bilder, {nowMs - _lastBackMs} ms)");
			BackIgnored++;
			return "verworfen";
		}
		string since = _lastBackFrame == ulong.MaxValue ? "erster" : $"{frame - _lastBackFrame} Bilder/{nowMs - _lastBackMs} ms nach dem vorigen";
		_lastBackFrame = frame;
		_lastBackMs = nowMs;
		string r = HandleBack();
		Ui.InputLog.Log($"zurück ({source}, {since}) → {r}");
		BackHandled++;
		LastBackResult = r;
		GD.Print($"[ui] Zurück-Taste ({source}): {r}");
		return r;
	}
	internal const ulong BackDebounceFrames = 6;
	internal const ulong BackDebounceMs = 150;
	ulong _lastBackMs;
	/// <summary>Runde 37d: Uhr der Entprellung (Selbsttest setzt eine eigene, um eine niedrige Bildrate nachzustellen).</summary>
	internal Func<ulong> BackClock = () => Time.GetTicksMsec();
	ulong _lastBackFrame = ulong.MaxValue;
	/// <summary>Selbsttest: verarbeitete und verworfene Zurück-Zustellungen.</summary>
	internal int BackHandled, BackIgnored;
	internal string LastBackResult = "";
	internal bool TestTransition => _transition;

	internal string HandleBack()
	{
		if (_catchUp is not null) return "nachholen";
		// Runde 37d: während der An-/Abdock-Animation nichts (vorher öffnete Zurück mitten im Anflug das Menü)
		if (_transition) return "animation";
		var cur = _hud.Screens.Current;
		// Runde 37c: innerste Ebene zuerst — offene Meldung, Fraktionsdetail (→ Liste), Startpaket-Auswahl, offene Handelszeile
		if (_hud.TestAlert.Length > 0) { _hud.HideAlert(); return "meldung"; }
		if (_hud.Map.Visible) { _hud.Map.Visible = false; ShowBase(); return "karte"; }
		if (cur == _hud.Command) { _sfx.Play(SfxId.Click); ShowBase(); return "menü"; }
		if (cur == _hud.CcFactions && _hud.CcFactions.TestDetailOpen && _hud.CcFactions.IsNarrow) { _hud.CcFactions.TestDetailOpen = false; return "fraktionsliste"; }
		if (cur == _hud.CcSystem && _hud.CcSystem.TestChoosing) { _hud.CcSystem.TestChoosing = false; return "startpakete"; }
		if (cur is CommandScreen) { CloseCommand(); return "zentrale"; }
		if (cur == _hud.DockTrade && _hud.DockTrade.CloseOpenRow()) return "handelszeile";
		if (cur is not null && cur != _hud.DockMenu && cur != _hud.Flight && _docked != null) { _sfx.Play(SfxId.Click); ShowDockMenu(); return "dockbereich"; }
		if (cur == _hud.DockMenu) { _hud.Toast("Zum Abdocken „Abdocken“ antippen."); return "dockmenü"; }
		ToggleCommand();
		return "flug";
	}

	void ToggleCommand()
	{
		Ui.InputLog.Log($"menü umschalten (jetzt {(_hud.Screens.Current == _hud.Command ? "offen → zu" : "zu → auf")}, Aufrufer {Ui.InputLog.Caller()})");
		if (_hud.Screens.Current == _hud.Command) { ShowBase(); return; }
		else { _hud.Map.Visible = false; RefreshCameraMenu(); _hud.Screens.Show(_hud.Command); }
	}

	/// <summary>Zielposition für den Target-Lock (XZ): gewählter Asteroid, sonst Autopilot-Ziel im selben Sektor; sonst null.</summary>
	Vector3? LockTargetPosition()
	{
		if (_fight.Target.Value != 0 && SpaceSim.Core.Combat.PlayerCombat.PositionOf(_session.U, _fight.Target) is { } sp && sp.Sector == _session.U.PlayerSector)
			return new Vector3(sp.Pos.X, 0f, sp.Pos.Y);   // Phase 25: Kampfkamera — das gewählte Schiff liegt vor dem Schiff
		if (_mineTarget is { Minable: true } rock) return new Vector3(rock.Position.X, 0f, rock.Position.Y);
		if (_target is { } t && t.Sector == _session.U.PlayerSector) return new Vector3(t.Position.X, 0f, t.Position.Y);
		return null;
	}

	/// <summary>Überträgt den Kamerazustand (Modus, Invertierung, Abstand) in die Anzeige des Hamburger-Menüs.</summary>
	void RefreshCameraMenu()
	{
		_hud.Command.SetCameraState(_orbit.Mode, _orbit.InvertDrag, _orbit.Distance, LockTargetPosition() != null, _orbit.ChaseHeight, _orbit.ChaseBack);
		_hud.Command.SetFuelActive(_session.U.Rules.FuelActive);   // Phase 14: Notruf nur mit Treibstoff
		_hud.Command.SetAudioState();   // Runde 12: Ton-Schalter
		_hud.Command.SetFast(_fast);   // HUD-Neu
		_hud.Command.SetCommsBadge(SpaceSim.Core.Comms.Comms.Unread(_session.U));   // Runde 40: echter Zähler
	}

	const float MenuZoomStep = 0.85f;

	/// <summary>Runde 44: Kamera-Einstellungen speichern (Menütipp sofort, Gesten am Gestenende; schreibt nur bei Änderung).</summary>
	void PersistCamera() => SpaceAdventure.Game.CameraSettings.Save(SpaceSim.Core.Flight.CameraPrefs.Of(_orbit));

	void SaveNow(bool manual)
	{
		SyncPlayerData();
		bool ok = SaveService.Save(_session.U, out var error);
		if (manual)
		{
			_hud.Toast(ok ? "Gespeichert" : "Speichern fehlgeschlagen: " + error, ok ? 2f : 4f);
			// Gerätetest Phase 27: Rückmeldung auch im System-Bereich der Kommandozentrale (der Toast liegt darunter)
			if (_hud.Screens.TryGet("cc.system", out var sys) && sys is Ui.Screens.SystemScreen ss)
				ss.ShowMessage(ok ? $"Gespeichert ({DateTime.Now:HH:mm:ss})" : "Speichern fehlgeschlagen: " + error, ok);
		}
		else if (!ok) GD.PushWarning("Autosave fehlgeschlagen: " + error);
	}

	void SyncPlayerData()
	{
		if (_reenter) return;   // Phase 12: das aktive Schiff hat gewechselt — erst die Ansicht umsetzen, sonst bekäme das neue Schiff die alte Position
		PlayerData.Position = new Vec2(_player.Position.X, _player.Position.Z);
		if (_docked == null && !_transition && PlayerData.DockedAt.Value != 0) PlayerFleet.Undock(_session.U, PlayerData);   // fliegt = nicht angedockt
	}

	/// <summary>Phase 12: Das aktive Schiff liegt woanders (Fernwechsel, Taxi, Leihschiff) — die Ansicht setzt es im nächsten Frame um.</summary>
	bool _reenter;
	/// <summary>Runde 43: laufender Dock-/Abdock-Tween des Spielerschiffs — wird bei Sitzungswechsel/Ansicht-Reset gekillt, sonst schreibt ein
	/// Tween der alten Sitzung nach dem Reset weiter (Schiff blieb z. B. nach „Neues Spiel“ während des Andockens unsichtbar bei Maßstab 0,1).</summary>
	Godot.Tween? _playerTween;

	/// <summary>
	/// Phase 12: Ansicht auf das (neue) aktive Schiff setzen, dort wo es ist: Sektor neu aufbauen, Schiff an seine Position, angedockt → Andock-Menü,
	/// sonst Flug. Autopilot und Auto-Mining enden, die Kamera behält ihre Einstellung.
	/// </summary>
	void EnterActiveShip()
	{
		_reenter = false;
		var u = _session.U; var p = PlayerData;
		_hud.Screens.Hide();
		_target = null; _targetStation = null; _autopilot = false; _hud.Flight.SetAutopilot(false);
		SetAutoMining(false, announce: false);
		_chunks.Clear(); _mineTarget = null; _mining.ClearPlayer(); _mining.ResetNpc(); _combat.Reset(); _fight.ClearTarget();
		_transition = false; _playerTween?.Kill(); _player.Scale = Vector3.One; _player.Velocity = Vector3.Zero; _player.GiveGrace(2f);
		_lowestHull = p.Hull;
		_view.Rebuild(u);
		_player.Position = new Vector3(p.Position.X, 0, p.Position.Y);
		_camOffset = _orbit.Offset();
		SnapCam();
		if (u.Stations.TryGetValue(p.DockedAt, out var st))
		{
			_docked = st.Id;
			_player.Visible = false;
			_player.Position = new Vector3(st.Position.X, 0, st.Position.Y);
			ShowDockMenu();
			// Runde 42c: auch Taxi/Fernwechsel/Load — Andocken schaltet den Zeitraffer auf ×1 (Runde 42d: Meldung sichtbar)
			if (ResetFastOnDock()) _hud.Toast("Zeitraffer aus (angedockt)", 3.5f);
		}
		else
		{
			_docked = null;
			_player.Visible = true;
			_hud.Flight.Visible = true;
		}
	}

	/// <summary>Phase 12: Fernwechsel (Flotte, Besitz): in das gewählte eigene Schiff, dort wo es ist (Core: <see cref="PlayerFleet.SwitchRemote"/>).</summary>
	void SwitchRemote(ShipId id)
	{
		SyncPlayerData();
		bool ok = PlayerFleet.SwitchRemote(_session.U, id, out var message);
		_sfx.Play(ok ? SfxId.Dock : SfxId.Click);
		if (_reenter) EnterActiveShip();
		_hud.Toast(message, 3.5f);
	}

	/// <summary>Phase 12: Taxi gebucht — der Spieler sitzt schon im Schiff am Ziel; die Reisezeit vergeht im Zeitraffer, dann übernimmt die Ansicht.</summary>
	void OnTaxiBooked(bool ok, long ticks)
	{
		_sfx.Play(ok ? SfxId.Jump : SfxId.Click);
		if (!ok) return;
		long ran = _session.RunTicks(ticks);
		LastTaxiTicks = ran;
		if (_reenter) EnterActiveShip();
		_hud.Toast($"Taxi angekommen ({ran / SimClock.TicksPerSecond:0} s Reise): {(_session.U.Stations.TryGetValue(PlayerData.DockedAt, out var st) ? st.Name : "")}", 3.5f);
	}

	/// <summary>Phase 20: letzter Hinweis einer Versorgungspanik (Selbsttest).</summary>
	internal string? LastPanicMessage { get; private set; }
	/// <summary>Phase 22: letzter Hinweis „Station aufgegeben …“ (für den Selbsttest).</summary>
	internal string? LastDepartureMessage { get; private set; }
	/// <summary>Phase 24: letzter Gefechtshinweis im Spielersektor (Selbsttest).</summary>
	internal string? LastCombatMessage { get; private set; }
	/// <summary>Phase 27: letzte Meldung des Flottenkampfs (Verstärkung, Zuflucht, Verfolgung, Geleit) — Selbsttest.</summary>
	internal string? LastFleetMessage { get; private set; }
	/// <summary>Phase 25: letzte Verlustmeldung des aktiven Schiffs (Selbsttest).</summary>
	internal string? LastLossMessage { get; private set; }
	int _dropSeq;
	/// <summary>Phase 24: gezeichnete Gefechtsschüsse (Selbsttest).</summary>
	internal int TestCombatShots => _combat.ShotsSeen;
	/// <summary>Runde 12: Bildschirmposition eines Weltpunkts (Selbsttest: Gegner nicht unter Warnflächen).</summary>
	internal Vector2 TestScreenOf(Vector3 world) => _cam.UnprojectPosition(world);
	/// <summary>Runde 11: Darstellung der Schüsse/Raketen (Selbsttest: Mündung des letzten Strahls bzw. der letzten Rakete).</summary>
	internal CombatView TestCombatView => _combat;
	/// <summary>Runde 11: Spielerschiff mit dem Bug an den nächsten abbaubaren Asteroiden setzen (Kontaktabbau), Automatik aus.</summary>
	internal void TestTouchNearestRock()
	{
		var u = _session.U;
		var sector = u.Sectors[u.PlayerSector];
		var rock = sector.Asteroids.Where(a => a.Minable).OrderBy(a => Vec2.Distance(a.Position, new Vec2(_player.Position.X, _player.Position.Z))).FirstOrDefault();
		if (rock is null) return;
		CancelAutopilot();
		if (_miner.Enabled) SetAutoMining(false);
		var pos = rock.Position + new Vec2(1f, 0f) * (rock.Radius + 1.5f);
		_player.Position = new Vector3(pos.X, 0, pos.Y);
		_player.Velocity = Vector3.Zero;
		_player.GiveGrace(5f);
		TestFace(rock.Position);
	}
	/// <summary>Phase 28: zuletzt gezeigte Raketen, letzte Kriegs-/Stationsmeldung, scharfes Stationsziel (Selbsttest).</summary>
	internal int TestMissilesShown => _combat.MissilesShown;
	internal IEnumerable<string> TestBayPlaces => _bayViews.Where(b => b != null).Select(b => b.Place);
	internal IEnumerable<string> TestBayDetails => _bayViews.Where(b => b != null).Select(b => b.Detail);
	internal IEnumerable<string> TestBayNames => _bayViews.Where(b => b != null).Select(b => b.Name);   // Runde 20
	internal int TestTargetStation => _targetStation?.Value ?? 0;
	internal void TestClearTarget() { _target = null; _targetStation = null; }
	internal string LastWarMessage { get; private set; } = "";
	internal SpaceSim.Core.Model.StationId TestStationTarget => _fight.StationTarget;
	internal void TestArmStation() => ArmStationAttack();
	internal bool TestFireSlot(int slot) => FireWeapon(slot, quiet: true);
	internal string TestFightMessage => _fight.LastMessage;
	long _panicToastTick = -1;

	/// <summary>Für den Selbsttest: Dauer der letzten Taxifahrt in Ticks.</summary>
	internal long LastTaxiTicks { get; private set; }

	public override void _Notification(int what)
	{
		// Phase 28: Pause mitten im Nachholen — der Rest verfällt, die Regel wird zurückgesetzt, dann gespeichert
		if (_catchUp is { } pending && (what == NotificationWMCloseRequest || what == NotificationApplicationPaused)) FinishCatchUp(pending);
		if (_session != null && (what == NotificationWMCloseRequest || what == NotificationApplicationPaused)) SaveNow(manual: false);
		// Runde 12: Musik hält bei App-Pause an und läuft beim Fortsetzen weiter
		if (_music is not null && what == NotificationApplicationPaused) _music.SetPaused(true);
		if (_music is not null && what == NotificationApplicationResumed) _music.SetPaused(false);
		if (what == NotificationWMGoBackRequest && _hud is not null) BackRequested("Benachrichtigung");   // Runde 37b/37c
	}

	void OnSimEvent(SimEvent e)
	{
		if (JumpCloakEvent(e)) return;   // Sprungantrieb und Tarnfeld (Main.JumpCloak.cs)
		switch (e)
		{
			// Runde 12 (Handy-Test): nur Stationen im eigenen Sektor melden
			case StationBuilt b when _session.U.Stations.TryGetValue(b.Station, out var nb) && nb.Sector == _session.U.PlayerSector: _hud.Info($"Neue Station: {nb.Name}"); break;   // Runde 38a: Info (MessageRules)
			case ConstructionStarted cs when cs.Sector == _session.U.PlayerSector: _hud.Info($"Neue Baustelle: {_session.U.Stations[cs.Site].Name}"); break;
			// Phase 20: Versorgungspanik nach einem geschlossenen/zerstörten Tor (welt-weit sichtbar: die abgeschnittenen und die übrigen Sektoren reagieren)
			case SupplyPanicRaised sp when _session.U.Tick != _panicToastTick:   // je Tick ein Hinweis (eine Sperre löst oft mehrere Waren zugleich aus)
				_panicToastTick = _session.U.Tick;
				LastPanicMessage = SpaceSim.Core.Simulation.SupplyPanic.Describe(_session.U, sp); _hud.Info(LastPanicMessage, 4f); break;
			case EmergencyConstruction ec when _session.U.Stations.TryGetValue(ec.Site, out var es) && es.Sector == _session.U.PlayerSector: _hud.Info($"Notbau: {es.Name}"); break;
			case PlayerSold: _sales++; break;
			case MinerShot shot when shot.Sector == _session.U.PlayerSector: OnMinerShot(shot); break;
			case ContactMined cm when cm.Sector == _session.U.PlayerSector: OnContactMined(cm); break;
			case AnchorReleased ar when ar.Sector == _session.U.PlayerSector: OnAnchorReleased(ar); break;   // Phase 32
			case AnchorFired af when af.Ship == _session.U.PlayerShip: _anchorBrokeLeft = 0f; break;   // Runde 18: neues Ansetzen löscht den Rissgrund
			case AsteroidSplit split when split.Sector == _session.U.PlayerSector:   // Runde 12: Spaltbacken klappen
				if (split.Ship == _session.U.PlayerShip) _player.Visual?.SnapJaws(); else _view.SnapJaws(split.Ship);
				break;
			// Phase 24: NPC-Gefechte im Spielersektor — Schüsse als Strahlen, Beginn als Hinweis
			case ShotFired sf when sf.Sector == _session.U.PlayerSector:
				_fight.OnShot(_session.U, sf, Time.GetTicksMsec() / 1000.0);
				if (sf.Target == _session.U.PlayerShip && sf.Hit) { _hud.Flash(new Color(1f, 0.25f, 0.15f), 0.25f); _sfx.Play(SfxId.Hit); _sinceHit = 0f; }
				if (sf.Target == _session.U.PlayerShip) Danger("du wirst beschossen");
				_combat.Shot(sf.Shooter, sf.Slot, default, -1, sf.Target, null, null, sf.Type, sf.Hit, Time.GetTicksMsec() / 1000.0, sf.Shooter.Value * 37 + _combat.ShotsSeen * 53);
				if (sf.Hit && _view.ShipPosition(sf.Target) is { } hp) _fx.Burst(hp, sf.Type == DamageType.Energy ? new Color(1f, 0.4f, 0.3f) : new Color(1f, 0.85f, 0.4f), 4, 5f, 0.3f);
				break;
			// Phase 28: Stationsgeschütze, Schüsse auf Stationen, Raketen, Fall einer Station, Krieg und Frieden
			case StationShot sts when sts.Sector == _session.U.PlayerSector && _session.U.Stations.TryGetValue(sts.Station, out var sst):
				_fight.OnStationShot(_session.U, sts, Time.GetTicksMsec() / 1000.0);
				if (sts.Target == _session.U.PlayerShip) { Danger("Station feuert auf dich"); if (sts.Hit) { _hud.Flash(new Color(1f, 0.25f, 0.15f), 0.25f); _sfx.Play(SfxId.Hit); } }
				_combat.Shot(default, -1, sts.Station, sts.Gun, sts.Target, new Vector3(sst.Position.X, 0, sst.Position.Y), null, sts.Type, sts.Hit, Time.GetTicksMsec() / 1000.0, sts.Station.Value * 41 + _combat.ShotsSeen * 53);
				break;
			case ShotAtStation sas when sas.Sector == _session.U.PlayerSector && _session.U.Stations.TryGetValue(sas.Station, out var sast):
				_combat.Shot(sas.Shooter, sas.Slot, default, -1, default, null, new Vector3(sast.Position.X, 0, sast.Position.Y), sas.Type, sas.Hit, Time.GetTicksMsec() / 1000.0, sas.Shooter.Value * 37 + _combat.ShotsSeen * 53);
				if (sas.Hit) _fx.Burst(new Vector3(sast.Position.X, 0, sast.Position.Y), new Color(1f, 0.6f, 0.3f), 5, 6f, 0.3f);
				break;
			// Phase 31: Schüsse auf Tore, Torangriff, Ausfall und Wiederanlauf
			case ShotAtGate sag when sag.Sector == _session.U.PlayerSector && _session.U.Gates.TryGetValue(sag.Gate, out var sagt):
				_combat.Shot(sag.Shooter, sag.Slot, default, -1, default, null, new Vector3(sagt.Position.X, 0, sagt.Position.Y), sag.Type, sag.Hit, Time.GetTicksMsec() / 1000.0, sag.Shooter.Value * 37 + _combat.ShotsSeen * 53);
				if (sag.Hit) _fx.Burst(new Vector3(sagt.Position.X, 0, sagt.Position.Y), new Color(0.6f, 0.8f, 1f), 5, 6f, 0.3f);
				break;
			// Runde 14 (Handy-Test Kriegstest): der eigene Verband tritt erkennbar bei
			case CombatJoined cj when cj.Reason == "squadron" && _session.U.Ships.TryGetValue(cj.Ship, out var cjs) && cjs.IsPlayer && cjs.Id != _session.U.PlayerShip:
				if (Time.GetTicksMsec() / 1000.0 - _lastWingToast > 20.0) { _lastWingToast = Time.GetTicksMsec() / 1000.0; _hud.Toast("Dein Verband greift mit an.", 3f); }
				break;
			case GateAttacked ga:
				LastWarMessage = ga.Message;
				if (ga.Sector == _session.U.PlayerSector) _hud.Toast(ga.Message, 4f);
				break;
			case GateDisabled gd:
				LastWarMessage = gd.Message;
				if (_session.U.Gates.TryGetValue(gd.Gate, out var gdg) && (gdg.Sector == _session.U.PlayerSector || _session.U.Gates[gdg.Target].Sector == _session.U.PlayerSector)) _hud.Alert(gd.Message, 8f);
				else _hud.Toast(gd.Message, 4f);
				break;
			case GateRestored gr:
				LastWarMessage = gr.Message;
				// Runde 17 (Handy-Test: Wiederanlauf-Meldung nicht gesehen): im eigenen Sektor als Meldung wie der Ausfall, sonst Hinweis
				if (_session.U.Gates.TryGetValue(gr.Gate, out var grg) && (grg.Sector == _session.U.PlayerSector || _session.U.Gates[grg.Target].Sector == _session.U.PlayerSector)) _hud.Alert(gr.Message, 6f);
				else _hud.Toast(gr.Message, 4f);
				break;
			case MissileLaunched ml when ml.Sector == _session.U.PlayerSector:
				_sfx.Play(SfxId.Zap);
				if (ml.Target == _session.U.PlayerShip) Danger("Rakete im Anflug");
				break;
			case MissileResolved mr when mr.Sector == _session.U.PlayerSector && mr.Outcome == "hit":
				if (mr.TargetStation.Value != 0 && _session.U.Stations.TryGetValue(mr.TargetStation, out var mst)) _fx.Burst(new Vector3(mst.Position.X, 0, mst.Position.Y), new Color(1f, 0.5f, 0.6f), 10, 8f, 0.4f);
				else if (_view.ShipPosition(mr.Target) is { } mp) _fx.Burst(mp, new Color(1f, 0.5f, 0.6f), 10, 8f, 0.4f);
				if (mr.Target == _session.U.PlayerShip) { _hud.Flash(new Color(1f, 0.3f, 0.4f), 0.3f); _sfx.Play(SfxId.Hit); }
				break;
			case StationFell fell:
				LastWarMessage = fell.How switch { "destroyed" => $"{fell.Name} wurde zerstört.", "captured" => $"{fell.Name} wurde erobert.", _ => $"{fell.Name} ist ausgeschaltet." };
				if (fell.Sector == _session.U.PlayerSector) _hud.Alert(LastWarMessage, 6f); else _hud.Info(LastWarMessage, 3f);
				break;
			case WarDeclared wd: LastWarMessage = wd.Message; _hud.Alert(wd.Message, 8f); _music.Sting("sting_war", Now); break;
			case PeaceConcluded pc: LastWarMessage = pc.Message; _hud.Alert(pc.Message, 6f); _music.Sting("sting_peace", Now); break;
			case BountyEarned be when be.Hunter == _session.U.PlayerActor: _music.Sting("sting_victory", Now); break;   // Runde 12: Signale
			case EncounterStarted siege when siege.Cause == EncounterCause.Siege && siege.Sector == _session.U.PlayerSector:
				LastCombatMessage = siege.Message; _hud.Toast(siege.Message, 4f); break;
			// Gerätetest Phase 28: Überfall auf dich — das angreifende Schiff wird Ziel, Hinweis auf Dauerfeuer/Zielhilfe
			case EncounterStarted raid when raid.Cause == EncounterCause.Raid && raid.Defender == _session.U.PlayerShip:
				TakeShipTarget(raid.Attacker);   // Runde 21: räumt Tor-/Stationswahl samt Angriff ab
				LastCombatMessage = $"Überfall! Ziel gewählt: {(_session.U.Ships.TryGetValue(raid.Attacker, out var ra) ? SpaceSim.Core.Combat.PlayerCombat.Describe(_session.U, ra) : "Kaperschiff")} — " + (_fight.AutoFire ? "Dauerfeuer läuft." : "„Dauerfeuer“ an?") + " „Ziel folgen“ hält es im Bugwinkel.";   // Runde 22: je nach Zustand
				Danger("Überfall", LastCombatMessage, 8f);
				break;
			case EncounterStarted arrest when arrest.Cause == EncounterCause.Arrest:
				LastCombatMessage = arrest.Message; Danger("Polizei greift an", arrest.Message, 8f); break;
			case EncounterStarted es when es.Sector == _session.U.PlayerSector:
				LastCombatMessage = es.Cause == EncounterCause.Assault ? es.Message
					: SpaceSim.Core.Combat.CombatInfo.StartToast(_session.U, es.Cause, _session.U.Fights.Of(es.Attacker)?.RaiderOwner ?? default);
				// Runde 38a: fremdes Gefecht im Sektor = Info; dein eigenes (z. B. Angriff) wichtig
				if (SpaceSim.Core.Ui.MessageRules.InMyFight(_session.U, es.Encounter)) _hud.Toast(LastCombatMessage, 4f); else _hud.Info(LastCombatMessage, 3f);
				break;
			// Phase 22: Station im Spielersektor aufgegeben — Grund (pleite / länger nicht rentabel) im Text
			case StationDeparted mine when _docked is { } dk && mine.Station == dk:
				_lostStationMessage = (SpaceSim.Core.Simulation.Departures.StationToast(mine) ?? "Die Station wurde aufgegeben.") + " Du bist abgedockt und wieder im Flug.";
				break;
			case StationDeparted sd when sd.Sector == _session.U.PlayerSector && SpaceSim.Core.Simulation.Departures.StationToast(sd) is { } departed:
				LastDepartureMessage = departed; _hud.Info(departed, 3.5f); break;
			case ActiveShipChanged changed: OnActiveShipChanged(changed); break;
			// Phase 25: Verlust des aktiven Schiffs — Explosion, Meldung; die Ansicht setzt im nächsten Frame in das neue Schiff um (EnterActiveShip)
			case PlayerShipLost lost when Sting("sting_loss"):
				_fx.Burst(_player.Position, new Color(1f, 0.5f, 0.2f), 60, 28f, 1.2f);
				_sfx.Play(SfxId.Explosion);
				Danger("Schiff verloren", lost.Message, 10f, sticky: true);   // Gerätetest Phase 28: bleibt über dem Dock-Menü, bis angetippt
				LastLossMessage = lost.Message;
				_reenter = true;
				break;
			case CargoDropped drop when drop.Sector == _session.U.PlayerSector && !_session.U.Rules.DamageActive: CombatController.SpawnDrops(_chunks, drop, _dropSeq++); break;
			case EncounterEnded end when end.Sector == _session.U.PlayerSector && end.Cause == EncounterCause.Assault:
				_hud.Toast(end.Message, 4f); LastCombatMessage = end.Message; break;
			// Phase 11: Vorwarnung, Ausgang von Überfall/Kontrolle, Überfälle in deinem Sektor, Verbrechen
			case EncounterWarning w when w.Target == _session.U.PlayerShip:
				// Runde 15/16: schon in der Warnphase Ziel (Kamera, Randpfeil) — auch wenn das Warnsignal gerade gesperrt ist (vorher hing alles am Signal);
				// ein laufendes Gefechtsziel bleibt
				if (w.Kind == EncounterKind.Raid && w.Source.Value != 0) { if (_fight.Target.Value == 0) TakeShipTarget(w.Source); _pendingTargeted = (w.Source, w.DueTick); }
				Sting("sting_warning");
				_sfx.Play(SfxId.Click); Danger("Überfall droht", w.Message, WarnSeconds(w.DueTick)); LastSecurityMessage = w.Message;
				StartCountdown(w.DueTick); break;
			case FireStarted fs when fs.Ship == _session.U.PlayerShip: Danger("Brand an Bord", SpaceSim.Core.Combat.DamageInfo.FireWarning(_session.U, PlayerData) ?? "Brand an Bord"); break;
			case EncounterEscaped x when x.Target == _session.U.PlayerShip:
				_hud.Toast(x.Message, 4f); LastSecurityMessage = x.Message;
				// Runde 41 (A2): Warnung aufgehoben (z. B. Waffenstillstand) — die stehende Warnmeldung im Alert sofort entfernen
				if (_warnShown is not null && _hud.TestAlert == _warnShown) { _hud.HideAlert(); _warnText = null; }
				break;
			case RaidResolved rr when rr.Player: _hud.Alert(rr.Message, 8f); LastSecurityMessage = rr.Message; break;
			case RaidResolved rn when rn.Sector == _session.U.PlayerSector:
				_hud.Info($"Hier im Sektor: {rn.Message}", 3f); break;   // Runde 38a: fremder Überfall = Info   // Runde 4: welches Schiff (vorher klang es mitten im Stationsangriff nach dem eigenen Gefecht)
			case RaiderIntercepted ri when ri.Sector == _session.U.PlayerSector:
				_hud.Info(ri.PoliceWon ? (ri.RaiderDestroyed ? "Polizei hat ein Kaperschiff zerstört" : "Polizei vertreibt ein Kaperschiff") : "Ein Kaperschiff entkommt der Polizei", 3f); break;
			case PlayerIntercepted pi: _hud.Toast(pi.Message, 6f); LastSecurityMessage = pi.Message; break;
			// Phase 27: Flottenkampf — Ergeben, Polizeigefecht, Verstärkung, Zuflucht, Verfolgung, Geleit im Spielersektor
			case PlayerSurrendered ps: _hud.Alert(ps.Message, 6f); LastSecurityMessage = ps.Message; break;
			// Runde 6: Rückzug aus der eigenen Belagerung bzw. ihr Ende — Angriff in der Zielkarte beenden, Dauerfeuer aus, Meldung
			case SiegeWithdrawn sw when sw.Player == _session.U.PlayerShip:
				_fight.ArmStation(default); _fight.AutoFire = false;
				// Runde 7: Sieg als Meldung (ersetzt „… ist ausgeschaltet“ um den Verband); Rückzug als Hinweis — eine danach folgende Fahndungswarnung (Meldung)
				// überdeckt ihn dann nicht, der Hinweis rückt unter sie
				if (sw.Won) _hud.Alert(sw.Message, 6f); else _hud.Toast(sw.Message, 6f);
				LastCombatMessage = sw.Message; break;
			case EncounterEnded se when se.Cause == EncounterCause.Siege && se.Sector == _session.U.PlayerSector:
				if (_fight.AtStation && _session.U.Stations.TryGetValue(_fight.StationTarget, out var downSt) && SpaceSim.Core.Combat.StationCombat.IsDown(_session.U, downSt))
				{ _fight.ArmStation(default); _fight.AutoFire = false; }
				if (_fight.AtGate && _session.U.Gates.TryGetValue(_fight.GateTarget, out var downG) && SpaceSim.Core.Combat.GateCombat.IsDown(_session.U, downG))
				{ _fight.ArmGate(default); _fight.AutoFire = false; }
				_hud.Toast(se.Message, 4f); LastCombatMessage = se.Message; break;
			case EncounterEnded ae when ae.Cause == EncounterCause.Arrest: _hud.Alert(ae.Message, 6f); LastCombatMessage = ae.Message; break;
			case CombatJoined or RefugeReached or PursuitStarted or EscortAssigned or ForceSquadron when SpaceSim.Core.Combat.FleetInfo.Toast(_session.U, e) is { } ft:
				// Runde 38a (Handy-Abschlusstest z1/z2): fremde Flucht/Verstärkung/Verfolgung nur als Info; groß nur, was dich oder dein Gefecht betrifft
				if (SpaceSim.Core.Ui.MessageRules.FleetWeight(_session.U, e) == SpaceSim.Core.Ui.MessageWeight.Important) _hud.Toast(ft, 3.5f); else _hud.Info(ft, 3f);
				LastFleetMessage = ft; break;
			// Runde 38a (Handy-Abschlusstest k7: der zweite Gegner verschwand ohne Meldung): jeder Ausgang eines Gegners in deinem Gefecht
			case CombatExited cx when SpaceSim.Core.Ui.MessageRules.EnemyExit(_session.U, cx) is { } exitMsg:
				_hud.Toast(exitMsg, 3.5f); LastCombatMessage = exitMsg; LastEnemyExit = exitMsg; break;
			case CrimeReported cr when cr.Actor == _session.U.PlayerActor:
				_hud.Toast($"Fahndung bei {(_session.U.Factions.TryGetValue(cr.Faction, out var cf) ? cf.Name : "?")}: {SecurityNames.WantedLevel(cr.Level)} ({SecurityNames.Crime(cr.Crime)})", 4.5f); break;
		}
	}

	// ───────── Frame ─────────

	/// <summary>Phase 28: laufendes Offline-Nachholen (null = keins). Bis es fertig ist, läuft nur das Nachholen; danach wird die Sitzung angehängt.</summary>
	CatchUpJob? _catchUp;

	/// <summary>Ein Bild Nachholen: Ticks bis etwa <paramref name="budgetMs"/> Millisekunden, Fortschritt anzeigen; fertig → Sitzung anhängen.</summary>
	void StepCatchUp(CatchUpJob job, double budgetMs = 80)   // Runde 14 (Handy-Test: 38 s Nachholen): Ladebild, darf langsamer zeichnen — mehr Ticks je Bild
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();
		while (!job.Done && sw.Elapsed.TotalMilliseconds < budgetMs) job.Step(_session.Runner, 25);
		_hud.ShowLoading($"Spielzeit wird nachgeholt … {job.Progress * 100f:0} %", job.Progress);
		if (!job.Done) return;
		FinishCatchUp(job);
	}

	void FinishCatchUp(CatchUpJob job)
	{
		job.Finish();
		_catchUp = null;
		_hud.HideLoading();
		AttachSession();
		var result = job.Result;
		if (result.TicksRun > 0)
			_hud.Toast($"Willkommen zurück – {result.TicksRun / SimClock.TicksPerSecond / 60} Min Spielzeit nachgeholt{(result.Capped ? " (gedeckelt)" : "")}", 4f);
	}

	/// <summary>Runde 44: Zeitraffer-Faktor und Teilschritt des eigenen Autopilot-Flugs.</summary>
	const float WarpFactor = 10f, WarpSubstep = 1f / 30f;
	int _lastFlightSteps = 1;
	/// <summary>Selbsttest: Zahl der Flug-Teilschritte im letzten Bild (1 = Echtzeit).</summary>
	internal int TestFlightSteps => _lastFlightSteps;
	/// <summary>
	/// Runde 44g: der eigene Flug läuft im Zeitraffer immer (manuell, Autopilot, Auto-Mining, Abbau) — nicht angedockt, ohne offenes Menü, nicht im Übergang
	/// und nicht im Gefecht bzw. bei angekündigtem Überfall (Schüsse, Zielhilfe und Dauerfeuer laufen im Bildtakt; ×10-Flug mit ×1-Feuer wäre schief).
	/// </summary>
	bool WarpFlightAllowed(bool frozen)
	{
		if (!_fast || frozen || _transition) return false;
		var u = _session.U;
		if (u.Fights.IsEngaged(u.PlayerShip)) return false;
		foreach (var p in u.PendingEncounters) if (p.Target == u.PlayerShip) return false;
		return true;
	}

	public override void _Process(double deltaD)
	{
		if (_catchUp is { } catchUp) { StepCatchUp(catchUp); return; }
		float delta = (float)deltaD;
		if (_reenter) EnterActiveShip();   // Phase 12: aktives Schiff liegt woanders (Fernwechsel, Taxi, Leihschiff)
		_jumpCooldown = Mathf.Max(0f, _jumpCooldown - delta);
		_splitReload = Mathf.Max(0f, _splitReload - delta);
		_hitFlash = Mathf.Max(0f, _hitFlash - delta);
		_anchorBrokeLeft = Mathf.Max(0f, _anchorBrokeLeft - delta);   // Runde 18

		_session.Advance(delta, _fast ? 10f : 1f);
		// Runde 37c (Prüfung M1): die angedockte Station kann verschwinden (pleite, aufgegeben, zerstört) — dann zurück in den Flug, mit Meldung (sonst blieb
		// _docked stehen und ShowBase/ShowDockMenu zeigten nichts)
		if (_docked is { } dockedId && !_session.U.Stations.ContainsKey(dockedId)) ForcedUndock(_lostStationMessage ?? "Die Station, an der du angedockt warst, gibt es nicht mehr — du bist wieder im Flug.");
		if (_view.NeedsRebuild(_session.U)) { _view.Rebuild(_session.U); _mining.ResetNpc(); _combat.Reset(); _chunks.Clear(); _mineTarget = null; _anchors.Clear(); }
		_view.SyncAsteroids(_session.U, delta);
		_view.SyncWrecks(_session.U);   // Phase 26: Wracks und Frachtbehälter
		GateSparks(delta);   // Phase 31: beschädigte Tore sprühen Funken
		_view.SyncStations(_session.U);   // Runde 8: Besitzerfarbe und Zustand der Stationsmodelle
		_missions.Sync(_session.U, delta);   // Runde 41: Leuchtfeuer der Missionsziele im Sektor
		UpdateJumpCloak(delta);   // Sprungantrieb: Ladeleuchten, Stillhalten; Tarnfeld: Durchsichtigkeit

		// Phase 26: keine Gratis-Regeneration der Hülle mehr (Reparatur nur über den Dienst „repair“); ohne Schadensmodell wie bisher
		_sinceHit += delta;
		if (!_session.U.Rules.DamageActive && _sinceHit > 5f && PlayerData.Hull < PlayerData.MaxHull && !_session.U.Fights.IsEngaged(PlayerData.Id)) PlayerData.Hull = Mathf.Min(PlayerData.MaxHull, PlayerData.Hull + 3f * delta);

		bool frozen = _hud.AnyModalOpen || _docked != null;
		// Runde 44 (größere Sektoren): Zeitraffer auch für den eigenen Flug — seit 44g immer, nicht nur mit Autopilot (Nutzerwunsch „Zeitraffer auch immer für
		// Spieler“): manueller Flug, Auto-Mining mit Anflug, Kontaktabbau, Anker, Traktor und Brocken laufen in Teilschritten (≤ 1/30 s, Kollision, Tore,
		// Eingaben, Abbau je Teilschritt), so schnell wie die Simulation (×10). Gefahr, Warnung und Andocken schalten den Zeitraffer wie bisher ab; im Gefecht
		// (und bei angekündigtem Überfall) bleibt der eigene Flug in Echtzeit, weil Schüsse, Zielhilfe und Dauerfeuer im Bildtakt laufen.
		var flightU = _session.U;
		bool warpFlight = WarpFlightAllowed(frozen);
		int flightSteps = warpFlight ? Mathf.Clamp(Mathf.CeilToInt(delta * WarpFactor / WarpSubstep), 1, 40) : 1;
		float flightDt = warpFlight ? delta * WarpFactor / flightSteps : delta;
		_lastFlightSteps = flightSteps;
		var flightSector = flightU.PlayerSector;
		_mineFire = false; _mineAim = null;
		for (int fs = 0; fs < flightSteps; fs++)
		{
		// angedockt, Sprung, Übergang, Menü, Gefahr oder Zeitraffer aus: der Rest des Bilds verfällt
		if (fs > 0 && (!WarpFlightAllowed(_hud.AnyModalOpen || _docked != null) || _reenter || flightU.PlayerSector != flightSector || _session.U != flightU)) break;
		float dt = flightDt;
		_miningStep = _miner.Enabled && !frozen
			? _miner.Step(_session.U, _chunks, PlayerData, new Vec2(_player.Position.X, _player.Position.Z))
			: null;
		// Reglertasten (W/X) wirken in Echtzeit — einmal je Bild; Wenden und Schub wirken je Teilschritt
		var (dir, throttle, speedScale, turn) = frozen ? (Vector2.Zero, 0f, 1f, 0f) : ReadSteering(fs == 0 ? delta : 0f);
		// Gerätetest Phase 28: Zielhilfe „Ziel folgen“ — ohne Wendetaste, ohne Autopilot/Auto-Mining dreht die Nase mit der Drehrate des Schiffs zum gewählten Feind
		// (Core-Regel AimAssist.Turn); Schub bleibt beim Regler und zeigt dann in die neue Blickrichtung
		if (!frozen && turn == 0f && _aimAssist && !_autopilot && !_miner.Enabled && AimTarget() is { } aimAt)
		{
			float h = AimAssist.Turn(_player.Heading, AimAssist.Bearing(new Vec2(_player.Position.X, _player.Position.Z), aimAt), AimAssist.PlayerTurnRate, dt);
			_player.Heading = h;
			if (throttle != 0f) dir = new Vector2(-Mathf.Sin(h), -Mathf.Cos(h));
		}
		_player.Locked = _docked != null || _transition;
		bool hold = _miner.Enabled && _miningStep is { Nav: null, Waypoint: null };   // Automatik arbeitet an Ort und Stelle
		// Treibstoff (Phase 4c): leerer Tank deckelt das Tempo (Notantrieb), die geflogene Strecke wird abgebucht
		float fuelCap = PlayerFuel.ThrottleCap(_session.U, PlayerData);
		var before = _player.Position;
		AnchorPin(throttle, turn, frozen);   // Phase 32: Verankerung hält das Schiff am Fels, Schub/Wenden reißt sie
		// Phase 23: Stellung „Antrieb“ hebt das Höchsttempo (Core-Regel ShipSystems.SpeedFactor)
		float engine = SpaceSim.Core.Combat.ShipSystems.SpeedFactor(_session.U, PlayerData);
		_player.Step(dt, turn, dir, throttle, speedScale * fuelCap * engine, _view, brake: hold);
		if (!_player.Locked)
		{
			PlayerFuel.ConsumeFlown(_session.U, PlayerData, new Vector2(_player.Position.X - before.X, _player.Position.Z - before.Z).Length());
			if (fuelCap >= 1f && PlayerFuel.ThrottleCap(_session.U, PlayerData) < 1f)
				_hud.Toast("Tank leer – Notantrieb. Notruf im Menü oder zur nächsten Tankstelle schleichen.", 5f);
		}
		_sfx.SetThrust(_player.Thrusting ? Mathf.Clamp(Mathf.Abs(throttle), 0.3f, 1f) : 0f);

		if (!frozen) CheckGates();
		// Abbau je Teilschritt (Kontakt, Anker, Laser-Automatik, Brocken, Traktor) — im Sprung-Teilschritt nicht mehr (anderer Sektor)
		if (flightU.PlayerSector == flightSector && _session.U == flightU) StepMining(dt, frozen, fs == 0);
		}
		if (!_miner.Enabled && _hud.Flight.AutoMineOn) { _hud.Flight.SetAutoMine(false); _hud.Toast(_miner.Status, 3.5f); }   // Automatik hat sich selbst beendet
		ShowMining(delta);
		SyncPlayerData();
		// Runde 17: im Gefecht und in der Warnphase nur wenige Stations-Randpfeile
		_hud.EdgeThin = _session.U.Fights.IsEngaged(PlayerData.Id) || _session.U.PendingEncounters.Any(p => p.Target == _session.U.PlayerShip);
		if (!frozen && !_reenter && _fight.AutoFire) FireCombatBays(quiet: true);   // Phase 25: Dauerfeuer

		_view.Animate(delta);
		long tl = System.Diagnostics.Stopwatch.GetTimestamp();
		HideCoveredLabels(delta);
		long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
		_view.UpdateShips(_session.U, _session.FractionalTick, _docked != null ? null : _player.Position, delta, new Vec2(-Mathf.Sin(_player.Heading), -Mathf.Cos(_player.Heading)));   // angedockt: niemand weicht dem unsichtbaren Schiff aus
		long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
		UpdateShipModels(delta);
		long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
		if (_camDebug) _hud.SetDebugLine(CameraDebugLine());   // Runde 16
		_musicClock += delta;
		if (_musicClock >= 0.1f) { UpdateMusic(_musicClock); _musicClock = 0f; }   // Runde 13 (FPS): Musikzustand mit 10 Hz
		long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
		Profile("labels", t0 - tl); Profile("ships", t1 - t0); Profile("models", t2 - t1); Profile("music", t3 - t2);
		// Kamera: Ist-Offset folgt weich dem Orbit-Soll-Offset, Kamera sitzt auf Schiff + Offset und blickt auf das Schiff
		// Runde 43b (Handy-Test: Kamerawinkel springt beim Lösen der Anker): am (verankerten oder eben gelösten) Fels führt der Target-Lock den Yaw nicht nach —
		// das Ziel liegt dort wenige Meter vor der Schiffsmitte, jede kleine Verschiebung (Entpinnen, Abprallen, Schub) drehte die Kamera stark
		if (_orbit.Mode == CameraMode.TargetLock && LockTargetPosition() is { } lt && !LockHeldAtRock())
			_orbit.FollowTarget(lt.X - _player.Position.X, lt.Z - _player.Position.Z, delta);   // Yaw führt das Ziel vor das Schiff
		if (_orbit.Mode == CameraMode.Chase && _docked == null) _orbit.FollowHeading(_player.Heading, delta);   // Runde 44: Rückansicht hinter dem Bug (auch rückwärts)
		// Runde 13 (Handy-Test): Gefechtskamera — mit Gefechtsziel bzw. angekündigtem Überfall rahmt sie Schiff und Ziel (Core: CombatFraming), weich, bis 140
		var want = _orbit.Offset();
		var focus = Vector3.Zero;
		float sz = ShipSizeZoom();
		if (!_manualZoomInFight && sz > 1f) want = new CamVec3(want.X * sz, want.Y * sz, want.Z * sz);   // Runde 15: Standardabstand nach Schiffsgröße
		var framingTarget = FramingTargetR();
		_camReason = _docked != null ? "angedockt" : framingTarget is null ? (_fight.Target.Value == 0 ? "kein Ziel" : "Ziel nicht feindlich/zu weit (>400 m)") : _manualZoomInFight ? "manuell übersteuert (Zoom)" : "";
		if (framingTarget is { } ftr && !_manualZoomInFight)
		{
			_framingActive = true;
			var ft = ftr.Pos;
			var vs = GetViewport().GetVisibleRect().Size;
			// Runde 15: Mindestgröße — Obergrenze 90 (Runde 17: 130, der anfliegende Kaperer ab ~120 m im Bild) × Schiffsgröße; darüber nur das Schiff (Randpfeil zeigt das Ziel); im Kampf so nah, dass beide gut erkennbar sind
			var fr = SpaceSim.Core.Flight.CombatFraming.ComputeCombat(_player.Position.X, _player.Position.Z, ft.X, ft.Z, _orbit.YawDeg, _orbit.PitchDeg, _cam.Fov,
				vs.X / Mathf.Max(1f, vs.Y), OrbitCamera.MinDistance * sz, ftr.Radius > 0f ? OrbitCamera.MaxDistance * sz : _framingWarning ? WarningCap(sz) : 130f * sz, OrbitCamera.DefaultDistance * sz, FramingField(), ftr.Radius,
				fillShare: _framingWarning ? 1f : SpaceSim.Core.Flight.CombatFraming.FillShare);   // Station/Tor: bis zum Höchstabstand; Runde 17: in der Warnphase weiter heraus (Anflug ab ~100–130 m im Bild), nur „im Feld“ verlangt
			_lastFrame = new SpaceSim.Core.Flight.CombatFraming.Frame(fr.FocusX, fr.FocusZ, fr.Distance);
			_framingBoth = fr.Both;
			_camField = FramingField();
			_camReason = fr.Both ? "Schiff+Ziel" : "nur Schiff (Ziel passt nicht)";
			float k = fr.Distance / Mathf.Max(1f, _orbit.Distance * sz);
			want = new CamVec3(want.X * k, want.Y * k, want.Z * k);
			focus = new Vector3(fr.FocusX - _player.Position.X, 0f, fr.FocusZ - _player.Position.Z);
		}
		else if (FramingTarget() is null) { _framingActive = false; _manualZoomInFight = false; }
		// Runde 33b: ist die Bildmitte von HUD-Tasten belegt (Kompakt-HUD bei UI-Faktor > 1), legt der Blickpunkt das Schiff in die Mitte des freien Felds
		if (!_framingActive && _docked == null && FramingField() is var ff && !(ff.X0 < -0.08f && ff.X1 > 0.08f && ff.Y0 < -0.08f && ff.Y1 > 0.08f))
		{
			var vsz = GetViewport().GetVisibleRect().Size;
			float dist = Mathf.Sqrt(want.X * want.X + want.Y * want.Y + want.Z * want.Z);
			var cf = SpaceSim.Core.Flight.CombatFraming.CenterShip(_player.Position.X, _player.Position.Z, _orbit.YawDeg, _orbit.PitchDeg, _cam.Fov, vsz.X / Mathf.Max(1f, vsz.Y), dist, ff);
			focus = new Vector3(cf.FocusX - _player.Position.X, 0f, cf.FocusZ - _player.Position.Z);
		}
		// Runde 44: Rückansicht — Vorausblick vor dem Bug statt der Mitte des freien Felds (Schutz ShipInFreeField bleibt)
		if (!_framingActive && _docked == null && _orbit.Mode == CameraMode.Chase)
		{
			var la = _orbit.LookAhead(_player.Heading, _manualZoomInFight ? 1f : sz);
			focus = new Vector3(la.X, 0f, la.Z);
		}
		if (!_framingActive && NearLargeStationZoom() is var kz and > 1f) want = new CamVec3(want.X * kz, want.Y * kz, want.Z * kz);   // Runde 14: große Stationen
		// Runde 18 (Handy-Test: Bohrerdrehung kaum zu sehen): verankert rückt die Kamera näher an Bug, Trossen und Werkzeug (manueller Zoom bleibt die Grundlage)
		// Runde 43b: der Anker-Zoom gleitet weich hinein und wieder heraus (nur der Abstand ändert sich, Yaw/Neigung bleiben)
		float zoomWant = !_framingActive && _docked == null && PlayerData.AnchorRock != 0 ? AnchorZoom : 1f;
		_anchorZoomNow = _docked != null ? 1f : Mathf.MoveToward(_anchorZoomNow, zoomWant, delta * (1f - AnchorZoom) / AnchorZoomSeconds);
		if (!_framingActive && _anchorZoomNow < 1f) want = new CamVec3(want.X * _anchorZoomNow, want.Y * _anchorZoomNow, want.Z * _anchorZoomNow);
		_camOffset = OrbitCamera.Follow(_camOffset, want, delta, _framingActive ? 3f : 4f);
		// Runde 15: Blickpunkt zügig zurück (vorher hing das Schiff nach dem Kampf sekundenlang am oberen Rand)
		_camFocus = _camFocus.Lerp(focus, 1f - Mathf.Exp(-(_framingActive ? 3f : 5f) * delta));
		var co = _camOffset;
		_cam.Position = _player.Position + _camFocus + new Vector3(co.X, co.Y, co.Z);
		_cam.LookAt(_player.Position + _camFocus);
		FollowBackdrop();   // Runde 44: Sterne kacheln mit, Planet mit Parallaxe
		// Runde 15: das eigene Schiff verlässt nie das freie Feld — liegt es (im Übergang) außerhalb, rückt der Blickpunkt sofort zum Schiff
		// Runde 33b: nur wenn die Bildmitte selbst frei ist — im Kompakt-HUD (Mitte unter Kampftasten) zog das Zurückziehen das Schiff genau unter die Tasten
		var gf = FramingField();
		bool centerFree = gf.X0 < -0.02f && gf.X1 > 0.02f && gf.Y0 < -0.02f && gf.Y1 > 0.02f;
		for (int guard = 0; guard < 6 && centerFree && !ShipInFreeField(); guard++)
		{
			_camFocus *= 0.6f;
			_cam.Position = _player.Position + _camFocus + new Vector3(co.X, co.Y, co.Z);
			_cam.LookAt(_player.Position + _camFocus);
		}

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
		float sliderBefore = flight.SliderValue;
		// Zweiter Handytest A15: am Handy ohne Tastatur zählen Tastatur-Steuertasten nicht (Verdacht: Gerät/System meldet Tastenereignisse — das Protokoll
		// „[input] taste …“ zeigt sie; der Regler stieg dann ohne Berührung in Tastenschritten auf 6 % bzw. 56 % und brach den Autopiloten ab)
		bool keys = KeySteering;
		bool K(Key k) => keys && Input.IsKeyPressed(k);
		float turnKeys = (K(Key.D) || K(Key.Right) ? 1 : 0) - (K(Key.A) || K(Key.Left) ? 1 : 0);
		bool keyThrottle = delta > 0f && (K(Key.W) || K(Key.Up) || K(Key.X));
		if (K(Key.W) || K(Key.Up)) flight.SetSlider(flight.SliderValue + delta * 0.8f);   // W bzw. Pfeil hoch erhöht den Slider
		if (K(Key.X)) flight.SetSlider(0f);                                                // X = STOPP
		bool keyReverse = K(Key.S) || K(Key.Down);
		bool reverse = _testReverse || flight.ReverseHeld || keyReverse;                    // eigene Rückwärts-Haltetaste
		float turn = turnKeys + (flight.TurnRightHeld ? 1f : 0f) - (flight.TurnLeftHeld ? 1f : 0f);
		if (!keys && Ui.InputLog.Enabled && (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.D)
			|| Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) && !_keysIgnoredLogged)
		{ _keysIgnoredLogged = true; Ui.InputLog.Log("steuerung: Tastatur-Steuertaste gedrückt, ignoriert (mobil ohne Hardwaretastatur)"); }
		else if (!Input.IsKeyPressed(Key.W) && !Input.IsKeyPressed(Key.Up)) _keysIgnoredLogged = false;
		// Geräte-Eingabe (Handytest A15: Autopilot fiel nach 2–3 s mit „manuelle Steuerung“ aus, Regler auf 6 % ohne Berührung): Autopilot und
		// Auto-Mining brechen nur auf bewusste Bedienung ab — ein Finger, der auf dem Regler aufgesetzt und ihn bewegt hat, die Reglertasten, Wenden
		// oder RÜCK. Ein Reglerwert ≠ 0 ohne solche Bedienung wird während der Automatik verworfen (der Regler zeigt 0, die Automatik fliegt weiter).
		bool sliderTouched = flight.TakeSliderTouched();
		bool automation = _autopilot || _miner.Enabled;
		if (automation && !sliderTouched && !keyThrottle && flight.SliderValue != 0f) flight.SetSlider(0f);
		float slider = flight.SliderValue;

		if (turn != 0f || reverse || sliderTouched || keyThrottle || Mathf.Abs(slider) > 0.001f)   // manuell: Slider, Rückwärts und/oder Wenden
		{
			if ((_autopilot || _miner.Enabled) && Ui.InputLog.Enabled)
			{
				var why = new List<string>();
				if (flight.TurnLeftHeld) why.Add($"◀ gehalten ({flight.TurnLeftInfo})");
				if (flight.TurnRightHeld) why.Add($"▶ gehalten ({flight.TurnRightInfo})");
				if (turnKeys != 0f) why.Add("Wendetaste Tastatur (A/D/←/→)");
				if (flight.ReverseHeld) why.Add($"RÜCK gehalten ({flight.ReverseInfo})");
				if (keyReverse) why.Add("Rückwärtstaste Tastatur (S/↓)");
				if (_testReverse) why.Add("Selbsttest-Rückwärts");
				if (sliderTouched) why.Add($"Regler per Finger ({flight.SliderInfo})");
				if (keyThrottle) why.Add($"Reglertaste Tastatur (W/↑/X: {(Input.IsKeyPressed(Key.W) ? "W " : "")}{(Input.IsKeyPressed(Key.Up) ? "↑ " : "")}{(Input.IsKeyPressed(Key.X) ? "X" : "")})");
				if (why.Count == 0) why.Add("Reglerwert ≠ 0");
				Ui.InputLog.Log($"{(_autopilot ? "autopilot" : "auto-mining")} AUS (manuelle Steuerung): {string.Join(", ", why)} · Regler {sliderBefore:0.00} → {slider:0.00}");
			}
			if (_autopilot) _hud.Toast("Autopilot aus (manuelle Steuerung)");   // Gerätetest: nie mehr still
			CancelAutopilot("manuelle Steuerung");
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
		// Runde 15: Annäherungsautomatik beim Stations-/Torangriff (mit „Ziel folgen“): auf Reichweite der eigenen kürzesten Waffe heran und halten
		if (_aimAssist && AttackApproachThrottle() is { } at && at != 0f)
			return (new Vector2(-Mathf.Sin(_player.Heading), -Mathf.Cos(_player.Heading)), at, ClearanceScale(), 0f);
		return (Vector2.Zero, 0f, 1f, 0f);
	}

	/// <summary>Runde 16: Zielkartenzeile einer Station — „Außenhaut 87 m · eig. ab 45 m“ (kurz, passt in die Karte).</summary>
	string StationInfoLine(Universe u, Station st, Vec2 pos)
	{
		string own = SpaceSim.Core.Combat.WarInfo.OwnGunsShort(u, st, PlayerData, pos);
		return $"Außenhaut {Mathf.Max(0f, Vec2.Distance(pos, st.Position) - st.Radius):0} m" + (own.Length > 0 && u.Rules.Warfare ? $" · {own}" : $" · Mitte {Vec2.Distance(pos, st.Position):0} m");
	}

	/// <summary>Runde 15: Schub der Annäherungsautomatik (Core: AttackApproach) — null ohne Stations-/Torangriff oder ohne Waffe.</summary>
	float? AttackApproachThrottle()
	{
		var u = _session.U;
		(Vec2 Pos, float Radius)? t = _fight.AtStation && u.Stations.TryGetValue(_fight.StationTarget, out var st) && st.Sector == u.PlayerSector ? (st.Position, st.Radius)
			: _fight.AtGate && u.Gates.TryGetValue(_fight.GateTarget, out var g) && g.Sector == u.PlayerSector ? (g.Position, SpaceSim.Core.Combat.GateCombat.BodyRadius) : null;
		if (t is not { } tt) return null;
		float shortest = float.MaxValue;
		var me = PlayerData;
		for (int i = 0; i < me.Fit.Count; i++)
			if (me.Fit[i] is { } k && u.Modules.TryGetValue(k, out var m) && m.IsWeapon && !SpaceSim.Core.Combat.Missiles.IsMissile(m) && me.ConditionOf(i) >= u.Rules.ModuleFailCondition)
				shortest = Mathf.Min(shortest, m.Stat("range"));
		if (shortest == float.MaxValue) return null;
		float d = Vec2.Distance(new Vec2(_player.Position.X, _player.Position.Z), tt.Pos);
		return AttackApproach.Throttle(d, tt.Radius, shortest);
	}
	internal float? TestAttackApproach => AttackApproachThrottle();

	static (Vector2, float, float, float) WithTurn((Vector2 dir, float throttle, float speedScale) s) => (s.dir, s.throttle, s.speedScale, 0f);

	readonly PathFollower _follower = new();

	(Vector2, float, float) Autopilot(NavTarget target)
	{
		var u = _session.U;
		// Phase 10: Route nach dem Mobilitätsprofil des Spielerschiffs (gesperrte Gates werden umflogen, sonst Meldung mit Grund)
		// Gerätetest Phase 28: Autopilot mit Gedächtnis (plant einmal, fliegt die Punkte ab, löst sich aus Sackgassen) — vorher kreiste er vor dem HQ Alpha
		var step = _follower.Next(u, u.PlayerSector, new Vec2(_player.Position.X, _player.Position.Z), target, PlayerData);
		switch (step.Status)
		{
			case NavStatus.Unreachable:
				_hud.Toast("Kein Weg zum Ziel: " + (GateAccess.BlockReason(u, PlayerData, u.PlayerSector, target.Sector) ?? "Gates gesperrt"));
				CancelAutopilot(); return (Vector2.Zero, 0f, 1f);
			case NavStatus.Arrived:
				CancelAutopilot();
				// Prüfer-Empfehlung Phase 28: Ankunft an einer Station dockt selbsttätig an — die Meldung sagt es
				if (_targetStation is { } sid && u.Stations.ContainsKey(sid)) { Dock(sid); _hud.Toast($"Angedockt per Autopilot: {u.Stations[sid].Name}"); }
				else _hud.Toast($"Ziel erreicht: {target.Label}");
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
		JumpDrive.Abort(u, PlayerData, "moved", "Torsprung");   // Sprungantrieb: ein Torsprung beendet das Laden
		u.MoveShip(PlayerData, far.Sector);
		u.PlayerSector = far.Sector;
		_music.Sting("sting_jump", Now);   // Runde 12
		_view.Rebuild(u);
		_mining.ResetNpc(); _combat.Reset(); _mining.ClearPlayer(); _chunks.Clear(); _mineTarget = null;
		_player.GiveGrace(2.5f);

		var gpos = new Vector3(far.Position.X, 0, far.Position.Y);
		var inward = (-gpos).Normalized();
		// Gerätetest Phase 27: Austritt in eine freie Richtung (zur Sektormitte, sonst gedreht) — mit Vollschub prallte das Schiff nach der Schonfrist auf Felsen
		var free = SpaceSim.Core.Flight.UndockPath.Heading(_view.Obstacles, far.Position - new Vec2(inward.X, inward.Z), far.Position);
		var dir = new Vector3(free.X, 0, free.Y);
		_player.Position = new Vector3(far.Position.X, 0, far.Position.Y) + dir * 22f;
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

	/// <summary>Runde 15 (Handy-Test): Antippen wählt das Tor nur aus (wie Stationen) — „Autopilot“ fliegt hin und springt, „Springen“ in der Nähe, „Tor angreifen“.</summary>
	void SelectGate(GateId id)
	{
		var u = _session.U;
		var gate = u.Gates[id];
		SelectTarget(Navigator.ForSector(u, u.Gates[gate.Target].Sector, u.PlayerShip), null, quiet: true);
		_targetGate = gate.Id;
		// Runde 16: eigener Hinweis für Tore (vorher „Autopilot fliegt hin, Andocken in der Nähe“). Runde 44j (Handytest): ohne den Angriffshinweis —
		// die Taste „Tor angreifen“ nennt „Verbrechen“ selbst; so bleibt der Hinweis höchstens zweizeilig (Tornamen sind schon kurz: „Tor Alpha–Beta“).
		_hud.Toast($"Ziel: {SpaceSim.Core.Combat.GateCombat.NameOf(u, gate)} · „Autopilot“ fliegt hin und springt", 3.5f);
		UpdateHud();
	}
	internal void TestSelectGate(GateId id) => SelectGate(id);
	internal bool TestAutopilot => _autopilot;
	internal void TestSelectStation(StationId id) => SelectTarget(Navigator.ForStation(_session.U, id, _session.U.PlayerShip), id);

	/// <summary>Runde 16: zuletzt gemeldeter Munitionsmangel (Hinweis nur beim Wechsel).</summary>
	string _lastDepleted = "";

	/// <summary>Gerätetest Phase 28: Ziel wählen ohne Autopilot (Tipp auf eine Station).</summary>
	void SelectTarget(NavTarget target, StationId? station, bool quiet = false)
	{
		if (_targetStation != station) _fight.ArmStation(default);
		_fight.ArmGate(default); _targetGate = null;   // Phase 31
		_fight.SetTarget(default);   // Runde 5: das neue Ziel ersetzt ein gewähltes Schiff (vorher zeigte die Zielkarte noch den alten Gegner)
		_target = target; _targetStation = station;
		_sfx.Play(SfxId.Click);
		// Runde 44i (Handytest u18: dreizeiliger Hinweis über dem Spielfeld): Kurzname mit Kürzel, ohne den Zusatz zum Andocken (die Kontexttaste zeigt ihn)
		string tl = station is { } sid && _session?.U.Stations.TryGetValue(sid, out var tst) == true ? SpaceSim.Core.Model.StationNames.Short(_session.U, tst) : target.Label;
		if (!quiet) _hud.Toast($"Ziel: {tl} · „Autopilot“ fliegt hin");
		if (_session != null) UpdateHud();   // Runde 5: Zielkarte sofort, nicht erst im nächsten Bild
	}

	void SetTarget(NavTarget target, StationId? station)
	{
		if (_targetStation != station || station is null) _fight.ArmStation(default);   // Gerätetest Phase 28: neues Ziel beendet einen Stationsangriff in der Zielkarte
		_fight.ArmGate(default); _targetGate = null;   // Phase 31
		_fight.SetTarget(default);   // Runde 5: auch ein gewähltes Schiff
		_target = target; _targetStation = station;
		_follower.Reset();   // Prüfer-Empfehlung Phase 28: neuer Plan für das neue Ziel
		_autopilot = true; _hud.Flight.SetAutopilot(true);
		Ui.InputLog.Log($"autopilot AN: {target.Label} (Regler {_hud.Flight.SliderValue:0.00} → 0, Aufrufer {Ui.InputLog.Caller()})");
		_hud.Flight.SetSlider(0f);   // Gerätetest: der Autopilot übernimmt — ein Rest am Regler (1–3 %) hätte ihn sofort wieder abgeschaltet
		_sfx.Play(SfxId.Click);
		_hud.Toast($"Autopilot: {target.Label}");
	}

	void ToggleAutopilot()
	{
		_sfx.Play(SfxId.Click);
		if (_target == null) { _hud.Flight.SetAutopilot(false); _hud.Toast("Kein Ziel – Station oder Gate antippen, oder Karte öffnen"); Ui.InputLog.Log("autopilot: Taste ohne Ziel"); return; }
		_autopilot = _hud.Flight.AutopilotOn;
		Ui.InputLog.Log($"autopilot {(_autopilot ? "AN" : "AUS")} per Taste: {_target.Value.Label} (Regler {_hud.Flight.SliderValue:0.00})");
		if (_autopilot) _follower.Reset();   // Prüfer-Empfehlung Phase 28: Wiedereinschalten plant von der jetzigen Position
		if (_autopilot) { _hud.Flight.SetSlider(0f); _hud.Toast($"Autopilot an: {_target.Value.Label}"); }   // Gerätetest: übernimmt den Regler
		else _hud.Toast("Autopilot aus");
	}

	void CancelAutopilot(string why = "", [System.Runtime.CompilerServices.CallerMemberName] string caller = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
	{
		if (!_autopilot) return;
		if (why != "manuelle Steuerung") Ui.InputLog.Log($"autopilot AUS{(why.Length > 0 ? ": " + why : "")} ({caller}:{line})");
		_autopilot = false; _hud.Flight.SetAutopilot(false);
	}

	/// <summary>
	/// Zweiter Handytest A15: Tastatur-Steuertasten (A/D/W/S/X/Pfeile) nur am Desktop oder mit angeschlossener Hardwaretastatur — am Handy ohne Tastatur
	/// lösen gemeldete Tastenereignisse keine manuelle Steuerung aus (Selbsttest: <see cref="TestKeySteering"/>).
	/// </summary>
	bool KeySteering => TestKeySteering ?? (!OS.HasFeature("mobile") || DisplayServer.HasHardwareKeyboard());
	internal bool? TestKeySteering;
	bool _keysIgnoredLogged;

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
	(ContextKind Kind, int Id) Context() => ContextChoice.Pick(NearestStation(out _), NearestGate(60f), _targetStation, _targetGate);

	void ContextAction()
	{
		var ctx = Context();
		if (ctx.Kind == ContextKind.Dock) { _sfx.Play(SfxId.Click); Dock(new StationId(ctx.Id)); return; }
		if (ctx.Kind == ContextKind.Jump) { _sfx.Play(SfxId.Click); Jump(new GateId(ctx.Id)); return; }
		_hud.Toast("Nichts in Reichweite – näher an Station oder Gate fliegen");
	}

	/// <summary>Nächste andockbare Station in Reichweite; Phase 30: Abstand zur Außenhaut (40, bei einer mittleren Station wie bisher 55 ab Mitte), Kleinstationen nie.
	/// <paramref name="distance"/> = Abstand zur Mitte.</summary>
	StationId? NearestStation(out float distance)
	{
		StationId? best = null; distance = 55f;
		float bestEdge = 40f;
		var u = _session.U;
		foreach (var sid in u.Sectors[u.PlayerSector].Stations)
		{
			var st = u.Stations[sid];
			if (!st.Dockable) continue;
			float d = new Vector2(_player.Position.X - st.Position.X, _player.Position.Z - st.Position.Y).Length();
			if (d - st.Radius < bestEdge) { bestEdge = d - st.Radius; distance = d; best = sid; }
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
		_hud.DockCtx.Prices = _session.Prices;
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
			DockAreas.Taxi => _hud.DockTaxi,
			DockAreas.Repair => _hud.DockRepair,
			DockAreas.Outfitter => _hud.DockOutfitter,
			DockAreas.Shipyard => _hud.DockShipyard,
			DockAreas.Storage => _hud.DockStorage,
			DockAreas.BlackMarket => _hud.DockBlackMarket,
			DockAreas.Authority => _hud.DockAuthority,
			DockAreas.Missions => _hud.DockMissions,
			DockAreas.Research => _hud.DockResearch,
			_ => _hud.DockMenu,
		};
		if (screen is DockAreaScreen area) area.AreaId = areaId;
		_hud.Screens.Show(screen);
		_sfx.Play(SfxId.Click);
	}

	/// <summary>Hangar „Übernehmen“ (Phase 5c): wechselt an der angedockten Station in ein dort geparktes eigenes Schiff (Core: <see cref="PlayerFleet.SwitchTo"/>).</summary>
	void SwitchShip(ShipId id)
	{
		if (_docked is not { } sid || !_session.U.Stations.TryGetValue(sid, out _)) return;
		SyncPlayerData();
		var r = _hud.DockHangar.Execute(SpaceSim.Core.Services.HangarService.ServiceKey, $"{SpaceSim.Core.Services.HangarService.Switch}:{id.Value}");   // Phase 12: über den Dienst „hangar“
		_hud.Toast(r.Message);
		_hud.Screens.Current?.Refresh();
	}

	/// <summary>
	/// Das aktive Schiff hat gewechselt (Kauf an der Werft oder „Übernehmen“ im Hangar): Dock-Kontext auf das neue Schiff, Abbau-
	/// Mikrosimulation und Automatik zurücksetzen. Die Ansicht liest das Schiff ohnehin über <see cref="GameSession.Player"/>; das alte
	/// Schiff liegt jetzt geparkt an der Station und erscheint wie andere Schiffe in der Sektoransicht.
	/// </summary>
	void OnActiveShipChanged(ActiveShipChanged e)
	{
		// Phase 12: liegt das neue Schiff nicht an der Station, an der die Ansicht angedockt ist (Fernwechsel, Taxi, Leihschiff am Ziel), setzt die
		// Ansicht im nächsten Frame um (EnterActiveShip); an derselben Station wechselt nur der Dock-Kontext
		if (_docked is not { } here || e.Station != here || PlayerData.Sector != _session.U.PlayerSector || PlayerData.DockedAt != here) _reenter = true;
		_hud.DockCtx.Ship = PlayerData;
		_chunks.Clear(); _mineTarget = null; _mining.ClearPlayer();
		SetAutoMining(false, announce: false);
		_lowestHull = PlayerData.Hull;
	}

	/// <summary>Andocken: Das Schiff fliegt in die Station hinein und verschwindet (unsichtbar, keine Kollision); ein Brand erlischt (Phase 26: keine Gratisreparatur mehr).</summary>
	void Dock(StationId id)
	{
		var u = _session.U;
		var st = u.Stations[id];
		// Phase 30: Größenregel — Kleinstationen gar nicht, kleine Stationen nur bis M-Schiffe (dieselbe Regel wie die KI-Zielwahl)
		if (SpaceSim.Core.Simulation.Docking.Issue(u, st, PlayerData) is { } why) { _hud.Toast(why, 3f); CancelAutopilot(); return; }
		// Gerätetest Phase 28: Handels-Editor und alte Meldung („Kauf erfolgreich“) gehören zur vorigen Station
		if (_hud.DockCtx.Station?.Id != id) _hud.DockTrade.ResetSession();
		_docked = id;
		PlayerData.DockedAt = id;   // Phase 12: Liegeplatz des aktiven Schiffs (Hangar, Taxi, Liegegebühr)
		u.Events.Publish(new ShipDocked(PlayerData.Id, id));   // Runde 42b: Andocken über die Ansicht meldet der Bus (z. B. Transport-Ablieferung)
		_transition = true;
		_player.Velocity = Vector3.Zero;
		_miningStep = null;

		var center = new Vector3(st.Position.X, 0, st.Position.Y);
		_playerTween?.Kill();
		var tw = _playerTween = CreateTween();
		tw.TweenProperty(_player, "position", center, 0.7);
		tw.Parallel().TweenProperty(_player, "scale", Vector3.One * 0.1f, 0.7);
		tw.TweenCallback(Callable.From(() => { _player.Visible = false; _transition = false; }));

		// Phase 26: keine Gratis-Vollreparatur beim Andocken mehr (Dienst „Reparatur“); Andocken löscht aber jeden Brand
		bool repaired = !u.Rules.DamageActive && PlayerData.Hull < PlayerData.MaxHull - 0.5f;
		if (repaired) PlayerData.Hull = PlayerData.MaxHull;
		bool burned = PlayerData.Systems.Burning.Count > 0;
		SpaceSim.Core.Combat.ModuleDamage.ExtinguishAll(u, PlayerData, "docked");
		_sfx.Play(SfxId.Dock);
		ShowDockMenu();
		// Runde 42c: im Dock automatisch ×1 — sonst verfallen Auftragsfristen unbemerkt (×10). Runde 42d (Handytest):
		// die Meldung „Zeitraffer aus (angedockt)“ wird mit den übrigen Andock-Meldungen gebündelt, damit kein
		// anderer Toast sie verdrängt.
		var dockMsgs = new List<string>();
		if (ResetFastOnDock()) dockMsgs.Add("Zeitraffer aus (angedockt)");
		if (repaired) dockMsgs.Add("Hülle repariert");
		else if (burned) dockMsgs.Add("Brand beim Andocken gelöscht");
		else if (SpaceSim.Core.Services.RepairService.NeedsRepair(PlayerData)) dockMsgs.Add("Schäden: Reparatur im Dienst „Reparatur“");
		if (dockMsgs.Count > 0) _hud.Toast(string.Join(" · ", dockMsgs), 3.5f);
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

	/// <summary>
	/// Runde 42c (Handytest): Zeitraffer beim Andocken automatisch auf ×1 schalten — im Dock läuft die Simulation sonst
	/// mit ×10 weiter und Auftragsfristen verfallen unbemerkt. Gilt für jeden Andock-Pfad, der die Ansicht ins Dock bringt.
	/// Liefert true, wenn zurückgesetzt wurde; die Meldung „Zeitraffer aus (angedockt)“ zeigt der Aufrufer (Runde 42d:
	/// dort mit den übrigen Andock-Meldungen gebündelt, damit kein anderer Toast sie verdrängt).
	/// </summary>
	bool ResetFastOnDock()
	{
		if (!_fast) return false;
		_fast = false;
		_hud.Command.SetFast(false);
		return true;
	}

	/// <summary>Abdocken: Das Schiff taucht aus der Station auf und fliegt zum Parkpunkt; danach kurze Schonfrist ohne Schaden. Funktioniert aus jedem Dock-Bereich.</summary>
	string? _lostStationMessage;
	/// <summary>Runde 38a, Selbsttest: letzte Meldung zum Ausgang eines Gegners.</summary>
	internal string LastEnemyExit { get; private set; } = "";

	/// <summary>Runde 37c (M1): angedockt, aber die Station ist weg — Dock-Zustand lösen, alle Bildschirme zu, Schiff sichtbar an seiner Lage, Meldung.</summary>
	void ForcedUndock(string message)
	{
		_docked = null;
		_lostStationMessage = null;
		_transition = false;
		_playerTween?.Kill();
		_hud.Map.Visible = false;
		_hud.Screens.Hide();
		_hud.Flight.Visible = true;
		var p = PlayerData.Position;
		_player.Position = new Vector3(p.X, 0, p.Y);
		_player.Scale = Vector3.One;
		_player.Visible = true;
		_player.GiveGrace(2.5f);
		_hud.Alert(message, 8f);
		LastForcedUndock = message;
	}
	/// <summary>Selbsttest: Meldung des letzten erzwungenen Abdockens.</summary>
	internal string LastForcedUndock { get; private set; } = "";

	void Undock()
	{
		if (_docked is not { } id || !_session.U.Stations.TryGetValue(id, out var st)) return;
		_sfx.Play(SfxId.Click);
		_docked = null;
		PlayerFleet.Undock(_session.U, PlayerData);
		_transition = true;
		_hud.Screens.Hide();          // schließt Andock-Menü und jeden offenen Bereich
		_hud.Flight.Visible = true;

		var spot = st.DockPoint(_session.U.PlayerShip);
		_player.Position = new Vector3(st.Position.X, 0, st.Position.Y);
		_player.Scale = Vector3.One * 0.1f;
		_player.Visible = true;
		// Gerätetest Phase 27: Abflug in eine freie Richtung (radial, sonst gedreht) — vorher prallte Vollschub nach der Schonfrist auf Felsen/Nachbarstation
		var dir = SpaceSim.Core.Flight.UndockPath.Heading(ObstaclesWithGates(st.Sector), st.Position, spot);
		var outward = new Vector3(dir.X, 0, dir.Y);
		_player.Heading = Mathf.Atan2(-outward.X, -outward.Z);

		_playerTween?.Kill();
		var tw = _playerTween = CreateTween();
		tw.TweenProperty(_player, "position", new Vector3(spot.X, 0, spot.Y), 0.7);
		tw.Parallel().TweenProperty(_player, "scale", Vector3.One, 0.7);
		tw.TweenCallback(Callable.From(() => { _transition = false; _player.Velocity = outward * 8f; _player.GiveGrace(2.5f); }));
	}

	// ───────── Zeiger in der Welt und Kamera-Gesten ─────────

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_catchUp is not null) return;   // Phase 28: während des Nachholens keine Eingaben
		// Runde 37b: Escape wie die Zurück-Taste — Runde 37c: nur am Desktop (auf dem Gerät kommt die Zurück-Taste als Benachrichtigung); eine Taste
		// „Zurück“ (Key.Back), falls ein Gerät sie zusätzlich als Tastenereignis schickt, läuft durch dieselbe Entprellung
		if (_hud != null && @event is InputEventKey { Pressed: true, Echo: false } bk && (bk.Keycode == Key.Back || (bk.Keycode == Key.Escape && !OS.HasFeature("mobile"))))
		{ BackRequested(bk.Keycode == Key.Back ? "Taste Zurück" : "Escape"); GetViewport().SetInputAsHandled(); return; }
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
				if (!mb.Pressed) PersistCamera();   // Runde 44: Gestenende
				return;
			}
			if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
			{
				_orbit.Zoom(wheel.ButtonIndex == MouseButton.WheelUp ? WheelZoomIn : WheelZoomOut); ManualZoom(); PersistCamera();
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
				if (_pointerFinger == t.Index && t.Canceled) { _pointerFinger = -1; _pointerDragging = false; }   // Geräte-Eingabe: abgebrochene Geste wählt nichts
				else if (_pointerFinger == t.Index) HandlePointer(t.Index, t.Position, pressed: false, motion: false);
				if (_touch.Count < 2) _pinchLast = 0f;
				if (_touch.Count == 0) PersistCamera();   // Runde 44: Gestenende
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
				if (_pinchLast > 0f) { _orbit.Zoom(_pinchLast / Mathf.Max(pinch, 1f)); ManualZoom(); }   // Finger auseinander = hineinzoomen
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
			if (_pointerDragging) PersistCamera();   // Runde 44: Gestenende
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

	/// <summary>
	/// Bildposition eines Weltpunkts für die Randpfeile: hinter der Kamera nach unten geschoben (Pfeil am unteren Rand). Null, solange die Kamera nicht im
	/// Baum ist oder der Punkt in der Kameraebene liegt (Tiefe 0) — dort hat die Projektion keinen Wert (Godot meldet „p.d == 0“; Gerätetest: in den
	/// ersten Bildern nach dem Start steht die Kamera noch im Ursprung, eine Station mit z = 0 lag genau in ihrer Ebene).
	/// </summary>
	Vector2? EdgeScreen(Vector3 w)
	{
		if (_cam is null || !IsInstanceValid(_cam) || !_cam.IsInsideTree()) return null;
		float depth = (_cam.GlobalTransform.AffineInverse() * w).Z;
		if (Mathf.Abs(depth) < 1e-3f) return null;
		var p = _cam.UnprojectPosition(w);
		if (_cam.IsPositionBehind(w)) p = new Vector2(p.X, GetViewport().GetVisibleRect().Size.Y * 2f);   // hinter der Kamera: nach unten
		return p;
	}

	/// <summary>Runde 16: gewähltes Ziel (Schiff, angegriffene Station/Tor, angekündigter Kaperer) als Bildposition für den Zielpfeil am Rand des freien Felds.</summary>
	(Vector2 Screen, string Label, bool Hostile)? TargetEdge()
	{
		if (_session == null || _docked != null || FramingTargetR() is not { } t) return null;
		var u = _session.U;
		var w = t.Pos;
		if (EdgeScreen(w) is not { } p) return null;
		string label = _fight.AtStation && u.Stations.TryGetValue(_fight.StationTarget, out var st) ? st.Name
			: _fight.AtGate && u.Gates.TryGetValue(_fight.GateTarget, out var g) ? SpaceSim.Core.Combat.GateCombat.NameOf(u, g)
			: _fight.Target.Value != 0 && u.Ships.TryGetValue(_fight.Target, out var s) ? $"{SpaceSim.Core.Combat.PlayerCombat.Describe(u, s)} · {Vec2.Distance(new Vec2(_player.Position.X, _player.Position.Z), new Vec2(w.X, w.Z)):0} m"
			: u.PendingEncounters.FirstOrDefault(x => x.Target == u.PlayerShip && x.Kind == EncounterKind.Raid) is { } pe && u.Ships.TryGetValue(pe.Source, out var ps)
				? $"{SpaceSim.Core.Combat.PlayerCombat.Describe(u, ps)} · {Vec2.Distance(new Vec2(_player.Position.X, _player.Position.Z), new Vec2(w.X, w.Z)):0} m"
			: "Ziel";
		return (p, label, true);
	}

	/// <summary>Gerätetest Phase 28: Stationen des Spielersektors als Bildschirmpositionen (für die Randmarker außerhalb des Bildes); im Dock/Vollbild leer.</summary>
	IReadOnlyList<(Vector2 Screen, string Name, int Id)> StationScreenPositions()
	{
		var list = new List<(Vector2, string, int)>();
		if (_session == null || _docked != null) return list;
		var u = _session.U;
		var me = new Vec2(_player.Position.X, _player.Position.Z);
		// Runde 4: Reihenfolge = Vorrang beim Zeichnen (nahe beieinander liegende Marker: der erste gewinnt) — Stationen mit Verteidigung (HQ, Polizei, Werft)
		// zuerst, dann nach Abstand; höchstens 10
		var order = u.Sectors[u.PlayerSector].Stations.Where(id => u.Stations.ContainsKey(id) && !(_targetStation is { } ts && ts == id))   // das gewählte Ziel hat seinen eigenen Pfeil
			.Select(id => u.Stations[id])
			.OrderBy(st => SpaceSim.Core.Combat.StationCombat.DefenseOf(u, st) is null ? 1 : 0).ThenBy(st => Vec2.Distance(st.Position, me)).ThenBy(st => st.Id.Value).Take(10);
		foreach (var st in order)
		{
			var w = new Vector3(st.Position.X, 0, st.Position.Y);
			if (EdgeScreen(w) is not { } p) continue;   // Kamera noch nicht bereit / Punkt in der Kameraebene
			// Runde 44: Entfernung am Pfeil (in vergrößerten Sektoren liegt fast alles außerhalb des Bildes)
			list.Add((p, SpaceSim.Core.Model.StationNames.Short(u, st) + " · " + SpaceSim.Core.Flight.DistanceText.Format(MathF.Max(0f, Vec2.Distance(st.Position, me) - st.Radius)), st.Id.Value));   // Phase 29: Kürzel
		}
		return list;
	}

	/// <summary>Runde 41: Bildschirmpositionen der Missionsmarker im aktuellen Sektor (Projektion wie die Stationen; auch außerhalb des Bildes → Randpfeil).</summary>
	IReadOnlyList<(Vector2 Screen, string Label, int MissionId)> MissionScreenPositions()
	{
		var list = new List<(Vector2, string, int)>();
		if (_session == null || _docked != null) return list;
		var u = _session.U;
		foreach (var mk in SpaceSim.Core.Missions.MissionMarkers.For(u))
		{
			if (mk.Sector != u.PlayerSector) continue;   // anderer Sektor: Symbol auf der Karte, kein Randpfeil hier
			var w = new Vector3(mk.Position.X, 0, mk.Position.Y);
			if (EdgeScreen(w) is not { } p) continue;   // Kamera noch nicht bereit / Punkt in der Kameraebene
			list.Add((p, mk.Label, mk.Mission));
		}
		return list;
	}

	/// <summary>Runde 4: Station, deren Körper (projizierter Radius, mindestens 28 Gerätepixel) den Tipp enthält — die nächste gewinnt.</summary>
	StationId? PickStationScreen(Vector2 screen) => PickStationScreen(screen, out _);

	/// <summary>Runde 19: Tor, dessen projizierte Öffnung (Tormodell-Radius, mindestens 28 Gerätepixel) den Tipp enthält — das nächste gewinnt.</summary>
	GateId? PickGateScreen(Vector2 screen, out float centrePx)
	{
		centrePx = float.MaxValue;
		var u = _session.U;
		float minPx = Mathf.Max(20f, 28f * GetViewport().GetVisibleRect().Size.X / Mathf.Max(1f, DisplayServer.WindowGetSize().X));
		GateId? best = null; float bestD = float.MaxValue;
		foreach (var id in u.Sectors[u.PlayerSector].Gates)
		{
			if (!u.Gates.TryGetValue(id, out var g)) continue;
			var c = new Vector3(g.Position.X, 0, g.Position.Y);
			if (_cam.IsPositionBehind(c)) continue;
			var sc = _cam.UnprojectPosition(c);
			float r = sc.DistanceTo(_cam.UnprojectPosition(c + new Vector3(GateModels.Scale, 0, 0)));
			float d = sc.DistanceTo(screen);
			if (SpaceSim.Core.Flight.TapPick.InBody(d, r, minPx) && d < bestD) { bestD = d; best = id; }
		}
		centrePx = bestD;
		return best;
	}
	internal GateId? TestPickGateAt(Vector2 screen) { TryPickTarget(screen); return _targetGate; }

	StationId? PickStationScreen(Vector2 screen, out float centrePx)
	{
		centrePx = float.MaxValue;
		var u = _session.U;
		float minPx = Mathf.Max(20f, 28f * GetViewport().GetVisibleRect().Size.X / Mathf.Max(1f, DisplayServer.WindowGetSize().X));
		StationId? best = null; float bestD = float.MaxValue;
		foreach (var id in u.Sectors[u.PlayerSector].Stations)
		{
			if (!u.Stations.TryGetValue(id, out var st)) continue;
			var c = new Vector3(st.Position.X, 0, st.Position.Y);
			if (_cam.IsPositionBehind(c)) continue;
			var sc = _cam.UnprojectPosition(c);
			float r = Mathf.Max(minPx, sc.DistanceTo(_cam.UnprojectPosition(c + new Vector3(st.Radius * 0.8f, 0, 0))));
			float d = sc.DistanceTo(screen);
			if (d <= r && d < bestD) { bestD = d; best = id; }
		}
		centrePx = bestD;
		return best;
	}

	/// <summary>
	/// Schiff als Ziel wählen (Tipp oder Selbsttest); false = kein gültiges Ziel. Runde 20 (Handy-Test s_a: „Tor angreifen“ blieb nach dem Wechsel Tor → Schiff):
	/// ein Schiff als Ziel beendet Tor-/Stationswahl und -angriff; ein laufender Autopilot behält sein Navigationsziel.
	/// </summary>
	bool SelectShip(ShipId picked)
	{
		if (!_session.U.Ships.TryGetValue(picked, out var ps) || !TakeShipTarget(picked)) return false;
		_sfx.Play(SfxId.Click);
		_hud.Toast($"Ziel: {SpaceSim.Core.Combat.PlayerCombat.Describe(_session.U, ps)}");
		UpdateHud();   // Zielkarte sofort
		return true;
	}
	internal bool TestSelectShip(ShipId id) => SelectShip(id);
	internal void TestNextEnemy() => SelectNextEnemy();
	internal void TestSwitchRemote(ShipId id) => SwitchRemote(id);
	/// <summary>
	/// Runde 21 (Prüferhinweis): der eine Weg, ein Schiff zum Ziel zu machen (Tipp, „Nächster Gegner“, Überfallwarnung/-beginn): gilt das Ziel, endet die
	/// Tor-/Stationswahl samt Angriffsarmierung; ein laufender Autopilot behält sein Navigationsziel.
	/// </summary>
	bool TakeShipTarget(ShipId id)
	{
		_fight.SetTarget(id);
		_fight.Validate(_session.U);
		if (_fight.Target != id) return false;
		ClearPlaceTarget();
		return true;
	}
	void ClearPlaceTarget()
	{
		_targetGate = null; _fight.ArmGate(default); _fight.ArmStation(default);
		if (!_autopilot) { _target = null; _targetStation = null; }
	}

	void TryPickTarget(Vector2 screen)
	{
		// Gerätetest Phase 28: Randmarker einer Station außerhalb des Bildes antippen wählt sie
		foreach (var (pos, sid) in _hud.EdgeMarkers)
			if (pos.DistanceTo(screen) <= 36f && _session.U.Stations.ContainsKey(new StationId(sid)))
			{
				SelectTarget(Navigator.ForStation(_session.U, new StationId(sid), _session.U.PlayerShip), new StationId(sid));
				return;
			}
		if (GroundPoint(screen) is not { } ground) return;
		// Phase 26: Wracks und Frachtbehälter antippbar (Traktor zieht Fracht, „Bergen“ birgt Module)
		if (_session.U.Rules.DamageActive && SalvageController.Pick(_session.U, new Vec2(ground.X, ground.Z), 4f) is { } wreck)
		{
			_salvage.Select(wreck.Id);
			_sfx.Play(SfxId.Click);
			_hud.Toast(SpaceSim.Core.Combat.DamageInfo.WreckLine(_session.U, wreck));
			return;
		}
		// Stationen und Schiffe im Bildschirmraum: Stationskörper ≥ 28 Gerätepixel, Schiffsmarker ≥ 56 (nächstes gewinnt). Der Stationskörper gewinnt, außer der Tipp
		// liegt näher an einem Schiffsmarker als an der Stationsmitte (Runde 4/5). Ein gewähltes Schiff ist Waffenziel (Zielkarte mit Schild und Hülle).
		float pickPx = Mathf.Max(40f, 56f * GetViewport().GetVisibleRect().Size.X / Mathf.Max(1f, DisplayServer.WindowGetSize().X));
		Vector2? ToScreen(Vec2 p) => _cam.IsPositionBehind(new Vector3(p.X, 0, p.Y)) ? null : _cam.UnprojectPosition(new Vector3(p.X, 0, p.Y));
		// Runde 19 (Handy-Test K_2: Toast „Ziel: Korsar …“, Zielkarte „Kein Ziel“): nur Schiffe, die als Ziel gültig bleiben (im Sektor nach der Core-Position, wie
		// CombatController.Validate) — ein Marker auf dem Durchflug am Tor ist kein Ziel
		var uu = _session.U;
		var markers = _view.ShipMarkers().Where(m => m.Id != uu.PlayerShip && uu.Ships.ContainsKey(m.Id)
			&& SpaceSim.Core.Combat.PlayerCombat.PositionOf(uu, m.Id) is { } cp && cp.Sector == uu.PlayerSector).ToList();
		var shipHit = CombatController.PickScreen(markers, ToScreen, screen, pickPx);
		float shipPx = shipHit is { } sh && markers.FirstOrDefault(m => m.Id == sh) is var mk && ToScreen(mk.Pos) is { } msp ? msp.DistanceTo(screen) : float.MaxValue;
		if (PickStationScreen(screen, out float stationPx) is { } bodyHit && !(shipHit is not null && SpaceSim.Core.Flight.TapPick.ShipBeatsStation(shipPx, stationPx)))
		{
			SelectTarget(Navigator.ForStation(_session.U, bodyHit, _session.U.PlayerShip), bodyHit);
			return;
		}
		// Runde 19 (K_5–K_7: Tippen aufs Tor wählte immer Händler davor/dahinter): ein Tipp in der projizierten Toröffnung wählt das Tor, außer er liegt deutlich
		// näher an einem Schiffsmarker (Core: TapPick.ShipBeatsGate)
		if (PickGateScreen(screen, out float gatePx) is { } gateHit && !(shipHit is not null && SpaceSim.Core.Flight.TapPick.ShipBeatsGate(shipPx, gatePx)))
		{
			SelectGate(gateHit);
			return;
		}
		if (shipHit is { } picked && SelectShip(picked)) return;
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
			// Gerätetest Phase 28: Tippen wählt die Station nur als Ziel (vorher Autopilot → nahe Station sofort angedockt; „Station angreifen“ kaum erreichbar);
			// hinfliegen über „Autopilot“, andocken über die Kontexttaste
			SelectTarget(Navigator.ForStation(u, sid, u.PlayerShip), sid);
		}
		else
		{
			SelectGate(new GateId(best.Id));
		}
	}

	// ───────── Rückmeldung ─────────

	/// <summary>
	/// Phase 32: Anker des Spielers in der Ansicht — eigener Schub über der Schwelle oder Wenden reißt die Verankerung (Core-Regel
	/// <c>MiningAnchor.BreaksFromSteering</c>); angedockt/im Übergang holt das Schiff die Anker ein; solange Harpunen hängen, liegt das Schiff an der
	/// Felsoberfläche (<c>MiningAnchor.HoldPosition</c>, Runde 43 mit dem Abstand der Bohrerspitze, damit das Werkzeug die Oberfläche berührt);
	/// kappt es nach Gefechtsbeginn die Trossen, steht es so lange still.
	/// </summary>
	/// <summary>Runde 18: Kamerafaktor während der Verankerung.</summary>
	internal const float AnchorZoom = 0.7f;
	/// <summary>Runde 43b: Dauer, in der der Anker-Zoom ganz hinein- bzw. herausgleitet (Sekunden).</summary>
	internal const float AnchorZoomSeconds = 0.9f;
	/// <summary>Runde 43b: aktueller Anker-Zoomfaktor (1 = frei, <see cref="AnchorZoom"/> = verankert), weich nachgeführt.</summary>
	float _anchorZoomNow = 1f;
	internal float TestAnchorZoomNow => _anchorZoomNow;
	/// <summary>Runde 43b: Abstand zur Felsoberfläche, unterhalb dessen der Target-Lock einen Fels nicht nachführt.</summary>
	const float LockRockMargin = 12f;
	/// <summary>Runde 43b: Fels, von dem sich der Spieler eben gelöst hat (0 = keiner) — bis er sich davon entfernt, hält der Target-Lock Yaw/Neigung.</summary>
	int _lockHoldRock;
	/// <summary>Runde 43b: das Lock-Ziel ist der Fels, an dem das Schiff verankert ist oder (eben gelöst) noch liegt — dann behält die Kamera Yaw/Neigung.</summary>
	bool LockHeldAtRock()
	{
		if (_fight.Target.Value != 0 || _mineTarget is not { } r) { _lockHoldRock = 0; return false; }   // Lock-Ziel ist ein Schiff, eine Station oder ein Tor: normal nachführen
		if (PlayerData.AnchorRock == r.Id) return true;
		if (_lockHoldRock != r.Id) return false;
		var p = new Vec2(_player.Position.X, _player.Position.Z);
		if (Vec2.Distance(p, r.Position) < r.Radius + LockRockMargin) return true;
		_lockHoldRock = 0;   // vom Fels weggeflogen: der Lock führt wieder nach
		return false;
	}
	bool _anchorAllowed = true;
	string _anchorBroke = "", _anchorBrokeShort = "";
	/// <summary>Runde 18: Restzeit (Spielbilder, nicht Uhrzeit) der Rissanzeige an der Werkzeugtaste.</summary>
	float _anchorBrokeLeft;

	/// <summary>Phase 32: Anker gelöst/gerissen — Trossen schnappen zurück (Funken bei einem Riss); beim Spieler kurz der Grund an der Werkzeugtaste.</summary>
	void OnAnchorReleased(AnchorReleased ar)
	{
		_anchors.OnReleased(ar);
		if (ar.Ship != _session.U.PlayerShip) return;
		_lockHoldRock = ar.Asteroid;   // Runde 43b
		_anchorBroke = SpaceSim.Core.Mining.MiningAnchor.ReasonText(ar.Reason);
		_anchorBrokeShort = SpaceSim.Core.Mining.MiningAnchor.ShortReason(ar.Reason);
		_anchorBrokeLeft = 2.5f;
		if (ar.Reason is "thrust" or "turn" or "impact" or "hit" or "split" or "combat") _hud.Toast(_anchorBroke, 2.5f);
	}
	void AnchorPin(float throttle, float turn, bool frozen)
	{
		var u = _session.U;
		var me = PlayerData;
		_player.Pin = null; _player.PinHeading = null;
		_anchorAllowed = frozen || !SpaceSim.Core.Mining.MiningAnchor.BreaksFromSteering(u.Rules, throttle, turn);
		if (me.AnchorRock != 0)
		{
			if (_docked != null || _transition) SpaceSim.Core.Mining.MiningAnchor.Release(u, me, u.PlayerSector, "left");
			else if (!frozen && SpaceSim.Core.Mining.MiningAnchor.SteeringBreak(u.Rules, throttle, turn) is { } why) SpaceSim.Core.Mining.MiningAnchor.Release(u, me, u.PlayerSector, why);   // Runde 18: „Wenden“ eigener Grund
		}
		if (me.AnchorRock != 0 && u.Sectors[u.PlayerSector].Asteroids.FirstOrDefault(a => a.Id == me.AnchorRock) is { } rock)
		{
			var pos = new Vec2(_player.Position.X, _player.Position.Z);
			// Runde 43: das Schiff liegt so am Fels, dass die Bohrerspitze des Modells die Oberfläche berührt (statt des pauschalen Kollisionskreises)
			float standoff = _player.Visual is { } pv ? pv.TipOffset : Collision.ShipRadius;
			var hold = SpaceSim.Core.Mining.MiningAnchor.HoldPosition(pos, rock, standoff);
			_player.Pin = new Vector3(hold.X, 0f, hold.Y);
			_player.PinHeading = AimAssist.Bearing(hold, rock.Position);
			return;
		}
		// Trossen kappen nach Gefechtsbeginn: still stehen
		if (u.Fights.Of(me.Id) is { } e && e.Of(me.Id) is { Active: true } p && u.Tick < p.CastOffUntil) _player.Pin = _player.Position;
	}

	/// <summary>Harter Aufprall auf Asteroid/Station (Schwelle <see cref="PlayerShip.HardImpact"/>); leichte Stöße prallen nur ab.</summary>
	void OnPlayerHit(float impact, Vector3 point)
	{
		_hitCount++;
		var uu = _session.U;
		SpaceSim.Core.Mining.MiningAnchor.Release(uu, PlayerData, uu.PlayerSector, "impact");   // Phase 32: harter Stoß reißt die Trossen
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

	/// <summary>
	/// Hülle durch Aufprall zerstört (Phase 25: echter Verlust wie im Gefecht): Core-Regel <see cref="ShipLoss.LoseActive"/> — Schiff und Fracht weg, Geld
	/// bleibt, die Person landet an der nächsten eigenen bzw. neutralen Station im eigenen oder einem Leihschiff; die Ansicht setzt um (PlayerShipLost).
	/// </summary>
	void Respawn()
	{
		SyncPlayerData();
		CancelAutopilot();
		if (_miner.Enabled) SetAutoMining(false, "Auto-Mining unterbrochen (Schiff zerstört)");
		_chunks.Clear();
		if (ShipLoss.LoseActive(_session.U, PlayerData, "collision") is null)
		{
			PlayerData.Hull = PlayerData.MaxHull;   // ohne jede Station: nie handlungsunfähig
			_hud.Toast("Schiff zerstört – notdürftig repariert", 3.5f);
		}
	}

	/// <summary>
	/// Ein Spielerschuss ist gefallen: Ton, Laserblitz, bei Treffer Funken – und Rückstoß: Das Schiff wird
	/// vom Asteroiden weggestoßen (echte Geschwindigkeitsänderung, deshalb bremst die Automatik im Haltemodus aktiv).
	/// </summary>
	/// <summary>Phase 29: Kontaktabbau hat eine Portion gelöst — Funken am Bug (bis die Schiffsmodelle drehende Werkzeuge bekommen).</summary>
	void OnContact(Asteroid rock, GoodId good, float units)
	{
		// Runde 19 (Handy-Test d_crops: Funken verdeckten die Werkzeugspitze): kleine Funken an der Felsoberfläche, abwechselnd seitlich neben dem Werkzeug
		var c = new Vector3(rock.Position.X, 0f, rock.Position.Y);
		var n = _player.Position - c; n.Y = 0f; n = n.LengthSquared() < 1e-4f ? Vector3.Back : n.Normalized();
		var side = new Vector3(-n.Z, 0f, n.X) * ((_contactSparks % 2 == 0) ? 1.8f : -1.8f);
		_fx.Burst(c + n * rock.Radius + side, new Color(1f, 0.75f, 0.35f), 4, 6f, 0.3f);
		_contactSparks++;
	}
	int _contactSparks;
	internal int TestContactSparks => _contactSparks;
	internal ContactState TestContactState => _chunks.LastContact;

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

	/// <summary>Phase 29: ein NPC-Schürfer hat laut Simulation mit seinem Kontaktwerkzeug eine Portion gelöst — Funken an der Kontaktstelle, kein Rückstoß.</summary>
	void OnContactMined(ContactMined cm)
	{
		var shipPos = _view.ShipPosition(cm.Ship);
		if (shipPos is not { } sp) return;
		var rockPos = new Vector3(cm.AsteroidPosition.X, 0, cm.AsteroidPosition.Y);
		var surface = rockPos + (sp - rockPos).Normalized() * cm.AsteroidRadius;
		_fx.Burst(surface, GoodColor(cm.Good), 3 + (int)cm.Units, 5f, 0.35f);
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
		TickCountdown();
		int commsUnread = SpaceSim.Core.Comms.Comms.Unread(_session.U);   // Runde 40: Comms-Zähler echt (Hamburger-Menü + Kommando-Knopf)
		_hud.Flight.SetCommandBadge(commsUnread);
		_hud.Command.SetCommsBadge(commsUnread);
		// Runde 22 (Handy-Test h3: „Kein Ziel“ neben dem angekündigten Kaperer): ein verschobener Überfall (kam während eines Gefechts fällig) wird ohne neue
		// Warnung neu terminiert — auch dann ist der ankündigende Kaperer Ziel, solange kein anderes gewählt ist
		if (_fight.Target.Value == 0 && !_fight.AtStation && !_fight.AtGate && _docked is null
			&& _session.U.PendingEncounters.Where(p => p.Target == _session.U.PlayerShip && p.Kind == EncounterKind.Raid && p.Source.Value != 0).OrderBy(p => p.DueTick).FirstOrDefault() is { } pr
			&& _pendingTargeted != (pr.Source, pr.DueTick))   // einmal je Ankündigung — danach darf der Spieler frei wählen (z. B. eine Station zum Andocken)
		{
			_pendingTargeted = (pr.Source, pr.DueTick);
			TakeShipTarget(pr.Source);
		}
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
			FuelActive = u.Rules.FuelActive,   // Phase 14
			// Phase 23: Schild, Leistungsbilanz, Speicherbank und Verteilung echt
			Shield = PlayerData.ShieldNow, ShieldMax = PlayerData.Stats.Shield,
			PowerOutput = PlayerData.Stats.PowerOutput, PowerDraw = PlayerData.Stats.PowerDraw,
			Charge = PlayerData.Charge, ChargeMax = PlayerData.Stats.PowerStorage, PowerMode = PlayerData.PowerMode,
			Credits = _session.U.WalletOf(PlayerData).Credits,   // Phase 5b: Spielerkasse
			CostsPerMinute = SpaceSim.Core.Simulation.Hangar.RunningCostsPerMinute(u, u.PlayerActor),   // Runde 16
			SectorName = sector.Name,
			FactionName = u.Factions.TryGetValue(sector.Owner, out var f) ? f.Name : "",
			FactionTag = u.Factions.TryGetValue(sector.Owner, out var ft) ? SpaceSim.Core.Model.ActorNames.Tag(u, ft.Id) : "",   // Runde 33: schmaler Sektorchip im Kompakt-HUD
			// Phase 11: Lage und Fahndung (echt aus der Lagekarte und dem Fahndungsregister)
			Security = SecurityInfo.ForSector(u, sector.Id, PlayerData).Summary,
			Wanted = SecurityInfo.HudLine(u) ?? "",
			WantedSevere = SpaceSim.Core.Simulation.Wanted.MaxLevel(u, u.PlayerActor) >= 3,
			Fire = SpaceSim.Core.Combat.DamageInfo.FireWarning(u, PlayerData) ?? "",   // Phase 26
			Mission = SpaceSim.Core.Missions.MissionMarkers.First(u) is { } mk ? mk.Label : "",   // Runde 41: Auftragszeile (Gold)
			AutopilotOn = _autopilot,
			AutoMineOn = _miner.Enabled,
			TractorOn = _tractorOn,
			FastOn = _fast,
			SliderValue = _hud.Flight.SliderValue,
			Speed = _player.Velocity.Length(),
			Bays = _bayViews,
		};

		// Zielkarte: Phase 25 gewähltes Schiff (Schild/Hülle), sonst Autopilot-Ziel, sonst Laserziel
		Vec2? targetWorld = null;
		d.AutoFireOn = _fight.AutoFire;
		d.AutoFireHint = SpaceSim.Core.Combat.PlayerGunnery.WeaponSlots(u, PlayerData).Count > 0 && _docked is null && (u.PendingEncounters.Any(p => p.Target == u.PlayerShip && p.Kind == EncounterKind.Raid)
			|| u.Fights.Of(u.PlayerShip) is { Cause: EncounterCause.Raid });   // Runde 15
		d.HasWeapons = _bays.Any(b => SpaceSim.Core.Combat.PlayerGunnery.SlotOf(b.Key) >= 0);
		d.EnemyNear = SpaceSim.Core.Combat.PlayerCombat.NextEnemy(_session.U, PlayerData, default).Value != 0;
		d.CanSurrender = _docked is null && SpaceSim.Core.Combat.PlayerSurrender.Issue(_session.U) is null;   // Phase 27
		d.SurrenderRelevant = _docked is null && SpaceSim.Core.Combat.PlayerSurrender.Relevant(_session.U);   // Gerätetest Phase 28: Taste auch ohne Fracht, deaktiviert mit Grund
		d.SurrenderReason = SpaceSim.Core.Combat.PlayerSurrender.Issue(_session.U) ?? "";
		d.HitWarning = _fight.Warning(u, pos, headingDeg, Time.GetTicksMsec() / 1000.0);
		// Phase 28: gewählte Station angreifbar? (Kriegsführung an, Waffen an Bord, nicht angedockt, Core-Regel PlayerGunnery.StationIssue)
		var navStation = _targetStation is { } nsid && u.Stations.TryGetValue(nsid, out var nst) && nst.Sector == u.PlayerSector ? nst : null;
		d.CanAttackStation = _docked is null && u.Rules.Warfare && d.HasWeapons && navStation is not null
			&& SpaceSim.Core.Combat.PlayerGunnery.StationIssue(u, PlayerData, navStation) is null;
		d.StationAttackArmed = _fight.StationTarget.Value != 0;
		d.StationAttackInfo = navStation is not null ? SpaceSim.Core.Combat.WarInfo.DefenseWarning(u, navStation, PlayerData) : "";
		// Runde 5: Kräfteverhältnis für die gewählte bzw. angegriffene Station mit Geschützen (Hinweis „Aussichtslos allein – Fregatte oder Flotte nötig“)
		var oddsStation = _fight.AtStation && u.Stations.TryGetValue(_fight.StationTarget, out var oas) ? oas : navStation;
		if (u.Rules.Warfare && _docked is null && d.HasWeapons && _fight.Target.Value == 0 && oddsStation is not null && oddsStation.Sector == u.PlayerSector
			&& SpaceSim.Core.Combat.StationCombat.DefenseOf(u, oddsStation) is not null)
		{
			d.StationOdds = SpaceSim.Core.Combat.WarInfo.SiegeOddsText(u, oddsStation, PlayerData);
			d.StationHopeless = SpaceSim.Core.Combat.WarInfo.SiegeHopeless(u, oddsStation, PlayerData);
		}
		// Phase 31: gewähltes Tor angreifbar?
		var navGate = _targetGate is { } ngid && u.Gates.TryGetValue(ngid, out var ng) && ng.Sector == u.PlayerSector ? ng : null;
		d.CanAttackGate = _docked is null && u.Rules.Warfare && u.Rules.GateCombat && d.HasWeapons && navGate is not null && !_fight.AtGate
			&& SpaceSim.Core.Combat.PlayerGunnery.GateIssue(u, PlayerData, navGate) is null;
		// Runde 17 (Handy-Test: „Verbrechen · Hülle 35…“, „Notbetrieb – nicht an…“ abgeschnitten): kurze Unterzeilen; die Hülle steht auf der Zielkarte
		d.GateAttackInfo = navGate is not null ? "Verbrechen" : "";
		d.GateAttackBlocked = _docked is null && u.Rules.GateCombat && d.HasWeapons && navGate is not null && SpaceSim.Core.Combat.GateCombat.InGrace(u, navGate)
			? $"Notbetrieb {SpaceSim.Core.Combat.GateCombat.GraceLeft(u, navGate) / SimClock.TicksPerSecond / 60}:{SpaceSim.Core.Combat.GateCombat.GraceLeft(u, navGate) / SimClock.TicksPerSecond % 60:00}" : "";   // Runde 14b: Schonfrist
		d.EscapeStatus = SpaceSim.Core.Combat.PlayerCombat.EscapeStatus(u);
		d.AimAssistVisible = _docked is null && AimTarget() is not null;
		d.AimAssistOn = _aimAssist;
		if (_fight.AtStation && u.Stations.TryGetValue(_fight.StationTarget, out var ast))
		{
			d.TargetName = $"Angriff: {ActorNames.Tagged(u, ast.Owner, ast.Name)}";
			d.TargetPrefix = "Angriff: "; d.TargetNameOptions = SpaceSim.Core.Model.StationNames.Candidates(u, ast);   // Runde 44i: kürzere Formen statt „…“
			d.TargetHasInfo = true;
			d.TargetHostile = true;
			targetWorld = ast.Position;
			d.TargetDistance = Vec2.Distance(pos, ast.Position);
			d.TargetInfo = StationInfoLine(u, ast, pos);   // Runde 14/16: Außenhaut und ob die eigenen Geschütze reichen (kurz)
			// Prüfung Phase 28: kurz genug für die Zielkarte (vorher „… · Schild 250/250…“ abgeschnitten)
			d.TargetCombat = SpaceSim.Core.Combat.WarInfo.StationShort(u, ast);
		}
		else if (_fight.AtGate && u.Gates.TryGetValue(_fight.GateTarget, out var agt))
		{
			d.TargetName = $"Angriff: {SpaceSim.Core.Combat.GateCombat.NameOf(u, agt)}";
			d.TargetHasInfo = true;
			d.TargetHostile = true;
			targetWorld = agt.Position;
			d.TargetDistance = Vec2.Distance(pos, agt.Position);
			d.TargetInfo = $"Entfernung {d.TargetDistance:0} m · Tor";
			d.TargetCombat = SpaceSim.Core.Combat.GateInfo.Short(u, agt);
		}
		else if (_fight.Target.Value != 0 && u.Ships.TryGetValue(_fight.Target, out var ts) && SpaceSim.Core.Combat.PlayerCombat.PositionOf(u, _fight.Target) is { } tsp)
		{
			bool hostile = SpaceSim.Core.Combat.PlayerCombat.IsHostile(u, PlayerData, ts.Id);
			d.TargetName = SpaceSim.Core.Combat.PlayerCombat.Describe(u, ts);
			d.TargetHasInfo = true;
			d.TargetHostile = hostile;
			// Runde 21 (Handy-Test b03: Begleiter „Entfernung 0 m“): die dargestellte Position (Formationsplatz aus dem Materializer), sonst die Core-Position —
			// ein Folger steht im Core beim Führer
			var shown = _view.ShipPosition(ts.Id) is { } vp3 ? new Vec2(vp3.X, vp3.Z) : tsp.Pos;
			targetWorld = shown;
			d.TargetDistance = Vec2.Distance(pos, shown);
			d.TargetInfo = $"Entfernung {d.TargetDistance:0} m" + (hostile ? " · feindlich" : "");
			d.TargetCombat = CombatController.TargetLine(u, ts);
		}
		else if (_target is { } t)
		{
			d.TargetName = _targetStation is { } tsn && u.Stations.TryGetValue(tsn, out var tst0) ? ActorNames.Tagged(u, tst0.Owner, tst0.Name) : t.Label;   // Phase 29: Kürzel
			if (_targetStation is { } tsn1 && u.Stations.TryGetValue(tsn1, out var tst1)) d.TargetNameOptions = SpaceSim.Core.Model.StationNames.Candidates(u, tst1);   // Runde 44i
			d.TargetHasInfo = true;
			if (t.Sector == u.PlayerSector)
			{
				targetWorld = t.Position;
				d.TargetDistance = Vec2.Distance(pos, t.Position);
				d.TargetInfo = $"Entfernung {d.TargetDistance:0} m";
				// Runde 14 (Handy-Test: „Entfernung 2 m“ am großen HQ): bei Stationen der Abstand zur Außenhaut, die Mitte dahinter
				if (_targetStation is { } tsr && u.Stations.TryGetValue(tsr, out var tsrs) && tsrs.Sector == u.PlayerSector)
					d.TargetInfo = StationInfoLine(u, tsrs, pos);
			}
			else d.TargetInfo = "anderer Sektor – über Gate fliegen";
			// Phase 31: ein gewähltes Tor zeigt seinen Zustand (Hülle/Schild, beschädigt, außer Betrieb mit Wiederanlauf)
			if (_targetGate is { } tg && u.Gates.TryGetValue(tg, out var tgg) && tgg.Sector == u.PlayerSector && SpaceSim.Core.Combat.GateCombat.TypeOf(u, tgg) is not null)
			{
				d.TargetName = SpaceSim.Core.Combat.GateCombat.NameOf(u, tgg);
				d.TargetCombat = SpaceSim.Core.Combat.GateInfo.Short(u, tgg);
			}
			// zweiter Gerätetest Phase 28: eine gewählte Station mit Verteidigung zeigt Hülle/Schild immer (vorher erst nach Angriffsbeginn)
			if (u.Rules.Warfare && _targetStation is { } tsid && u.Stations.TryGetValue(tsid, out var tst) && tst.Sector == u.PlayerSector && !tst.IsBuildSite
				&& SpaceSim.Core.Combat.StationCombat.DefenseOf(u, tst) is not null)
				d.TargetCombat = SpaceSim.Core.Combat.WarInfo.StationShort(u, tst);
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
			// Phase 25: mit Schiffsziel zählt der erste Waffenschacht, sonst wie bisher der Laser
			int ai = _bays.Count > 0 && _bays[0].Key is "jump" or "cloak" ? _bays.Count : 0;   // Sprung/Tarnung haben keinen Feuerwinkel
			if (d.TargetCombat.Length > 0)
				for (int k = 0; k < _bays.Count; k++) if (SpaceSim.Core.Combat.PlayerGunnery.SlotOf(_bays[k].Key) >= 0) { ai = k; break; }
			var bay = ai < _bays.Count ? _bays[ai] : WeaponBays.PlayerDefault[0];
			var aimAt = d.TargetCombat.Length > 0 ? tw : _bayAimPos;
			d.TargetInArc = _bayStates.Length > ai && _bayStates[ai] is BayState.Ready or BayState.Reloading;
			d.TargetArc = _bayStates.Length > ai && _bayStates[ai] == BayState.OutsideArc && aimAt is { } a
				? TurnHintText(bay, headingDeg, pos, a)
				: _bayStates.Length > ai && _bayStates[ai] == BayState.Ready ? $"Im Feuerwinkel: {bay.Name}" : "";
		}
		// Gerätetest Phase 28 (Runde 4): Station mit Geschützen gewählt — klare Lage „in Geschützreichweite“/„außerhalb“ (Abstand zur Mitte, wie die Schussregel)
		if (u.Rules.Warfare && (_fight.AtStation ? _fight.StationTarget : _targetStation) is { } rsid && rsid.Value != 0 && u.Stations.TryGetValue(rsid, out var rst)
			&& rst.Sector == u.PlayerSector && _fight.Target.Value == 0 && SpaceSim.Core.Combat.WarInfo.RangeShort(u, rst, PlayerData, pos) is { Length: > 0 } rs
			&& d.TargetArc.Length == 0)
			d.TargetArc = rs;

		// Runde 16: Munition/Raketen leer im Gefecht — sichtbar auf der Zielkarte und einmal als Hinweis (vorher schwieg der Massetreiber ohne Erklärung)
		{
			bool fighting = u.Fights.Of(PlayerData.Id) is not null;
			string dep = fighting ? SpaceSim.Core.Combat.PlayerGunnery.DepletedText(u, PlayerData) : "";
			if (dep.Length > 0 && d.TargetArc.Length == 0) d.TargetArc = dep;
			if (dep.Length > 0 && dep != _lastDepleted)
				_hud.Toast(dep + ". Nachladen: Werft oder Polizeiwache (Reparatur → Munition).", 4f);
			_lastDepleted = dep;
		}

		// Kontexttaste: nahe Station „Andocken“, am Gate „Springen“
		// Runde 20: das gewählte Ziel hat Vorrang (Core: ContextChoice) — mit gewähltem Tor kein „Andocken“ an einer nahen Station
		var ctx = Context();
		if (ctx.Kind == ContextKind.Dock && u.Stations.TryGetValue(new StationId(ctx.Id), out var cst))
		{
			d.ContextLabel = SpaceSim.Core.Simulation.Docking.Allows(u, cst, PlayerData) ? $"Andocken: {cst.Name}" : $"Zu klein: {cst.Name}";   // Phase 30
			// Runde 44i (Handytest u19/u21: „Andocken: Hauptquartier – Födera…“): kürzere Namensformen, notfalls zweizeilig
			d.ContextVerb = SpaceSim.Core.Simulation.Docking.Allows(u, cst, PlayerData) ? "Andocken" : "Zu klein";
			d.ContextNameOptions = SpaceSim.Core.Model.StationNames.Candidates(u, cst);
		}
		else if (ctx.Kind == ContextKind.Jump && u.Gates.TryGetValue(new GateId(ctx.Id), out var cg))
			d.ContextLabel = $"Springen: {u.Sectors[u.Gates[cg.Target].Sector].Name}";

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
			if (bays[i].Key is "tractor" or "split" or "contact" or "jump" or "cloak") continue;   // Traktor ist Umschalter, der Spalter feuert nur einzeln; Sprung/Tarnung sind keine Waffen
			fire++;
			if (_bayStates[i] == BayState.Ready) ready++;
		}
		d.ReadyBays = ready;
		d.FireBays = fire;
		d.InCombat = _session.U.Fights.Of(PlayerData.Id) is not null;   // Runde 34b: Schicht im Gefecht festhalten
		// HUD-Neu: Gruppen des Daumenbogens (Core: BayGroups) mit ihrem Zustand aus den Schachtzuständen
		_groups = SpaceSim.Core.Weapons.BayGroups.Of(_session.U, PlayerData, bays);
		var gv = new List<GroupView>(_groups.Count);
		foreach (var g in _groups)
		{
			if (!g.Combat)
			{
				var v = _bayViews[g.Members[0]];
				gv.Add(new GroupView { Label = v.Name, Sub = v.Detail, State = v.State, Reload = v.Reload, Combat = false, On = bays[g.Members[0]].Key == "tractor" && _tractorOn || bays[g.Members[0]].Key == WeaponBays.Cloak.Key && PlayerData.Cloaked });
				continue;
			}
			int rdy = g.Members.Count(m => _bayStates[m] == BayState.Ready);
			BayState st = rdy > 0 ? BayState.Ready : g.Members.Any(m => _bayStates[m] == BayState.OutsideArc) ? BayState.OutsideArc
				: g.Members.Any(m => _bayStates[m] == BayState.Reloading) ? BayState.Reloading : g.Members.All(m => _bayStates[m] == BayState.NoAmmo) ? BayState.NoAmmo : BayState.NoTarget;
			float rel = g.Members.Max(m => _bayViews[m].Reload);
			gv.Add(new GroupView { Label = g.Label, Sub = $"{rdy}/{g.Members.Count} bereit", State = st, Reload = rel, Combat = true });
		}
		d.Groups = gv;

		_hud.Flight.Refresh(d);
	}

	/// <summary>Zustände der Waffenschächte als reine Core-Funktion (FiringArc) auswerten.</summary>
	void UpdateBays(Sector sector, Vec2 pos, float headingDeg)
	{
		_bays = WeaponBays.For(_session.U, PlayerData);   // Phase 25: ein Schacht je Kampfwaffe
		_fight.Validate(_session.U);
		var bays = _bays;
		_bayViews = new BayView[bays.Count];
		_bayStates = new BayState[bays.Count];
		for (int i = 0; i < bays.Count; i++)
		{
			var bay = bays[i];
			Vec2? target = null;
			float radius = 0f;
			bool reloaded = true;
			int wslot = SpaceSim.Core.Combat.PlayerGunnery.SlotOf(bay.Key);
			if (wslot >= 0)
			{
				// Phase 25: Kampfwaffe — Zustand aus der Core-Regel (Ziel, Reichweite, Munition, Nachladen, Leistung, Winkel)
				// Phase 28: mit scharfer Station ohne Schiffsziel zählt die Station
				var wst = _fight.AtStation ? SpaceSim.Core.Combat.PlayerGunnery.StateAtStation(_session.U, PlayerData, wslot, headingDeg, _fight.StationTarget)
					: _fight.AtGate ? SpaceSim.Core.Combat.PlayerGunnery.StateAtTarget(_session.U, PlayerData, wslot, headingDeg, SiegeRef.Of(_fight.GateTarget))   // Phase 31
					: CombatController.StateOf(_session.U, PlayerData, bay, headingDeg, _fight.Target);
				var tpos = _fight.AtStation && _session.U.Stations.TryGetValue(_fight.StationTarget, out var atSt) ? atSt.Position
					: _fight.AtGate && _session.U.Gates.TryGetValue(_fight.GateTarget, out var atG) ? atG.Position
					: SpaceSim.Core.Combat.PlayerCombat.PositionOf(_session.U, _fight.Target)?.Pos;
				_bayStates[i] = wst;
				string wdetail = wst switch
				{
					BayState.Ready => "FEUER",
					BayState.OutsideArc when tpos is { } tp => TurnHintText(bay, headingDeg, pos, tp),
					BayState.Reloading => "lädt nach",
					BayState.NoPower => "keine Leistung",
					BayState.NoAmmo => "keine Munition",
					_ => _fight.Target.Value == 0 && !_fight.AtStation && !_fight.AtGate ? "kein Ziel"
						: !_fight.AtStation && _session.U.Ships.TryGetValue(_fight.Target, out var tgs) && SpaceSim.Core.Combat.PlayerCombat.TargetIssueShort(_session.U, PlayerData, tgs) is { } why
							? why   // Runde 20: Grund aus der Core-Regel (vorher „Ziel angedockt“ geraten)
							: "außer Reichweite",
				};
				string wplace = (bay.IsTurret ? "Turm · 360°" : $"{FacingName(bay.Facing)} · ±{(int)bay.ArcDegrees}°") + $" · {bay.Range:0} m";
				// Phase 25 (Korrektur): lange Modulnamen auf das letzte Wort kürzen („Kinetisches Geschütz“ → „Geschütz“), damit sie in die Taste passen
				string wname = ShortBayName(bay.Name);
				// Gerätetest Phase 28: Raketenvorrat auf der Bucht („Raketen 6/6“)
				if (wslot < PlayerData.Fit.Count && PlayerData.Fit[wslot] is { } wkey && _session.U.Modules.TryGetValue(wkey, out var wmod) && SpaceSim.Core.Combat.Missiles.IsMissile(wmod))
					wplace = $"{SpaceSim.Core.Combat.Missiles.Loaded(_session.U, PlayerData):0}/{PlayerData.Stats.MissileCapacity:0} Rak. · {bay.Range:0} m";   // Runde 18: kurz (vorher „Vorrat 12/12 · 11…“)
				_bayViews[i] = new BayView { Name = wname, Place = wplace, Detail = wdetail, State = wst,
					Reload = SpaceSim.Core.Combat.PlayerGunnery.ReloadProgress(_session.U, PlayerData, wslot), Facing = bay.Facing, ArcDegrees = bay.ArcDegrees };
				continue;
			}
			if (JumpCloakBay(i, bay)) continue;   // Sprungantrieb, Tarnfeld (Main.JumpCloak.cs)
			if (bay.Key == WeaponBays.Contact.Key)
			{
				// Phase 29: Kontaktwerkzeug — reine Anzeige, Zustand aus der Core-Regel (OreChunks.Contact im selben Bild)
				var cs = _chunks.LastContact;
				var cst = cs is ContactState.Mining or ContactState.Released ? BayState.Ready : cs == ContactState.HoldFull ? BayState.NoAmmo : cs == ContactState.Anchoring ? BayState.Reloading : BayState.NoTarget;
				_bayStates[i] = cst;
				// Phase 32: Ankerzustand („verankert“, „Anker fliegt“ mit Fortschritt, kurz nach einem Abriss der Grund)
				bool anchors = SpaceSim.Core.Mining.MiningAnchor.Active(_session.U);
				string? broke = _anchorBrokeLeft > 0f ? _anchorBrokeShort : null;
				float prog = cs == ContactState.Anchoring ? Mathf.Clamp(PlayerData.AnchorProgress / Mathf.Max(1, _session.U.Rules.AnchorTicks), 0f, 1f) : 1f;
				_bayViews[i] = new BayView { Name = anchors && PlayerData.AnchorRock != 0 ? "Lösen" : cs == ContactState.Released ? "Ankern" : bay.Name, Place = "Bug · Kontakt", Detail = WeaponBays.ContactText(cs, anchors, broke), State = cst, Reload = prog, Facing = bay.Facing, ArcDegrees = bay.ArcDegrees };
				continue;
			}
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
			else if (bay.Key == WeaponBays.Salvager.Key)
			{
				// Phase 26: Bergungsgreifer — Ziel ist das gewählte bzw. nächste Wrack mit Modulen in Reichweite
				target = _salvage.Current(_session.U, pos, wantModules: true)?.Position;
			}
			else   // Traktor: Ziel ist die nächste Beute in Reichweite (Erzbrocken, sonst Wrack/Frachtbehälter mit Fracht — Phase 26)
			{
				OreChunk? best = null; float bd = bay.Range;
				foreach (var c in _chunks.Chunks)
				{
					float dd = Vec2.Distance(c.Position, pos);
					if (dd <= bd) { bd = dd; best = c; }
				}
				target = best?.Position ?? _salvage.Current(_session.U, pos, wantModules: false)?.Position;
			}
			float progress = bay.Key == WeaponBays.Splitter.Key ? 1f - _splitReload / WeaponBays.SplitReloadSeconds : _chunks.ReloadProgress;
			var st = FiringArc.State(bay, headingDeg, pos, target, radius, reloaded, progress);
			_bayStates[i] = st;
			string place = bay.IsTurret ? "Turm · 360°" : $"{FacingName(bay.Facing)} · ±{(int)bay.ArcDegrees}°";
			string detail = st switch
			{
				BayState.Ready => bay.Key == "tractor" ? (_tractorOn ? "AN" : "aus") : bay.Key == "laser" && _mineTarget is null ? "FEUER · nächster Fels" : "FEUER",   // Gerätetest: ohne gewähltes Ziel sagen, worauf er schießt
				BayState.OutsideArc => TurnHintText(bay, headingDeg, pos, target!.Value),
				BayState.Reloading => "lädt nach",
				_ => bay.Key == "tractor" ? "keine Beute" : "kein Ziel",
			};
			if (bay.Key == WeaponBays.Salvager.Key) detail = st == BayState.Ready ? "BERGEN" : st == BayState.NoTarget ? "kein Wrack" : detail;
			_bayViews[i] = new BayView { Name = bay.Name, Place = place, Detail = bay.Key == WeaponBays.Splitter.Key && st == BayState.Ready ? "SPALTEN" : detail, State = st, Reload = progress, Facing = bay.Facing, ArcDegrees = bay.ArcDegrees };
		}
	}

	/// <summary>
	/// Kurzname einer Waffe für die Schachttaste: lange Namen aufs letzte Wort („Kinetisches Geschütz“ → „Geschütz“, Phase 25), Phase 26 (Prüfung): zusammengesetzte
	/// Waffenwörter auf ihren ersten Teil („Laserkanone“ → „Laser“), damit der Name die Taste nicht ausfüllt.
	/// </summary>
	public static string ShortBayName(string name)
	{
		// HUD-Neu: Größen-/Stufenzusatz („Laserkanone M“, „… Mk2“) bleibt erhalten, gekürzt wird das Wort davor → „Laser M“
		int sp = name.LastIndexOf(' ');
		if (sp > 0 && name.Length - sp - 1 is >= 1 and <= 3 && name.Length > 9)
		{
			string tier = name[(sp + 1)..] switch { "Mk2" => "II", "Mk3" => "III", "Mk4" => "IV", var t => t };
			string head = ShortBayName(name[..sp]);
			if (head == "Raketen") head = "Rakete";
			return head + " " + tier;
		}
		if (name.Length > 13 && name.LastIndexOf(' ') > 0) name = name[(name.LastIndexOf(' ') + 1)..];
		foreach (var suffix in new[] { "kanone", "geschütz", "werfer" })
			if (name.Length > 9 && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length - suffix.Length >= 3) return name[..^suffix.Length];
		return name;
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
		if (bays[index].Key == WeaponBays.Jump.Key || bays[index].Key == WeaponBays.Cloak.Key) { JumpCloakDown(bays[index].Key); return; }   // Sprungantrieb, Tarnfeld
		if (bays[index].Key == WeaponBays.Splitter.Key) { FireSplitter(index); return; }
		if (bays[index].Key == WeaponBays.Salvager.Key) { GrabSalvage(); return; }
		if (bays[index].Key == WeaponBays.Contact.Key)
		{
			// Phase 32: verankert → die Taste löst die Anker
			if (PlayerData.AnchorRock != 0)
			{
				int held = PlayerData.AnchorRock;
				SpaceSim.Core.Mining.MiningAnchor.Release(_session.U, PlayerData, _session.U.PlayerSector, "released");
				_chunks.HoldOff(held);   // Runde 20: kein selbsttätiger Neuanker, solange der Bug am selben Fels bleibt
				if (_miner.Enabled) SetAutoMining(false, "Auto-Mining aus (Anker gelöst)");
				return;
			}
			// Runde 20: nach „Lösen“ mit Bug am Fels — die Taste heißt „Ankern“ und setzt die Harpunen wieder
			if (_chunks.LastContact == ContactState.Released) { _chunks.Rearm(); _hud.Toast("Ankern: Harpunen werden geschossen"); return; }
			_hud.Toast(SpaceSim.Core.Mining.MiningAnchor.Active(_session.U)
				? "Bohrer: mit dem Bug langsam (Schub unter 30 %) an den Asteroiden – die Harpunen verankern das Schiff, dann läuft der Abbau."
				: "Bohrer: mit dem Bug an den Asteroiden fliegen – der Abbau läuft bei Kontakt von selbst.");
			return;
		}
		if (SpaceSim.Core.Combat.PlayerGunnery.SlotOf(bays[index].Key) is int ws and >= 0) { FireWeapon(ws, quiet: false); return; }
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

	/// <summary>Phase 26: Schacht „Bergen“ — ein Modul aus dem gewählten/nächsten Wrack bergen (Core-Regel <see cref="Wrecks.TakeModule"/>). true = geborgen.</summary>
	public bool GrabSalvage()
	{
		SyncPlayerData();
		bool ok = _salvage.Grab(_session.U, PlayerData);
		_sfx.Play(ok ? SfxId.Pickup : SfxId.Click);
		_hud.Toast(_salvage.LastMessage);
		return ok;
	}

	/// <summary>
	/// HUD-Neu: Gruppenknopf — eine Kampfgruppe feuert alle ihre bereiten Schächte (dieselbe Regel wie FEUER, <see cref="WeaponBays.ReadyToFire"/>, auf die
	/// Gruppe beschränkt); eine Bergbaugruppe wirkt wie ihr einziger Schacht (Laser, Traktor, Bohrer/Anker, Spalter, Bergen).
	/// </summary>
	void OnGroupDown(int index)
	{
		if (index < 0 || index >= _groups.Count) return;
		var g = _groups[index];
		if (!g.Combat) { OnBayDown(g.Members[0]); return; }
		if (_docked != null || _hud.AnyModalOpen) return;
		var members = new HashSet<int>(g.Members);
		bool any = false;
		foreach (int i in WeaponBays.ReadyToFire(_bays, _bayStates))
			if (members.Contains(i) && SpaceSim.Core.Combat.PlayerGunnery.SlotOf(_bays[i].Key) is int ws and >= 0 && FireWeapon(ws, quiet: true)) any = true;
		if (!any) Hint($"{g.Label}: keine Waffe bereit und im Winkel");
		LastGroupFired = any ? g.Label : "";
	}
	List<SpaceSim.Core.Weapons.BayGroup> _groups = new();
	/// <summary>Selbsttest: zuletzt gefeuerte Gruppe („“ = keine) und Gruppen.</summary>
	internal string LastGroupFired { get; private set; } = "";
	internal IReadOnlyList<SpaceSim.Core.Weapons.BayGroup> TestGroups => _groups;
	internal void TestGroupDown(int i) => OnGroupDown(i);

	/// <summary>„Alle Feuer“: löst nur bereite Waffen im Winkel aus (hier: der Laser).</summary>
	void FireReadyBays(int from = 0)
	{
		var bays = _bays;
		bool any = false;
		// Runde 34b (Prüfung B8): eine Kernregel für „Alle Feuer“ und die Sammeltaste (bereit = geladen, im Feuerwinkel, mit Ziel)
		foreach (int i in WeaponBays.ReadyToFire(bays, _bayStates, from))
		{
			if (SpaceSim.Core.Combat.PlayerGunnery.SlotOf(bays[i].Key) is int ws and >= 0) { if (FireWeapon(ws, quiet: true)) any = true; }
			else { _fireRequested = true; any = true; }
		}
		if (!any) Hint("Keine Waffe bereit und im Winkel");
	}

	/// <summary>Phase 25: ein Schuss einer Kampfwaffe auf das gewählte Ziel (Core-Regel <see cref="SpaceSim.Core.Combat.PlayerGunnery.TryFire"/>). true = gefeuert.</summary>
	bool FireWeapon(int slot, bool quiet)
	{
		if (_docked != null || _hud.AnyModalOpen) return false;
		SyncPlayerData();
		var r = _fight.Fire(_session.U, PlayerData, slot, Mathf.RadToDeg(_player.Heading));
		if (r.Result == SpaceSim.Core.Combat.GunResult.Fired)
		{
			_sfx.Play(SfxId.Zap);
			return true;
		}
		if (!quiet && r.Result != SpaceSim.Core.Combat.GunResult.Reloading) Hint(r.Message);
		return false;
	}

	/// <summary>Phase 25: Dauerfeuer bzw. „Alle Feuer“ für die Kampfwaffen: jede bereite Waffe im Winkel schießt.</summary>
	void FireCombatBays(bool quiet)
	{
		if (_fight.Target.Value == 0 && !_fight.AtStation && !_fight.AtGate) return;
		var bays = _bays;
		for (int i = 0; i < bays.Count && i < _bayStates.Length; i++)
			if (_bayStates[i] == BayState.Ready && SpaceSim.Core.Combat.PlayerGunnery.SlotOf(bays[i].Key) is int ws and >= 0) FireWeapon(ws, quiet);
	}

	/// <summary>Phase 28: „Station angreifen“ bestätigt — die Waffen schießen auf die gewählte Station (bis ein Schiff gewählt wird oder das Ziel wechselt).</summary>
	void ArmStationAttack()
	{
		if (_targetStation is not { } sid || !_session.U.Stations.TryGetValue(sid, out var st)) return;
		if (SpaceSim.Core.Combat.PlayerGunnery.StationIssue(_session.U, PlayerData, st) is { } issue) { _hud.Toast(issue, 3f); return; }
		_fight.ClearTarget();
		_fight.ArmStation(sid);
		bool hopeless = SpaceSim.Core.Combat.WarInfo.SiegeHopeless(_session.U, st, PlayerData);
		// Gerätetest Phase 28 (Runde 4): der Angriff schaltet Dauerfeuer und „Ziel folgen“ ein (vorher feuerte nichts, bis Dauerfeuer an war)
		_fight.AutoFire = true;
		EnableAimAssist();
		_hud.Toast($"Angriff auf {st.Name}: Dauerfeuer an — „Ziel folgen“ fliegt auf Reichweite deiner Geschütze heran und hält (Regler übersteuert). Fahndung und Gegenwehr folgen."
			+ (hopeless ? $" {SpaceSim.Core.Combat.WarInfo.SiegeOddsText(_session.U, st, PlayerData)}!" : ""), 5f);
	}

	/// <summary>
	/// Phase 31: „Tor angreifen“ bestätigt — die Waffen schießen auf das gewählte Tor (schweres Verbrechen bei beiden Endfraktionen; fällt es aus, ist es
	/// geschlossen, bis es repariert ist oder die Notregel es wieder anlaufen lässt).
	/// </summary>
	void ArmGateAttack()
	{
		var u = _session.U;
		if (_targetGate is not { } gid || !u.Gates.TryGetValue(gid, out var g)) return;
		if (SpaceSim.Core.Combat.PlayerGunnery.GateIssue(u, PlayerData, g) is { } issue) { _hud.Toast(issue, 3f); return; }
		_fight.ClearTarget();
		_fight.ArmGate(gid);
		_fight.AutoFire = true;
		EnableAimAssist();
		_hud.Toast($"Angriff auf {SpaceSim.Core.Combat.GateCombat.NameOf(u, g)}: Dauerfeuer an — schweres Verbrechen bei {GateDefendersText(u, g)}. Fällt das Tor aus, ist die Route gesperrt.", 5f);
	}
	internal void TestArmGate(GateId id) { _targetGate = id; ArmGateAttack(); }
	internal bool TestAtGate => _fight.AtGate;

	static string GateDefendersText(Universe u, Gate g)
	{
		var list = SpaceSim.Core.Combat.GateCombat.DefendersOf(u, g).Select(f => u.Factions.TryGetValue(f, out var fac) ? fac.Name : "?").ToList();
		return list.Count == 0 ? "niemandem (herrenlos)" : string.Join(" und ", list);
	}

	/// <summary>Phase 25: Taste „Nächster Gegner“ (Gefechtsgegner, dann Kaperschiffe im Sektor, nach Abstand, zyklisch).</summary>
	/// <summary>Phase 27: „Ergeben“ — Polizei: Strafe zahlen und Schmuggelware abgeben; Kaperer: Fracht abwerfen, sie drehen ab (Core: PlayerSurrender).</summary>
	void Surrender()
	{
		var res = SpaceSim.Core.Combat.PlayerSurrender.Surrender(_session.U);
		if (res is null) { _hud.Toast(SpaceSim.Core.Combat.PlayerSurrender.Issue(_session.U) ?? "Ergeben nicht möglich", 3f); return; }
		_sfx.Play(SfxId.Click);
		LastSecurityMessage = res.Message;
	}

	/// <summary>Phase 27: Selbsttest — „Ergeben“ drücken.</summary>
	internal void TestSurrender() => Surrender();
	/// <summary>Phase 27: Selbsttest — HUD-Daten sofort neu setzen (sonst erst im nächsten Frame).</summary>
	internal void SyncHudForTest() => UpdateHud();

	/// <summary>Zielhilfe: Ort des Ziels — scharfe Station, sonst gewähltes feindliches Schiff im Sektor (Gefechts- oder Flugposition); null = keins.</summary>
	Vec2? AimTarget()
	{
		var u = _session.U;
		if (_fight.AtStation && u.Stations.TryGetValue(_fight.StationTarget, out var st) && st.Sector == u.PlayerSector) return st.Position;
		if (_fight.AtGate && u.Gates.TryGetValue(_fight.GateTarget, out var ag) && ag.Sector == u.PlayerSector) return ag.Position;   // Phase 31
		if (_fight.Target.Value != 0 && u.Ships.ContainsKey(_fight.Target) && SpaceSim.Core.Combat.PlayerCombat.IsHostile(u, PlayerData, _fight.Target)
			&& SpaceSim.Core.Combat.PlayerCombat.PositionOf(u, _fight.Target) is { } p && p.Sector == u.PlayerSector) return p.Pos;
		return null;
	}

	/// <summary>Gerätetest Phase 28: Zielhilfe an (Standard; „Dauerfeuer“ schaltet sie mit ein).</summary>
	bool _aimAssist = true;
	/// <summary>Runde 4: Ziel, für das der Spieler „Ziel folgen“ bewusst ausgeschaltet hat (null = keins) — Dauerfeuer/Angriff schalten es dann nicht wieder ein.</summary>
	string? _aimOffFor;
	string AimKey() => _fight.AtStation ? $"st{_fight.StationTarget.Value}" : _fight.AtGate ? $"gt{_fight.GateTarget.Value}" : $"sh{_fight.Target.Value}";
	void EnableAimAssist() { if (_aimOffFor != AimKey()) { _aimAssist = true; _aimOffFor = null; } }
	internal bool TestAimAssist { get => _aimAssist; set => _aimAssist = value; }
	internal float TestHeadingRad => _player.Heading;

	void SelectNextEnemy()
	{
		SyncPlayerData();
		_sfx.Play(SfxId.Click);
		if (!_fight.NextEnemy(_session.U, PlayerData)) { _hud.Toast("Kein Gegner in diesem Sektor"); return; }
		ClearPlaceTarget();   // Runde 21: „Nächster Gegner“ beendet Tor-/Stationswahl und -angriff wie ein Tipp aufs Schiff
		if (_session.U.Ships.TryGetValue(_fight.Target, out var t)) _hud.Toast($"Ziel: {SpaceSim.Core.Combat.PlayerCombat.Describe(_session.U, t)}");
	}

	// ───────── Statische Szene ─────────

	void BuildEnvironment()
	{
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.01f, 0.01f, 0.04f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			// Runde 8: Stationsmodelle in Gunmetal/Graphit — Umgebung etwas heller, damit die Flächen im Schatten lesbar bleiben
			AmbientLightColor = new Color(0.32f, 0.35f, 0.47f),
			AmbientLightEnergy = 1.15f,
			GlowEnabled = true,
		};
		AddChild(new WorldEnvironment { Environment = env });
		AddChild(new World.SkyboxView(env, () => _session?.U, ChaseSkyForward));   // Himmelskugel je Sektor (eigene Komponente, folgt dem Spielersektor; in der Rückansicht dreht sie mit der Kamera)
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), LightEnergy = 1.3f });
		// Runde 8: schwaches, kühles Fülllicht von der Gegenseite (ohne Schatten, mobil billig) — die abgewandten Seiten der Modelle versinken sonst
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-25, 210, 0), LightEnergy = 0.45f, LightColor = new Color(0.7f, 0.8f, 1f), ShadowEnabled = false });
	}

	/// <summary>Runde 44: Kantenlänge einer Sternkachel; 3 × 3 Kacheln folgen der Kamera auf diesem Raster (das Muster wiederholt sich lückenlos).</summary>
	const float StarTile = 800f;
	readonly List<MultiMeshInstance3D> _starTiles = new();

	/// <summary>Runde 44 (größere Sektoren): Sterne kacheln mit der Kamera mit, der Planet folgt mit Parallaxe; Sichtweite aus der Ausdehnung.</summary>
	void FollowBackdrop()
	{
		var c = _cam.Position;
		float cx = Mathf.Round(c.X / StarTile) * StarTile, cz = Mathf.Round(c.Z / StarTile) * StarTile;
		for (int i = 0; i < _starTiles.Count; i++)
			_starTiles[i].Position = new Vector3(cx + (i % 3 - 1) * StarTile, 0f, cz + (i / 3 - 1) * StarTile);
		_view.FollowBackdrop(c);
		if (_session != null && _session.U.Sectors.TryGetValue(_session.U.PlayerSector, out var sec))
			_cam.Far = Mathf.Max(4000f, sec.Extent * 2f + 1500f);
	}

	/// <summary>Kantenlänge eines Sternwürfels (vor den Himmelskugeln: kleiner als in Runde 44 — 0,5 —, damit es feiner Staub und keine Klötze sind).</summary>
	public const float StarSize = 0.2f;
	/// <summary>Helligkeitsband der Sterne (unbeleuchtet, Farbe je Stern; Runde 44: weiß 0,9–1 mit Leuchten 2 — vor dem Himmel zu grell).</summary>
	public const float StarBrightMin = 0.22f, StarBrightMax = 0.55f;

	void BuildStars()
	{
		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = new BoxMesh { Size = new Vector3(StarSize, StarSize, StarSize) },
			InstanceCount = 500,
		};
		var rng = new RandomNumberGenerator { Seed = 42 };
		for (int i = 0; i < 500; i++)
		{
			float s = rng.RandfRange(0.5f, 1.3f);
			mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(s, s, s)),
				new Vector3(rng.RandfRange(-StarTile / 2f, StarTile / 2f), rng.RandfRange(-40, -8), rng.RandfRange(-StarTile / 2f, StarTile / 2f))));
			// gedämpft und kaum gesättigt: leicht kühl oder leicht warm, damit der Staub zum Himmel passt und nicht vor ihm steht
			float b = rng.RandfRange(StarBrightMin, StarBrightMax), tint = rng.RandfRange(-0.06f, 0.06f);
			mm.SetInstanceColor(i, new Color(b * (1f + tint), b, b * (1f - tint) * 1.04f));
		}
		// unbeleuchtet: die Instanzfarbe ist die sichtbare Helligkeit (kein Leuchten, kein Glow-Halo)
		var starMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White };
		for (int i = 0; i < 9; i++)   // Runde 44: 3 × 3 Kacheln derselben Sterne (gemeinsames MultiMesh)
		{
			var tile = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = starMat, Name = "Sterne" + i, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			AddChild(tile);
			_starTiles.Add(tile);
		}
	}

	// ───────── Abbau ─────────

	void SetAutoMining(bool on, string? message = null, bool announce = true)
	{
		if (on != _miner.Enabled) Ui.InputLog.Log($"auto-mining {(on ? "AN" : "AUS")}{(message is { Length: > 0 } ? ": " + message : "")} (Aufrufer {Ui.InputLog.Caller()})");
		if (on)
		{
			CancelAutopilot(); _target = null; _targetStation = null;
			_fight.ClearTarget();   // Runde 12 (Handy-Test): die Zielkarte zeigte weiter das zuvor gewählte Schiff
			_hud.Flight.SetSlider(0f);   // Gerätetest: Auto-Mining übernimmt den Regler
			_chunks.Rearm();   // Runde 20: Auto-Mining darf nach einem Lösen von Hand wieder verankern
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

	/// <summary>Gerätetest: Gefahr für das eigene Schiff (Überfallwarnung, Beschuss, Brand, Verlust) — der Zeitraffer schaltet auf ×1, mit Meldung.</summary>
	void Danger(string reason, string? message = null, float seconds = 6f, bool sticky = false)
	{
		if (!_fast) { if (message != null) _hud.Alert(message, seconds, sticky); return; }
		_fast = false;
		// Runde 35c (Prüfung: „Zeitraffer aus: Überfall — Überfall! …“): beginnt die Meldung schon mit dem Grund, steht er nur einmal da
		string head = message is not null && message.StartsWith(reason, StringComparison.OrdinalIgnoreCase) ? "Zeitraffer aus" : $"Zeitraffer aus: {reason}";
		_hud.Alert(head + (message != null ? $" — {message}" : ""), seconds, sticky);
		LastDangerMessage = reason;
	}

	/// <summary>Selbsttest: letzter Grund, aus dem der Zeitraffer abgeschaltet wurde.</summary>
	internal string? LastDangerMessage { get; private set; }

	void Hint(string text)
	{
		TestLastHint = text;
		if (_hintCooldown > 0f) return;
		_hintCooldown = 1.5f;
		_hud.Toast(text, 1.5f);
	}

	/// <summary>Runde 44g: Feuer, Ziel und Traktor des letzten Abbau-Teilschritts (für die Darstellung einmal je Bild).</summary>
	bool _mineFire, _mineTractor;
	Asteroid? _mineAim;

	/// <summary>
	/// Laser und Traktorstrahl des Spielers (manuell oder durch den Automodus), Kontaktabbau/Anker, Brocken — ein Teilschritt der Simulationszeit
	/// <paramref name="delta"/> (Runde 44g: im Zeitraffer mehrmals je Bild). Ein manueller Schuss gilt nur im ersten Teilschritt.
	/// </summary>
	void StepMining(float delta, bool frozen, bool first)
	{
		var u = _session.U;
		var ship = PlayerData;
		var sector = u.Sectors[u.PlayerSector];
		var pos = new Vec2(_player.Position.X, _player.Position.Z);
		if (_mineTarget != null && !_chunks.IsMinable(sector, _mineTarget)) _mineTarget = null;

		bool fire = false, tractor = _tractorOn;
		Asteroid? aim = null;
		if (!frozen)
		{
			if (_miningStep is { } ms) { fire = ms.Fire; tractor |= ms.Tractor; aim = ms.Aim; }   // Automatik: feuert, sobald nachgeladen
			else
			{
				fire = first && _fireRequested;   // Spieler: ein Tipp / Tastendruck = ein Schuss
				aim = _mineTarget ?? _chunks.Nearest(sector, pos, MiningRules.LaserRange);
			}
		}
		if (first) _fireRequested = false;

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
		// Phase 29: Kontaktabbau läuft von selbst, solange die Werkzeugfront den Fels berührt (gemeinsame Core-Regel mit den NPC-Schürfern)
		if (!frozen && !_transition && ship.Stats.ContactMining > 0f)
			_chunks.Contact(u, ship, sector, pos, new Vec2(-Mathf.Sin(_player.Heading), -Mathf.Cos(_player.Heading)), delta, _anchorAllowed);
		if (_chunks.Update(u, ship, pos, tractor, delta) > 0f) _sfx.Play(SfxId.Pickup);
		// Gerätetest: klarer Grund, warum ein Brocken nicht geladen wird (Laderaum für seine Form voll)
		if (_chunks.HoldFull is { } full && u.Goods.TryGetValue(full, out var fg))
			Hint($"Laderaum für {fg.Name} voll ({(fg.Form == GoodForm.Liquid ? "flüssig" : "fest")}) – verkaufen oder abwerfen");
		// Phase 26: der Traktorstrahl zieht auch Fracht aus Wracks und Frachtbehältern in Reichweite (Core-Regel Wrecks.TakeCargo)
		if (tractor && !_transition) { SyncPlayerData(); _salvage.Pull(u, ship, true, delta); }
		_mineFire |= fire;
		if (fire && aim != null) _mineAim = aim;
		_mineTractor = tractor;
	}

	/// <summary>Strahldarstellung von Laser/Traktor, Brocken, NPC-Laser, Schüsse und Raketen — einmal je Bild (Echtzeit).</summary>
	void ShowMining(float delta)
	{
		var u = _session.U;
		var sector = u.Sectors[u.PlayerSector];
		_hintCooldown = Mathf.Max(0f, _hintCooldown - delta);
		var shown = _mineFire && _mineAim != null ? _mineAim : _mineTarget;
		if (shown != null && sector.Asteroids.Contains(shown))
			_mining.SetTarget(new Vector3(shown.Position.X, 0, shown.Position.Y), shown.Radius);
		else _mining.SetTarget(null, 0f);

		double time = Time.GetTicksMsec() / 1000.0;
		_mining.UpdatePlayer(_player.Position, _mineTractor, _chunks.Chunks, time);
		_mining.UpdateNpc(_view.ShipPosition, time);
		_combat.UpdateBeams(ShipWorld, time);   // Runde 11: auch Schüsse des Spielers (vorher ohne Strahl: der Spieler ist kein Marker)
		_combat.UpdateMissiles(u);   // Phase 28: Raketen im Flug
	}

	/// <summary>
	/// Runde 11: Schiffsmodell des aktiven Schiffs (Wechsel/Umbau), Schub und Werkzeugdrehung, Türme des Spielers zum Ziel, Stationstürme zu ihren Gegnern.
	/// </summary>
	void UpdateShipModels(float delta)
	{
		var u = _session.U;
		_view.PlayerPosition = _docked != null ? null : _player.Position;
		if (u.Ships.TryGetValue(u.PlayerShip, out var me))
		{
			_player.SetModel(me.ModelKey, ShipModels.AccentOf(u, me.Owner));
			_player.Animate(SpaceSim.Core.Mining.ContactMining.Spin(me.Stats, _chunks.LastContact), delta);
			if (_player.Visual is { Hardpoints.TurretCount: > 0 } pv && ShipWorld(_fight.Target) is { } aim) pv.Hardpoints.AimAll(aim, delta);
			else if (_player.Visual is { Hardpoints.TurretCount: > 0 } sv && _fight.AtStation && _targetStation is { } ts && u.Stations.TryGetValue(ts, out var tst))
				sv.Hardpoints.AimAll(new Vector3(tst.Position.X, 0, tst.Position.Y), delta);
			else if (_player.Visual is { Hardpoints.TurretCount: > 0 } gv && _fight.AtGate && u.Gates.TryGetValue(_fight.GateTarget, out var tgt))
				gv.Hardpoints.AimAll(new Vector3(tgt.Position.X, 0, tgt.Position.Y), delta);   // Phase 31
		}
		_view.AimStationGuns(u, delta);
		// Phase 32: verankerte NPC-Schürfer zeigen mit dem Bug zum Fels; Trossen aller verankerten Schiffe im Sektor
		var sec = u.Sectors[u.PlayerSector];
		foreach (var id in u.Tracker.ParkedIn(u.PlayerSector))
			if (id != u.PlayerShip && u.Ships.TryGetValue(id, out var ns) && ns.AnchorRock != 0 && sec.Asteroids.FirstOrDefault(a => a.Id == ns.AnchorRock) is { } nr)
				_view.FaceTo(id, new Vector3(nr.Position.X, 0f, nr.Position.Y), delta);
		_anchors.Update(u, id => id == u.PlayerShip ? (_docked == null && !_transition ? (_player.Visual, (Node3D)_player) : null) : _view.BodyOf(id), delta);
	}

	/// <summary>Runde 12: Uhr des Musikreglers (Sekunden, Echtzeit).</summary>
	static double Now => Time.GetTicksMsec() / 1000.0;

	/// <summary>Runde 12: Signal abspielen; immer true (damit es in einer Fall-Bedingung des Ereignis-Schalters stehen kann).</summary>
	bool Sting(string key) { _music.Sting(key, Now); return true; }

	/// <summary>Runde 12: Musikzustand aus dem Core (Menü/Vollbild, angedockt, Abbau) und weich blenden.</summary>
	void UpdateMusic(float delta)
	{
		var u = _session.U;
		bool menu = _hud.Screens.Current != null && _hud.Screens.Current != _hud.Flight && _docked == null;
		bool mining = _miner.Enabled || _chunks.LastContact is ContactState.Mining or ContactState.Anchoring || _tractorOn;   // Phase 32: auch beim Verankern
		_music.Step(SpaceSim.Core.Audio.MusicState.Raw(u, new SpaceSim.Core.Audio.MusicContext(menu, _docked != null, mining)), Now, delta);
	}

	/// <summary>Runde 11: Weltposition eines Schiffs im Sektor (Spieler oder NPC-Marker).</summary>
	Vector3? ShipWorld(ShipId id) => id.Value == 0 ? null : id == _session.U.PlayerShip ? _player.Position : _view.ShipPosition(id);

	/// <summary>Runde 11: Mündung eines Slots des Spielerschiffs (ohne Modell/Stellung die Schiffsmitte).</summary>
	Vector3? PlayerMuzzle(int slot) => slot >= 0 && _player.Visual?.Hardpoints.Muzzle(slot) is { } p ? p : _player.Position;

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
	internal bool TestDocked => _docked != null;   // Runde 34: Prüfwerkzeug
	internal void TestUndock() => Undock();
	internal void TestSwitchShip(ShipId id) => SwitchShip(id);
	/// <summary>Phase 12: Flotte öffnen mit diesem Schiff gewählt.</summary>
	internal void TestFleetSelect(ShipId id) { _hud.CcFleet.Select(id); OpenCcArea(CommandAreas.Fleet); }
	/// <summary>Phase 12: Befehl über die Flotten-Oberfläche geben (wie ein Tipp auf Befehl und Ziel).</summary>
	internal bool TestFleetGive(ShipId id, ShipCommand cmd)
	{
		if (!_session.U.Ships.TryGetValue(id, out var s)) return false;
		TestFleetSelect(id);
		return _hud.CcFleet.Give(_session.U, s, cmd);
	}
	internal bool TestSave() { SaveNow(manual: false); return SaveService.Exists; }
	internal void TestMenu(string a) => OnMenu(a);
	internal void TestStartMining() => SetAutoMining(true);
	internal float TestCargoUnits => PlayerData.Cargo.Values.Sum();
	internal int TestSales => _sales;
	internal int TestHits => _hitCount;
	/// <summary>Phase 32: Selbsttest — Ankertrossen, letzter Abrissgrund, Werkzeugtaste.</summary>
	internal AnchorView TestAnchors => _anchors;
	/// <summary>Runde 17: lebende Universen unter den ab Index <paramref name="from"/> angehängten (Sitzungen einer Selbsttest-Stufe).</summary>
	internal int TestLiveUniverses(int from)
	{
		for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
		return AttachedUniverses.Skip(from).Count(w => w.IsAlive);
	}
	internal int TestAttachedCount => AttachedUniverses.Count;
	internal int TestLiveUniverses()
	{
		for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
		var alive = AttachedUniverses.Select((w, i) => w.IsAlive ? (ReferenceEquals(w.Target, _session.U) ? $"{AttachedLabels[i]}=aktuell" : AttachedLabels[i]) : "").Where(x => x.Length > 0);
		GD.Print($"[selftest] Universen lebendig je Index (von {AttachedUniverses.Count}): {string.Join(",", alive)}");
		return AttachedUniverses.Count(w => w.IsAlive);
	}
	internal string TestAnchorBroke => _anchorBroke;
	internal bool TestPinned => _player.Pin is not null;
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
	/// <summary>Phase 29: Werkzeugslot des aktiven Schiffs auf ein Modul setzen (Selbsttest: Laserstufen mit dem Mk1-Laser, danach zurück zum Bohrkopf).</summary>
	/// <summary>HUD-Neu (Prüfwerkzeug, Bilder): das aktive Schiff auf ein anderes Modell umrüsten (Slots, Fit, Werte) — z. B. den Leviathan mit zehn Waffen.</summary>
	internal void TestUseModel(string modelKey)
	{
		var u = _session.U;
		if (!u.ShipModels.TryGetValue(modelKey, out var m)) return;
		var ship = PlayerData;
		ship.ModelKey = m.Key;
		ship.Slots = u.ShipClasses[m.ClassKey].Slots.ToList();
		ship.Fit = m.Fit.ToList();
		ship.RecomputeStats(u);
		ship.CargoCapacity = ship.Stats.Cargo;
		ship.Hull = ship.MaxHull;
	}

	internal void TestSetTool(string module)
	{
		var ship = PlayerData;
		int tool = ship.Slots.FindIndex(sl => sl.Type == "tool");
		if (tool < 0) return;
		ship.Fit[tool] = module;
		float cap = ship.CargoCapacity;
		ship.RecomputeStats(_session.U);
		ship.CargoCapacity = cap;
	}

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
	// Phase 25: Spielerkampf
	internal CombatController TestFight => _fight;
	/// <summary>Zweiter Gerätetest Phase 28: Blickrichtung des Spielerschiffs als Peilung in Grad (wie FiringArc.Bearing).</summary>
	internal float TestHeadingDeg => Mathf.RadToDeg(_player.Heading);
	/// <summary>Zweiter Gerätetest Phase 28: Dock-Bereich schließen, zurück ins Dock-Menü.</summary>
	internal void TestCloseDockArea() => ShowDockMenu();
	internal void TestSetShipTarget(ShipId id) => _fight.SetTarget(id);
	/// <summary>Dreht das Spielerschiff zur Position (Bugwaffen tragen).</summary>
	internal void TestFace(Vec2 at) { var d = new Vector2(at.X - _player.Position.X, at.Y - _player.Position.Z); _player.Heading = Mathf.Atan2(-d.X, -d.Y); }
	/// <summary>„Alle Feuer“ wie ein Tipp auf die Taste.</summary>
	internal void TestAllFire() => FireReadyBays();
	/// <summary>Phase 26: Bergung (Selbsttest).</summary>
	internal SalvageController TestSalvage => _salvage;
	internal bool TestGrabSalvage() => GrabSalvage();
	internal void TestTractor(bool on) => _tractorOn = on;
	/// <summary>Gerätetest: Zeitraffer setzen/lesen (Selbsttest der automatischen Abschaltung bei Gefahr).</summary>
	internal void TestSetFast(bool on) => _fast = on;
	/// <summary>Runde 44: Sektoransicht neu aufbauen (Selbsttest, nach einer Test-Umstellung von Stationen).</summary>
	internal void TestRebuildView() => _view.Rebuild(_session.U);
	internal bool TestFast => _fast;
	/// <summary>Runde 40, Selbsttest: Schiff anhalten (Autopilot aus, Schub 0, Geschwindigkeit 0) — für positionsgenaue Tipp-Prüfungen (Randmarker).</summary>
	internal void TestStopShip() { _autopilot = false; _hud.Flight.SetAutopilot(false); SetAutoMining(false, announce: false); _player.Velocity = Vector3.Zero; }
	/// <summary>HUD-Neu, Selbsttest: zuletzt angeforderter Hinweis (auch wenn er wegen der Sperre nicht gezeigt wurde).</summary>
	internal string TestLastHint { get; private set; } = "";
	internal bool TestAutopilotOn => _autopilot;
	internal bool TestHasTarget => _target != null;
	internal IReadOnlyList<WeaponBay> TestBays => _bays;
	/// <summary>Runde 8: Selbsttest — die Sektoransicht (Stationsmodelle).</summary>
	internal SectorView TestView => _view;
	/// <summary>Phase 8: Setzt das Schiff in einen anderen Sektor (wie ein Sprung, ohne Gate) – Test des Lagerhauses in Gamma.</summary>
	internal void TestEnterSector(SectorId sector)
	{
		var u = _session.U;
		if (u.PlayerSector == sector) return;
		u.MoveShip(PlayerData, sector);
		u.PlayerSector = sector;
		_view.Rebuild(u);
		_mining.ResetNpc(); _combat.Reset(); _mining.ClearPlayer(); _chunks.Clear(); _mineTarget = null;
	}
	/// <summary>Phase 10: Sprung durch ein Gate des aktuellen Sektors auslösen (wie das Einfliegen ins Gate); false = abgewiesen.</summary>
	internal bool TestJumpThrough(GateId gate)
	{
		var before = _session.U.PlayerSector;
		_jumpCooldown = 0f;
		Jump(gate);
		return _session.U.PlayerSector != before;
	}
	/// <summary>Phase 11: Teleport an einen Punkt im aktuellen Sektor (Überfall-Test abseits der Stationen).</summary>
	internal void TestTeleportTo(Vec2 pos)
	{
		// Runde 44: ein laufender Ab-/Andock-Tween überschrieb sonst den Teleport (Selbsttest Stufe 108 nach dem Handel der Vorstufe)
		if (_playerTween is not null && _playerTween.IsValid() && _docked == null) { _playerTween.Kill(); _transition = false; _player.Scale = Vector3.One; _player.Visible = true; }
		_player.Position = new Vector3(pos.X, 0, pos.Y);
		_player.Velocity = Vector3.Zero;
		_player.GiveGrace(5f);
		PlayerData.Position = pos;
		CancelAutopilot();
		_camOffset = _orbit.Offset();
		SnapCam();
	}
	/// <summary>Phase 8: Teleport in Andocknähe einer bestimmten Station (auch Baustelle) und Andocken.</summary>
	internal void TestDockAt(StationId id) { TestEnterSector(_session.U.Stations[id].Sector); TestTeleportNear(id); Dock(id); }
	/// <summary>Runde 41 (Selbsttest): sichtbare Missions-Leuchtfeuer im aktuellen Sektor.</summary>
	internal int TestMissionBeacons => _missions.TestVisible;
	/// <summary>Runde 41 (Selbsttest): Texte der sichtbaren Missions-Leuchtfeuer.</summary>
	internal IReadOnlyList<string> TestMissionBeaconLabels => _missions.TestLabels;
	/// <summary>Phase 8: Teleport in Andocknähe einer bestimmten Station (Bildschirmfotos der Baustelle im Flug).</summary>
	internal void TestTeleportNear(StationId id)
	{
		var st = _session.U.Stations[id];
		var pos = st.Position + new Vec2(st.Radius + 20f, 0);
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
	/// <summary>Rückansicht auf Höhe/Abstand setzen (Selbsttest Himmel; ohne Speichern).</summary>
	internal void TestSetChase(float height, float back)
	{
		_orbit.SetChase(height, back);
		_camOffset = _orbit.Offset();
	}
	/// <summary>„Kamera ↺“ wie ein Tipp auf den Knopf (Selbsttest).</summary>
	internal void TestResetCam() => ResetCamera();
	internal void TestCameraAction(string action) => OnCommandAction(action);
	/// <summary>Schaltet Autopilot und Auto-Mining ab, damit Lenkungstests nur die Handeingabe messen.</summary>
	internal void TestStopAutomation() { CancelAutopilot(); if (_miner.Enabled) SetAutoMining(false); }
	internal CameraMode TestCamMode => _orbit.Mode;
	/// <summary>Runde 44: Höhe und Abstand der Rückansicht.</summary>
	internal (float Height, float Back) TestCamChase => (_orbit.ChaseHeight, _orbit.ChaseBack);
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
	/// <summary>Runde 43b: Blickrichtung der Kamera (Welt) — Selbsttest: Winkel vor/nach dem Lösen der Anker.</summary>
	internal Vector3 TestCamForward => -_cam.GlobalTransform.Basis.Z;
	/// <summary>Runde 43b: Abbauziel direkt setzen (Selbsttest: Target-Lock auf den verankerten Fels).</summary>
	internal void TestSetMineTarget(int rockId) => _mineTarget = _session.U.Sectors[_session.U.PlayerSector].Asteroids.FirstOrDefault(a => a.Id == rockId);
	/// <summary>Aktueller Ist-Offset der Kamera relativ zum Schiff (Selbsttest).</summary>
	internal Vector3 TestCamOffset => new(_camOffset.X, _camOffset.Y, _camOffset.Z);
	internal Camera3D TestCam => _cam;

	/// <summary>
	/// Blickrichtung der echten Kamera, solange die Rückansicht aktiv ist — der Himmel dreht und neigt sich dann mit (Planet bleibt bei jedem Kurs und
	/// jeder Höhe/Abstand oben vorn im Bild); sonst null (weltfester Himmel). Aus der Kamera selbst, damit der Himmel genau der geglätteten Kamera folgt.
	/// </summary>
	Vector3? ChaseSkyForward()
	{
		if (_orbit.Mode != CameraMode.Chase || _cam is null || !IsInstanceValid(_cam)) return null;
		return -_cam.GlobalBasis.Z;
	}
	internal Vector3 TestCamPos => _cam.Position;
	/// <summary>Weltposition des Spielerschiffs in der Ansicht (Selbsttest Himmel: Schiff außerhalb der Planetenscheibe).</summary>
	internal Vector3 TestShipViewPos => _player.Position;
	/// <summary>Bildschirm → Welt: Schnitt des Kamerastrahls durch diesen Pixel mit der XZ-Ebene (wie „Halten fliegt zum Zeiger“).</summary>
	internal Vector3? TestGroundPoint(Vector2 screen) => GroundPoint(screen);
	/// <summary>Welt → Bildschirm: projiziert einen Weltpunkt (Roundtrip-Prüfung mit gedrehter Kamera).</summary>
	internal Vector2 TestProject(Vector3 world) => _cam.UnprojectPosition(world);
}
