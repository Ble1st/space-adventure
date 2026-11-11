using System;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Phase 18: Zwei-Tipp-Bestätigung für Tasten mit Folgen, die sich nicht rückgängig machen lassen (z. B. „Abwerfen“): Der erste Tipp macht die Taste
/// scharf („Wirklich?“), erst ein zweiter Tipp innerhalb von <see cref="WindowMs"/> führt aus. Der Zustand lebt im Bildschirm (nicht in der Taste),
/// damit er den regelmäßigen Neuaufbau der Bereiche übersteht. Mobile-first: kein Dialog, kein langes Drücken.
/// Phase 19: Läuft die Frist ab, springt die Beschriftung von selbst zurück (<see cref="KitConfirmTicker"/> an jeder verdrahteten Taste prüft je Bild) —
/// kein Bildschirm muss das selbst lösen.
/// </summary>
public sealed class KitConfirm
{
	/// <summary>So lange bleibt eine Taste nach dem ersten Tipp scharf (Millisekunden).</summary>
	public const ulong WindowMs = 3000;

	/// <summary>Mindestabstand zwischen erstem und zweitem Tipp (Millisekunden).</summary>
	public static ulong DebounceMs { get; set; } = 250;

	/// <summary>Phase 19: Uhr in Millisekunden (Standard <see cref="Time.GetTicksMsec"/>); der Selbsttest stellt sie vor, um den Ablauf der Frist zu prüfen.</summary>
	public static Func<ulong> Now { get; set; } = Time.GetTicksMsec;

	/// <summary>Meta-Schlüssel, unter dem eine verdrahtete Taste ihren Bestätigungsschlüssel trägt (für Test-Hooks, <see cref="Find"/>).</summary>
	public const string MetaKey = "kit_confirm_key";

	string? _key;
	ulong _at;

	/// <summary>Ist die Taste mit diesem Schlüssel gerade scharf?</summary>
	public bool IsArmed(string key) => _key == key && Now() - _at <= WindowMs;

	/// <summary>Ein Tipp: true = bestätigt (zweiter Tipp im Fenster, ausführen), false = jetzt scharf gemacht.</summary>
	public bool Tap(string key)
	{
		if (IsArmed(key))
		{
			// Entprellen: ein zweites Ereignis desselben Tipps (Touch + emulierte Maus) bestätigt nicht
			if (Now() - _at < DebounceMs) return false;
			_key = null;
			return true;
		}
		_key = key;
		_at = Now();
		return false;
	}

	public void Reset() => _key = null;

	/// <summary>
	/// Verdrahtet eine Taste: Beschriftung nach Zustand, <paramref name="confirmed"/> beim zweiten Tipp. Phase 19: ein <see cref="KitConfirmTicker"/> als Kind
	/// der Taste stellt die Beschriftung zurück, sobald die Frist abgelaufen ist.
	/// </summary>
	public KitButton Wire(KitButton b, string key, string text, Action confirmed)
	{
		void Label() { bool armed = IsArmed(key); b.Text = armed ? "Wirklich?" : text; b.Sub = armed ? "nochmal tippen" : ""; }
		b.State = KitButtonState.Warn;
		b.SetMeta(MetaKey, key);
		Label();
		b.Pressed += () => { if (Tap(key)) confirmed(); else Label(); };
		b.AddChild(new KitConfirmTicker(() => IsArmed(key), Label));
		return b;
	}

	/// <summary>Phase 19 (Test-Hook): die unter <paramref name="root"/> verdrahtete Taste mit diesem Schlüssel, sonst null.</summary>
	public static KitButton? Find(Node root, string key)
	{
		if (root.IsQueuedForDeletion()) return null;   // beim Neuaufbau verworfene Tasten liegen bis zum Bildende noch im Baum
		if (root is KitButton kb && kb.HasMeta(MetaKey) && kb.GetMeta(MetaKey).AsString() == key) return kb;
		foreach (var child in root.GetChildren())
			if (Find(child, key) is { } found) return found;
		return null;
	}
}
