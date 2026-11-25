using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Sonnen (ERKUNDUNG.md „Sonnen“): Ansicht in <see cref="World.SunView"/>; hier nur die Warnung im Flug-HUD (Zeile aus <see cref="Suns.Warning"/>, vor der
/// Hitzewarnung), die Meldung beim Eintritt in Atmosphäre und Korona (Korona schaltet den Zeitraffer ab) und die Sichtweite der Kamera bis zur Sonne.
/// </summary>
public partial class Main
{
	World.SunView? _sunView;
	SunZone _sunZone;

	/// <summary>Selbsttest: Sonnenansicht und zuletzt gemeldete Zone.</summary>
	internal World.SunView? TestSun => _sunView;
	internal SunZone TestSunZone => _sunZone;

	string SunHudWarning(Universe u) => _docked != null || !u.Ships.ContainsKey(u.PlayerShip) ? "" : Suns.WarningShort(u, PlayerData);

	/// <summary>Sichtweite bis hinter die Sonne des Spielersektors (0 ohne Sonne).</summary>
	float SunFar(Vector3 cam)
	{
		if (_session is null || Suns.Of(_session.U, _session.U.PlayerSector) is not { } sun) return 0f;
		float d = new Vector2(cam.X - sun.Position.X, cam.Z - sun.Position.Y).Length();
		return d + Suns.AtmosphereRadius(_session.U.Rules, sun) + 200f;
	}

	/// <summary>Randpfeil: Bildschirmlage der Sonne und „Sonne · 1,2 km“ (Abstand zur Atmosphäre; in ihr „in der Atmosphäre“).</summary>
	(Vector2 Screen, string Label)? SunScreenPosition()
	{
		if (_session is null || _docked != null || Suns.Of(_session.U, _session.U.PlayerSector) is not { } sun) return null;
		if (EdgeScreen(new Vector3(sun.Position.X, 0f, sun.Position.Y)) is not { } p) return null;
		float d = Vec2.Distance(PlayerData.Position, sun.Position) - Suns.AtmosphereRadius(_session.U.Rules, sun);
		return (p, d > 0f ? $"Sonne · {SpaceSim.Core.Flight.DistanceText.Format(d)}" : "Sonne · in der Atmosphäre");
	}

	/// <summary>Zonenwechsel melden: Atmosphäre als Hinweis, Korona als Gefahr (Zeitraffer aus).</summary>
	void SunTick()
	{
		if (_session is null || _docked != null || !_session.U.Ships.ContainsKey(_session.U.PlayerShip)) { _sunZone = SunZone.None; return; }
		var u = _session.U;
		var zone = Suns.ZoneAt(u, u.PlayerSector, PlayerData.Position);
		if (zone == _sunZone) return;
		var was = _sunZone;
		_sunZone = zone;
		if (zone is SunZone.Corona or SunZone.Body && was is SunZone.None or SunZone.Atmosphere) Danger("Sonnenkorona", Suns.Warning(u, PlayerData));
		else if (zone == SunZone.Atmosphere && was == SunZone.None) _hud.Toast("Sonnenatmosphäre: das Schiff heizt sich auf — Abstand halten.", 4f);
	}
}
