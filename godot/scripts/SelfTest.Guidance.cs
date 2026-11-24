using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 163 (Raketenlenkung und Gegenmaßnahmen): Falke mit Täuschkörperwerfer und Störsender; ein Donner überfällt ihn, eine Infrarotrakete fliegt an
/// → die Raketenwarnung (Lenkart, Restzeit, Richtung) ist sichtbar; ein echter Tipp auf „Täusch.“ stößt Täuschkörper aus → die Rakete wird abgelenkt (Ereignis,
/// Meldung); ein echter Tipp auf „Störer“ schaltet den Störsender an und wieder aus. Gruppentasten ohne abgeschnittenen Text.
/// </summary>
public partial class SelfTest
{
	Vector2 _glAt;
	ShipId _glBomber;
	List<SimEvent>? _glEvents;
	int _glFlareFrame = -1;

	bool GuidanceStage(int f)
	{
		var u = _m.Session.U;
		var fl = _m.HudRef.Flight;
		int Group(string label) => fl.TestGroupLabels.IndexOf(label);
		switch (f)
		{
			case 1: _m.TestMenu("new:fighter"); return false;
			case 8:
			{
				var me = u.Ships[u.PlayerShip];
				_m.TestUndock();
				u.Rules.PlayerRaids = false;   // kein zufälliger Überfall mitten im Test
				u.Rules.FlareChance = 1f;      // Ergebnis der Prüfung sicher (die Regel selbst prüfen die Core-Tests)
				Check(u.Rules.MissileGuidance, "RL: Raketenlenkung in der Standardwelt an");
				me.Slots.Add(new ClassSlot { Type = "utility", Size = SlotSize.S }); me.Fit.Add("flare_launcher_s");
				me.Slots.Add(new ClassSlot { Type = "utility", Size = SlotSize.S }); me.Fit.Add("jammer_s");
				me.RecomputeStats(u);
				me.Hull = me.MaxHull;
				// Donner der Piraten, ohne weitere Raketen (nur die eine, die der Test schickt)
				var pirate = u.Actors.Values.OrderBy(a => a.Id.Value).First(a => a.Kind == ActorKind.Outlaw);
				var m = u.ShipModels["bomber"];
				var yard = u.StationsById().First(s => !s.IsBuildSite);
				var bomber = ShipPurchase.Create(u, u.NewShipId(), "bomber", u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Pirate, Controller.Ai, pirate.Id, yard, 0f);
				u.AddShip(bomber);
				if (bomber.Sector != me.Sector) u.MoveShip(bomber, me.Sector);
				bomber.DockedAt = default;
				bomber.Position = me.Position + new Vec2(0f, -60f);
				bomber.Systems.MissilesMissing = bomber.Stats.MissileCapacity;
				_glBomber = bomber.Id;
				var e = Engage.StartRaid(u, bomber, me, u.Sectors[me.Sector]);
				var bp = e.Of(bomber.Id)!;
				_glEvents = new List<SimEvent>();
				u.Events.Raised += _glEvents.Add;
				e.Missiles.Add(new MissileFlight { Shooter = bomber.Id, Owner = bomber.Owner, Side = bp.Side, Module = "missile_launcher_s", Target = me.Id,
					Left = 80f, Total = 80f, Speed = 35f, From = bp.Pos });
				return false;
			}
			case 20:
			{
				var me = u.Ships[u.PlayerShip];
				Check(Countermeasures.Incoming(u, me).Count > 0, "RL: Rakete fliegt auf das Spielerschiff zu");
				Check(_m.LastMissileWarning.StartsWith("RAKETE") && _m.LastMissileWarning.Contains("Infrarot"), $"RL: Warnung mit Lenkart ({_m.LastMissileWarning})");
				Check(_m.LastMissileWarning.Contains(" s"), "RL: Warnung nennt die Restzeit");
				Check(_m.HudRef.TestAlert.StartsWith("RAKETE"), $"RL: Warnung als große Meldung sichtbar ({_m.HudRef.TestAlert})");
				Check(Group("Täusch.") >= 0 && Group("Störer") >= 0, $"RL: Gruppen „Täusch.“ und „Störer“ ({string.Join(", ", fl.TestGroupLabels)})");
				Check(fl.TestGroupOverflow.Length == 0, $"RL: Gruppentasten ohne abgeschnittenen Text ({fl.TestGroupOverflow})");
				int g = Group("Täusch.");
				if (g < 0) { Fail("RL: keine Taste „Täusch.“"); return false; }
				Check(fl.TestGroupState(g) == Ui.Kit.KitButtonState.Ready, $"RL: „Täusch.“ bereit ({fl.TestGroupSub(g)})");
				_glAt = fl.TestGroupRect(g).GetCenter();
				Tap(_glAt, true);
				return false;
			}
			case 22: Tap(_glAt, false); _glFlareFrame = _frame; return false;
			case 26:
			{
				var me = u.Ships[u.PlayerShip];
				Check(Countermeasures.FlaresActive(u, me), $"RL: echter Tipp stößt Täuschkörper aus ({_m.LastCounterMessage})");
				Check(me.FlaresMissing >= 1f, "RL: ein Täuschkörper verbraucht");
				return false;
			}
		}
		if (f > 26 && f < 400)
		{
			// die Rakete erreicht das Ziel und wird abgelenkt
			if (_glEvents!.OfType<MissileResolved>().Any(r => r.Target == u.PlayerShip)) return Finish(f);
			return false;
		}
		if (f >= 400) { Fail("RL: Rakete nicht aufgelöst"); return true; }
		return false;
	}

	bool _glJammerDone;
	int _glJamStep;

	bool Finish(int f)
	{
		var u = _m.Session.U;
		var fl = _m.HudRef.Flight;
		int Group(string label) => fl.TestGroupLabels.IndexOf(label);
		var me = u.Ships[u.PlayerShip];
		if (_glJamStep == 0)
		{
			var res = _glEvents!.OfType<MissileResolved>().First(r => r.Target == u.PlayerShip);
			Check(res.Outcome == "decoyed", $"RL: Rakete durch Täuschkörper abgelenkt ({res.Outcome})");
			Check(_m.LastCounterMessage.Contains("abgelenkt"), $"RL: Meldung nennt die Ablenkung ({_m.LastCounterMessage})");
			int g = Group("Störer");
			if (g < 0) { Fail("RL: keine Taste „Störer“"); return true; }
			_glAt = fl.TestGroupRect(g).GetCenter();
			Tap(_glAt, true);
			_glJamStep = f;
			return false;
		}
		if (f == _glJamStep + 2) { Tap(_glAt, false); return false; }
		if (f == _glJamStep + 6)
		{
			Check(me.JammerOn, $"RL: echter Tipp schaltet den Störsender an ({_m.LastCounterMessage})");
			int g = Group("Störer");
			Check(g >= 0 && fl.TestGroupSub(g).StartsWith("AN"), $"RL: Taste zeigt „AN“ ({(g >= 0 ? fl.TestGroupSub(g) : "-")})");
			Tap(_glAt, true);
			return false;
		}
		if (f == _glJamStep + 8) { Tap(_glAt, false); return false; }
		if (f >= _glJamStep + 12 && !_glJammerDone)
		{
			_glJammerDone = true;
			Check(!me.JammerOn, "RL: zweiter Tipp schaltet den Störsender aus");
			if (_glEvents is not null) u.Events.Raised -= _glEvents.Add;
			if (u.Ships.TryGetValue(_glBomber, out var b)) SpaceSim.Core.Simulation.Departures.RemoveShip(u, b, DepartureReason.Sold, "test");
			Log($"RL: Raketenlenkung geprüft (Warnung, Täuschkörper per Tipp, Störsender an/aus); Flare-Tipp in Bild {_glFlareFrame}");
			return true;
		}
		return false;
	}
}
