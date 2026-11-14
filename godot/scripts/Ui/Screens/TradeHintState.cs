using System.Collections.Generic;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Runde 26: Ansichtszustand der Abnehmer-Auskunft im Handel — der zuletzt genannte Abnehmer je (Station, Ware) für die Hysterese und die Flugzeit des
/// Spielers zu einem Abnehmer (<see cref="PlayerTrip"/>, je Liegeplatz einmal gerechnet). Nicht gespeichert.
/// </summary>
public sealed class HintState
{
	/// <summary>Der gehaltene Abnehmer bleibt, solange sein vorsichtiger Erlös mindestens diesen Anteil des besten erreicht.</summary>
	public const float Keep = 0.9f;
	readonly Dictionary<(int, int), StationId> _held = new();
	readonly Dictionary<int, float> _secs = new();
	(int Sector, int X, int Y) _from = (-1, 0, 0);

	public void Clear() { _held.Clear(); _secs.Clear(); _from = (-1, 0, 0); }

	public StationId? HeldFor(StationId here, GoodId good) => _held.TryGetValue((here.Value, good.Value), out var s) ? s : null;
	public void Hold(StationId here, GoodId good, StationId buyer) => _held[(here.Value, good.Value)] = buyer;

	/// <summary>Flugzeit per Autopilot zum Andockpunkt des Abnehmers (Sekunden), zwischengespeichert für den aktuellen Standort.</summary>
	public float Seconds(Universe u, Ship ship, Station to)
	{
		var from = (ship.Sector.Value, (int)ship.Position.X, (int)ship.Position.Y);
		if (from != _from) { _secs.Clear(); _from = from; }
		if (!_secs.TryGetValue(to.Id.Value, out var s)) _secs[to.Id.Value] = s = PlayerTrip.SecondsTo(u, ship.Sector, ship.Position, to, ship);
		return s;
	}
}
