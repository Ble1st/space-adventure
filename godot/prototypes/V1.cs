using Godot;
using System.Linq;
using static SpaceAdventure.Prototypes.K;

namespace SpaceAdventure.Prototypes;

/// <summary>Vorschlag 1: nah an der Referenz (dünne Kopfleiste, Zielkarte oben rechts, Funkfeed links, Befehlsliste rechts, Trefferleiste unten).</summary>
public static class V1
{
	public static void Hud(Control root, ProtoData d)
	{
		// Kopfleiste
		var bar = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.78f), new Color(0.3f, 0.5f, 0.8f, 0.5f), 0, 4), 0, 0, 1280, 36);
		var row = H(14); bar.AddChild(row);
		row.AddChild(L($"  Orion-A „Falke“ · {d.Sector}", 18, Cyan));
		var sp = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; row.AddChild(sp);
		row.AddChild(L($"{N0(d.PlayerCredits)} Cr", 18, Orange));
		foreach (var t in new[] { "K", "W", "i", "II", "≡" }) row.AddChild(B(t, 40, 28, null, 15, null, 6));

		// Zielkarte oben rechts
		var tgt = At(root, Pnl(), 940, 46, 330, 92);
		var tv = V(2); tgt.AddChild(tv);
		tv.AddChild(L("Asteroid Ae-14 (Eisenerz)", 18, Orange));
		tv.AddChild(L("Erz 340   Entfernung 141 m", 16, Dim));
		tv.AddChild(L("Ertrag/Schuss ▮▮▮▯▯  Chance 62 %", 14, Dim));
		G(root, 1196, 56, 64, 64, g => { var c = new Vector2(32, 32); g.DrawCircle(c, 30, new Color(0, 0.08f, 0.04f, 0.8f)); g.DrawArc(c, 30, 0, Mathf.Tau, 48, Green, 2); g.DrawArc(c, 15, 0, Mathf.Tau, 32, new Color(Green, 0.4f), 1); g.DrawCircle(c, 3, Cyan); g.DrawCircle(c + new Vector2(10, -12), 3, Orange); g.DrawCircle(c + new Vector2(-14, 8), 3, Orange); });

		// Zielklammern am Asteroiden
		G(root, 300, 12, 134, 134, g => Brackets(g, new Rect2(4, 4, 126, 126), 18, Orange, 2));

		// Funkfeed links
		var feed = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.6f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 8, 8), 16, 60, 380, 76);
		var fr = H(10); feed.AddChild(fr);
		var face = Pnl(new Color(0.12f, 0.2f, 0.34f), Edge, 6, 0); face.CustomMinimumSize = new Vector2(52, 52); fr.AddChild(face);
		var fv = V(0); fr.AddChild(fv);
		fv.AddChild(L("Hafenkontrolle (Terra)", 16, Cyan));
		fv.AddChild(L($"Andockfreigabe erteilt.\nWillkommen in {d.Sector}.", 15, White));

		// Stick und Menü unten links
		G(root, 40, 440, 220, 220, g => { var c = new Vector2(110, 110); g.DrawCircle(c, 100, new Color(1, 1, 1, 0.07f)); g.DrawArc(c, 100, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.35f), 2); g.DrawCircle(c + new Vector2(18, -12), 36, new Color(1, 1, 1, 0.28f)); });
		At(root, B("≡", 56, 56, null, 26, null, 28), 16, 648);

		// Mitte unten: Rundtaste und Hüllenleiste
		At(root, B("◎", 52, 52, null, 24, null, 26), 505, 650);
		At(root, Bar(d.Hull, Green, 260, 22), 570, 665);
		At(root, L(((int)d.Hull).ToString(), 22, White), 840, 661);

		// Befehlsliste rechts
		var slots = new[] { ("Feuer", Orange, true), ("Traktor", Cyan, false), ("Auto-Mining", Green, false), ("Andocken", White, false), ("Autopilot", White, false) };
		float y = 300;
		foreach (var (name, col, on) in slots)
		{
			var b = B("", 190, 56, on ? new Color(0.25f, 0.4f, 0.6f, 0.95f) : null, 18, null, 8);
			At(root, b, 1074, y);
			var l = L(name, 18, col); l.Position = new Vector2(14, 12); b.AddChild(l);
			y += 62;
		}
	}

	public static void Dock(Control root, ProtoData d)
	{
		ProtoVariants.Dim(root, 0.55f);
		var win = Win(root, 70, 50, 1140, 620, PanelSolid, Edge);
		G(win, 20, 14, 1100, 60, g =>
		{
			g.Text(new Vector2(0, 28), d.Station, 28, Cyan);
			g.Text(new Vector2(0, 52), $"{d.Type} · Fraktion {d.Faction} · Kasse der Station {N0(d.StationCredits)}", 16, Dim);
			g.TextR(1100, 28, $"Deine Credits  {N0(d.PlayerCredits)}", 22, Orange);
		});
		float x = 20;
		foreach (var (t, on) in new[] { ("Handel", true), ("Reparatur", false), ("Tanken", false), ("Werft", false) })
		{ At(win, B(t, 150, 46, on ? new Color(0.2f, 0.38f, 0.62f, 0.95f) : null, 20, null, 8), x, 86); x += 158; }
		var table = Win(win, 20, 142, 760, 400, new Color(0.02f, 0.04f, 0.09f, 0.9f), null, 8);
		var tv = V(2); At(table, tv, 8, 6, 744, 388);
		var head = G(tv, 0, 0, 744, 26, g => { foreach (var (s, px) in new[] { ("Ware", 32f), ("Preis", 270f), ("Bestand", 380f), ("Soll", 490f), ("an Bord", 600f) }) g.Text(new Vector2(px, 20), s, 16, Dim); });
		head.CustomMinimumSize = new Vector2(744, 26);
		int i = 0;
		foreach (var r in d.Rows)
		{
			bool sel = i == 0;
			var line = Pnl(sel ? new Color(0.18f, 0.3f, 0.5f, 0.9f) : new Color(0f, 0f, 0f, 0f), sel ? Cyan : null, 6, 0);
			line.CustomMinimumSize = new Vector2(744, 40);
			tv.AddChild(line);
			var cap = r;
			G(line, 0, 0, 744, 40, g =>
			{
				g.DrawRect(new Rect2(8, 13, 14, 14), cap.Color);
				g.Text(new Vector2(32, 27), cap.Name, 19, White);
				g.Text(new Vector2(270, 27), N1(cap.Price), 19, White);
				g.Text(new Vector2(380, 27), N0(cap.Amount), 19, White);
				g.Text(new Vector2(490, 27), N0(cap.Target), 19, Dim);
				g.Text(new Vector2(600, 27), cap.Held > 0 ? N0(cap.Held) : "–", 19, cap.Held > 0 ? Cyan : Dim);
			});
			i++;
		}
		var sr = d.Rows[0];
		var (tl, tc) = Trend(sr);
		var det = Win(win, 796, 142, 324, 400, new Color(0.02f, 0.04f, 0.09f, 0.9f), null, 8);
		var dv = V(10); At(det, dv, 16, 14, 292, 372);
		dv.AddChild(L(sr.Name, 26, White));
		dv.AddChild(L($"Preis {N1(sr.Price)}   ({tl})", 19, tc));
		dv.AddChild(L($"Bestand {N0(sr.Amount)} von Soll {N0(sr.Target)}", 16, Dim));
		dv.AddChild(Bar(System.Math.Min(100, sr.Amount / System.Math.Max(1, sr.Target) * 100), sr.Color, 290, 10));
		dv.AddChild(L("Menge", 16, Dim));
		var qr = H(6); dv.AddChild(qr);
		foreach (var q in new[] { "1", "10", "50", "Max" }) qr.AddChild(B(q, 66, 46, q == "10" ? new Color(0.2f, 0.38f, 0.62f, 0.95f) : null, 18, null, 8));
		dv.AddChild(L($"Summe 10 × {N1(sr.Price)} = {N0(sr.Price * 10)}", 18, Orange));
		var ar = H(8); dv.AddChild(ar);
		ar.AddChild(B("Kaufen", 140, 60, new Color(0.15f, 0.45f, 0.28f, 0.95f), 22, null, 10, Green));
		ar.AddChild(B("Verkaufen", 140, 60, new Color(0.5f, 0.28f, 0.12f, 0.95f), 22, null, 10, Orange));
		dv.AddChild(L($"Laderaum {N0(d.CargoUsed)}/{N0(d.CargoMax)}   Hülle {N0(d.Hull)}   Sprit {N0(d.Fuel * 100)} %", 15, Dim));
		At(win, B("Alles verkaufen", 240, 56, null, 20, null, 10), 20, 552);
		At(win, B("Abdocken", 200, 56, new Color(0.2f, 0.38f, 0.62f, 0.95f), 22, null, 10), 920, 552);
	}
}
