using System;
using System.Collections.Generic;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Runde 26/27: Ansichtszustand der Abnehmer-Auskunft im Handel — der genannte Abnehmer der offenen Zeile (gehalten bis Schließen/Mengenänderung) und die Flugzeit des
/// Spielers zu einem Abnehmer (<see cref="PlayerTrip"/>, je Liegeplatz einmal gerechnet). Nicht gespeichert.
/// </summary>
public sealed class HintState
{
	// Runde 27: der genannte Abnehmer bleibt, bis die Zeile geschlossen (Release) oder die Menge geändert wird
	(int Station, int Good, float Qty, StationId Buyer)? _held;
	readonly Dictionary<int, float> _secs = new();
	(int Sector, int X, int Y) _from = (-1, 0, 0);

	public void Clear() { _held = null; _secs.Clear(); _from = (-1, 0, 0); }
	public void Release() => _held = null;

	public StationId? HeldFor(StationId here, GoodId good, float qty) =>
		_held is { } h && h.Station == here.Value && h.Good == good.Value && MathF.Abs(h.Qty - qty) < 0.001f ? h.Buyer : null;
	public void Hold(StationId here, GoodId good, float qty, StationId buyer) => _held = (here.Value, good.Value, qty, buyer);

	/// <summary>Flugzeit per Autopilot zum Andockpunkt des Abnehmers (Sekunden), zwischengespeichert für den aktuellen Standort.</summary>
	public float Seconds(Universe u, Ship ship, Station to)
	{
		var from = (ship.Sector.Value, (int)ship.Position.X, (int)ship.Position.Y);
		if (from != _from) { _secs.Clear(); _from = from; }
		if (!_secs.TryGetValue(to.Id.Value, out var s)) _secs[to.Id.Value] = s = PlayerTrip.SecondsTo(u, ship.Sector, ship.Position, to, ship);
		return s;
	}
}
