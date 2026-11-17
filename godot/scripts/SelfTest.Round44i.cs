using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 126 (Runde 44i, Handytest u18/u19/u21): die drei längsten andockbaren Stationsnamen der Welt — Kontexttaste („Andocken: …“) und
/// Zielkarte zeigen den Namen ohne „…“ (Kurzform mit Kürzel bzw. zweizeilig), der Ziel-Hinweis hat höchstens zwei Zeilen. Fenster im Handyformat
/// (1560 × 720) mit UI-Faktor 1,15 wie „normal“ auf dem A15.
/// </summary>
public partial class SelfTest
{
	List<StationId> _r44iStations = new();
	int _r44iIdx;
	Vector2I _r44iSize;

	bool Round44iStage(int f)
	{
		var u = _m.Session.U;
		var root = GetTree().Root;
		if (f == 1) { _m.TestMenu("new:miner"); return false; }
		if (f == 4)
		{
			_r44iSize = root.Size;
			root.Size = new Vector2I(1560, 720);
			UiScale.TestForce = 1.15f; UiScale.Apply();
			_r44iStations = u.Stations.Values.Where(s => s.Dockable && s.Site is null)
				.OrderByDescending(s => ActorNames.Tagged(u, s.Owner, s.Name).Length).ThenBy(s => s.Id.Value).Take(3).Select(s => s.Id).ToList();
			Check(_r44iStations.Count == 3, $"R44i: drei lange Stationsnamen ({_r44iStations.Count})");
			return false;
		}
		if (f < 6) return false;
		int k = (f - 6) / 40, phase = (f - 6) % 40;
		if (k >= _r44iStations.Count)
		{
			UiScale.TestForce = null; UiScale.Apply();
			root.Size = _r44iSize;
			return true;
		}
		var id = _r44iStations[k];
		var st = u.Stations[id];
		if (phase == 0) { _m.TestEnterSector(st.Sector); return false; }
		if (phase == 4) { _m.TestTeleportNear(id); _m.TestSelectStation(id); return false; }
		if (phase == 10)
		{
			var fl = _m.HudRef.Flight;
			string full = ActorNames.Tagged(u, st.Owner, st.Name);
			string ctx = fl.TestContextLabel, name = fl.TestTargetName, toast = _m.HudRef.TestToastText;
			Check(ctx.Contains("Andock") && fl.TestContextOverflow.Length == 0 && !ctx.Contains('…'),
				$"R44i: Kontexttaste ohne „…“ ({full} → „{ctx}“, Überlauf „{fl.TestContextOverflow}“)");
			Check(!name.Contains('…') && fl.TestTargetNameFits && name.Length > 3,
				$"R44i: Zielkarte ohne „…“ ({full} → „{name}“)");
			Check(toast.StartsWith("Ziel:") && _m.HudRef.TestToastLines <= 2,
				$"R44i: Ziel-Hinweis höchstens zwei Zeilen ({_m.HudRef.TestToastLines}: „{toast}“)");
		}
		return false;
	}
}
