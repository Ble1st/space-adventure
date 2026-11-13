using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure.World;

/// <summary>
/// Jumpgate-Modelle: lädt <c>res://assets/gates/gate.glb</c> (Tunnel mit seitlicher Zollstation) und <c>gate_destroyed.glb</c> (Trümmer) einmal und
/// instanziert sie je Tor (Blender-Export aus <c>tools/blender/gate_model.py</c>; Öffnungsradius 1, Ursprung Mitte der Öffnung, Durchflug entlang Z,
/// Station bei +X, siehe <c>gate.json</c>). Der Zustand färbt das Tor ein: Feld (<c>Field</c>) über den Shader <c>shaders/gate_field.gdshader</c>
/// (offen blau pulsierend, eingeschränkt orange Warnstreifen, für dich gesperrt dichter/röter, geschlossen nur ein schwaches rotes Sperrgitter),
/// Kanten (<c>Glow</c>) und Statuslichter (<c>Signal</c>) nach den Werten aus gate.json, <c>Accent</c> in der Farbe der Sektorfraktion; zerstört =
/// Trümmermodell mit Glut/Funken (<c>shaders/gate_ember.gdshader</c>). Mesh und Originalmaterialien bleiben geteilt; Überschreibungen sind je
/// (Originalmaterial, Variante) einmal im Cache, nie je Tor. Fehlt das Modell, liefert <see cref="Create"/> null (alter Grundkörper).
/// </summary>
public static class GateModels
{
	public const string IntactPath = "res://assets/gates/gate.glb";
	public const string DestroyedPath = "res://assets/gates/gate_destroyed.glb";
	public const string FieldShaderPath = "res://shaders/gate_field.gdshader";
	public const string EmberShaderPath = "res://shaders/gate_ember.gdshader";

	/// <summary>
	/// Öffnungsradius im Spiel: der Sprung löst innerhalb von 10 Einheiten um die Tormitte aus (Main.GateRadius), der alte Ring hatte innen 12.
	/// 10,5 liegt nahe beim Sprungradius (±10 %); dabei bleibt die seitliche Zollstation (größter Ebenenradius ≈ 2,94 × 10,5 ≈ 31) innerhalb des Freiraums, den die
	/// Asteroiden um ein Tor halten (BeltPlacement.GateClear 35), und weit weg von Stationen (mindestens 50 Abstand).
	/// </summary>
	public const float Scale = 10.5f;

	public enum Look { Open, Restricted, Denied, Closed, Destroyed }

	static PackedScene? _intact, _destroyed;
	static bool _loaded;
	static Shader? _fieldShader, _emberShader;
	static readonly Dictionary<(ulong Material, string Variant), Material> _overrides = new();
	static float _fieldRadius = -1f;

	static void Load()
	{
		if (_loaded) return;
		_loaded = true;
		_intact = ResourceLoader.Exists(IntactPath) ? ResourceLoader.Load<PackedScene>(IntactPath) : null;
		_destroyed = ResourceLoader.Exists(DestroyedPath) ? ResourceLoader.Load<PackedScene>(DestroyedPath) : null;
		_fieldShader = ResourceLoader.Exists(FieldShaderPath) ? ResourceLoader.Load<Shader>(FieldShaderPath) : null;
		_emberShader = ResourceLoader.Exists(EmberShaderPath) ? ResourceLoader.Load<Shader>(EmberShaderPath) : null;
	}

	/// <summary>Gibt es das Tormodell?</summary>
	public static bool Has { get { Load(); return _intact is not null; } }

	/// <summary>Anzeigezustand eines Tors aus den Core-Daten: physischer Zustand, bei offenen Toren zusätzlich „für dich gesperrt“ (nur lesen).</summary>
	public static Look LookOf(Universe u, Gate g)
	{
		switch (g.State)
		{
			case GateState.Destroyed: return Look.Destroyed;
			case GateState.Closed: return Look.Closed;
		}
		if (u.Ships.TryGetValue(u.PlayerShip, out var me) && AccessInfo.ForGate(u, g, me).Denied) return Look.Denied;
		return g.State == GateState.Restricted ? Look.Restricted : Look.Open;
	}

	/// <summary>
	/// Drehung um Y, damit die Durchflugachse (Modell +Z) von der Sektormitte (Schwerpunkt der Stationen, Felder und übrigen Tore) zum Tor zeigt — von dort
	/// fliegen Schiffe an und dorthin verlassen sie das Tor; die Zollstation steht seitlich davon.
	/// </summary>
	public static float YawOf(Universe u, Gate g)
	{
		var s = u.Sectors[g.Sector];
		var pts = s.Stations.Where(u.Stations.ContainsKey).Select(id => u.Stations[id].Position)
			.Concat(s.Fields.Select(f => f.Position))
			.Concat(s.Gates.Where(id => id != g.Id && u.Gates.ContainsKey(id)).Select(id => u.Gates[id].Position)).ToList();
		var c = pts.Count == 0 ? new Vec2(0f, 0f) : pts.Aggregate(new Vec2(0f, 0f), (a, b) => a + b) * (1f / pts.Count);
		float dx = g.Position.X - c.X, dz = g.Position.Y - c.Y;
		if (dx * dx + dz * dz < 1e-4f) { dx = g.Position.X; dz = g.Position.Y; }
		if (dx * dx + dz * dz < 1e-4f) return 0f;
		return Mathf.Atan2(dx, dz);   // Modell +Z → (sin, 0, cos)
	}

	/// <summary>Modell im Zustand <paramref name="look"/> (zerstört: Trümmer), skaliert auf <see cref="Scale"/>; null ohne Modell.</summary>
	public static Node3D? Create(Look look, Color accent, out float top)
	{
		Load();
		top = 8f;
		var scene = look == Look.Destroyed ? (_destroyed ?? _intact) : _intact;
		if (scene is null) return null;
		var node = scene.Instantiate<Node3D>();
		node.Name = look == Look.Destroyed ? "GateWreck" : "GateModel";
		node.Scale = Vector3.One * Scale;
		float maxY = 0f;
		foreach (var (mi, xf) in Meshes(node, Transform3D.Identity))
		{
			mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			var box = xf * mi.GetAabb();
			maxY = Mathf.Max(maxY, box.End.Y);
		}
		top = maxY * Scale;
		Apply(node, look, accent);
		return node;
	}

	/// <summary>Einfärbung (neu) anwenden; der Wechsel zwischen heil und zerstört tauscht das Modell (siehe SectorView.SyncGates).</summary>
	public static void Apply(Node3D model, Look look, Color accent)
	{
		foreach (var (mi, _) in Meshes(model, Transform3D.Identity))
		{
			var mesh = mi.Mesh;
			if (mesh is null) continue;
			for (int i = 0; i < mesh.GetSurfaceCount(); i++)
			{
				var src = mesh.SurfaceGetMaterial(i);
				if (src is null) { mi.SetSurfaceOverrideMaterial(i, null); continue; }
				string name = src.ResourceName;
				string? variant = name switch
				{
					"Hull" or "Trim" or "Dark" when look == Look.Destroyed => "burnt",
					"Field" => $"field:{look}",
					"Glow" or "Signal" => $"{name}:{look}",
					"Accent" => $"accent:{look}:{accent.ToHtml(false)}",
					_ => null,
				};
				if (variant is null) { mi.SetSurfaceOverrideMaterial(i, null); continue; }
				var key = (src.GetInstanceId(), variant);
				if (!_overrides.TryGetValue(key, out var m))
				{
					m = Variant(src, name, look, accent, mesh);
					_overrides[key] = m;
				}
				mi.SetSurfaceOverrideMaterial(i, m);
			}
		}
	}

	/// <summary>Zahl der geteilten Überschreibungsmaterialien (Selbsttest: wächst nicht je Tor).</summary>
	public static int OverrideCount => _overrides.Count;

	/// <summary>Selbsttest: Öffnungsradius eines Modells in Weltmaßen = größter Abstand der Feldfläche von der Durchflugachse (null ohne Feld).</summary>
	public static float? OpeningRadius(Node3D model)
	{
		float r = -1f;
		foreach (var (mi, xf) in Meshes(model, Transform3D.Identity))
		{
			if (mi.Mesh is not ArrayMesh am) continue;
			for (int i = 0; i < am.GetSurfaceCount(); i++)
			{
				if (am.SurfaceGetMaterial(i)?.ResourceName != "Field") continue;
				var verts = (Vector3[])am.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Vertex];
				foreach (var v in verts) { var w = xf * v; r = Mathf.Max(r, new Vector2(w.X, w.Y).Length()); }
			}
		}
		return r < 0f ? null : r * model.Scale.X;
	}

	/// <summary>Selbsttest: Shader-Modus des Felds (0 offen, 1 eingeschränkt, 2 geschlossen; null = kein Feld/kein Shader) und Glow-Emissionsfarbe.</summary>
	public static (int? FieldMode, float Deny, Color? Glow, bool Ember) Probe(Node3D model)
	{
		int? mode = null; float deny = 0f; Color? glow = null; bool ember = false;
		foreach (var (mi, _) in Meshes(model, Transform3D.Identity))
			for (int i = 0; mi.Mesh is not null && i < mi.Mesh.GetSurfaceCount(); i++)
			{
				var src = mi.Mesh.SurfaceGetMaterial(i);
				var o = mi.GetSurfaceOverrideMaterial(i);
				if (src?.ResourceName == "Field" && o is ShaderMaterial sf) { mode = sf.GetShaderParameter("mode").AsInt32(); deny = sf.GetShaderParameter("deny").AsSingle(); }
				if (src?.ResourceName == "Glow" && o is StandardMaterial3D g) glow = g.Emission;
				if (src?.ResourceName is "Glow" or "Signal" && o is ShaderMaterial se && se.Shader == _emberShader) ember = true;
			}
		return (mode, deny, glow, ember);
	}

	// Farbwerte aus gate.json (Emissionsfarbe, Stärke); die Stärke ist im Spiel gedämpft (Godot-Glow ist an)
	const float EnergyScale = 0.45f;
	static (Color Color, float Energy) GlowOf(Look look) => look switch
	{
		Look.Open => (new Color(0.25f, 0.85f, 1f), 7f),
		Look.Restricted or Look.Denied => (new Color(1f, 0.55f, 0.1f), 6f),
		_ => (new Color(0.5f, 0.04f, 0.03f), 1.5f),
	};
	static (Color Color, float Energy) SignalOf(Look look) => look switch
	{
		Look.Open => (new Color(0.25f, 1f, 0.45f), 6f),
		Look.Restricted => (new Color(1f, 0.5f, 0.05f), 6f),
		Look.Denied => (new Color(1f, 0.2f, 0.05f), 6f),
		_ => (new Color(1f, 0.08f, 0.04f), 3f),
	};

	static Material Variant(Material src, string name, Look look, Color accent, Mesh mesh)
	{
		if (name == "Field" && _fieldShader is not null)
		{
			var f = new ShaderMaterial { Shader = _fieldShader, ResourceName = "Field" };
			var (inner, rim, energy, mode) = look switch
			{
				Look.Open => (new Color(0.35f, 0.9f, 1f), new Color(0.25f, 0.3f, 1f), 1.6f, 0),
				Look.Restricted => (new Color(1f, 0.8f, 0.3f), new Color(1f, 0.35f, 0.05f), 1.5f, 1),
				Look.Denied => (new Color(1f, 0.6f, 0.2f), new Color(1f, 0.2f, 0.04f), 1.5f, 1),
				_ => (new Color(0.9f, 0.08f, 0.05f), new Color(0.7f, 0.04f, 0.03f), 0.5f, 2),
			};
			f.SetShaderParameter("field_color", inner);
			f.SetShaderParameter("rim_color", rim);
			f.SetShaderParameter("intensity", energy);
			f.SetShaderParameter("mode", mode);
			f.SetShaderParameter("deny", look == Look.Denied ? 1f : 0f);
			f.SetShaderParameter("radius", FieldRadius(mesh));
			return f;
		}
		if (look == Look.Destroyed && name is "Glow" or "Signal" && _emberShader is not null)
		{
			var e = new ShaderMaterial { Shader = _emberShader, ResourceName = name };
			e.SetShaderParameter("glow", name == "Signal" ? 0.9f : 0.5f);
			e.SetShaderParameter("sparks", name == "Signal" ? 5f : 8f);
			return e;
		}
		var m = (Material)src.Duplicate();
		if (m is BaseMaterial3D b)
		{
			if (look == Look.Destroyed && name is "Hull" or "Trim" or "Dark")
			{
				// verbrannt: entsättigt und dunkel, kaum Glanz — „defekt“ soll klar anders wirken als „zu“
				var c = b.AlbedoColor;
				float grey = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
				b.AlbedoColor = new Color(grey * 0.32f, grey * 0.29f, grey * 0.27f, c.A);
				b.Metallic = 0f; b.Roughness = 1f; b.EmissionEnabled = false;
			}
			else if (name == "Accent")
			{
				var a = look == Look.Destroyed ? accent.Darkened(0.8f) : accent;
				b.AlbedoColor = new Color(b.AlbedoColor.R * a.R, b.AlbedoColor.G * a.G, b.AlbedoColor.B * a.B, b.AlbedoColor.A);
				if (look == Look.Destroyed) b.EmissionEnabled = false;
			}
			else
			{
				var (c, energy) = name == "Glow" ? GlowOf(look) : SignalOf(look);
				b.AlbedoColor = new Color(c.R, c.G, c.B, b.AlbedoColor.A);
				b.EmissionEnabled = true;
				b.Emission = c;
				b.EmissionEnergyMultiplier = energy * EnergyScale;
			}
		}
		return m;
	}

	/// <summary>Radius der Feldfläche in Modelleinheiten (für den Radialverlauf des Shaders), einmal gemessen.</summary>
	static float FieldRadius(Mesh mesh)
	{
		if (_fieldRadius > 0f) return _fieldRadius;
		float r = 0f;
		if (mesh is ArrayMesh am)
			for (int i = 0; i < am.GetSurfaceCount(); i++)
				if (am.SurfaceGetMaterial(i)?.ResourceName == "Field")
					foreach (var v in (Vector3[])am.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Vertex]) r = Mathf.Max(r, new Vector2(v.X, v.Y).Length());
		_fieldRadius = r > 0.1f ? r : 0.95f;
		return _fieldRadius;
	}

	static IEnumerable<(MeshInstance3D Mesh, Transform3D Xf)> Meshes(Node root, Transform3D xf)
	{
		foreach (var c in root.GetChildren())
		{
			if (c is not Node3D c3) continue;
			var t = xf * c3.Transform;
			if (c3 is MeshInstance3D mi) yield return (mi, t);
			foreach (var x in Meshes(c3, t)) yield return x;
		}
	}
}
