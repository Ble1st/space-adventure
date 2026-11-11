using System;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Phase 18: Zwei-Tipp-Bestätigung für Tasten mit Folgen, die sich nicht rückgängig machen lassen (z. B. „Abwerfen“): Der erste Tipp macht die Taste
/// scharf („Wirklich?“), erst ein zweiter Tipp innerhalb von <see cref="WindowMs"/> führt aus. Der Zustand lebt im Bildschirm (nicht in der Taste),
/// damit er den regelmäßigen Neuaufbau der Bereiche übersteht. Mobile-first: kein Dialog, kein langes Drücken.
/// </summary>
public sealed class KitConfirm
{
	/// <summary>So lange bleibt eine Taste nach dem ersten Tipp scharf (Millisekunden).</summary>
	public const ulong WindowMs = 3000;

	/// <summary>Mindestabstand zwischen erstem und zweitem Tipp (Millisekunden).</summary>
	public static ulong DebounceMs { get; set; } = 250;

	string? _key;
	ulong _at;

	/// <summary>Ist die Taste mit diesem Schlüssel gerade scharf?</summary>
	public bool IsArmed(string key) => _key == key && Time.GetTicksMsec() - _at <= WindowMs;

	/// <summary>Ein Tipp: true = bestätigt (zweiter Tipp im Fenster, ausführen), false = jetzt scharf gemacht.</summary>
	public bool Tap(string key)
	{
		if (IsArmed(key))
		{
			// Entprellen: ein zweites Ereignis desselben Tipps (Touch + emulierte Maus) bestätigt nicht
			if (Time.GetTicksMsec() - _at < DebounceMs) return false;
			_key = null;
			return true;
		}
		_key = key;
		_at = Time.GetTicksMsec();
		return false;
	}

	public void Reset() => _key = null;

	/// <summary>Verdrahtet eine Taste: Beschriftung nach Zustand, <paramref name="confirmed"/> beim zweiten Tipp.</summary>
	public KitButton Wire(KitButton b, string key, string text, Action confirmed)
	{
		void Label() { bool armed = IsArmed(key); b.Text = armed ? "Wirklich?" : text; b.Sub = armed ? "nochmal tippen" : ""; }
		b.State = KitButtonState.Warn;
		Label();
		b.Pressed += () => { if (Tap(key)) confirmed(); else Label(); };
		return b;
	}
}
