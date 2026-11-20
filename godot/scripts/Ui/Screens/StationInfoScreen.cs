using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Dock-Bereich „Station-Info“ (UI.md Abschnitt 3): Name, Typ, Besitzer-Fraktion, Stationskasse, Sektor und die
/// angebotenen Dienste (echt aus <see cref="SpaceSim.Core.Services.StationServices.ForStation"/>), dazu eine
/// Marktübersicht mit Nachfrage/Überschuss aus Bestand gegen Wunschbestand (echt aus <see cref="Station.Market"/>,
/// Einordnung über <see cref="MarketInfo"/>). Phase 10: Ruf (Beziehung des Besitzers zu dir), Zoll, Sperrstufe und Hinweise echt aus
/// <see cref="AccessInfo.ForStation"/>; Phase 11: Lage im Sektor (<see cref="SecurityInfo.ForSector"/>) und deine Fahndung bei der Fraktion der Station.
/// </summary>
public sealed partial class StationInfoScreen : DockAreaScreen
{
	public override string Id => "dock.info";
	public StationInfoScreen() => AreaId = DockAreas.Info;

	/// <summary>Selbsttest: zuletzt gezeigte Sperrstufe und Zoll (Phase 10).</summary>
	public string TestLock { get; private set; } = "";
	public string TestCustoms { get; private set; } = "";
	/// <summary>Selbsttest: zuletzt gezeigte Lage (Phase 11).</summary>
	public string TestSecurity { get; private set; } = "";
	/// <summary>Selbsttest: zuletzt gezeigte Exportzeile (Phase 14; leer ohne Exportsenke).</summary>
	public string TestBlackMarket = "";
	/// <summary>Ausbau Punkt 9, Selbsttest: Zeile zu Firmensitz oder Büro ("" = keins).</summary>
	public string TestSeatOffice = "";
	/// <summary>Ausbau Punkt 8: Zustandszeile (Selbsttest).</summary>
	public string TestWear = "";
	public string TestExport { get; private set; } = "";
	/// <summary>Selbsttest: zuletzt gezeigte Instandhaltung (Phase 15; leer, wenn die Station nicht instand gehalten wird).</summary>
	public string TestMaintenance { get; private set; } = "";
	/// <summary>Selbsttest: zuletzt gezeigte Rentabilität (Phase 22; leer, solange nicht gemessen).</summary>
	public string TestProfit { get; private set; } = "";
	/// <summary>Eigene Stationen, Selbsttest: Zeile „Deine Station“ (leer bei fremden Stationen).</summary>
	public string TestOwnLine { get; private set; } = "";
	/// <summary>Selbsttest Phase 28: Verteidigungszeile (leer bei Baustellen oder abgeschalteter Kriegsführung).</summary>
	public string TestDefense { get; private set; } = "";
	/// <summary>Selbsttest Phase 27: Dienst-Chips (deutsche Namen, Komma-getrennt).</summary>
	public string TestChips { get; private set; } = "";
	/// <summary>Selbsttest Punkt 17: Banditenzeile der Station („“ = keine Station einer etablierten Banditenfraktion).</summary>
	public string TestBandits { get; private set; } = "";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U; var st = Ctx.Station;
		float w = inner.Size.X > 0 ? inner.Size.X : 1020f;
		float h = inner.Size.Y > 0 ? inner.Size.Y : 520f;
		// Gerätetest Phase 27: Spalten mit festen Breiten; jede Zeile kürzt mit „…“ statt in die Nachbarspalte zu laufen
		// Runde 34b (Handytest r34 #4: bei „groß“ waren die Dienste-Chips abgeschnitten, nur per Rollen sichtbar): niedrige Fläche → „Allgemein“ und „Markt“
		// nebeneinander, „Politik & Hinweise“ darunter in voller Breite und voller Höhe (der Bereich rollt als Ganzes)
		bool stack = h < 470f || w < 900f;   // auch schmal (4:3-Tablet bei „groß“: drei Spalten à 230 kürzten Namen)
		float cw = stack ? (w - 10) * 0.46f : (w - 20) * 0.32f, mw = stack ? (w - 10) * 0.54f : (w - 20) * 0.37f;
		_rowW = cw - 44f;   // Platz für den Rollbalken der Karte

		// ── Karte 1: Allgemein (alles echt) ──
		var c1 = CardPanel(inner, 0, 0, cw, h);
		// Zweiter Gerätetest Phase 28: die Karte rollt, wenn ihr Inhalt (Verteidigung, zwei Chip-Zeilen) höher ist als der Rahmen — vorher wuchs der
		// PanelContainer über den Rahmen hinaus („Hangar“/„Taxi“ ragten heraus)
		var s1 = new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
		c1.AddChild(s1);
		var v1 = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		v1.AddThemeConstantOverride("separation", 6);
		s1.AddChild(v1);
		_infoScroll = s1;
		v1.AddChild(Text("Allgemein", 22, Cyan));
		string type = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Name : st.TypeKey;
		string faction = SpaceSim.Core.Model.ActorNames.Tagged(u, st.Owner);   // Phase 29: mit Kürzel   // Phase 9: Besitzer ist ein Akteur (Fraktionen tragen ihren Namen)
		string sector = u.Sectors.TryGetValue(st.Sector, out var sec) ? sec.Name : "?";
		Row(v1, "Name", st.Name);
		Row(v1, "Typ", type);
		Row(v1, "Besitzer", faction);
		Row(v1, "Sektor", sector);
		// Ausbau Punkt 17: Station einer etablierten Banditenfraktion — Sitz, Werke und Dauerzoll der Fraktion
		TestBandits = SpaceSim.Core.Simulation.Bandits.IsEstablished(u, st.Owner) ? SpaceSim.Core.Simulation.Bandits.Text(u, st.Owner) : "";
		if (TestBandits.Length > 0) Row(v1, "Banditen", TestBandits);
		if (sec?.Planet is { } planet) Row(v1, "Planet", planet.Name);   // Phase 14: Planet des Sektors
		Row(v1, st.IsBuildSite ? "Treuhand" : "Kasse", $"{st.Credits:0} Cr");
		// Phase 8: Baustelle — Zieltyp, Materialfortschritt, Bauschiff und Status (echt aus ConstructionInfo); Schutz/Kritikalität sonst
		if (ConstructionInfo.Of(u, st) is { } site)
		{
			Row(v1, "Baut", site.TypeName);
			Row(v1, "Material", $"{100f * site.Progress:0} % geliefert");
			Row(v1, "Bauschiff", site.Builder);
			Row(v1, "Status", site.State);
		}
		else if (st.Owner == u.PlayerActor && u.Rules.PlayerBuilding)
		{
			// eigene Stationen: deine Station — Produktion (Auslastung), Lager-Übersicht; Kasse umbuchen unter „Besitz“
			TestOwnLine = $"Produktion {st.Activity * 100f:0} %" + (st.Starved > 0.3f ? " · Zutaten fehlen" : "");
			Row(v1, "Deine Station", TestOwnLine);
			Row(v1, "", "Kasse umbuchen: Kommando → Besitz → Stationen");
		}
		else
			Row(v1, "Schutz", st.BankruptcyProtected ? "vor Pleite geschützt" : StationCriticality.IsCritical(u, st) ? "kritisch (letzte ihrer Kette)" : "keiner");
		// Phase 15: Instandhaltung (Material über den Handel, die Fraktionskasse erstattet) — „gedeckt“ oder „offen: …“ (reine Core-Anzeige)
		TestMaintenance = Maintenance.StatusText(u, st) ?? "";
		if (TestMaintenance.Length > 0)
		{
			// Runde 34b (Handytest r34 n2: „offen: 2 Stahl, 0,1 Elektr…“): Zustand und offene Mengen in zwei Zeilen
			int colon = TestMaintenance.IndexOf(": ", System.StringComparison.Ordinal);
			if (colon > 0) { Row(v1, "Instandhaltung", TestMaintenance[..colon]); Row(v1, "", TestMaintenance[(colon + 2)..]); }
			else Row(v1, "Instandhaltung", TestMaintenance);
		}
		// Ausbau Punkt 8: Zustand der Station (Verschleiß) und seine Wirkung — reine Core-Anzeige
		TestWear = StationWear.Text(u, st) ?? "";
		if (StationWear.Percent(u, st) is { } wp)
		{
			Row(v1, "Zustand", wp);
			if (StationWear.Effect(u, st) is { } we) Row(v1, "", we);
		}
		// Phase 22: Rentabilität (gleitender Netto-Geldfluss, reine Core-Anzeige; Grund c für das Aufgeben einer Station)
		TestProfit = st.IsBuildSite ? "" : Profitability.Text(u, st.Profit) ?? "";
		if (TestProfit.Length > 0)
		{
			// Prüfung Phase 28: die schmale Spalte schnitt „seit … Ticks im Minus“ ab — Rate und Zusatz in zwei Zeilen
			int cut = TestProfit.IndexOf(" · ", System.StringComparison.Ordinal);
			Row(v1, "Rentabilität", cut > 0 ? TestProfit[..cut] : TestProfit);
			if (cut > 0) Row(v1, "", TestProfit[(cut + 3)..]);
		}
		// Phase 28: Verteidigung (Hülle, Schild, Geschütze, ausgeschaltet/angegriffen) — reine Core-Anzeige
		TestDefense = st.IsBuildSite || !u.Rules.Warfare ? "" : SpaceSim.Core.Combat.WarInfo.StationLine(u, st);
		if (TestDefense.Length > 0)
		{
			// mehrere kurze Zeilen (die Spalte ist schmal): Hülle, Schild, Geschütze und Zustand
			var def = SpaceSim.Core.Combat.StationCombat.DefenseOf(u, st);
			Row(v1, "Verteidigung", $"Hülle {SpaceSim.Core.Combat.StationCombat.Hull(u, st):0}/{SpaceSim.Core.Combat.StationCombat.MaxHull(u, st):0}");
			if (SpaceSim.Core.Combat.StationCombat.MaxShield(u, st) > 0f)
				Row(v1, "", $"Schild {SpaceSim.Core.Combat.StationCombat.Shield(u, st):0}/{SpaceSim.Core.Combat.StationCombat.MaxShield(u, st):0}");
			string state = SpaceSim.Core.Combat.StationCombat.IsDown(u, st) ? " · AUS" : SpaceSim.Core.Combat.StationCombat.UnderSiege(u, st.Id) ? " · Angriff" : "";
			Row(v1, "Geschütze", $"{def?.Guns.Count ?? 0}{state}");
		}


		// ── Karte 2: Marktübersicht (Nachfrage/Überschuss echt aus Bestand gegen Ziel) ──
		var c2 = CardPanel(inner, cw + 10, 0, mw, h);
		var v2 = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		v2.AddThemeConstantOverride("separation", 4);
		c2.AddChild(v2);
		v2.AddChild(Text(st.IsBuildSite ? "Material (geliefert/verlangt)" : "Marktübersicht", 22, Cyan));
		var scroll = new KitScroll { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(mw - 32f, 0) };
		var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(mw - 44f, 0) };
		list.AddThemeConstantOverride("separation", 2);   // Runde 35c: die Zebrastreifen trennen die Waren
		_marketRow = 0;
		scroll.AddChild(list);
		v2.AddChild(scroll);
		foreach (var (gid, e) in st.Market.OrderBy(kv => kv.Key.Value))
		{
			var good = u.Goods.TryGetValue(gid, out var g) ? g : null;
			string name = good?.Name ?? gid.Value.ToString();
			Color col = good != null ? Color.FromHtml(good.Color) : new Color(1f, 0.75f, 0.3f);
			var (word, wcol) = MarketInfo.Classify(e) switch
			{
				MarketPressure.Scarce => ("knapp", Orange),
				MarketPressure.Glut => ("Überschuss", Green),
				_ => ("ok", Dim),
			};
			// Runde 34b (Prüfung B3: „Universalfrachtrau…“ dreimal untereinander, daneben Platz frei): zwei Zeilen je Ware — der Name bekommt die ganze
			// Spaltenbreite (mit der Schrift gemessen: erst kleiner bis 11, dann in der Mitte gekürzt, damit das unterscheidende Ende sichtbar bleibt), darunter
			// rechtsbündig Bestand/Ziel, Lage und Preis
			// Runde 35c (Prüfung: Werte standen 22–27 px unter dem Namen zwischen zwei Waren): passen Name und Werte in eine Zeile, stehen sie auf derselben
			// Grundlinie (Werte rechtsbündig); sonst zweizeilig als Paar auf einem Zebrastreifen (jede zweite Ware leicht hinterlegt), so dass klar ist,
			// welche Werte zu welchem Namen gehören
			string vals = $"{e.Amount:0}/{e.Target:0} · {word} · {e.Price:0}";
			var font = ThemeDB.FallbackFont;
			float rowW = mw - 56f;
			float valW = font.GetStringSize(vals, HorizontalAlignment.Left, -1, 12).X;
			float nameFull = font.GetStringSize(name, HorizontalAlignment.Left, -1, 15).X;
			bool oneLine = 14f + 8f + nameFull + 16f + valW <= rowW;
			var stripe = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
			stripe.AddThemeStyleboxOverride("panel", Box(_marketRow++ % 2 == 0 ? new Color(1f, 1f, 1f, 0.05f) : new Color(0, 0, 0, 0), null, 4, 0, 3));
			var r = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			r.AddThemeConstantOverride("separation", 8);
			var chip = new ColorRect { Color = col, CustomMinimumSize = new Vector2(14, 14), Size = new Vector2(14, 14), MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
			r.AddChild(chip);
			if (oneLine)
			{
				var nl = Text(name, 15, White);
				nl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				r.AddChild(nl);
				var vl = Text(vals, 12, wcol, HorizontalAlignment.Right);
				r.AddChild(vl);
				stripe.AddChild(r);
			}
			else
			{
				float nameRoom = rowW - 22f - 6f;
				var nl = Text(name, 15, White);
				FitInto(nl, name, nameRoom);
				r.AddChild(nl);
				var pair = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
				pair.AddThemeConstantOverride("separation", -2);
				pair.AddChild(r);
				var right = Text(vals, 12, wcol, HorizontalAlignment.Right);
				right.CustomMinimumSize = new Vector2(rowW - 6f, 0);
				pair.AddChild(right);
				stripe.AddChild(pair);
			}
			list.AddChild(stripe);
		}

		// ── Karte 3: Politik und Hinweise (Phase 10 echt aus AccessInfo: Ruf = Beziehung des Besitzers zu dir, Zoll und Sperrstufe des Sektors) ──
		float pw = stack ? w : w - (cw + 10) - (mw + 10);
		var c3 = stack ? CardPanel(inner, 0, h + 10, pw, h) : CardPanel(inner, cw + mw + 20, 0, pw, h);
		var s3 = new KitScroll { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
		c3.AddChild(s3);
		var v3 = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		v3.AddThemeConstantOverride("separation", 8);
		s3.AddChild(v3);
		_politicsScroll = s3;
		v3.AddChild(Text("Politik & Hinweise", 22, Cyan));
		var pol = AccessInfo.ForStation(u, st, Ctx.Ship);
		TestLock = pol.Lock; TestCustoms = pol.Customs;
		float tw = pw - 44;
		Wrapped(v3, "Ruf bei dir: " + pol.Reputation, tw, 15, White);
		Wrapped(v3, "Zoll: " + pol.Customs, tw, 15, pol.Customs == "kein Zoll" ? Green : pol.Customs.StartsWith("gesperrt") ? Red : Orange);
		Wrapped(v3, "Sperrstufe: " + pol.Lock, tw, 15, pol.Lock == "Offen" ? Green : Orange);
		// Phase 11: Lage im Sektor (Lagekarte) und deine Fahndung bei der Fraktion der Station (echt)
		var lage = SecurityInfo.ForSector(u, st.Sector, Ctx.Ship);
		TestSecurity = lage.Summary;
		Wrapped(v3, $"Lage: {lage.Summary} · {lage.Patrols} Streife{(lage.Patrols == 1 ? "" : "n")}", tw, 15, lage.Threat >= 1f ? Orange : White);
		int lvl = u.FactionOf(st) is { } sf ? Wanted.LevelOf(u, Ctx.Ship.Owner, sf) : 0;
		Wrapped(v3, "Fahndung hier: " + SecurityNames.WantedLevel(lvl), tw, 15, lvl >= 3 ? Red : lvl > 0 ? Orange : Green);
		// Phase 14: Exportsenke — „Export an <Planet>: n Chargen unterwegs, nächste in …“ (reine Core-Anzeige ExportInfo)
		var ex = ExportInfo.ForStation(u, st);
		TestExport = ex?.Line ?? "";
		if (ex is not null)
		{
			Wrapped(v3, ex.Line, tw, 15, Cyan);
			Wrapped(v3, $"Kauft {string.Join(", ", ex.Goods.Select(g => u.Goods[g].Name))} an · Erlös am Planeten +{100f * ex.Markup:0} %", tw, 13, new Color(Dim, 0.95f));
		}
		// Schwarzmarkt: Hehler im Versteck (reine Core-Anzeige BlackMarket.InfoLine)
		TestBlackMarket = SpaceSim.Core.Trading.BlackMarket.InfoLine(u, st) ?? "";
		if (TestBlackMarket.Length > 0) Wrapped(v3, TestBlackMarket, tw, 15, Orange);
		// Ausbau Punkt 9: Firmensitz (Firmenkasse) und Büro (Außenstelle; Schutzverträge folgen mit Punkt 3) — reine Core-Anzeige
		TestSeatOffice = FirmSeats.InfoLine(u, st) ?? "";
		if (TestSeatOffice.Length > 0) Wrapped(v3, TestSeatOffice, tw, 15, Cyan);
		v3.AddChild(Text("Hinweise", 16, Dim));
		foreach (var note in pol.Notes) Wrapped(v3, "· " + note, tw, 14, new Color(Dim, 0.95f));
		// Prüfung Runde 2: die Dienste stehen in der Hinweis-Karte (viel Platz) — in „Allgemein“ war die unterste Chip-Reihe angeschnitten
		v3.AddChild(Text("Angebotene Dienste", 18, Cyan));
		var services = Ctx.Services.ForStation(u, st);
		if (services.Count == 0) v3.AddChild(Text("keine", 15, Dim));
		// Gerätetest: Dienste-Chips umbrechen (liefen in die Marktspalte)
		var chips = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(pw - 44f, 0) };
		chips.AddThemeConstantOverride("h_separation", 6);
		chips.AddThemeConstantOverride("v_separation", 6);
		TestChips = string.Join(",", services.Select(sv => DockAreas.ServiceName(sv.Key, u)));
		foreach (var s in services)
			chips.AddChild(Chip(DockAreas.ServiceName(s.Key, u)));   // Dienst, nicht Dockbereich — immer der deutsche Name
		v3.AddChild(chips);
		if (stack)
		{
			// volle Höhe des Inhalts — die Karte rollt nicht selbst, der Bereich rollt
			float need = v3.GetCombinedMinimumSize().Y + 34f;
			c3.CustomMinimumSize = new Vector2(pw, need); c3.Size = c3.CustomMinimumSize;
		}
	}

	/// <summary>Mehrzeiliger Text mit manuellem Umbruch (Label-Autowrap ist in Karten unzuverlässig).</summary>
	static void Wrapped(Control parent, string text, float width, int size, Color color)
	{
		int lines = Math.Max(1, Wrap(text, size, width).Count);   // Prüfung Runde 2: gemessen wie WrappedText
		var holder = new Control { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0, lines * (size + 6)) };
		parent.AddChild(holder);
		WrappedText(holder, text, 0, 0, width, size, color);
	}

	static PanelContainer CardPanel(Control parent, float x, float y, float w, float h)
	{
		var p = At(parent, new PanelContainer(), x, y, w, h);
		p.AddThemeStyleboxOverride("panel", Box(Card, null, 10, 0, 14));
		return p;
	}

	float _rowW = 300f;
	KitScroll? _infoScroll, _politicsScroll;
	/// <summary>Selbsttest Runde 2: rollt eine der Karten (dann wäre unten etwas angeschnitten)?</summary>
	public bool TestCardsScroll => Scrolls(_infoScroll) || Scrolls(_politicsScroll);
	static bool Scrolls(KitScroll? k) => k is not null && k.GetVScrollBar().MaxValue - k.GetVScrollBar().Page > 0.5;
	/// <summary>Selbsttest: Rahmen der Karte „Allgemein“ und Rechteck ihres letzten Dienst-Chips (muss im Rahmen liegen oder die Karte rollt).</summary>
	public (Rect2 Card, Rect2 Content, bool Scrolls) TestInfoCard => _infoScroll is { } k && k.GetChild(0) is Control c
		? (k.GetGlobalRect(), c.GetGlobalRect(), k.GetVScrollBar().MaxValue - k.GetVScrollBar().Page > 0.5) : (default, default, false);

	/// <summary>Gerätetest Phase 27: Label kürzt mit „…“ und bestimmt nicht die Breite seines Containers.</summary>
	static Label Clip(Label l)
	{
		l.ClipText = true;
		l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		l.CustomMinimumSize = new Vector2(1, 0);
		return l;
	}

	void Row(Control parent, string name, string value)
	{
		var r = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(_rowW, 0), Size = new Vector2(_rowW, 0) };
		r.AddThemeConstantOverride("separation", 8);
		float nw = Math.Min(118f, _rowW * 0.38f);
		var l = Clip(Text(name, 14, Dim)); l.SizeFlagsHorizontal = Control.SizeFlags.Fill; l.CustomMinimumSize = new Vector2(nw, 0);
		FitSize(l, name, nw);   // Runde 34b: erst kleiner (bis 11), dann „…“
		r.AddChild(l);
		var v = Clip(Text(value, 15, White));
		FitSize(v, value, _rowW - nw - 8f);
		v.TooltipText = value;
		r.AddChild(v);
		parent.AddChild(r);
	}

	/// <summary>Runde 34b: Schriftgröße so wählen, dass <paramref name="text"/> in <paramref name="room"/> passt (Core: UiLayout.FitFontSize, bis 11).</summary>
	static void FitSize(Label l, string text, float room)
	{
		var font = ThemeDB.FallbackFont;
		int size = l.GetThemeFontSize("font_size");
		int fit = SpaceSim.Core.Ui.UiLayout.FitFontSize(fs => font.GetStringSize(text, HorizontalAlignment.Left, -1, fs).X, room, size);
		if (fit != size) l.AddThemeFontSizeOverride("font_size", fit);
	}

	/// <summary>Runde 34b: Warenname einpassen — kleiner bis 11, passt er dann noch nicht, in der Mitte kürzen (Core: UiLayout.MiddleEllipsis).</summary>
	int _marketRow;

	static void FitInto(Label l, string text, float room)
	{
		FitSize(l, text, room);
		var font = ThemeDB.FallbackFont;
		int size = l.GetThemeFontSize("font_size");
		l.Text = SpaceSim.Core.Ui.UiLayout.MiddleEllipsis(text, t => font.GetStringSize(t, HorizontalAlignment.Left, -1, size).X, room);
		l.CustomMinimumSize = new Vector2(room, 0);
	}

	static Control Chip(string label)
	{
		var c = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
		c.AddThemeStyleboxOverride("panel", Box(new Color(Cyan, 0.18f), null, 6, 0, 8));
		c.AddChild(Text(label, 14, Cyan));
		return c;
	}
}
