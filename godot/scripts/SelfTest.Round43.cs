using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.World;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 122 (Runde 43, Bergbau-Darstellung): (1) Für <b>jedes</b> Modell mit Kontaktwerkzeug steht beim Abbau die
/// Bohrerspitze an der Felsoberfläche und dringt beim Abbau sichtbar etwas ein (Runde 43b: Eindringtiefe im erwarteten Band, nach dem Lösen wieder außen) — beim Spieler (verankert, Pin mit Spitzenabstand)
/// und beim NPC-Schürfer (Marker wird mit der Spitze auf die Oberfläche gesetzt und zum Fels ausgerichtet). (2) Anker: die Trossen
/// starten außerhalb des Bohrerbereichs (Flanken), treffen die Oberfläche seitlich des Bohrpunkts und sind kurz (&lt; 2× Schiffsradius).
/// (3) Abbauende NPC-Miner stehen am Fels (nicht am Feld-Arbeitspunkt). (4) Leer abgebauter Fels zerbricht sichtbar (Trümmer ~2 s).
/// </summary>
public partial class SelfTest
{
	List<string> _r43Models = new();
	int _r43Rock, _r43RockNpc, _r43ModelIdx;
	ShipId _r43Npc;
	int _r43Base;
	bool _r43NpcMade;
	Vec2 _r43NpcStart, _r43NpcLast;
	float _r43NpcMaxStep;
	/// <summary>Runde 43b: Bilder je Modell (Spieler: Kontakt, volle Eindringtiefe, Lösen; NPC: Anflug, Eindringtiefe, Lösen).</summary>
	const int R43Cycle = 320;
	(float Yaw, float Pitch, float Dist) _r43Cam;
	Vector3 _r43Fwd, _r43Pos;
	float _r43Zoom, _r43ZoomMaxStep, _r43CamMaxDev;
	bool _r43ZoomDown;

	/// <summary>Runde 43b: liegt die Eindringtiefe (−Abstand) im erwarteten Band der vollen Tiefe (mit Pulsieren)?</summary>
	static bool R43BiteOk(ShipVisual vis, float gap) =>
		vis.BiteShown >= 0.99f && -gap >= vis.BiteDepth * (1f - ShipVisual.BitePulse) - 0.12f && -gap <= vis.BiteDepth * (1f + ShipVisual.BitePulse) + 0.12f && gap < -0.2f;

	Asteroid R43Rock(int id) => _m.Session.U.Sectors[_m.Session.U.PlayerSector].Asteroids.First(a => a.Id == id);

	/// <summary>Wählt einen frischen abbaubaren Fels für den NPC, weit weg vom Spieler-Fels (Spaltmodelle zerlegen ihren Fels sonst).</summary>
	int R43PickNpcRock()
	{
		var u = _m.Session.U;
		var sector = u.Sectors[u.PlayerSector];
		var pr = R43Rock(_r43Rock);
		var rock = sector.Asteroids.Where(a => a.Minable && a.BaseRadius > 3.5f && u.AsteroidClasses.ContainsKey(a.ClassKey) && Vec2.Distance(a.Position, pr.Position) > 25f)
			.OrderBy(a => Vec2.Distance(a.Position, pr.Position)).FirstOrDefault();
		if (rock is null) rock = sector.Asteroids.First(a => a.Minable && a.Id != _r43Rock);
		rock.Ore = rock.MaxOre = 100_000f;
		return rock.Id;
	}

	/// <summary>Setzt das Schiffsmodell des Spielers direkt (wie Umbau/Werft).</summary>
	void R43SetPlayerModel(string key)
	{
		var u = _m.Session.U;
		var me = u.Ships[u.PlayerShip];
		var m = u.ShipModels[key];
		me.ModelKey = key;
		me.Slots = new System.Collections.Generic.List<ClassSlot>(u.ShipClasses[m.ClassKey].Slots);
		me.Fit = new System.Collections.Generic.List<string?>(m.Fit);
		me.RecomputeStats(u);
	}

	void R43AnchorAt(int rockId)
	{
		var u = _m.Session.U;
		var rock = R43Rock(rockId);
		var me = u.Ships[u.PlayerShip];
		foreach (var g in me.Cargo.Keys.ToList()) me.Cargo[g] = 0f;
		me.AnchorRock = rockId;
		me.AnchorProgress = u.Rules.AnchorTicks;   // Anker hält sofort
		var p = _m.Player.Position;
		var from = new Vec2(p.X, p.Z);
		var dir = (from - rock.Position).Length > 1e-3f ? (from - rock.Position).Normalized() : new Vec2(1, 0);
		_m.TestTeleportTo(rock.Position + dir * (rock.Radius + Collision.ShipRadius + 0.3f));
		_m.TestFace(rock.Position);
		_m.TestSetSlider(0f);
	}

	/// <summary>Abstand Bohrerspitze (Welt) ↔ Felsoberfläche des Felsens; negativ = Eindringtiefe.</summary>
	float R43TipGap(ShipVisual? vis, Asteroid rock)
	{
		if (vis is null) return 999f;
		var tip = vis.TipWorld();
		var c = new Vector3(rock.Position.X, 0f, rock.Position.Y);
		return tip.DistanceTo(c) - rock.Radius;
	}

	/// <summary>Prüft Anker-Trossen eines Schiffs: Start außerhalb des Bohrerbereichs, Seillänge kurz. Liefert Fehlerliste.</summary>
	System.Collections.Generic.List<string> R43AnchorProblems(ShipVisual? vis, AnchorView av, ShipId id, Asteroid rock)
	{
		var bad = new System.Collections.Generic.List<string>();
		if (vis is null) return bad;
		var muz = av.MuzzlesOf(id).ToList();
		if (muz.Count == 0) { bad.Add("keine Trossen-Mündungen"); return bad; }
		float rope = av.RopeLength(id);
		float shipLen = 2f * vis.Root.Scale.X;
		Log($"[selftest] Seil {vis.Info.Key}: {rope:0.00} von Schiffslänge {shipLen:0.00} = {rope / shipLen:P0}");
		// Runde 46 (Gerätetest: Lücke zwischen Seil und Schiff): jedes Seil beginnt an der Mündung seines Harpunenwerfers (sichtbar verbunden)
		var inv = vis.Root.GlobalTransform.AffineInverse();
		for (int i = 0; i < muz.Count && vis.Harpoons.Count > 0; i++)
		{
			var hm = vis.Harpoons[System.Math.Min(i, vis.Harpoons.Count - 1)].GlobalPosition;
			if (muz[i].DistanceTo(hm) > 0.05f) { bad.Add($"Seil {i} beginnt nicht an der Werfermündung (Abstand {muz[i].DistanceTo(hm):0.00})"); break; }
		}
		if (rope > 0.6f * shipLen) bad.Add($"Seillänge {rope:0.00} > 60 % der Schiffslänge {shipLen:0.00}");
		// Spitzen stecken seitlich des Bohrpunkts in der Oberfläche, nicht nach innen zur Bugachse (Felskrümmung bei kleinem Fels: 15 % Spiel)
		var c = new Vector3(rock.Position.X, 0f, rock.Position.Y);
		var tips = av.TipsOf(id).ToList();
		foreach (var t in tips)
			if (Mathf.Abs(t.DistanceTo(c) - rock.Radius * AnchorView.ImpactDepth) > 0.6f) { bad.Add("Spitze nicht an der Felsoberfläche"); break; }
		for (int i = 0; i < tips.Count && i < muz.Count; i++)
		{
			var lm = inv * muz[i]; var lt = inv * tips[i];
			if (new Vector2(lt.X, lt.Y).Length() < 0.85f * new Vector2(lm.X, lm.Y).Length()) { bad.Add($"Seil {i} läuft nach innen (Spitze {new Vector2(lt.X, lt.Y).Length():0.00}, Mündung {new Vector2(lm.X, lm.Y).Length():0.00})"); break; }
		}
		return bad;
	}

	bool Round43Stage(int f)
	{
		var u = _m.Session.U;
		var sector = u.Sectors[u.PlayerSector];
		switch (f)
		{
			case 1: _m.TestMenu("new:miner"); return false;   // Schürfer-Start (Kontaktwerkzeug S)
			case 2: if (_m.TestDocked) _m.TestUndock(); return false;
			case 95:
			{
				_r43Models = World.ShipModels.ContactModels().Select(i => i.Key).OrderBy(k => k).ToList();
				Check(_r43Models.Count >= 8, $"Kontaktwerkzeug-Modelle gefunden ({_r43Models.Count}: {string.Join(",", _r43Models)})");
				var me = u.Ships[u.PlayerShip];
				var pos = me.Position;
				var rocks = sector.Asteroids.Where(a => a.Minable && a.BaseRadius > 3.5f && u.AsteroidClasses.ContainsKey(a.ClassKey))
					.OrderBy(a => Vec2.Distance(a.Position, pos)).ToList();
				_r43Rock = rocks[0].Id;
				_r43RockNpc = rocks.First(r => Vec2.Distance(r.Position, rocks[0].Position) > 30f).Id;
				foreach (var r in rocks) r.Ore = r.MaxOre = 100_000f;
				R43AnchorAt(_r43Rock);
				_m.TestSetCam(20f, 25f, 26f);
				_r43Base = f;
				_r43ModelIdx = -1;
				return false;
			}
		}
		// Modellesung: Spieler und NPC je Modell
		if (f > 95)
		{
			int mi = (f - 96) / R43Cycle;
			if (mi >= _r43Models.Count)
			{
				// Zerbrechen eines leer abgebauten Felsens
				int bf = f - 96 - R43Cycle * _r43Models.Count;
				if (bf == 1)
				{
					var rock3 = sector.Asteroids.First(a => a.Minable && a.Id != _r43Rock && a.Id != _r43RockNpc);
					Belt.Remove(u, sector, rock3, rock3.Ore);   // AsteroidDepleted → zerbricht in der Ansicht
					return false;
				}
				if (bf > 2 && bf < 30) { Check(_m.TestView.TestBreakupCount >= 1, "leer abgebauter Fels zerbricht sichtbar (Trümmer)"); return false; }
				if (bf >= 160)
				{
					Check(_m.TestView.TestBreakupCount == 0, "Trümmer nach ~2 s verschwunden");
					// Asteroiden schrumpfen nicht: alle lebenden Felsen haben weiterhin ihren Basisradius
					Check(sector.Asteroids.All(a => a.Radius == a.BaseRadius), "alle Felsen mit konstantem Radius (kein Schrumpfen)");
					return true;
				}
				return false;
			}
			int ph = (f - 96) % R43Cycle;
			if (mi != _r43ModelIdx)
			{
				_r43ModelIdx = mi;
				string key = _r43Models[mi];
				R43SetPlayerModel(key);
				Check(u.Ships[u.PlayerShip].Stats.UsesContact, $"Modell {key} hat ein Kontaktwerkzeug");
				R43AnchorAt(_r43Rock);
				// NPC-Schürfer mit demselben Modell am zweiten Fels
				if (!_r43NpcMade)
				{
					var yard = u.StationsById().First(s => !s.IsBuildSite);
					var m0 = u.ShipModels[key];
					var npc = ShipPurchase.Create(u, u.NewShipId(), key, u.ShipClasses[m0.ClassKey].Slots, m0.Fit, ShipRole.Miner, Controller.Ai, u.FreeActor, yard, 2000f);
					u.AddShip(npc);
					if (npc.Sector != u.PlayerSector) u.MoveShip(npc, u.PlayerSector);
					npc.DockedAt = default;
					_r43Npc = npc.Id;
					_r43NpcMade = true;
				}
				else
				{
					var npc = u.Ships[_r43Npc];
					var m0 = u.ShipModels[key];
					npc.ModelKey = key;
					npc.Slots = new System.Collections.Generic.List<ClassSlot>(u.ShipClasses[m0.ClassKey].Slots);
					npc.Fit = new System.Collections.Generic.List<string?>(m0.Fit);
					npc.RecomputeStats(u);
				}
				// Test-NPC ohne Spaltlaser, damit sein Fels nicht zerlegt wird (Messziel bleibt derselbe Fels)
				var npcS = u.Ships[_r43Npc];
				for (int i = 0; i < npcS.Fit.Count; i++)
					if (npcS.Fit[i] is { } k && u.Modules.TryGetValue(k, out var mod) && mod.Stats.GetValueOrDefault("splitPower") > 0f) npcS.Fit[i] = null;
				npcS.RecomputeStats(u);
				Check(npcS.Stats.UsesContact && npcS.Stats.SplitPower <= 0f, $"{key}: NPC-Testfit ohne Spalter, mit Kontaktwerkzeug");
				_r43RockNpc = R43PickNpcRock();   // Spaltmodelle zerlegen ihren Fels — frischen Fels wählen
				var nr = R43Rock(_r43RockNpc);
				var npcShip = u.Ships[_r43Npc];
				npcShip.Cargo.Clear();
				// Opus-Korrektur: der NPC startet 16 Einheiten vor dem Fels (nicht schon daran) und muss selbst anfliegen — geprüft wird der echte Weg
				// (Core-Anflug ohne Sprung, Ansicht setzt die Spitze erst im Kontakt an die Oberfläche)
				MiningAnchor.Release(u, npcShip, sector.Id, "released");
				npcShip.MineAsteroid = nr.Id;
				npcShip.Position = MiningAnchor.HoldPosition(npcShip.Position, nr, ContactMining.ToolReach(npcShip, u) + 16f);
				_r43NpcStart = npcShip.Position; _r43NpcLast = npcShip.Position; _r43NpcMaxStep = 0f;
				npcShip.MinePhase = MinePhase.Mining;
				int fi = sector.Fields.FindIndex(fl => ReferenceEquals(sector.FieldFor(nr.Position), fl));
				npcShip.Order = new MineOrder(sector.Id, fi, u.Stations.Keys.First());
				Log($"[selftest] R43 Modell {mi + 1}/{_r43Models.Count}: {key}");
				return false;
			}
			string keyNow = _r43Models[_r43ModelIdx];
			var rock = R43Rock(_r43Rock);
			if (u.Ships.TryGetValue(_r43Npc, out var npcNow))
			{
				_r43NpcMaxStep = Mathf.Max(_r43NpcMaxStep, Vec2.Distance(_r43NpcLast, npcNow.Position));
				_r43NpcLast = npcNow.Position;
			}
			if (ph == 45)
			{
				// Spieler: verankert, Pin mit Spitzenabstand → Spitze berührt die Oberfläche
				var me = u.Ships[u.PlayerShip];
				var dir = (rock.Position - me.Position).Normalized();
				Check(ContactMining.InContact(me.Position, dir, rock, ContactMining.ToolReach(me, u)), $"{keyNow}: Spieler-Werkzeugfront im Kontakt ({me.Position})");
				float gap = R43TipGap(_m.Player.Visual, rock);
				float bite = _m.Player.Visual?.BiteNow ?? 0f;
				Check(Mathf.Abs(gap + bite) <= 0.5f, $"{keyNow}: Spieler-Spitze an der Oberfläche bzw. um die Eindringtiefe darin (Abstand {gap:0.00}, Tiefe {bite:0.00})");
				var bad = R43AnchorProblems(_m.Player.Visual, _m.TestAnchors, me.Id, rock);
				Check(bad.Count == 0, $"{keyNow}: Spieler-Anker ok ({string.Join("; ", bad)})");
				Check(_m.TestAnchors.ChunksOn(me.Id), $"{keyNow}: abfallende Brocken am Bohrpunkt");
				return false;
			}
			if (ph == 110)
			{
				// Runde 43b: Spieler bohrt — die Spitze steckt um die erwartete Tiefe im Fels (Modell nach vorn, Schiff bleibt gepinnt)
				var vis = _m.Player.Visual;
				float gap = R43TipGap(vis, rock);
				Check(vis is not null && R43BiteOk(vis, gap),
					$"{keyNow}: Spieler-Bohrer dringt ein (Tiefe {-gap:0.00}, erwartet {vis?.BiteDepth:0.00} ±{ShipVisual.BitePulse:P0}, Bohrradius {vis?.DrillRadiusWorld:0.00}, Einfahrt {vis?.BiteShown:0.00})");
				Log($"[selftest] R43b {keyNow} (Rotoren {vis?.Rotors.Count}, Achsen {vis?.DrillAxes.Count}, Modell {vis?.Info.Key}): Tiefe {vis?.BiteDepth:0.00} = {(vis is null || vis.DrillRadiusWorld <= 0f ? 0f : vis.BiteDepth / vis.DrillRadiusWorld):P0} des Bohrradius {vis?.DrillRadiusWorld:0.00}");
				var me = u.Ships[u.PlayerShip];
				Check(ContactMining.InContact(me.Position, (rock.Position - me.Position).Normalized(), rock, ContactMining.ToolReach(me, u)), $"{keyNow}: Core-Schiffsposition unverändert im Kontakt (nur Darstellung verschoben)");
				_m.TestPressBay(_m.TestBayKeys.ToList().IndexOf(WeaponBays.Contact.Key));   // „Lösen“
				return false;
			}
			// Runde 43b (Handy-Test: Kamerawinkel ändert sich nach dem Lösen): Target-Lock auf den verankerten Fels — beim Lösen (Phase 110) und leichtem
			// Abdriften am Fels bleiben Yaw/Neigung gleich, nur der Anker-Zoom gleitet weich auf 1 zurück
			if (ph == 100) { _m.TestCameraAction("cam_lock"); _m.TestSetMineTarget(_r43Rock); _r43CamMaxDev = 0f; _r43ZoomMaxStep = 0f; _r43ZoomDown = false; }
			if (ph == 108) { _r43Pos = _m.Player.Position; _r43Cam = _m.TestCamAngles; _r43Fwd = _m.TestCamForward; _r43Zoom = _m.TestAnchorZoomNow; }
			if (ph > 110 && ph <= 158)
			{
				var a2 = _m.TestCamAngles;
				_r43CamMaxDev = Mathf.Max(_r43CamMaxDev, Mathf.Max(Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(a2.Yaw), Mathf.DegToRad(_r43Cam.Yaw))) * 57.2958f, Mathf.Abs(a2.Pitch - _r43Cam.Pitch)));
				_r43CamMaxDev = Mathf.Max(_r43CamMaxDev, Mathf.RadToDeg(_m.TestCamForward.AngleTo(_r43Fwd)));
				float z = _m.TestAnchorZoomNow;
				_r43ZoomMaxStep = Mathf.Max(_r43ZoomMaxStep, Mathf.Abs(z - _r43Zoom));
				if (z < _r43Zoom - 1e-5f) _r43ZoomDown = true;
				_r43Zoom = z;
				// leichtes Abdriften seitlich am Fels entlang (wie Entpinnen/Abprallen)
				var pp = _m.Player.Position;
				var rc = new Vector3(rock.Position.X, 0f, rock.Position.Y);
				var radial = (pp - rc).Normalized();
				if (ph <= 140) _m.Player.Position = pp + new Vector3(-radial.Z, 0f, radial.X) * 0.04f;
			}
			if (ph == 158)
			{
				Check(_r43CamMaxDev <= 0.5f, $"{keyNow}: Kamera behält beim Lösen Yaw/Neigung (größte Abweichung {_r43CamMaxDev:0.00}°, Target-Lock auf den Fels)");
				Check(!_r43ZoomDown && _r43ZoomMaxStep <= (1f - Main.AnchorZoom) / Main.AnchorZoomSeconds / 60f * 1.5f + 1e-4f && _m.TestAnchorZoomNow > 0.95f,
					$"{keyNow}: Anker-Zoom gleitet weich zurück (größter Schritt je Bild {_r43ZoomMaxStep:0.0000}, jetzt {_m.TestAnchorZoomNow:0.000})");
				_m.TestCameraAction("cam_free");
				_m.Player.Position = _r43Pos;   // Abdriften zurücknehmen (Folgeprüfungen am selben Punkt)
			}
			if (ph == 160)
			{
				var vis = _m.Player.Visual;
				float gap = R43TipGap(vis, rock);
				Check(u.Ships[u.PlayerShip].AnchorRock == 0 && vis is not null && vis.BiteNow < 0.02f && gap >= -0.1f,
					$"{keyNow}: nach dem Lösen fährt der Spieler-Bohrer wieder heraus (Abstand {gap:0.00}, Tiefe {vis?.BiteNow:0.00})");
				if (_m.TestContactState == ContactState.Released) _m.TestPressBay(_m.TestBayKeys.ToList().IndexOf(WeaponBays.Contact.Key));   // „Ankern“
				return false;
			}
			if (ph == 310)
			{
				var npcShip = u.Ships[_r43Npc];
				var vis = _m.TestView.TestVisual(_r43Npc);
				var nr = R43Rock(_r43RockNpc);
				float gap = R43TipGap(vis, nr);
				Check(MiningAnchor.State(u, npcShip) != AnchorState.Held && vis is not null && vis.BiteNow < 0.05f && gap >= -0.1f,
					$"{keyNow}: nach dem Lösen fährt der NPC-Bohrer wieder heraus (Abstand {gap:0.00}, Tiefe {vis?.BiteNow:0.00}, Anker {MiningAnchor.State(u, npcShip)})");
				return false;
			}
			if (ph == 270)
			{
				// NPC: Marker steht mit der Spitze an der Felsoberfläche, zum Fels ausgerichtet
				var npcShip = u.Ships[_r43Npc];
				var nr = sector.Asteroids.FirstOrDefault(a => a.Id == (npcShip.AnchorRock != 0 ? npcShip.AnchorRock : npcShip.MineAsteroid)) ?? R43Rock(_r43RockNpc);
				var vis = _m.TestView.TestVisual(_r43Npc);
				var toN = nr.Position - npcShip.Position;
				Check(ContactMining.InContact(npcShip.Position, toN.Normalized(), nr, ContactMining.ToolReach(npcShip, u)), $"{keyNow}: NPC hat den Fels selbst angeflogen und berührt ihn (Core)");
				Check(Vec2.Distance(_r43NpcStart, npcShip.Position) > 10f && _r43NpcMaxStep <= Travel.SpeedOf(npcShip) + 0.01f,
					$"{keyNow}: NPC-Anflug ohne Sprung ({Vec2.Distance(_r43NpcStart, npcShip.Position):0.0} geflogen, größter Schritt {_r43NpcMaxStep:0.00} ≤ Tempo {Travel.SpeedOf(npcShip):0.0})");
				float gap = R43TipGap(vis, nr);
				Check(vis is not null && R43BiteOk(vis, gap),
					$"{keyNow}: NPC-Bohrer dringt ein (Tiefe {-gap:0.00}, erwartet {vis?.BiteDepth:0.00}, Einfahrt {vis?.BiteShown:0.00}, Anker {MiningAnchor.State(u, npcShip)})");
				var marker = _m.TestView.ShipPosition(_r43Npc);
				if (marker is { } mp)
				{
					var toRock = (new Vector3(nr.Position.X, 0, nr.Position.Y) - mp);
					var yaw = Mathf.Atan2(-toRock.X, -toRock.Z);
					// Marker-Blickrichtung (Bug −Z) soll zum Fels zeigen
					var visNode = vis!.Root;
					var fwd = -visNode.GlobalTransform.Basis.Z;
					float dot = fwd.Dot(toRock.Normalized());
					Check(dot > 0.98f, $"{keyNow}: NPC ausgerichtet auf den Fels (dot {dot:0.000})");
				}
				var bad = R43AnchorProblems(vis, _m.TestAnchors, _r43Npc, nr);
				Check(bad.Count == 0, $"{keyNow}: NPC-Anker ok ({string.Join("; ", bad)})");
				Check(_m.TestAnchors.VisibleTethers > 0, $"{keyNow}: NPC-Trossen sichtbar");
				// Runde 43b: Anker lösen — bis der NPC neu verankert (Ankerzeit), fährt der Bohrer heraus (Prüfung bei Phase 310)
				MiningAnchor.Release(u, npcShip, sector.Id, "released");
				return false;
			}
			return false;
		}
		return false;
	}
}
