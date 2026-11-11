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
	public string TestExport { get; private set; } = "";

	protected override void BuildContent(Control inner)
	{
		var u = Ctx.U; var st = Ctx.Station;
		float w = inner.Size.X > 0 ? inner.Size.X : 1020f;
		float h = inner.Size.Y > 0 ? inner.Size.Y : 520f;
		float cw = (w - 20) / 3f;

		// ── Karte 1: Allgemein (alles echt) ──
		var c1 = CardPanel(inner, 0, 0, cw, h);
		var v1 = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		v1.AddThemeConstantOverride("separation", 8);
		c1.AddChild(v1);
		v1.AddChild(Text("Allgemein", 22, Cyan));
		string type = u.StationTypes.TryGetValue(st.TypeKey, out var t) ? t.Name : st.TypeKey;
		string faction = u.ActorName(st.Owner);   // Phase 9: Besitzer ist ein Akteur (Fraktionen tragen ihren Namen)
		string sector = u.Sectors.TryGetValue(st.Sector, out var sec) ? sec.Name : "?";
		Row(v1, "Name", st.Name);
		Row(v1, "Typ", type);
		Row(v1, "Besitzer", faction);
		Row(v1, "Sektor", sector);
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
		else
			Row(v1, "Schutz", st.BankruptcyProtected ? "vor Pleite geschützt" : StationCriticality.IsCritical(u, st) ? "kritisch (letzte ihrer Kette)" : "keiner");
		v1.AddChild(Text("Angebotene Dienste", 18, Cyan));
		var services = Ctx.Services.ForStation(u, st);
		if (services.Count == 0) v1.AddChild(Text("keine", 15, Dim));
		var chips = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		chips.AddThemeConstantOverride("separation", 6);
		foreach (var s in services) chips.AddChild(Chip(DockAreas.LabelFor(s.Key)));
		v1.AddChild(chips);

		// ── Karte 2: Marktübersicht (Nachfrage/Überschuss echt aus Bestand gegen Ziel) ──
		var c2 = CardPanel(inner, cw + 10, 0, cw, h);
		var v2 = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		v2.AddThemeConstantOverride("separation", 4);
		c2.AddChild(v2);
		v2.AddChild(Text(st.IsBuildSite ? "Material (geliefert/verlangt)" : "Marktübersicht", 22, Cyan));
		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(list);
		v2.AddChild(scroll);
		foreach (var (gid, e) in st.Market.OrderBy(kv => kv.Key.Value))
		{
			var good = u.Goods.TryGetValue(gid, out var g) ? g : null;
			string name = good?.Name ?? gid.Value.ToString();
			Color col = good != null ? Color.FromHtml(good.Color) : new Color(1f, 0.75f, 0.3f);
			var (word, wcol) = MarketInfo.Classify(e) switch
			{
				MarketPressure.Scarce => ("Nachfrage", Orange),
				MarketPressure.Glut => ("Überschuss", Green),
				_ => ("ausgeglichen", Dim),
			};
			var r = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			r.AddThemeConstantOverride("separation", 8);
			var chip = new ColorRect { Color = col, CustomMinimumSize = new Vector2(14, 14), Size = new Vector2(14, 14), MouseFilter = MouseFilterEnum.Ignore };
			r.AddChild(chip);
			r.AddChild(Text(name, 16, White));
			var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
			r.AddChild(spacer);
			r.AddChild(Text($"{e.Amount:0}/{e.Target:0} · {word} · {e.Price:0.0} Cr", 14, wcol));
			list.AddChild(r);
		}

		// ── Karte 3: Politik und Hinweise (Phase 10 echt aus AccessInfo: Ruf = Beziehung des Besitzers zu dir, Zoll und Sperrstufe des Sektors) ──
		var c3 = CardPanel(inner, 2 * (cw + 10), 0, w - 2 * (cw + 10) - 10, h);
		var v3 = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		v3.AddThemeConstantOverride("separation", 8);
		c3.AddChild(v3);
		v3.AddChild(Text("Politik & Hinweise", 22, Cyan));
		var pol = AccessInfo.ForStation(u, st, Ctx.Ship);
		TestLock = pol.Lock; TestCustoms = pol.Customs;
		float tw = w - 2 * (cw + 10) - 38;
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
		v3.AddChild(Text("Hinweise", 16, Dim));
		foreach (var note in pol.Notes) Wrapped(v3, "· " + note, tw, 14, new Color(Dim, 0.95f));
	}

	/// <summary>Mehrzeiliger Text mit manuellem Umbruch (Label-Autowrap ist in Karten unzuverlässig).</summary>
	static void Wrapped(Control parent, string text, float width, int size, Color color)
	{
		int lines = Math.Max(1, (int)Math.Ceiling(text.Length * size * 0.52f / Math.Max(60f, width)));
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

	static void Row(Control parent, string name, string value)
	{
		var r = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		r.AddThemeConstantOverride("separation", 8);
		var l = Text(name, 15, Dim); l.CustomMinimumSize = new Vector2(150, 0);
		r.AddChild(l);
		r.AddChild(Text(value, 16, White));
		parent.AddChild(r);
	}

	static Control Chip(string label)
	{
		var c = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
		c.AddThemeStyleboxOverride("panel", Box(new Color(Cyan, 0.18f), null, 6, 0, 8));
		c.AddChild(Text(label, 14, Cyan));
		return c;
	}
}
