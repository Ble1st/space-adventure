using Godot;

namespace SpaceAdventure.Ui;

/// <summary>
/// Vereinheitlicht Maus (Desktop) und Touch (Mobil), damit mehrere Finger gleichzeitig funktionieren
/// (Stick halten und Knopf tippen). Auf Touchgeräten werden nur Touch-, sonst nur Mausereignisse ausgewertet.
/// </summary>
public static class Pointer
{
	public static bool TouchMode => DisplayServer.IsTouchscreenAvailable();

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
}
