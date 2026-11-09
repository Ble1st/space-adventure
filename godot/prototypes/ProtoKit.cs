using System;
using System.Globalization;
using Godot;

namespace SpaceAdventure.Prototypes;

/// <summary>Zeichenfläche mit frei belegbarem Zeichenaufruf (Ringe, Radar, Schiffsskizze).</summary>
public partial class Gfx : Control
{
	public Action<Gfx>? Paint;
	public override void _Draw() => Paint?.Invoke(this);
	public Font? Custom;
	public Font F => Custom ?? ThemeDB.FallbackFont;
	public void Text(Vector2 p, string s, int size, Color c, HorizontalAlignment a = HorizontalAlignment.Left, float w = -1) => DrawString(F, p, s, a, w, size, c);
	/// <summary>Rechtsbündiger Text, der bei Breite w endet.</summary>
	public void TextR(float right, float y, string s, int size, Color c) => DrawString(F, new Vector2(right - 600, y), s, HorizontalAlignment.Right, 600, size, c);
}

/// <summary>Kleine Bausteine für die Entwürfe.</summary>
public static class K
{
	public static readonly Color PanelBg = new(0.04f, 0.07f, 0.13f, 0.84f);
	public static readonly Color PanelSolid = new(0.05f, 0.08f, 0.15f, 0.97f);
	public static readonly Color Edge = new(0.35f, 0.6f, 0.95f, 0.7f);
	public static readonly Color Cyan = new(0.35f, 0.8f, 1f);
	public static readonly Color Orange = new(1f, 0.7f, 0.2f);
	public static readonly Color Green = new(0.3f, 0.9f, 0.5f);
	public static readonly Color Red = new(1f, 0.38f, 0.38f);
	public static readonly Color Dim = new(0.68f, 0.76f, 0.9f, 0.85f);
	public static readonly Color White = new(0.95f, 0.97f, 1f);
	static readonly CultureInfo De = new("de-DE");

	public static string N0(float v) => v.ToString("N0", De);
	public static string N1(float v) => v.ToString("N1", De);

	public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 10, int bw = 0, int margin = 10)
	{
		var s = new StyleBoxFlat { BgColor = bg };
		if (border is { } b) { s.BorderColor = b; s.SetBorderWidthAll(bw <= 0 ? 2 : bw); }
		s.SetCornerRadiusAll(radius); s.SetContentMarginAll(margin);
		return s;
	}

	public static Label L(string text, int size = 18, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
	{
		var l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		l.AddThemeFontSizeOverride("font_size", size);
		l.AddThemeColorOverride("font_color", color ?? White);
		return l;
	}

	public static Button B(string text, float w, float h, Color? bg = null, int size = 18, Color? fg = null, int radius = 10, Color? border = null)
	{
		var c = bg ?? new Color(0.1f, 0.18f, 0.32f, 0.92f);
		var b = new Button { Text = text, CustomMinimumSize = new Vector2(w, h), Size = new Vector2(w, h), FocusMode = Control.FocusModeEnum.None };
		b.AddThemeStyleboxOverride("normal", Box(c, border ?? Edge, radius, 2, 6));
		b.AddThemeStyleboxOverride("hover", Box(c.Lightened(0.15f), border ?? Edge, radius, 2, 6));
		b.AddThemeStyleboxOverride("pressed", Box(c.Lightened(0.3f), border ?? Edge, radius, 2, 6));
		b.AddThemeStyleboxOverride("disabled", Box(c.Darkened(0.4f), null, radius, 0, 6));
		b.AddThemeFontSizeOverride("font_size", size);
		b.AddThemeColorOverride("font_color", fg ?? White);
		return b;
	}

	public static ProgressBar Bar(float value, Color fill, float w, float h, float max = 100)
	{
		var p = new ProgressBar { MaxValue = max, Value = value, ShowPercentage = false, CustomMinimumSize = new Vector2(w, h), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
		p.AddThemeStyleboxOverride("background", Box(new Color(0f, 0f, 0f, 0.45f), null, (int)(h / 2), 0, 0));
		p.AddThemeStyleboxOverride("fill", Box(fill, null, (int)(h / 2), 0, 0));
		return p;
	}

	public static PanelContainer Pnl(Color? bg = null, Color? border = null, int radius = 12, int margin = 12)
	{
		var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		p.AddThemeStyleboxOverride("panel", Box(bg ?? PanelBg, border ?? Edge, radius, 2, margin));
		return p;
	}

	public static T At<T>(Control parent, T c, float x, float y, float w = 0, float h = 0) where T : Control
	{
		parent.AddChild(c);
		c.Position = new Vector2(x, y);
		if (w > 0 || h > 0) { c.CustomMinimumSize = new Vector2(w, h); c.Size = new Vector2(w, h); }
		return c;
	}

	public static Gfx G(Control parent, float x, float y, float w, float h, Action<Gfx> paint)
	{
		var g = new Gfx { Paint = paint, MouseFilter = Control.MouseFilterEnum.Ignore };
		return At(parent, g, x, y, w, h);
	}

	/// <summary>Fenster mit freier Platzierung der Kinder (ein PanelContainer würde alle Kinder strecken).</summary>
	public static Control Win(Control parent, float x, float y, float w, float h, Color bg, Color? border = null, int radius = 14)
	{
		var c = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		parent.AddChild(c); c.Position = new Vector2(x, y); c.Size = new Vector2(w, h); c.CustomMinimumSize = new Vector2(w, h);
		var p = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
		p.AddThemeStyleboxOverride("panel", Box(bg, border, radius, 2, 0));
		c.AddChild(p); p.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		return c;
	}

	public static Font MonoFont()
	{
		var f = new SystemFont(); f.FontNames = new[] { "DejaVu Sans Mono", "Liberation Mono", "Monospace" }; return f;
	}

	public static VBoxContainer V(int sep = 6) { var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; v.AddThemeConstantOverride("separation", sep); return v; }
	public static HBoxContainer H(int sep = 8) { var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; h.AddThemeConstantOverride("separation", sep); return h; }

	public static (string label, Color color) Trend(MarketRow r)
	{
		float q = r.Avg <= 0 ? 1f : r.Price / r.Avg;
		return q > 1.08f ? ("teuer", Orange) : q < 0.92f ? ("günstig", Green) : ("normal", Dim);
	}

	/// <summary>Einfache Schiffsskizze (Draufsicht) in einem Rechteck.</summary>
	public static void Ship(Gfx g, Vector2 c, float s, Color body, Color accent)
	{
		var pts = new[] { c + new Vector2(0, -0.9f * s), c + new Vector2(0.28f * s, -0.2f * s), c + new Vector2(0.95f * s, 0.35f * s), c + new Vector2(0.9f * s, 0.55f * s), c + new Vector2(0.3f * s, 0.4f * s), c + new Vector2(0.2f * s, 0.8f * s), c + new Vector2(-0.2f * s, 0.8f * s), c + new Vector2(-0.3f * s, 0.4f * s), c + new Vector2(-0.9f * s, 0.55f * s), c + new Vector2(-0.95f * s, 0.35f * s), c + new Vector2(-0.28f * s, -0.2f * s) };
		g.DrawColoredPolygon(pts, body);
		g.DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[4], pts[5], pts[6], pts[7], pts[8], pts[9], pts[10], pts[0] }, accent, 2f, true);
		g.DrawCircle(c + new Vector2(0, -0.25f * s), 0.12f * s, accent);
	}

	public static void Brackets(Gfx g, Rect2 r, float len, Color c, float w = 2f)
	{
		var p = r.Position; var e = r.End;
		foreach (var (a, dx, dy) in new[] { (p, 1f, 1f), (new Vector2(e.X, p.Y), -1f, 1f), (new Vector2(p.X, e.Y), 1f, -1f), (e, -1f, -1f) })
		{
			g.DrawLine(a, a + new Vector2(dx * len, 0), c, w);
			g.DrawLine(a, a + new Vector2(0, dy * len), c, w);
		}
	}
}
