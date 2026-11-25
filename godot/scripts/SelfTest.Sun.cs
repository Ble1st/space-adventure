using System;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 165 (Sonnen): Jäger vor der Sonne des Startsektors, Bug zur Sonne; Sonnenkugel, Korona und Atmosphäre in der Ansicht, Sonne auf der
/// Sektorkarte; ein echter Tipp an das rechte Ende des Schubreglers fliegt in die Atmosphäre → Hitze steigt, Warnzeile „Sonnenatmosphäre“ sichtbar; weiter in
/// die Korona → Warnzeile „SONNENKORONA“, Meldung, Hülle sinkt; ein Tipp an das linke Ende hält an, Wegsetzen nimmt die Warnung weg.
/// </summary>
public partial class SelfTest
{
	Vector2 _sunTapAt;
	int _sunAtmFrame = -1, _sunCoronaFrame = -1;
	float _sunHullAtCorona, _sunStartDist;

	bool SunStage(int f)
	{
		var u = _m.Session.U;
		var fl = _m.HudRef.Flight;
		var hud = _m.HudRef;
		if (f == 1) { _m.TestMenu("new:fighter"); return false; }
		var me = u.Ships[u.PlayerShip];
		var sun = Suns.Of(u, u.PlayerSector);
		if (f == 8)
		{
			if (_m.TestDocked) _m.TestUndock();
			u.Rules.PlayerRaids = false;
			Check(u.Rules.Suns, "Sonne: in der Standardwelt an");
			Check(sun is not null, "Sonne: der Startsektor hat eine Sonne");
			if (sun is null) return true;
			float atm = Suns.AtmosphereRadius(u.Rules, sun);
			float len = sun.Position.Length;
			var dir = len < 1e-3f ? new Vec2(1f, 0f) : (default(Vec2) - sun.Position) * (1f / len);
			_m.TestTeleportTo(sun.Position + dir * (atm + 30f));
			_m.TestFace(sun.Position);
			_sunStartDist = atm + 30f;
			return false;
		}
		if (sun is null) return true;
		if (f == 12)
		{
			var view = _m.TestSun;
			Check(view is not null && view.TestVisible, "Sonne: Sonnenkugel in der Ansicht");
			if (view is not null)
			{
				Check(Mathf.Abs(view.TestBodyRadius - sun.Radius) < 0.5f, $"Sonne: Kugel hat den Sonnenradius ({view.TestBodyRadius} / {sun.Radius})");
				Check(view.TestCoronaRadius > view.TestBodyRadius && view.TestAtmosphereRadius > view.TestCoronaRadius, "Sonne: Korona- und Atmosphärenhülle um die Kugel");
				Check(view.TestLightEnergy > 0f, "Sonne: farbiges Licht");
			}
			Check(_m.TestSunZone == SunZone.None, $"Sonne: Start außerhalb der Atmosphäre ({_m.TestSunZone})");
			Check(hud.SunEdgeMarker is not null, "Sonne: Randpfeil zur Sonne im Flugbild");
			_m.TestOpenCcArea(Ui.Screens.CommandAreas.Sector);
			hud.Screens.Current?.Refresh();
			hud.Screens.Current?.Refresh();
			return false;
		}
		if (f == 16)
		{
			Check(hud.CcSector.TestSunDrawn, "Sonne: auf der Sektorkarte gezeichnet");
			_m.TestCloseCommand();
			return false;
		}
		if (f == 22) { var r = fl.TestSliderRect; _sunTapAt = r.Position + new Vector2(r.Size.X * 0.98f, r.Size.Y * 0.5f); Tap(_sunTapAt, true); return false; }
		if (f == 24) { Tap(_sunTapAt, false); return false; }
		if (f < 26) return false;
		if (f > 2400) { Fail($"Sonne: Flug in die Korona nicht beendet (Zone {_m.TestSunZone}, Abstand {Vec2.Distance(me.Position, sun.Position):0}, Hitze {me.Systems.Heat:0.#})"); return true; }
		float dist = Vec2.Distance(me.Position, sun.Position);
		var zone = Suns.ZoneAt(u, u.PlayerSector, me.Position);
		if (_sunAtmFrame < 0)
		{
			if (zone == SunZone.Atmosphere && me.Systems.Heat > 0.5f)
			{
				_sunAtmFrame = f;
				Check(dist < _sunStartDist, $"Sonne: echter Tipp auf den Regler fliegt zur Sonne ({dist:0} < {_sunStartDist:0})");
				Check(fl.TestFire.Contains("Sonnenatmosphäre"), $"Sonne: Warnzeile in der Atmosphäre („{fl.TestFire}“)");
				Check(Suns.HeatRate(u, me) > 0f, "Sonne: Hitze aus der Atmosphäre");
			}
			return false;
		}
		if (_sunCoronaFrame < 0)
		{
			if (zone is SunZone.Corona or SunZone.Body)
			{
				_sunCoronaFrame = f;
				_sunHullAtCorona = me.Hull;
				Check(me.Systems.Heat > 1f, $"Sonne: Hitze gestiegen ({me.Systems.Heat:0.#})");
			}
			return false;
		}
		if (f == _sunCoronaFrame + 6)
		{
			Check(fl.TestFire.Contains("SONNENKORONA"), $"Sonne: Warnzeile in der Korona („{fl.TestFire}“)");
			Check(hud.TestAlert.Contains("SONNENKORONA") || hud.TestAlert.Contains("Sonnenkorona"), $"Sonne: Meldung beim Eintritt in die Korona („{hud.TestAlert}“)");
			var r = fl.TestSliderRect; _sunTapAt = r.Position + new Vector2(2f, r.Size.Y * 0.5f); Tap(_sunTapAt, true);
			return false;
		}
		if (f == _sunCoronaFrame + 8) { Tap(_sunTapAt, false); return false; }
		if (f == _sunCoronaFrame + 60)
		{
			Check(me.Hull < _sunHullAtCorona || me.Hull <= 1f, $"Sonne: Korona nimmt Hülle ({_sunHullAtCorona:0.#} → {me.Hull:0.#})");
			Log($"Sonne: Atmosphäre in Bild {_sunAtmFrame}, Korona in Bild {_sunCoronaFrame}, Hitze {me.Systems.Heat:0.#}, Hülle {_sunHullAtCorona:0.#} → {me.Hull:0.#}");
			_m.TestTeleportTo(sun.Position + (me.Position - sun.Position) * (Suns.AtmosphereRadius(u.Rules, sun) * 1.4f / MathF.Max(1f, dist)));
			return false;
		}
		if (f == _sunCoronaFrame + 70)
		{
			Check(!fl.TestFire.Contains("Sonne") && !fl.TestFire.Contains("SONNE"), $"Sonne: Warnzeile außerhalb weg („{fl.TestFire}“)");
			me.Hull = me.MaxHull; me.Systems.Heat = 0f;
			return true;
		}
		return false;
	}
}
