using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace SpaceAdventure.World;

/// <summary>
/// Runde 11: eine sichtbare Geschützstellung (Hardpoint) aus <c>ships.json</c> bzw. <c>stations.json</c> — Mündung <see cref="Pos"/> und Grund-Schussrichtung
/// <see cref="Dir"/> im Modellraum (Schiffe: Bug −Z, Halblänge 1; Stationen: Radius 1), bei Türmen der Node <c>Turret_&lt;slot&gt;</c> (Drehachse lokal +Y, Rohr in Ruhe −Z).
/// </summary>
public sealed record HardpointDef(int Index, string Kind, Vector3 Pos, Vector3 Dir, string? Node, bool Empty);

/// <summary>Daten eines Schiffsmodells aus <c>ships.json</c> (nur die Felder, die das Spiel braucht).</summary>
public sealed record ShipModelInfo(string Key, string Size, float Scale, List<HardpointDef> Hardpoints, List<string> Rotors, List<string> Jaws);

/// <summary>
/// Gebundene Stellungen einer Modell-Instanz: löst die Turm-Nodes auf, rechnet die Mündung in das Koordinatensystem des Turms um (dreht der Turm, wandert sie
/// mit) und richtet Türme aus. Nur Darstellung — welche Waffe feuert, entscheidet der Core (Slot im Ereignis).
/// </summary>
public sealed class HardpointSet
{
	sealed class Bound
	{
		public HardpointDef Def = null!;
		public Node3D Anchor = null!;        // Turm-Node oder Modellwurzel
		public Vector3 Local;                // Mündung im Raum des Ankers
		public Vector3 LocalDir;             // Schussrichtung im Raum des Ankers
		public Basis Rest;                   // Ruhelage des Turms (Basis relativ zum Elternknoten)
		public float Angle;                  // aktuelle Drehung um die Turmachse
		public bool Turret;
	}

	readonly Dictionary<int, Bound> _byIndex = new();

	/// <summary>Zahl der gebundenen Stellungen.</summary>
	public int Count => _byIndex.Count;
	/// <summary>Zahl der Stellungen mit drehbarem Turm-Node.</summary>
	public int TurretCount { get { int n = 0; foreach (var b in _byIndex.Values) if (b.Turret) n++; return n; } }

	/// <summary>Stellungen an ein instanziertes Modell (<paramref name="model"/> = Szenenwurzel, Modellraum = ihr lokaler Raum) binden.</summary>
	public static HardpointSet Bind(Node3D model, IEnumerable<HardpointDef> defs)
	{
		var set = new HardpointSet();
		foreach (var d in defs)
		{
			var b = new Bound { Def = d, Anchor = model, Local = d.Pos, LocalDir = d.Dir };
			if (d.Node is { Length: > 0 } name && Find(model, name, Transform3D.Identity, out var node, out var rest) && node is not null)
			{
				// Mündung in den Turmraum: Ruhetransform des Turms im Modellraum invertieren
				var inv = rest.AffineInverse();
				b.Anchor = node; b.Local = inv * d.Pos; b.LocalDir = (inv.Basis * d.Dir).Normalized();
				b.Rest = node.Transform.Basis; b.Turret = true;
			}
			set._byIndex[d.Index] = b;
		}
		return set;
	}

	static bool Find(Node n, string name, Transform3D xf, out Node3D? found, out Transform3D at)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is not Node3D c3) continue;
			var t = xf * c3.Transform;
			if (c3.Name == name) { found = c3; at = t; return true; }
			if (Find(c3, name, t, out found, out at)) return true;
		}
		found = null; at = Transform3D.Identity;
		return false;
	}

	/// <summary>Gibt es eine Stellung für diesen Slot/dieses Geschütz?</summary>
	public bool Has(int index) => _byIndex.ContainsKey(index);

	/// <summary>Mündung in Weltkoordinaten (Knoten muss im Baum sein); null ohne Stellung.</summary>
	public Vector3? Muzzle(int index) => _byIndex.TryGetValue(index, out var b) && b.Anchor.IsInsideTree() ? b.Anchor.GlobalTransform * b.Local : null;

	/// <summary>Grund-Schussrichtung in Weltkoordinaten (Raketenschächte zeigen nach oben); null ohne Stellung.</summary>
	public Vector3? Direction(int index) => _byIndex.TryGetValue(index, out var b) && b.Anchor.IsInsideTree() ? (b.Anchor.GlobalTransform.Basis * b.LocalDir).Normalized() : null;

	/// <summary>Turm <paramref name="index"/> zum Ziel drehen (weich mit <paramref name="rate"/> rad/s; rate ≤ 0 = sofort). Starre Rohre und Schächte bleiben.</summary>
	public void Aim(int index, Vector3 worldTarget, float delta, float rate = 6f)
	{
		if (!_byIndex.TryGetValue(index, out var b) || !b.Turret || !b.Anchor.IsInsideTree() || b.Anchor.GetParent() is not Node3D parent) return;
		var local = parent.GlobalTransform.AffineInverse() * worldTarget - b.Anchor.Transform.Origin;
		var axis = b.Rest.Y.Normalized();
		var flat = local - axis * local.Dot(axis);
		var fwd = -b.Rest.Z.Normalized();
		fwd -= axis * fwd.Dot(axis);
		if (flat.LengthSquared() < 1e-6f || fwd.LengthSquared() < 1e-6f) return;
		flat = flat.Normalized(); fwd = fwd.Normalized();
		float want = Mathf.Atan2(axis.Dot(fwd.Cross(flat)), fwd.Dot(flat));
		b.Angle = rate <= 0f ? want : Mathf.LerpAngle(b.Angle, want, 1f - Mathf.Exp(-rate * delta));
		b.Anchor.Basis = new Basis(axis, b.Angle) * b.Rest;
	}

	/// <summary>Alle Türme zum Ziel drehen.</summary>
	public void AimAll(Vector3 worldTarget, float delta, float rate = 6f)
	{
		foreach (var i in _byIndex.Keys) Aim(i, worldTarget, delta, rate);
	}

	/// <summary>Indizes aller Stellungen (Selbsttest).</summary>
	public IEnumerable<int> Indices => _byIndex.Keys;

	/// <summary>Stellungsdaten (Selbsttest).</summary>
	public HardpointDef? Def(int index) => _byIndex.TryGetValue(index, out var b) ? b.Def : null;

	/// <summary>Liest eine Stellungsliste aus JSON (<paramref name="indexField"/> = „slot“ bei Schiffen, „gun“ bei Stationen); fehlt sie, leer.</summary>
	public static List<HardpointDef> Parse(JsonElement owner, string indexField)
	{
		var list = new List<HardpointDef>();
		if (!owner.TryGetProperty("hardpoints", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
		foreach (var h in arr.EnumerateArray())
		{
			if (!h.TryGetProperty(indexField, out var idx) || idx.ValueKind != JsonValueKind.Number) continue;
			list.Add(new HardpointDef(idx.GetInt32(),
				h.TryGetProperty("kind", out var k) ? k.GetString() ?? "gun" : "gun",
				Vec(h, "pos", Vector3.Zero), Vec(h, "dir", new Vector3(0, 0, -1)),
				h.TryGetProperty("node", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
				h.TryGetProperty("empty", out var e) && e.ValueKind == JsonValueKind.True));
		}
		return list;
	}

	static Vector3 Vec(JsonElement h, string name, Vector3 fallback)
	{
		if (!h.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Array || a.GetArrayLength() < 3) return fallback;
		return new Vector3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
	}
}

/// <summary>Eine Instanz eines Schiffsmodells in der Szene (Spieler oder NPC-Marker).</summary>
public sealed class ShipVisual
{
	public Node3D Root = null!;            // skalierte Szenenwurzel (Bug −Z)
	public ShipModelInfo Info = null!;
	public HardpointSet Hardpoints = new();
	public readonly List<Node3D> Rotors = new();
	public readonly List<Node3D> Jaws = new();
	internal readonly List<(MeshInstance3D Mesh, int Surface, Material Source)> Engines = new();
	internal readonly List<(MeshInstance3D Mesh, int Surface, Material Source)> Accents = new();
	int _engineLevel = -1;
	Color _accent;
	bool _accentSet;
	float _accentEnergy = 0.35f;
	readonly List<Basis> _jawRest = new();
	float _jawTime = -1f;
	/// <summary>Dauer einer Backenbewegung (Spalten) in Sekunden und größter Öffnungswinkel.</summary>
	public const float JawSeconds = 0.5f, JawAngle = 0.55f;
	/// <summary>Selbsttest: aktueller Öffnungswinkel der Spaltbacken (0 = Ruhe).</summary>
	public float JawOpen { get; private set; }
	/// <summary>Aktueller Drehwinkel der Werkzeuge (Selbsttest: wächst bei Kontaktabbau).</summary>
	public float RotorAngle { get; private set; }

	/// <summary>Schubleuchten nach Tempoanteil 0..1 (in fünf Stufen, damit die Überschreibungen geteilt bleiben).</summary>
	public void SetEngine(float share)
	{
		int level = Math.Clamp((int)MathF.Round(Math.Clamp(share, 0f, 1f) * (ShipModels.EngineLevels - 1)), 0, ShipModels.EngineLevels - 1);
		if (level == _engineLevel) return;
		_engineLevel = level;
		foreach (var (mi, i, src) in Engines) mi.SetSurfaceOverrideMaterial(i, ShipModels.EngineVariant(src, level));
	}

	/// <summary>Akzent in der Besitzerfarbe (<paramref name="energy"/> = Eigenleuchten; das Spielerschiff leuchtet kräftiger).</summary>
	public void SetAccent(Color c, float energy = 0.35f)
	{
		if (_accentSet && c == _accent && energy == _accentEnergy) return;
		_accent = c; _accentSet = true; _accentEnergy = energy;
		foreach (var (mi, i, src) in Accents) mi.SetSurfaceOverrideMaterial(i, ShipModels.AccentVariant(src, c, energy));
	}

	/// <summary>Runde 12: Spaltbacken (<c>Jaw_*</c>) einmal auf- und zuklappen (beim Spalten).</summary>
	public void SnapJaws()
	{
		if (Jaws.Count == 0) return;
		if (_jawRest.Count != Jaws.Count) { _jawRest.Clear(); foreach (var j in Jaws) _jawRest.Add(j.Transform.Basis); }
		_jawTime = 0f;
	}

	/// <summary>Backenbewegung weiterführen (je Bild); Paare klappen gegenläufig um ihre lokale Z-Achse.</summary>
	public void AnimateJaws(float delta)
	{
		if (_jawTime < 0f || Jaws.Count == 0 || _jawRest.Count != Jaws.Count) return;
		_jawTime += delta;
		float t = Mathf.Clamp(_jawTime / JawSeconds, 0f, 1f);
		JawOpen = Mathf.Sin(t * Mathf.Pi) * JawAngle;
		for (int i = 0; i < Jaws.Count; i++)
		{
			float sign = Jaws[i].Name.ToString().EndsWith("_L") ? -1f : 1f;
			Jaws[i].Basis = _jawRest[i] * new Basis(Vector3.Back, sign * JawOpen);
		}
		if (t >= 1f) { _jawTime = -1f; JawOpen = 0f; }
	}

	/// <summary>Werkzeuge drehen (<paramref name="spin"/> 0..1 wie <c>ContactMining.Spin</c>; Drehachse lokal Z).</summary>
	public void Spin(float spin, float delta)
	{
		if (Rotors.Count == 0 || spin <= 0f) return;
		float step = spin * ShipModels.RotorSpeed * delta;
		RotorAngle += step;
		foreach (var r in Rotors) r.RotateObjectLocal(Vector3.Back, step);
	}
}

/// <summary>
/// Runde 11: Schiffsmodelle aus <c>res://assets/ships/&lt;modelKey&gt;.glb</c> (Blender-Export, <c>tools/blender/README.md</c>): Bug −Z, Halblänge 1, Rumpf-Node
/// <c>Hull</c>, Werkzeuge <c>Rotor_*</c>/<c>Jaw_*</c>, Türme <c>Turret_&lt;slot&gt;</c>. Maßstab = <see cref="BaseHalfLength"/> × Größenfaktor aus <c>ships.json</c>
/// (S 1,0 · M 1,5 · L 2,4 · XL 3,6). Material <c>Accent</c> = Besitzerfarbe, <c>Engine</c> = Schub. Szenen und Überschreibungsmaterialien sind geteilt (je
/// Modell bzw. je (Material, Farbe/Stufe)), nie je Schiff. Fehlt ein Modell, liefert <see cref="Create"/> null — die Ansicht nimmt den alten Grundkörper.
/// </summary>
public static class ShipModels
{
	public const string Dir = "res://assets/ships/";
	public const string InfoPath = "res://assets/ships/ships.json";
	public const string StationInfoPath = "res://assets/stations/stations.json";
	/// <summary>Halblänge eines S-Schiffs im Spiel (Länge 4,8 ≈ der bisherige Grundkörper; Kollisionskreis bleibt 2,5).</summary>
	public const float BaseHalfLength = 2.4f;
	/// <summary>Stufen der Schubleuchte (geteilte Überschreibungen).</summary>
	public const int EngineLevels = 5;
	/// <summary>Werkzeugdrehung bei vollem Kontaktabbau in rad/s.</summary>
	public const float RotorSpeed = 14f;
	/// <summary>Ab diesem Abstand zur Kamera zeigt ein NPC nur den einfachen Grundkörper (Handy-Leistung).</summary>
	public const float LodDistance = 220f;

	static readonly Dictionary<string, PackedScene?> _scenes = new();
	static readonly Dictionary<(ulong, string), Material> _overrides = new();
	static Dictionary<string, ShipModelInfo>? _info;
	static Dictionary<string, List<HardpointDef>>? _stationGuns;
	static Dictionary<string, float> _sizeScale = new() { ["S"] = 1f, ["M"] = 1.5f, ["L"] = 2.4f, ["XL"] = 3.6f };

	/// <summary>Zahl der geteilten Überschreibungsmaterialien (Selbsttest: wächst nicht je Schiff).</summary>
	public static int OverrideCount => _overrides.Count;

	static Dictionary<string, ShipModelInfo> Info()
	{
		if (_info is not null) return _info;
		_info = new();
		try
		{
			if (!FileAccess.FileExists(InfoPath)) return _info;
			using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(InfoPath));
			var root = doc.RootElement;
			if (root.TryGetProperty("sizeScale", out var ss) && ss.ValueKind == JsonValueKind.Object)
				foreach (var p in ss.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Number) _sizeScale[p.Name] = p.Value.GetSingle();
			if (!root.TryGetProperty("models", out var models)) return _info;
			foreach (var m in models.EnumerateObject())
			{
				var v = m.Value;
				string size = v.TryGetProperty("size", out var sz) ? sz.GetString() ?? "S" : "S";
				float scale = v.TryGetProperty("scale", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetSingle() : _sizeScale.GetValueOrDefault(size, 1f);
				_info[m.Name] = new ShipModelInfo(m.Name, size, scale, HardpointSet.Parse(v, "slot"), Names(v, "rotors"), Names(v, "jaws"));
			}
		}
		catch (Exception ex) { GD.PushWarning($"ships.json nicht lesbar: {ex.Message}"); }
		return _info;
	}

	static List<string> Names(JsonElement v, string field)
	{
		var list = new List<string>();
		if (v.TryGetProperty(field, out var a) && a.ValueKind == JsonValueKind.Array)
			foreach (var e in a.EnumerateArray()) if (e.TryGetProperty("node", out var n) && n.GetString() is { } s) list.Add(s);
		return list;
	}

	/// <summary>Runde 13 (Handy-Test „Strahlen aus der Schiffsmitte?“): wie viele Schiffs- und Stationsmodelle Stellungen aus den Modelldaten haben (Selbsttest-Ausgabe).</summary>
	public static (int Ships, int Stations) HardpointCounts()
	{
		StationGuns("");
		return (Info().Values.Count(i => i.Hardpoints.Count > 0), _stationGuns!.Values.Count(l => l.Count > 0));
	}

	/// <summary>Geschützstellungen eines Stationstyps aus <c>stations.json</c> (leer ohne Eintrag).</summary>
	public static List<HardpointDef> StationGuns(string typeKey)
	{
		if (_stationGuns is null)
		{
			_stationGuns = new();
			try
			{
				if (FileAccess.FileExists(StationInfoPath))
				{
					using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(StationInfoPath));
					if (doc.RootElement.TryGetProperty("models", out var models))
						foreach (var m in models.EnumerateObject()) _stationGuns[m.Name] = HardpointSet.Parse(m.Value, "gun");
				}
			}
			catch (Exception ex) { GD.PushWarning($"stations.json nicht lesbar: {ex.Message}"); }
		}
		return _stationGuns.TryGetValue(typeKey, out var l) ? l : new List<HardpointDef>();
	}

	static PackedScene? Scene(string key)
	{
		if (_scenes.TryGetValue(key, out var s)) return s;
		string path = Dir + key + ".glb";
		s = key.Length > 0 && ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
		_scenes[key] = s;
		return s;
	}

	/// <summary>Gibt es ein Modell für diesen Schlüssel?</summary>
	public static bool Has(string modelKey) => Scene(modelKey) is not null;

	/// <summary>Maßstab (Halblänge im Spiel) eines Modells.</summary>
	public static float ScaleOf(string modelKey) => BaseHalfLength * (Info().TryGetValue(modelKey, out var i) ? i.Scale : 1f);

	/// <summary>Daten aus ships.json (null ohne Eintrag).</summary>
	public static ShipModelInfo? InfoOf(string modelKey) => Info().TryGetValue(modelKey, out var i) ? i : null;

	/// <summary>Instanz des Modells mit Akzentfarbe; null ohne Modell.</summary>
	public static ShipVisual? Create(string modelKey, Color accent)
	{
		if (Scene(modelKey) is not { } scene) return null;
		var info = InfoOf(modelKey) ?? new ShipModelInfo(modelKey, "S", 1f, new(), new(), new());
		var node = scene.Instantiate<Node3D>();
		node.Name = "ShipModel";
		node.Scale = Vector3.One * ScaleOf(modelKey);
		var v = new ShipVisual { Root = node, Info = info };
		void Walk(Node n)
		{
			foreach (var c in n.GetChildren())
			{
				if (c is Node3D c3)
				{
					string name = c3.Name;
					if (name.StartsWith("Rotor_")) v.Rotors.Add(c3);
					else if (name.StartsWith("Jaw_")) v.Jaws.Add(c3);
				}
				if (c is MeshInstance3D mi && mi.Mesh is { } mesh)
				{
					mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;   // Handy: keine Schatten
					for (int i = 0; i < mesh.GetSurfaceCount(); i++)
					{
						if (mesh.SurfaceGetMaterial(i) is not { } src) continue;
						if (src.ResourceName == "Engine") v.Engines.Add((mi, i, src));
						else if (src.ResourceName == StationModels.AccentMaterial) v.Accents.Add((mi, i, src));
					}
				}
				Walk(c);
			}
		}
		Walk(node);
		v.Hardpoints = HardpointSet.Bind(node, info.Hardpoints);
		v.SetAccent(accent);
		v.SetEngine(0.2f);
		return v;
	}

	internal static Material AccentVariant(Material src, Color accent, float energy = 0.35f)
	{
		var key = (src.GetInstanceId(), "a:" + accent.ToHtml(false) + ":" + energy.ToString("0.00"));
		if (_overrides.TryGetValue(key, out var m)) return m;
		m = (Material)src.Duplicate();
		if (m is BaseMaterial3D b)
		{
			var sat = accent.S > 0.2f ? Color.FromHsv(accent.H, Mathf.Max(accent.S, 0.75f), Mathf.Max(accent.V, 0.9f)) : accent;
			b.AlbedoColor = new Color(sat.R, sat.G, sat.B, b.AlbedoColor.A);
			b.EmissionEnabled = true;
			b.Emission = sat;
			b.EmissionEnergyMultiplier = energy;
		}
		_overrides[key] = m;
		return m;
	}

	/// <summary>Runde 12 (Prüfung: „Spieler-Akzent kaum sichtbar“): kräftiges Cyan und starkes Eigenleuchten für das eigene Schiff.</summary>
	public static readonly Color PlayerAccent = new(0f, 0.85f, 1f);
	public const float PlayerAccentEnergy = 1.4f;

	internal static Material EngineVariant(Material src, int level)
	{
		var key = (src.GetInstanceId(), "e:" + level);
		if (_overrides.TryGetValue(key, out var m)) return m;
		m = (Material)src.Duplicate();
		if (m is BaseMaterial3D b)
		{
			float k = level / (float)(EngineLevels - 1);
			b.EmissionEnabled = true;
			if (b.Emission.Luminance < 0.05f) b.Emission = new Color(1f, 0.55f, 0.2f);
			b.EmissionEnergyMultiplier = Mathf.Lerp(0.3f, 4f, k);
		}
		_overrides[key] = m;
		return m;
	}

	/// <summary>Akzentfarbe eines Besitzers: Fraktionsfarbe, Gesetzlose rot, sonst grau (wie die Stationen).</summary>
	public static Color AccentOf(SpaceSim.Core.Model.Universe u, SpaceSim.Core.Model.ActorId owner)
	{
		if (owner == u.PlayerActor) return PlayerAccent;
		if (u.Actors.TryGetValue(owner, out var a))
		{
			if (a.Membership is { } f) return Ui.MapOverlay.FactionColor(f);
			if (a.Kind == SpaceSim.Core.Model.ActorKind.Outlaw) return new Color(1f, 0.28f, 0.22f);
		}
		return new Color(0.62f, 0.64f, 0.68f);
	}
}
