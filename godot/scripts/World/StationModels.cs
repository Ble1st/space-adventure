using System.Collections.Generic;
using Godot;

namespace SpaceAdventure.World;

/// <summary>
/// Stationsmodelle (Runde 8): lädt je Stationstyp die Szene <c>res://assets/stations/&lt;typeKey&gt;.glb</c> (Blender-Export aus <c>tools/blender/</c>: ein Mesh,
/// Ursprung in der Mitte, Radius in der Spielebene genau 1) und färbt sie je Instanz ein — Material <c>Accent</c> mit der Besitzerfarbe, abgedunkelt für
/// verlassene Baustellen und ausgeschaltete Stationen (Emission aus). Mesh und Originalmaterialien werden geteilt; Überschreibungen sind je
/// (Originalmaterial, Variante) einmal im Cache, nie je Station. Fehlt ein Modell (eigene Welten), liefert <see cref="Create"/> null — die Ansicht nimmt
/// dann den alten Grundkörper.
/// </summary>
public static class StationModels
{
	public const string Dir = "res://assets/stations/";
	public const string AccentMaterial = "Accent";

	static readonly Dictionary<string, PackedScene?> _scenes = new();
	static readonly Dictionary<(ulong Material, string Variant), Material> _overrides = new();

	/// <summary>Gibt es ein Modell für diesen Typ?</summary>
	public static bool Has(string typeKey) => Scene(typeKey) is not null;

	static PackedScene? Scene(string typeKey)
	{
		if (_scenes.TryGetValue(typeKey, out var s)) return s;
		string path = Dir + typeKey + ".glb";
		s = ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
		_scenes[typeKey] = s;
		return s;
	}

	/// <summary>Zustand einer Station für die Einfärbung.</summary>
	public enum Look { Normal, Abandoned, Disabled }

	/// <summary>
	/// Modell des Typs, skaliert auf <paramref name="radius"/> (Radius in der Spielebene), Akzent in <paramref name="accent"/>; null ohne Modell.
	/// <paramref name="top"/>/<paramref name="bottom"/> = Höhe der Ober-/Unterkante über/unter der Ebene (für Beschriftung).
	/// </summary>
	public static Node3D? Create(string typeKey, float radius, Color accent, Look look, out float top, out float bottom)
	{
		top = 6f; bottom = -6f;
		if (Scene(typeKey) is not { } scene) return null;
		var node = scene.Instantiate<Node3D>();
		node.Name = "Model";
		node.Scale = Vector3.One * radius;
		var box = new Aabb();
		bool first = true;
		void Walk(Node n, Transform3D xf)
		{
			foreach (var c in n.GetChildren())
			{
				if (c is not Node3D c3) continue;
				var t = xf * c3.Transform;
				if (c3 is MeshInstance3D mi)
				{
					mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
					var aabb = t * mi.GetAabb();
					box = first ? aabb : box.Merge(aabb);
					first = false;
				}
				Walk(c3, t);
			}
		}
		Walk(node, Transform3D.Identity);
		if (!first) { top = box.End.Y * radius; bottom = box.Position.Y * radius; }
		Apply(node, accent, look);
		return node;
	}

	/// <summary>Einfärbung (neu) anwenden — Besitzerwechsel, Ausschalten, Wiedereinschalten.</summary>
	public static void Apply(Node3D model, Color accent, Look look)
	{
		foreach (var mi in Meshes(model))
		{
			var mesh = mi.Mesh;
			if (mesh is null) continue;
			for (int i = 0; i < mesh.GetSurfaceCount(); i++)
			{
				var src = mesh.SurfaceGetMaterial(i);
				if (src is null) { mi.SetSurfaceOverrideMaterial(i, null); continue; }
				bool isAccent = src.ResourceName == AccentMaterial;
				if (look == Look.Normal && !isAccent) { mi.SetSurfaceOverrideMaterial(i, null); continue; }
				string variant = $"{look}:{(isAccent ? accent.ToHtml(false) : "-")}";
				var key = (src.GetInstanceId(), variant);
				if (!_overrides.TryGetValue(key, out var m))
				{
					m = Variant(src, isAccent ? accent : null, look);
					_overrides[key] = m;
				}
				mi.SetSurfaceOverrideMaterial(i, m);
			}
		}
	}

	/// <summary>Zahl der geteilten Überschreibungsmaterialien (Selbsttest: wächst nicht je Station).</summary>
	public static int OverrideCount => _overrides.Count;

	/// <summary>Selbsttest: Akzentfarbe (Überschreibung, Original) der ersten Akzentfläche eines Modells (null = keine Akzentfläche).</summary>
	public static (Color Shown, Color Source)? AccentOf(Node3D model)
	{
		foreach (var mi in Meshes(model))
			for (int i = 0; mi.Mesh is not null && i < mi.Mesh.GetSurfaceCount(); i++)
				if (mi.Mesh.SurfaceGetMaterial(i) is BaseMaterial3D src && src.ResourceName == AccentMaterial && mi.GetSurfaceOverrideMaterial(i) is BaseMaterial3D b)
					return (b.AlbedoColor, src.AlbedoColor);
		return null;
	}

	/// <summary>Selbsttest: größter Abstand eines Eckpunkts von der Mitte in der Spielebene (XZ), in Weltmaßen.</summary>
	public static float PlaneRadius(Node3D model)
	{
		float r = 0f;
		void Walk(Node n, Transform3D xf)
		{
			foreach (var c in n.GetChildren())
			{
				if (c is not Node3D c3) continue;
				var t = xf * c3.Transform;
				if (c3 is MeshInstance3D mi && mi.Mesh is not null)
					foreach (var v in mi.Mesh.GetFaces()) { var w = t * v; r = Mathf.Max(r, new Vector2(w.X, w.Z).Length()); }
				Walk(c3, t);
			}
		}
		Walk(model, Transform3D.Identity);
		return r * model.Scale.X;
	}

	static Material Variant(Material src, Color? accent, Look look)
	{
		var m = (Material)src.Duplicate();
		if (m is BaseMaterial3D b)
		{
			if (accent is { } a)
			{
				// Phase 29 (Handy-Test): kräftiger — gesättigte Fraktionsfarbe statt Multiplikation über das fast weiße Accent, dazu leichte Eigenleuchtkraft
				var sat = a.S > 0.2f ? Color.FromHsv(a.H, Mathf.Max(a.S, 0.75f), Mathf.Max(a.V, 0.9f)) : a;   // grau (herrenlos/frei) bleibt grau
				b.AlbedoColor = new Color(sat.R, sat.G, sat.B, b.AlbedoColor.A);
				b.EmissionEnabled = true;
				b.Emission = sat;
				b.EmissionEnergyMultiplier = 0.35f;
			}
			if (look != Look.Normal)
			{
				// verlassen: entsättigt und dunkel; ausgeschaltet: dunkel, Lichter aus
				var c = b.AlbedoColor;
				float grey = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
				var g = new Color(grey, grey, grey, c.A);
				b.AlbedoColor = (look == Look.Abandoned ? g : c.Lerp(g, 0.5f)).Darkened(look == Look.Abandoned ? 0.45f : 0.55f);
				b.EmissionEnabled = false;
			}
		}
		return m;
	}

	static IEnumerable<MeshInstance3D> Meshes(Node root)
	{
		if (root is MeshInstance3D mi) yield return mi;
		foreach (var c in root.GetChildren())
			foreach (var m in Meshes(c)) yield return m;
	}
}
