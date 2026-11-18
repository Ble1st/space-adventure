using System.Linq;
using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 140 (Kapitulation, docs/KAMPF.md „Kapitulation“): Kriegstest (Bollwerk vor dem Sektor-HQ Beta), Station antippen, „Station angreifen“
/// über zwei echte Tipps (KitConfirm); Hinweis „Kapitulation möglich ab 25 % Hülle“; die Hülle steht knapp über der Schwelle und die Chance ist erzwungen
/// (Regeldaten, kein Haken im Code) → der nächste Treffer der eigenen Waffen lässt die Station kapitulieren: Besitzer = Spieler, Angriff aus, Meldung
/// „kapituliert … gehört jetzt dir“, Andocken an der eigenen Station möglich (Dockmenü).
/// </summary>
public partial class SelfTest
{
	StationId _capHq;
	Vector2 _capAt;
	int _capWaitFrom;

	bool CapitulationStage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef;
		switch (f)
		{
			case 1: _m.TestMenu("new:war"); return false;
			case 8:
			{
				Check(u.Rules.StationCapitulation, "Kapitulation: Regel in der Standardwelt an");
				var hq = u.StationsById().Where(s => s.Sector == u.PlayerSector && s.TypeKey == "sector_hq").OrderBy(s => SpaceSim.Core.Model.Vec2.Distance(s.Position, u.Ships[u.PlayerShip].Position)).FirstOrDefault();
				if (hq is null) { Fail("Kapitulation: kein Sektor-HQ im Startsektor des Kriegstests"); return false; }
				_capHq = hq.Id;
				// Chance erzwingen (Regeldaten); Hülle knapp über der ersten Schwelle, Schild leer
				u.Rules.CapitulationChances = new() { 1f, 1f };
				u.Rules.CapitulationChanceMin = 1f;
				u.Rules.CapitulationChanceMax = 1f;
				float max = StationCombat.MaxHull(u, hq);
				hq.Condition = new StationCondition { HullMissing = max * (1f - 0.26f), ShieldMissing = StationCombat.MaxShield(u, hq) };
				Check(_m.TestCapitulationHint(hq.Id).Contains("Kapitulation möglich ab 25 %"), $"Kapitulation: Hinweis nennt die Schwelle ({_m.TestCapitulationHint(hq.Id)})");
				_m.TestSelectStation(hq.Id);
				_m.SyncHudForTest();
				return false;
			}
			case 10:
				Check(hud.Flight.TestStationAttackVisible, "Kapitulation: „Station angreifen“ sichtbar");
				_capAt = hud.Flight.TestStationAttackRect.GetCenter();
				Tap(_capAt, true); return false;
			case 11: Tap(_capAt, false); return false;
			case 40: Tap(_capAt, true); return false;   // zweiter Tipp nach der Entprellzeit (KitConfirm)
			case 41: Tap(_capAt, false); return false;
			case 44:
				Check(!hud.Flight.TestStationAttackVisible && _m.TestFight.AtStation, "Kapitulation: Angriff scharf (zwei Tipps)");
				_capWaitFrom = f;
				return false;
			default:
				if (f > 44 && _capWaitFrom > 0)
				{
					if (!u.Stations.TryGetValue(_capHq, out var hq)) { Fail("Kapitulation: Station verschwunden (zerstört statt kapituliert)"); return true; }
					if (hq.Owner != u.PlayerActor)
					{
						if (f - _capWaitFrom > 60 * 40) { Fail($"Kapitulation: keine Übergabe nach 40 s (Hülle {StationCombat.Hull(u, hq):0}/{StationCombat.MaxHull(u, hq):0}, Spielerschiff {u.PlayerShip.Value})"); return true; }
						return false;
					}
					_capWaitFrom = 0;
					_capAt = new Vector2(f, 0f);
					Check(hud.TestAlert.Contains("kapituliert") || _m.LastWarMessage.Contains("kapituliert"), $"Kapitulation: Meldung ({hud.TestAlert} / {_m.LastWarMessage})");
					Check(!_m.TestFight.AtStation, "Kapitulation: Angriff beendet");
					return false;
				}
				if (_capWaitFrom == 0 && _capAt.Y == 0f && _capAt.X > 0f && f == (int)_capAt.X + 30)
				{
					_m.TestDockAt(_capHq);
					return false;
				}
				if (_capWaitFrom == 0 && _capAt.X > 0f && f == (int)_capAt.X + 60)
				{
					Check(_m.TestDocked, "Kapitulation: an der eigenen (übernommenen) Station angedockt");
					Check(u.Stations[_capHq].Owner == u.PlayerActor, "Kapitulation: Station gehört dem Spieler");
					return true;
				}
				return false;
		}
	}
}
