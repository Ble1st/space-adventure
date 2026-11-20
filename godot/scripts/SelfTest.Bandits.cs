using System.Linq;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 153 (Ausbau Punkt 17, docs/BANDITEN.md): neues Spiel (Händler); Fraktionen → etablierte Banditenfraktion zeigt die Banditenzeile
/// (Sitz, Werke, Zoll, Kasse) ungekürzt; Andocken am Sitz (Freistatt) → Stations-Info hat die Zeile „Banditen“; Text-über-Text/beschnitten 0.
/// </summary>
public partial class SelfTest
{
	FactionId _bdFaction;

	bool BanditStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				Check(u.Rules.BanditFactions, "Banditen: Regel in der Standardwelt an");
				var fid = u.Factions.Keys.FirstOrDefault(x => Bandits.IsEstablished(u, x));
				Check(fid.Value != 0, "Banditen: etablierte Banditenfraktion vorhanden");
				_bdFaction = fid;
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Factions);
				hud.CcFactions.TestSelect(fid);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 8:
			{
				var line = hud.CcFactions.TestBandits;
				Check(line.StartsWith("Sitz ", System.StringComparison.Ordinal) && line.Contains("Zoll"), $"Banditen: Fraktionen-Detail zeigt die Banditenzeile ({line})");
				DrawnText.Enabled = true;
				if (hud.Screens.Current is Godot.Control sc) { sc.QueueRedraw(); foreach (var n in UiCheck.Visible(sc)) if (n is Godot.Control cc) cc.QueueRedraw(); }
				return false;
			}
			case 10:
			{
				CheckTexts(hud, "Banditen: Fraktionen-Detail");
				DrawnText.Enabled = false;
				_m.TestCloseCommand();
				var seat = Bandits.Seat(u, new ActorId(_bdFaction.Value));
				Check(seat is not null, "Banditen: Sitz vorhanden");
				if (seat is not null) _m.TestDockAt(seat.Id);
				return false;
			}
			case 20: _m.TestOpenDockArea(Ui.Screens.DockAreas.Info); return false;
			case 24:
			{
				var info = (Ui.Screens.StationInfoScreen)hud.Screens.Get("dock.info");
				Check(info.TestBandits.StartsWith("Sitz ", System.StringComparison.Ordinal), $"Banditen: Stations-Info am Sitz zeigt die Banditenzeile ({info.TestBandits})");
				var rows = Labels(info).Where(l => l.Text == info.TestBandits && l.IsVisibleInTree()).ToList();
				Check(rows.Count == 1, $"Banditen: Zeile sichtbar ({rows.Count})");
				CheckTexts(hud, "Banditen: Stations-Info");
				_m.TestUndock();
				return true;
			}
		}
		return false;
	}

	void CheckTexts(Ui.Hud hud, string what)
	{
		if (hud.Screens.Current is not Godot.Control sc) return;
		var scope = UiCheck.Visible(sc).ToList();
		var inter = scope.Where(c => UiCheck.Interactive(c) && c.Size.X > 1f && c.Size.Y > 1f && c.MouseFilter != Godot.Control.MouseFilterEnum.Ignore).ToList();
		var notes = new System.Collections.Generic.List<string>();
		var tc = UiCheck.TextChecks(scope, inter, notes);
		Check(tc.TextOverlaps == 0 && tc.Clipped == 0, $"{what}: Text über Text/Taste {tc.TextOverlaps}, beschnitten {tc.Clipped} [{string.Join("; ", notes.Take(4))}]");
	}
}
