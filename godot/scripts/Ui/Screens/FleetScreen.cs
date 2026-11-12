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
public sealed partial class FleetScreen : CommandScreen
{
	public override string Id => "cc.fleet";

	/// <summary>Auto-Mining umschalten (echte Entsprechung: <see cref="MiningAutopilot"/> des Spielers in Main).</summary>
	public event Action<bool>? AutoMiningToggled;
	/// <summary>„Übernehmen“: der Spieler wechselt aus der Ferne in dieses Schiff (Main führt <see cref="PlayerFleet.SwitchRemote"/> aus).</summary>
	public event Action<ShipId>? SwitchRequested;

	ShipId? _sel;
	string? _pending;      // gewählter Befehl, der noch ein Ziel braucht
	string _message = "";
	bool _msgOk = true;
	int _radius = -2;      // −2 = noch nicht aus den Regeln gesetzt
	float _maxBuy, _reserve;
	int _enabledCommands;

	/// <summary>Wählt ein Schiff vor (Hangar „Befehle“, Besitz „Befehle“).</summary>
	public void Select(ShipId id) { _sel = id; _pending = null; _message = ""; }
	/// <summary>Für den Selbsttest: Zahl der freigegebenen Befehlsknöpfe des gewählten Schiffs.</summary>
	public int TestEnabledCommands => _enabledCommands;
	public string TestMessage => _message;
	/// <summary>Für Selbsttest/Bildschirmfotos: Befehl wählen (wie ein Tipp im Raster; Befehle mit Ziel öffnen die Zielwahl).</summary>
	public void TestChoose(string kind) { var u = Ctx.U; if (_sel is { } id && u.Ships.TryGetValue(id, out var s)) Choose(u, s, kind); }

	static readonly string[] Targeted = { CommandKinds.GoTo, CommandKinds.Patrol, CommandKinds.Guard, CommandKinds.Escort, CommandKinds.Follow, CommandKinds.Build };

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
		At(inner, Text($"Befehle {Commands.Commanded(u, u.PlayerActor)}/{capShips} · Verbände {Squadrons.Of(u, u.PlayerActor).Count}/{capSquads}", 13, Dim), 12, 32, 360, 18);
		var scroll = At(inner, new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 12, 56, 368, 456);
		var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(list);
		foreach (var s in ships)
		{
			bool sel = s.Id == ship.Id, active = s.Id == u.PlayerShip;
			var b = new KitButton($"#{s.Id.Value} {ModelName(u, s)}", new Vector2(352, 64)) { AlignLeft = true, FontSize = 16, State = sel ? KitButtonState.On : KitButtonState.Normal };
			string sq = Squadrons.Get(u, s) is { } q ? $"[{q.Name}{(q.Leader == s.Id ? ", Anführer" : "")}] " : "";
			b.Sub = active ? (Ctx.AutoMiningOn() ? "aktiv · Auto-Mining" : "aktiv · von dir gesteuert") : Shorten(sq + Commands.Describe(u, s), 46);
			b.Detail = Where(u, s);
			var id = s.Id;
			b.Pressed += () => { Select(id); Invalidate(); };
			list.AddChild(b);
		}

		// ── Rechts: Detail ──
		var det = CardBg(inner, 392, 6, 618, 506, Card);
		bool isActive = ship.Id == u.PlayerShip;
		At(det, Text($"#{ship.Id.Value} {ModelName(u, ship)}{(ship.IsRental ? " (Leihschiff)" : "")}", 20, White), 14, 6, 400, 26);
		At(det, Text(isActive ? "AKTIV – du steuerst selbst" : Commands.Describe(u, ship), 14, ship.Emergency ? Red : isActive ? Cyan : Orange), 14, 32, 590, 20);
		At(det, Text($"{Where(u, ship)} · Hülle {(int)ship.Hull}/{(int)ship.MaxHull} {(u.Rules.FuelActive ? $"· Tank {ship.TotalFuel:0}/{ship.Stats.FuelCapacity:0} " : "")}· Regel {CommandNames.Rule(Squadrons.RulesOf(u, ship))}", 13, Dim), 14, 54, 590, 18);
		if (!isActive)
		{
			var bSwitch = At(det, new KitButton("Übernehmen", new Vector2(150, 40)) { FontSize = 15, State = KitButtonState.Ready }, 454, 4);
			var sid2 = ship.Id;
			bSwitch.Pressed += () => SwitchRequested?.Invoke(sid2);
		}

		if (isActive)
		{
			At(det, Text("Das aktive Schiff bekommt keine Befehle – du fliegst es. Automatik:", 15, Dim), 14, 84, 590, 22);
			bool mining = Ctx.AutoMiningOn();
			var bMine = At(det, new KitButton("Auto-Mining", new Vector2(220, 56)) { FontSize = 17, State = mining ? KitButtonState.On : KitButtonState.Normal }, 14, 112);
			bMine.Pressed += () => AutoMiningToggled?.Invoke(!Ctx.AutoMiningOn());
			BuildSquadronRow(det, u, ship, 398, cancel: false);
			Message(det);
			return;
		}

		// Befehlsraster (aus der Registry, gesperrt nach Fähigkeit/Ziel); Touch-Ziele 56 px
		At(det, Text(_pending is null ? "Befehl geben" : $"{CommandNames.Kind(_pending)}: Ziel wählen (oder Befehl erneut tippen)", 15, Cyan), 14, 76, 590, 20);
		_enabledCommands = 0;
		var missing = new List<string>();
		int i = 0;
		foreach (var ex in OrderExecutors.Default.All)
		{
			string kind = ex.Kind;
			bool capable = ex.Needs == Capability.None || ship.Has(ex.Needs);
			bool ok = capable && (Targeted.Contains(kind) || Commands.Check(u, ship, Simple(u, ship, kind)!) is null);
			if (ok) _enabledCommands++;
			else if (!capable) missing.Add(CommandNames.Kind(kind));
			var b = At(det, new KitButton(CommandNames.Kind(kind), new Vector2(112, 56)) { FontSize = 15,
				State = !ok ? KitButtonState.Disabled : _pending == kind || ship.Command?.Kind == kind ? KitButtonState.On : KitButtonState.Normal }, 14 + (i % 5) * 118, 98 + (i / 5) * 60);
			if (ok) b.Pressed += () => Choose(u, ship, kind);
			i++;
		}

		// Zielwahl (Schritt 3) oder Grenzen der Automatik
		float ty = 222f;
		if (_pending is { } pk) BuildTargets(det, u, ship, pk, ty);
		else BuildLimits(det, u, ty);

		// Verhalten (Regeln des Verbands, wenn das Schiff in einem ist)
		At(det, Text("Verhalten", 14, Cyan), 14, 354, 86, 20);
		float rx = 104;
		var rules = Squadrons.RulesOf(u, ship);
		foreach (var e in new[] { Engagement.Passive, Engagement.Defend, Engagement.Attack, Engagement.HoldFire })
		{
			var b = At(det, new KitButton(CommandNames.Rule(e), new Vector2(124, 56)) { FontSize = 14, State = rules == e ? KitButtonState.On : KitButtonState.Normal }, rx, 336);
			var ee = e;
			b.Pressed += () => { SetRules(u, ship, ee); Invalidate(); };
			rx += 128;
		}
		BuildSquadronRow(det, u, ship, 398, cancel: true);
		if (_message.Length == 0 && missing.Count > 0) { _message = "Gesperrt, Fähigkeit fehlt: " + string.Join(", ", missing); _msgOk = true; Message(det, dim: true); _message = ""; }
		else Message(det);
	}

	void Message(Control det, bool dim = false)
	{
		if (_message.Length > 0) At(det, Text(_message, 13, dim ? new Color(Dim, 0.85f) : _msgOk ? Green : Red), 14, 482, 596, 20);
	}

	/// <summary>Befehl ohne Ziel (oder mit den gewählten Grenzen); null = braucht ein Ziel.</summary>
	ShipCommand? Simple(Universe u, Ship ship, string kind) => kind switch
	{
		CommandKinds.Hold => new HoldCommand(),
		CommandKinds.Return => new ReturnCommand(),
		CommandKinds.Trade => new TradeCommand(Limits(u, ship)),
		CommandKinds.Mine => new MineCommand(Limits(u, ship)),
		CommandKinds.Build => new BuildCommand(default),
		CommandKinds.GoTo => u.Stations.Values.Select(s => new GoToCommand(default, default, s.Id)).FirstOrDefault(),
		CommandKinds.Patrol => new PatrolCommand(new[] { ship.Sector }),
		CommandKinds.Guard => new GuardCommand(u.Stations.Keys.FirstOrDefault()),
		CommandKinds.Escort or CommandKinds.Follow => null,
		_ => null,
	};

	AutoLimits Limits(Universe u, Ship ship) => new(ship.Sector, _radius, _maxBuy, _reserve);

	void Choose(Universe u, Ship ship, string kind)
	{
		if (Targeted.Contains(kind)) { _pending = _pending == kind ? null : kind; _message = ""; Invalidate(); return; }
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
		return ok;
	}

	void Say(string msg, bool ok) { _message = msg; _msgOk = ok; Invalidate(); }

	void BuildTargets(Control det, Universe u, Ship ship, string kind, float y)
	{
		var options = new List<(string Label, ShipCommand Cmd)>();
		switch (kind)
		{
			case CommandKinds.GoTo:
			case CommandKinds.Guard:
				// Phase 21: nach Sprüngen vom Schiff aus (bei 11 Sektoren stehen die nahen Ziele vorn), dann Sektor und Id
				foreach (var st in u.Stations.Values.Where(s => !s.IsBuildSite).OrderBy(s => Hops(u, ship.Sector, s.Sector)).ThenBy(s => s.Sector.Value).ThenBy(s => s.Id.Value))
					options.Add((Shorten(st.Name, 18), kind == CommandKinds.GoTo ? new GoToCommand(default, default, st.Id) : new GuardCommand(st.Id)));
				break;
			case CommandKinds.Patrol:
				foreach (var sec in u.Sectors.Values.OrderBy(s => Hops(u, ship.Sector, s.Id)).ThenBy(s => s.Id.Value)) options.Add((sec.Name.Replace("Sektor ", ""), new PatrolCommand(new[] { sec.Id })));
				if (u.Sectors.Count > 1) options.Add(("Alle Sektoren", new PatrolCommand(u.Sectors.Keys.OrderBy(k => k.Value).ToList())));
				break;
			case CommandKinds.Escort:
			case CommandKinds.Follow:
				foreach (var other in PlayerFleet.Owned(u).Where(s => s.Id != ship.Id))
					options.Add(($"#{other.Id.Value} {ModelName(u, other)}", kind == CommandKinds.Escort ? new EscortCommand(other.Id) : new FollowCommand(other.Id)));
				break;
			case CommandKinds.Build:
				options.Add(("Nächste freie", new BuildCommand(default)));
				foreach (var site in u.Stations.Values.Where(s => s.Site is { Abandoned: false }).OrderBy(s => s.Id.Value))
					options.Add((Shorten(site.Name, 18), new BuildCommand(site.Id)));
				break;
		}
		var scroll = At(det, new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 14, y, 596, 110);
		var grid = new GridContainer { Columns = 4, MouseFilter = MouseFilterEnum.Ignore };
		grid.AddThemeConstantOverride("h_separation", 6);
		grid.AddThemeConstantOverride("v_separation", 6);
		scroll.AddChild(grid);
		foreach (var (label, cmd) in options)
		{
			bool ok = Commands.Check(u, ship, cmd) is null;
			var b = new KitButton(label, new Vector2(142, 56)) { FontSize = 13, State = ok ? KitButtonState.Normal : KitButtonState.Disabled };
			var c = cmd;
			if (ok) b.Pressed += () => Give(u, ship, c);
			grid.AddChild(b);
		}
		if (options.Count == 0) At(det, Text("Keine möglichen Ziele.", 14, Dim), 14, y, 400, 20);
	}

	/// <summary>Phase 21: Sprünge zwischen zwei Sektoren (physische Route; unerreichbar = ans Ende).</summary>
	static int Hops(Universe u, SectorId from, SectorId to) => from == to ? 0 : u.FindRoute(from, to)?.Count ?? int.MaxValue;

	void BuildLimits(Control det, Universe u, float y)
	{
		At(det, Text("Automatik (Handel/Schürfen): Grenzen ab dem aktuellen Sektor", 14, Cyan), 14, y, 590, 18);
		float x = 14;
		void Stepper(string label, string value, Action dec, Action inc)
		{
			At(det, Text(label, 13, Dim), x, y + 20, 190, 16);
			var m = At(det, new KitButton("−", new Vector2(56, 56)) { FontSize = 20 }, x, y + 38);
			m.Pressed += () => { dec(); Invalidate(); };
			At(det, Text(value, 15, White, HorizontalAlignment.Center), x + 56, y + 54, 72, 22);
			var p = At(det, new KitButton("+", new Vector2(56, 56)) { FontSize = 20 }, x + 128, y + 38);
			p.Pressed += () => { inc(); Invalidate(); };
			x += 198;
		}
		Stepper("Reichweite", _radius < 0 ? "alle" : $"{_radius} Spr.", () => _radius = Math.Max(-1, _radius - 1), () => _radius = Math.Min(6, _radius + 1));
		Stepper("max. Einkauf", _maxBuy <= 0f ? "frei" : $"{_maxBuy:0.0}×", () => _maxBuy = MathF.Max(0f, MathF.Round(_maxBuy - 0.1f, 1)), () => _maxBuy = MathF.Min(4f, MathF.Round(_maxBuy + 0.1f, 1)));
		Stepper("Rücklage", $"{_reserve:0} Cr", () => _reserve = MathF.Max(0f, _reserve - 100f), () => _reserve += 100f);
	}

	void BuildSquadronRow(Control det, Universe u, Ship ship, float y, bool cancel)
	{
		var sq = Squadrons.Get(u, ship);
		At(det, Text("Verband", 14, Cyan), 14, y + 8, 86, 18);
		At(det, Text(sq is null ? "keiner" : Shorten(sq.Name, 11), 11, Dim), 14, y + 28, 86, 16);
		float x = 104;
		void Btn(string label, Action act, bool enabled = true)
		{
			var b = At(det, new KitButton(label, new Vector2(124, 56)) { FontSize = 13, State = enabled ? KitButtonState.Normal : KitButtonState.Disabled }, x, y);
			if (enabled) b.Pressed += () => act();
			x += 128;
		}
		if (sq is null)
		{
			Btn("Bilden", () => { var q = Squadrons.Create(u, ship, "", out var m); Say(m, q is not null); });
			var join = Squadrons.Of(u, u.PlayerActor).FirstOrDefault(q => q.Members.Count < u.Rules.SquadronMaxMembers);
			Btn(join is null ? "Beitreten" : $"→ {Shorten(join.Name, 10)}", () => { bool ok = Squadrons.Join(u, join!, ship, out var m); Say(m, ok); }, join is not null && ship.Id != u.PlayerShip);
			x += 128;
		}
		else
		{
			Btn("Verlassen", () => { Squadrons.Leave(u, ship); Say("Verband verlassen.", true); });
			bool lead = sq.Leader == ship.Id;
			Btn($"Formation", () => { sq.Formation = (Formation)(((int)sq.Formation + 1) % 4); Say($"Formation {CommandNames.Formation(sq.Formation)} (nur Darstellung) · {sq.Members.Count}/{u.Rules.SquadronMaxMembers} Schiffe.", true); }, lead);
			Btn("Auflösen", () => { Squadrons.Disband(u, sq); Say("Verband aufgelöst.", true); }, lead);
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
