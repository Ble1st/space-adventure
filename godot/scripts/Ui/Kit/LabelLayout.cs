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
			canvas.DrawString(_font, r.Position + new Vector2(2f, size), text, HorizontalAlignment.Left, -1, size, color);
			return true;
		}
		return false;
	}
}
