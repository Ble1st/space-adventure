using System.Linq;
using Godot;
using SpaceAdventure.World;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe Phase 30 / Runde 13 (eigene Datei): Kleinstation im Sektor (Modell, Radius, nicht andockbar), Größenklassen der Stationen,
/// Waffenstellung schießt aus ihren Stellungen; Gefechtskamera rahmt Schiff und Ziel bei 30 und 60 m; Musik-Schalter per Tipp; Profil- und Stellungsausgabe.
/// </summary>
public partial class SelfTest
{
	StationId _smPlat;
	ShipId _smRaider;
	int _smShots, _smOffCentre;
	bool _smMusic;
	System.Collections.Generic.List<SimEvent>? _smEv;

	/// <summary>Rückgabe true = Stufe fertig.</summary>
	bool SmallStationStage(int f)
	{
		var u = _m.Session.U;
		switch (f)
		{
			case 1:
			{
				_m.TestMenu("new");
				var (ships, stations) = ShipModels.HardpointCounts();
				GD.Print($"[selftest] Hardpoints geladen: {ships} Schiffsmodelle, {stations} Stationsmodelle");
				Check(ships >= 10 && stations >= 5, $"Stellungen aus den Modelldaten geladen ({ships} Schiffe, {stations} Stationen)");
				return false;
			}
			case 2:
			{
				var plat = u.StationsById().First(s => s.TypeKey == "platform_light");
				_smPlat = plat.Id;
				_m.TestEnterSector(plat.Sector);
				_m.TestResetProfile();
				return false;
			}
			case 8:
			{
				var plat = u.Stations[_smPlat];
				Check(!plat.Dockable && Docking.Issue(u, plat, u.Ships[u.PlayerShip]) is { } why && why.Contains("Kleinstation"), "Waffenstellung ist nicht andockbar");
				Check(plat.Radius < 10f && plat.Radius == u.StationTypes[plat.TypeKey].Radius, $"Radius der Kleinstation aus dem Typ ({plat.Radius})");
				var node = _m.TestView.StationModelNodes().FirstOrDefault(n => n.Id == _smPlat);
				Check(node.Id == _smPlat, "Waffenstellung in der Sektoransicht");
				if (node.Model is { } model)
				{
					float r = StationModels.PlaneRadius(model);
					Check(Mathf.Abs(r - plat.Radius) <= 0.15f * plat.Radius + 0.3f, $"Modellradius {r:0.0} ≈ Radius {plat.Radius:0.0}");
				}
				// Größenklassen: Radius wächst mit der Klasse, das HQ ist größer als ein Wasserwerk
				foreach (var (id, m2, _) in _m.TestView.StationModelNodes())
					if (u.Stations.TryGetValue(id, out var st) && m2 is not null && !st.IsBuildSite)
						Check(Mathf.Abs(StationModels.PlaneRadius(m2) - st.Radius) <= 0.15f * st.Radius + 0.3f, $"{st.Name}: Modell im Maßstab der Größenklasse ({StationModels.PlaneRadius(m2):0.0} ≈ {st.Radius:0})");
				// Andockversuch wird abgelehnt
				_m.TestDockAt(_smPlat);
				Check(!_m.Docked, "Andocken an der Kleinstation abgelehnt");
				// Stellung feuert: ein Kaperer in Reichweite
				var raider = u.ShipsById().First(ThreatMap.IsRaider);
				_smRaider = raider.Id;
				if (raider.Transit is not null) u.FinishTransit(raider);
				if (raider.Sector != plat.Sector) u.MoveShip(raider, plat.Sector);
				raider.Position = plat.Position + new Vec2(plat.Radius + u.Rules.OutpostCloseRange - 4f, 0f);   // Runde 13b: Kaperer nur im Nahbereich
				raider.DockedAt = default; raider.Order = null; raider.Command = null;
				_smEv = new();
				u.Events.Raised += _smEv.Add;
				_smShots = 0; _smOffCentre = 0;
				return false;
			}
			case > 8 and < 400:
			{
				foreach (var e in _smEv!.OfType<StationShot>().Where(e => e.Station == _smPlat))
				{
					_smShots++;
					var at = _m.TestView.StationMuzzle(_smPlat, e.Gun);
					var centre = new Vector3(u.Stations[_smPlat].Position.X, 0f, u.Stations[_smPlat].Position.Y);
					if (at is { } a && new Vector2(a.X - centre.X, a.Z - centre.Z).Length() > 0.3f) _smOffCentre++;
				}
				_smEv!.Clear();
				if (_smShots >= 3) { _stageStart = _frame - 399; }
				return false;
			}
			case 400:
			{
				Check(_smShots > 0, $"Waffenstellung eröffnet das Feuer auf den Kaperer ({_smShots} Schüsse)");
				Check(_smOffCentre == _smShots && _smShots > 0, $"Schüsse starten an den Stellungen, nicht in der Mitte ({_smOffCentre}/{_smShots})");
				if (_smEv is not null) u.Events.Raised -= _smEv.Add;
				GD.Print($"[selftest] Profil je Bild: {_m.TestProfile()}");
				// Gefechtskamera: Kaperer 30 m vor dem Bug, Überfall läuft
				var me = u.Ships[u.PlayerShip];
				if (u.Ships.TryGetValue(_smRaider, out var raider))
				{
					if (u.Fights.Of(raider.Id) is { } fe && fe.Of(raider.Id) is { } fp) Engage.Leave(u, fe, fp, CombatExit.Fled);
					var p = _m.Player.Position;
					raider.Position = new Vec2(p.X, p.Z) + new Vec2(0f, -30f);
					raider.Hull = raider.MaxHull;
					_m.TestFace(raider.Position);
					Engage.StartRaid(u, raider, me, u.Sectors[me.Sector]);
				}
				return false;
			}
			case 520: FramingCheck(u, 30); return false;
			case 521:
			{
				if (u.Ships.TryGetValue(_smRaider, out var raider))
				{
					var p = _m.Player.Position;
					raider.Position = new Vec2(p.X, p.Z) + new Vec2(0f, -60f);
					if (u.Fights.Of(raider.Id) is { } fe && fe.Of(raider.Id) is { } fp) { fp.Pos = raider.Position; fp.PrevPos = raider.Position; }
				}
				return false;
			}
			case 640:
			{
				FramingCheck(u, 60);
				if (u.Ships.TryGetValue(_smRaider, out var raider) && u.Fights.Of(raider.Id) is { } fe)
					foreach (var part in fe.Participants.Where(x => x.Active).ToList()) Engage.Leave(u, fe, part, CombatExit.Fled);
				// Musik-Schalter per simuliertem Tipp im Hamburger-Menü
				_m.TestOpenCommand();
				_smMusic = SpaceAdventure.Audio.AudioSettings.MusicMuted;
				return false;
			}
			case 642:
			{
				var mb = Buttons(_m.HudRef.Command).First(b => b.Text.StartsWith("Musik"));
				GD.Print($"[selftest] Musik-Taste {mb.GetGlobalRect()} sichtbar {mb.IsVisibleInTree()}, Meldung {(_m.HudRef.TestAlert.Length > 0 ? _m.HudRef.TestAlertRect.ToString() : "keine")}, Bildschirm {_m.HudRef.Screens.Current?.Id}");
				PressButton(_m.HudRef.Command, mb.Text, true); return false;
			}
			case 644: PressButton(_m.HudRef.Command, "", false); return false;
			case 646:
			{
				Check(SpaceAdventure.Audio.AudioSettings.MusicMuted != _smMusic, "Musik-Schalter reagiert auf einen Tipp");
				SpaceAdventure.Audio.AudioSettings.ToggleMusicMute();   // zurück
				_m.HudRef.Screens.Hide();
				return true;
			}
		}
		return false;
	}

	void FramingCheck(Universe u, int metres)
	{
		Check(_m.TestFraming, $"Gefechtskamera aktiv ({metres} m)");
		if (!u.Ships.TryGetValue(_smRaider, out var raider)) { Check(false, "Kaperer für die Gefechtskamera fehlt"); return; }
		var size = GetViewport().GetVisibleRect().Size;
		var cam = _m.TestCam;
		foreach (var (what, w) in new[] { ("Ziel", new Vector3(raider.Position.X, 0f, raider.Position.Y)), ("Spielerschiff", _m.Player.Position) })
		{
			var s = cam.UnprojectPosition(w);
			bool inside = s.X > size.X * 0.15f && s.X < size.X * 0.85f && s.Y > size.Y * 0.18f && s.Y < size.Y * 0.8f;
			Check(inside, $"{what} bei {metres} m im freien Bildfeld ({s.X:0},{s.Y:0} in {size.X:0}×{size.Y:0})");
		}
	}
}
