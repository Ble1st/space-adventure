using Godot;

namespace SpaceAdventure.Ui;

/// <summary>
/// Vereinheitlicht Maus (Desktop) und Touch (Mobil), damit mehrere Finger gleichzeitig funktionieren
/// (Stick halten und Knopf tippen). Auf Touchgeräten werden nur Touch-, sonst nur Mausereignisse ausgewertet.
/// </summary>
public static class Pointer
{
	/// <summary>Selbsttest (Geräte-Eingabe): Touchmodus erzwingen (kopflos meldet Godot keinen Touchschirm); null = Gerät.</summary>
	public static bool? ForceTouch;

	public static bool TouchMode => ForceTouch ?? DisplayServer.IsTouchscreenAvailable();

	public static bool TryRead(InputEvent e, out int index, out Vector2 pos, out bool pressed, out bool isMotion)
	{
		index = 0; pos = default; pressed = false; isMotion = false;
		if (TouchMode)
		{
			if (e is InputEventScreenTouch t) { index = t.Index; pos = t.Position; pressed = t.Pressed; return true; }
			if (e is InputEventScreenDrag d) { index = d.Index; pos = d.Position; pressed = true; isMotion = true; return true; }
		}
		else
		{
			if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left) { pos = mb.Position; pressed = mb.Pressed; return true; }
			if (e is InputEventMouseMotion mm) { pos = mm.Position; pressed = (mm.ButtonMask & MouseButtonMask.Left) != 0; isMotion = true; return true; }
		}
		return false;
	}

	/// <summary>
	/// Geräte-Eingabe: ein abgebrochenes Loslassen (Android ACTION_CANCEL — das System hat die Geste übernommen, z. B. eine Randgeste) ist kein Tipp:
	/// Tasten lösen dabei nicht aus, der Schubregler kehrt auf den Wert vor dem Druck zurück.
	/// </summary>
	public static bool Canceled(InputEvent e) => e is InputEventScreenTouch { Canceled: true } || e is InputEventMouseButton { Canceled: true };
}
