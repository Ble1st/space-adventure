using System;
using Godot;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Mehrpunkt-Route: langes Drücken auf einer Karte (≥ <see cref="Seconds"/>, Finger wandert höchstens <see cref="Slop"/> Bildpunkte). Die Zeit läuft über die
/// Bilddauer (<see cref="Process"/>), nicht über die Uhr — der Selbsttest mit festen 60 Bildern je Sekunde misst dieselbe Dauer. Der Zustand lebt im
/// Bildschirm, nicht in der Karte (die Karte wird neu gebaut); nach dem Auslösen wird das Loslassen geschluckt (kein zusätzlicher Tipp).
/// </summary>
public sealed class LongPress
{
	public const float Seconds = 0.5f, Slop = 14f;
	Vector2 _at;
	float _held;
	bool _down, _fired;
	/// <summary>Finger liegt (noch) auf der Karte — der Bildschirm baut dann nicht neu.</summary>
	public bool Down => _down;
	public event Action<Vector2>? Fired;

	/// <summary>Zeigerereignis der Karte (lokale Lage); true = Ereignis schlucken (Loslassen nach dem Auslösen).</summary>
	public bool Input(bool pressed, bool motion, Vector2 pos)
	{
		if (motion) { if (_down && !_fired && pos.DistanceTo(_at) > Slop) _down = false; return _fired && _down; }
		if (pressed) { _down = true; _fired = false; _at = pos; _held = 0f; return false; }
		bool swallow = _fired && _down;
		_down = false; _fired = false;
		return swallow;
	}

	public void Process(float dt)
	{
		if (!_down || _fired) return;
		if (KitScroll.Swiping) { _down = false; return; }
		_held += dt;
		if (_held >= Seconds) { _fired = true; Fired?.Invoke(_at); }
	}

	public void Reset() { _down = false; _fired = false; }
}
