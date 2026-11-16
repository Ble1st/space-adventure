using System.Linq;
using Godot;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 124 (Runde 44, größere Sektoren): (1) Randpfeile tragen die Entfernung („· 1,2 km“); (2) Sterne kacheln mit der Kamera, der Planet
/// folgt mit Parallaxe, die Sichtweite reicht über den Sektor; (3) Sektorkarte mit Maßstabsleiste, Universumkarte mit „Ø … · Flug …“; (4) Zeitraffer
/// mit Autopilot: der eigene Flug läuft in Teilschritten (×10), ohne harten Aufprall, und das Andocken schaltet den Zeitraffer ab. (5) Runde 44g: auch
/// manueller Flug (Regler voll) legt im Zeitraffer ~10× die Strecke zurück, ohne Aufprall; ein Brand schaltet ihn ab; Auto-Mining mit Zeitraffer fliegt
/// an, verankert und baut mehrfach schneller ab als in Echtzeit möglich, ohne harten Aufprall.
/// </summary>
public partial class SelfTest
{
	Vector3 _r44bStart;
	StationId _r44bTarget;
	int _r44bDockFrame = -1;
	float _r44bDist;

	bool Round44bStage(int f)
	{
		var u = _m.Session.U;
		if (_r44gPhase > 0) return Round44gStep(f);
		if (f == 0) { _r44gPhase = 0; _m.TestMenu("new:trader"); return false; }
		if (f == 40)
		{
			var sec = u.Sectors[u.PlayerSector];
			Check(sec.Scale >= 3f && sec.Scale <= 8f, $"R44: Startsektor {sec.Name} Maßstab {sec.Scale:0.0} (3–8)");
			var list = _m.HudRef.StationScreenPositions?.Invoke();
			Check(list is { Count: > 0 } && list.All(e => e.Name.Contains(" · ") && (e.Name.EndsWith(" km") || e.Name.EndsWith(" m"))),
				"R44: Randpfeil-Namen tragen die Entfernung: " + string.Join(" | ", (list ?? new System.Collections.Generic.List<(Vector2, string, int)>()).Take(3).Select(e => e.Name)));
			Check(_m.HudRef.EdgeMarkers.Count > 0, $"R44: {_m.HudRef.EdgeMarkers.Count} Randpfeile im großen Sektor");
			var cam = _m.TestCam.Position;
			var tiles = _m.GetChildren().OfType<MultiMeshInstance3D>().Where(n => n.Name.ToString().StartsWith("Sterne")).ToList();
			Check(tiles.Count == 9 && tiles.Any(t => Mathf.Abs(t.Position.X - cam.X) <= 400.1f && Mathf.Abs(t.Position.Z - cam.Z) <= 400.1f), $"R44: Sternkacheln um die Kamera ({tiles.Count})");
			Check(_m.TestCam.Far >= sec.Extent * 2f, $"R44: Sichtweite {_m.TestCam.Far:0} ≥ 2 × Ausdehnung {sec.Extent:0}");
			// Sektorkarte mit Maßstabsleiste
			_m.TestOpenCcArea(Ui.Screens.CommandAreas.Sector);
			return false;
		}
		if (f == 60)
		{
			var bar = _m.HudRef.CcSector.TestScaleBar;
			Check(bar.Text.Length > 0 && bar.Px > 20f && bar.Px <= 151f, $"R44: Maßstabsleiste der Sektorkarte „{bar.Text}“ ({bar.Px:0} px)");
			var other = u.Sectors.Values.Where(s => s.Id != u.PlayerSector).OrderBy(s => s.Id.Value).First();
			_m.HudRef.CcUniverse.TestSelect(other.Id);
			_m.TestOpenCcArea(Ui.Screens.CommandAreas.Universe);
			return false;
		}
		if (f == 80)
		{
			string line = _m.HudRef.CcUniverse.TestSizeLine;
			Check(line.StartsWith("Ø ") && line.Contains("Flug ~"), $"R44: Universumkarte „{line}“");
			_m.TestCloseCommand();
			// Zeitraffer mit Autopilot: zur entferntesten andockbaren Station im Sektor
			var me = u.Ships[u.PlayerShip];
			var far = u.Sectors[u.PlayerSector].Stations.Select(id => u.Stations[id]).Where(s => s.Dockable && s.Id != me.DockedAt)
				.OrderByDescending(s => Vec2.Distance(s.Position, me.Position)).First();
			_r44bTarget = far.Id;
			_r44bDist = Vec2.Distance(far.Position, me.Position);
			if (_m.TestDocked) _m.TestUndock();
			return false;
		}
		if (f == 100)
		{
			_m.TestSetStationTarget(_r44bTarget);
			_m.TestSetFast(true);
			_r44bStart = _m.Player.Position;
			_lowestHullR44 = _m.Session.U.Ships[_m.Session.U.PlayerShip].Hull;
			return false;
		}
		if (f > 100 && f < 100 + 60 * 40)
		{
			var me = u.Ships[u.PlayerShip];
			_lowestHullR44 = Mathf.Min(_lowestHullR44, me.Hull);
			if (f == 160)
			{
				Check(_m.TestFlightSteps > 1, $"R44: Zeitraffer mit Autopilot — {_m.TestFlightSteps} Flug-Teilschritte je Bild");
				float moved = _m.Player.Position.DistanceTo(_r44bStart);
				Check(moved > 45f * 1f * 2.5f, $"R44: in 1 s Echtzeit {moved:0} m geflogen (Zeitraffer, Echtzeit wären ≤ 45 m)");
			}
			if (_m.TestDocked && _r44bDockFrame < 0) _r44bDockFrame = f;
			if (_r44bDockFrame >= 0 && f >= _r44bDockFrame + 10)
			{
				Check(_m.TestDockedStation == _r44bTarget, $"R44: per Autopilot im Zeitraffer angedockt ({_r44bDist:0} m in {(_r44bDockFrame - 100) / 60f:0.0} s Echtzeit)");
				Check(!_m.TestFast, "R44: Andocken schaltet den Zeitraffer ab");
				Check(_lowestHullR44 >= me.MaxHull - 0.01f, $"R44: kein Aufprall im Zeitraffer-Flug (Hülle min {_lowestHullR44:0}/{me.MaxHull:0})");
				_m.TestUndock();
				_r44gPhase = 1; _r44gT = f;   // Runde 44g: weiter mit manuellem Flug und Auto-Mining im Zeitraffer
				return false;
			}
			return false;
		}
		if (f >= 100 + 60 * 40) { Check(false, $"R44: Zeitraffer-Flug kam nicht an (Autopilot {_m.TestAutopilot}, Abstand {_m.Player.Position.DistanceTo(new Vector3(u.Stations[_r44bTarget].Position.X, 0, u.Stations[_r44bTarget].Position.Y)):0})"); _m.TestSetFast(false); return true; }
		return false;
	}
	float _lowestHullR44;

	// ───────── Runde 44g: Zeitraffer immer für den eigenen Flug ─────────
	int _r44gPhase, _r44gT, _r44gHits, _r44gHeld = -1;
	Vector3 _r44gP1, _r44gP2;
	float _r44gD1, _r44gCargo;

	/// <summary>Freie Gerade (Hindernisse + Rand, Tore mit Sprungradius) ab einem Startpunkt im Spielersektor: Start und Kurs (Heading) oder null.</summary>
	(Vec2 Pos, float Heading)? R44gClearLine(float length)
	{
		var u = _m.Session.U;
		var obs = _m.View.Obstacles;
		var gates = _m.View.Gates.Select(g => new Vec2(g.Pos.X, g.Pos.Z)).ToList();
		bool Free(Vec2 a, Vec2 b)
		{
			var d = b - a; float len = d.Length; var n = d * (1f / len);
			foreach (var o in obs)
			{
				float t = Mathf.Clamp((o.Position - a).Dot(n), 0f, len);
				if (Vec2.Distance(a + n * t, o.Position) < o.Radius + 12f) return false;
			}
			foreach (var g in gates)
			{
				float t = Mathf.Clamp((g - a).Dot(n), 0f, len);
				if (Vec2.Distance(a + n * t, g) < 40f) return false;
			}
			return true;
		}
		var me = u.Ships[u.PlayerShip];
		var starts = new System.Collections.Generic.List<Vec2> { me.Position };
		float ext = u.Sectors[u.PlayerSector].Extent;
		for (float x = -ext; x <= ext; x += 80f) for (float y = -ext; y <= ext; y += 80f) starts.Add(new Vec2(x, y));
		foreach (var s0 in starts)
			for (int k = 0; k < 72; k++)
			{
				float h = k * Mathf.Tau / 72f;
				var dir = new Vec2(-Mathf.Sin(h), -Mathf.Cos(h));
				if (Free(s0, s0 + dir * length)) return (s0, h);
			}
		return null;
	}

	bool Round44gStep(int f)
	{
		var u = _m.Session.U;
		int t = f - _r44gT;
		switch (_r44gPhase)
		{
			case 1:   // manueller Flug: freie Gerade suchen, bei ×1 auf Reisetempo beschleunigen
				if (t < 20) return false;
				if (t == 20)
				{
					u.Rules.PlayerRaids = false;   // keine Überfallwarnung, die den Zeitraffer abschaltet
					u.PendingEncounters.RemoveAll(p => p.Target == u.PlayerShip);
					if (R44gClearLine(560f) is not { } line) { Check(false, "R44g: keine freie Gerade für den manuellen Flug gefunden"); return true; }
					_m.TestTeleportTo(line.Pos);
					_m.Player.Heading = line.Heading;
					_m.Player.Grace = 0f;
					_m.TestSetFast(false);
					_m.TestSetSlider(1f);
					_r44gHits = _m.TestHits;
					_lowestHullR44 = u.Ships[u.PlayerShip].Hull;
					return false;
				}
				_lowestHullR44 = Mathf.Min(_lowestHullR44, u.Ships[u.PlayerShip].Hull);
				if (t == 20 + 180) _r44gP1 = _m.Player.Position;   // 3 s bei ×1 beschleunigt
				if (t == 20 + 210) { _r44gP2 = _m.Player.Position; _r44gD1 = _r44gP2.DistanceTo(_r44gP1); _m.TestSetFast(true); }   // 0,5 s bei ×1
				if (t == 20 + 240)
				{
					float d10 = _m.Player.Position.DistanceTo(_r44gP2);   // 0,5 s Echtzeit bei ×10
					Check(_m.TestFlightSteps > 1, $"R44g: manueller Flug im Zeitraffer in {_m.TestFlightSteps} Teilschritten je Bild");
					Check(!_m.TestAutopilotOn && _m.TestFast, "R44g: manueller Flug ohne Autopilot, Zeitraffer an");
					float ratio = d10 / Mathf.Max(0.01f, _r44gD1);
					Check(ratio > 7f && ratio < 12f, $"R44g: manueller Flug legt im Zeitraffer ~10× die Strecke zurück ({_r44gD1:0.0} m bei ×1 → {d10:0} m bei ×10 in 0,5 s, ×{ratio:0.0})");
					Check(_m.TestHits == _r44gHits && _lowestHullR44 >= u.Ships[u.PlayerShip].MaxHull - 0.01f, $"R44g: kein Aufprall im manuellen Zeitraffer-Flug (Treffer {_m.TestHits - _r44gHits}, Hülle min {_lowestHullR44:0})");
					// Gefahr schaltet den Zeitraffer weiter ab (Brand an Bord)
					u.Events.Publish(new SpaceSim.Core.Events.FireStarted(u.PlayerShip, u.PlayerSector, 0, ""));
					return false;
				}
				if (t == 20 + 242)
				{
					Check(!_m.TestFast && _m.LastDangerMessage == "Brand an Bord", $"R44g: Gefahr schaltet den Zeitraffer im manuellen Flug ab ({_m.LastDangerMessage})");
					_m.TestSetSlider(0f);
					_m.TestMenu("new:miner");
					_r44gPhase = 2; _r44gT = f; _r44gHeld = -1;
				}
				return false;
			case 2:   // Auto-Mining mit Zeitraffer: Anflug, Anker, Kontaktabbau
			{
				if (t < 40) return false;
				var me = u.Ships[u.PlayerShip];
				if (t == 40)
				{
					u.Rules.PlayerRaids = false;
					u.PendingEncounters.RemoveAll(p => p.Target == u.PlayerShip);
					if (_m.TestDocked) _m.TestUndock();
					return false;
				}
				if (t == 70)
				{
					_m.TestStartMining();
					_m.TestSetFast(true);
					_r44gHits = _m.TestHits;
					_lowestHullR44 = me.Hull;
					return false;
				}
				if (t < 70) return false;
				_lowestHullR44 = Mathf.Min(_lowestHullR44, me.Hull);
				if (_r44gHeld < 0 && SpaceSim.Core.Mining.MiningAnchor.State(u, me) == SpaceSim.Core.Mining.AnchorState.Held && _m.TestContactState == SpaceSim.Core.Mining.ContactState.Mining)
				{
					_r44gHeld = f; _r44gCargo = me.UsedVolume(u);
					Check(_m.TestFast && _m.TestFlightSteps > 1, $"R44g: Auto-Mining im Zeitraffer verankert ({_m.TestFlightSteps} Teilschritte, nach {(t - 70) / 60f:0.0} s Echtzeit)");
				}
				if (_r44gHeld >= 0 && f == _r44gHeld + 15)
				{
					float gain = me.UsedVolume(u) - _r44gCargo;
					float cm = me.Stats.ContactMining;
					Check(gain > 0.8f * cm, $"R44g: Abbau im Zeitraffer {gain:0.0} Einheiten in 0,25 s Echtzeit (bei ×1 höchstens {0.5f * cm:0.0})");
					Check(_m.TestHits == _r44gHits && _lowestHullR44 >= me.MaxHull - 0.01f, $"R44g: Auto-Mining im Zeitraffer ohne harten Aufprall (Treffer {_m.TestHits - _r44gHits}, Hülle min {_lowestHullR44:0}/{me.MaxHull:0})");
					_m.TestSetFast(false);
					_m.TestStopAutomation();
					_m.TestSetSlider(0f);
					return true;
				}
				if (t > 70 + 60 * 40) { Check(false, $"R44g: Auto-Mining im Zeitraffer verankerte nicht (Kontakt {_m.TestContactState}, Anker {me.AnchorRock}, Status {_m.TestMinerStatus})"); _m.TestSetFast(false); return true; }
				return false;
			}
		}
		return true;
	}
}
