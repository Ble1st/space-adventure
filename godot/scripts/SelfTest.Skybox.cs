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
				var aimed = sky.Rotation * SkyboxView.WorldDir(sec.SkyboxYaw, e.Elevation);
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
			return true;
		}
		return false;
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

