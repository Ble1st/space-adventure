using System.Linq;
using Godot;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>Bilder des Tarnfelds (99a ohne, 99b mit Tarnung, Nomade mit Tarnfeld Mk1, nah von schräg hinten); nur mit <c>--ui-shots-cloak</c>.</summary>
public partial class UiShots
{
	const int CloakShotsStart = 600_101;

	void CloakShots()
	{
		var u = _m.Session.U;
		switch (_stage)
		{
			case 600_101: _m.TestMenu("new:trader"); _stage = 600_102; _wait = 240; break;
			case 600_102:
			{
				_m.TestUndock();
				u.Rules.PlayerRaids = false;
				var me = u.Ships[u.PlayerShip];
				int util = me.Slots.FindIndex(s => s.Type == "utility");
				if (util >= 0) me.Fit[util] = "cloak_s";
				me.RecomputeStats(u);
				_stage = 600_103; _wait = 120; break;
			}
			case 600_103: _m.TestSetCam(150f, 32f, 16f); _stage = 600_104; _wait = 30; break;
			case 600_104: Capture("99a-ohne-tarnung.png"); _stage = 600_105; _wait = 5; break;
			case 600_105:
			{
				var issue = Cloak.TurnOn(u, u.Ships[u.PlayerShip]);
				GD.Print($"[ui-shots] Tarnung: {(issue ?? "an")}");
				_stage = 600_106; _wait = 30; break;
			}
			case 600_106: _m.TestSetCam(150f, 32f, 16f); _stage = 600_107; _wait = 10; break;
			case 600_107: GD.Print($"[ui-shots] Durchsicht {_m.TestPlayerTransparency:0.00} cloaked={u.Ships[u.PlayerShip].Cloaked}"); Capture("99b-mit-tarnung.png"); _stage = 999_999; _wait = 5; break;
		}
	}
}
