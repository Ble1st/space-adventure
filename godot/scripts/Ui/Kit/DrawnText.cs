using System.Collections.Generic;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Runde 35b (Prüfung: das Prüfwerkzeug sah selbst gezeichnete Texte nicht): Meldeschnittstelle für Texte, die mit <c>DrawString</c> in Karten und Zeilen
/// gezeichnet werden (Universums- und Sektorkarte, Handelszeilen, Besitz-Zeilen …). Jeder Zeichner meldet je Text sein Rechteck (lokal), Schriftgröße und ob
/// er gekürzt wurde; <c>--ui-check</c> liest die Meldungen sichtbarer Knoten. Nur Anzeige-Diagnose, keine Spiellogik; billig (ein Wörterbucheintrag je Text).
/// </summary>
public static class DrawnText
{
	public readonly record struct Entry(ulong Owner, Rect2 Local, string Shown, string Full, int Size);
	static readonly Dictionary<(ulong, int), Entry> _entries = new();
	/// <summary>Nur im Prüfwerkzeug eingeschaltet (sonst kostet es nichts).</summary>
	public static bool Enabled;

	/// <summary>Meldet einen gezeichneten Text: <paramref name="baseline"/> wie bei DrawString (links, Grundlinie), gezeigte und volle Fassung.</summary>
	public static void Report(CanvasItem owner, int slot, Vector2 baseline, string shown, string full, int size, Font? font = null, float width = -1f, HorizontalAlignment align = HorizontalAlignment.Left)
	{
		if (!Enabled || shown.Length == 0) return;
		font ??= ThemeDB.FallbackFont;
		float tw = font.GetStringSize(shown, HorizontalAlignment.Left, -1, size).X;
		float x = baseline.X;
		if (width > 0f && align == HorizontalAlignment.Center) x += (width - tw) / 2f;
		else if (width > 0f && align == HorizontalAlignment.Right) x += width - tw;
		// Glyphenkern (Großbuchstabe 0,75 über bis Unterlänge 0,2 unter der Grundlinie), nicht die volle Zeilenhöhe — sonst zählen eng gesetzte, sich nicht berührende Zeilen als Überlappung
		_entries[(owner.GetInstanceId(), slot)] = new Entry(owner.GetInstanceId(), new Rect2(x, baseline.Y - 0.75f * size, tw, 0.95f * size), shown, full, size);
	}

	/// <summary>Alle Meldungen eines Zeichners vergessen (vor dem Neuzeichnen, damit verschwundene Texte nicht stehen bleiben).</summary>
	public static void Clear(CanvasItem owner)
	{
		if (!Enabled) return;
		ulong id = owner.GetInstanceId();
		var drop = new List<(ulong, int)>();
		foreach (var k in _entries.Keys) if (k.Item1 == id) drop.Add(k);
		foreach (var k in drop) _entries.Remove(k);
	}

	public static IEnumerable<Entry> All => _entries.Values;
}
