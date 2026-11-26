using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 174 (Welle C): neues Spiel (Händler), Sektorbehörde in Nebelmark, Kasse nur einen Tag Regierungsunterhalt → Vorwarnung als Alarm im
/// Flug und Zeile in „Meine Fraktion“ („reicht für … Tage“, WARNUNG); „Ruhen lassen“ mit zwei echten Tipps (KitConfirm) → Behörde ruht (kein Unterhalt,
/// kein Einfluss), „Wieder an“ mit einem echten Tipp → läuft wieder.
/// </summary>
public partial class SelfTest
{
	Vector2 _wcAt;
	StationId _wcSeat;

	bool WcTapOnce(int k, string label)
	{
		var hud = _m.HudRef;
		if (k is 0 or 2 or 4) { if (R45Button(hud.CcMyFaction, label) is { } b0) HqVisible(b0); return false; }
		if (k == 8)
		{
			var b = R45Button(hud.CcMyFaction, label);
			if (b is null || HqVisible(b) is not { } p) { Fail($"WC: „{label}“ fehlt oder nicht sichtbar ({hud.CcMyFaction.TestUpkeepLine})"); return false; }
			_wcAt = p;
			Tap(_wcAt, true);
		}
		if (k == 10) Tap(_wcAt, false);
		return false;
	}

	bool WelleCStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		var neb = u.Sectors.Values.First(s => s.Name.Contains("Nebelmark")).Id;
		switch (f)
		{
			case 1: _m.TestMenu("new:trader"); return false;
			case 6:
				Check(u.Rules.PlayerUpkeepPriority && u.Rules.PlayerSectorTaxes && u.Rules.PlayerStandingFloor < 0f && u.Rules.PlayerSeatContested, "WC: Welle-C-Regeln in der Standardwelt an");
				u.Player.Treasury.Credits = 2_000_000f;
				_m.TestCloseCommand();
				if (_m.TestDocked) _m.TestUndock();
				_m.TestEnterSector(neb);
				return false;
			case 12:
			{
				foreach (var c in new[] { new Vec2(0, 0), new(200, 200), new(-200, 200), new(200, -200), new(-200, -200), new(400, 0), new(-400, 0), new(0, 400), new(0, -400) })
				{
					_m.TestTeleportTo(c);
					if (PlayerSeats.FoundIssue(u, neb) is null) break;
				}
				Check(PlayerSeats.FoundIssue(u, neb) is null, $"WC: Behörde in Nebelmark möglich ({PlayerSeats.FoundIssue(u, neb)})");
				_wcSeat = PlayerSeats.Found(u, neb).Id;
				// Kasse deckt nur einen Tag Regierungsunterhalt → Vorwarnung
				u.Player.Treasury.Credits = PlayerUpkeepOrder.DailyUpkeep(u) * 1f;
				Check(PlayerUpkeepOrder.Warn(u), "WC: Vorwarnung fällig (unter drei Tagen)");
				return false;
			}
			case 90:
				Check(_m.TestUpkeepWarning.Length > 0 && hud.TestAlert.Contains("Regierungsunterhalt"), $"WC: Alarm im Flug zeigt die Vorwarnung („{hud.TestAlert}“)");
				_m.TestOpenCcArea(Ui.Screens.CommandAreas.MyFaction);
				hud.Screens.Current?.Refresh();
				return false;
			case 94:
				Check(hud.CcMyFaction.TestUpkeepLine.Contains("Tage") && hud.CcMyFaction.TestUpkeepLine.Contains("WARNUNG"), $"WC: Zeile in „Meine Fraktion“ mit Reichweite und Warnung ({hud.CcMyFaction.TestUpkeepLine})");
				return false;
			case >= 96 and <= 112: return TapTwice(f, 96, "Ruhen lassen", ref _wcAt);
			case 116:
			{
				hud.Screens.Current?.Refresh();
				var st = u.Stations[_wcSeat];
				Check(st.Dormant && PlayerSeats.Seats(u).Count == 1, $"WC: zwei Tipps lassen die Behörde ruhen ({hud.CcMyFaction.TestPolicyMessage})");
				Check(!u.Sectors[neb].Influence.ContainsKey(u.PlayerFaction!.Value) || u.Sectors[neb].Influence[u.PlayerFaction.Value] <= 0f, "WC: ruhende Behörde gibt keinen Einfluss");
				Check(hud.CcMyFaction.TestUpkeepLine.Contains("ruhend"), $"WC: Zeile nennt die ruhende Behörde ({hud.CcMyFaction.TestUpkeepLine})");
				return false;
			}
			case >= 118 and <= 128: return WcTapOnce(f - 118, "Wieder an");
			case 132:
			{
				hud.Screens.Current?.Refresh();
				var st = u.Stations[_wcSeat];
				Check(!st.Dormant, $"WC: ein Tipp schaltet die Behörde wieder an ({hud.CcMyFaction.TestPolicyMessage})");
				_m.TestCloseCommand();
				return true;
			}
		}
		return false;
	}
}
