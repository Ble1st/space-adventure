using System.Collections.Generic;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Gerätetest (Phase 26): Kollisionsvermeidung für Beschriftungen auf gezeichneten Karten (Universum, Sektor, Sternenkarte). Jede Beschriftung sucht unter festen
/// Kandidaten (unter, über, rechts, links, weiter weg) den ersten Platz, der keine schon gesetzte Beschriftung oder freigehaltene Fläche (Knoten, Symbole)
/// überlappt und im Zeichenbereich liegt; findet sie keinen, entfällt sie (lieber eine Beschriftung weniger als ein unlesbarer Stapel). Zu lange Texte werden
/// mit „…“ gekürzt. Reine Darstellung, keine Spielregel.
/// </summary>
public sealed class LabelLayout
{
	readonly List<Rect2> _taken = new();
	readonly Rect2 _bounds;
	readonly Font _font;

	public LabelLayout(Rect2 bounds, Font? font = null)
	{
		_bounds = bounds;
		_font = font ?? ThemeDB.FallbackFont;
	}

	/// <summary>Fläche freihalten (z. B. ein Knoten oder Symbol), damit keine Beschriftung darauf landet.</summary>
	public void Reserve(Rect2 r) => _taken.Add(r);

	/// <summary>Hintergrund unter jeder Beschriftung (Alpha 0 = keiner).</summary>
	public Color Backdrop { get; set; } = new(0.02f, 0.04f, 0.09f, 0.85f);

	/// <summary>Kreis freihalten.</summary>
	public void ReserveCircle(Vector2 c, float r) => _taken.Add(new Rect2(c - new Vector2(r, r), new Vector2(2 * r, 2 * r)));

	/// <summary>
	/// Beschriftung nahe <paramref name="anchor"/> setzen und zeichnen. <paramref name="gap"/> = Abstand vom Anker (z. B. Symbolradius). Liefert, ob sie
	/// gezeichnet wurde.
	/// </summary>
	public bool Draw(CanvasItem canvas, Vector2 anchor, string text, int size, Color color, float gap = 12f, float maxWidth = 180f)
	{
		string full = text;
		text = KitButton.Fit(_font, text, size, maxWidth);
		var sz = _font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
		float w = sz.X + 4f, h = size + 4f;
		var candidates = new[]
		{
			new Vector2(-w / 2f, gap), new Vector2(-w / 2f, -gap - h), new Vector2(gap, -h / 2f), new Vector2(-gap - w, -h / 2f),
			new Vector2(-w / 2f, gap + h), new Vector2(-w / 2f, -gap - 2 * h), new Vector2(gap, h / 2f), new Vector2(-gap - w, h / 2f),
			new Vector2(gap, -1.5f * h), new Vector2(-gap - w, -1.5f * h),
		};
		foreach (var off in candidates)
		{
			var r = new Rect2(anchor + off, new Vector2(w, h));
			if (!_bounds.Encloses(r)) continue;
			bool free = true;
			foreach (var t in _taken) if (t.Intersects(r)) { free = false; break; }
			if (!free) continue;
			_taken.Add(r);
			// Gerätetest Phase 27: dunkler Hintergrund unter jeder Beschriftung — Linien (Tore, Routen), die darunter laufen, verdecken sie nicht mehr
			if (Backdrop.A > 0f) canvas.DrawRect(r.Grow(1f), Backdrop);
			UiKit.DrawText(canvas, 1000 + _taken.Count, _font, r.Position + new Vector2(2f, size), text, size, color, full: full);   // Runde 35b/c: gezeichnet und gemeldet
			return true;
		}
		return false;
	}

	/// <summary>
	/// Runde 37 (Handytest r35: Stationen ohne Namen auf der Sektorkarte): letzter Versuch — Plätze weiter weg (vier Ringe, zwölf Richtungen, feste Reihenfolge)
	/// mit einer dünnen Linie vom Anker zur Beschriftung, damit die Zuordnung erkennbar bleibt.
	/// </summary>
	readonly List<(Vector2 A, Vector2 B)> _leaders = new();

	/// <summary>Runde 37b: schneidet die Strecke eine schon gezogene Leitlinie oder eine gesetzte Fläche, die den Anfangspunkt nicht enthält?</summary>
	bool Crosses(Vector2 a, Vector2 b)
	{
		foreach (var (c, d) in _leaders) if (Cross(a, b, c, d)) return true;
		foreach (var t in _taken)
		{
			if (t.Grow(1f).HasPoint(a) || t.Grow(1f).HasPoint(b)) continue;
			var e = t.End;
			var corners = new[] { t.Position, new Vector2(e.X, t.Position.Y), e, new Vector2(t.Position.X, e.Y) };
			for (int i = 0; i < 4; i++) if (Cross(a, b, corners[i], corners[(i + 1) % 4])) return true;
		}
		return false;
	}

	static bool Cross(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2)
	{
		static float O(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
		float d1 = O(q, q2, p), d2 = O(q, q2, p2), d3 = O(p, p2, q), d4 = O(p, p2, q2);
		return (d1 > 0f) != (d2 > 0f) && (d3 > 0f) != (d4 > 0f) && d1 != 0f && d2 != 0f && d3 != 0f && d4 != 0f;
	}

	public bool DrawWithLeader(CanvasItem canvas, Vector2 anchor, string text, int size, Color color, float gap, float maxWidth = 160f, bool allowCross = false)
	{
		string full = text;
		text = KitButton.Fit(_font, text, size, maxWidth);
		var sz = _font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
		float w = sz.X + 4f, h = size + 4f;
		for (int ring = 1; ring <= 4; ring++)
		{
			float rad = gap + ring * (h + 6f);
			for (int k = 0; k < 12; k++)
			{
				float a = k * Mathf.Tau / 12f + (ring % 2) * Mathf.Tau / 24f;
				var p = anchor + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
				var r = new Rect2(p - new Vector2(Mathf.Cos(a) < -0.2f ? w : Mathf.Cos(a) > 0.2f ? 0f : w / 2f, Mathf.Sin(a) < -0.2f ? h : Mathf.Sin(a) > 0.2f ? 0f : h / 2f), new Vector2(w, h));
				if (!_bounds.Encloses(r)) continue;
				bool free = true;
				foreach (var t in _taken) if (t.Intersects(r)) { free = false; break; }
				if (!free) continue;
				var near = new Vector2(Mathf.Clamp(anchor.X, r.Position.X, r.End.X), Mathf.Clamp(anchor.Y, r.Position.Y, r.End.Y));
				// Runde 37b (Handytest r37: Leitlinien kreuzten sich): die Linie kreuzt keine andere Leitlinie und läuft durch keine gesetzte Beschriftung
				if (!allowCross && Crosses(anchor, near)) continue;
				_taken.Add(r);
				_leaders.Add((anchor, near));
				canvas.DrawLine(anchor, near, new Color(color, 0.6f), 1f, true);
				if (Backdrop.A > 0f) canvas.DrawRect(r.Grow(1f), Backdrop);
				UiKit.DrawText(canvas, 1000 + _taken.Count, _font, r.Position + new Vector2(2f, size), text, size, color, full: full);
				return true;
			}
		}
		return false;
	}
}
