using Godot;
using System.Linq;
using static SpaceAdventure.Prototypes.K;

namespace SpaceAdventure.Prototypes;

/// <summary>Vorschlag 5: Terminal-Stil in Grün auf Schwarz, dicht und tastaturnah.</summary>
public static class V5
{
	static readonly Color Gn = new(0.35f, 1f, 0.6f);
	static readonly Color GnDim = new(0.2f, 0.65f, 0.38f);
	static readonly Color Am = new(1f, 0.78f, 0.25f);

	static void Scan(Control root, float alpha)
	{
		var f = MonoFont();
		G(root, 0, 0, 1280, 720, g => { for (int y = 0; y < 720; y += 4) g.DrawLine(new Vector2(0, y), new Vector2(1280, y), new Color(0, 0.04f, 0.02f, alpha), 1.5f); });
	}

	static string Seg(float pct, int n = 10) { int f = Mathf.RoundToInt(Mathf.Clamp(pct, 0, 100) / 100f * n); return "[" + new string('█', f) + new string('░', n - f) + "]"; }

	public static void Hud(Control root, ProtoData d)
	{
		var mono = MonoFont();
		G(root, 0, 0, 1280, 720, g =>
		{
			g.Custom = mono;
			Brackets(g, new Rect2(8, 8, 1264, 704), 40, new Color(Gn, 0.8f), 2);
			g.Text(new Vector2(28, 44), $"HULL {Seg(d.Hull)} {(int)d.Hull:000}", 18, Gn);
			g.Text(new Vector2(28, 68), $"SHLD {Seg(d.Shield)} {(int)d.Shield:000}", 18, Gn);
			g.Text(new Vector2(28, 92), $"FUEL {Seg(d.Fuel * 100)} {(int)(d.Fuel * 100):000}%", 18, Am);
			g.Text(new Vector2(28, 116), $"PWR  {Seg(75)} 009/012", 18, GnDim);
			g.TextR(1252, 44, $"{d.Sector.ToUpper()} · TERRA", 18, Gn);
			g.TextR(1252, 68, $"CRED {N0(d.PlayerCredits)}", 18, Am);
			g.TextR(1252, 92, "TGT  ASTEROID AE-14  141M", 18, Am);
			g.TextR(1252, 116, "ORE  IRON 340  CHANCE 62%", 18, GnDim);
			Brackets(g, new Rect2(300, 12, 134, 134), 18, Am, 2);
			g.Text(new Vector2(28, 160), "> COMMS: DOCKING CLEARANCE GRANTED", 16, GnDim);
			// Befehlsleiste unten
			float x = 300;
			foreach (var (k, t, on) in new[] { ("F", "FIRE", true), ("G", "TRACTOR", false), ("M", "AUTO", false), ("D", "DOCK", false), ("P", "AUTOPILOT", false) })
			{
				var r = new Rect2(x, 664, 152, 40);
				if (on) g.DrawRect(r, new Color(Gn, 0.25f));
				g.DrawRect(r, Gn, false, 2);
				g.Text(new Vector2(x + 8, 690), $"[{k}] {t}", 16, on ? Gn : GnDim);
				x += 160;
			}
			// Stick
			var c = new Vector2(130, 560);
			g.DrawArc(c, 80, 0, Mathf.Tau, 48, new Color(Gn, 0.6f), 2); g.DrawCircle(c + new Vector2(14, -10), 28, new Color(Gn, 0.25f));
			g.DrawLine(c - new Vector2(80, 0), c + new Vector2(80, 0), new Color(Gn, 0.25f), 1); g.DrawLine(c - new Vector2(0, 80), c + new Vector2(0, 80), new Color(Gn, 0.25f), 1);
		});
		Scan(root, 0.18f);
	}

	public static void Dock(Control root, ProtoData d)
	{
		ProtoVariants.Dim(root, 0.9f);
		var mono = MonoFont();
		G(root, 0, 0, 1280, 720, g =>
		{
			g.Custom = mono;
			g.DrawRect(new Rect2(24, 20, 1232, 680), Gn, false, 2);
			g.Text(new Vector2(44, 56), $"{d.Station.ToUpper()}  ::  {d.Type.ToUpper()}  ::  {d.Faction.ToUpper()}", 22, Gn);
			g.TextR(1236, 56, $"CRED {N0(d.PlayerCredits)}", 22, Am);
			float tx = 44;
			foreach (var (t, on) in new[] { ("TRADE", true), ("REPAIR", false), ("FUEL", false), ("OUTFIT", false), ("YARD", false), ("MISSIONS", false) })
			{
				var r = new Rect2(tx, 76, 132, 34);
				if (on) g.DrawRect(r, Gn); else g.DrawRect(r, GnDim, false, 1.5f);
				g.Text(new Vector2(tx, 99), t, 17, on ? Colors.Black : GnDim, HorizontalAlignment.Center, 132);
				tx += 140;
			}
			// Tabelle
			g.Text(new Vector2(48, 150), "ITEM", 16, GnDim); g.TextR(300, 150, "BUY", 16, GnDim); g.Text(new Vector2(322, 150), "TREND", 16, GnDim); g.TextR(485, 150, "STOCK", 16, GnDim); g.TextR(587, 150, "TARGET", 16, GnDim); g.TextR(674, 150, "HELD", 16, GnDim); g.TextR(754, 150, "VOL", 16, GnDim);
			g.DrawLine(new Vector2(44, 158), new Vector2(860, 158), GnDim, 1);
			int i = 0;
			foreach (var r in d.Rows)
			{
				float y = 188 + i * 36;
				bool sel = i == 0;
				var (tl, _) = Trend(r);
				if (sel) g.DrawRect(new Rect2(40, y - 22, 824, 30), Gn);
				var col = sel ? Colors.Black : Gn;
				var dim = sel ? Colors.Black : GnDim;
				g.Text(new Vector2(48, y), r.Name.ToUpper().PadRight(14), 18, col);
				g.Text(new Vector2(224, y), N1(r.Price).PadLeft(7), 18, col);
				g.Text(new Vector2(322, y), (tl == "teuer" ? "HIGH" : tl == "günstig" ? "LOW" : "NORM").PadRight(6), 18, sel ? Colors.Black : tl == "teuer" ? Am : tl == "günstig" ? Gn : GnDim);
				g.Text(new Vector2(420, y), N0(r.Amount).PadLeft(6), 18, col);
				g.Text(new Vector2(522, y), N0(r.Target).PadLeft(6), 18, dim);
				g.Text(new Vector2(620, y), (r.Held > 0 ? N0(r.Held) : "-").PadLeft(5), 18, r.Held > 0 && !sel ? Am : dim);
				g.Text(new Vector2(722, y), "1,0", 18, dim);
				i++;
			}
			// Befehlszeile
			var s = d.Rows[0];
			g.DrawLine(new Vector2(44, 508), new Vector2(860, 508), GnDim, 1);
			g.Text(new Vector2(44, 540), $"SELECT: {s.Name.ToUpper()}   PRICE {N1(s.Price)}   AVG {N1(s.Avg)}", 18, Gn);
			g.Text(new Vector2(44, 574), "QTY  <  10  >   [1] [10] [50] [MAX]    TOTAL " + N0(s.Price * 10) + " CR", 18, Am);
			g.Text(new Vector2(44, 610), "[B] BUY   [S] SELL   [A] SELL ALL   [R] UNDO   [U] UNDOCK", 18, Gn);
			g.Text(new Vector2(44, 644), "> BEST KNOWN BUYER: BETA 13.6 (+3.9/UNIT)  ROUTE 2 JUMPS", 16, GnDim);
			// Schiffsstatus
			g.DrawLine(new Vector2(890, 130), new Vector2(890, 680), GnDim, 1);
			g.Text(new Vector2(912, 156), "SHIP STATUS", 18, Gn);
			g.Text(new Vector2(912, 192), $"HULL  {Seg(d.Hull, 10)} {(int)d.Hull}", 17, Gn);
			g.Text(new Vector2(912, 220), $"SHLD  {Seg(d.Shield, 10)} {(int)d.Shield}", 17, Gn);
			g.Text(new Vector2(912, 248), $"FUEL  {Seg(d.Fuel * 100, 10)} {(int)(d.Fuel * 100)}%", 17, Am);
			g.Text(new Vector2(912, 276), $"PWR   {Seg(75, 10)} 9/12", 17, GnDim);
			g.Text(new Vector2(912, 320), "CARGO", 18, Gn);
			g.Text(new Vector2(912, 348), $"SOLID {Seg(66, 10)} 40/60", 17, Gn);
			g.Text(new Vector2(912, 376), $"CANST {Seg(30, 10)} 12/40", 17, Gn);
			g.Text(new Vector2(912, 420), "ON BOARD", 18, Gn);
			int k = 0;
			foreach (var r in d.Rows.Where(x => x.Held > 0)) { g.Text(new Vector2(912, 450 + k * 26), $"{r.Name.ToUpper().PadRight(12)}{N0(r.Held),4}", 16, GnDim); k++; }
		});
		Scan(root, 0.2f);
	}
}
