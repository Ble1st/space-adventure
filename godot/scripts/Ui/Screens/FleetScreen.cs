using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Flotte“ (UI.md Abschnitt 4, FLOTTEN.md Abschnitt 5; Phase 12 echt): links die eigenen Schiffe mit Befehl, Standort und Verband,
/// im Kopf die Befehlskapazität; rechts für das gewählte Schiff das Befehlsraster (aus der Registry <see cref="OrderExecutors"/>, gesperrt nach
/// Fähigkeit und Ziel), bei Befehlen mit Ziel die Zielwahl (höchstens drei Schritte: Schiff → Befehl → Ziel), die Grenzen der Automatikaufträge
/// (Reichweite, höchster Einkaufspreis, Rücklage), die Verhaltensregel und die Verbandsaktionen. Alles wirkt über die Core-API
/// (<see cref="Commands"/>, <see cref="Squadrons"/>); der Wechsel in ein Schiff geht an Main (<see cref="SwitchRequested"/>). Auto-Mining des
/// aktiven Schiffs bleibt ein Schalter der Ansicht.
/// </summary>
public sealed partial class FleetScreen : CommandScreen, ISessionScreen
{
	public override string Id => "cc.fleet";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { _sel = null; _pending = null; _message = ""; _msgOk = true; _radius = -2; _allowLoss = false; _group.Clear(); _deliverIdx = 0; }   // Runde 25: auch die Verlustsperre

	/// <summary>Auto-Mining umschalten (echte Entsprechung: <see cref="MiningAutopilot"/> des Spielers in Main).</summary>
	public event Action<bool>? AutoMiningToggled;
	/// <summary>„Übernehmen“: der Spieler wechselt aus der Ferne in dieses Schiff (Main führt <see cref="PlayerFleet.SwitchRemote"/> aus).</summary>
	public event Action<ShipId>? SwitchRequested;
	/// <summary>Kleines Schiffsmenü (Werte, Module, Inventar, Befehl, Verband).</summary>
	public event Action<ShipId>? MenuRequested;

	ShipId? _sel;
	string? _pending;      // gewählter Befehl, der noch ein Ziel braucht
	string _message = "";
	bool _msgOk = true;
	int _radius = -2;      // −2 = noch nicht aus den Regeln gesetzt
	float _maxBuy, _reserve;
	bool _allowLoss;   // Runde 24: Verlustkauf-Sperre aus
	/// <summary>Runde 24: Schalter der Verlustsperre (Selbsttest).</summary>
	public KitButton? TestLossGuardButton { get; private set; }
	/// <summary>Nachtrag 1c: Taste „Verlustschutz“ im Liefern-Bereich (Selbsttest).</summary>
	public KitButton? TestDeliverLossButton { get; private set; }
	int _enabledCommands;

	/// <summary>Wählt ein Schiff vor (Hangar „Befehle“, Besitz „Befehle“).</summary>
	public void Select(ShipId id) { _sel = id; _pending = null; _message = ""; _group.Clear(); }

	// Ausbau Punkt 1b: Gruppenwahl (Sammelpunkt, Konvoi) und Menge je Fahrt (Liefern)
	readonly HashSet<int> _group = new();
	int _deliverIdx;
	static readonly int[] DeliverAmounts = { 0, 10, 25, 50, 100, 200 };
	static bool IsGroupKind(string? k) => k is CommandKinds.Rally or CommandKinds.Convoy;
	/// <summary>Selbsttest: Schiff für Sammelpunkt/Konvoi mitnehmen (wie der Tipp auf „mit“).</summary>
	public void TestToggleGroup(ShipId id) { if (!_group.Add(id.Value)) _group.Remove(id.Value); Invalidate(); }
	/// <summary>Selbsttest: Zieloptionen der laufenden Zielwahl (Beschriftung).</summary>
	public IReadOnlyList<string> TestTargetLabels => _targetButtons.Select(b => b.Text).ToList();
	/// <summary>Selbsttest: Zieltaste der laufenden Zielwahl.</summary>
	public KitButton? TestTargetButton(int i) => i >= 0 && i < _targetButtons.Count && IsInstanceValid(_targetButtons[i]) ? _targetButtons[i] : null;
	/// <summary>Selbsttest: Befehlstaste im Raster.</summary>
	public KitButton? TestCommandButton(string kind) => _commandButtons.TryGetValue(kind, out var b) && IsInstanceValid(b) ? b : null;
	/// <summary>Selbsttest: „mit“-Taste einer Listenzeile.</summary>
	public KitButton? TestGroupButton(ShipId id) => _groupButtons.TryGetValue(id.Value, out var b) && IsInstanceValid(b) ? b : null;
	/// <summary>Selbsttest: „Konvoi bilden“.</summary>
	public KitButton? TestConvoyButton { get; private set; }
	readonly List<KitButton> _targetButtons = new();
	readonly Dictionary<string, KitButton> _commandButtons = new();
	readonly Dictionary<int, KitButton> _groupButtons = new();
	/// <summary>Für den Selbsttest: Zahl der freigegebenen Befehlsknöpfe des gewählten Schiffs.</summary>
	public int TestEnabledCommands => _enabledCommands;
	public string TestMessage => _message;
	/// <summary>Für Selbsttest/Bildschirmfotos: Befehl wählen (wie ein Tipp im Raster; Befehle mit Ziel öffnen die Zielwahl).</summary>
	public void TestChoose(string kind) { var u = Ctx.U; if (_sel is { } id && u.Ships.TryGetValue(id, out var s)) Choose(u, s, kind); }

	static readonly string[] Targeted = { CommandKinds.GoTo, CommandKinds.Patrol, CommandKinds.Guard, CommandKinds.Escort, CommandKinds.Follow, CommandKinds.Build,
		CommandKinds.Attack, CommandKinds.Pursue, CommandKinds.Salvage, CommandKinds.Blockade, CommandKinds.Deliver, CommandKinds.Rally, CommandKinds.Convoy, CommandKinds.ExploreSector };   // Ausbau Punkt 1/1b, 16

	/// <summary>Kurze Beschriftung der Befehlstaste (94 px).</summary>
	static string ButtonLabel(string kind) => kind switch
	{
		CommandKinds.Blockade => "Blockade",
		CommandKinds.Rally => "Sammeln",
		CommandKinds.Patrol => "Streife",
		CommandKinds.ExploreSector => "Erkunden",
		CommandKinds.Taxi => "Taxi",   // Ausbau Punkt 14   // Ausbau Punkt 16: Ziele „Universum“ und Sektoren
		_ => CommandNames.Kind(kind),
	};

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		if (_radius == -2) { _radius = u.Rules.AutoRadiusJumps; _maxBuy = u.Rules.AutoMaxBuyFactor; _reserve = u.Rules.AutoReserve; }
		var ships = PlayerFleet.Owned(u);
		var ship = _sel is { } sid && u.Ships.TryGetValue(sid, out var s0) && s0.IsPlayer ? s0 : ships.FirstOrDefault(s => s.Id != u.PlayerShip) ?? Ctx.Player;
		_sel = ship.Id;

		// ── Links: Liste und Kapazität ──
		var (capShips, capSquads) = Commands.Capacity(u, u.PlayerActor);
		At(inner, Text("Deine Schiffe", 18, Cyan), 12, 6, 200, 24);
		// Runde 25: über der Kapazität (z. B. nach Auflösen eines Verbands) rot mit Hinweis — laufende Befehle bleiben, neue sind gesperrt
		bool over = Commands.OverCapacity(u, u.PlayerActor) > 0;
		// Runde 26 (Gerätetest c03: „Befehle 0/4“ trotz zweier „Halten“ an Begleiter): gezählt werden Einzelbefehle; Schiffe im Verband zählen über den Verband
		At(inner, Text(KitButton.Fit(ThemeDB.FallbackFont, $"Einzelbefehle {Commands.Commanded(u, u.PlayerActor)}/{capShips}{(over ? " (zu viele)" : "")} · Verbände {Squadrons.Of(u, u.PlayerActor).Count}/{capSquads}{(Squadrons.Of(u, u.PlayerActor).Count > 0 ? " · Verband zählt als 1" : "")}", 13, 368), 13, over ? Red : Dim), 12, 32, Narrow(inner) ? InnerW(inner) - 24f : 368, 18);
		bool narrow = Narrow(inner);   // Runde 35: schmal — Liste oben, Detail darunter
		// Runde 35b (Prüfung: schmal stand eine große Lücke zwischen Liste und Detail): Listenhöhe nach Inhalt, höchstens 220, das Detail folgt direkt darunter
		float listH = narrow ? MathF.Min(220f, MathF.Max(1, ships.Count) * 70f - 6f) : 456f;
		_groupButtons.Clear();
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 12, 56, narrow ? InnerW(inner) - 24f : 368, listH);
		var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(list);
		// Runde 24 (Flottentest: Eskorte brauchte fünf Tipps): ist ein bewaffnetes Schiff gewählt, trägt jede andere Zeile „Begleiten“ — ein Tipp gibt die Eskorte
		bool canEscort = ship.Id != u.PlayerShip && ship.Has(Capability.Combat);
		foreach (var s in ships)
		{
			bool sel = s.Id == ship.Id, active = s.Id == u.PlayerShip;
			bool groupHere = IsGroupKind(_pending) && !sel && !active;   // Ausbau Punkt 1b: Gruppe für Sammelpunkt/Konvoi
			bool escortHere = canEscort && !sel && !groupHere;
			var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			row.AddThemeConstantOverride("separation", 4);
			var b = new KitButton($"#{s.Id.Value} {ModelName(u, s)}", new Vector2(escortHere || groupHere ? 228 : 352, 64)) { AlignLeft = true, FontSize = 16, State = sel ? KitButtonState.On : KitButtonState.Normal };
			string sq = Squadrons.Get(u, s) is { } q ? $"[{q.Name}{(q.Leader == s.Id ? ", Anführer" : "")}] " : "";
			b.Sub = active ? (Ctx.AutoMiningOn() ? "aktiv · Auto-Mining" : (s.Command is null ? "aktiv · von dir gesteuert" : Shorten("aktiv · " + Commands.Describe(u, s), 46))) : Shorten(sq + Commands.Describe(u, s), escortHere || groupHere ? 26 : 46);
			b.Detail = Where(u, s);
			var id = s.Id;
			b.Pressed += () => { Select(id); Invalidate(); };
			row.AddChild(b);
			if (groupHere)
			{
				bool inGroup = _group.Contains(s.Id.Value);
				var g = new KitButton(inGroup ? "✓ mit" : "mit", new Vector2(120, 64)) { FontSize = 15, Sub = inGroup ? "dabei" : "mitnehmen", State = inGroup ? KitButtonState.On : KitButtonState.Normal };
				g.Pressed += () => { if (!_group.Add(id.Value)) _group.Remove(id.Value); Invalidate(); };
				_groupButtons[id.Value] = g;
				row.AddChild(g);
			}
			if (escortHere)
			{
				// Runde 25 (Gerätetest: „Begleiten durch #x“ war missverständlich): wer wen begleitet, steht ausgeschrieben
				var guard = new KitButton($"Von #{ship.Id.Value}", new Vector2(120, 64)) { FontSize = 13, Sub = "begleiten lassen" };
				var protector = ship;
				guard.Pressed += () => Give(u, protector, new EscortCommand(id));
				row.AddChild(guard);
			}
			list.AddChild(row);
		}

		// ── Rechts: Detail ──
		var det = Narrow(inner) ? CardBg(inner, 12, 56 + listH + 10, MathF.Max(618f, InnerW(inner) - 24f), 506, Card) : CardBg(inner, 392, 6, 618, 506, Card);
		bool isActive = ship.Id == u.PlayerShip;
		At(det, Text(KitButton.Fit(ThemeDB.FallbackFont, $"#{ship.Id.Value} {ModelName(u, ship)}{(ship.IsRental ? " (Leihschiff)" : "")}", 20, 330), 20, White), 14, 6, 330, 26);
		var bMenu = At(det, new KitButton("Menü", new Vector2(90, 40)) { FontSize = 15, Sub = "" }, 356, 4);
		{ var mid = ship.Id; bMenu.Pressed += () => MenuRequested?.Invoke(mid); }
		// Runde 25 (Gerätetest d06: „Handel automatisch (…)“ lief unter „Übernehmen“): neben der Taste nur bis zu ihr, mit der Schrift gekürzt
		float descW = isActive ? 590f : 434f;
		At(det, Text(KitButton.Fit(ThemeDB.FallbackFont, isActive ? (u.PlayerOnFoot ? "AKTIV – du bist zu Fuß" : "AKTIV – du steuerst selbst") : Commands.Describe(u, ship), 14, descW), 14, ship.Emergency ? Red : isActive ? Cyan : Orange), 14, 32, descW, 20);
		At(det, Text($"{Where(u, ship)} · Hülle {(int)ship.Hull}/{(int)ship.MaxHull} {(u.Rules.FuelActive ? $"· Tank {ship.TotalFuel:0}/{ship.Stats.FuelCapacity:0} " : "")}· Regel {CommandNames.Rule(Squadrons.RulesOf(u, ship))}", 13, Dim), 14, 54, 590, 18);
		if (!isActive)
		{
			var bSwitch = At(det, new KitButton("Übernehmen", new Vector2(150, 40)) { FontSize = 15, State = KitButtonState.Ready }, 454, 4);
			var sid2 = ship.Id;
			bSwitch.Pressed += () => SwitchRequested?.Invoke(sid2);
		}

		if (isActive)
		{
			// Nutzerwunsch: Auch das Schiff, in dem man sitzt, bekommt Befehle wie ein Flottenschiff. An Bord pausiert der Befehl
			// (die Person hat das Steuer), nach dem Aussteigen (Station "Schiff verlassen", Taxi) oder Wechsel läuft er weiter. Zu Fuß führt er sofort.
			At(det, Text(u.PlayerOnFoot ? "Zu Fuß: Dein Schiff führt den Befehl wie ein Flottenschiff." : "Aktives Schiff: Befehl pausiert an Bord – nach dem Aussteigen (Station, Taxi) oder Wechsel läuft er weiter.", 14, Dim), 14, 76, 618, 36);
			var bMine = At(det, new KitButton("Auto-Mining", new Vector2(150, 40)) { FontSize = 15, State = Ctx.AutoMiningOn() ? KitButtonState.On : KitButtonState.Normal }, 454, 4);
			bMine.Pressed += () => AutoMiningToggled?.Invoke(!Ctx.AutoMiningOn());
		}

		// Befehlsraster (aus der Registry, gesperrt nach Fähigkeit/Ziel); Touch-Ziele 56 px. Ausbau Punkt 1/1b: sechs Spalten, Höhe der Karte nach Inhalt
		At(det, Text(_pending is null ? (isActive ? "Befehl geben (pausiert, solange du an Bord bist)" : "Befehl geben") : $"{CommandNames.Kind(_pending)}: Ziel wählen (oder Befehl erneut tippen)", 15, Cyan), 14, isActive ? 116 : 76, 590, 20);
		_enabledCommands = 0;
		_commandButtons.Clear();
		var missing = new List<string>();
		int i = 0;
		foreach (var ex in OrderExecutors.Default.All.Where(e => e.ForPlayer))   // Phase 27: Lauern nur für Piraten
		{
			string kind = ex.Kind;
			bool capable = ex.Needs == Capability.None || ship.Has(ex.Needs);
			bool ok = capable && (Targeted.Contains(kind) || Commands.Check(u, ship, Simple(u, ship, kind)!) is null);
			if (ok) _enabledCommands++;
			else if (!capable) missing.Add(CommandNames.Kind(kind));
			var b = At(det, new KitButton(ButtonLabel(kind), new Vector2(94, 56)) { FontSize = 14,
								State = !ok ? KitButtonState.Disabled : _pending == kind || ship.Command?.Kind == kind ? KitButtonState.On : KitButtonState.Normal }, 14 + (i % 6) * 98, (isActive ? 138f : 98f) + (i / 6) * 60);
			if (ok) b.Pressed += () => Choose(u, ship, kind);
			_commandButtons[kind] = b;
			i++;
		}

		// Zielwahl (Schritt 3) oder Grenzen der Automatik
		float ty = (isActive ? 138f : 98f) + (i + 5) / 6 * 60f + 4f;
		ty = _pending is { } pk ? BuildTargets(det, u, ship, pk, ty) : BuildLimits(det, u, ty);

		// Verhalten (Regeln des Verbands, wenn das Schiff in einem ist)
		At(det, Text("Verhalten", 14, Cyan), 14, ty + 18, 86, 20);
		float rx = 104;
		var rules = Squadrons.RulesOf(u, ship);
		foreach (var e in new[] { Engagement.Passive, Engagement.Defend, Engagement.Attack, Engagement.HoldFire })
		{
			var b = At(det, new KitButton(CommandNames.Rule(e), new Vector2(124, 56)) { FontSize = 14, State = rules == e ? KitButtonState.On : KitButtonState.Normal }, rx, ty);
			var ee = e;
			b.Pressed += () => { SetRules(u, ship, ee); Invalidate(); };
			rx += 128;
		}
		BuildSquadronRow(det, u, ship, ty + 62, cancel: true);
		_messageY = ty + 126;
		if (_message.Length == 0 && missing.Count > 0) { _message = "Gesperrt, Fähigkeit fehlt: " + string.Join(", ", missing); _msgOk = true; Message(det, dim: true); _message = ""; }
		else Message(det);
		FitCard(det, _messageY + 44f);
	}

	/// <summary>Ausbau Punkt 1/1b: Kartenhöhe nach Inhalt (der Bereich rollt als Ganzes, wenn sie über die Fläche reicht).</summary>
	static void FitCard(Control det, float h)
	{
		var size = new Vector2(det.Size.X, MathF.Max(det.Size.Y, h));
		det.CustomMinimumSize = size;
		det.Size = size;
	}

	float _messageY = 462f;

	// Runde 25 (Gerätetest: die Kapazitätsmeldung lief über das Zeilenende): bis zu zwei Zeilen, mit der Schrift gemessen umbrochen
	void Message(Control det, bool dim = false)
	{
		if (_message.Length == 0) return;
		var lines = Wrap(_message, 13, 596);
		_messageLines = lines.Count;
		if (lines.Count > 2) lines = new List<string> { lines[0], Shorten(string.Join(" ", lines.Skip(1)), 90) };
		for (int k = 0; k < lines.Count; k++) At(det, Text(lines[k], 13, dim ? new Color(Dim, 0.85f) : _msgOk ? Green : Red), 14, _messageY + k * 19, 596, 19);
	}

	/// <summary>Für den Selbsttest: Zeilen der letzten Meldung nach dem Umbruch (gemessen, vor dem Kürzen).</summary>
	public int TestMessageLines => _messageLines;
	int _messageLines;

	static string Join(string a, string b) => b.Length == 0 ? a : a + " " + b;

	/// <summary>Befehl ohne Ziel (oder mit den gewählten Grenzen); null = braucht ein Ziel.</summary>
	ShipCommand? Simple(Universe u, Ship ship, string kind) => kind switch
	{
		CommandKinds.Hold => new HoldCommand(),
		CommandKinds.Return => new ReturnCommand(),
		CommandKinds.Trade => new TradeCommand(Limits(u, ship)),
		CommandKinds.Mine => new MineCommand(Limits(u, ship)),
		CommandKinds.Taxi => new TaxiCommand(Limits(u, ship)),   // Ausbau Punkt 14
		CommandKinds.Build => new BuildCommand(default),
		CommandKinds.GoTo => u.Stations.Values.Select(s => new GoToCommand(default, default, s.Id)).FirstOrDefault(),
		CommandKinds.Patrol => new PatrolCommand(new[] { ship.Sector }),
		CommandKinds.Guard => new GuardCommand(u.Stations.Keys.FirstOrDefault()),
		CommandKinds.Escort or CommandKinds.Follow => null,
		_ => null,
	};

	AutoLimits Limits(Universe u, Ship ship) => new(ship.Sector, _radius, _maxBuy, _reserve, _allowLoss);

	void Choose(Universe u, Ship ship, string kind)
	{
		if (Targeted.Contains(kind)) { _pending = _pending == kind ? null : kind; _message = ""; _group.Clear(); Invalidate(); return; }
		Give(u, ship, Simple(u, ship, kind)!);
	}

	/// <summary>Gibt dem gewählten Schiff (bzw. seinem Verband, wenn es Anführer ist) einen Befehl über die Core-API.</summary>
	public bool Give(Universe u, Ship ship, ShipCommand cmd)
	{
		string msg;
		bool ok = Squadrons.Get(u, ship) is { } sq && sq.Leader == ship.Id && sq.Members.Count > 1
			? Squadrons.Command(u, sq, cmd, out msg)
			: Commands.Give(u, ship, cmd, out msg);
		_pending = null;
		Say(ok ? msg : "Nicht möglich: " + msg, ok);
		if (ok) Given?.Invoke(ship.Id, msg);   // Runde 45: Main startet den Befehlsstand, wenn das aktive Schiff (Person an Bord) den Befehl erhält
		return ok;
	}

	/// <summary>Runde 45 (Befehlsstand): Befehl wurde per Bildschirm gegeben (Schiff, Befehlstext).</summary>
	public event Action<SpaceSim.Core.Model.ShipId, string>? Given;

	void Say(string msg, bool ok) { _message = msg; _msgOk = ok; Invalidate(); }

	/// <summary>Zielwahl (Schritt 3). Liefert die Unterkante.</summary>
	float BuildTargets(Control det, Universe u, Ship ship, string kind, float y)
	{
		_targetButtons.Clear();
		TestConvoyButton = null;
		var options = new List<(string Label, string Sub, ShipCommand? Cmd)>();
		switch (kind)
		{
			case CommandKinds.GoTo:
			case CommandKinds.Guard:
				// Phase 21: nach Sprüngen vom Schiff aus (bei 11 Sektoren stehen die nahen Ziele vorn), dann Sektor und Id
				foreach (var st in u.Stations.Values.Where(s => !s.IsBuildSite).OrderBy(s => Hops(u, ship.Sector, s.Sector)).ThenBy(s => s.Sector.Value).ThenBy(s => s.Id.Value))
					options.Add((Shorten(st.Name, 18), "", kind == CommandKinds.GoTo ? new GoToCommand(default, default, st.Id) : new GuardCommand(st.Id)));
				break;
			case CommandKinds.Patrol:
				foreach (var sec in u.Sectors.Values.OrderBy(s => Hops(u, ship.Sector, s.Id)).ThenBy(s => s.Id.Value)) options.Add((sec.Name.Replace("Sektor ", ""), "", new PatrolCommand(new[] { sec.Id })));
				if (u.Sectors.Count > 1) options.Add(("Alle Sektoren", "", new PatrolCommand(u.Sectors.Keys.OrderBy(k => k.Value).ToList())));
				break;
			case CommandKinds.Escort:
			case CommandKinds.Follow:
				// Runde 24: Zieltexte unterscheidbar (Rolle bzw. Ort), nicht gleich wie die Schiffszeile links
				foreach (var other in PlayerFleet.Owned(u).Where(s => s.Id != ship.Id))
					options.Add(($"{ModelName(u, other)} #{other.Id.Value} · {(other.Id == u.PlayerShip ? "aktiv" : Shorten(Where(u, other), 12))}", "", kind == CommandKinds.Escort ? new EscortCommand(other.Id) : new FollowCommand(other.Id)));
				break;
			case CommandKinds.Build:
				options.Add(("Nächste freie", "", new BuildCommand(default)));
				foreach (var site in u.Stations.Values.Where(s => s.Site is { Abandoned: false }).OrderBy(s => s.Id.Value))
					options.Add((Shorten(site.Name, 18), "", new BuildCommand(site.Id)));
				break;
			// Ausbau Punkt 1/1b
			case CommandKinds.Attack: foreach (var t in FleetTargets.Attack(u, ship)) options.Add((t.Label, t.Sub, t.Command)); break;
			case CommandKinds.Pursue: foreach (var t in FleetTargets.Pursue(u, ship)) options.Add((t.Label, t.Sub, t.Command)); break;
			case CommandKinds.Salvage: foreach (var t in FleetTargets.Salvage(u, ship)) options.Add((t.Label, t.Sub, t.Command)); break;
			case CommandKinds.ExploreSector:   // Ausbau Punkt 16
				foreach (var t in FleetTargets.ExploreSector(u, ship)) options.Add((t.Label, t.Sub, t.Command));
				if (options.Count == 0) { At(det, Text("Nichts zu erkunden — alle erreichbaren Sektoren sind bekannt.", 14, Dim), 14, y + 4, 590, 20); return y + 30f; }
				break;
			case CommandKinds.Blockade:
				foreach (var t in FleetTargets.Blockade(u, ship)) options.Add((t.Label, t.Sub, t.Command));
				if (options.Count == 0) { At(det, Text("Tor blockieren geht nur im Krieg — kein Kriegsgegner.", 14, Dim), 14, y + 4, 590, 20); return y + 30f; }
				break;
			case CommandKinds.Deliver:
			{
				At(det, Text("Menge je Fahrt", 13, Dim), 14, y + 2, 132, 16);
				var m = At(det, new KitButton("−", new Vector2(56, 56)) { FontSize = 20 }, 150, y);
				m.Pressed += () => { _deliverIdx = Math.Max(0, _deliverIdx - 1); Invalidate(); };
				int amount = DeliverAmounts[_deliverIdx];
				At(det, Text(amount == 0 ? "voll" : $"{amount}", 15, White, HorizontalAlignment.Center), 206, y + 16, 60, 22);
				var p = At(det, new KitButton("+", new Vector2(56, 56)) { FontSize = 20 }, 266, y);
				p.Pressed += () => { _deliverIdx = Math.Min(DeliverAmounts.Length - 1, _deliverIdx + 1); Invalidate(); };
				// Nachtrag 1c: Verlustschutz wie beim Handel (Standard an) — dieselbe Taste/Einstellung wie die Automatikgrenzen
				var lossBtn = At(det, new KitButton(_allowLoss ? "Verlust erlaubt" : "Verlustschutz", new Vector2(136, 56)) { FontSize = 13, State = _allowLoss ? KitButtonState.Warn : KitButtonState.On, Sub = _allowLoss ? "Schutz aus" : "Schutz an" }, 332, y);
				lossBtn.Pressed += () => { _allowLoss = !_allowLoss; Invalidate(); };
				TestDeliverLossButton = lossBtn;
				At(det, Text(_maxBuy <= 0f ? "Einkauf frei" : $"Einkauf ≤ {_maxBuy:0.0}×", 13, Dim), 474, y + 20, 130, 18);
				y += 62f;
				foreach (var t in FleetTargets.Deliver(u, ship, amount, _maxBuy, _allowLoss)) options.Add((t.Label, t.Sub, t.Command));
				break;
			}
			case CommandKinds.Rally:
			{
				var group = GroupShips(u, ship);
				At(det, Text($"Links „mit“ tippen: {group.Count} Schiff{(group.Count == 1 ? "" : "e")} · dann Treffpunkt wählen", 13, Dim), 14, y + 2, 590, 18);
				y += 24f;
				foreach (var t in FleetTargets.Rally(u, ship, group)) options.Add((t.Label, t.Sub, t.Command));
				break;
			}
			case CommandKinds.Convoy:
			{
				var group = GroupShips(u, ship);
				int traders = group.Count(s => s.Has(Capability.Cargo)), escorts = group.Count - traders;
				At(det, Text($"Links „mit“ tippen: {traders} Händler, {escorts} Geleitschutz · Grenzen wie Handel", 13, Dim), 14, y + 2, 590, 18);
				var go = At(det, new KitButton("Konvoi bilden", new Vector2(190, 56)) { FontSize = 15, State = group.Count >= 2 ? KitButtonState.Ready : KitButtonState.Disabled, Sub = group.Count >= 2 ? "" : "mind. 2 Schiffe" }, 14, y + 24);
				if (group.Count >= 2) go.Pressed += () =>
				{
					bool ok = FleetFormations.StartConvoy(u, group, Limits(u, ship), out var msg);
					if (ok) { _pending = null; _group.Clear(); }
					Say(msg, ok);
				};
				TestConvoyButton = go;
				return y + 86f;
			}
		}
		var scroll = At(det, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 14, y, 596, 124);
		var grid = new GridContainer { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
		grid.AddThemeConstantOverride("h_separation", 6);
		grid.AddThemeConstantOverride("v_separation", 6);
		scroll.AddChild(grid);
		foreach (var (label, sub, cmd) in options)
		{
			bool ok = cmd is not null && (kind == CommandKinds.Rally ? GroupShips(u, ship).Count >= 2 : Commands.Check(u, ship, cmd) is null);
			var b = new KitButton(label, new Vector2(194, 56)) { FontSize = 13, Sub = sub, State = ok ? KitButtonState.Normal : KitButtonState.Disabled };
			var c = cmd;
			if (ok && c is RallyCommand rc && u.Stations.TryGetValue(rc.At, out var at))
				b.Pressed += () =>
				{
					var g = GroupShips(u, ship);
					bool done = FleetFormations.StartRally(u, ship, g.Where(s => s.Id != ship.Id), at, out var msg);
					if (done) { _pending = null; _group.Clear(); }
					Say(msg, done);
				};
			else if (ok) b.Pressed += () => Give(u, ship, c!);
			grid.AddChild(b);
			_targetButtons.Add(b);
		}
		if (options.Count == 0) At(det, Text("Keine möglichen Ziele.", 14, Dim), 14, y, 400, 20);
		return y + 130f;
	}

	/// <summary>Ausbau Punkt 1b: das gewählte Schiff und die mit „mit“ markierten (eigene, nicht aktive; Id-Reihenfolge nach dem gewählten).</summary>
	List<Ship> GroupShips(Universe u, Ship ship)
	{
		var list = new List<Ship> { ship };
		foreach (var id in _group.OrderBy(v => v))
			if (u.Ships.TryGetValue(new ShipId(id), out var s) && s.IsPlayer && s.Id != u.PlayerShip && s.Id != ship.Id) list.Add(s);
		return list;
	}

	/// <summary>Phase 21: Sprünge zwischen zwei Sektoren (physische Route; unerreichbar = ans Ende).</summary>
	static int Hops(Universe u, SectorId from, SectorId to) => from == to ? 0 : u.FindRoute(from, to)?.Count ?? int.MaxValue;

	float BuildLimits(Control det, Universe u, float y)
	{
		At(det, Text("Automatik (Handel/Schürfen): Grenzen ab dem aktuellen Sektor", 14, Cyan), 14, y, 590, 18);
		float x = 14;
		void Stepper(string label, string value, Action dec, Action inc)
		{
			At(det, Text(label, 13, Dim), x, y + 20, 190, 16);
			var m = At(det, new KitButton("−", new Vector2(56, 56)) { FontSize = 20 }, x, y + 38);
			m.Pressed += () => { dec(); Invalidate(); };
			At(det, Text(value, 14, White, HorizontalAlignment.Center), x + 56, y + 54, 54, 22);   // Runde 24: schmaler, Platz für die Verlustsperre
			var p = At(det, new KitButton("+", new Vector2(56, 56)) { FontSize = 20 }, x + 110, y + 38);
			p.Pressed += () => { inc(); Invalidate(); };
			x += 168;
		}
		Stepper("Reichweite", _radius < 0 ? "alle" : $"{_radius} Spr.", () => _radius = Math.Max(-1, _radius - 1), () => _radius = Math.Min(6, _radius + 1));
		Stepper("max. Einkauf", _maxBuy <= 0f ? "frei" : $"{_maxBuy:0.0}×", () => _maxBuy = MathF.Max(0f, MathF.Round(_maxBuy - 0.1f, 1)), () => _maxBuy = MathF.Min(4f, MathF.Round(_maxBuy + 0.1f, 1)));
		Stepper("Rücklage", $"{_reserve:0} Cr", () => _reserve = MathF.Max(0f, _reserve - 100f), () => _reserve += 100f);
		// Runde 24: Verlustkauf-Sperre (Standard an) — die Automatik kauft nicht, wenn der Einkauf über dem Preis des Abnehmers liegt
		At(det, Text("Verlustkauf", 13, Dim), 14 + 3 * 168, y + 20, 92, 16);
		var guard = At(det, new KitButton(_allowLoss ? "erlaubt" : "gesperrt", new Vector2(92, 56)) { FontSize = 14, State = _allowLoss ? KitButtonState.Warn : KitButtonState.On }, 14 + 3 * 168, y + 38);
		guard.Pressed += () => { _allowLoss = !_allowLoss; Invalidate(); };
		TestLossGuardButton = guard;
		return y + 100f;
	}

	void BuildSquadronRow(Control det, Universe u, Ship ship, float y, bool cancel)
	{
		var sq = Squadrons.Get(u, ship);
		At(det, Text("Verband", 14, Cyan), 14, y + 8, 86, 18);
		At(det, Text(sq is null ? "keiner" : Shorten(sq.Name, 11), 11, Dim), 14, y + 28, 86, 16);
		float x = 104;
		void Btn(string label, Action act, bool enabled = true, string? why = null)
		{
			// Runde 26 (Gerätetest: „Auflösen“ abgeblendet ohne Grund): eine abgeblendete Taste nennt den Grund in der Unterzeile
			var b = At(det, new KitButton(label, new Vector2(124, 56)) { FontSize = 13, State = enabled ? KitButtonState.Normal : KitButtonState.Disabled, Sub = enabled ? "" : why ?? "" }, x, y);
			if (enabled) b.Pressed += () => act();
			x += 128;
		}
		if (sq is null)
		{
			Btn("Bilden", () => { var q = Squadrons.Create(u, ship, "", out var m); Say(m, q is not null); });
			var join = Squadrons.Of(u, u.PlayerActor).FirstOrDefault(q => q.Members.Count < u.Rules.SquadronMaxMembers);
			Btn(join is null ? "Beitreten" : $"→ {Shorten(join.Name, 10)}", () => { bool ok = Squadrons.Join(u, join!, ship, out var m); Say(m, ok); }, join is not null && ship.Id != u.PlayerShip, join is null ? "kein Verband frei" : "aktives Schiff");
			x += 128;
		}
		else
		{
			Btn("Verlassen", () => { bool ok = Squadrons.Leave(u, ship, out var lm); Say(ok ? Join(lm, Commands.OverCapacityText(u, u.PlayerActor)) : lm, ok); });   // Runde 36: verweigert über der Kapazität
			bool lead = sq.Leader == ship.Id;
			Btn($"Formation", () => { sq.Formation = (Formation)(((int)sq.Formation + 1) % 4); Say($"Formation {CommandNames.Formation(sq.Formation)} (nur Darstellung) · {sq.Members.Count}/{u.Rules.SquadronMaxMembers} Schiffe.", true); }, lead, "nur Anführer");
			Btn("Auflösen", () => { bool ok = Squadrons.Disband(u, sq, out var dm); Say(ok ? Join(dm, Commands.OverCapacityText(u, u.PlayerActor)) : dm, ok); }, lead, "nur Anführer");
		}
		if (cancel)
		{
			var bCancel = At(det, new KitButton("Befehl beenden", new Vector2(124, 56)) { FontSize = 13, State = ship.Command is null ? KitButtonState.Disabled : KitButtonState.Normal }, x, y);
			if (ship.Command is not null) bCancel.Pressed += () => { Commands.Cancel(u, ship, "vom Spieler beendet"); Say("Befehl beendet – das Schiff hält die Position.", true); };
		}
	}

	static void SetRules(Universe u, Ship ship, Engagement e)
	{
		if (Squadrons.Get(u, ship) is { } sq) Squadrons.SetRules(u, sq, e); else ship.Engagement = e;
	}

	static string ModelName(Universe u, Ship s) => u.ShipModels.TryGetValue(s.ModelKey, out var m) ? m.Name : "ohne Modell";
	static string Shorten(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

	static string Where(Universe u, Ship s)
	{
		if (s.Transit is { } t) return $"unterwegs nach {SectorName(u, t.ToSector)}";
		if (u.Stations.TryGetValue(s.DockedAt, out var st)) return $"angedockt: {Shorten(st.Name, 22)}";
		return SectorName(u, s.Sector);
	}
}
