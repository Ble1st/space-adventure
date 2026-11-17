using System.Collections.Generic;
using Godot;

namespace SpaceAdventure.World;

/// <summary>
/// Oberflächen-Details der Modelle (Schiffe, Stationen, Kleinstationen, Tore): EIN gemeinsamer Satz kachelbarer Texturen aus
/// <c>tools/blender/surface_textures.py</c> für alle Modelle — Plattentöne, Fugen, Nieten, Lüftungsgitter, Schmutz, abgeplatzte Kanten
/// (<c>hull_detail.png</c>, RGB = Helligkeitsfaktor, A = Rauheit), die Normalenkarte dazu (<c>hull_normal.png</c>), Fensterreihen/Positionslichter
/// (<c>hull_lights.png</c>, nur Stationen) und ein Solarzellenraster (<c>panel_cells.png</c>, Material <c>Panel</c>).
/// Die Modelle tragen eine Kasten-Projektion als UV (<c>stationkit.box_uv</c>: 1 UV-Einheit = 1 Modelleinheit); hier wird der Kachelmaßstab je Modell
/// aus seiner Spielgröße gesetzt (eine Kachel = <see cref="ShipTile"/> bzw. <see cref="StationTile"/> Welteinheiten), damit die Plattengröße je Modellart gleich ist.
/// Die Originalmaterialien der importierten Szene werden einmal je Modell verändert (geteilt von allen Instanzen); die Überschreibungen
/// (Fraktionsfarbe, Schub, abgeschaltet) entstehen per <c>Duplicate</c> und erben die Texturen. Modelle ohne UV (alte Exporte) bleiben unverändert.
/// </summary>
public static class SurfaceDetail
{
	public const string Dir = "res://assets/textures/";
	/// <summary>Welteinheiten je Texturkachel auf Schiffen (Platten ≈ 0,4–1,5 Einheiten).</summary>
	public const float ShipTile = 7f;
	/// <summary>Welteinheiten je Texturkachel auf Stationen und Toren (größere Platten, sonst wirkt die Fläche körnig).</summary>
	public const float StationTile = 20f;
	/// <summary>Welteinheiten je Kachel des Solarzellenrasters (8 × 8 Zellen).</summary>
	public const float CellTileWorld = 5f;
	/// <summary>Mittlere lineare Helligkeit von <c>hull_detail.png</c> — die Grundfarbe wird um den Kehrwert angehoben, damit die Modelle nicht dunkler werden.</summary>
	const float DetailMean = 0.83f;
	/// <summary>Mittlerer Rauheitsfaktor (Alpha) von <c>hull_detail.png</c>.</summary>
	const float RoughMean = 0.89f;

	/// <summary>Schalter für Vorher/Nachher-Bilder (<c>--no-surface-detail</c>); aus = Modelle wie vor den Texturen.</summary>
	public static bool Enabled = true;

	/// <summary>Materialien mit Plattenstruktur (Rumpf, Panzer, Akzent, Tor-Rahmen).</summary>
	static readonly HashSet<string> Detailed = new()
	{
		"ShipHull", "ShipPanel", "ShipArmor", "Hull", "HullDark", "Armor", "Steel", "Accent", "Container", "Rust", "Hazard", "Copper", "Gold",
		"Trim", "Dark",
	};
	/// <summary>Materialien, die auf Stationen Fensterreihen tragen.</summary>
	static readonly HashSet<string> Lit = new() { "ShipHull", "ShipPanel", "Hull" };

	static Texture2D? _detail, _normal, _lights, _cells;
	static bool _loaded;
	static readonly HashSet<ulong> _done = new();

	/// <summary>Zahl der bisher mit Details versehenen Materialien (Selbsttest/Messung).</summary>
	public static int MaterialCount => _done.Count;

	static void Load()
	{
		if (_loaded) return;
		_loaded = true;
		Texture2D? T(string f) => ResourceLoader.Exists(Dir + f) ? ResourceLoader.Load<Texture2D>(Dir + f) : null;
		_detail = T("hull_detail.png");
		_normal = T("hull_normal.png");
		_lights = T("hull_lights.png");
		_cells = T("panel_cells.png");
	}

	/// <summary>
	/// Details auf alle Materialien eines frisch instanziierten Modells legen (einmal je Originalmaterial).
	/// <paramref name="worldScale"/> = Welteinheiten je Modelleinheit (Stationsradius, Schiffs-Halblänge, Tormaßstab);
	/// <paramref name="lights"/> = Fensterreihen (Stationen).
	/// </summary>
	public static void Apply(Node root, float worldScale, float tile, bool lights)
	{
		if (!Enabled) return;
		Load();
		if (_detail is null) return;
		Walk(root, worldScale / tile, worldScale / CellTileWorld, lights);
	}

	static void Walk(Node n, float uv, float cellUv, bool lights)
	{
		if (n is MeshInstance3D mi && mi.Mesh is ArrayMesh mesh)
			for (int i = 0; i < mesh.GetSurfaceCount(); i++)
			{
				if (mesh.SurfaceGetMaterial(i) is not BaseMaterial3D m) continue;
				if ((mesh.SurfaceGetFormat(i) & Mesh.ArrayFormat.FormatTexUV) == 0) continue;
				if (!_done.Add(m.GetInstanceId())) continue;
				Dress(m, uv, cellUv, lights);
			}
		foreach (var c in n.GetChildren()) Walk(c, uv, cellUv, lights);
	}

	static void Dress(BaseMaterial3D m, float uv, float cellUv, bool lights)
	{
		string name = m.ResourceName;
		if (name == "Panel" && _cells is not null)
		{
			m.AlbedoTexture = _cells;
			if (m.EmissionEnabled)
			{
				m.EmissionTexture = _cells;
				m.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply;   // Leuchten nur in den Zellen, Stege dunkel
			}
			m.Uv1Scale = Vector3.One * cellUv;
			m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
			return;
		}
		if (!Detailed.Contains(name)) return;
		var c = m.AlbedoColor;
		float k = 0.92f / DetailMean;   // Grundton leicht abgesenkt: die hellen Rümpfe überstrahlten ohne Struktur
		m.AlbedoColor = new Color(Mathf.Min(1f, c.R * k), Mathf.Min(1f, c.G * k), Mathf.Min(1f, c.B * k), c.A);
		m.AlbedoTexture = _detail;
		m.RoughnessTexture = _detail;
		m.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Alpha;
		m.Roughness = Mathf.Min(1f, m.Roughness / RoughMean);
		if (_normal is not null)
		{
			m.NormalEnabled = true;
			m.NormalTexture = _normal;
			m.NormalScale = 1f;
		}
		if (lights && Lit.Contains(name) && _lights is not null && !m.EmissionEnabled)
		{
			m.EmissionEnabled = true;
			m.Emission = Colors.Black;   // Operator „Addieren“: Leuchten = (Farbe + Textur) × Stärke — nur die Textur soll leuchten
			m.EmissionTexture = _lights;
			m.EmissionEnergyMultiplier = 2.4f;
		}
		m.Uv1Scale = Vector3.One * uv;
		m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
	}
}
