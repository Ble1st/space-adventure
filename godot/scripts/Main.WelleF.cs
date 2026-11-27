using System.Collections.Generic;
using SpaceSim.Core.Events;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>Welle F: Meldungen — Spielerschiff wartet (Treibstoff/Notfall nicht bezahlbar), Kriegskasse unter der Rücklage.</summary>
public partial class Main
{
	/// <summary>Letzter Tick einer Wartemeldung je Schiff (Ansicht, nicht gespeichert): höchstens eine Meldung je Spieltag und Schiff.</summary>
	readonly Dictionary<int, long> _strandedShown = new();

	/// <summary>Selbsttest: zuletzt gezeigte Wartemeldung.</summary>
	public string TestStrandedText { get; private set; } = "";

	bool WelleFEvent(SimEvent e)
	{
		var u = _session.U;
		switch (e)
		{
			case FleetShipStranded fs when u.Ships.TryGetValue(fs.Ship, out var ship):
				if (_strandedShown.TryGetValue(fs.Ship.Value, out var last) && u.Tick >= last && u.Tick - last < u.Rules.DayTicks) return true;   // neues Spiel: Tick kleiner, Meldung wieder erlaubt
				_strandedShown[fs.Ship.Value] = u.Tick;
				TestStrandedText = $"{SpaceSim.Core.Model.ShipNames.Display(u, ship)}: {FuelPriority.Text(u, fs)}";
				_hud.Toast(TestStrandedText, 5f);   // eigenes Schiff: wichtig (MessageRules)
				return true;
			case WarBrokeNoticed wb when u.PlayerFaction is { } me && (wb.Side == me || wb.Enemy == me):
				_hud.Info(wb.Side == me
					? $"Deine Kasse ist im Minus — hält das an, endet der Krieg mit {u.ActorName(wb.Enemy)} wegen leerer Kasse."
					: $"{u.ActorName(wb.Side)}: Kriegskasse unter der Rücklage — hält es an, bittet sie um Frieden.");
				return true;
		}
		return false;
	}
}
