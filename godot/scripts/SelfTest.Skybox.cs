using System.Linq;
using Godot;
using SpaceAdventure.World;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 127 (Himmelskugeln): jeder Sektor der Standardwelt bekommt einen Himmel aus dem Verzeichnis; Planetensektoren einen Himmel mit
/// Planet (Scheibe am oberen Bildrand der Standard- und Rückansicht, Schiff außerhalb) und <b>keine</b> zusätzliche Planetenkugel, alle anderen einen Himmel ohne Planet; der Hintergrund ist der Himmel (nicht die Farbe); die
/// Drehung legt den Fokus-Bildpunkt in die vorgesehene Richtung; ein echter Torsprung tauscht den Himmel; im selben Sektor wird die Textur nicht
/// erneut geladen (höchstens ein Ladevorgang je Wechsel), es bleibt genau eine Himmelskomponente.
/// </summary>
public partial class SelfTest
{
	SectorId[] _skySectors = System.Array.Empty<SectorId>();
	int _skyLoads;

	SkyboxView? Sky => _m.GetChildren().OfType<SkyboxView>().FirstOrDefault();

	bool SkyboxStage(int f)
	{
		var u = _m.Session.U;
		if (f == 0) { _m.TestMenu("new"); return false; }
		if (f == 30)
		{
			Check(_m.GetChildren().OfType<SkyboxView>().Count() == 1, "Himmel: genau eine Himmelskomponente");
			Check(SkyboxView.Manifest.Count >= 10, $"Himmel: Verzeichnis gelesen ({SkyboxView.Manifest.Count} Himmel)");
			_skySectors = u.Sectors.Values.OrderBy(s => s.Id.Value).Select(s => s.Id).ToArray();
			if (_m.TestDocked) _m.TestUndock();
			return false;
		}
		// je Sektor: Bild 40 + 8·i hineinsetzen, 4 Bilder später prüfen
		int n = _skySectors.Length;
		if (f >= 40 && f < 40 + 8 * n)
		{
			int i = (f - 40) / 8, k = (f - 40) % 8;
			var sec = u.Sectors[_skySectors[i]];
			if (k == 0) { _skyLoads = Sky?.Loads ?? 0; _m.TestEnterSector(sec.Id); }
			if (k == 4)
			{
				var sky = Sky;
				bool resolves = SkyboxView.Manifest.TryGetValue(sec.Skybox, out var e);
				Check(resolves, $"Himmel: {sec.Name} hat einen bekannten Himmel („{sec.Skybox}“)");
				if (sky is null || e is null) return false;
				Check(sky.CurrentSector == sec.Id && sky.CurrentKey == sec.Skybox && sky.Texture is not null, $"Himmel: {sec.Name} zeigt {sky.CurrentKey}");
				var env = _m.GetChildren().OfType<WorldEnvironment>().First().Environment;
				Check(env.BackgroundMode == Godot.Environment.BGMode.Sky && env.Sky is not null, $"Himmel: {sec.Name} Hintergrund ist der Himmel");
				Check(env.AmbientLightSource == Godot.Environment.AmbientSource.Color && env.ReflectedLightSource == Godot.Environment.ReflectionSource.Disabled,
					"Himmel: Umgebungslicht bleibt eine feste Farbe, keine Spiegelung aus dem Himmel");
				bool planet = sec.Planet is not null;
				Check(sky.CurrentHasPlanet == planet, $"Himmel: {sec.Name} {(planet ? "mit" : "ohne")} Planet ({sky.CurrentKey})");
				Check(_m.View.PlanetNode is null, $"Himmel: {sec.Name} ohne zusätzliche Planetenkugel");
				var focus = SkyboxView.PanoramaDir(e.FocusU, e.FocusV);
				var aimed = sky.Rotation * (sky.AppliedFollow * SkyboxView.WorldDir(sec.SkyboxYaw, e.Elevation));   // in der Rückansicht kommt die Kamera-Nachführung dazu
				Check(aimed.DistanceTo(focus) < 0.01f, $"Himmel: {sec.Name} Fokus in Blickrichtung (Abweichung {aimed.DistanceTo(focus):0.000})");
				if (planet)
				{
					// Planet unter der Ebene, aber am oberen Bildrand: in der Standardansicht (freie Kamera 57,7°, Bildmitte = Schiff) und in der
					// Standard-Rückansicht (Höhe 45 / Abstand 35, Schiff im unteren Drittel) ist die Scheibe sichtbar, liegt im oberen Bildteil
					// (Schwerpunkt im oberen Drittel, nicht unter den Eckfeldern des HUD), und das Schiff steht außerhalb der Scheibe.
					Check(e.Radius > 0f && e.Elevation <= 0f, $"Himmel: {sec.Name} Planet unter der Ebene (Mitte {e.Elevation:0.0}°, Radius {e.Radius:0.0}°)");
					var c = SkyboxView.WorldDir(sec.SkyboxYaw, e.Elevation);
					float fov = _m.TestCam.Fov;
					var vp = _m.GetViewport().GetVisibleRect().Size;
					float aspect = vp.Y > 0f ? vp.X / vp.Y : 16f / 9f;
					float freePitch = SpaceSim.Core.Flight.OrbitCamera.DefaultPitchDeg;
					float chaseH = SpaceSim.Core.Flight.OrbitCamera.DefaultChaseHeight, chaseB = SpaceSim.Core.Flight.OrbitCamera.DefaultChaseBack;
					float chasePitch = Mathf.RadToDeg(Mathf.Atan2(chaseH, chaseB));
					float chaseShip = Mathf.RadToDeg(Mathf.Atan2(chaseH, chaseB * (1f - SpaceSim.Core.Flight.OrbitCamera.LookAheadShare)));
					foreach (var (view, pitch, ship) in new[] { ("Standardansicht", freePitch, freePitch), ("Rückansicht", chasePitch, chaseShip) })
					{
						var (share, cx, cy, maxY) = DiscOnScreen(c, e.Radius, pitch, fov, aspect);
						Check(share >= 0.05f && cy < 0.3f && maxY < 0.6f && cx > 0.25f && cx < 0.8f,
							$"Himmel: {sec.Name} {view}: Planet oben im Bild (Anteil {share:P0}, Schwerpunkt x {cx:0.00} y {cy:0.00}, unterster Scheibenpunkt y {maxY:0.00})");
						float gap = Mathf.RadToDeg(SkyboxView.WorldDir(0f, -ship).AngleTo(c)) - e.Radius;
						Check(gap >= 5f, $"Himmel: {sec.Name} {view}: Schiff außerhalb der Scheibe (Abstand zum Rand {gap:0.0}°)");
					}
					var fwd = -_m.TestCam.GlobalBasis.Z.Normalized();
					Check(Mathf.RadToDeg(fwd.AngleTo(c)) > e.Radius, $"Himmel: {sec.Name} Kamera blickt nicht in den Planeten");
				}
				else Check(e.Radius == 0f, $"Himmel: {sec.Name} Sternhimmel ohne Planetenradius");
				int d = sky.Loads - _skyLoads;
				Check(d <= 1, $"Himmel: {sec.Name} höchstens ein Ladevorgang beim Wechsel ({d})");
			}
			return false;
		}
		int g = f - (40 + 8 * n);
		if (g == 0)
		{
			// echter Torsprung aus einem Planetensektor in einen Sektor mit anderem Himmel
			var from = u.Sectors.Values.First(s => s.Planet is not null && s.Gates.Any(gid => u.Gates[gid].Open && u.Sectors[u.Gates[u.Gates[gid].Target].Sector].Skybox != s.Skybox));
			_m.TestEnterSector(from.Id);
			return false;
		}
		if (g == 4)
		{
			var from = u.Sectors[u.PlayerSector];
			Check(Sky?.CurrentKey == from.Skybox, $"Himmel: vor dem Sprung {from.Skybox}");
			var gate = from.Gates.Select(id => u.Gates[id]).First(x => x.Open && u.Sectors[u.Gates[x.Target].Sector].Skybox != from.Skybox);
			_skyLoads = Sky?.Loads ?? 0;
			Check(_m.TestJumpThrough(gate.Id), "Himmel: Torsprung ausgeführt");
			return false;
		}
		if (g == 10)
		{
			var now = u.Sectors[u.PlayerSector];
			Check(Sky?.CurrentKey == now.Skybox && Sky.CurrentSector == now.Id, $"Himmel: nach dem Sprung nach {now.Name} {Sky?.CurrentKey}");
			_skyLoads = Sky?.Loads ?? 0;
			return false;
		}
		if (g == 130)
		{
			Check((Sky?.Loads ?? -1) == _skyLoads, $"Himmel: 120 Bilder im selben Sektor ohne Neuladen ({(Sky?.Loads ?? 0) - _skyLoads})");
			// Rückansicht: Himmel dreht mit dem Kamera-Gier — Planet bleibt oben vorn, auch nach einer Kurve von 120°
			var p = u.Sectors.Values.Where(s => s.Planet is not null).OrderBy(s => s.Skybox == "ice_planet" ? 0 : 1).ThenBy(s => s.Id.Value).First();
			_m.TestEnterSector(p.Id);
			if (_m.TestDocked) _m.TestUndock();
			return false;
		}
		if (g == 136)
		{
			_m.TestCameraAction("cam_chase"); _m.TestCameraAction("cam_reset");
			_m.TestFace(ShipAhead(0f));   // Kurs -Z: Standard-Rückansicht
			_m.TestCameraAction("cam_chase");   // SnapHeading auf den neuen Kurs
			return false;
		}
		if (g == 240)
		{
			Check(Sky is { } s0 && s0.Follow >= 0.999f, $"Himmel: Rückansicht — Himmel folgt der Kamera (Anteil {Sky?.Follow:0.00})");
			_skyChaseA = ChaseDisc("Kurs -Z");
			_m.TestFace(ShipAhead(120f));   // Kurve um 120°
			return false;
		}
		if (g == 420)
		{
			var b = ChaseDisc("Kurs 120°");
			Check(Mathf.Abs(Mathf.Wrap(_m.TestCamAngles.Yaw - 120f, -180f, 180f)) < 3f, $"Himmel: Rückansicht hat die Kurve mitgedreht (Kamera-Gier {_m.TestCamAngles.Yaw:0.0}°)");
			Check(Mathf.Abs(b.Cx - _skyChaseA.Cx) < 0.03f && Mathf.Abs(b.Cy - _skyChaseA.Cy) < 0.03f && Mathf.Abs(b.Share - _skyChaseA.Share) < 0.02f,
				$"Himmel: Rückansicht — Planet nach der Kurve an derselben Bildstelle (x {_skyChaseA.Cx:0.000}→{b.Cx:0.000}, y {_skyChaseA.Cy:0.000}→{b.Cy:0.000}, Anteil {_skyChaseA.Share:P1}→{b.Share:P1})");
			// Höhe/Abstand extrem: fast senkrecht (120/15) und sehr flach (15/100), jeweils bei Kurs 120° und nach einer Kurve zurück auf -Z —
			// der Himmel neigt sich mit, der Planet bleibt an derselben Bildstelle wie in der Standard-Rückansicht
			_m.TestSetChase(120f, 15f);
			return false;
		}
		if (g == 500) { SkyChaseSame("Höhe 120/Abstand 15, Kurs 120°"); _m.TestFace(ShipAhead(0f)); return false; }
		if (g == 680) { SkyChaseSame("Höhe 120/Abstand 15, Kurs -Z"); _m.TestSetChase(15f, 100f); return false; }
		if (g == 760) { SkyChaseSame("Höhe 15/Abstand 100, Kurs -Z"); _m.TestFace(ShipAhead(120f)); return false; }
		if (g == 940)
		{
			SkyChaseSame("Höhe 15/Abstand 100, Kurs 120°");
			_m.TestCameraAction("cam_reset");   // Rückansicht: Höhe/Abstand zurück auf 45/35
			_skyPrevYaw = Sky?.AppliedYaw ?? 0f;
			_m.TestCameraAction("cam_free");
			return false;
		}
		if (g == 941)
		{
			float step = Mathf.Abs(Mathf.Wrap((Sky?.AppliedYaw ?? 0f) - _skyPrevYaw, -180f, 180f));
			Check(step < 10f, $"Himmel: Wechsel zur freien Kamera ohne Sprung (erstes Bild {step:0.0}°)");
			return false;
		}
		if (g == 1000)
		{
			var sec = u.Sectors[u.PlayerSector];
			Check(Sky is { } s1 && s1.Follow <= 0.001f && Mathf.Abs(Mathf.Wrap(s1.AppliedYaw - sec.SkyboxYaw, -180f, 180f)) < 0.01f,
				$"Himmel: freie Kamera — weltfest (Anteil {Sky?.Follow:0.00}, Gier {Sky?.AppliedYaw:0.0}° = {sec.SkyboxYaw:0.0}°)");
			_m.TestSetCam(120f, OrbitCameraPitch, 44.94f);
			return false;
		}
		if (g == 1030)
		{
			var sec = u.Sectors[u.PlayerSector];
			Check(Sky is { } s2 && Mathf.Abs(Mathf.Wrap(s2.AppliedYaw - sec.SkyboxYaw, -180f, 180f)) < 0.01f,
				$"Himmel: freie Kamera um 120° geschwenkt — Himmel bleibt weltfest ({Sky?.AppliedYaw:0.0}°)");
			_m.TestCameraAction("cam_reset");
			return true;
		}
		return false;
	}

	(float Share, float Cx, float Cy) _skyChaseA;

	/// <summary>Rückansicht mit geänderter Höhe/Abstand/Kurs: Planetenscheibe an derselben Bildstelle und gleich groß wie in der Standard-Rückansicht.</summary>
	void SkyChaseSame(string what)
	{
		var b = ChaseDisc(what);
		var (_, pitch, _) = _m.TestCamAngles;
		Check(Mathf.Abs(b.Cx - _skyChaseA.Cx) < 0.03f && Mathf.Abs(b.Cy - _skyChaseA.Cy) < 0.03f && Mathf.Abs(b.Share - _skyChaseA.Share) < 0.02f,
			$"Himmel: Rückansicht {what} (Neigung {pitch:0.0}°, Himmel geneigt {Sky?.AppliedTilt:0.0}°) — Planet an derselben Bildstelle " +
			$"(x {_skyChaseA.Cx:0.000}→{b.Cx:0.000}, y {_skyChaseA.Cy:0.000}→{b.Cy:0.000}, Anteil {_skyChaseA.Share:P1}→{b.Share:P1})");
	}
	float _skyPrevYaw;
	static float OrbitCameraPitch => SpaceSim.Core.Flight.OrbitCamera.DefaultPitchDeg;

	/// <summary>Punkt 1000 vor dem Schiff in Kursrichtung <paramref name="headingDeg"/> (0 = -Z; Kurs h → Richtung (-sin h, -cos h)).</summary>
	Vec2 ShipAhead(float headingDeg)
	{
		var pos = _m.TestShipViewPos;
		float h = Mathf.DegToRad(headingDeg);
		return new Vec2(pos.X - 1000f * Mathf.Sin(h), pos.Z - 1000f * Mathf.Cos(h));
	}

	/// <summary>
	/// Lage der Planetenscheibe im Bild der echten Kamera, mit der gerade gesetzten Himmelsdrehung (Planetenmitte = Fokus-Bildpunkt zurück in die Welt);
	/// prüft dabei, dass die Scheibe oben im Bild liegt und das Schiff außerhalb steht.
	/// </summary>
	(float Share, float Cx, float Cy) ChaseDisc(string what)
	{
		var sky = Sky;
		var u = _m.Session.U;
		var sec = u.Sectors[u.PlayerSector];
		if (sky is null || !SkyboxView.Manifest.TryGetValue(sec.Skybox, out var e)) { Check(false, "Himmel: Rückansicht ohne Himmel"); return (0f, 0f, 0f); }
		var c = (sky.Rotation.Transposed() * SkyboxView.PanoramaDir(e.FocusU, e.FocusV)).Normalized();
		var env = _m.GetChildren().OfType<WorldEnvironment>().First().Environment;
		var applied = Basis.FromEuler(env.SkyRotation).Transposed();
		Check(applied.X.DistanceTo(sky.Rotation.X) < 0.01f && applied.Z.DistanceTo(sky.Rotation.Z) < 0.01f, $"Himmel: {what} — Umgebung trägt die Drehung");
		var cam = _m.TestCam;
		var b = cam.GlobalBasis;
		var vp = _m.GetViewport().GetVisibleRect().Size;
		float aspect = vp.Y > 0f ? vp.X / vp.Y : 16f / 9f;
		float t = Mathf.Tan(Mathf.DegToRad(cam.Fov * 0.5f)), cosR = Mathf.Cos(Mathf.DegToRad(e.Radius));
		const int W = 96, H = 48;
		int n = 0; float sx = 0f, sy = 0f, maxY = 0f;
		for (int j = 0; j < H; j++)
			for (int i = 0; i < W; i++)
			{
				float uu = (i + 0.5f) / W, vv = (j + 0.5f) / H;
				var r = (b * new Vector3((2f * uu - 1f) * t * aspect, (1f - 2f * vv) * t, -1f)).Normalized();
				if (r.Dot(c) < cosR) continue;
				n++; sx += uu; sy += vv; maxY = Mathf.Max(maxY, vv);
			}
		float share = (float)n / (W * H), cx = n > 0 ? sx / n : -1f, cy = n > 0 ? sy / n : 1f;
		Check(share >= 0.05f && cy < 0.3f && maxY < 0.6f, $"Himmel: {sec.Name} Rückansicht {what}: Planet oben im Bild (Anteil {share:P0}, Schwerpunkt x {cx:0.00} y {cy:0.00}, unterster Punkt {maxY:0.00})");
		float gap = Mathf.RadToDeg((_m.TestShipViewPos - cam.GlobalPosition).Normalized().AngleTo(c)) - e.Radius;
		Check(gap >= 5f, $"Himmel: {sec.Name} Rückansicht {what}: Schiff außerhalb der Scheibe (Abstand zum Rand {gap:0.0}°)");
		return (share, cx, cy);
	}

	/// <summary>
	/// Lage der Planetenscheibe (Mitte <paramref name="c"/>, Winkelradius <paramref name="radiusDeg"/>) im Bild einer Kamera mit Blick nach -Z, Neigung
	/// <paramref name="pitchDeg"/> nach unten: Bildanteil, mittleres x und y (0..1) und unterstes y (0 oben, 1 unten) der Scheibenpunkte (Raster 96 × 48).
	/// </summary>
	static (float Share, float CenterX, float CenterY, float MaxY) DiscOnScreen(Vector3 c, float radiusDeg, float pitchDeg, float fovDeg, float aspect)
	{
		var f = SkyboxView.WorldDir(0f, -pitchDeg);
		var z = -f; var x = Vector3.Up.Cross(z).Normalized(); var y = z.Cross(x);
		float t = Mathf.Tan(Mathf.DegToRad(fovDeg * 0.5f)), cosR = Mathf.Cos(Mathf.DegToRad(radiusDeg));
		const int W = 96, H = 48;
		int n = 0; float sx = 0f, sy = 0f, maxY = 0f;
		for (int j = 0; j < H; j++)
			for (int i = 0; i < W; i++)
			{
				float u = (i + 0.5f) / W, v = (j + 0.5f) / H;
				var r = ((2f * u - 1f) * t * aspect * x + (1f - 2f * v) * t * y - z).Normalized();
				if (r.Dot(c) < cosR) continue;
				n++; sx += u; sy += v; maxY = Mathf.Max(maxY, v);
			}
		return n == 0 ? (0f, -1f, 1f, 1f) : ((float)n / (W * H), sx / n, sy / n, maxY);
	}
}

