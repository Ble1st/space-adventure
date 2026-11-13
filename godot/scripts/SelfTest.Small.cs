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
	int _smWide = -1;
	System.Collections.Generic.List<SimEvent>? _smEv;

	/// <summary>Rückgabe true = Stufe fertig.</summary>
	bool SmallStationStage(int f)
	{
		var u = _m.Session.U;
		switch (f)
		{
			case 1:
			{
				GetWindow().Size = new Vector2I(1280, 720);   // Runde 15: echtes Seitenverhältnis (der headless-Standard 1280×1280 hat unter dem HUD eine leere Fläche)
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
				// Runde 14 (Handy-Test 2340×1080): gleiche Prüfung bei Gerätegröße — Fenster umstellen, Gefecht neu
				GetWindow().Size = new Vector2I(2340, 1080);
				_smWide = 0;
				return false;
			}
			case > 640 and < 1100 when _smWide >= 0:
			{
				int k = (f - 641);
				int[] ranges = { 30, 60, 100 };
				int idx = k / 120;
				if (idx >= ranges.Length) { _smWide = -1; GetWindow().Size = new Vector2I(1280, 720); _stageStart = _frame - 1099; return false; }
				if (k % 120 == 1)
					foreach (var fe in u.Fights.Encounters.ToList()) foreach (var part in fe.Participants.Where(x => x.Active).ToList()) Engage.Leave(u, fe, part, CombatExit.Fled);
				if (k % 120 == 10 && u.Ships.TryGetValue(_smRaider, out var rw))
				{
					var me = u.Ships[u.PlayerShip];
					var p = _m.Player.Position;
					if (rw.Transit is not null) u.FinishTransit(rw);
					if (rw.Sector != me.Sector) u.MoveShip(rw, me.Sector);
					rw.Command = null; rw.Order = null; rw.DockedAt = default;
					rw.Position = new Vec2(p.X, p.Z) + new Vec2(0f, -ranges[idx]);
					rw.Hull = rw.MaxHull;
					_m.TestFace(rw.Position);
					// gewähltes feindliches Ziel (Kaperer steht still) — die Kamera rahmt Schiff und Ziel
					u.PendingEncounters.RemoveAll(x => x.Target == me.Id);
					rw.NextThinkTick = u.Tick + 100_000;
					_m.TestFight.SetTarget(rw.Id);
				}
				if (k % 120 == 110)
				{
					var size = GetViewport().GetVisibleRect().Size;
					var free = _m.FramingField();
					if (idx == 0) GD.Print($"[selftest] Gefechtskamera {size.X:0}×{size.Y:0}: freies Feld x {free.X0:0.00}…{free.X1:0.00}, y {free.Y0:0.00}…{free.Y1:0.00}; HUD {string.Join(" ", _m.HudRef.Flight.OccupiedRects().Select(r => $"[{r.Position.X:0},{r.Position.Y:0} {r.Size.X:0}x{r.Size.Y:0}]"))}");
					Check(_m.TestFraming, $"{size.X:0}×{size.Y:0}: Gefechtskamera aktiv ({ranges[idx]} m; {_m.TestFramingDebug})");
					if (u.Ships.TryGetValue(_smRaider, out var rw2))
					{
						var cam = _m.TestCam;
						var pts = new[] { ("Ziel", _m.TestView.ShipPosition(rw2.Id) ?? new Vector3(rw2.Position.X, 0f, rw2.Position.Y)), ("Spielerschiff", _m.Player.Position) };
						foreach (var (what, w) in pts)
						{
							if (what == "Ziel" && !_m.TestFramingBoth) continue;   // Runde 15: zu weit — nur das Schiff, Ziel per Randpfeil
							var sp = cam.UnprojectPosition(w);
							float nx = sp.X / size.X * 2f - 1f, ny = 1f - sp.Y / size.Y * 2f;
							bool inside = nx >= free.X0 - 0.03f && nx <= free.X1 + 0.03f && ny >= free.Y0 - 0.03f && ny <= free.Y1 + 0.03f;
							// 100 m: der Höchstabstand der Kamera reicht nicht immer — dann wenigstens im Bild
							float real = (pts[0].Item2 - pts[1].Item2).Length();
							if (ranges[idx] >= 100 || real > 85f) inside = sp.X >= 0 && sp.X <= size.X && sp.Y >= 0 && sp.Y <= size.Y;
							Check(inside, $"{size.X:0}×{size.Y:0}: {what} bei {ranges[idx]} m im freien Feld ({nx:0.00}, {ny:0.00}; {_m.TestFrameDebug}; Ziel {w})");
						}
					}
				}
				return false;
			}
			case 1100:
			{
				u.PendingEncounters.Clear();
				foreach (var fe in u.Fights.Encounters.ToList()) foreach (var part in fe.Participants.Where(x => x.Active).ToList()) Engage.Leave(u, fe, part, CombatExit.Fled);
				// Musik-Schalter per simuliertem Tipp im Hamburger-Menü
				_m.TestOpenCommand();
				_smMusic = SpaceAdventure.Audio.AudioSettings.MusicMuted;
				return false;
			}
			case 1102:
			{
				var mb = Buttons(_m.HudRef.Command).First(b => b.Text.StartsWith("Musik"));
				GD.Print($"[selftest] Musik-Taste {mb.GetGlobalRect()} sichtbar {mb.IsVisibleInTree()}, Meldung {(_m.HudRef.TestAlert.Length > 0 ? _m.HudRef.TestAlertRect.ToString() : "keine")}, Bildschirm {_m.HudRef.Screens.Current?.Id}");
				PressButton(_m.HudRef.Command, mb.Text, true); return false;
			}
			case 1104: PressButton(_m.HudRef.Command, "", false); return false;
			case 1106:
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
		var free = _m.FramingField();   // Runde 14: das freie Feld aus den HUD-Flächen der aktuellen Auflösung
		var tpos = u.Fights.Of(raider.Id) is { Spatial: true } fe && fe.Of(raider.Id) is { } rp ? rp.Pos : raider.Position;
		foreach (var (what, w) in new[] { ("Ziel", new Vector3(tpos.X, 0f, tpos.Y)), ("Spielerschiff", _m.Player.Position) })
		{
			if (what == "Ziel" && !_m.TestFramingBoth) continue;   // Runde 15: zu weit — nur das Schiff gerahmt, das Ziel zeigt der Randpfeil
			var sp = cam.UnprojectPosition(w);
			float nx = sp.X / size.X * 2f - 1f, ny = 1f - sp.Y / size.Y * 2f;
			bool inside = nx >= free.X0 - 0.03f && nx <= free.X1 + 0.03f && ny >= free.Y0 - 0.03f && ny <= free.Y1 + 0.03f;
			Check(inside, $"{what} bei {metres} m im freien Bildfeld ({nx:0.00}, {ny:0.00} in x {free.X0:0.00}…{free.X1:0.00}, y {free.Y0:0.00}…{free.Y1:0.00}; {size.X:0}×{size.Y:0}, Kamera {_m.TestCamAngles.Dist:0})");
		}
	}
}
