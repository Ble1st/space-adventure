using Godot;
using System.Linq;
using static SpaceAdventure.Prototypes.K;

namespace SpaceAdventure.Prototypes;

/// <summary>Kommandozentrale: Karten, Fraktionen, Spielerfraktion, Flotte, Besitz, Comms und Hamburger-Menü.</summary>
public static class FinalUi2
{
	public static readonly string[] Screens = { "hamburger", "universe", "sector", "factions", "myfaction", "fleet", "assets", "asset", "comms" };

	static readonly Color Act = new(0.2f, 0.38f, 0.62f, 0.95f);
	static readonly Color BuyC = new(0.15f, 0.45f, 0.28f, 0.95f);
	static readonly Color SellC = new(0.5f, 0.28f, 0.12f, 0.95f);
	static readonly Color Card = new(0.02f, 0.04f, 0.09f, 0.88f);
	static readonly string[] Rail = { "Universum", "Sektor", "Fraktionen", "Meine Fraktion", "Flotte", "Besitz", "Comms" };

	static readonly Color Terra = new(0.35f, 0.8f, 1f), Teck = new(1f, 0.62f, 0.2f), Aurum = new(0.5f, 0.92f, 0.5f), Korsar = new(1f, 0.38f, 0.38f), Frei = new(0.72f, 0.72f, 0.95f);

	public static void Build(string name, Control root, ProtoData d)
	{
		switch (name)
		{
			case "hamburger": Hamburger(root, d); break;
			case "universe": Universe(root, d); break;
			case "sector": Sector(root, d); break;
			case "factions": Factions(root, d); break;
			case "myfaction": MyFaction(root, d); break;
			case "fleet": Fleet(root, d); break;
			case "assets": Assets(root, d); break;
			case "asset": Assets(root, d); AssetDetail(root, d); break;
			case "comms": Comms(root, d); break;
		}
	}

	// ───────────── Gerüst der Kommandozentrale ─────────────
	static Control Shell(Control root, ProtoData d, string active, string sub)
	{
		ProtoVariants.Dim(root, 0.84f);
		G(root, 20, 10, 1240, 64, g =>
		{
			g.Text(new Vector2(0, 32), "Kommandozentrale", 28, Cyan);
			g.Text(new Vector2(0, 56), sub, 16, Dim);
			g.TextR(1240, 32, $"{N0(d.PlayerCredits)} Credits", 26, Orange);
			g.TextR(1240, 56, "Orion-A „Falke“ · angedockt Sektor Alpha", 15, Dim);
		});
		var rail = At(root, Pnl(PanelBg, null, 10, 8), 14, 84, 200, 540);
		var rv = V(6); rail.AddChild(rv);
		foreach (var t in Rail)
		{
			var b = B(t, 184, 56, t == active ? Act : null, 18, null, 8, t == active ? Cyan : null);
			rv.AddChild(b);
			if (t == "Comms") { var badge = L("3", 14, Colors.Black, HorizontalAlignment.Center); var bp = Pnl(Orange, null, 11, 0); bp.CustomMinimumSize = new Vector2(24, 22); bp.Position = new Vector2(150, 8); b.AddChild(bp); bp.AddChild(badge); }
		}
		var center = Win(root, 224, 84, 1042, 540, PanelBg, Edge, 10);
		At(root, B("← Flug", 160, 58, null, 20, null, 10), 14, 648);
		At(root, L("Kommandozentrale öffnet sich über das Hamburger-Menü", 13, Dim), 190, 664);
		return center;
	}

	static void Segmented(Control parent, float x, float y, string[] items, int active, float w = 130)
	{
		for (int i = 0; i < items.Length; i++)
			At(parent, B(items[i], w, 40, i == active ? Act : null, 17, null, 8, i == active ? Cyan : null), x + i * (w + 6), y);
	}

	// ───────────── Hamburger-Menü ─────────────
	public static void Hamburger(Control root, ProtoData d)
	{
		FinalUi.Hud(root, d);
		ProtoVariants.Dim(root, 0.45f);
		var card = Win(root, 14, 214, 330, 436, PanelSolid, Edge, 14);
		var items = new (string, string, bool)[] { ("Karte", "Universum und Sektor", false), ("Fraktionen", "Beziehungen und Lage", false), ("Meine Fraktion", "Größe, Ruf, Fahndung", false), ("Flotte", "Verbände und Befehle", false), ("Besitz", "Schiffe und Stationen", false), ("Comms", "3 neue Nachrichten", true), ("Speichern", "Autosave vor 12 s", false), ("Einstellungen", "Ton, Tasten, Sprache", false) };
		float y = 10;
		foreach (var (t, s, badge) in items)
		{
			var b = B("", 310, 48, null, 16, null, 8);
			At(card, b, 10, y);
			var l1 = L(t, 17, White); l1.Position = new Vector2(14, 4); b.AddChild(l1);
			var l2 = L(s, 12, Dim); l2.Position = new Vector2(14, 26); b.AddChild(l2);
			if (badge) { var bp = Pnl(Orange, null, 11, 0); bp.CustomMinimumSize = new Vector2(26, 22); bp.Position = new Vector2(268, 13); b.AddChild(bp); bp.AddChild(L("3", 14, Colors.Black, HorizontalAlignment.Center)); }
			y += 52;
		}
		var hb = At(root, B("", 60, 60, Act, 24, null, 30, Cyan), 16, 644);
		G(hb, 0, 0, 60, 60, g => { foreach (var yy in new[] { 20f, 30f, 40f }) g.DrawRect(new Rect2(16, yy, 28, 4), White); });
	}

	// ───────────── Karten ─────────────
	static readonly (string n, Vector2 p, Color c, string owner)[] Sectors =
	{
		("Alpha", new Vector2(130, 320), Terra, "Terra"), ("Beta", new Vector2(280, 190), Terra, "Terra"), ("Gamma", new Vector2(440, 300), Aurum, "Aurum"),
		("Delta", new Vector2(290, 420), Teck, "Teck"), ("Epsilon", new Vector2(590, 190), Korsar, "Korsaren"), ("Zeta", new Vector2(600, 390), Frei, "Frei"),
	};

	static void Dashed(Gfx g, Vector2 a, Vector2 b, Color c, float w)
	{
		var dir = (b - a); float len = dir.Length(); dir /= len;
		for (float t = 0; t < len; t += 14) g.DrawLine(a + dir * t, a + dir * Mathf.Min(len, t + 8), c, w);
	}

	public static void Universe(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Universum", "Universumskarte · 6 Sektoren bekannt");
		Segmented(c, 12, 10, new[] { "Universum", "Sektor" }, 0, 120);
		At(c, L("Ebene:", 14, Dim), 270, 20);
		float lx = 332; foreach (var (t, on) in new[] { ("Fraktion", true), ("Sicher.", false), ("Preise", false), ("Schiffe", true) }) { At(c, B(t, 86, 36, on ? Act : null, 13, null, 18), lx, 12); lx += 90; }
		G(c, 0, 56, 710, 484, g =>
		{
			foreach (var s in Sectors) g.DrawCircle(s.p, 78, new Color(s.c, 0.10f));
			void Gate(int a, int b, string kind)
			{
				var pa = Sectors[a].p; var pb = Sectors[b].p;
				if (kind == "open") g.DrawLine(pa, pb, new Color(Dim, 0.7f), 2.5f);
				else if (kind == "closed") Dashed(g, pa, pb, Red, 3);
				else Dashed(g, pa, pb, Orange, 3);
			}
			Gate(0, 1, "open"); Gate(1, 2, "open"); Gate(0, 3, "open"); Gate(2, 3, "closed"); Gate(2, 4, "open"); Gate(2, 5, "open"); Gate(3, 5, "restricted");
			g.DrawLine(Sectors[0].p, Sectors[1].p, new Color(Green, 0.9f), 5);
			for (int i = 0; i < Sectors.Length; i++)
			{
				var s = Sectors[i]; bool sel = i == 1, here = i == 0;
				g.DrawCircle(s.p, 30, new Color(0.04f, 0.07f, 0.13f, 0.97f));
				g.DrawArc(s.p, 30, 0, Mathf.Tau, 40, s.c, sel ? 5 : 3, true);
				if (sel) g.DrawArc(s.p, 38, 0, Mathf.Tau, 40, Cyan, 2, true);
				g.Text(s.p + new Vector2(-40, 5), s.n.Substring(0, 1), 22, White, HorizontalAlignment.Center, 80);
				g.Text(s.p + new Vector2(-60, 56), s.n, 16, White, HorizontalAlignment.Center, 120);
				g.Text(s.p + new Vector2(-60, 73), s.owner, 12, s.c, HorizontalAlignment.Center, 120);
				if (here) { Ship(g, s.p + new Vector2(-26, -34), 10, new Color(0.25f, 0.33f, 0.5f), Cyan); }
			}
			Ship(g, Sectors[1].p + new Vector2(26, -34), 10, new Color(0.25f, 0.33f, 0.5f), Green);
			g.Text(Sectors[2].p + new Vector2(24, -34), "!", 20, Red);
			g.Text(new Vector2(376, 396), "gesperrt", 13, Red);
			g.Text(new Vector2(446, 404), "Zoll", 13, Orange);
			// Legende
			float lx2 = 12;
			foreach (var (n, col) in new[] { ("Terra", Terra), ("Teck", Teck), ("Aurum", Aurum), ("Korsaren", Korsar), ("Frei", Frei) }) { g.DrawCircle(new Vector2(lx2 + 6, 14), 6, col); g.Text(new Vector2(lx2 + 18, 19), n, 13, Dim); lx2 += 90; }
			g.Text(new Vector2(480, 19), "— offen   - - gesperrt   · · Zoll", 13, Dim);
		});
		var card = Win(c, 722, 12, 308, 516, Card, null, 10);
		G(card, 0, 0, 308, 516, g =>
		{
			g.Text(new Vector2(16, 34), "Sektor Beta", 24, Cyan);
			g.Text(new Vector2(16, 58), "Terra · Kontrolle: Dominanz", 15, Terra);
			g.Text(new Vector2(16, 98), "Sicherheit", 14, Dim); g.DrawRect(new Rect2(110, 88, 170, 10), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(110, 88, 170 * 0.78f, 10), Green);
			g.Text(new Vector2(16, 128), "Stationen 4 · Gates 2", 15, White);
			g.Text(new Vector2(16, 156), "Wache: ja · Zoll 4 %", 15, White);
			g.Text(new Vector2(16, 190), "Deine Schiffe", 14, Dim);
			Ship(g, new Vector2(28, 214), 9, new Color(0.25f, 0.33f, 0.5f), Green); g.Text(new Vector2(46, 219), "Spatz · Skiff · angedockt", 14, Green);
			g.Text(new Vector2(16, 258), "Route von Alpha: 1 Sprung · 2 min", 14, Dim);
			g.Text(new Vector2(16, 280), "Taxi 120 Cr · Dort wartet dein Skiff", 14, Orange);
			g.Text(new Vector2(16, 322), "Lage: Handel steigend, keine Meldung", 14, Dim);
		});
		At(card, B("Sektorkarte öffnen", 276, 54, Act, 18, null, 10, Cyan), 16, 352);
		At(card, B("Route planen", 134, 50, null, 16, null, 10), 16, 414);
		At(card, B("Taxi buchen", 134, 50, BuyC, 16, null, 10, Green), 158, 414);
		At(card, L("Tippen auf einen Sektor wählt ihn", 12, Dim), 16, 478);
	}

	public static void Sector(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Sektor", "Sektorkarte · Alpha");
		Segmented(c, 12, 10, new[] { "Universum", "Sektor" }, 1, 120);
		At(c, L("Sektor Alpha · Terra", 17, Terra), 290, 20);
		G(c, 0, 56, 710, 484, g =>
		{
			var ctr = new Vector2(355, 240);
			g.DrawCircle(ctr, 225, new Color(0.03f, 0.05f, 0.1f, 0.9f));
			g.DrawArc(ctr, 225, 0, Mathf.Tau, 80, new Color(Edge, 0.6f), 2, true);
			g.DrawArc(ctr, 150, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.05f), 1);
			g.DrawArc(ctr, 75, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.05f), 1);
			// Gürtel
			var belt = new Vector2(500, 130);
			var rnd = new RandomNumberGenerator { Seed = 7 };
			for (int i = 0; i < 26; i++) g.DrawCircle(belt + new Vector2(rnd.RandfRange(-60, 60), rnd.RandfRange(-36, 36)), rnd.RandfRange(2.5f, 6f), new Color(0.6f, 0.55f, 0.5f));
			g.Text(belt + new Vector2(-70, 62), "Gürtel Alpha-3 · Eisenerz", 12, Dim);
			// Gates am Rand
			foreach (var (nm, ang, col) in new[] { ("Gate → Beta", -50f, Green), ("Gate → Delta · Zoll", 150f, Orange) })
			{
				var gp = ctr + Vector2.FromAngle(Mathf.DegToRad(ang)) * 225;
				g.DrawCircle(gp, 14, new Color(0.04f, 0.07f, 0.13f)); g.DrawArc(gp, 14, 0, Mathf.Tau, 24, col, 3, true);
				g.Text(gp + new Vector2(-60, ang < 0 ? -22 : 32), nm, 13, col, HorizontalAlignment.Center, 120);
			}
			// Stationen
			void St(Vector2 p, string n, bool sel, Color col) { g.DrawRect(new Rect2(p - new Vector2(10, 10), new Vector2(20, 20)), new Color(0.06f, 0.1f, 0.2f)); g.DrawRect(new Rect2(p - new Vector2(10, 10), new Vector2(20, 20)), col, false, sel ? 4 : 2); if (sel) g.DrawArc(p, 22, 0, Mathf.Tau, 24, Cyan, 2, true); g.Text(p + new Vector2(-70, 36), n, 13, White, HorizontalAlignment.Center, 140); }
			St(new Vector2(280, 250), "Handelsstation Alpha", true, Terra);
			St(new Vector2(450, 330), "Raffinerie Alpha-2", false, Terra);
			St(new Vector2(190, 140), "Solarkraftwerk", false, Terra);
			// Schiffe
			Ship(g, new Vector2(296, 238), 9, new Color(0.25f, 0.33f, 0.5f), Cyan);
			g.Text(new Vector2(310, 232), "Falke", 12, Cyan);
			Ship(g, new Vector2(470, 160), 9, new Color(0.25f, 0.33f, 0.5f), Green); g.Text(new Vector2(484, 172), "Pickaxe", 12, Green);
			foreach (var p in new[] { new Vector2(380, 300), new Vector2(340, 130), new Vector2(230, 320) }) { g.DrawCircle(p, 4, new Color(Dim, 0.8f)); g.DrawLine(p, p + new Vector2(-14, 8), new Color(Dim, 0.35f), 1.5f); }
			g.Text(new Vector2(12, 470), "◼ Station   ● fremdes Schiff   ▲ deine Schiffe   ◯ Gate", 13, Dim);
		});
		var card = Win(c, 722, 12, 308, 516, Card, null, 10);
		G(card, 0, 0, 308, 516, g =>
		{
			g.Text(new Vector2(16, 34), "Handelsstation Alpha", 21, Cyan);
			g.Text(new Vector2(16, 58), "Terra · freundlich · 0 m (angedockt)", 14, Dim);
			g.Text(new Vector2(16, 96), "Dienste", 14, Dim);
			g.Text(new Vector2(16, 120), "Handel · Reparatur · Taxi · Hangar", 14, White);
			g.Text(new Vector2(16, 160), "Im Sektor", 14, Dim);
			g.Text(new Vector2(16, 184), "3 Stationen · 1 Gürtel · 2 Gates", 14, White);
			g.Text(new Vector2(16, 208), "7 fremde Schiffe · 2 deine", 14, White);
			g.Text(new Vector2(16, 248), "Sicherheit ▮▮▮▯  Patrouille 2", 14, Green);
		});
		At(card, B("Autopilot hierhin", 276, 52, Act, 17, null, 10, Cyan), 16, 300);
		At(card, B("Andocken", 276, 52, BuyC, 17, null, 10, Green), 16, 362);
		At(card, B("Info öffnen", 276, 48, null, 16, null, 10), 16, 424);
	}

	// ───────────── Fraktionen ─────────────
	static readonly (string n, string kind, Color c, int rel, string status, int sectors)[] Fs =
	{
		("Terra", "Staat", Terra, 62, "Frieden · Handelsabkommen", 3), ("Teck", "Staat", Teck, 5, "neutral · Gate gesperrt", 2),
		("Gilde Aurum", "Gilde", Aurum, 34, "Frieden", 1), ("Korsaren", "Gesetzlose", Korsar, -70, "feindlich · Kopfgeld 500", 1), ("Freie Händler", "Gruppe", Frei, 20, "neutral", 1),
	};

	public static void Factions(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Fraktionen", "Alle bekannten Fraktionen · Kenntnisstand 4 von 5");
		var list = V(6); At(c, list, 12, 12, 400, 516);
		int i = 0;
		foreach (var (n, kind, col, rel, status, sec) in Fs)
		{
			bool sel = i == 0;
			var row = Pnl(sel ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card, sel ? Cyan : null, 8, 4);
			row.CustomMinimumSize = new Vector2(396, 96);
			list.AddChild(row);
			var cn = n; var ck = kind; var cc = col; var cr = rel; var cs = status; var csec = sec;
			G(row, 0, 0, 388, 88, g =>
			{
				g.DrawCircle(new Vector2(24, 28), 12, cc);
				g.Text(new Vector2(48, 34), cn, 20, White);
				g.Text(new Vector2(48, 56), $"{ck} · {csec} Sektor{(csec == 1 ? "" : "en")}", 13, Dim);
				g.Text(new Vector2(48, 78), cs, 13, cr < 0 ? Red : cr > 40 ? Green : Dim);
				g.DrawRect(new Rect2(240, 24, 130, 10), new Color(0, 0, 0, 0.5f));
				float mid = 240 + 65; g.DrawLine(new Vector2(mid, 20), new Vector2(mid, 38), Dim, 1);
				if (cr >= 0) g.DrawRect(new Rect2(mid, 24, 65 * cr / 100f, 10), Green); else g.DrawRect(new Rect2(mid + 65 * cr / 100f, 24, -65 * cr / 100f, 10), Red);
				g.TextR(370, 56, $"{(cr > 0 ? "+" : "")}{cr}", 16, cr < 0 ? Red : White);
			});
			i++;
		}
		// Detail Terra
		var det = Win(c, 424, 12, 606, 516, Card, null, 10);
		G(det, 0, 0, 606, 516, g =>
		{
			g.DrawCircle(new Vector2(30, 36), 14, Terra);
			g.Text(new Vector2(54, 44), "Terra", 28, White);
			g.Text(new Vector2(54, 68), "Staat · Hauptstadt Alpha · Größe 100 %", 15, Dim);
			g.Text(new Vector2(20, 112), "Persönlichkeit", 16, Cyan);
			float y = 128;
			foreach (var (n, v) in new[] { ("Aggression", 0.3f), ("Gier", 0.55f), ("Kooperation", 0.75f), ("Gesetzestreue", 0.8f) })
			{ g.Text(new Vector2(20, y + 16), n, 14, Dim); g.DrawRect(new Rect2(150, y + 5, 200, 10), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(150, y + 5, 200 * v, 10), Cyan); y += 28; }
			g.Text(new Vector2(380, 112), "Lage", 16, Cyan);
			g.Text(new Vector2(380, 140), "Sektoren: Alpha, Beta, Eta", 14, White);
			g.Text(new Vector2(380, 164), "Zoll 4 % · Embargo: keines", 14, White);
			g.Text(new Vector2(380, 188), "Sperrung: Gate Delta (Teck)", 14, Orange);
			g.Text(new Vector2(20, 272), "Beziehungen", 16, Cyan);
			int k = 0;
			foreach (var (n, v, col) in new[] { ("Teck", -10, Teck), ("Aurum", 40, Aurum), ("Korsaren", -85, Korsar) })
			{ g.DrawCircle(new Vector2(30, 300 + k * 28), 6, col); g.Text(new Vector2(44, 305 + k * 28), n, 14, White); g.TextR(240, 305 + k * 28, $"{(v > 0 ? "+" : "")}{v}", 14, v < 0 ? Red : Green); k++; }
			g.Text(new Vector2(280, 272), "Mit dir", 16, Cyan);
			g.Text(new Vector2(280, 300), "Ruf +62 freundlich", 14, Green);
			g.Text(new Vector2(280, 324), "Handelsabkommen aktiv (Zoll −5 %)", 14, White);
			g.Text(new Vector2(280, 348), "Fahndung: sauber", 14, Green);
			g.Text(new Vector2(20, 376), "Letzte Ereignisse", 16, Cyan);
			g.Text(new Vector2(20, 402), "▮ Pirat bei Beta abgewehrt (+3 Ruf)", 13, Dim);
			g.Text(new Vector2(20, 424), "▮ Gate Delta gesperrt (Streit um Erz)", 13, Dim);
		});
		At(det, B("Kontakt aufnehmen", 220, 50, Act, 17, null, 10, Cyan), 370, 452);
		At(det, B("Gebiet auf Karte", 200, 50, null, 16, null, 10), 150, 452);
	}

	// ───────────── Spielerfraktion ─────────────
	public static void MyFaction(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Meine Fraktion", "Spielerfraktion · unabhängig");
		var a = Win(c, 12, 12, 500, 250, Card, null, 10);
		G(a, 0, 0, 500, 250, g =>
		{
			g.Text(new Vector2(20, 38), "Einzelakteur", 26, White);
			g.Text(new Vector2(20, 62), "Unabhängig · keine Fraktion", 15, Dim);
			g.Text(new Vector2(20, 100), "Größe 38 von 60 bis Kleinfirma", 15, Orange);
			g.DrawRect(new Rect2(20, 110, 460, 14), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(20, 110, 460 * 38f / 60f, 14), Cyan);
			g.Text(new Vector2(20, 156), "Schiffe 4 (Gewicht 20)", 14, White); g.TextR(480, 156, "+20", 14, Green);
			g.Text(new Vector2(20, 180), "Stationen 0", 14, White); g.TextR(480, 180, "+0", 14, Dim);
			g.Text(new Vector2(20, 204), "Kasse 12.480", 14, White); g.TextR(480, 204, "+18", 14, Green);
			g.Text(new Vector2(20, 232), "Tipp: eine eigene Station oder zwei weitere Schiffe", 13, Dim);
		});
		var b = Win(c, 524, 12, 506, 250, Card, null, 10);
		G(b, 0, 0, 506, 250, g =>
		{
			g.Text(new Vector2(20, 34), "Mitgliedschaft und Kasse", 18, Cyan);
			g.Text(new Vector2(20, 66), "Kasse der Person", 14, Dim); g.TextR(486, 66, $"{N0(d.PlayerCredits)} Cr", 20, Orange);
			g.Text(new Vector2(20, 96), "Mitglied von", 14, Dim); g.TextR(486, 96, "keiner Fraktion", 16, White);
			g.Text(new Vector2(20, 126), "Unterhalt Schiffe / Tag", 14, Dim); g.TextR(486, 126, "−42 Cr", 15, Red);
			g.Text(new Vector2(20, 156), "Liegegebühr / Tag (fremde Stationen)", 14, Dim); g.TextR(486, 156, "−24 Cr", 15, Red);
			g.Text(new Vector2(20, 186), "Einnahmen / Tag (Schätzung)", 14, Dim); g.TextR(486, 186, "+210 Cr", 15, Green);
		});
		At(b, B("Beitreten", 150, 46, null, 16, null, 10), 20, 196);
		At(b, B("Gründen (ab Kleinfirma)", 240, 46, null, 15, null, 10), 180, 196).Modulate = new Color(1, 1, 1, 0.45f);
		var rep = Win(c, 12, 274, 500, 254, Card, null, 10);
		G(rep, 0, 0, 500, 254, g =>
		{
			g.Text(new Vector2(20, 34), "Ruf und Fahndung", 18, Cyan);
			g.Text(new Vector2(20, 60), "Fraktion", 13, Dim); g.Text(new Vector2(250, 60), "Ruf", 13, Dim); g.Text(new Vector2(390, 60), "Fahndung", 13, Dim);
			float y = 86; foreach (var (n, col, rel, heat, hc) in new[] { ("Terra", Terra, 62, "sauber", Green), ("Teck", Teck, 5, "sauber", Green), ("Aurum", Aurum, 34, "sauber", Green), ("Korsaren", Korsar, -70, "gesucht", Red), ("Freie", Frei, 20, "auffällig", Orange) })
			{ g.DrawCircle(new Vector2(28, y - 5), 6, col); g.Text(new Vector2(42, y), n, 15, White); g.Text(new Vector2(250, y), $"{(rel > 0 ? "+" : "")}{rel}", 15, rel < 0 ? Red : White); g.Text(new Vector2(390, y), heat, 15, hc); y += 30; }
			g.Text(new Vector2(20, 240), "Fahndung gilt je Hoheitsgebiet und verfällt mit der Zeit", 12, Dim);
		});
		var perm = Win(c, 524, 274, 506, 254, Card, null, 10);
		G(perm, 0, 0, 506, 254, g =>
		{
			g.Text(new Vector2(20, 34), "Fähigkeiten nach Größe", 18, Cyan);
			float y = 66;
			foreach (var (t, ok, note) in new[] { ("Verbände", true, "1 von 1 · 4 Schiffe"), ("Taxi, Hangar, Leihschiff", true, "alle Stationen"), ("Büro an eigener Station", false, "ab Kleinfirma"), ("Fehde erklären", false, "ab Kleinfirma"), ("Firmensitz", false, "ab Firma"), ("Zoll und Sperren", false, "ab Dominanz im Sektor") })
			{ g.Text(new Vector2(20, y), ok ? "✓" : "🔒", 15, ok ? Green : Dim); g.Text(new Vector2(44, y), t, 15, ok ? White : Dim); g.TextR(486, y, note, 13, Dim); y += 30; }
		});
	}

	// ───────────── Flotte mit Befehlen ─────────────
	public static void Fleet(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Flotte", "Verbände und Befehle · Befehlskapazität: 2 von 3 Verbänden, 4 von 8 Schiffen");
		var list = V(6); At(c, list, 12, 12, 380, 516);
		int i = 0;
		foreach (var (n, sub, st, ok) in new[] { ("Verband Alpha-Handel", "Mule, Spatz · 2 Schiffe", "unterwegs nach Beta · 2 min", true), ("Verband Erz-1", "Pickaxe · 1 Schiff", "schürft Eisenerz · Automatik", true), ("Ohne Verband", "Falke (von dir gesteuert)", "angedockt Alpha", false) })
		{
			bool sel = i == 0;
			var row = Pnl(sel ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card, sel ? Cyan : null, 8, 4);
			row.CustomMinimumSize = new Vector2(372, 92);
			list.AddChild(row);
			var cn = n; var cs = sub; var cst = st; var cok = ok;
			G(row, 0, 0, 364, 84, g =>
			{
				g.Text(new Vector2(12, 30), cn, 19, White);
				g.Text(new Vector2(12, 52), cs, 13, Dim);
				g.Text(new Vector2(12, 74), cst, 13, cok ? Cyan : Dim);
			});
			i++;
		}
		list.AddChild(B("+ Neuer Verband", 372, 48, null, 16, null, 10));
		var det = Win(c, 404, 12, 626, 516, Card, null, 10);
		G(det, 0, 0, 626, 516, g =>
		{
			g.Text(new Vector2(20, 36), "Verband Alpha-Handel", 24, White);
			g.Text(new Vector2(20, 60), "Heimatbasis: Handelsstation Alpha · Formation Linie", 14, Dim);
			Ship(g, new Vector2(48, 112), 20, new Color(0.25f, 0.33f, 0.5f), Cyan); g.Text(new Vector2(78, 108), "Mule · Anführer", 15, White); g.Text(new Vector2(78, 126), "Hülle 96 · Sprit 88 · Fracht 110", 12, Dim);
			Ship(g, new Vector2(348, 112), 20, new Color(0.25f, 0.33f, 0.5f), Green); g.Text(new Vector2(378, 108), "Spatz", 15, White); g.Text(new Vector2(378, 126), "Hülle 100 · Sprit 91 · Fracht 20", 12, Dim);
			g.Text(new Vector2(20, 168), "Aktueller Befehl", 15, Cyan);
			g.Text(new Vector2(20, 192), "Handel Alpha ↔ Beta (Stahl) · Gewinn ca. 18 Cr je Tick", 14, White);
			g.Text(new Vector2(20, 214), "Ebene: Befehl · Notfall: Rückkehr bei Sprit unter 15 %", 13, Dim);
			g.Text(new Vector2(20, 256), "Neuen Befehl geben", 15, Cyan);
		});
		float bx = 20, by = 268; int bi = 0;
		foreach (var (t, en) in new[] { ("Fliegen", true), ("Halten", true), ("Handel", true), ("Schürfen", false), ("Patrouille", false), ("Wache", false), ("Eskorte", false), ("Heimkehr", true) })
		{
			var b = B(t, 134, 46, null, 16, null, 8); if (!en) b.Modulate = new Color(1, 1, 1, 0.4f);
			At(det, b, bx + (bi % 4) * 144, by + (bi / 4) * 54); bi++;
		}
		At(det, L("Grau: Fähigkeit fehlt (Kampf, Bergbau)", 12, Dim), 20, 378);
		At(det, L("Verhalten", 15, Cyan), 20, 402);
		float rx = 20; foreach (var (t, on) in new[] { ("Passiv", false), ("Verteidigen", true), ("Angriff", false), ("Feuer halten", false) }) { At(det, B(t, 134, 40, on ? Act : null, 14, null, 8, on ? Cyan : null), rx, 422); rx += 144; }
		At(det, B("Automatik an · 2 Sprünge · Budget 2.000", 350, 42, null, 14, null, 8), 20, 468);
		At(det, B("Befehl erteilen", 210, 46, BuyC, 18, null, 10, Green), 400, 466);
	}

	// ───────────── Besitz ─────────────
	static readonly (string n, string model, string where, string state, float hull, float fuel, bool here, bool docked)[] Owned =
	{
		("Falke", "Orion-A · Skiff", "Alpha · Handelsstation Alpha", "von dir gesteuert", 82, 72, true, true),
		("Mule", "Leichter Frachter", "Beta · im Flug nach Beta", "Handel Alpha ↔ Beta", 96, 88, false, false),
		("Spatz", "Skiff", "Beta · Handelsstation Beta", "angedockt", 100, 91, false, true),
		("Pickaxe", "Schürfer", "Alpha · Gürtel Alpha-3", "schürft Eisenerz", 71, 54, false, false),
	};

	public static void Assets(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Besitz", "Alles, was dir gehört · Tippen öffnet Details");
		G(c, 0, 0, 1042, 80, g =>
		{
			float x = 20;
			foreach (var (lab, val) in new[] { ("Kasse", "12.480 Cr"), ("Schiffe", "4"), ("Stationen", "0"), ("Verträge", "2"), ("Gesamtwert", "≈ 61.000 Cr") })
			{ g.Text(new Vector2(x, 30), val, 22, lab == "Kasse" ? Orange : White); g.Text(new Vector2(x, 54), lab, 13, Dim); x += 200; }
		});
		float tx = 12; foreach (var (t, on) in new[] { ("Schiffe", true), ("Stationen", false), ("Verträge", false), ("Lager", false) }) { At(c, B(t, 130, 40, on ? Act : null, 17, null, 8, on ? Cyan : null), tx, 84); tx += 136; }
		float y = 132;
		int i = 0;
		foreach (var (n, model, where, state, hull, fuel, here, docked) in Owned)
		{
			bool sel = i == 1;
			var cn = n; var cm = model; var cw = where; var cs = state; var ch = hull; var cf = fuel; var chere = here; var cd = docked;
			var row = Win(c, 12, y, 1018, 88, chere ? new Color(0.12f, 0.22f, 0.4f, 0.95f) : (sel ? new Color(0.1f, 0.18f, 0.32f, 0.95f) : Card), chere ? Cyan : (sel ? Edge : Edge), 10);
			G(row, 0, 0, 1018, 88, g =>
			{
				Ship(g, new Vector2(54, 46), 28, new Color(0.25f, 0.33f, 0.5f), Cyan);
				g.Text(new Vector2(110, 36), cn, 22, White);
				g.Text(new Vector2(110, 60), cm, 14, Dim);
				g.Text(new Vector2(110, 82), cw, 14, cd ? Cyan : Orange);
				g.Text(new Vector2(420, 36), cs, 15, chere ? Cyan : White);
				g.Text(new Vector2(420, 60), cd ? "angedockt" : "im Flug / draußen", 13, Dim);
				g.Text(new Vector2(640, 36), "Hülle", 13, Dim); g.DrawRect(new Rect2(700, 26, 120, 10), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(700, 26, 120 * ch / 100f, 10), Green);
				g.Text(new Vector2(640, 62), "Sprit", 13, Dim); g.DrawRect(new Rect2(700, 52, 120, 10), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(700, 52, 120 * cf / 100f, 10), Orange);
				if (chere) g.TextR(1000, 36, "AKTIV", 14, Cyan); else g.TextR(1000, 36, "Tippen ▸", 14, Dim);
				if (!chere && cd) g.TextR(1000, 62, "Liegegebühr 12 Cr/Tag", 12, Dim);
			});
			y += 94; i++;
		}
		At(c, L("Liegegebühr nur an fremden Stationen; eigene Stationen sind gratis.", 13, Dim), 20, 514);
	}

	public static void AssetDetail(Control root, ProtoData d)
	{
		ProtoVariants.Dim(root, 0.55f);
		var w = Win(root, 190, 70, 900, 580, PanelSolid, Cyan, 16);
		G(w, 0, 0, 900, 580, g =>
		{
			Ship(g, new Vector2(90, 100), 50, new Color(0.25f, 0.33f, 0.5f), Cyan);
			g.Text(new Vector2(170, 52), "Mule", 32, White);
			g.Text(new Vector2(170, 80), "Leichter Frachter · Güte: Verbessert", 16, Dim);
			g.Text(new Vector2(170, 108), "Im Flug nach Beta · Ankunft in 2 min", 16, Orange);
			g.Text(new Vector2(30, 190), "Zustand", 17, Cyan);
			float y = 218; foreach (var (n, v, col, t) in new[] { ("Hülle", 96f, Green, "96"), ("Schild", 40f, Cyan, "40"), ("Sprit", 88f, Orange, "88 %"), ("Fracht", 92f, Cyan, "110/120") })
			{ g.Text(new Vector2(30, y + 14), n, 14, Dim); g.DrawRect(new Rect2(110, y + 4, 170, 11), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(110, y + 4, 170 * v / 100f, 11), col); g.Text(new Vector2(292, y + 14), t, 14, White); y += 30; }
			g.Text(new Vector2(30, 362), "Module", 17, Cyan);
			g.Text(new Vector2(30, 388), "Reaktor Mk1 · Chemischer Antrieb Mk1 · Frachtmodul ×2", 14, White);
			g.Text(new Vector2(30, 410), "Kanisterraum · Schild Mk1 · Leistung 9 von 12", 14, White);
			g.Text(new Vector2(30, 450), "Auftrag", 17, Cyan);
			g.Text(new Vector2(30, 476), "Handel Alpha ↔ Beta (Stahl) im Verband Alpha-Handel", 14, White);
			g.Text(new Vector2(30, 498), "Unterhalt 14 Cr/Tag · Wert ≈ 9.800 Cr", 14, Dim);
			// Wechsel-Hinweis rechts
			g.Text(new Vector2(500, 190), "Wenn du wechselst", 17, Cyan);
		});
		var note = Win(w, 490, 204, 380, 260, Card, null, 10);
		G(note, 0, 0, 380, 260, g =>
		{
			g.Text(new Vector2(16, 30), "Du übernimmst Mule dort, wo es ist.", 15, White);
			g.Text(new Vector2(16, 58), "Falke bleibt an Ort und Stelle:", 15, White);
			g.Text(new Vector2(16, 84), "im Hangar Alpha, Liegegebühr 12 Cr/Tag", 14, Dim);
			g.Text(new Vector2(16, 118), "Das Geld bleibt bei dir (Kasse der Person).", 14, Dim);
			g.Text(new Vector2(16, 144), "Fracht und Treibstoff bleiben im jeweiligen Schiff.", 14, Dim);
			g.Text(new Vector2(16, 182), "Mule fliegt gerade: Du steigst im Flug um.", 14, Orange);
			g.Text(new Vector2(16, 206), "Der alte Befehl wird beendet, Mule hält Position.", 13, Dim);
			g.Text(new Vector2(16, 232), "Im freien Raum bleibt ein Schiff ungeschützt.", 13, Orange);
		});
		At(w, B("In dieses Schiff wechseln", 380, 62, BuyC, 21, null, 12, Green), 490, 480);
		At(w, B("Befehle", 120, 50, null, 16, null, 10), 30, 524);
		At(w, B("Heim schicken", 160, 50, null, 16, null, 10), 160, 524);
		At(w, B("Verkaufen", 140, 50, null, 16, null, 10), 330, 524);
		At(w, B("Schließen ✕", 150, 44, null, 15, null, 10), 730, 16);
	}

	// ───────────── Comms ─────────────
	public static void Comms(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Comms", "Nachrichten und Verhandlungen · 3 neu");
		var inbox = V(6); At(c, inbox, 12, 12, 340, 516);
		int i = 0;
		foreach (var (from, subj, col, isNew) in new[] { ("Terra · Zoll", "Zollbescheid Delta-Route", Terra, true), ("Teck · Grenzwache", "Warnung: Sperrgebiet", Teck, true), ("Aurum · Gilde", "Angebot: Handelsvertrag", Aurum, true), ("Freie Händler", "Preisinfo Titan", Frei, false) })
		{
			bool sel = i == 2;
			var row = Pnl(sel ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card, sel ? Cyan : null, 8, 4);
			row.CustomMinimumSize = new Vector2(332, 70);
			inbox.AddChild(row);
			var cf = from; var cs = subj; var cc = col; var cn = isNew;
			G(row, 0, 0, 324, 62, g => { g.DrawCircle(new Vector2(16, 20), 8, cc); g.Text(new Vector2(34, 26), cf, 16, White); g.Text(new Vector2(34, 50), cs, 13, Dim); if (cn) g.DrawCircle(new Vector2(304, 20), 6, Orange); });
			i++;
		}
		inbox.AddChild(B("Kontakt aufnehmen …", 332, 48, null, 16, null, 10));
		var dlg = Win(c, 364, 12, 666, 516, Card, null, 10);
		G(dlg, 0, 0, 666, 516, g =>
		{
			g.DrawRect(new Rect2(16, 16, 64, 64), new Color(0.12f, 0.2f, 0.34f)); g.DrawRect(new Rect2(16, 16, 64, 64), Aurum, false, 2);
			g.Text(new Vector2(94, 38), "Handelsgilde Aurum", 22, White);
			g.Text(new Vector2(94, 62), "Gildenmeisterin Ilka Voss · Ruf +34", 14, Dim);
			g.DrawRect(new Rect2(16, 96, 634, 74), new Color(0.06f, 0.1f, 0.18f, 0.95f)); g.DrawRect(new Rect2(16, 96, 634, 74), Edge, false, 1.5f);
			g.Text(new Vector2(30, 124), "„Pilot, wir beobachten deine Lieferungen nach Gamma. Die Gilde", 15, White);
			g.Text(new Vector2(30, 146), "bietet dir einen Vertrag: weniger Zoll, dafür feste Mengen.“", 15, White);
			g.Text(new Vector2(16, 200), "Deine Antwort", 16, Cyan);
		});
		float y = 212;
		foreach (var (t, eff, enabled, kind) in new[]
		{
			("Handelsvertrag annehmen", "Zoll bei Aurum −5 % · Pflicht: 40 Stahl je Woche · Ruf +4", true, 0),
			("Zolltarif verhandeln", "Chance 55 % · bei Misserfolg Ruf −2", true, 0),
			("Passierschein Delta kaufen", "300 Cr · gilt 3 Tage · Gate Gamma–Delta", true, 0),
			("Schutzvertrag anfragen", "120 Cr je Tag · Patrouille deiner Stationen", false, 0),
			("Krieg erklären", "Fehde mit Aurum · Ruf −60 · nur mit Kriegsgrund", true, 1),
		})
		{
			var b = B("", 634, 52, kind == 1 ? new Color(0.3f, 0.08f, 0.08f, 0.95f) : (enabled ? null : new Color(0.08f, 0.12f, 0.2f, 0.6f)), 16, null, 10, kind == 1 ? Red : (enabled ? Edge : new Color(Dim, 0.3f)));
			At(dlg, b, 16, y);
			var l1 = L(t, 17, enabled ? White : Dim); l1.Position = new Vector2(14, 3); b.AddChild(l1);
			var l2 = L(eff, 12, kind == 1 ? Red : Dim); l2.Position = new Vector2(14, 27); b.AddChild(l2);
			y += 58;
		}
		At(dlg, L("Ignorieren kostet keinen Ruf, das Angebot verfällt in 2 Tagen.", 12, Dim), 16, 496);
	}
}
