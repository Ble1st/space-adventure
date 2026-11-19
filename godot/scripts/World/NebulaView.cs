using System;
using System.Collections.Generic;
using Godot;
using SpaceSim.Core.Model;

namespace SpaceAdventure.World;

/// <summary>
/// Gasernte und Nebel (SCHIFFSSLOTS.md „Gasernte und Nebel“), reine Darstellung: Ein Nebelsektor (<see cref="Sector.Nebula"/>) bekommt sektorweiten
/// violetten Tiefennebel über die Umgebung (Exponentialnebel, auf Mobilgeräten billig, keine Partikel) und färbt den Himmel leicht ein; Gasfelder werden als
/// flache, durchscheinende, additiv gezeichnete Wolkenschichten mit einem dünnen Randring dargestellt (kein Hindernis). Folgt dem Spielersektor, baut nur bei
/// einem Sektorwechsel neu.
/// </summary>
public partial class NebulaView : Node3D
{
	/// <summary>Dichte des Tiefennebels (Exponentialnebel; bei 60 m Kamerastand ≈ 9 %, bei 600 m ≈ 60 %).</summary>
	public const float FogDensity = 0.0015f;
	public static readonly Color FogColor = new(0.42f, 0.28f, 0.62f);
	static readonly Color CloudColor = new(0.62f, 0.42f, 0.92f, 0.09f);

	readonly Godot.Environment _env;
	readonly Func<Universe?> _universe;
	SectorId _sector;
	bool _built;
	readonly List<Node3D> _fields = new();
	static StandardMaterial3D? s_cloud, s_ring;

	public bool FogOn => _env.FogEnabled;
	public int FieldNodes => _fields.Count;

	public NebulaView(Godot.Environment env, Func<Universe?> universe)
	{
		_env = env;
		_universe = universe;
	}

	public override void _Process(double delta)
	{
		var u = _universe();
		if (u is null || !u.Sectors.TryGetValue(u.PlayerSector, out var sec)) return;
		if (_built && sec.Id == _sector && _fields.Count == sec.GasFields.Count * 2) return;
		Apply(sec);
	}

	public void Apply(Sector sec)
	{
		_sector = sec.Id;
		_built = true;
		_env.FogEnabled = sec.Nebula;
		if (sec.Nebula)
		{
			_env.FogMode = Godot.Environment.FogModeEnum.Exponential;
			_env.FogLightColor = FogColor;
			_env.FogLightEnergy = 1f;
			_env.FogDensity = FogDensity;
			_env.FogSkyAffect = 0.35f;
			_env.FogSunScatter = 0f;
		}
		foreach (var n in _fields) n.QueueFree();
		_fields.Clear();
		s_cloud ??= new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoColor = CloudColor,
			NoDepthTest = false, DisableFog = true,
		};
		s_ring ??= new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = new Color(0.75f, 0.55f, 1f, 0.35f), DisableFog = true,
		};
		foreach (var f in sec.GasFields)
		{
			var cloud = new MeshInstance3D
			{
				Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 8 },
				MaterialOverride = s_cloud, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Position = new Vector3(f.Position.X, -6f, f.Position.Y), Scale = new Vector3(f.Radius, f.Radius * 0.06f, f.Radius),
			};
			var ring = new MeshInstance3D
			{
				Mesh = new TorusMesh { InnerRadius = 0.985f, OuterRadius = 1f, Rings = 48, RingSegments = 4 },
				MaterialOverride = s_ring, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Position = new Vector3(f.Position.X, -1f, f.Position.Y), Scale = new Vector3(f.Radius, 1f, f.Radius),
			};
			AddChild(cloud); AddChild(ring);
			_fields.Add(cloud); _fields.Add(ring);
		}
	}
}
