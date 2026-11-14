using System.Linq;
using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 101 (Runde 22, Handy-Test h3: beim zweiten Kaperer war „Dauerfeuer“ aus, ohne dass getippt wurde): Gefechtstest über den echten
/// Ansichtspfad — „Dauerfeuer“ per Tipp an, erster Kaperer fällt (Kopfgeld), der zweite Überfall wird angekündigt und beginnt; Dauerfeuer bleibt an
/// (Zustand und Taste), der zweite Kaperer wird Ziel und beschossen.
/// </summary>
public partial class SelfTest
{
	ShipId _r22Lead, _r22Second;
	int _r22Phase, _r22Since, _r22ShotsAtSecond;
	string _r22Log = "";
	PendingEncounter? _r22Postponed;

	bool Round22Stage(int f)
	{
		var u = _m.Session.U;
		var hud = _m.HudRef.Flight;
		if (f == 1) { _m.TestMenu("new:combat"); return false; }
		if (f < 6) return false;
		if (f == 6)
		{
			_r22Lead = u.PendingEncounters.Where(e => e.Kind == EncounterKind.Raid && e.Target == u.PlayerShip).OrderBy(e => e.DueTick).First().Source;
			_r22Phase = 0; _r22ShotsAtSecond = 0; _r22Second = default; _r22Log = "";
			_m.TestAimAssist = true;
			u.Events.Raised += e => { if (e is ShotFired sf && sf.Shooter == _m.Session.U.PlayerShip && _r22Second.Value != 0 && sf.Target == _r22Second) _r22ShotsAtSecond++; };
			return false;
		}
		if (!u.Ships.TryGetValue(u.PlayerShip, out var me) || me.ModelKey != "falcon") { Fail($"Dauerfeuer über zwei Überfälle: Falke verloren ({_r22Log})"); return true; }
		switch (_r22Phase)
		{
			case 0:   // erster Überfall beginnt → „Dauerfeuer“ per Tipp
				if (u.Fights.IsEngaged(me.Id)) { _r22Phase = 1; _r22Since = f; }
				break;
			case 1:
				if (f == _r22Since + 30) PhoneTap("Dauerfeuer", true);
				if (f == _r22Since + 32)
				{
					PhoneTap("Dauerfeuer", false);
				}
				if (f == _r22Since + 40) Check(_m.TestFight.AutoFire, "Tipp „Dauerfeuer“: an");
				if (f > _r22Since + 40 && !u.Ships.ContainsKey(_r22Lead)) { _r22Phase = 2; _r22Since = f; _r22Log += $"erster weg f{f}; "; }
				break;
			case 2:   // zwischen den Überfällen: Dauerfeuer bleibt an
				if (!_m.TestFight.AutoFire) _r22Log += $"AUS f{f} (Ziel {_m.TestFight.Target.Value}); ";
				var pend = u.PendingEncounters.FirstOrDefault(e => e.Kind == EncounterKind.Raid && e.Target == me.Id);
				if (pend is not null && _r22Second.Value == 0) { _r22Second = pend.Source; _r22Log += $"Ankündigung #{pend.Source.Value} f{f}; "; }
				if (_r22Second.Value == 0 && u.Fights.Of(me.Id) is { } fe && fe.Participants.FirstOrDefault(p => p.Active && p.Ship != me.Id && u.Ships.TryGetValue(p.Ship, out var rs) && SpaceSim.Core.Simulation.ThreatMap.IsRaider(rs)) is { } rp)
				{ _r22Second = rp.Ship; _r22Log += $"zweiter im Gefecht #{rp.Ship.Value} f{f}; "; }
				if (_r22Second.Value != 0 && u.Fights.Of(me.Id) is { } fight && fight.Of(_r22Second) is { Active: true }) { _r22Phase = 3; _r22Since = f; }
				break;
			case 3:
				if (f == _r22Since + 240)
				{
					_m.SyncHudForTest();
					var btn = FindKit(hud, "Dauerfeuer");
					Check(_m.TestFight.AutoFire && btn is { On: true } && !hud.TestAutoFireHint,
						$"zweiter Überfall: Dauerfeuer noch an (Zustand {_m.TestFight.AutoFire}, Taste {btn?.On}, Pulsieren {hud.TestAutoFireHint}; {_r22Log})");
					Check(_m.TestFight.Target == _r22Second, $"zweiter Kaperer ist Ziel ({_m.TestFight.Target.Value} / {_r22Second.Value}; {_r22Log})");
					Check(_r22ShotsAtSecond > 0, $"zweiter Kaperer wird beschossen ({_r22ShotsAtSecond} Schüsse)");
					Check(_m.LastCombatMessage is null || !_m.LastCombatMessage.Contains("„Dauerfeuer“ an?"), $"Meldung fragt nicht nach Dauerfeuer, wenn es läuft ({_m.LastCombatMessage})");
					// verschobener Überfall ohne neue Warnung (Handy-Test h3: „Kein Ziel“ neben dem Kaperer): der ankündigende Kaperer wird Ziel
					var other = u.ShipsById().FirstOrDefault(s => SpaceSim.Core.Simulation.ThreatMap.IsRaider(s) && s.Sector == u.PlayerSector && s.Id != _r22Second) ?? (u.Ships.TryGetValue(_r22Second, out var sec2) ? sec2 : null);
					if (other is not null)
					{
						_m.TestFight.AutoFire = false;
						_m.TestSetShipTarget(default);
						_r22Postponed = new PendingEncounter { Kind = EncounterKind.Raid, Source = other.Id, Target = me.Id, Sector = me.Sector, DueTick = u.Tick + 999 };
						u.PendingEncounters.Add(_r22Postponed);
						_r22Phase = 4; _r22Since = f;
					}
					else return true;
				}
				break;
			case 4:
				if (f == _r22Since + 3)
				{
					_m.SyncHudForTest();
					Check(_m.TestFight.Target == _r22Postponed!.Source, $"verschobener Überfall ohne Warnung: Kaperer wird Ziel ({_m.TestFight.Target.Value} / {_r22Postponed.Source.Value})");
					u.PendingEncounters.Remove(_r22Postponed);
					return true;
				}
				break;
		}
		if (f > 60 * 200) { Fail($"Dauerfeuer über zwei Überfälle: kein zweiter Überfall in 200 s ({_r22Phase}; {_r22Log})"); return true; }
		return false;
	}
}
