using Godot;
using System.Linq;
using static SpaceAdventure.Prototypes.K;

namespace SpaceAdventure.Prototypes;

/// <summary>Vorschlag 2: Kontexttaste und dreispaltiges Vollbild-Dock mit aufklappbarem Mengenwähler.</summary>
public static class V2
{
	public static void Hud(Control root, ProtoData d)
	{
		// Status oben links
		var st = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.65f), new Color(0.3f, 0.5f, 0.8f, 0.4f), 10, 10), 14, 12, 300, 112);
		var sv = V(5); st.AddChild(sv);
		foreach (var (n, v, c) in new[] { ("Hülle", d.Hull, Green), ("Schild", d.Shield, Cyan), ("Sprit", d.Fuel * 100, Orange) })
		{
			var r = H(8); sv.AddChild(r);
			var l = L(n, 16, Dim); l.CustomMinimumSize = new Vector2(60, 0); r.AddChild(l);
			var b = Bar(v, c, 170, 12); b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; r.AddChild(b);
			r.AddChild(L(((int)v).ToString(), 16, White));
		}
		At(root, L($"{N0(d.PlayerCredits)} Credits", 20, Orange), 20, 128);

		// Ziel oben Mitte
		var tg = At(root, Pnl(new Color(0.03f, 0.05f, 0.1f, 0.7f), Orange, 20, 6), 450, 12, 380, 44);
		var tr = H(10); tg.AddChild(tr);
		tr.AddChild(L("Handelsstation Alpha", 20, Orange));
		tr.AddChild(L("340 m · Terra · freundlich", 16, Dim));

		// Karte und Menü oben rechts
		At(root, B("Karte", 110, 52, null, 18, null, 26), 1040, 12);
		At(root, B("Menü", 100, 52, null, 18, null, 26), 1164, 12);

		// Stick
		G(root, 30, 450, 220, 220, g => { var c = new Vector2(110, 110); g.DrawCircle(c, 100, new Color(1, 1, 1, 0.07f)); g.DrawArc(c, 100, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.35f), 2); g.DrawCircle(c, 38, new Color(1, 1, 1, 0.28f)); });

		// Kontexttaste und kleine Aktionen
		var ctx = B("Andocken", 150, 150, new Color(0.15f, 0.5f, 0.3f, 0.95f), 24, null, 75, Green);
		At(root, ctx, 1090, 540);
		At(root, B("Feuer", 84, 84, null, 17, null, 42), 984, 560);
		At(root, B("Traktor", 84, 84, null, 17, null, 42), 996, 462);
		At(root, B("Auto", 84, 84, null, 17, null, 42), 1090, 410);
		// Hinweis unten Mitte
		var toast = At(root, Pnl(new Color(0.03f, 0.1f, 0.06f, 0.8f), Green, 18, 6), 480, 650, 340, 44);
		toast.AddChild(L("Verkauft für 640 Credits", 20, Green, HorizontalAlignment.Center));
	}

	public static void Dock(Control root, ProtoData d)
	{
		ProtoVariants.Dim(root, 0.78f);
		// Kopf
		G(root, 20, 10, 1240, 64, g =>
		{
			g.Text(new Vector2(0, 32), d.Station, 28, Cyan);
			g.Text(new Vector2(0, 56), $"{d.Type} · {d.Faction} · Ruf freundlich", 16, Dim);
			g.TextR(1240, 32, $"{N0(d.PlayerCredits)} Credits", 26, Orange);
			g.TextR(1240, 56, $"Kasse Station {N0(d.StationCredits)}", 16, Dim);
		});
		// Linke Leiste (Tabs aus den Diensten)
		var rail = At(root, Pnl(PanelBg, null, 10, 8), 14, 84, 190, 540);
		var rv = V(8); rail.AddChild(rv);
		foreach (var (t, on, en) in new[] { ("Handel", true, true), ("Reparatur", false, true), ("Tanken", false, true), ("Ausrüster", false, false), ("Werft", false, false), ("Aufträge", false, false) })
		{
			var b = B(t, 174, 58, on ? new Color(0.2f, 0.38f, 0.62f, 0.95f) : null, 19, en ? White : Dim, 8);
			if (!en) b.Modulate = new Color(1, 1, 1, 0.45f);
			rv.AddChild(b);
		}
		rv.AddChild(L("grau = hier nicht\nverfügbar", 13, Dim));
		// Mitte: Liste
		var mid = At(root, Pnl(PanelBg, null, 10, 8), 214, 84, 700, 540);
		var mv = V(4); mid.AddChild(mv);
		var tabs = H(6); mv.AddChild(tabs);
		foreach (var (t, on) in new[] { ("Kaufen", true), ("Verkaufen", false), ("Alle", false) }) tabs.AddChild(B(t, 130, 40, on ? new Color(0.2f, 0.38f, 0.62f, 0.95f) : null, 17, null, 8));
		tabs.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		tabs.AddChild(L("sortiert nach Gewinn", 15, Dim));
		int i = 0;
		foreach (var r in d.Rows.Take(6))
		{
			var (tl, tc) = Trend(r);
			bool open = i == 0;
			var card = Pnl(open ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : new Color(0.02f, 0.04f, 0.09f, 0.85f), open ? Cyan : null, 8, 4);
			card.CustomMinimumSize = new Vector2(680, open ? 128 : 52);
			mv.AddChild(card);
			var cr = r; var o = open; var tlc = tl; var tcc = tc;
			G(card, 0, 0, 670, open ? 120 : 44, g =>
			{
				g.DrawRect(new Rect2(6, 12, 18, 18), cr.Color);
				g.Text(new Vector2(34, 29), cr.Name, 20, White);
				g.Text(new Vector2(230, 29), N1(cr.Price), 20, tcc);
				g.Text(new Vector2(306, 29), tlc, 15, tcc);
				float fill = Mathf.Clamp(cr.Amount / Mathf.Max(1, cr.Target), 0, 1.4f) / 1.4f;
				g.DrawRect(new Rect2(392, 16, 100, 10), new Color(0, 0, 0, 0.5f)); g.DrawRect(new Rect2(392, 16, 100 * fill, 10), cr.Color);
				g.Text(new Vector2(500, 29), N0(cr.Amount), 17, Dim);
				g.Text(new Vector2(575, 29), cr.Held > 0 ? $"Bord {N0(cr.Held)}" : "", 17, Cyan);
				if (o)
				{
					g.Text(new Vector2(34, 66), "Menge", 16, Dim);
					g.DrawRect(new Rect2(100, 48, 56, 44), new Color(0.1f, 0.18f, 0.32f)); g.Text(new Vector2(128, 78), "−", 26, White, HorizontalAlignment.Center, 0);
					g.DrawRect(new Rect2(160, 48, 90, 44), new Color(0, 0, 0, 0.5f)); g.Text(new Vector2(205, 78), "10", 26, White, HorizontalAlignment.Center, 0);
					g.DrawRect(new Rect2(254, 48, 56, 44), new Color(0.1f, 0.18f, 0.32f)); g.Text(new Vector2(282, 78), "+", 26, White, HorizontalAlignment.Center, 0);
					g.Text(new Vector2(330, 78), "1   10   50   Max", 18, Dim);
					g.Text(new Vector2(34, 108), $"Ø {N1(cr.Price * 1.03f)} · Summe {N0(cr.Price * 10.3f)} · beste bekannte Verkaufsstelle: Beta {N1(cr.Price * 1.4f)} (+{N1(cr.Price * 0.37f)})", 15, Dim);
					g.DrawRect(new Rect2(500, 48, 160, 44), new Color(0.15f, 0.45f, 0.28f)); g.Text(new Vector2(580, 78), "Kaufen", 22, White, HorizontalAlignment.Center, 0);
				}
			});
			i++;
		}
		// Rechts: Dein Schiff
		var ship = At(root, Pnl(PanelBg, null, 10, 12), 924, 84, 342, 540);
		var sv = V(8); ship.AddChild(sv);
		sv.AddChild(L("Dein Schiff", 20, Cyan));
		var sk = new Gfx { CustomMinimumSize = new Vector2(310, 130), MouseFilter = Control.MouseFilterEnum.Ignore };
		sk.Paint = g => Ship(g, new Vector2(155, 68), 52, new Color(0.25f, 0.33f, 0.5f), Cyan);
		sv.AddChild(sk);
		foreach (var (n, v, c, t) in new[] { ("Hülle", d.Hull, Green, $"{N0(d.Hull)}"), ("Schild", d.Shield, Cyan, $"{N0(d.Shield)}"), ("Sprit", d.Fuel * 100, Orange, $"{N0(d.Fuel * 100)} %") })
		{
			var r = H(8); sv.AddChild(r);
			var l = L(n, 16, Dim); l.CustomMinimumSize = new Vector2(64, 0); r.AddChild(l);
			var b = Bar(v, c, 170, 12); b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; r.AddChild(b);
			r.AddChild(L(t, 16, White));
		}
		sv.AddChild(L("Laderaum", 16, Dim));
		sv.AddChild(Bar(d.CargoUsed, Cyan, 310, 16, d.CargoMax));
		sv.AddChild(L($"Fest 40/60   Kanister 12/40", 15, Dim));
		sv.AddChild(L("An Bord", 16, Dim));
		foreach (var r in d.Rows.Where(x => x.Held > 0).Take(4)) sv.AddChild(L($"■ {r.Name}  {N0(r.Held)}", 16, r.Color.Lerp(White, 0.5f)));
		// Fuß
		At(root, B("← Zurück", 160, 58, null, 20, null, 10), 14, 648);
		At(root, B("Alles verkaufen", 240, 58, null, 20, null, 10), 810, 648);
		At(root, B("Abdocken", 200, 58, new Color(0.2f, 0.38f, 0.62f, 0.95f), 22, null, 10), 1066, 648);
	}
}
