using System.Collections.Generic;
using System.Linq;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceAdventure.Ui.Kit;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 132 (Spielersperre): neues Spiel (Händler); drei Angriffe bei der Union → Hinweis „Fahndung … · Beziehung −0,10“, Beziehung um den
/// Tagesdeckel gesunken; Fraktionen → Union zeigt „Ansehen −0,30 durch Angriff …“ in der Bündniszeile (Text passt, kein „…“).
/// </summary>
public partial class SelfTest
{
	FactionId _ssUnion;
	float _ssBefore;

	bool SpielersperreStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
			{
				_ssUnion = u.NpcFactionIds()[0];
				_ssBefore = Relations.Effective(u, _ssUnion, u.PlayerActor);
				Check(u.Rules.CrimeStandingLoss.GetValueOrDefault("assault") > 0f, "Spielersperre: Regel in der Standardwelt an");
				for (int i = 0; i < 3; i++) Wanted.Report(u, u.PlayerActor, _ssUnion, "assault");
				return false;
			}
			case 8:
			{
				float now = Relations.Effective(u, _ssUnion, u.PlayerActor);
				Check(System.MathF.Abs(_ssBefore - u.Rules.CrimeStandingCapPerDay - now) < 0.001f, $"Spielersperre: Beziehung {_ssBefore:0.00} → {now:0.00} (Tagesdeckel)");
				Check(hud.TestToastText.Contains("Beziehung −"), $"Spielersperre: Hinweis nennt den Ansehensverlust ({hud.TestToastText})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Factions);
				hud.CcFactions.TestSelect(_ssUnion);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 10:
			{
				var line = hud.CcFactions.TestCrimeStanding;
				Check(line.StartsWith("Ansehen −0,30 durch Angriff", System.StringComparison.Ordinal), $"Spielersperre: Fraktionen-Detail nennt den Grund ({line})");
				var labels = Labels(hud.CcFactions).Where(l => l.Text == line && l.IsVisibleInTree()).ToList();
				Check(labels.Count == 1, $"Spielersperre: Ansehenszeile sichtbar und ungekürzt ({labels.Count})");
				DrawnText.Enabled = true;
				if (hud.Screens.Current is Godot.Control sc) { sc.QueueRedraw(); foreach (var n in UiCheck.Visible(sc)) if (n is Godot.Control cc) cc.QueueRedraw(); }
				return false;
			}
			case 12:
			{
				if (hud.Screens.Current is Godot.Control sc)
				{
					var scope = UiCheck.Visible(sc).ToList();
					var inter = scope.Where(c => UiCheck.Interactive(c) && c.Size.X > 1f && c.Size.Y > 1f && c.MouseFilter != Godot.Control.MouseFilterEnum.Ignore).ToList();
					var notes = new System.Collections.Generic.List<string>();
					var tc = UiCheck.TextChecks(scope, inter, notes);
					Check(tc.TextOverlaps == 0 && tc.Clipped == 0, $"Spielersperre: Fraktionen-Detail Text über Text/Taste {tc.TextOverlaps}, beschnitten {tc.Clipped} [{string.Join("; ", notes.Take(4))}]");
				}
				DrawnText.Enabled = false;
				_m.TestCloseCommand();
				return true;
			}
		}
		return false;
	}
}
