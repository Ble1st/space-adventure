using System.Linq;
using Godot;
using SpaceAdventure.World;
using SpaceSim.Core.Model;

namespace SpaceAdventure;

/// <summary>
/// Selbsttest-Stufe 127 (Himmelskugeln): jeder Sektor der Standardwelt bekommt einen Himmel aus dem Verzeichnis; Planetensektoren einen Himmel mit
/// Planet und <b>keine</b> zusätzliche Planetenkugel, alle anderen einen Himmel ohne Planet; der Hintergrund ist der Himmel (nicht die Farbe); die
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
}
