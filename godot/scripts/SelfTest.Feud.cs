using System.Linq;
using Godot;
using SpaceSim.Core.Comms;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 152 (Ausbau Punkt 6, docs/POLITIK.md „Fehden, Vermittlung, Regierungskrise“): neues Spiel (Händler), Firmensitz in Nebelmark (Fehden frei),
/// Comms → Diplomatie → Gilde: „Fehde erklären“ mit zwei echten Tipps → Fehde läuft, Detailzeile; „Fehde beenden“ mit einem Tipp → vorbei. Dann eine
/// gealterte Fehde Union–Montanbund: die Union zeigt „Vermitteln: Fehde mit …“, ein echter Tipp vermittelt (Gebühr in der Kasse, Fehde vorbei);
/// Fraktionen-Detail zeigt die Fehdezeile.
/// </summary>
public partial class SelfTest
{
	Vector2 _fdAt;
	float _fdWallet;
	FactionId _fdGuild, _fdUnion, _fdMont;

	Ui.Kit.KitButton? FdButton(Ui.Hud hud, string prefix) =>
		Buttons(hud.CcComms).FirstOrDefault(b => b.Text.StartsWith(prefix, System.StringComparison.Ordinal) && b.IsVisibleInTree() && b.State != Ui.Kit.KitButtonState.Disabled);

	bool FdTap(Ui.Hud hud, int k, string prefix, string what)
	{
		// k: 0..2 vorrollen, 4 drücken, 6 loslassen
		if (k is 0 or 2) { if (FdButton(hud, prefix) is { } b0) R45Visible(hud, b0); return false; }
		if (k == 4)
		{
			var b = FdButton(hud, prefix);
			if (b is null || R45Visible(hud, b) is not { } at) { Fail($"FD: „{prefix}“ fehlt oder nicht sichtbar ({what}; Taste {(b is null ? "fehlt" : b.GetGlobalRect().ToString())}, Ansicht {hud.CcComms.TestDetailScroll?.GetGlobalRect()}, Tasten {string.Join(",", Buttons(hud.CcComms).Where(x => x.IsVisibleInTree()).Select(x => x.Text))})"); return false; }
			_fdAt = at;
			Tap(_fdAt, true);
		}
		if (k == 6) Tap(_fdAt, false);
		return false;
	}

	bool FeudStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var neb = u.Sectors.Values.First(s => s.Name.Contains("Nebelmark")).Id;
		var cands = new[] { new Vec2(0, 0), new(200, 200), new(-200, 200), new(200, -200), new(-200, -200), new(400, 0), new(-400, 0), new(0, 400), new(0, -400), new(300, 300), new(-300, -300) };
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
				Check(u.Rules.FeudsActive && u.Rules.PlayerFeuds && u.Rules.Mediation && u.Rules.PlayerMediation && u.Rules.GovernmentCrisis, "FD: Fehden, Vermittlung und Krise in der Standardwelt an");
				u.Player.Treasury.Credits = 400_000f;
				_fdUnion = u.NpcFactionIds()[0]; _fdGuild = u.NpcFactionIds()[1]; _fdMont = u.NpcFactionIds()[2];
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				_m.TestEnterSector(neb);
				return false;
			case 12:
			{
				foreach (var c in cands) { _m.TestTeleportTo(c); if (PlayerSeatsOffices.SeatIssue(u, neb) is null) break; }
				var res = CommsActions.Default().Execute(u, $"firmseat:found:{neb.Value}");
				Check(res.Success && PlayerSeatsOffices.FeudUnlocked(u), $"FD: Firmensitz gegründet, Fehden frei ({res.Message})");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Comms);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 14: PressButton(hud.CcComms, "Diplomatie", true); return false;
			case 16: PressButton(hud.CcComms, "Diplomatie", false); hud.Screens.Current?.Refresh(); return false;
			case 18: hud.CcComms.TestSelectFaction(_fdGuild); hud.Screens.Current?.Refresh(); return false;
			case >= 22 and <= 28: return FdTap(hud, f - 22, "Fehde erklären", "erster Tipp");
			case 32:
				Check(u.Feuds.Count == 0, "FD: erster Tipp auf „Fehde erklären“ führt noch nicht aus (zwei Tipps)");
				return false;
			case >= 34 and <= 40: return FdTap(hud, f - 34, "Wirklich?", "Bestätigung");
			case 44:
			{
				hud.Screens.Current?.Refresh();
				var me = u.PlayerFaction!.Value;
				var fd = Feuds.Between(u, me, _fdGuild);
				Check(fd is { ByPlayer: true }, $"FD: zwei Tipps erklären die Fehde ({hud.CcComms.TestMessage})");
				Check(hud.CcComms.TestFeudRow.StartsWith("Fehde mit dir"), $"FD: Detailzeile „Fehde/Krise“ ({hud.CcComms.TestFeudRow})");
				return false;
			}
			case >= 46 and <= 52: return FdTap(hud, f - 46, "Fehde beenden", "Fehde beenden");
			case 56:
			{
				hud.Screens.Current?.Refresh();
				Check(u.Feuds.Count == 0, $"FD: ein Tipp beendet die Fehde ({hud.CcComms.TestMessage})");
				// gealterte KI-Fehde Union–Montanbund; beide trauen dem Spieler
				var me = u.PlayerFaction!.Value;
				foreach (var x in new[] { _fdUnion, _fdMont }) { Relations.Set(u, x, me, 0.5f); Relations.Set(u, me, x, 0.5f); u.Actors[x].Treasury.Credits = 300_000f; }
				u.Rules.Mediation = false;   // die KI vermittelt nicht vor dem Spieler
				var fdm = Feuds.Start(u, _fdUnion, _fdMont);
				fdm.Since -= u.Rules.MediationMinTicks;
				_fdWallet = u.Player.Treasury.Credits;
				hud.CcComms.TestSelectFaction(_fdUnion);
				hud.Screens.Current?.Refresh();
				return false;
			}
			case 58:
				Check(hud.CcComms.TestMediateKeys.Count == 1, $"FD: Vermittlungsangebot sichtbar ({string.Join(",", hud.CcComms.TestMediateKeys)})");
				Check(hud.CcComms.TestFeudRow.StartsWith("Fehde mit"), $"FD: Union zeigt die Fehde ({hud.CcComms.TestFeudRow})");
				return false;
			case >= 60 and <= 66: return FdTap(hud, f - 60, "Vermitteln:", "Vermitteln");
			case 70:
				hud.Screens.Current?.Refresh();
				Check(u.Feuds.Count == 0, $"FD: Tipp auf „Vermitteln“ beendet die Fehde ({hud.CcComms.TestMessage})");
				Check(u.Player.Treasury.Credits > _fdWallet + 100f, $"FD: Vermittlungsgebühr in der Kasse ({_fdWallet:0} → {u.Player.Treasury.Credits:0})");
				// Fraktionen-Detail: Fehdezeile einer laufenden Fehde
				u.FeudEnded.Clear();
				Feuds.Start(u, _fdUnion, _fdGuild);
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.Factions);
				hud.CcFactions.TestSelect(_fdGuild);
				hud.Screens.Current?.Refresh();
				return false;
			case 74:
				Check(hud.CcFactions.TestFeud.StartsWith("Fehde mit"), $"FD: Fraktionen-Detail zeigt die Fehde ({hud.CcFactions.TestFeud})");
				u.Feuds.Clear();
				_m.TestCloseCommand();
				return true;
		}
		return false;
	}
}
