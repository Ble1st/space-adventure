using Godot;
using System.Linq;
using static SpaceAdventure.Prototypes.K;

namespace SpaceAdventure.Prototypes;

/// <summary>Vorschlag 4: schmale Symbolleiste mit Schublade und Routenkarte; Dock als Kachelraster mit Bottom-Sheet.</summary>
public static class V4
{
	public static void Hud(Control root, ProtoData d)
	{
		// Symbolleiste links
		var icons = new[] { ("S", "Schiff", true), ("F", "Flotte", false), ("K", "Karte", false), ("W", "Wirtschaft", false), ("A", "Aufträge", false), ("≡", "Menü", false) };
		float y = 70;
		foreach (var (g, n, on) in icons)
		{ At(root, B(g, 58, 58, on ? new Color(0.2f, 0.38f, 0.62f, 0.95f) : new Color(0.04f, 0.07f, 0.13f, 0.8f), 22, null, 29, on ? Cyan : null), 14, y); y += 68; }
		// Schublade „Schiff“
		var dr = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.82f), Cyan, 12, 12), 84, 70, 330, 330);
		var dv = V(6); dr.AddChild(dv);
		dv.AddChild(L("Schiff · Orion-A „Falke“", 18, Cyan));
		foreach (var (n, v, c, t) in new[] { ("Hülle", d.Hull, Green, "82"), ("Schild", d.Shield, Cyan, "64"), ("Leistung", 75f, Orange, "9/12") })
		{ var r = H(8); dv.AddChild(r); var l = L(n, 15, Dim); l.CustomMinimumSize = new Vector2(70, 0); r.AddChild(l); var b = Bar(v, c, 150, 12); b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; r.AddChild(b); r.AddChild(L(t, 15, White)); }
		dv.AddChild(L("Laderaum", 15, Dim));
		var cr = H(8); dv.AddChild(cr);
		cr.AddChild(L("Fest", 14, Dim)); cr.AddChild(Bar(40, Cyan, 90, 12, 60)); cr.AddChild(L("Kanister", 14, Dim)); cr.AddChild(Bar(12, Green, 70, 12, 40));
		dv.AddChild(L("Treibstoff", 15, Dim));
		dv.AddChild(L("▮▮▮▮▯ Treibstoff 72 %\n▮▮▯▯▯ Ionengas 31 %", 15, Orange));
		dv.AddChild(L($"{N0(d.PlayerCredits)} Credits", 20, Orange));

		// Routenkarte rechts
		var rt = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.78f), new Color(0.3f, 0.5f, 0.8f, 0.5f), 12, 12), 940, 14, 326, 150);
		var rv = V(3); rt.AddChild(rv);
		rv.AddChild(L("Route · Sektor Beta", 18, Cyan));
		rv.AddChild(L("2 Sprünge · 6 min · Sprit −14", 15, White));
		rv.AddChild(L("Gate Alpha → Gamma → Beta", 14, Dim));
		rv.AddChild(L("Fraktion Terra: Kontrolle", 14, Green));
		rv.AddChild(L("Zoll am Gate Gamma: 40 Cr", 14, Orange));

		// Stick und Kontextleiste
		G(root, 30, 470, 200, 200, g => { var c = new Vector2(100, 100); g.DrawCircle(c, 92, new Color(1, 1, 1, 0.07f)); g.DrawArc(c, 92, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.35f), 2); g.DrawCircle(c, 34, new Color(1, 1, 1, 0.28f)); });
		var bar = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.8f), Green, 36, 8), 430, 640, 520, 62);
		var br = H(12); bar.AddChild(br);
		br.AddChild(L("Handelsstation Alpha · 340 m", 18, White));
		br.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		br.AddChild(B("Andocken", 150, 46, new Color(0.15f, 0.5f, 0.3f, 0.95f), 20, null, 23, Green));
		At(root, B("Feuer", 86, 86, null, 17, Orange, 43), 1180, 560);
		At(root, B("Traktor", 86, 86, null, 17, Cyan, 43), 1086, 590);
	}

	public static void Dock(Control root, ProtoData d)
	{
		ProtoVariants.Dim(root, 0.82f);
		G(root, 20, 8, 1240, 56, g =>
		{
			g.Text(new Vector2(0, 30), d.Station, 26, Cyan);
			g.Text(new Vector2(0, 52), $"{d.Type} · {d.Faction}", 15, Dim);
			g.TextR(1240, 30, $"{N0(d.PlayerCredits)} Credits", 24, Orange);
			g.TextR(1240, 52, $"Laderaum {N0(d.CargoUsed)}/{N0(d.CargoMax)} · Hülle {N0(d.Hull)} · Sprit {N0(d.Fuel * 100)} %", 15, Dim);
		});
		float x = 20;
		foreach (var (t, on) in new[] { ("Handel", true), ("Reparatur", false), ("Tanken", false), ("Ausrüster", false), ("Werft", false), ("Lager", false) })
		{ At(root, B(t, 150, 44, on ? new Color(0.2f, 0.38f, 0.62f, 0.95f) : null, 18, null, 22), x, 68); x += 158; }

		// Kachelraster 4 x 2
		int i = 0;
		foreach (var r in d.Rows.Take(8))
		{
			int col = i % 4, row = i / 4;
			float tx = 20 + col * 310, ty = 124 + row * 164;
			bool sel = i == 0;
			var cap = r; var (tl, tc) = Trend(r);
			var tile = Win(root, tx, ty, 298, 152, sel ? new Color(0.12f, 0.22f, 0.4f, 0.97f) : new Color(0.04f, 0.07f, 0.13f, 0.9f), sel ? Cyan : Edge, 12);
			G(tile, 0, 0, 298, 152, g =>
			{
				g.DrawRect(new Rect2(0, 0, 298, 8), cap.Color);
				g.Text(new Vector2(14, 40), cap.Name, 22, White);
				g.Text(new Vector2(14, 82), N1(cap.Price), 34, tc);
				g.Text(new Vector2(130, 80), tl, 15, tc);
				float fill = Mathf.Clamp(cap.Amount / Mathf.Max(1, cap.Target), 0, 1.4f) / 1.4f;
				g.DrawRect(new Rect2(14, 100, 270, 10), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(14, 100, 270 * fill, 10), cap.Color);
				g.Text(new Vector2(14, 134), $"Bestand {N0(cap.Amount)}", 15, Dim);
				if (cap.Held > 0) { g.DrawRect(new Rect2(210, 118, 76, 26), new Color(Cyan, 0.25f)); g.Text(new Vector2(210, 137), $"Bord {N0(cap.Held)}", 15, Cyan, HorizontalAlignment.Center, 76); }
			});
			i++;
		}
		// Bottom-Sheet
		var sr = d.Rows[0]; var (stl, stc) = Trend(sr);
		var sheet = Win(root, 20, 456, 1240, 174, new Color(0.06f, 0.1f, 0.18f, 0.98f), Cyan, 14);
		G(sheet, 0, 0, 1240, 174, g =>
		{
			g.DrawRect(new Rect2(14, 20, 20, 20), sr.Color);
			g.Text(new Vector2(44, 38), sr.Name, 26, White);
			g.Text(new Vector2(44, 68), $"Preis {N1(sr.Price)} ({stl}) · Bestand {N0(sr.Amount)} von {N0(sr.Target)}", 16, stc);
			g.Text(new Vector2(14, 108), "Beste bekannte Verkaufsstelle: Beta 13,6 (+3,9 je Einheit)", 15, Dim);
			g.Text(new Vector2(14, 140), "Letzter Handel: 20 × gekauft bei 9,8", 14, Dim);
		});
		var qv = H(8); At(sheet, qv, 520, 20, 700, 60);
		qv.AddChild(B("−", 64, 60, null, 28, null, 10)); qv.AddChild(B("10", 110, 60, new Color(0, 0, 0, 0.5f), 28, null, 10, Dim)); qv.AddChild(B("+", 64, 60, null, 28, null, 10));
		foreach (var q in new[] { "1", "10", "50", "Max" }) qv.AddChild(B(q, 72, 60, null, 19, null, 10));
		var av = H(10); At(sheet, av, 520, 96, 700, 62);
		av.AddChild(L($"Summe {N0(sr.Price * 10)}", 20, Orange));
		av.AddChild(B("Kaufen", 190, 60, new Color(0.15f, 0.45f, 0.28f, 0.95f), 22, null, 10, Green));
		av.AddChild(B("Verkaufen", 190, 60, new Color(0.5f, 0.28f, 0.12f, 0.95f), 22, null, 10, Orange));
		At(root, B("← Zurück", 160, 58, null, 20, null, 10), 20, 646);
		At(root, B("Alles verkaufen", 240, 58, null, 20, null, 10), 810, 646);
		At(root, B("Abdocken", 200, 58, new Color(0.2f, 0.38f, 0.62f, 0.95f), 22, null, 10), 1060, 646);
	}
}
