using Godot;
using System.Linq;
using static SpaceAdventure.Prototypes.K;

namespace SpaceAdventure.Prototypes;

/// <summary>Finale Oberfläche: Kontext-HUD, kleines Andock-Menü und Dock-Bereiche (Handel, Tanken, Ausrüster, Hangar, Info, Taxi).</summary>
public static class FinalUi
{
	public static readonly string[] Screens = { "hud", "bays", "menu", "handel", "kacheln", "tanken", "ausruester", "hangar", "info", "taxi" };

	static readonly Color Act = new(0.2f, 0.38f, 0.62f, 0.95f);
	static readonly Color BuyC = new(0.15f, 0.45f, 0.28f, 0.95f);
	static readonly Color SellC = new(0.5f, 0.28f, 0.12f, 0.95f);
	static readonly Color Card = new(0.02f, 0.04f, 0.09f, 0.88f);
	static readonly string[] Rail = { "Handel", "Reparatur & Tanken", "Ausrüster", "Werft", "Hangar", "Station-Info", "Taxi" };

	public static void Build(string name, Control root, ProtoData d)
	{
		switch (name)
		{
			case "hud": Hud(root, d); break;
			case "bays": Bays(root, d); break;
			case "menu": Menu(root, d); break;
			case "handel": Handel(root, d); break;
			case "kacheln": Kacheln(root, d); break;
			case "tanken": Tanken(root, d); break;
			case "ausruester": Ausruester(root, d); break;
			case "hangar": Hangar(root, d); break;
			case "info": Info(root, d); break;
			case "taxi": Taxi(root, d); break;
		}
	}

	static void StickUnused(Control root, float x, float y)
		=> G(root, x, y, 220, 220, g => { var c = new Vector2(110, 110); g.DrawCircle(c, 100, new Color(1, 1, 1, 0.07f)); g.DrawArc(c, 100, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.35f), 2); g.DrawCircle(c, 38, new Color(1, 1, 1, 0.28f)); });

	// ───────────── Flug-HUD ─────────────
	static void Wedge(Gfx g, Vector2 c, float r, float centerDeg, float halfDeg, Color fill, Color edge)
	{
		var pts = new System.Collections.Generic.List<Vector2> { c };
		const int n = 28;
		for (int i = 0; i <= n; i++)
		{
			float a = Mathf.DegToRad(centerDeg - halfDeg + 2 * halfDeg * i / n - 90);
			pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
		}
		g.DrawColoredPolygon(pts.ToArray(), fill);
		pts.Add(c);
		g.DrawPolyline(pts.ToArray(), edge, 1.5f, true);
	}

	enum BayState { InArc, Outside, Reloading, NoTarget }

	/// <summary>Eine Taste je Waffenschacht: Name, Feuerwinkel als Mini-Symbol und Zustand (im Winkel, außerhalb, lädt nach).</summary>
	static void BayButton(Control parent, float x, float y, string name, string place, float centerDeg, float halfDeg, BayState st, string detail, float reload = 0f)
	{
		var (border, fill, txt) = st switch
		{
			BayState.InArc => (Green, new Color(0.1f, 0.34f, 0.2f, 0.95f), Green),
			BayState.Outside => (Orange, new Color(0.3f, 0.2f, 0.06f, 0.92f), Orange),
			BayState.Reloading => (Dim, new Color(0.08f, 0.12f, 0.2f, 0.92f), Dim),
			_ => (new Color(Dim, 0.4f), new Color(0.05f, 0.08f, 0.14f, 0.85f), Dim),
		};
		var w = Win(parent, x, y, 176, 68, fill, border, 12);
		var cn = name; var cp = place; var cd = detail; var cs = st; var cc = centerDeg; var ch = halfDeg; var cr = reload; var tc = txt; var bc = border;
		G(w, 0, 0, 176, 68, g =>
		{
			var c = new Vector2(30, 36);
			g.DrawCircle(c, 22, new Color(0, 0, 0, 0.35f));
			Wedge(g, c, 20, cc, ch, new Color(bc, 0.45f), bc);
			g.DrawCircle(c, 3.5f, White);
			g.Text(new Vector2(62, 28), cn, 17, White);
			g.Text(new Vector2(62, 46), cp, 12, Dim);
			g.Text(new Vector2(62, 62), cd, 13, tc);
			if (cs == BayState.Reloading) { g.DrawRect(new Rect2(62, 31, 100, 4), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(62, 31, 100 * cr, 4), Cyan); }
		});
	}

	public static void Hud(Control root, ProtoData d)
	{
		var shipC = new Vector2(640, 362);
		// Feuerwinkel um das Schiff
		G(root, 0, 0, 1280, 720, g =>
		{
			Wedge(g, shipC, 290, 0, 40, new Color(Orange, 0.07f), new Color(Orange, 0.45f));
			Wedge(g, shipC, 290, -90, 50, new Color(Green, 0.10f), new Color(Green, 0.6f));
			Wedge(g, shipC, 290, 90, 50, new Color(Dim, 0.05f), new Color(Dim, 0.3f));
			var tgt = new Vector2(232, 296);
			g.DrawLine(shipC + (tgt - shipC).Normalized() * 40, tgt, new Color(Green, 0.8f), 2f, true);
			g.Text(new Vector2(410, 310), "81° ◀", 16, Green);
		});
		G(root, 188, 252, 88, 88, g => Brackets(g, new Rect2(4, 4, 80, 80), 16, Green, 2));

		// Status oben links
		var st = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.68f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 10, 10), 14, 12, 300, 132);
		var sv = V(5); st.AddChild(sv);
		foreach (var (n, v, c, t) in new[] { ("Hülle", d.Hull, Green, "82"), ("Schild", d.Shield, Cyan, "64"), ("Sprit", d.Fuel * 100, Orange, "72"), ("Leistung", 75f, Dim, "9/12") })
		{
			var r = H(8); sv.AddChild(r);
			var l = L(n, 16, Dim); l.CustomMinimumSize = new Vector2(66, 0); r.AddChild(l);
			var b = Bar(v, c, 156, 12); b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; r.AddChild(b);
			r.AddChild(L(t, 16, White));
		}
		At(root, L($"{N0(d.PlayerCredits)} Credits", 20, Orange), 20, 150);

		// Sektorchip und Funkfeed oben Mitte
		var chip = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.68f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 20, 6), 420, 12, 430, 42);
		var cr = H(12); chip.AddChild(cr);
		cr.AddChild(L(d.Sector, 18, Cyan)); cr.AddChild(L("Terra · Kontrolle", 15, Dim)); cr.AddChild(L("Sicherheit ▮▮▮▯", 15, Green));
		var feed = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.6f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 8, 8), 420, 62, 330, 56);
		var fr = H(10); feed.AddChild(fr);
		var face = Pnl(new Color(0.12f, 0.2f, 0.34f), Edge, 6, 0); face.CustomMinimumSize = new Vector2(36, 36); fr.AddChild(face);
		var fv = V(0); fr.AddChild(fv);
		fv.AddChild(L("Hafenkontrolle", 14, Cyan)); fv.AddChild(L("Erzfrachter Mule: Route frei", 13, White));

		// Karte und Zeit oben rechts
		At(root, B("Karte", 110, 50, null, 18, null, 25), 1030, 12);
		At(root, B("Zeit ×1", 110, 50, null, 17, null, 25), 1150, 12);

		// Zielkarte mit Radar und Schnellschaltern
		var tg = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.72f), Green, 12, 12), 930, 72, 340, 132);
		var tv = V(2); tg.AddChild(tv);
		tv.AddChild(L("Asteroid Ae-14 · Eisenerz", 18, Green));
		tv.AddChild(L("Erz 340 · 410 m · Winkel 81° links", 15, Dim));
		tv.AddChild(L("Im Feuerwinkel: Laser 2", 15, White));
		tv.AddChild(L("Ertrag/Schuss ▮▮▮▯▯  Chance 62 %", 14, Dim));
		G(root, 1196, 80, 64, 64, g => { var c = new Vector2(32, 32); g.DrawCircle(c, 30, new Color(0, 0.08f, 0.04f, 0.8f)); g.DrawArc(c, 30, 0, Mathf.Tau, 48, Green, 2); g.DrawArc(c, 15, 0, Mathf.Tau, 32, new Color(Green, 0.4f), 1); g.DrawCircle(c, 3, Cyan); g.DrawCircle(c + new Vector2(-24, -6), 3, Green); g.DrawCircle(c + new Vector2(10, -12), 3, Orange); });
		At(root, B("Autopilot", 160, 42, null, 16, null, 21), 930, 212);
		At(root, B("Auto-Mining", 170, 42, null, 16, null, 21), 1100, 212);

		// ── Steuerung: Wenden und Hamburger links, Tempo-Slider unten rechts am Bildrand ──
		G(root, 660, 646, 600, 68, g =>
		{
			g.Text(new Vector2(0, 28), "TEMPO", 14, Dim);
			g.Text(new Vector2(0, 50), "148 m/s", 18, White);
			float left = 100, right = 570, cy = 30, pct = 0.62f;
			float tx = left + pct * (right - left);
			g.DrawRect(new Rect2(left, cy - 7, right - left, 14), new Color(1, 1, 1, 0.16f));
			g.DrawRect(new Rect2(left, cy - 7, tx - left, 14), new Color(Cyan, 0.8f));
			foreach (var (lab, f) in new[] { ("STOPP", 0f), ("25", 0.25f), ("50", 0.5f), ("75", 0.75f), ("MAX", 1f) })
			{
				float x = left + f * (right - left);
				g.DrawLine(new Vector2(x, cy + 11), new Vector2(x, cy + 21), new Color(1, 1, 1, 0.5f), 2);
				g.Text(new Vector2(x - 30, cy + 36), lab, 12, Dim, HorizontalAlignment.Center, 60);
			}
			var th = new Rect2(tx - 32, cy - 19, 64, 38);
			g.DrawRect(th, new Color(0.2f, 0.38f, 0.62f, 0.97f)); g.DrawRect(th, Cyan, false, 2);
			g.Text(new Vector2(tx - 32, cy + 6), "62 %", 18, White, HorizontalAlignment.Center, 64);
		});
		At(root, L("WENDEN", 13, Dim, HorizontalAlignment.Center), 100, 560, 260, 20);
		At(root, B("◀", 120, 100, null, 44, null, 20), 96, 584);
		At(root, B("▶", 120, 100, null, 44, null, 20), 230, 584);
		var hb = At(root, B("", 60, 60, null, 24, null, 30), 16, 644);
		G(hb, 0, 0, 60, 60, g => { foreach (var yy in new[] { 20f, 30f, 40f }) g.DrawRect(new Rect2(16, yy, 28, 4), White); });

		// ── Waffen rechts, je Bay eine Taste ──
		float by = 296;
		BayButton(root, 1090, by, "Laser 1", "Bug · ±40°", 0, 40, BayState.Outside, "◀ 41° drehen"); by += 76;
		BayButton(root, 1090, by, "Laser 2", "Backbord · ±50°", -90, 50, BayState.InArc, "FEUER"); by += 76;
		BayButton(root, 1090, by, "Laser 3", "Steuerbord · ±50°", 90, 50, BayState.Outside, "außerhalb"); by += 76;
		BayButton(root, 1090, by, "Traktor", "Bug · ±30°", 0, 30, BayState.NoTarget, "keine Beute"); by += 76;
		var all = B("Alle\nFeuer", 120, 120, new Color(0.15f, 0.5f, 0.3f, 0.95f), 20, null, 60, Green);
		At(root, all, 940, 440);
		At(root, L("1 von 3 bereit", 13, Green, HorizontalAlignment.Center), 940, 564, 120, 20);

		var toast = At(root, Pnl(new Color(0.03f, 0.1f, 0.06f, 0.8f), Green, 18, 6), 420, 596, 340, 44);
		toast.AddChild(L("Verkauft für 640 Credits", 20, Green, HorizontalAlignment.Center));
	}

	// ───────────── Legende der Laser-Tasten ─────────────
	public static void Bays(Control root, ProtoData d)
	{
		ProtoVariants.Dim(root, 0.88f);
		G(root, 40, 20, 1200, 60, g =>
		{
			g.Text(new Vector2(0, 30), "Laser-Tasten und Feuerwinkel", 28, Cyan);
			g.Text(new Vector2(0, 54), "Jede Waffenschacht hat eine eigene Taste. Das Symbol zeigt ihren Winkel, die Farbe, ob das Ziel gerade im Winkel liegt.", 16, Dim);
		});
		// Winkeldiagramm
		var diag = Win(root, 40, 100, 520, 580, Card, Edge, 12);
		G(diag, 0, 0, 520, 580, g =>
		{
			var c = new Vector2(260, 300);
			Wedge(g, c, 220, 0, 40, new Color(Orange, 0.12f), new Color(Orange, 0.7f));
			Wedge(g, c, 220, -90, 50, new Color(Green, 0.16f), new Color(Green, 0.8f));
			Wedge(g, c, 220, 90, 50, new Color(Dim, 0.07f), new Color(Dim, 0.4f));
			Ship(g, c, 34, new Color(0.25f, 0.33f, 0.5f), Cyan);
			g.Text(new Vector2(c.X - 60, c.Y - 232), "Laser 1 · Bug", 14, Orange, HorizontalAlignment.Center, 120);
			g.Text(new Vector2(c.X - 232, c.Y - 4), "Laser 2", 14, Green, HorizontalAlignment.Left);
			g.Text(new Vector2(c.X + 140, c.Y - 4), "Laser 3", 14, Dim, HorizontalAlignment.Left);
			var t = c + new Vector2(-170, -28);
			g.DrawCircle(t, 9, Green); g.DrawArc(t, 15, 0, Mathf.Tau, 24, Green, 2);
			g.Text(t + new Vector2(-30, -22), "Ziel", 14, Green);
			g.Text(new Vector2(0, 560), "Beispiel: Ziel liegt 81° links, also nur im Winkel von Laser 2", 14, Dim, HorizontalAlignment.Center, 520);
			g.Text(new Vector2(16, 30), "Draufsicht, Bug oben", 15, Dim);
		});
		// Zustände
		float y = 100;
		var rows = new (string, string, BayState, string, float, float, float)[]
		{
			("Im Winkel und bereit", "Grün. Die Taste feuert sofort.", BayState.InArc, "FEUER", 0, -90, 50),
			("Ziel außerhalb", "Orange. Der Hinweis nennt, wie weit zu drehen ist. Die Taste ist gesperrt.", BayState.Outside, "◀ 41° drehen", 0, 0, 40),
			("Lädt nach", "Grau mit Balken. Nach dem Schuss, bis die Waffe wieder bereit ist.", BayState.Reloading, "lädt nach", 0.6f, 90, 50),
			("Kein Ziel oder keine Beute", "Gedämpft. Zum Beispiel der Traktor ohne Brocken in Reichweite.", BayState.NoTarget, "keine Beute", 0, 0, 30),
		};
		foreach (var (t, desc, st, detail, rl, cd, hd) in rows)
		{
			var card = Win(root, 590, y, 650, 126, Card, Edge, 12);
			BayButton(card, 18, 29, "Laser", "Bug · ±40°", cd, hd, st, detail, rl);
			var ct = t; var cdesc = desc;
			G(card, 214, 0, 420, 126, g =>
			{
				g.Text(new Vector2(0, 46), ct, 22, White);
				var words = cdesc.Split(' '); string line = ""; float yy = 76;
				foreach (var w in words)
				{
					if ((line + " " + w).Length > 46) { g.Text(new Vector2(0, yy), line, 15, Dim); yy += 22; line = w; }
					else line = line.Length == 0 ? w : line + " " + w;
				}
				g.Text(new Vector2(0, yy), line, 15, Dim);
			});
			y += 138;
		}
		G(root, 590, 656, 650, 40, g => g.Text(new Vector2(0, 26), "„Alle Feuer“ löst nur Waffen aus, die im Winkel liegen und bereit sind.", 15, Dim));
	}

	// ───────────── Kleines Andock-Menü ─────────────
	static void ShipChip(Control parent, float x, float y, string name, string sub, float hull, bool sel, bool foreign)
	{
		var w = Win(parent, x, y, 122, 92, sel ? new Color(0.14f, 0.26f, 0.46f, 0.98f) : new Color(0.04f, 0.07f, 0.13f, 0.95f), sel ? Cyan : (foreign ? new Color(Dim, 0.4f) : Edge), 10);
		G(w, 0, 0, 122, 92, g =>
		{
			Ship(g, new Vector2(61, 28), 20, foreign ? new Color(0.25f, 0.28f, 0.34f) : new Color(0.25f, 0.33f, 0.5f), foreign ? Dim : Cyan);
			g.Text(new Vector2(0, 62), name, 15, White, HorizontalAlignment.Center, 122);
			g.Text(new Vector2(0, 78), sub, 12, Dim, HorizontalAlignment.Center, 122);
			g.DrawRect(new Rect2(14, 84, 94, 4), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(14, 84, 94 * hull / 100f, 4), hull > 60 ? Green : Orange);
		});
	}

	public static void Menu(Control root, ProtoData d)
	{
		ProtoVariants.Dim(root, 0.62f);
		var card = Win(root, 350, 36, 580, 648, PanelSolid, Edge, 16);
		G(card, 20, 14, 540, 66, g =>
		{
			g.Text(new Vector2(0, 28), d.Station, 26, Cyan);
			g.Text(new Vector2(0, 52), $"{d.Sector} · {d.Faction} · Ruf freundlich · Liegeplatz 3", 15, Dim);
		});
		G(card, 20, 86, 540, 22, g => g.Text(new Vector2(0, 16), "ANGEDOCKT", 12, Dim));
		float cx = 20;
		foreach (var (n, sub, hull, sel, foreign) in new[] { ("Falke", "Orion-A · aktiv", 82f, true, false), ("Mule", "Frachter · Handel", 96f, false, false), ("Pickaxe", "Schürfer · Auto", 71f, false, false), ("Hauler", "Terra · fremd", 100f, false, true) })
		{ ShipChip(card, cx, 110, n, sub, hull, sel, foreign); cx += 130; }

		var items = new[]
		{
			("Handel", "8 Waren · Preise steigen", true), ("Reparatur & Tanken", "Hülle 82 % · Sprit 72 %", false),
			("Ausrüster", "3 Module im Lager", false), ("Werft", "Bausatz verfügbar", false),
			("Hangar", "4 Schiffe", false), ("Station-Info", "Rezepte, Zoll, Ruf", false),
			("Taxi", "ab 40 Cr · du reist allein", false), ("Aufträge", "2 offen", false),
		};
		for (int i = 0; i < items.Length; i++)
		{
			var (n, sub, hi) = items[i];
			float bx = 20 + (i % 2) * 272, by = 220 + (i / 2) * 78;
			var b = B("", 262, 68, hi ? Act : null, 18, null, 10, hi ? Cyan : null);
			At(card, b, bx, by);
			var l1 = L(n, 20, White); l1.Position = new Vector2(16, 8); b.AddChild(l1);
			var l2 = L(sub, 14, Dim); l2.Position = new Vector2(16, 38); b.AddChild(l2);
		}
		At(card, B("Abdocken", 540, 64, new Color(0.15f, 0.5f, 0.3f, 0.95f), 24, null, 12, Green), 20, 540);
		G(card, 20, 612, 540, 28, g => g.Text(new Vector2(0, 18), "Liegegebühr 0 Cr · Hülle repariert beim Andocken", 13, Dim, HorizontalAlignment.Center, 540));
	}

	// ───────────── Gemeinsames Gerüst der Dock-Bereiche ─────────────
	static Control Shell(Control root, ProtoData d, string active, bool showShip, float centerW = 0)
	{
		ProtoVariants.Dim(root, 0.82f);
		G(root, 20, 10, 1240, 64, g =>
		{
			g.Text(new Vector2(0, 32), d.Station, 28, Cyan);
			g.Text(new Vector2(0, 56), $"{d.Type} · {d.Faction} · Ruf freundlich", 16, Dim);
			g.TextR(1240, 32, $"{N0(d.PlayerCredits)} Credits", 26, Orange);
			g.TextR(1240, 56, $"Kasse der Station {N0(d.StationCredits)}", 16, Dim);
		});
		var rail = At(root, Pnl(PanelBg, null, 10, 8), 14, 84, 200, 540);
		var rv = V(6); rail.AddChild(rv);
		foreach (var t in Rail)
			rv.AddChild(B(t, 184, 56, t == active ? Act : null, 17, null, 8, t == active ? Cyan : null));
		var sp = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; rv.AddChild(sp);
		rv.AddChild(L("Schiff: Falke (aktiv)", 13, Dim));
		float cw = centerW > 0 ? centerW : (showShip ? 690 : 1040);
		var center = Win(root, 224, 84, cw, 540, PanelBg, Edge, 10);
		if (showShip) ShipPanel(root, d, 224 + cw + 10, 84, 1266 - (224 + cw + 10));
		At(root, B("← Menü", 160, 58, null, 20, null, 10), 14, 648);
		At(root, B("Abdocken", 200, 58, new Color(0.15f, 0.5f, 0.3f, 0.95f), 22, null, 10, Green), 1066, 648);
		return center;
	}

	static void ShipPanel(Control root, ProtoData d, float x, float y, float w)
	{
		var ship = At(root, Pnl(PanelBg, null, 10, 12), x, y, w, 540);
		var sv = V(7); ship.AddChild(sv);
		sv.AddChild(L("Dein Schiff", 20, Cyan));
		var sk = new Gfx { CustomMinimumSize = new Vector2(w - 28, 110), MouseFilter = Control.MouseFilterEnum.Ignore };
		sk.Paint = g => Ship(g, new Vector2((w - 28) / 2, 56), 44, new Color(0.25f, 0.33f, 0.5f), Cyan);
		sv.AddChild(sk);
		foreach (var (n, v, c, t) in new[] { ("Hülle", d.Hull, Green, "82"), ("Schild", d.Shield, Cyan, "64"), ("Sprit", d.Fuel * 100, Orange, "72 %") })
		{
			var r = H(8); sv.AddChild(r);
			var l = L(n, 15, Dim); l.CustomMinimumSize = new Vector2(58, 0); r.AddChild(l);
			var b = Bar(v, c, w - 150, 12); b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; r.AddChild(b);
			r.AddChild(L(t, 15, White));
		}
		sv.AddChild(L("Laderaum", 15, Dim));
		sv.AddChild(Bar(d.CargoUsed, Cyan, w - 28, 14, d.CargoMax));
		sv.AddChild(L("Fest 40/60 · Kanister 12/40", 14, Dim));
		sv.AddChild(L("An Bord", 15, Dim));
		foreach (var r in d.Rows.Where(q => q.Held > 0).Take(4)) sv.AddChild(L($"■ {r.Name}  {N0(r.Held)}", 15, r.Color.Lerp(White, 0.5f)));
	}

	// ───────────── Handel (Liste) ─────────────
	public static void Handel(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Handel", true);
		var mv = V(4); At(c, mv, 10, 8, 670, 524);
		var tabs = H(6); mv.AddChild(tabs);
		foreach (var (t, on) in new[] { ("Kaufen", true), ("Verkaufen", false), ("Alle", false) }) tabs.AddChild(B(t, 116, 40, on ? Act : null, 17, null, 8));
		tabs.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		tabs.AddChild(B("Liste", 84, 40, Act, 16, null, 8)); tabs.AddChild(B("Kacheln", 92, 40, null, 16, null, 8));
		int i = 0;
		foreach (var r in d.Rows.Take(6))
		{
			var (tl, tc) = Trend(r);
			bool open = i == 0;
			var card = Pnl(open ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card, open ? Cyan : null, 8, 4);
			card.CustomMinimumSize = new Vector2(660, open ? 128 : 52);
			mv.AddChild(card);
			var cr = r; var o = open; var tlc = tl; var tcc = tc;
			G(card, 0, 0, 650, open ? 120 : 44, g =>
			{
				g.DrawRect(new Rect2(6, 12, 18, 18), cr.Color);
				g.Text(new Vector2(34, 29), cr.Name, 20, White);
				g.Text(new Vector2(220, 29), N1(cr.Price), 20, tcc);
				g.Text(new Vector2(296, 29), tlc, 15, tcc);
				float fill = Mathf.Clamp(cr.Amount / Mathf.Max(1, cr.Target), 0, 1.4f) / 1.4f;
				g.DrawRect(new Rect2(382, 16, 100, 10), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(382, 16, 100 * fill, 10), cr.Color);
				g.Text(new Vector2(490, 29), N0(cr.Amount), 17, Dim);
				g.Text(new Vector2(560, 29), cr.Held > 0 ? $"Bord {N0(cr.Held)}" : "", 17, Cyan);
				if (o)
				{
					g.Text(new Vector2(34, 66), "Menge", 16, Dim);
					g.DrawRect(new Rect2(100, 48, 56, 44), new Color(0.1f, 0.18f, 0.32f)); g.Text(new Vector2(128, 78), "−", 26, White, HorizontalAlignment.Center, 0);
					g.DrawRect(new Rect2(160, 48, 90, 44), new Color(0, 0, 0, 0.5f)); g.Text(new Vector2(205, 78), "10", 26, White, HorizontalAlignment.Center, 0);
					g.DrawRect(new Rect2(254, 48, 56, 44), new Color(0.1f, 0.18f, 0.32f)); g.Text(new Vector2(282, 78), "+", 26, White, HorizontalAlignment.Center, 0);
					g.Text(new Vector2(330, 78), "1   10   50   Max", 18, Dim);
					g.Text(new Vector2(34, 108), $"Ø {N1(cr.Price * 1.03f)} · Summe {N0(cr.Price * 10.3f)} · beste bekannte Stelle: Beta {N1(cr.Price * 1.4f)} (+{N1(cr.Price * 0.37f)})", 15, Dim);
					g.DrawRect(new Rect2(490, 48, 150, 44), new Color(0.15f, 0.45f, 0.28f)); g.Text(new Vector2(565, 78), "Kaufen", 22, White, HorizontalAlignment.Center, 0);
				}
			});
			i++;
		}
		mv.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
		var fr = H(8); mv.AddChild(fr);
		fr.AddChild(B("Alles verkaufen", 220, 52, null, 18, null, 10)); fr.AddChild(B("Rückgängig", 160, 52, null, 18, null, 10));
	}

	// ───────────── Handel (Kacheln) ─────────────
	public static void Kacheln(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Handel", true);
		var tabs = H(6); At(c, tabs, 10, 8, 670, 40);
		foreach (var (t, on) in new[] { ("Kaufen", true), ("Verkaufen", false), ("Alle", false) }) tabs.AddChild(B(t, 116, 40, on ? Act : null, 17, null, 8));
		tabs.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		tabs.AddChild(B("Liste", 84, 40, null, 16, null, 8)); tabs.AddChild(B("Kacheln", 92, 40, Act, 16, null, 8));
		int i = 0;
		foreach (var r in d.Rows.Take(6))
		{
			int col = i % 3, row = i / 3;
			float tx = 10 + col * 224, ty = 56 + row * 150;
			bool sel = i == 0; var cap = r; var (tl, tc) = Trend(r);
			var tile = Win(c, tx, ty, 214, 140, sel ? new Color(0.12f, 0.22f, 0.4f, 0.97f) : new Color(0.04f, 0.07f, 0.13f, 0.9f), sel ? Cyan : Edge, 10);
			G(tile, 0, 0, 214, 140, g =>
			{
				g.DrawRect(new Rect2(0, 0, 214, 7), cap.Color);
				g.Text(new Vector2(12, 34), cap.Name, 20, White);
				g.Text(new Vector2(12, 74), N1(cap.Price), 30, tc);
				g.Text(new Vector2(110, 72), tl, 14, tc);
				float fill = Mathf.Clamp(cap.Amount / Mathf.Max(1, cap.Target), 0, 1.4f) / 1.4f;
				g.DrawRect(new Rect2(12, 90, 190, 8), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(12, 90, 190 * fill, 8), cap.Color);
				g.Text(new Vector2(12, 124), $"Bestand {N0(cap.Amount)}", 14, Dim);
				if (cap.Held > 0) { g.DrawRect(new Rect2(128, 108, 74, 24), new Color(Cyan, 0.25f)); g.Text(new Vector2(128, 126), $"Bord {N0(cap.Held)}", 14, Cyan, HorizontalAlignment.Center, 74); }
			});
			i++;
		}
		var sr = d.Rows[0];
		var sheet = Win(c, 10, 362, 670, 168, new Color(0.06f, 0.1f, 0.18f, 0.98f), Cyan, 12);
		G(sheet, 0, 0, 670, 168, g =>
		{
			g.DrawRect(new Rect2(14, 16, 18, 18), sr.Color);
			g.Text(new Vector2(40, 32), sr.Name, 24, White);
			g.Text(new Vector2(14, 62), $"Preis {N1(sr.Price)} · normal", 15, Dim);
			g.Text(new Vector2(14, 84), $"Bestand {N0(sr.Amount)} von {N0(sr.Target)}", 15, Dim);
			g.Text(new Vector2(14, 106), "Beta zahlt 33,6 (+8,9)", 14, Dim);
			g.Text(new Vector2(14, 140), $"Summe {N0(sr.Price * 10)} Cr", 19, Orange);
		});
		var qv = H(6); At(sheet, qv, 230, 14, 430, 50);
		qv.AddChild(B("−", 46, 50, null, 24, null, 8)); qv.AddChild(B("10", 80, 50, new Color(0, 0, 0, 0.5f), 24, null, 8, Dim)); qv.AddChild(B("+", 46, 50, null, 24, null, 8));
		foreach (var q in new[] { "1", "10", "50" }) qv.AddChild(B(q, 46, 50, null, 16, null, 8));
		qv.AddChild(B("Max", 62, 50, null, 16, null, 8));
		var av = H(8); At(sheet, av, 230, 90, 430, 60);
		av.AddChild(B("Kaufen", 206, 58, BuyC, 20, null, 10, Green)); av.AddChild(B("Verkaufen", 206, 58, SellC, 20, null, 10, Orange));
	}

	// ───────────── Reparatur und Tanken ─────────────
	public static void Tanken(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Reparatur & Tanken", true);
		var rep = Win(c, 10, 10, 670, 200, Card, null, 10);
		G(rep, 0, 0, 670, 200, g =>
		{
			g.Text(new Vector2(16, 34), "Reparatur", 24, Cyan);
			g.Text(new Vector2(16, 62), "Hülle 82 von 100", 16, Dim);
			g.Text(new Vector2(16, 106), "Kosten 180 Credits", 20, Orange);
			g.Text(new Vector2(16, 134), "Verbraucht 4 Hüllenplatten aus dem Stationslager (92 vorrätig)", 14, Dim);
		});
		At(rep, Bar(82, Green, 400, 16), 16, 74);
		At(rep, B("Alles reparieren", 220, 54, BuyC, 19, null, 10, Green), 16, 142);
		At(rep, B("Teilweise (+10)", 190, 54, null, 17, null, 10), 246, 142);
		At(rep, L("Keine Wartezeit", 13, Dim), 450, 160);

		var fuel = Win(c, 10, 222, 670, 308, Card, null, 10);
		G(fuel, 0, 0, 670, 308, g => { g.Text(new Vector2(16, 34), "Tanken", 24, Cyan); g.Text(new Vector2(16, 62), "Treibstoff je Antriebsart, Preis je Einheit", 14, Dim); });
		float fy = 76;
		foreach (var (n, pct, price, stock, tag) in new[] { ("Treibstoff", 72f, 6.4f, 480f, "chemisch · Antrieb Mk2"), ("Ionengas", 31f, 14.2f, 36f, "Ionenantrieb · wenig Vorrat"), ("Fusionsbrennstoff", 0f, 0f, 0f, "hier nicht erhältlich") })
		{
			bool avail = stock > 0; var cn = n; var cp = pct; var cpr = price; var cs = stock; var ct = tag;
			var row = Win(fuel, 10, fy, 650, 62, new Color(0.04f, 0.07f, 0.13f, 0.9f), avail ? Edge : new Color(Dim, 0.25f), 8);
			G(row, 0, 0, 650, 62, g =>
			{
				g.Text(new Vector2(14, 26), cn, 20, avail ? White : Dim);
				g.Text(new Vector2(14, 48), ct, 13, Dim);
				if (avail)
				{
					g.DrawRect(new Rect2(220, 18, 150, 12), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(220, 18, 150 * cp / 100f, 12), Orange);
					g.Text(new Vector2(220, 50), $"{(int)cp} % · Preis {N1(cpr)}", 14, Dim);
				}
			});
			if (avail) { At(row, B("+10", 70, 44, null, 17, null, 8), 400, 9); At(row, B("Voll tanken", 150, 44, BuyC, 17, null, 8, Green), 480, 9); }
			fy += 70;
		}
		At(fuel, L("Treibstoff ist eine Ware: Der Preis folgt dem Bestand der Station.", 13, Dim), 16, 282);
	}

	// ───────────── Ausrüster ─────────────
	public static void Ausruester(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Ausrüster", false);
		G(c, 0, 0, 1040, 540, g =>
		{
			var ctr = new Vector2(250, 270);
			Ship(g, ctr, 150, new Color(0.17f, 0.23f, 0.36f), new Color(Cyan, 0.7f));
			var slots = new[]
			{
				("Werkzeug", "Laser Mk1", new Vector2(250, 78), false), ("Utility", "– leer –", new Vector2(420, 130), false),
				("Schild", "Schild Mk1", new Vector2(80, 130), false), ("Fracht", "Frachtmodul", new Vector2(70, 290), false),
				("Fracht", "Kanister", new Vector2(430, 290), false), ("Reaktor", "Reaktor Mk1", new Vector2(190, 270), false),
				("Speicher", "Bank Mk1", new Vector2(310, 270), false), ("Antrieb", "Ionenantrieb Mk1", new Vector2(250, 420), true),
			};
			foreach (var (kind, name, p, sel) in slots)
			{
				bool empty = name.StartsWith("–");
				if (!(kind == "Reaktor" || kind == "Speicher")) g.DrawLine(ctr, p, new Color(Cyan, 0.2f), 1.5f);
				g.DrawCircle(p, 30, sel ? new Color(0.15f, 0.4f, 0.6f, 0.95f) : new Color(0.06f, 0.1f, 0.2f, 0.95f));
				g.DrawArc(p, 30, 0, Mathf.Tau, 40, sel ? Cyan : empty ? new Color(Dim, 0.5f) : Edge, sel ? 4 : 2, true);
				g.Text(p + new Vector2(-30, 8), kind.Substring(0, 1), 22, sel ? White : Dim, HorizontalAlignment.Center, 60);
				g.Text(p + new Vector2(-80, 52), kind, 13, Dim, HorizontalAlignment.Center, 160);
				g.Text(p + new Vector2(-80, 68), name, 13, sel ? Cyan : White, HorizontalAlignment.Center, 160);
			}
			g.Text(new Vector2(16, 28), "Slot tippen, um Module zu tauschen", 15, Dim);
			g.Text(new Vector2(16, 518), "Im Lager der Station und an Bord", 13, Dim);
		});
		var v = V(8); At(c, v, 520, 12, 510, 516);
		v.AddChild(L("Antrieb · Größe S", 22, Cyan));
		v.AddChild(L("Eingebaut: Ionenantrieb Mk1 · Treibstoff: Ionengas", 14, Dim));
		void Cand(string name, string[] stats, string price, bool best)
		{
			var card = Pnl(best ? new Color(0.12f, 0.22f, 0.4f, 0.95f) : Card, best ? Cyan : null, 8, 8);
			card.CustomMinimumSize = new Vector2(500, 96);
			v.AddChild(card);
			var h = H(10); card.AddChild(h);
			var col = V(2); col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; h.AddChild(col);
			col.AddChild(L(name, 17, White));
			foreach (var s in stats) col.AddChild(L(s, 14, s.Contains("▲") ? Green : s.Contains("▼") ? Red : Dim));
			var bv = V(4); h.AddChild(bv);
			bv.AddChild(B("Einbauen", 120, 42, BuyC, 16, null, 8, Green));
			bv.AddChild(L(price, 13, Orange, HorizontalAlignment.Center));
		}
		Cand("Chemischer Antrieb Mk2", new[] { "Schub 55  ▲ +15", "Verbrauch 1,2  ▼ höher", "Treibstoff: Treibstoff (billig)" }, "3.400 Cr · Lager 2", true);
		Cand("Ionenantrieb Mk2", new[] { "Schub 32  ▲ +7", "Verbrauch 0,6  ▲ sparsamer", "Treibstoff: Ionengas" }, "5.200 Cr · Lager 1", false);
		v.AddChild(L("Leistung", 15, Dim));
		var pb = H(8); v.AddChild(pb); pb.AddChild(Bar(9, Orange, 260, 14, 12)); pb.AddChild(L("Last 9 von 12", 14, White));
		var sb = H(8); v.AddChild(sb); sb.AddChild(Bar(14, Cyan, 260, 14, 20)); sb.AddChild(L("Speicherbank 14 von 20", 14, White));
		v.AddChild(B("Upgrade auf Mk2 · Chance 60 % (+10 % Mitleid) · 2.100 Cr", 500, 48, new Color(0.35f, 0.25f, 0.08f, 0.95f), 15, null, 8, Orange));
	}

	// ───────────── Hangar (angedockte Schiffe) ─────────────
	public static void Hangar(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Hangar", false);
		G(c, 0, 0, 1040, 40, g => g.Text(new Vector2(16, 30), "Angedockte Schiffe · Liegeplätze 4 von 6 belegt", 18, Cyan));
		float y = 48;
		foreach (var (name, model, job, hull, fuel, cargo, active, foreign) in new[]
		{
			("Falke", "Orion-A · Skiff", "von dir gesteuert", 82f, 72f, 52f, true, false),
			("Mule", "Leichter Frachter", "Befehl: Handel Alpha ↔ Beta", 96f, 88f, 110f, false, false),
			("Pickaxe", "Schürfer", "Automatik: Eisenerz, Gürtel Alpha-3", 71f, 54f, 38f, false, false),
			("Hauler", "Terra · Händler", "fremdes Schiff", 100f, 90f, 0f, false, true),
		})
		{
			var cn = name; var cm = model; var cj = job; var ch = hull; var cf = fuel; var cc = cargo; var ca = active; var cfo = foreign;
			var row = Win(c, 10, y, 1020, 114, ca ? new Color(0.12f, 0.22f, 0.4f, 0.95f) : Card, ca ? Cyan : (cfo ? new Color(Dim, 0.25f) : Edge), 10);
			G(row, 0, 0, 1020, 114, g =>
			{
				Ship(g, new Vector2(64, 56), 34, cfo ? new Color(0.25f, 0.28f, 0.34f) : new Color(0.25f, 0.33f, 0.5f), cfo ? Dim : Cyan);
				g.Text(new Vector2(130, 38), cn, 24, White);
				g.Text(new Vector2(130, 62), cm, 15, Dim);
				g.Text(new Vector2(130, 90), cj, 15, cfo ? Dim : Cyan);
				float bx = 480;
				foreach (var (lab, v, col) in new[] { ("Hülle", ch, Green), ("Sprit", cf, Orange) })
				{
					g.Text(new Vector2(bx, 38), lab, 14, Dim);
					g.DrawRect(new Rect2(bx + 56, 27, 110, 10), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(bx + 56, 27, 110 * v / 100f, 10), col);
					bx += 0; break;
				}
				g.Text(new Vector2(480, 38), "Hülle", 14, Dim); g.DrawRect(new Rect2(536, 27, 110, 10), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(536, 27, 110 * ch / 100f, 10), Green);
				g.Text(new Vector2(480, 64), "Sprit", 14, Dim); g.DrawRect(new Rect2(536, 53, 110, 10), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(536, 53, 110 * cf / 100f, 10), Orange);
				g.Text(new Vector2(480, 90), "Fracht", 14, Dim); g.DrawRect(new Rect2(536, 79, 110, 10), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(536, 79, 110 * Mathf.Min(1f, cc / 120f), 10), Cyan);
			});
			if (ca) At(row, L("AKTIV", 14, Cyan), 940, 14);
			else if (!cfo)
			{
				At(row, B("Steuern", 120, 46, Act, 17, null, 8, Cyan), 700, 14);
				At(row, B("Befehle", 120, 46, null, 17, null, 8), 830, 14);
				At(row, B("Verkaufen", 120, 46, null, 17, null, 8), 700, 64);
				At(row, B("Heimat", 120, 46, null, 17, null, 8), 830, 64);
			}
			y += 124;
		}
	}

	// ───────────── Station-Info ─────────────
	public static void Info(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Station-Info", false);
		Win(c, 10, 10, 336, 520, Card, null, 10);
		Win(c, 354, 10, 336, 520, Card, null, 10);
		Win(c, 698, 10, 332, 520, Card, null, 10);
		G(c, 0, 0, 1040, 540, g =>
		{
			g.Text(new Vector2(26, 42), "Allgemein", 22, Cyan);
			string[,] a = { { "Typ", "Handelsstation" }, { "Größe", "Mittel" }, { "Besitzer", "Terra (Staat)" }, { "Sektor", d.Sector }, { "Kontrolle", "Dominanz" }, { "Ruf bei dir", "freundlich" }, { "Fahndung", "sauber" }, { "Liegeplätze", "6" } };
			for (int i = 0; i < 8; i++) { g.Text(new Vector2(26, 82 + i * 34), a[i, 0], 15, Dim); g.TextR(326, 82 + i * 34, a[i, 1], 16, White); }
			g.Text(new Vector2(26, 380), "Dienste", 18, Cyan);
			float cx = 26, cy = 396;
			foreach (var s in new[] { "Handel", "Reparatur", "Tanken", "Ausrüster", "Werft", "Hangar", "Taxi", "Aufträge" })
			{
				float w = 14 + s.Length * 9;
				if (cx + w > 320) { cx = 26; cy += 36; }
				g.DrawRect(new Rect2(cx, cy, w, 28), new Color(Cyan, 0.18f)); g.Text(new Vector2(cx, cy + 20), s, 14, Cyan, HorizontalAlignment.Center, w);
				cx += w + 8;
			}

			g.Text(new Vector2(370, 42), "Wirtschaft", 22, Cyan);
			g.Text(new Vector2(370, 80), "Rezepte", 15, Dim);
			g.Text(new Vector2(370, 108), "keine Produktion (Knotenmarkt)", 15, White);
			g.Text(new Vector2(370, 150), "Nachfrage", 15, Dim);
			int k = 0;
			foreach (var r in d.Rows.Take(5)) { g.DrawRect(new Rect2(370, 164 + k * 32, 14, 14), r.Color); g.Text(new Vector2(392, 177 + k * 32), r.Name, 16, White); g.TextR(676, 177 + k * 32, $"{N0(r.Amount)} / {N0(r.Target)}", 15, Dim); k++; }
			g.Text(new Vector2(370, 352), "Kasse der Station", 15, Dim); g.TextR(676, 352, $"{N0(d.StationCredits)} Cr", 18, Orange);
			g.Text(new Vector2(370, 384), "Unterhalt je Stunde", 15, Dim); g.TextR(676, 384, "120 Cr", 16, White);
			g.Text(new Vector2(370, 416), "Einnahmen Zoll heute", 15, Dim); g.TextR(676, 416, "1.340 Cr", 16, Green);
			g.Text(new Vector2(370, 448), "Handelsvolumen 24 h", 15, Dim); g.TextR(676, 448, "8.920 Cr", 16, White);

			g.Text(new Vector2(714, 42), "Politik und Sicherheit", 22, Cyan);
			g.Text(new Vector2(714, 82), "Zoll", 15, Dim); g.TextR(1014, 82, "4 %", 16, White);
			g.Text(new Vector2(714, 114), "Sperrstufe", 15, Dim); g.TextR(1014, 114, "0 · offen", 16, Green);
			g.Text(new Vector2(714, 146), "Embargo", 15, Dim); g.TextR(1014, 146, "keines", 16, White);
			g.Text(new Vector2(714, 178), "Wache", 15, Dim); g.TextR(1014, 178, "ja · Patrouille 2", 16, White);
			g.Text(new Vector2(714, 226), "Sektorsicherheit", 15, Dim);
			g.DrawRect(new Rect2(714, 238, 300, 12), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(714, 238, 300 * 0.72f, 12), Green);
			g.Text(new Vector2(714, 290), "Hinweise", 15, Dim);
			g.Text(new Vector2(714, 320), "▮ Gate nach Delta gesperrt (Terra – Teck)", 15, Orange);
			g.Text(new Vector2(714, 350), "▮ Piratenmeldung im Sektor Gamma", 15, Red);
			g.Text(new Vector2(714, 380), "▮ Taxi nach Beta: dort wartet dein Skiff Spatz", 15, Dim);
		});
	}

	// ───────────── Taxi (der Spieler reist allein, das Schiff bleibt im Hangar) ─────────────
	public static void Taxi(Control root, ProtoData d)
	{
		var c = Shell(root, d, "Taxi", false);
		G(c, 0, 0, 640, 44, g => g.Text(new Vector2(16, 32), "Ziel wählen · du reist allein, dein Schiff bleibt im Hangar", 17, Cyan));
		float y = 48;
		int i = 0;
		foreach (var (name, jumps, time, price, note, ok, ship, shipOk) in new[]
		{
			("Handelsstation Beta", 1, "2 min", 120, "Zoll inklusive", true, "Dort wartet: Spatz (dein Skiff)", true),
			("Kolonie Gamma", 2, "5 min", 260, "Piratenmeldung im Sektor", true, "Kein eigenes Schiff · Leihschiff ab 180 Cr", false),
			("Werft Delta", 3, "8 min", 0, "Gate gesperrt (Terra – Teck)", false, "Dort wartet: nichts", false),
			("Raffinerie Alpha-2", 0, "1 min", 40, "gleicher Sektor", true, "Kein eigenes Schiff · Leihschiff ab 60 Cr", false),
		})
		{
			var cn = name; var cj = jumps; var ct = time; var cp = price; var cno = note; var cok = ok; var csh = ship; var cso = shipOk; bool sel = i == 0;
			var row = Win(c, 10, y, 620, sel ? 172 : 86, sel ? new Color(0.12f, 0.22f, 0.4f, 0.95f) : Card, sel ? Cyan : (cok ? Edge : new Color(Red, 0.5f)), 10);
			G(row, 0, 0, 620, sel ? 172 : 86, g =>
			{
				g.Text(new Vector2(16, 32), cn, 21, cok ? White : Dim);
				g.Text(new Vector2(16, 56), $"{cj} {(cj == 1 ? "Sprung" : "Sprünge")} · {ct}", 15, Dim);
				g.Text(new Vector2(16, 74), cno, 13, cok ? Dim : Red);
				if (cok) g.TextR(604, 36, $"{N0(cp)} Cr", 22, Orange);
				else g.TextR(604, 36, "nicht erreichbar", 16, Red);
				if (cok) g.TextR(604, 74, csh, 13, cso ? Green : Orange);
				if (sel)
				{
					g.Text(new Vector2(16, 108), "Falke bleibt auf Liegeplatz 3 (12 Cr je Tag).", 13, Dim);
					g.Text(new Vector2(16, 128), "Fracht und Credits des Schiffs bleiben dort.", 13, Dim);
					g.Text(new Vector2(16, 148), "Am Ziel wählst du das Schiff im Andock-Menü.", 13, Dim);
				}
			});
			if (sel) At(row, B("Taxi buchen · 120 Cr", 250, 46, BuyC, 19, null, 10, Green), 354, 112);
			y += (sel ? 172 : 86) + 8;
			i++;
		}
		var map = Win(c, 660, 10, 370, 520, Card, null, 10);
		G(map, 0, 0, 370, 520, g =>
		{
			g.Text(new Vector2(16, 34), "Route · deine Schiffe", 20, Cyan);
			var pos = new[] { new Vector2(70, 180), new Vector2(190, 120), new Vector2(300, 200), new Vector2(250, 330) };
			var names = new[] { "Alpha", "Beta", "Gamma", "Delta" };
			g.DrawLine(pos[0], pos[1], Green, 4);
			g.DrawLine(pos[1], pos[2], new Color(Dim, 0.5f), 2);
			g.DrawLine(pos[2], pos[3], new Color(Red, 0.7f), 2);
			g.DrawLine(pos[0], pos[3], new Color(Dim, 0.3f), 1.5f);
			g.Text(new Vector2(284, 276), "gesperrt", 13, Red);
			for (int k = 0; k < 4; k++)
			{
				bool here = k == 0, dest = k == 1;
				g.DrawCircle(pos[k], here ? 16 : 13, here ? new Color(Cyan, 0.9f) : dest ? Green : new Color(Dim, 0.6f));
				g.Text(pos[k] + new Vector2(-40, 34), names[k], 15, White, HorizontalAlignment.Center, 80);
			}
			// Eigene Schiffe auf der Karte
			Ship(g, pos[0] + new Vector2(24, -24), 9, new Color(0.25f, 0.33f, 0.5f), Cyan);
			g.Text(pos[0] + new Vector2(36, -26), "Falke", 12, Cyan);
			Ship(g, pos[1] + new Vector2(24, -22), 9, new Color(0.25f, 0.33f, 0.5f), Green);
			g.Text(pos[1] + new Vector2(36, -24), "Spatz", 12, Green);
			g.Text(new Vector2(16, 410), "Von Alpha nach Beta", 16, White);
			g.Text(new Vector2(16, 436), "1 Sprung · 2 min · du reist allein", 15, Dim);
			g.Text(new Vector2(16, 462), "Fahndung: sauber · kein Zoll-Hinweis", 14, Green);
			g.Text(new Vector2(16, 488), "Dort wartet dein Skiff Spatz im Hangar", 13, Dim);
		});
	}
}
