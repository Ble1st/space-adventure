using SpaceSim.Core.Events;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Welle E: Meldung, wenn ein Großkampfschiff an einer Werft im Spielersektor fertig wird (Text aus dem Core, <see cref="CapitalShipCompleted.Text"/>);
/// ein neuer Auftrag an einer Werft im Spielersektor ist eine kleine Info.
/// </summary>
public partial class Main
{
	/// <summary>Selbsttest: zuletzt gezeigte Fertigmeldung.</summary>
	public string TestCapitalToast { get; private set; } = "";

	bool CapitalEvent(SimEvent e)
	{
		var u = _session.U;
		switch (e)
		{
			case CapitalShipCompleted c:
				if (u.Stations.TryGetValue(c.Yard, out var y) && y.Sector == u.PlayerSector)
				{
					TestCapitalToast = "Großkampfschiff: " + c.Text;
					_hud.Toast(TestCapitalToast, 5f);
				}
				return true;
			case CapitalOrderPlaced p:
				if (u.Stations.TryGetValue(p.Yard, out var py) && py.Sector == u.PlayerSector)
					_hud.Info($"Bauauftrag an {py.Name}: {CapitalOrders.InfoLine(u, py)?.Replace("Bauauftrag: ", "") ?? p.Model}");
				return true;
		}
		return false;
	}
}
