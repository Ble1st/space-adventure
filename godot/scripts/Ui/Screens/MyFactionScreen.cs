using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Meine Fraktion“ (UI.md Abschnitt 4), seit Phase 9 aus dem Spielerakteur (<see cref="Universe.Player"/>): Größenklasse und
/// Fortschritt zur nächsten Klasse (Größenzahl aus dem Besitz, <see cref="ActorRules"/>), eigene Schiffe und Stationen (Besitzer = du),
/// Mitgliedschaft, Kasse der Person und die Beziehungen der Fraktionen zu dir (<see cref="Relations.Effective"/>). Unterhalt, Liegegebühr,
/// Einnahmen und die Fähigkeiten mit Schloss sind weiter Platzhalter über <see cref="UiPlaceholders"/>; die Fahndung je Fraktion ist seit Phase 11 echt.
/// </summary>
public sealed partial class MyFactionScreen : CommandScreen
{
	public override string Id => "cc.myfaction";
	/// <summary>Selbsttest: zuletzt gezeigte Fahndungszeile (Phase 11).</summary>
	public string TestWanted { get; private set; } = "";

	/// <summary>Runde 25, für den Selbsttest: wurde die Zeile „Laufende Kosten“ gekürzt, und ihr Text.</summary>
	public bool TestCostsOverflow { get; private set; }
	/// <summary>Runde 26: eine Fähigkeitsnotiz, die nicht in ihre Spalte passte ("" = keine).</summary>
	public string TestAbilityOverflow { get; private set; } = "";
	public string TestCostsText { get; private set; } = "";
	/// <summary>Runde 38, Selbsttest: zuletzt gezeigte Einnahmen/Tag.</summary>
	public string TestIncomeText { get; private set; } = "";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U;
		var me = EconomyStats.Actors(u).First(a => a.Id == u.PlayerActor);
		var thresholds = u.Rules.ActorSizeThresholds;
		int ci = (int)me.Class;

		// ── Karte 1 (links oben): Du ──
		var a = CardBg(inner, 12, 12, 500, 250, Card);
		// Runde 38: der Spieler ist von Anbeginn eine eigene Fraktion („Deine Fraktion [SPL]“); ohne Regel weiter „Du“
		var ownFaction = u.PlayerFaction;
		At(a, Text(ownFaction is not null ? $"{PlayerFaction.DisplayName} {ActorNames.Tag(u, u.PlayerActor)}".Trim() : "Du", 26, White), 20, 14, 460, 34);
		At(a, Text($"{ActorNames.Size(me.Class)} · Größe {me.Size:0.0}", 15, Dim), 20, 52, 460, 22);
		string next = ci < thresholds.Count
			? $"Nächste Stufe {ActorNames.Size((ActorSizeClass)(ci + 1))} ab Größe {thresholds[ci]:0} (noch {thresholds[ci] - me.Size:0.0})"
			: "Höchste Stufe erreicht";
		At(a, Text(next, 15, Orange), 20, 80, 460, 22);
		float lo = ci == 0 ? 0f : thresholds[ci - 1], hi = ci < thresholds.Count ? thresholds[ci] : me.Size;
		float frac = hi > lo ? Mathf.Clamp((me.Size - lo) / (hi - lo), 0f, 1f) : 1f;
		At(a, new ColorRect { Color = new Color(0, 0, 0, 0.5f), CustomMinimumSize = new Vector2(460, 10), Size = new Vector2(460, 10), MouseFilter = MouseFilterEnum.Ignore }, 20, 108);
		At(a, new ColorRect { Color = Orange, CustomMinimumSize = new Vector2(460 * frac, 10), Size = new Vector2(460 * frac, 10), MouseFilter = MouseFilterEnum.Ignore }, 20, 108);
		WrappedText(a, $"Eigene Schiffe: {me.Ships} · eigene Stationen: {me.Stations} · Kasse {me.Treasury:0} Cr (Größe = Schiffe × {u.Rules.ActorShipWeight:0} + Stationen × {u.Rules.ActorStationWeight:0} + Kasse ÷ {u.Rules.ActorMoneyScale:0})", 20, 124, 460, 13, White, 18);
		if (ownFaction is { } own)
		{
			int sectors = u.Factions.TryGetValue(own, out var ofac) ? ofac.Territory.Count : 0;
			int wars = Wars.CountOf(u, own);
			WrappedText(a, $"Eigene Fraktion · Gebiet: {(sectors == 0 ? "keins" : $"{sectors} Sektor(en)")}" + (wars > 0 ? $" · Krieg mit {string.Join(", ", Wars.Enemies(u, own).Select(e => FactionName(u, e)))}" : " · im Frieden mit allen"),
				20, 178, 460, 14, wars > 0 ? Red : new Color(Dim, 0.95f), 19);
		}
		else
		{
			string membership = u.Player.Membership is { } m ? FactionName(u, m) : "keiner Fraktion (unabhängig)";
			WrappedText(a, $"Mitglied von: {membership}", 20, 178, 460, 14, new Color(Dim, 0.95f), 19);
		}
		// Phase 13: Forschungsstand der eigenen Fraktion (echt, ResearchInfo); ohne Mitgliedschaft der Hinweis zur Größe
		// Phase 14: mit abgeschalteter Forschung keine Forschungszeile
		if (u.Rules.ResearchActive && u.Player.Membership is { } mf && ResearchInfo.ForFaction(u, mf) is { Total: > 0 } rv)
			WrappedText(a, $"Forschung: {rv.Summary}", 20, 206, 460, 13, Cyan, 17);
		else
			WrappedText(a, "Weitere Schiffe, eine eigene Station oder mehr Geld heben die Stufe.", 20, 206, 460, 13, new Color(Dim, 0.75f), 17);

		// ── Karte 2 (rechts oben): Kasse der Person ──
		bool narrow = Narrow(inner);   // Runde 35: schmal — die vier Karten untereinander
		var b = narrow ? CardBg(inner, 12, 274, 486, 250, Card) : CardBg(inner, 524, 12, 486, 250, Card);
		At(b, Text("Kasse der Person", 18, Cyan), 20, 14, 446, 26);
		// Phase 5b: das Geld hängt an der Person (Universe.PlayerWallet), nicht am Schiff (POLITIK.md „Spielerkasse“)
		// Phase 26: negative Kasse = Schulden (laufende Leihmiete)
		At(b, Text(u.PlayerWallet.Credits < 0f ? $"Schulden: {(int)-u.PlayerWallet.Credits} Cr" : $"{(int)u.PlayerWallet.Credits} Cr", 26,
			u.PlayerWallet.Credits < 0f ? Red : Orange, HorizontalAlignment.Right), 20, 42, 446, 34);
		WrappedText(b, "Spielerkasse (echt): Geld der Person, nicht des Schiffs – Handel, Tanken und Reparatur buchen hierauf", 20, 80, 446, 12, new Color(Dim, 0.75f), 16);
		// Runde 16: echt — Flottenunterhalt der Schiffe mit Befehl (auch Verband) + Liegegebühr
		{ string costs = SpaceSim.Core.Simulation.Hangar.RunningCostsText(u, u.PlayerActor);
		  Row(b, 20, 126, 446, "Laufende Kosten", costs.Length > 0 ? costs : "keine", 14); TestCostsOverflow = LastRowOverflow; TestCostsText = costs; }
		Row(b, 20, 150, 446, "Liegegebühr/Tag (fremde St.)", $"−{SpaceSim.Core.Simulation.Hangar.BerthFeesPerDay(u, u.PlayerActor):0} Cr/Tag", 14);   // Phase 12 echt
		// Runde 38: echt — Verkäufe, Flottenhandel, Kopfgeld und Schiffsverkauf des letzten Spieltags (Ereignisse, SpaceSim.Core.Diagnostics.PlayerIncome)
		{ string income = Ctx.Income() is { } inc ? inc.Text() : "–";
		  Row(b, 20, 174, 446, "Einnahmen/Tag", income, 14); TestIncomeText = income; }
		WrappedText(b, "Einnahmen: Verkäufe, Flottenhandel, Kopfgeld und Schiffsverkauf im letzten Spieltag (10 min).", 20, 204, 446, 12, new Color(Dim, 0.75f), 16);

		// ── Karte 3 (links unten): Beziehungen der Fraktionen zu dir und Fahndung (echt) ──
		var rep = narrow ? CardBg(inner, 12, 536, 500, 234, Card) : CardBg(inner, 12, 274, 500, 234, Card);
		At(rep, Text("Beziehungen und Fahndung", 18, Cyan), 20, 14, 460, 26);
		// Phase 11: Fahndung je Hoheitsgebiet und offenes Kopfgeld (echt, SecurityInfo.ForPlayer)
		var wanted = SecurityInfo.ForPlayer(u);
		float claims = wanted.Sum(w => w.BountyClaim);
		string summary = wanted.All(w => w.Level == 0) ? "Fahndung: überall sauber" : "Fahndung: " + string.Join(", ", wanted.Where(w => w.Level > 0).Select(w => $"{w.FactionName} {w.LevelText}"));
		At(rep, Text(summary + (claims > 0f ? $" · Kopfgeld offen {claims:0} Cr" : ""), 13, wanted.Any(w => w.Level >= 3) ? Red : wanted.Any(w => w.Level > 0) ? Orange : Green), 20, 44, 460, 20);
		TestWanted = summary;
		float ry = 72;
		foreach (var f in u.NpcFactionIds().Select(id => u.Factions[id]).Take(4))   // Runde 38: nur KI-Fraktionen (die eigene steht oben)
		{
			float v = Relations.Effective(u, f.Id, u.PlayerActor);
			var w = wanted.FirstOrDefault(x => x.Faction == f.Id);
			At(rep, new ColorRect { Color = MapOverlay.FactionColor(f.Id), CustomMinimumSize = new Vector2(12, 12), Size = new Vector2(12, 12), MouseFilter = MouseFilterEnum.Ignore }, 22, ry + 3);
			At(rep, Text(f.Name, 15, White), 42, ry, 240, 20);
			At(rep, Text($"{Relations.Label(v)} ({(v > 0 ? "+" : "")}{v:0.00})", 14, v >= 0.15f ? Green : v <= -0.15f ? Red : White, HorizontalAlignment.Right), 280, ry, 200, 20);
			string wl = w.Level > 0 ? $"Fahndung {w.LevelText} ({w.Crime}) · Strafe {w.Fine:0} Cr" : "Fahndung: sauber";
			if (w.BountyClaim > 0f) wl += $" · Kopfgeld {w.BountyClaim:0} Cr";
			At(rep, Text(wl, 12, w.Level >= 3 ? Red : w.Level > 0 ? Orange : new Color(Dim, 0.85f)), 42, ry + 20, 440, 16);
			ry += 42;
		}

		// ── Karte 4 (rechts unten): Fähigkeiten mit Schloss ──
		var perm = narrow ? CardBg(inner, 12, 782, 486, 234, Card) : CardBg(inner, 524, 274, 486, 234, Card);
		At(perm, Text("Fähigkeiten nach Größe", 18, Cyan), 20, 14, 260, 26);
		var (capShips, capSquads) = SpaceSim.Core.Simulation.Commands.Capacity(u, u.PlayerActor);
		float ay = 48;
		// Phase 12 echt: Befehlskapazität und Verbände nach Größe, Taxi/Hangar/Leihschiff für alle; der Rest bleibt Platzhalter
		var abilities = new[]
		{
			// Runde 24: Befehlsstufe (eigene Schiffe zählen wenig; Stationen, Sektoren, Geld heben sie)
			("Befehlsstufe", true, SpaceSim.Core.Model.ActorNames.Size(SpaceSim.Core.Simulation.Commands.CapacityClass(u, u.PlayerActor))),
			("Befehle an eigene Schiffe", true, $"bis {SpaceSim.Core.Simulation.Commands.Count(capShips, "Schiff", "Schiffe")}"),
			("Verbände führen", capSquads > 0, $"bis {SpaceSim.Core.Simulation.Commands.Count(capSquads, "Verband", "Verbände")}"),
			("Taxi, Hangar, Leihschiff", true, "an Stationen mit Dienst"),
			// Runde 45: echt — eigener Sektor über eine Sektorbehörde (Karte „Sektorpolitik“)
			("Zoll und Sperren", PlayerSeats.OwnSectors(u).Count > 0, PlayerSeats.OwnSectors(u).Count > 0 ? (PlayerHq.Current(u) is not null ? "eigener Sektor, HQ (Stufe 1–5)" : "eigener Sektor (Stufe 1–2)") : "Sektorbehörde gründen"),
			// Ausbau Punkt 9: echt — Firmensitz (Befehlsstufe +1), Fehde (Hook für Punkt 6), Büros in fremden Sektoren
			("Firmensitz", PlayerSeatsOffices.Seat(u) is not null, PlayerSeatsOffices.Seat(u) is not null ? $"Befehlsstufe +{u.Rules.FirmSeatCommandBonus}" : $"ab {ActorNames.Size((ActorSizeClass)u.Rules.PlayerFirmSeatMinClass)} · Karte unten"),
			("Fehde erklären", PlayerSeatsOffices.FeudUnlocked(u), PlayerSeatsOffices.FeudUnlocked(u) ? (u.PlayerFaction is { } pfd && Feuds.Enemies(u, pfd).Any() ? "läuft · Comms" : "freigeschaltet · Comms") : "mit Firmensitz"),
			("Büros (fremde Sektoren)", Offices.Of(u, u.PlayerActor).Count > 0, Offices.Of(u, u.PlayerActor).Count is var no and > 0 ? $"{no} Büro{(no == 1 ? "" : "s")}" : $"ab {ActorNames.Size((ActorSizeClass)u.Rules.PlayerOfficeMinClass)} · Zustimmung"),
		};
		// Gerätetest: Zeilenabstand passt sich der Kartenhöhe an (die letzten Zeilen liefen über den Rand); Notiz rechts mit Innenabstand
		var list = abilities.ToList();
		TestAbilityOverflow = "";
		float step = MathF.Min(30f, (perm.Size.Y - ay - 12f) / MathF.Max(1, list.Count));
		foreach (var (name, unlocked, note) in list)
		{
			At(perm, Text(unlocked ? "✓" : "🔒", 15, unlocked ? Green : Dim), 20, ay, 24, 20);
			At(perm, Text(name, 15, unlocked ? White : Dim), 48, ay, 230, 20);
			At(perm, Text(KitButton.Fit(ThemeDB.FallbackFont, note, 12, 168), 12, new Color(Dim, 0.8f), HorizontalAlignment.Right), 282, ay, 172, 20);
			if (ThemeDB.FallbackFont.GetStringSize(note, HorizontalAlignment.Left, -1, 12).X > 168) TestAbilityOverflow = note;
			ay += step;
		}

		// ── Karte 5 (unten, ganze Breite): Sektorpolitik — Runde 45: Sektorbehörde gründen und eigene Sperren setzen (nur über die Comms-Registry) ──
		BuildPolicyCard(inner, u, narrow ? 1028f : 520f, narrow ? 486f : 998f);
	}

	readonly KitConfirm _confirm = new();
	string _policyMsg = "";
	/// <summary>Runde 45, Selbsttest: Zeile der Sektorpolitik (eigene Sektoren oder Grund, warum keine Behörde möglich ist).</summary>
	public string TestPolicyLine { get; private set; } = "";
	/// <summary>Runde 45, Selbsttest: letzte Meldung einer Sektorpolitik-Aktion.</summary>
	public string TestPolicyMessage => _policyMsg;
	/// <summary>Ausgleich, Selbsttest: Zeile der unbezahlten Regierungsstationen (leer = alles bezahlt).</summary>
	public string TestUnpaidLine = "";
	/// <summary>Eigenes Hauptquartier, Selbsttest: Zeile des Hauptquartiers (Name und Unterhalt, oder Vorschau bzw. Grund).</summary>
	public string TestHqLine { get; private set; } = "";
	/// <summary>Ausbau Punkt 9, Selbsttest: Zeile des Firmensitzes (Name oder Vorschau bzw. Grund) und der Büros.</summary>
	public string TestFirmSeatLine { get; private set; } = "";
	public string TestOfficeLine { get; private set; } = "";

	void PolicyButton(Control card, string key, string label, float x, float y, float w, bool confirm)
	{
		var offer = Ctx.Comms.Preview(Ctx.U, key);
		string k = key;
		KitButton b = confirm
			? _confirm.Wire(new KitButton(label, new Vector2(w, 56)) { FontSize = 15 }, "policy:" + key, label, () => Run(k))
			: new KitButton(label, new Vector2(w, 56)) { FontSize = 15 };
		if (!offer.Possible) b.State = KitButtonState.Disabled;
		else if (!confirm) b.Pressed += () => Run(k);
		At(card, b, x, y);
	}

	void Run(string key)
	{
		var r = Ctx.Comms.Execute(Ctx.U, key);
		_policyMsg = r.Message;
		Invalidate();
	}

	void BuildPolicyCard(Control inner, Universe u, float top, float width)
	{
		if (u.PlayerFaction is null || !u.Rules.PlayerSeats) { TestPolicyLine = ""; return; }
		var own = PlayerSeats.OwnSectors(u);
		float h = own.Count == 0 ? 200f : 96f + 150f * own.Count;
		var card = CardBg(inner, 12, top, width, h, Card);
		At(card, Text("Sektorpolitik – Behörde und Sperren", 18, Cyan), 20, 14, width - 40, 26);
		float y = 46;
		if (own.Count == 0)
		{
			var ship = u.Ships.TryGetValue(u.PlayerShip, out var ps) ? ps : null;
			string key = ship is null ? "seat:found:0" : $"seat:found:{ship.Sector.Value}";
			var offer = Ctx.Comms.Preview(u, key);
			TestPolicyLine = offer.Possible ? offer.Preview : offer.Reason ?? "";
			float ph = WrappedText(card, "Kein eigener Sektor. Eine Sektorbehörde in einem herrenlosen Sektor macht ihn deiner (Dominanz: Zoll, Sperrstufe 1–2). " + TestPolicyLine,
				20, y, width - 40, 13, offer.Possible ? new Color(Dim, 0.95f) : Orange, 18);
			y = ph + 6;
			PolicyButton(card, key, "Sektorbehörde gründen", 20, y, MathF.Min(320f, width - 40), confirm: true);
			y += 64;
		}
		else
		{
			var lines = new System.Collections.Generic.List<string>();
			foreach (var sec in own)
			{
				var acc = sec.Access;
				string line = $"{sec.Name} · {AccessNames.Stage(sec.Control)} · Einfluss {(sec.Influence.TryGetValue(u.PlayerFaction!.Value, out var infl) ? infl : 0f):P0} · "
					+ (acc.EntryLevel == 0 ? "offen" : $"Stufe {acc.EntryLevel} ({AccessNames.Level(acc.EntryLevel)}){(acc.Enforced ? "" : " – nur auf dem Papier")}, endet in {SpaceSim.Core.Comms.Comms.In(u, acc.ExpiresTick)}");
				lines.Add(line);
				At(card, Text(line, 14, White), 20, y, width - 40, 20);
				y += 26;
				int cap = SectorControl.MaxLevel(u, sec);
				var seat = PlayerSeats.Seats(u).FirstOrDefault(s => s.Sector == sec.Id);
				// Eigenes Hauptquartier: mit Kontrolle Stufen bis 5 — Stufenknöpfe 0 bis max(2, Deckel), „Schließen“ der Behörde dahinter
				int topLv = Math.Max(2, cap);
				bool oneRow = topLv <= 2;   // Runde-45-Anordnung: drei Stufen und „Schließen“ in einer Zeile
				int nb = oneRow ? 4 : topLv + 1;
				float bw = MathF.Min(140f, (width - 40 - (nb - 1) * 8) / nb);
				bool shortLabels = bw < 110f;
				for (int lv = 0; lv <= topLv; lv++)
					PolicyButton(card, $"lock:{sec.Id.Value}:{lv}", lv == 0 ? (shortLabels ? "Offen" : "Aufheben") : shortLabels ? $"{lv}" : $"Stufe {lv}", 20 + lv * (bw + 8), y, bw, confirm: false);
				if (!oneRow) y += 62;
				if (seat is not null) PolicyButton(card, $"seat:close:{seat.Id.Value}", oneRow ? "Schließen" : "Behörde schließen", oneRow ? 20 + 3 * (bw + 8) : 20, y, oneRow ? bw : MathF.Min(260f, width - 40), confirm: true);
				if (oneRow || seat is not null) y += 62;
				string why = cap < 3 ? $"Höchstens Stufe {cap} ({AccessNames.Stage(sec.Control)}); Stufe 3–5 brauchen Patrouillenabdeckung (eigenes Hauptquartier)." : "";
				var pre = Ctx.Comms.Preview(u, $"lock:{sec.Id.Value}:{Math.Min(cap, Math.Max(1, acc.EntryLevel))}");
				y = WrappedText(card, (pre.Possible ? pre.Preview : pre.Reason ?? "") + (why.Length > 0 ? " " + why : ""), 20, y, width - 40, 12, new Color(Dim, 0.9f), 16) + 10;
			}
			TestPolicyLine = string.Join(" | ", lines);
		}
		// Eigenes Hauptquartier (POLITIK.md „Eigenes Hauptquartier“): gründen (zwei Tipps) oder aufgeben, alles über die Comms-Registry („hq:…“)
		TestHqLine = "";
		if (u.Rules.PlayerHq)
		{
			At(card, Text("Hauptquartier", 16, Cyan), 20, y, width - 40, 22);
			y += 26;
			var hqs = PlayerHq.All(u);
			if (hqs.Count > 0)
			{
				foreach (var hq in hqs)
				{
					float up = u.StationTypes.TryGetValue(hq.TypeKey, out var ht) ? PlayerHq.UpkeepPerMinute(u, ht) : 0f;
					TestHqLine = $"{hq.Name} ({u.Sectors[hq.Sector].Name}) · Unterhalt {up:0} Cr/min aus deiner Kasse";
					y = WrappedText(card, TestHqLine, 20, y, width - 40, 14, White, 19) + 6;
					PolicyButton(card, $"hq:close:{hq.Id.Value}", "Hauptquartier aufgeben", 20, y, MathF.Min(320f, width - 40), confirm: true);
					y += 64;
				}
			}
			else
			{
				var ship = u.Ships.TryGetValue(u.PlayerShip, out var ps) ? ps : null;
				string key = ship is null ? "hq:found:0" : $"hq:found:{ship.Sector.Value}";
				var offer = Ctx.Comms.Preview(u, key);
				TestHqLine = offer.Possible ? offer.Preview : offer.Reason ?? "";
				var t = PlayerHq.HqType(u);
				string cond = t is null ? "" : $"Bedingungen: eigener Sektor, dein Schiff dort, Größenklasse {ActorNames.Size((ActorSizeClass)u.Rules.PlayerHqMinClass)}, {PlayerHq.Cost(u, t):0} Cr. ";
				y = WrappedText(card, cond + TestHqLine, 20, y, width - 40, 13, offer.Possible ? new Color(Dim, 0.95f) : Orange, 18) + 6;
				PolicyButton(card, key, "Hauptquartier gründen", 20, y, MathF.Min(320f, width - 40), confirm: true);
				y += 64;
			}
		}
		// Ausbau Punkt 9: Firmensitz (Befehlsstufe +1, Fehden) und Büros in fremden Sektoren — gründen/aufgeben über die Comms-Registry („firmseat:…“, „office:…“)
		TestFirmSeatLine = ""; TestOfficeLine = "";
		var shipNow = u.Ships.TryGetValue(u.PlayerShip, out var psNow) ? psNow : null;
		if (u.Rules.PlayerFirmSeat)
		{
			At(card, Text("Firmensitz", 16, Cyan), 20, y, width - 40, 22);
			y += 26;
			if (PlayerSeatsOffices.Seat(u) is { } fs)
			{
				float up = u.StationTypes.TryGetValue(fs.TypeKey, out var ft) ? PlayerSeatsOffices.UpkeepPerMinute(u, ft) : 0f;
				TestFirmSeatLine = $"{fs.Name} ({u.Sectors[fs.Sector].Name}) · Befehlsstufe +{u.Rules.FirmSeatCommandBonus} · Fehden freigeschaltet (Punkt 6) · Unterhalt {up:0} Cr/min";
				y = WrappedText(card, TestFirmSeatLine, 20, y, width - 40, 14, White, 19) + 6;
				PolicyButton(card, $"firmseat:close:{fs.Id.Value}", "Firmensitz aufgeben", 20, y, MathF.Min(320f, width - 40), confirm: true);
				y += 64;
			}
			else
			{
				string key = shipNow is null ? "firmseat:found:0" : $"firmseat:found:{shipNow.Sector.Value}";
				var offer = Ctx.Comms.Preview(u, key);
				TestFirmSeatLine = offer.Possible ? offer.Preview : offer.Reason ?? "";
				var ft = FirmSeats.SeatType(u);
				string cond = ft is null ? "" : $"Bedingungen: eigener oder herrenloser Sektor ohne Versteck, dein Schiff dort, Größenklasse {ActorNames.Size((ActorSizeClass)u.Rules.PlayerFirmSeatMinClass)}, {FirmSeats.Cost(u, ft):0} Cr. ";
				y = WrappedText(card, cond + TestFirmSeatLine, 20, y, width - 40, 13, offer.Possible ? new Color(Dim, 0.95f) : Orange, 18) + 6;
				PolicyButton(card, key, "Firmensitz gründen", 20, y, MathF.Min(320f, width - 40), confirm: true);
				y += 64;
			}
		}
		if (u.Rules.PlayerOffices)
		{
			At(card, Text("Büros", 16, Cyan), 20, y, width - 40, 22);
			y += 26;
			var offices = Offices.Of(u, u.PlayerActor);
			foreach (var of in offices)
			{
				string line = $"{of.Name} ({u.Sectors[of.Sector].Name}) · Hangar, Auftragsbrett";
				y = WrappedText(card, line, 20, y, width - 40, 14, White, 19) + 4;
				PolicyButton(card, $"office:close:{of.Id.Value}", "Büro schließen", 20, y, MathF.Min(260f, width - 40), confirm: true);
				y += 64;
			}
			if (shipNow is not null && !offices.Any(o => o.Sector == shipNow.Sector))
			{
				string key = $"office:found:{shipNow.Sector.Value}";
				var offer = Ctx.Comms.Preview(u, key);
				TestOfficeLine = offer.Possible ? offer.Preview : offer.Reason ?? "";
				var ot = Offices.OfficeType(u);
				string cond = ot is null ? "" : $"Bedingungen: fremder Sektor, Gastgeber stimmt zu (Beziehung ab {u.Rules.OfficeMinRelation:0.00}, kein Krieg, keine Sperre gegen dich), Größenklasse {ActorNames.Size((ActorSizeClass)u.Rules.PlayerOfficeMinClass)}, {Offices.Cost(u, ot):0} Cr. ";
				y = WrappedText(card, cond + TestOfficeLine, 20, y, width - 40, 13, offer.Possible ? new Color(Dim, 0.95f) : Orange, 18) + 6;
				PolicyButton(card, key, "Büro gründen", 20, y, MathF.Min(320f, width - 40), confirm: true);
				y += 64;
			}
			else if (offices.Count > 0) TestOfficeLine = string.Join(" | ", offices.Select(o => o.Name));
		}
		// Ausgleich (Punkt 2): Regierungsstationen, deren Unterhalt die Kasse nicht trägt, ruhen (kein Einfluss, keine Sperren, kein Gericht)
		TestUnpaidLine = PlayerUpkeep.StatusText(u);
		if (TestUnpaidLine.Length > 0) y = WrappedText(card, TestUnpaidLine, 20, y, width - 40, 13, Orange, 18) + 6;
		if (_policyMsg.Length > 0) { y = WrappedText(card, _policyMsg, 20, y, width - 40, 13, Green, 18) + 6; }
		h = MathF.Max(h, y + 12f);
		card.CustomMinimumSize = new Vector2(width, h);
		card.Size = new Vector2(width, h);
	}
}
