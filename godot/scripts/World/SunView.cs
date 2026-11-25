using System;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure.World;

/// <summary>
/// Sonnen (ERKUNDUNG.md „Sonnen“), reine Darstellung: die Sonne des Spielersektors als leuchtende Kugel mit bewegter Oberfläche (unbeleuchteter Shader,
/// Randverdunklung, HDR-Werte für das Leuchten), zwei additive Hüllen (Korona dicht, Atmosphäre zart; Rückseiten mit Fresnel-Abfall, daher ein weicher
/// Kranz um den Körper und ein Farbschleier, wenn die Kamera in der Atmosphäre steht), ein dünner Ring auf der Ebene am Atmosphärenrand (die Gefahrenzone
/// lesbar machen) und ein farbiges Punktlicht in Sonnennähe. Das Hauptlicht der Szene nimmt Farbe und Richtung der Sonne an (Licht fällt von der Sonne
/// auf den Spieler). Mobil billig: vier Meshes, ein Punktlicht ohne Schatten. Baut nur bei Sektor- oder Sonnenwechsel neu.
/// </summary>
public partial class SunView : Node3D
{
	const string SurfaceCode = @"shader_type spatial;
render_mode unshaded, cull_back, fog_disabled;
uniform vec4 core : source_color;
uniform vec4 rim : source_color;
uniform float energy = 1.55;
varying vec3 objpos;
float h3(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float n3(vec3 x) {
	vec3 i = floor(x); vec3 f = fract(x); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(h3(i), h3(i + vec3(1, 0, 0)), f.x), mix(h3(i + vec3(0, 1, 0)), h3(i + vec3(1, 1, 0)), f.x), f.y),
	           mix(mix(h3(i + vec3(0, 0, 1)), h3(i + vec3(1, 0, 1)), f.x), mix(h3(i + vec3(0, 1, 1)), h3(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
void vertex() { objpos = VERTEX; }
void fragment() {
	float t = TIME * 0.06;
	vec3 p = objpos * 5.0;
	float n = n3(p + vec3(t, 0.0, -t)) * 0.55 + n3(p * 2.3 - vec3(0.0, t * 1.7, 0.0)) * 0.3 + n3(p * 6.1 + t * 2.0) * 0.15;
	float mu = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	vec3 c = mix(rim.rgb, core.rgb, pow(mu, 0.45));
	c *= 0.72 + 0.56 * n;
	ALBEDO = c * energy;
}";

	const string ShellCode = @"shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_front, shadows_disabled, fog_disabled;
uniform vec4 tint : source_color;
uniform float power = 2.0;
uniform float strength = 1.0;
void fragment() {
	float mu = abs(dot(NORMAL, VIEW));
	float wave = 0.92 + 0.08 * sin(TIME * 0.7 + mu * 9.0);
	ALBEDO = tint.rgb * pow(mu, power) * strength * wave;
}";

	static Shader? s_surface, s_shell;
	static StandardMaterial3D? s_ringMat;

	readonly Func<Universe?> _universe;
	readonly DirectionalLight3D? _mainLight;
	readonly Color _mainColor;
	readonly Vector3 _mainRot;
	SectorId _sector;
	Sun? _sun;
	bool _built;
	MeshInstance3D? _body, _corona, _atmosphere, _ring;
	OmniLight3D? _light;

	/// <summary>Selbsttest: Sonne sichtbar, Radien der Meshes, Hüllen, Licht.</summary>
	public bool TestVisible => _body is { } b && b.Visible && IsInstanceValid(b);
	public float TestBodyRadius => _body?.Scale.X ?? 0f;
	public float TestCoronaRadius => _corona?.Scale.X ?? 0f;
	public float TestAtmosphereRadius => _atmosphere?.Scale.X ?? 0f;
	public float TestLightEnergy => _light?.LightEnergy ?? 0f;
	public Color TestMainLightColor => _mainLight?.LightColor ?? Colors.White;

	public SunView(Func<Universe?> universe, DirectionalLight3D? mainLight)
	{
		_universe = universe;
		_mainLight = mainLight;
		_mainColor = mainLight?.LightColor ?? Colors.White;
		_mainRot = mainLight?.RotationDegrees ?? new Vector3(-50, 30, 0);
	}

	public override void _Process(double delta)
	{
		var u = _universe();
		if (u is null || !u.Sectors.ContainsKey(u.PlayerSector)) return;
		var sun = Suns.Of(u, u.PlayerSector);
		if (!_built || u.PlayerSector != _sector || !ReferenceEquals(sun, _sun)) Apply(u.PlayerSector, sun);
		AimMainLight(u, sun);
	}

	static Color Parse(string hex) => Color.FromString(hex, new Color(1f, 0.9f, 0.65f));

	void Apply(SectorId sector, Sun? sun)
	{
		_sector = sector; _sun = sun; _built = true;
		foreach (var n in new Node?[] { _body, _corona, _atmosphere, _ring, _light }) n?.QueueFree();
		_body = _corona = _atmosphere = _ring = null; _light = null;
		var u = _universe();
		if (sun is null || u is null) return;
		var r = u.Rules;
		var col = Parse(sun.Color);
		var pos = new Vector3(sun.Position.X, 0f, sun.Position.Y);
		s_surface ??= new Shader { Code = SurfaceCode };
		s_shell ??= new Shader { Code = ShellCode };
		var bodyMat = new ShaderMaterial { Shader = s_surface };
		bodyMat.SetShaderParameter("core", col.Lightened(0.3f));
		bodyMat.SetShaderParameter("rim", col.Darkened(0.45f));
		_body = Sphere(bodyMat, pos, sun.Radius, 48, 24);
		_corona = Sphere(Shell(col, 1.2f, 1.5f), pos, Suns.CoronaRadius(r, sun), 40, 20);
		_atmosphere = Sphere(Shell(col, 2.6f, 0.32f), pos, Suns.AtmosphereRadius(r, sun), 40, 20);
		s_ringMat ??= new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, DisableFog = true,
			AlbedoColor = new Color(1f, 0.6f, 0.3f, 0.4f),
		};
		float atm = Suns.AtmosphereRadius(r, sun);
		_ring = new MeshInstance3D
		{
			Mesh = new TorusMesh { InnerRadius = 0.996f, OuterRadius = 1f, Rings = 96, RingSegments = 4 },
			MaterialOverride = s_ringMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = pos + new Vector3(0f, -1f, 0f), Scale = new Vector3(atm, 1f, atm),
		};
		AddChild(_ring);
		_light = new OmniLight3D
		{
			Position = pos + new Vector3(0f, sun.Radius * 0.6f, 0f), LightColor = col, LightEnergy = 2.2f, OmniRange = atm * 2.6f, OmniAttenuation = 1.2f,
			ShadowEnabled = false,
		};
		AddChild(_light);
	}

	static ShaderMaterial Shell(Color col, float power, float strength)
	{
		var m = new ShaderMaterial { Shader = s_shell };
		m.SetShaderParameter("tint", col);
		m.SetShaderParameter("power", power);
		m.SetShaderParameter("strength", strength);
		return m;
	}

	MeshInstance3D Sphere(Material mat, Vector3 pos, float radius, int seg, int rings)
	{
		var m = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = seg, Rings = rings },
			MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = pos, Scale = new Vector3(radius, radius, radius),
		};
		AddChild(m);
		return m;
	}

	/// <summary>Hauptlicht: fällt von der Sonne auf den Spieler (gleiche Neigung wie zuvor), Farbe zur Sonne hin; ohne Sonne die alten Werte.</summary>
	void AimMainLight(Universe u, Sun? sun)
	{
		if (_mainLight is null) return;
		if (sun is null || !u.Ships.TryGetValue(u.PlayerShip, out var me))
		{
			if (_mainLight.LightColor != _mainColor) _mainLight.LightColor = _mainColor;
			if (_mainLight.RotationDegrees != _mainRot) _mainLight.RotationDegrees = _mainRot;
			return;
		}
		float dx = me.Position.X - sun.Position.X, dz = me.Position.Y - sun.Position.Y;
		float yaw = Mathf.RadToDeg(Mathf.Atan2(-dx, -dz));
		var rot = new Vector3(_mainRot.X, yaw, 0f);
		if (Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(_mainLight.RotationDegrees.Y), Mathf.DegToRad(yaw))) > 0.005f) _mainLight.RotationDegrees = rot;
		var tint = _mainColor.Lerp(Parse(sun.Color), 0.4f);
		if (_mainLight.LightColor != tint) _mainLight.LightColor = tint;
	}
}
