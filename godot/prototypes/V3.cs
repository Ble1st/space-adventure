using Godot;
using static SpaceAdventure.Prototypes.K;

namespace SpaceAdventure.Prototypes;

/// <summary>Vorschlag 3: Cockpit mit Ringanzeige, Radar und Radialmenü; Dock als Ausrüster mit Schiffsskizze und Slots.</summary>
public static class V3
{
	static void Arc(Gfx g, Vector2 c, float r, float pct, Color col, float w)
	{
		float start = Mathf.DegToRad(135), sweep = Mathf.DegToRad(270);
		g.DrawArc(c, r, start, start + sweep, 64, new Color(1, 1, 1, 0.12f), w, true);
		g.DrawArc(c, r, start, start + sweep * Mathf.Clamp(pct, 0, 1), 64, col, w, true);
	}

	public static void Hud(Control root, ProtoData d)
	{
		// Sektorkopf
		var top = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.6f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 18, 6), 14, 12, 440, 40);
		var tr = H(12); top.AddChild(tr);
		tr.AddChild(L(d.Sector, 19, Cyan)); tr.AddChild(L("Terra", 16, Dim)); tr.AddChild(L("Sicherheit ▮▮▮▯", 16, Green));
		// Warnhinweis
		var warn = At(root, Pnl(new Color(0.2f, 0.05f, 0.05f, 0.8f), Red, 18, 6), 470, 12, 330, 40);
		var wr = H(8); warn.AddChild(wr); wr.AddChild(L("!", 20, Red)); wr.AddChild(L("Fremdes Schiff nähert sich", 17, White));
		// Radar oben rechts
		G(root, 1084, 12, 186, 186, g =>
		{
			var c = new Vector2(93, 93);
			g.DrawCircle(c, 90, new Color(0, 0.06f, 0.1f, 0.75f));
			foreach (var rr in new[] { 30f, 60f, 90f }) g.DrawArc(c, rr, 0, Mathf.Tau, 48, new Color(Cyan, 0.35f), 1.5f);
			g.DrawLine(c - new Vector2(90, 0), c + new Vector2(90, 0), new Color(Cyan, 0.2f), 1);
			g.DrawLine(c - new Vector2(0, 90), c + new Vector2(0, 90), new Color(Cyan, 0.2f), 1);
			g.DrawCircle(c, 4, Cyan);
			g.DrawCircle(c + new Vector2(40, -30), 4, Orange); g.DrawCircle(c + new Vector2(-48, 18), 4, Orange); g.DrawCircle(c + new Vector2(14, 56), 5, Green); g.DrawCircle(c + new Vector2(62, 38), 4, Red);
			g.Text(new Vector2(0, 186), "Radar 2 km", 14, Dim, HorizontalAlignment.Center, 186);
		});
		// Stick
		G(root, 24, 470, 200, 200, g => { var c = new Vector2(100, 100); g.DrawCircle(c, 92, new Color(1, 1, 1, 0.07f)); g.DrawArc(c, 92, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.35f), 2); g.DrawCircle(c, 34, new Color(1, 1, 1, 0.28f)); });
		// Ringanzeige unten Mitte
		G(root, 500, 480, 280, 240, g =>
		{
			var c = new Vector2(140, 120);
			Arc(g, c, 100, d.Hull / 100, Green, 12);
			Arc(g, c, 82, d.Shield / 100, Cyan, 9);
			Arc(g, c, 66, d.Fuel, Orange, 7);
			Ship(g, c + new Vector2(0, 2), 34, new Color(0.25f, 0.33f, 0.5f), Cyan);
			g.Text(new Vector2(0, 232), "Hülle 82   Schild 64   Sprit 72 %", 15, Dim, HorizontalAlignment.Center, 280);
		});
		// Radialmenü rechts unten
		var center = new Vector2(1150, 580);
		G(root, 980, 410, 300, 300, g => g.DrawArc(center - new Vector2(980, 410), 156, Mathf.DegToRad(170), Mathf.DegToRad(285), 32, new Color(Cyan, 0.25f), 2));
		var items = new[] { ("Feuer", 172f, Orange), ("Traktor", 207f, Cyan), ("Auto", 242f, Green), ("Auto-\npilot", 277f, White) };
		foreach (var (name, ang, col) in items)
		{
			var pos = center + Vector2.FromAngle(Mathf.DegToRad(ang)) * 156f;
			var b = B(name, 74, 74, ang == 172f ? new Color(0.35f, 0.25f, 0.08f, 0.95f) : null, 16, col, 37, ang == 172f ? Orange : null);
			At(root, b, pos.X - 37, pos.Y - 37);
		}
		At(root, B("Andocken", 112, 112, new Color(0.15f, 0.5f, 0.3f, 0.95f), 20, null, 56, Green), center.X - 56, center.Y - 56);
	}

	public static void Dock(Control root, ProtoData d)
	{
		ProtoVariants.Dim(root, 0.82f);
		G(root, 20, 8, 1240, 56, g =>
		{
			g.Text(new Vector2(0, 30), $"{d.Station}", 26, Cyan);
			g.Text(new Vector2(0, 52), $"{d.Type} · {d.Faction}", 15, Dim);
			g.TextR(1240, 30, $"{N0(d.PlayerCredits)} Credits", 24, Orange);
		});
		float x = 20;
		foreach (var (t, on) in new[] { ("Handel", false), ("Reparatur", false), ("Tanken", false), ("Ausrüster", true), ("Werft", false) })
		{ At(root, B(t, 170, 46, on ? new Color(0.2f, 0.38f, 0.62f, 0.95f) : null, 19, null, 23), x, 68); x += 178; }

		// Schiffsskizze mit Slots
		var left = Win(root, 20, 124, 740, 480, new Color(0.03f, 0.06f, 0.12f, 0.9f), Edge, 12);
		G(left, 0, 0, 740, 480, g =>
		{
			var c = new Vector2(370, 250);
			Ship(g, c, 150, new Color(0.17f, 0.23f, 0.36f), new Color(Cyan, 0.7f));
			var slots = new[]
			{
				("Werkzeug", "Laser Mk1", new Vector2(370, 70), false), ("Utility", "– leer –", new Vector2(560, 120), false),
				("Fracht", "Frachtmodul", new Vector2(165, 270), false), ("Fracht", "Kanister", new Vector2(575, 270), false),
				("Reaktor", "Reaktor Mk1", new Vector2(300, 250), false), ("Speicher", "Bank Mk1", new Vector2(440, 250), false),
				("Antrieb", "Ionenantrieb Mk1", new Vector2(370, 392), true), ("Schild", "Schild Mk1", new Vector2(180, 130), false),
			};
			foreach (var (kind, name, p, sel) in slots)
			{
				bool empty = name.StartsWith("–");
				if (!(kind == "Reaktor" || kind == "Speicher")) g.DrawLine(c, p, new Color(Cyan, 0.2f), 1.5f);
				g.DrawCircle(p, 30, sel ? new Color(0.15f, 0.4f, 0.6f, 0.95f) : new Color(0.06f, 0.1f, 0.2f, 0.95f));
				g.DrawArc(p, 30, 0, Mathf.Tau, 40, sel ? Cyan : empty ? new Color(Dim, 0.5f) : Edge, sel ? 4 : 2, true);
				g.Text(p + new Vector2(-30, 6), kind.Substring(0, 1), 22, sel ? White : Dim, HorizontalAlignment.Center, 60);
				g.Text(p + new Vector2(-60, 52), kind, 14, Dim, HorizontalAlignment.Center, 120);
				g.Text(p + new Vector2(-80, 69), name, 14, sel ? Cyan : White, HorizontalAlignment.Center, 160);
			}
			g.Text(new Vector2(16, 28), "Slots tippen, um Module zu tauschen", 15, Dim);
		});

		// Detail rechts
		var right = Win(root, 774, 124, 486, 480, new Color(0.03f, 0.06f, 0.12f, 0.92f), Edge, 12);
		var v = V(8); At(right, v, 16, 12, 454, 456);
		v.AddChild(L("Antrieb · Größe S", 22, Cyan));
		v.AddChild(L("Eingebaut: Ionenantrieb Mk1  (Treibstoff: Ionengas)", 15, Dim));
		void Cand(string name, string[] stats, string price, bool best)
		{
			var card = Pnl(best ? new Color(0.12f, 0.22f, 0.4f, 0.95f) : new Color(0.02f, 0.04f, 0.09f, 0.9f), best ? Cyan : null, 8, 8);
			card.CustomMinimumSize = new Vector2(450, 100);
			v.AddChild(card);
			var h = H(10); card.AddChild(h);
			var col = V(2); col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; h.AddChild(col);
			col.AddChild(L(name, 18, White));
			foreach (var s in stats) col.AddChild(L(s, 15, s.Contains("▲") ? Green : s.Contains("▼") ? Red : Dim));
			var bv = V(4); h.AddChild(bv);
			bv.AddChild(B("Einbauen", 130, 44, new Color(0.15f, 0.45f, 0.28f, 0.95f), 17, null, 8, Green));
			bv.AddChild(L(price, 15, Orange, HorizontalAlignment.Center));
		}
		Cand("Chemischer Antrieb Mk2", new[] { "Schub 55  ▲ +15", "Verbrauch 1,2  ▼ höher", "Treibstoff: Treibstoff (billig)" }, "3.400 Cr · im Lager 2", true);
		Cand("Ionenantrieb Mk2", new[] { "Schub 32  ▲ +7", "Verbrauch 0,6  ▲ sparsamer", "Treibstoff: Ionengas" }, "5.200 Cr · im Lager 1", false);
		v.AddChild(L("Leistung", 16, Dim));
		var pb = H(8); v.AddChild(pb);
		pb.AddChild(Bar(9, Orange, 250, 14, 12)); pb.AddChild(L("Last 9 von 12", 15, White));
		var sb = H(8); v.AddChild(sb);
		sb.AddChild(Bar(14, Cyan, 250, 14, 20)); sb.AddChild(L("Speicherbank 14 von 20", 15, White));
		var up = B("Upgrade auf Mk2 · Chance 60 % (+10 % Mitleid) · 2.100 Cr", 450, 52, new Color(0.35f, 0.25f, 0.08f, 0.95f), 16, null, 8, Orange);
		v.AddChild(up);

		At(root, B("← Zurück", 160, 58, null, 20, null, 10), 20, 626);
		At(root, B("Abdocken", 200, 58, new Color(0.2f, 0.38f, 0.62f, 0.95f), 22, null, 10), 1060, 626);
	}
}
