using System;
using System.Linq;
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

	/// <summary>Runde 35b: nur für die Zeitmessung im Selbsttest abschaltbar (Kosten des Einpassens).</summary>
	public static bool AutoFit = true;

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
		// Runde 34b (Prüfung B1/B3: schmalere Bereiche auf dem Handy kürzten Texte): passt ein einzeiliger Text nicht in seine Breite, wird die Schrift erst bis 11
		// verkleinert (Core: UiLayout.FitFontSize, mit der Schrift gemessen); erst dann greift „…“
		if (AutoFit && w > 0 && c is Label fl && fl.AutowrapMode == TextServer.AutowrapMode.Off && fl.Text.Length > 0) FitLabel(fl, w);
		return c;
	}

	/// <summary>
	/// Runde 35: unterste Kante des Inhalts unter <paramref name="root"/> (in dessen Koordinaten) — rekursiv über frei platzierte Kinder; der Inhalt rollender
	/// Listen zählt nicht (er ist beschnitten), nur die Liste selbst. Grundlage, damit ein Bereich als Ganzes rollt, wenn er höher ist als seine Fläche.
	/// </summary>
	public static float ContentBottom(Control root)
	{
		float bottom = 0f;
		void Walk(Control c, Vector2 off)
		{
			foreach (var ch in c.GetChildren())
			{
				if (ch is not Control cc || !cc.Visible) continue;
				var p = off + cc.Position;
				bottom = MathF.Max(bottom, p.Y + MathF.Max(cc.Size.Y, cc.GetCombinedMinimumSize().Y));
				if (cc is not ScrollContainer && cc is not Container) Walk(cc, p);
			}
		}
		Walk(root, Vector2.Zero);
		return bottom;
	}

	/// <summary>Runde 34b: Schrift eines einzeiligen Labels so weit verkleinern (bis 11), dass der Text in <paramref name="w"/> passt.</summary>
	public static void FitLabel(Label l, float w)
	{
		var font = l.GetThemeFont("font");
		int size = l.GetThemeFontSize("font_size");
		// Runde 35b: nie unter die Schrift-Untergrenze der Ebene (Core: UiLayout.FontFloor) — die UI-Vergrößerung wird nicht wieder weggekürzt;
		// passt der Text dann nicht und ist Platz für zwei Zeilen, bricht er um
		int floor = SpaceSim.Core.Ui.UiLayout.FontFloor(size, LayerFactor(l));
		int fit = SpaceSim.Core.Ui.UiLayout.FitFontSize(fs => font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, fs).X, w, size, floor);
		if (fit != size) l.AddThemeFontSizeOverride("font_size", fit);
		if (font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, fit).X > w + 0.5f && l.Size.Y >= 2f * font.GetHeight(fit) - 2f)
		{ l.ClipText = false; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.MaxLinesVisible = (int)(l.Size.Y / font.GetHeight(fit)); }
	}

	/// <summary>Runde 35b: Faktor der Ebene, in der das Element liegt (Maßstab der globalen Transformation; außerhalb des Baums 1).</summary>
	public static float LayerFactor(CanvasItem c) => c.IsInsideTree() ? MathF.Max(1f, c.GetGlobalTransform().Scale.Y) : 1f;

	/// <summary>
	/// Runde 35b: selbst gezeichneten Text einpassen und zeichnen — erst kleiner bis <paramref name="minSize"/>, dann gekürzt (am Ende oder in der Mitte),
	/// und an <see cref="DrawnText"/> melden (für <c>--ui-check</c>). <paramref name="pos"/> wie bei DrawString (linke Kante, Grundlinie);
	/// bei Mitte/rechts ist <paramref name="maxW"/> die Ausrichtungsbreite. Liefert die gezeichnete Breite.
	/// </summary>
	public static float DrawFit(CanvasItem ci, int slot, Font font, Vector2 pos, string full, int size, float maxW, Color col,
		HorizontalAlignment align = HorizontalAlignment.Left, bool middle = false, int minSize = 11)
	{
		if (full.Length == 0) return 0f;
		var (shown, fs) = FitText(ci, font, full, size, maxW, middle, minSize);
		ci.DrawString(font, pos, shown, align, align == HorizontalAlignment.Left ? -1 : maxW + 1f, fs, col);
		DrawnText.Report(ci, slot, pos, shown, full, fs, font, align == HorizontalAlignment.Left ? -1 : maxW, align);
		return font.GetStringSize(shown, HorizontalAlignment.Left, -1, fs).X;
	}

	/// <summary>
	/// Runde 35c (Prüfung W2): selbst gezeichneten Text ohne Einpassen zeichnen und an <see cref="DrawnText"/> melden — der einzige Weg zu DrawString in
	/// godot/scripts/Ui (Wächter: Test <c>Runde35cTests.UiZeichnetTexteNurMitMeldung</c>). <paramref name="width"/> &gt; 0 = Ausrichtungsbreite.
	/// </summary>
	public static void DrawText(CanvasItem ci, int slot, Font font, Vector2 pos, string text, int size, Color col, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f, string? full = null)
	{
		if (text.Length == 0) return;
		ci.DrawString(font, pos, text, align, width, size, col);
		DrawnText.Report(ci, slot, pos, text, full ?? text, size, font, width, align);
	}

	/// <summary>Runde 35c: Einpassen ohne Zeichnen (dieselbe Regel wie <see cref="DrawFit"/>) — gezeigte Fassung und Schriftgröße.</summary>
	public static (string Shown, int Size) FitText(CanvasItem ci, Font font, string full, int size, float maxW, bool middle = false, int minSize = 11)
	{
		minSize = Math.Max(minSize, SpaceSim.Core.Ui.UiLayout.FontFloor(size, LayerFactor(ci)));   // Runde 35b: Untergrenze der Ebene
		int fs = maxW > 0f ? SpaceSim.Core.Ui.UiLayout.FitFontSize(s => font.GetStringSize(full, HorizontalAlignment.Left, -1, s).X, maxW, size, minSize) : size;
		string shown = full;
		if (maxW > 0f && font.GetStringSize(full, HorizontalAlignment.Left, -1, fs).X > maxW + 0.5f)
			shown = middle ? SpaceSim.Core.Ui.UiLayout.MiddleEllipsis(full, t => font.GetStringSize(t, HorizontalAlignment.Left, -1, fs).X, maxW) : KitButton.Fit(font, full, fs, maxW);
		return (shown, fs);
	}

	/// <summary>
	/// Runde 35b: wie <see cref="DrawFit"/>, passt der Text auch an der Untergrenze nicht in eine Zeile, in zwei Zeilen (an Wortgrenzen, Zeilenabstand
	/// Schrift + 3); zuletzt wird die zweite Zeile gekürzt. <paramref name="pos"/> ist die Grundlinie der ersten Zeile, wenn zweizeilig, sonst
	/// <paramref name="oneLine"/>.
	/// </summary>
	public static void DrawFit2(CanvasItem ci, int slot, Font font, Vector2 pos, Vector2 oneLine, string full, int size, float maxW, Color col, int minSize = 11)
	{
		int floor = Math.Max(minSize, SpaceSim.Core.Ui.UiLayout.FontFloor(size, LayerFactor(ci)));
		if (font.GetStringSize(full, HorizontalAlignment.Left, -1, floor).X <= maxW + 0.5f || !full.Contains(' '))
		{ DrawFit(ci, slot, font, oneLine, full, size, maxW, col, minSize: floor); return; }
		for (int fs = size; fs >= floor; fs--)
		{
			var words = full.Split(' ');
			string a = "";
			int i = 0;
			for (; i < words.Length; i++)
			{
				string t = a.Length == 0 ? words[i] : a + " " + words[i];
				if (font.GetStringSize(t, HorizontalAlignment.Left, -1, fs).X > maxW + 0.5f) break;
				a = t;
			}
			if (a.Length == 0) continue;
			string rest = string.Join(" ", words[i..]);
			// zu langes Wort in der zweiten Zeile (Komposita: „Schiffsbausatz“): mit Trennstrich über beide Zeilen teilen
			if (font.GetStringSize(rest, HorizontalAlignment.Left, -1, fs).X > maxW + 0.5f && i < words.Length)
			{
				string wd = words[i];
				for (int cut = wd.Length - 3; cut >= 3; cut--)
				{
					string a2 = a + " " + wd[..cut] + "-", r2 = string.Join(" ", new[] { wd[cut..] }.Concat(words[(i + 1)..]));
					if (font.GetStringSize(a2, HorizontalAlignment.Left, -1, fs).X <= maxW + 0.5f && font.GetStringSize(r2, HorizontalAlignment.Left, -1, fs).X <= maxW + 0.5f)
					{ a = a2; rest = r2; break; }
				}
			}
			if (font.GetStringSize(rest, HorizontalAlignment.Left, -1, fs).X <= maxW + 0.5f || fs == floor)
			{
				DrawFit(ci, slot, font, pos, a, fs, maxW, col, minSize: fs);
				DrawFit(ci, slot + 100, font, pos + new Vector2(0, fs + 3), rest, fs, maxW, col, middle: true, minSize: fs);
				return;
			}
		}
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
