using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>Ausbau Punkt 1/1b: Meldungen der Flottenbefehle (Angriff eines eigenen Schiffs, Verfolgung beendet, Sammelpunkt erreicht).</summary>
public partial class Main
{
	bool FleetOpsEvent(SimEvent e)
	{
		var u = _session.U;
		switch (e)
		{
			case FleetAttackStarted fa:
				if (fa.Crime) _hud.Toast(fa.Text, 4f); else _hud.Info(fa.Text);
				return true;
			case RallyFormed rf:
				_hud.Toast(rf.Text, 4f);
				return true;
			case PursuitEnded pe when u.Ships.TryGetValue(pe.Hunter, out var ps) && ps.IsPlayer:
				_hud.Info(pe.How switch
				{
					"expired" => $"Schiff #{ps.Id.Value}: Verfolgung abgebrochen (Frist).",
					"locked" => $"Schiff #{ps.Id.Value}: Verfolgung abgebrochen — Sektor gesperrt.",
					"lost" => $"Schiff #{ps.Id.Value}: Ziel ist weg.",
					_ => $"Schiff #{ps.Id.Value}: Verfolgung beendet.",
				});
				return true;
		}
		return false;
	}
}
