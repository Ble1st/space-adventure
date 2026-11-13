using System;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Farbpalette und Stilhelfer der UI-Bausteine (optische Vorlage: godot/prototypes/ProtoKit.cs).
/// Alle wiederverwendbaren Bausteine liegen in diesem Kit, Bildschirme bauen darauf.
/// </summary>
public static class UiKit
{
	public static readonly Color PanelBg = new(0.04f, 0.07f, 0.13f, 0.84f);
	public static readonly Color PanelSolid = new(0.05f, 0.08f, 0.15f, 0.97f);
	public static readonly Color Card = new(0.02f, 0.04f, 0.09f, 0.88f);
	public static readonly Color Edge = new(0.35f, 0.6f, 0.95f, 0.7f);
	public static readonly Color Cyan = new(0.35f, 0.8f, 1f);
	public static readonly Color Orange = new(1f, 0.7f, 0.2f);
	public static readonly Color Green = new(0.3f, 0.9f, 0.5f);
	public static readonly Color Red = new(1f, 0.38f, 0.38f);
	public static readonly Color Dim = new(0.68f, 0.76f, 0.9f, 0.85f);
	public static readonly Color White = new(0.95f, 0.97f, 1f);

	/// <summary>Abgerundete Fläche mit optionalem Rand.</summary>
	public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 10, int bw = 2, int margin = 10)
	{
		var s = new StyleBoxFlat { BgColor = bg };
		if (border is { } b) { s.BorderColor = b; s.SetBorderWidthAll(bw); }
		s.SetCornerRadiusAll(radius);
		s.SetContentMarginAll(margin);
		return s;
	}

	/// <summary>Textzeile: Label mit Größe und Farbe, nicht klickbar.</summary>
	public static Label Text(string text, int size = 18, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
	{
		var l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		l.AddThemeFontSizeOverride("font_size", size);
		l.AddThemeColorOverride("font_color", color ?? White);
		return l;
	}

	/// <summary>Kind direkt mit Position und Größe an einen Parent hängen (freie Platzierung, kein Container).</summary>
	public static T At<T>(Control parent, T c, float x, float y, float w = 0, float h = 0) where T : Control
	{
		// Gerätetest (Phase 26): Textzeilen mit fester Breite laufen nie über ihren Platz hinaus — sie werden mit „…“ gekürzt statt in Nachbarspalten,
		// Radarkreise oder Kartenränder zu ragen (systematisch für jedes Label, das mit Breite platziert wird). Vor dem Setzen der Größe, sonst wächst das
		// Label auf seine Textbreite.
		if (w > 0 && c is Label l && l.AutowrapMode == TextServer.AutowrapMode.Off) { l.ClipText = true; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }
		parent.AddChild(c);
		c.Position = new Vector2(x, y);
		if (w > 0 || h > 0) { c.CustomMinimumSize = new Vector2(w, h); c.Size = new Vector2(w, h); }
		return c;
	}

	/// <summary>
	/// Karte mit freier Platzierung der Kinder: ein PanelContainer würde alle Kinder auf die volle Fläche
	/// strecken, deshalb ein plain Control mit Hintergrund-Panel.
	/// </summary>
	public static Control CardBg(Control parent, float x, float y, float w, float h, Color? bg = null, Color? border = null, int radius = 12, int bw = 2)
	{
		var c = At(parent, new Control { MouseFilter = Control.MouseFilterEnum.Stop }, x, y, w, h);
		var p = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
		p.AddThemeStyleboxOverride("panel", Box(bg ?? PanelSolid, border, radius, bw, 0));
		c.AddChild(p);
		p.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		return c;
	}

	/// <summary>
	/// Fließtext mit manuellem Zeilenumbruch: ein Label mit Autowrap ist in frei platzierten Controls unzuverlässig, deshalb hier selbst umbrechen.
	/// Zweiter Gerätetest Phase 28: die Breite wird mit der Schrift gemessen (vorher geschätzt — Zeilen liefen über und wurden mit „…“ gekürzt).
	/// Liefert die y-Position unter dem Text.
	/// </summary>
	public static float WrappedText(Control parent, string text, float x, float y, float w, int size, Color color, int lineH = 0)
	{
		if (lineH <= 0) lineH = size + 6;
		float cy = y;
		foreach (var line in Wrap(text, size, w)) { At(parent, Text(line, size, color), x, cy, w, lineH); cy += lineH; }
		return cy;
	}

	/// <summary>Zweiter Gerätetest Phase 28: Zeilen eines Fließtexts bei Schriftgröße <paramref name="size"/> und Breite <paramref name="w"/> (gemessen).</summary>
	public static System.Collections.Generic.List<string> Wrap(string text, int size, float w)
	{
		var font = ThemeDB.FallbackFont;
		float Width(string t) => font.GetStringSize(t, HorizontalAlignment.Left, -1, size).X;
		var lines = new System.Collections.Generic.List<string>();
		var line = new System.Text.StringBuilder();
		foreach (var word in text.Split(' '))
		{
			string next = line.Length > 0 ? line + " " + word : word;
			if (line.Length > 0 && Width(next) > w - 4f) { lines.Add(line.ToString()); line.Clear(); line.Append(word); }
			else { line.Clear(); line.Append(next); }
		}
		if (line.Length > 0) lines.Add(line.ToString());
		return lines;
	}
}
