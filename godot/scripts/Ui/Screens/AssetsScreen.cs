using System.Collections.Generic;
using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Trading;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Besitz“ (UI.md Abschnitt 4): Kasse, Zahl Schiffe/Stationen und Gesamtwert aus echten
/// Daten (Spielerkasse + Fracht zu Basispreisen über <see cref="SectorEconomy.PlayerNetWorth"/>, klar beschriftet). Tabs Schiffe,
/// Stationen, Verträge, Lager. Schiffe: das Spielerschiff mit Standort, Auftrag, Hülle, Fracht (alles echt).
/// Tippen auf ein Schiff öffnet ein Detailfenster mit „In dieses Schiff wechseln“ (Phase 12: auch aus der Ferne, UI.md „Schiff wechseln“),
/// „Befehle“ (Flotte), „Heim schicken“ (Befehl Heimkehr) und „Verkaufen“ (Dienst „hangar“ der Station, an der es liegt); Liegegebühr und Wert echt.
/// </summary>
public sealed partial class AssetsScreen : CommandScreen, ISessionScreen
{
	public override string Id => "cc.assets";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { _tab = "ships"; _detailShip = null; _message = ""; }

	string _tab = "ships";
	ShipId? _detailShip;
	string _message = "";
	readonly KitConfirm _confirm = new();   // Phase 18: „Abwerfen“ erst beim zweiten Tipp

	/// <summary>„In dieses Schiff wechseln“ (Phase 12: auch aus der Ferne; Main führt <see cref="SpaceSim.Core.Simulation.PlayerFleet.SwitchRemote"/> aus).</summary>
	public event Action<ShipId>? SwitchRequested;
	/// <summary>„Befehle“: in die Flotte wechseln, dieses Schiff gewählt.</summary>
	public event Action<ShipId>? CommandsRequested;

	/// <summary>Test-Hook: Detailfenster eines Schiffs öffnen.</summary>
	public void TestOpenShipDetail(ShipId? id = null) { _detailShip = id ?? Ctx.Player.Id; Invalidate(); }

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var ship = Ctx.Player;
		int myShips = u.Ships.Values.Count(s => s.IsPlayer);
		int myStations = u.Stations.Values.Count(s => s.Owner == u.PlayerActor);   // Phase 9: Besitz über den Spielerakteur (Stationen hat er noch keine)
		float total = SectorEconomy.PlayerNetWorth(u);   // Runde 22: Spielerkasse + Verkaufswert und Fracht aller Spielerschiffe

		// ── Kopf: Kasse, Zahlen, Gesamtwert (echt) ──
		float x = 20, xstep = MathF.Min(200f, (InnerW(inner) - 40f) / 4f);   // Runde 35: vier Zahlen in der Breite der Fläche
		foreach (var (label, val, col) in new[] { ("Kasse", $"{(int)u.PlayerWallet.Credits} Cr", Orange), ("Schiffe", myShips.ToString(), White), ("Stationen", myStations.ToString(), White), ("Gesamtwert", $"≈ {total:0} Cr", White) })
		{
			At(inner, Text(val, 22, col), x, 8, xstep - 10, 28);
			At(inner, Text(label, 13, Dim), x, 38, xstep - 10, 18);
			x += xstep;
		}
		At(inner, Text("Kasse + Schiffe + Fracht", 11, new Color(Dim, 0.75f)), 20 + 3 * xstep, 58, xstep - 10, 16);

		// ── Tabs ──
		float tx = 12;
		foreach (var (id, label) in new[] { ("ships", "Schiffe"), ("stations", "Stationen"), ("contracts", "Verträge"), ("storage", "Lager") })
		{
			var b = At(inner, new KitButton(label, new Vector2(130, 40)) { FontSize = 17, State = _tab == id ? KitButtonState.On : KitButtonState.Normal }, tx, 76);
			string t = id;
			b.Pressed += () => { _tab = t; _detailShip = null; Invalidate(); };
			tx += 136;
		}

		// ── Inhalt je Tab ──
		switch (_tab)
		{
			case "ships": BuildShips(inner, u, ship, myShips); break;
			case "stations": EmptyTab(inner, "Du besitzt noch keine Stationen."); break;
			case "contracts": EmptyTab(inner, "Du besitzt noch keine Verträge."); break;
			case "storage": EmptyTab(inner, "Du besitzt noch kein Lager."); break;
		}

		// ── Detailfenster eines Schiffs (Overlay) ──
		if (_detailShip is { } did && u.Ships.TryGetValue(did, out var dship)) BuildShipDetail(inner, u, dship);
	}

	void BuildShips(Control inner, Universe u, Ship ship, int myShips)
	{
		var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 8);
		// Phase 12: eine Flotte passt nicht auf eine Seite — die Liste rollt
		float lw = InnerW(inner) - 24f;   // Runde 35: Liste in der Breite der Fläche
		var scroll = At(inner, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 12, 128, lw, InnerH(inner) - 154);
		scroll.AddChild(list);
		foreach (var s in u.Ships.Values.Where(s => s.IsPlayer).OrderBy(s => s.Id.Value))
		{
			bool active = s.Id == u.PlayerShip;
			var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
			row.AddThemeStyleboxOverride("panel", Box(active ? new Color(0.12f, 0.22f, 0.4f, 0.95f) : Card, active ? Cyan : Edge, 10, 2, 12));
			row.CustomMinimumSize = new Vector2(lw - 20f, 92);
			var s2 = s;
			UiKit.OnTap(row, () => { _detailShip = s2.Id; Invalidate(); });   // Runde 37: Wisch wählt nichts
			row.AddChild(new AssetRow(u, s2, active, Ctx.Docked, lw - 44f));
			list.AddChild(row);
		}
		if (myShips == 0) At(inner, Text("Du besitzt noch keine Schiffe.", 15, Dim), 24, 140, 600, 22);
		At(inner, Text("Tippen auf ein Schiff öffnet das Detailfenster.", 13, new Color(Dim, 0.75f)), 20, InnerH(inner) - 20, 600, 18);
	}

	static void EmptyTab(Control inner, string text)
	{
		At(inner, Text(text, 18, Dim), 24, 150, 700, 26);
		At(inner, Text(UiPlaceholders.Tag, 13, new Color(Dim, 0.6f)), 24, 184, 400, 20);
	}

	/// <summary>Phase 23: Zeile der Bordsysteme (Schild, Bank, Magazin, Leistungsbilanz, Verteilung) — nur Core-Werte.</summary>
	internal static string SystemsLine(Universe u, Ship s)
	{
		var parts = new List<string>();
		parts.Add(s.Stats.Shield > 0f ? $"Schild {s.ShieldNow:0}/{s.Stats.Shield:0}" : "kein Schild");
		if (s.Stats.PowerStorage > 0f) parts.Add($"Bank {s.Charge:0}/{s.Stats.PowerStorage:0}");
		if (s.Stats.AmmoCapacity > 0f) parts.Add($"Magazin {SpaceSim.Core.Combat.ShipSystems.Ammo(u, s):0}/{s.Stats.AmmoCapacity:0}");
		parts.Add($"Leistung {s.Stats.PowerDraw:0}/{s.Stats.PowerOutput:0}");
		parts.Add($"Verteilung {SpaceSim.Core.Combat.ShipSystems.ModeName(s.PowerMode)}");
		return string.Join(" · ", parts);
	}

	void BuildShipDetail(Control inner, Universe u, Ship s)
	{
		// abgedunkeltes Overlay + Fenster (wie prototypes/FinalUi2.cs „AssetDetail“)
		var dim = At(inner, new ColorRect { Color = new Color(0.01f, 0.02f, 0.05f, 0.55f), MouseFilter = MouseFilterEnum.Stop }, 0, 0, InnerW(inner), InnerH(inner));
		// Runde 35: das Fenster ist für 900 × 500 gebaut — auf schmaleren Flächen wird es als Ganzes passend verkleinert (nie unter die Größe bei Faktor 1, weil
		// die Schicht selbst um den UI-Faktor vergrößert ist) und mittig gelegt
		float ws = MathF.Min(1f, (InnerW(inner) - 24f) / 900f);   // nur nach der Breite; ist es höher als die Fläche, rollt der Bereich
		var w = CardBg(inner, (InnerW(inner) - 900f * ws) / 2f, 10, 900, 500, PanelSolid, Cyan, 16, 2);
		w.Scale = new Vector2(ws, ws);
		bool active = s.Id == u.PlayerShip;

		// Schiffskarte links
		At(w, new DockAreaScreen.ShipSketch { CustomMinimumSize = new Vector2(120, 100), Size = new Vector2(120, 100), MouseFilter = MouseFilterEnum.Ignore }, 30, 40);
		At(w, Text($"Schiff #{s.Id.Value}{(active ? " (aktiv)" : "")}", 30, White), 170, 24, 400, 36);
		At(w, Text($"{(u.ShipModels.TryGetValue(s.ModelKey, out var md) ? md.Name : "ohne Modell")} · Güteklasse {ShipQualityNames.Name(s.Quality)} · Besitzer {u.ActorName(s.Owner)}", 15, Dim), 170, 62, 600, 20);
		string ort = OrtText(u, s, active ? Ctx.Docked : s.DockedAt);
		At(w, Text(ort, 16, active ? Cyan : Orange), 170, 88, 500, 22);

		// Zustand (Phase 12: Hülle, Sprit echt; Phase 23: Schild, Speicherbank, Magazin und Leistung echt aus den Bordsystemen)
		At(w, Text("Zustand", 17, Cyan), 30, 160, 200, 22);
		Bar(w, 30, 188, "Hülle", s.Hull, s.MaxHull, s.Hull > s.MaxHull * 0.6f ? Green : Orange, $"{(int)s.Hull}");
		if (u.Rules.FuelActive)   // Phase 14: Tankanzeige nur mit Treibstoff
			Bar(w, 30, 216, "Sprit", s.TotalFuel, MathF.Max(1f, s.Stats.FuelCapacity), Cyan, $"{s.TotalFuel:0}/{s.Stats.FuelCapacity:0}");
		At(w, Text(SystemsLine(u, s), 14, Dim), 30, u.Rules.FuelActive ? 236 : 216, 440, 20);

		// Auftrag und Standort (echt)
		At(w, Text("Auftrag", 17, Cyan), 30, 256, 200, 22);
		At(w, Text(active ? "von dir gesteuert" : SpaceSim.Core.Simulation.Commands.Describe(u, s), 15, White), 30, 282, 560, 22);
		At(w, Text("Standort", 17, Cyan), 30, 316, 200, 22);
		At(w, Text(ort + (s.Transit != null ? " · im Transit" : ""), 15, White), 30, 342, 560, 22);
		float berth = SpaceSim.Core.Simulation.Hangar.BerthFeeNow(u, s);
		At(w, Text($"Liegegebühr {(berth > 0f ? $"{berth:0} Cr/Tag" : "keine")} · Materialwert ≈ {SpaceSim.Core.Simulation.Hangar.MaterialValue(u, s):0} Cr · Verkauf ≈ {SpaceSim.Core.Simulation.Hangar.SellPrice(u, s):0} Cr", 14, new Color(Dim, 0.9f)), 30, 376, 560, 20);
		if (_message.Length > 0) At(w, Text(_message, 14, Orange), 30, 400, 840, 20);

		// Phase 18: Fracht mit „Abwerfen“ (auch im Flug; Regel CargoJettison, zweiter Tipp bestätigt)
		BuildCargo(w, u, s);
		BuildDamage(w, u, s);

		// Knöpfe unten (Phase 12 echt): Befehle (Flotte), Heim schicken, Verkaufen (an der Station, an der es liegt), Wechseln (auch aus der Ferne)
		var bSwitch = At(w, new KitButton("In dieses Schiff wechseln", new Vector2(380, 58)) { FontSize = 19, AlignLeft = true, State = active ? KitButtonState.Disabled : KitButtonState.Ready }, 490, 428);
		if (active) bSwitch.Detail = "Bereits aktiv";
		else { var sid = s.Id; bSwitch.Pressed += () => SwitchRequested?.Invoke(sid); }
		var bCmd = At(w, new KitButton("Befehle", new Vector2(120, 48)) { FontSize = 16, AlignLeft = true, State = active ? KitButtonState.Disabled : KitButtonState.Normal }, 30, 428);
		if (!active) { var sid = s.Id; bCmd.Pressed += () => CommandsRequested?.Invoke(sid); }
		bool homeOk = !active && SpaceSim.Core.Simulation.Commands.Check(u, s, new ReturnCommand()) is null;
		var bHome = At(w, new KitButton("Heim schicken", new Vector2(160, 48)) { FontSize = 16, AlignLeft = true, State = homeOk ? KitButtonState.Normal : KitButtonState.Disabled }, 160, 428);
		if (homeOk) bHome.Pressed += () => { bool ok = SpaceSim.Core.Simulation.Commands.Give(u, s, new ReturnCommand(), out var m); _message = ok ? m : "Nicht möglich: " + m; Invalidate(); };
		var dockSt = u.Stations.GetValueOrDefault(s.DockedAt);
		var hangar = dockSt is null ? null : Ctx.Services.ForStation(u, dockSt).FirstOrDefault(x => x.Key == SpaceSim.Core.Services.HangarService.ServiceKey);
		bool sellOk = hangar is not null && SpaceSim.Core.Simulation.Hangar.SellIssue(u, dockSt!, s) is null;
		var bSell = At(w, new KitButton("Verkaufen", new Vector2(140, 48)) { FontSize = 16, AlignLeft = true, State = sellOk ? KitButtonState.Normal : KitButtonState.Disabled }, 330, 428);
		if (!sellOk) bSell.Detail = active ? "" : dockSt is null ? "nur angedockt" : "";
		else bSell.Pressed += () =>
		{
			var r = hangar!.Execute(u, dockSt!, Ctx.Player, new SpaceSim.Core.Services.ServiceRequest($"{SpaceSim.Core.Services.HangarService.SellShip}:{s.Id.Value}", default, 0f));
			_message = r.Message;
			if (r.Success) _detailShip = null;
			Invalidate();
		};
		var bClose = At(w, new KitButton("Schließen", new Vector2(150, 44)) { FontSize = 15 }, 730, 16);
		bClose.Pressed += () => { _detailShip = null; _message = ""; Invalidate(); };
	}

	/// <summary>
	/// Phase 18: Frachtliste des Schiffs (Ware, Menge, Annahme der Station, an der es liegt) mit „Abwerfen“ je Ware — der Ausweg für Reste, die keine
	/// Station nimmt; die Ware verschwindet ohne Erlös. Ausführung über die Core-Regel <see cref="CargoJettison"/> (kein Stationsdienst nötig, auch im Flug).
	/// </summary>
	/// <summary>
	/// Phase 26: Schadensliste (Module mit Zustand, ausgefallen, Brand und Löschchance) — reine Anzeige aus <see cref="SpaceSim.Core.Combat.DamageInfo"/>,
	/// rechts über der Fracht (höchstens drei Modulzeilen, dann „+n weitere“).
	/// </summary>
	void BuildDamage(Control w, Universe u, Ship s)
	{
		var d = SpaceSim.Core.Combat.DamageInfo.For(u, s);
		var hurt = d.Rows.Where(r => r.Condition < 0.999f || r.Burning).ToList();
		_testDamageRows = hurt.Count;
		string head = hurt.Count == 0 ? "Schäden: Module intakt" : $"Schäden: {hurt.Count} Modul(e)" + (d.Fires > 0 ? $" · {d.Fires} Brand · Löschen {d.ExtinguishChance * 100f:0} %/Runde" : "");
		At(w, Text(head, 14, hurt.Count == 0 ? Dim : d.Fires > 0 ? Red : Orange), 600, 64, 290, 20);
		for (int i = 0; i < hurt.Count && i < 3; i++)
			At(w, Text(i == 2 && hurt.Count > 3 ? $"+{hurt.Count - 2} weitere" : hurt[i].Text, 13, hurt[i].Failed || hurt[i].Burning ? Red : Orange), 600, 84 + i * 15, 290, 16);
	}
	int _testDamageRows;
	/// <summary>Phase 26 Test-Hook: beschädigte/brennende Module in der Schadensliste des offenen Schiffsdetails.</summary>
	public int TestDamageRows => _testDamageRows;

	void BuildCargo(Control w, Universe u, Ship s)
	{
		At(w, Text("Fracht", 17, Cyan), 600, 130, 260, 22);
		var cargo = s.Cargo.Where(kv => kv.Value > 0f).OrderBy(kv => kv.Key.Value).ToList();
		if (cargo.Count == 0) { At(w, Text("leer", 15, Dim), 600, 158, 260, 20); return; }
		var scroll = At(w, new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass }, 600, 156, 280, 236);
		var list = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		list.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(list);
		foreach (var (good, held) in cargo)
		{
			bool ammo = SpaceSim.Core.Combat.Rearm.AmmoGood(u) is { } ag && ag.Id == good && s.Stats.AmmoCapacity > 0f;   // Phase 25: Munition umladen
			var row = new Control { CustomMinimumSize = new Vector2(266, ammo ? 118 : 58), MouseFilter = MouseFilterEnum.Pass };
			string name = u.Goods.TryGetValue(good, out var g) ? g.Name : good.Value.ToString();
			At(row, Text(name, 15, White), 0, 4, 140, 22);
			At(row, Text($"{held:0.##} an Bord", 13, Dim), 0, 28, 140, 20);
			var b = At(row, new KitButton("Abwerfen", new Vector2(120, 56)) { FontSize = 16 }, 144, 1);
			var sid = s.Id; var gid = good;
			_confirm.Wire(b, $"drop:{sid.Value}:{gid.Value}", "Abwerfen", () => Jettison(sid, gid));
			if (ammo)
			{
				string? issue = SpaceSim.Core.Combat.Rearm.FromCargoIssue(u, s);
				var bl = At(row, new KitButton("Ins Magazin", new Vector2(120, 56)) { FontSize = 15, State = issue is null ? KitButtonState.Ready : KitButtonState.Disabled }, 144, 60);
				At(row, Text($"Magazin {s.AmmoLoaded:0}/{s.Stats.AmmoCapacity:0}", 13, Dim), 0, 78, 140, 20);
				if (issue is null) bl.Pressed += () => LoadAmmo(sid);
			}
			list.AddChild(row);
		}
	}

	void Jettison(ShipId id, GoodId good)
	{
		var u = Ctx.U;
		if (!u.Ships.TryGetValue(id, out var ship)) return;
		string name = u.Goods.TryGetValue(good, out var g) ? g.Name : good.Value.ToString();
		float n = CargoJettison.Jettison(u, ship, good, 0f);
		_message = n > 0f ? $"{n:0.##} {name} abgeworfen" : CargoJettison.Issue(u, ship, good) ?? "Nicht möglich";
		Invalidate();
	}

	/// <summary>Phase 25: Munition aus der Fracht ins Magazin umladen (auch im Flug; Core-Regel <see cref="SpaceSim.Core.Combat.Rearm.FromCargo"/>).</summary>
	public float LoadAmmo(ShipId id)
	{
		var u = Ctx.U;
		if (!u.Ships.TryGetValue(id, out var ship)) return 0f;
		string? issue = SpaceSim.Core.Combat.Rearm.FromCargoIssue(u, ship);
		float n = issue is null ? SpaceSim.Core.Combat.Rearm.FromCargo(u, ship) : 0f;
		_message = n > 0f ? $"{n:0} Munition ins Magazin umgeladen (Magazin {ship.AmmoLoaded:0}/{ship.Stats.AmmoCapacity:0})" : issue ?? "Nicht möglich";
		Invalidate();
		return n;
	}

	/// <summary>Phase 18 Test-Hook: ein Tipp auf „Abwerfen“ im Schiffsdetail (erster macht scharf, zweiter führt aus); liefert, ob ausgeführt wurde.</summary>
	public bool TestTapJettison(ShipId id, GoodId good)
	{
		if (!_confirm.Tap($"drop:{id.Value}:{good.Value}")) return false;
		Jettison(id, good);
		return true;
	}
	public string TestMessage => _message;

	/// <summary>Standort: angedockt an einer Station (aktives Schiff: laut Ansicht), unterwegs oder im Raum.</summary>
	/// <summary>
	/// Runde 21 (Handy-Test a09: „… · Fracht 428/500 m³ · Wert…“ abgeschnitten): die erste Fassung der Zeile unter dem Namen, die in 310 px passt —
	/// der Wert steht immer drin, Fracht und Koordinaten fallen zuerst weg (die Fracht zeigt der Balken rechts).
	/// </summary>
	internal static string RowDetail(Font font, Universe u, Ship s, string ort, float room = 310f)
	{
		string value = $"≈ {SectorEconomy.PlayerShipValue(u, s):0} Cr";   // Runde 22: Verkaufswert + Fracht (vorher nur Fracht: Fregatte „≈ 0 Cr“)
		string place = s.Transit is null && !ort.StartsWith("angedockt") && u.Sectors.TryGetValue(s.Sector, out var sec) ? sec.Name : ort;
		foreach (var t in new[] { $"{ort} · Fracht {s.UsedVolume(u):0.#}/{s.CargoCapacity:0} · Wert {value}", $"{ort} · Wert {value}", $"{place} · Fracht {s.UsedVolume(u):0}/{s.CargoCapacity:0} · Wert {value}", $"{place} · Wert {value}" })
			if (font.GetStringSize(t, HorizontalAlignment.Left, -1, 13).X <= room) return t;
		return $"{place} · Wert {value}";
	}

	/// <summary>
	/// Zustand unter dem Befehl einer Schiffszeile. Runde 22 (Handy-Test k8: „Folgt #100“ über „geparkt“): ein Schiff mit Folge-/Geleitbefehl ohne
	/// Liegeplatz ist „im Verband“ (auch während der Folgereise); „angedockt“ nur mit Liegeplatz.
	/// Runde 25 (Gerätetest: ein Schürfer mit Schürfbefehl stand als „geparkt“): der Zustand folgt dem Befehl und der Tätigkeit — „unterwegs nach …“,
	/// „schürft“, „handelt“ (angedockt mit Arbeitsbefehl „… · angedockt“), „hält Position“, „im Einsatz“; „geparkt“ nur ohne Befehl und ohne Reise.
	/// </summary>
	internal static string StateText(Ship s, bool active, bool activeDocked, Universe? u = null)
	{
		if (active) return s.Transit != null ? "im Transit" : activeDocked ? "angedockt" : "im Flug";
		string work = s.Command switch
		{
			SpaceSim.Core.Model.MineCommand => "schürft",
			SpaceSim.Core.Model.TradeCommand => "handelt",
			_ => "",
		};
		if (s.Transit is { } t)
		{
			string to = u is not null && u.Sectors.TryGetValue(t.ToSector, out var ts) ? $"unterwegs nach {ts.Name}" : "im Transit";
			return work.Length > 0 ? $"{work} · {to}" : s.Command is SpaceSim.Core.Model.FollowCommand or SpaceSim.Core.Model.EscortCommand ? $"im Verband · {to}" : to;
		}
		if (s.DockedAt.Value != 0) return work.Length > 0 ? $"{work} · angedockt" : "angedockt";
		if (work.Length > 0) return work;
		return s.Command switch
		{
			null => "geparkt",
			SpaceSim.Core.Model.FollowCommand or SpaceSim.Core.Model.EscortCommand => "im Verband",
			SpaceSim.Core.Model.HoldCommand => "hält Position",
			_ => "im Einsatz",
		};
	}

	static string OrtText(Universe u, Ship s, StationId? docked) =>
		s.Transit is { } t ? $"unterwegs nach {(u.Sectors.TryGetValue(t.ToSector, out var ts) ? ts.Name : "?")}"
		: docked is { Value: > 0 } d && u.Stations.TryGetValue(d, out var st) ? $"angedockt an {st.Name}"
		: $"{(u.Sectors.TryGetValue(s.Sector, out var sec) ? sec.Name : "?")} · ({s.Position.X:0}/{s.Position.Y:0})";

	static void Bar(Control parent, float x, float y, string name, float value, float max, Color fill, string text)
	{
		At(parent, Text(name, 14, Dim), x, y, 110, 18);
		var bar = new KitBar(170, 11, fill);
		bar.Set(value, fill, max);
		At(parent, bar, x + 110, y + 3, 170, 11);
		At(parent, Text(text, 14, White), x + 292, y, 200, 18);
	}

	/// <summary>Eine Schiffzeile: Skizze, Name, Standort, Auftrag, Hülle, Fracht (alles echt).</summary>
	sealed partial class AssetRow : Control
	{
		readonly Universe _u; readonly Ship _s; readonly bool _active; readonly StationId? _docked;
		readonly float _w;
		readonly bool _narrow;
		public AssetRow(Universe u, Ship s, bool active, StationId? docked, float width = 954f)
		{
			_u = u; _s = s; _active = active; _docked = docked;
			MouseFilter = MouseFilterEnum.Ignore;
			// Runde 35b (Prüfung: bei 1280×720 × 1,3 liefen Wert, Zustand, „AKTIV“ und „Fracht“ ineinander): unter 954 px dreizeilig — links Name/Ort/Befehl,
			// rechts eine feste Spalte mit Hülle, Fracht und „AKTIV“; alle Texte mit der Schrift eingepasst und an DrawnText gemeldet
			_w = MathF.Min(954f, width);
			_narrow = _w < 954f;
			CustomMinimumSize = new Vector2(_w, _narrow ? 76 : 68);
			Size = CustomMinimumSize;
		}

		public override void _Draw()
		{
			DrawnText.Clear(this);
			var font = ThemeDB.FallbackFont;
			var c = new Vector2(44, 36); float sc = 20f;
			var pts = new[]
			{
				c + new Vector2(0, -sc), c + new Vector2(sc * 0.55f, sc * 0.35f), c + new Vector2(0, sc * 0.55f), c + new Vector2(-sc * 0.55f, sc * 0.35f),
			};
			DrawColoredPolygon(pts, new Color(0.25f, 0.33f, 0.5f));
			var closed = new System.Collections.Generic.List<Vector2>(pts) { pts[0] };
			DrawPolyline(closed.ToArray(), Cyan, 1.5f, true);

			string ort = OrtText(_u, _s, _active ? _docked : _s.DockedAt);
			string model = _u.ShipModels.TryGetValue(_s.ModelKey, out var md) ? md.Name : "";
			string name = $"Schiff #{_s.Id.Value} {model}{(_s.Quality == ShipQuality.Standard ? "" : " · " + ShipQualityNames.Name(_s.Quality))}";
			string cmd = _active ? "von dir gesteuert" : SpaceSim.Core.Simulation.Commands.Describe(_u, _s);
			string state = StateText(_s, _active, _docked is not null, _u);
			string tag = _active ? "AKTIV" : "Tippen ▸";
			Color tagCol = _active ? Cyan : Dim;
			if (_narrow)
			{
				float right = _w - 196f;            // feste rechte Spalte: Beschriftung 44 + Balken 100 + Zahl 44
				float tw = right - 96f - 10f;
				UiKit.DrawFit(this, 0, font, new Vector2(96, 22), name, 18, tw, White, minSize: 13);
				UiKit.DrawFit(this, 1, font, new Vector2(96, 44), RowDetail(font, _u, _s, ort, tw), 13, tw, Dim);
				UiKit.DrawFit(this, 2, font, new Vector2(96, 66), $"{cmd} · {state}", 13, tw, White);
				DrawBars(font, right, 3);
				UiKit.DrawFit(this, 7, font, new Vector2(right, 68), tag, 14, 190f, tagCol, HorizontalAlignment.Right);
				return;
			}
			// breit wie bisher: jede Spalte auf ihre Breite eingepasst
			UiKit.DrawFit(this, 0, font, new Vector2(96, 26), name, 20, 310, White, minSize: 14);
			UiKit.DrawFit(this, 1, font, new Vector2(96, 48), RowDetail(font, _u, _s, ort), 13, 310, Dim);
			UiKit.DrawFit(this, 2, font, new Vector2(420, 26), cmd, 14, 230, White);
			UiKit.DrawFit(this, 3, font, new Vector2(420, 48), state, 12, 230, Dim);
			DrawBars(font, 660f, 4);
			UiKit.DrawFit(this, 8, font, new Vector2(870, 26), tag, 14, 74, tagCol, HorizontalAlignment.Right);
		}

		/// <summary>Hülle und Fracht als zwei Balkenzeilen ab <paramref name="x"/> (Beschriftung 44, Balken bis 100/120, Zahl dahinter).</summary>
		void DrawBars(Font font, float x, int slot)
		{
			float bw = _narrow ? 100f : 120f;
			UiKit.DrawFit(this, slot, font, new Vector2(x, 22), "Hülle", 12, 44, Dim);
			DrawRect(new Rect2(x + 46, 14, bw, 9), new Color(0, 0, 0, 0.5f));
			DrawRect(new Rect2(x + 46, 14, bw * Mathf.Clamp(_s.Hull / _s.MaxHull, 0f, 1f), 9), _s.Hull > 60f ? Green : Orange);
			UiKit.DrawFit(this, slot + 1, font, new Vector2(x + 52 + bw, 22), $"{(int)_s.Hull}", 13, 40, White);
			UiKit.DrawFit(this, slot + 2, font, new Vector2(x, 44), "Fracht", 12, 44, Dim);
			DrawRect(new Rect2(x + 46, 36, bw, 9), new Color(0, 0, 0, 0.5f));
			DrawRect(new Rect2(x + 46, 36, bw * Mathf.Clamp(_s.UsedVolume(_u) / _s.CargoCapacity, 0f, 1f), 9), Cyan);
			UiKit.DrawFit(this, slot + 3, font, new Vector2(x + 52 + bw, 44), $"{_s.UsedVolume(_u):0.#}", 13, 40, White);
		}
	}
}
