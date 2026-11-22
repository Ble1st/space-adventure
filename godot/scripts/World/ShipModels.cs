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
public sealed record ShipModelInfo(string Key, string Size, float Scale, List<HardpointDef> Hardpoints, List<string> Rotors, List<string> Jaws, string Mining = "", float ContactRadius = 0f);

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
	/// <summary>Phase 32: Harpunenspitzen (<c>Harpoon_&lt;n&gt;</c>, Ursprung an der Mündung, lokal −Z = Abschussrichtung) — leer = Trossen vom Bug.</summary>
	public readonly List<Node3D> Harpoons = new();
	/// <summary>Runde 43: Spitze des Kontaktwerkzeugs im Modellraum (Root) — vorderster Punkt (kleinstes Z) aller Rotor-Meshes; (0,0,-1) ohne Werkzeug.</summary>
	public Vector3 TipLocal { get; internal set; } = new(0f, 0f, -1f);
	/// <summary>Runde 43: Abstand Schiffsmitte → Werkzeugspitze in Welteinheiten entlang der Bugachse (TipLocal mal Maßstab).</summary>
	public float TipOffset { get; internal set; }
	/// <summary>Runde 43: Bohrerachsen im Modellraum (Ursprung je Rotor, Radius = contactRadius aus ships.json bzw. Mesh-AABB) — für die Anker-Flankenprüfung.</summary>
	public readonly List<(Vector3 Axis, float Radius)> DrillAxes = new();
	/// <summary>Runde 45 (Gerätetest: zwischen Trosse und Schiff war Luft): Rumpfhülle im Modellraum (Vereinigung der Rumpf-Meshes, ohne Werkzeuge/Werfer).
	/// Der Seilstart der Ankertrossen wird in diese Box geklemmt — das Seil beginnt sichtbar in/am Rumpf, nie frei daneben.</summary>
	public Aabb HullLocal { get; internal set; } = new(new Vector3(-0.5f, -0.3f, -1f), new Vector3(1f, 0.6f, 2f));
	/// <summary>Runde 46 (Gerätetest: Lücke zwischen Ankertrosse und Schiff): Dreiecke der Rumpf-Meshes im Modellraum (Dreiecksliste, je drei Punkte).</summary>
	public Vector3[] HullTris { get; internal set; } = System.Array.Empty<Vector3>();
	readonly Dictionary<(int, Vector3), Vector3> _hullSnap = new();
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

	/// <summary>Lokale Position eines Knotens im Raum der Modellwurzel (Root), ohne Weltbaum.</summary>
	public Vector3 LocalOf(Node3D n)
	{
		var t = n.Transform;
		for (var p = n.GetParent(); p is Node3D p3 && p != Root; p = p.GetParent()) t = p3.Transform * t;
		return t.Origin;
	}

	/// <summary>Runde 43: Punkt <paramref name="local"/> (Modellraum) liegt er im Bohrerbereich (lateral näher als Radius + 0,10 an einer Werkzeugachse)?</summary>
	public bool InsideDrill(Vector3 local, out float margin)
	{
		margin = 0f;
		foreach (var (axis, r) in DrillAxes)
		{
			float dx = local.X - axis.X, dy = local.Y - axis.Y;
			float d = MathF.Sqrt(dx * dx + dy * dy);
			if (d < r + 0.10f) { margin = r + 0.10f - d; return true; }
		}
		return false;
	}

	/// <summary>
	/// Runde 43: Ankerpunkt im Modellraum, der außerhalb des Bohrerbereichs liegt: liegt der Harpunenpunkt innen (z. B. mittlerer Werfer
	/// direkt hinter dem Werkzeug), wird er radial auf die Flanke geschoben (Radius + 0,16), sonst bleibt er an der Harpunenmündung.
	/// </summary>
	public Vector3 OutsideDrill(Vector3 local, int index)
	{
		if (DrillAxes.Count == 0) return local;
		// nächste Bohrerachse
		(Vector3 Axis, float Radius) best = (Vector3.Zero, 0f);
		float bestD = float.MaxValue;
		foreach (var a in DrillAxes)
		{
			float dx = local.X - a.Axis.X, dy = local.Y - a.Axis.Y;
			float d = MathF.Sqrt(dx * dx + dy * dy);
			if (d < bestD) { bestD = d; best = a; }
		}
		float want = best.Radius + 0.16f;
		if (bestD >= best.Radius + 0.10f) return local;   // liegt schon außen an der Flanke
		float ux = local.X - best.Axis.X, uy = local.Y - best.Axis.Y;
		float ul = MathF.Sqrt(ux * ux + uy * uy);
		if (ul < 1e-4f) { ux = index % 2 == 0 ? 1f : -1f; uy = 0f; ul = 1f; }
		return new Vector3(best.Axis.X + ux / ul * want, best.Axis.Y + uy / ul * want, local.Z);
	}

	/// <summary>
	/// Runde 44h (Handy-Test r44: Seile liefen von der Rumpfmitte parallel nach vorn, fast eine halbe Schiffslänge): Abstand des Seilstarts hinter der
	/// Werkzeugspitze im Modellraum (Halblänge 1). Der Start liegt so im vorderen Drittel an der Flanke, das Seil ist kurz.
	/// </summary>
	public const float AnchorStartBack = 0.24f;

	/// <summary>
	/// Runde 44h: Seilstart im Modellraum für Harpune <paramref name="index"/> — seitliche Lage (X/Y) des Harpunenwerfers, längs aber nahe am Bug
	/// (höchstens <see cref="AnchorStartBack"/> hinter der Werkzeugspitze), außerhalb des Bohrerbereichs (<see cref="OutsideDrill"/>).
	/// </summary>
	public Vector3 AnchorStart(Vector3 harpoonLocal, int index)
	{
		float z = MathF.Min(harpoonLocal.Z, TipLocal.Z + AnchorStartBack);
		var p = OutsideDrill(new Vector3(harpoonLocal.X, harpoonLocal.Y, z), index);
		// Runde 46: der Seilstart sitzt auf der echten Rumpffläche (nächster Punkt der Rumpfdreiecke, minimal nach innen) — die AABB-Klemmung
		// ließ an schmalen Bugformen Luft zwischen Seil und Rumpf
		return SnapToHull(p, index);
	}

	/// <summary>Runde 46: nächster Punkt der Rumpfoberfläche zu <paramref name="p"/> (Modellraum), 0,02 zur Rumpfmitte hin versenkt; ohne Dreiecke unverändert.</summary>
	public Vector3 SnapToHull(Vector3 p, int index)
	{
		if (HullTris.Length < 3) return p;
		if (_hullSnap.TryGetValue((index, p), out var cached)) return cached;
		var best = p; float bestD = float.MaxValue;
		for (int i = 0; i + 2 < HullTris.Length; i += 3)
		{
			var q = ClosestOnTriangle(p, HullTris[i], HullTris[i + 1], HullTris[i + 2]);
			float d = q.DistanceSquaredTo(p);
			if (d < bestD) { bestD = d; best = q; }
		}
		var inward = HullLocal.GetCenter() - best;
		if (inward.LengthSquared() > 1e-6f) best += inward.Normalized() * 0.02f;
		_hullSnap[(index, p)] = best;
		return best;
	}

	/// <summary>Nächster Punkt auf dem Dreieck abc (Ericson, Real-Time Collision Detection).</summary>
	static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
	{
		var ab = b - a; var ac = c - a; var ap = p - a;
		float d1 = ab.Dot(ap), d2 = ac.Dot(ap);
		if (d1 <= 0f && d2 <= 0f) return a;
		var bp = p - b; float d3 = ab.Dot(bp), d4 = ac.Dot(bp);
		if (d3 >= 0f && d4 <= d3) return b;
		float vc = d1 * d4 - d3 * d2;
		if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
		var cp = p - c; float d5 = ab.Dot(cp), d6 = ac.Dot(cp);
		if (d6 >= 0f && d5 <= d6) return c;
		float vb = d5 * d2 - d1 * d6;
		if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
		float va = d3 * d6 - d5 * d4;
		if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
		float den = 1f / (va + vb + vc);
		return a + ab * (vb * den) + ac * (vc * den);
	}

	/// <summary>Runde 45: liegt dieser Modellraum-Punkt innerhalb des Bohrerbereichs (nächste Bohrerachse, Rand + 0,1)? Siehe <see cref="OutsideDrill"/>.</summary>
	public bool WithinDrill(Vector3 local)
	{
		if (DrillAxes.Count == 0) return false;
		float bestD = float.MaxValue;
		float bestR = 0f;
		foreach (var (axis, r) in DrillAxes)
		{
			float d = MathF.Sqrt((local.X - axis.X) * (local.X - axis.X) + (local.Y - axis.Y) * (local.Y - axis.Y));
			if (d < bestD) { bestD = d; bestR = r; }
		}
		return bestD < bestR + 0.10f;
	}

	/// <summary>
	/// Runde 43b („Bohrer beim Bohren etwas in den Asteroiden“): reine Darstellung — während verankert abgebaut wird, rückt das Modell (Root) entlang der
	/// Bugachse um <see cref="BiteNow"/> nach vorn, die Spitze steckt so im Fels. Tiefe = max(<see cref="BiteMin"/> × Größenmaßstab, <see cref="BiteShare"/> ×
	/// Bohrradius), weich ein (<see cref="BiteInSeconds"/>) und aus (<see cref="BiteOutSeconds"/>), langsames Vor-/Zurückpulsieren (±<see cref="BitePulse"/>).
	/// Schiffs- und Core-Position bleiben unverändert (kein Kollisions- oder Hash-Einfluss).
	/// </summary>
	public const float BiteShare = 0.32f, BiteMin = 0.3f, BitePulse = 0.12f, BiteInSeconds = 0.8f, BiteOutSeconds = 0.5f, BitePulseSeconds = 1.6f;
	/// <summary>Runde 43b: größter Bohrerradius in Welteinheiten (Rotor ohne messbares Mesh: <c>contactRadius</c> aus ships.json).</summary>
	public float DrillRadiusWorld => (DrillAxes.Count > 0 ? DrillAxes.Max(a => a.Radius) : Info.ContactRadius) * Root.Scale.X;
	/// <summary>Runde 43b: hat das Modell ein Werkzeug, das eindringen kann?</summary>
	public bool HasDrill => DrillAxes.Count > 0 || Rotors.Count > 0;
	/// <summary>Runde 43b: volle Eindringtiefe ohne Pulsieren (0 ohne Werkzeug).</summary>
	public float BiteDepth => !HasDrill ? 0f : MathF.Max(BiteMin * Info.Scale, BiteShare * DrillRadiusWorld);
	/// <summary>Runde 43b: Einfahrfortschritt 0..1 (linear; angezeigt geglättet).</summary>
	public float BiteShown { get; private set; }
	/// <summary>Runde 43b: aktuelle Eindringtiefe in Welteinheiten (Verschiebung des Modells nach vorn).</summary>
	public float BiteNow { get; private set; }
	float _bitePhase;

	/// <summary>Runde 43b: Eindringen je Bild weiterführen (<paramref name="drilling"/> = Anker halten und Abbau läuft).</summary>
	public void AnimateBite(bool drilling, float delta)
	{
		if (!HasDrill) { BiteShown = 0f; BiteNow = 0f; return; }
		BiteShown = Mathf.MoveToward(BiteShown, drilling ? 1f : 0f, delta / (drilling ? BiteInSeconds : BiteOutSeconds));
		_bitePhase = BiteShown > 0f ? _bitePhase + delta : 0f;
		float s = BiteShown * BiteShown * (3f - 2f * BiteShown);
		float pulse = 1f + BitePulse * MathF.Sin(_bitePhase * MathF.Tau / BitePulseSeconds);
		BiteNow = BiteDepth * s * pulse;
		Root.Position = new Vector3(0f, 0f, -BiteNow);
	}

	/// <summary>Runde 43b: sofort ganz heraus (Sektorwechsel, neue Sitzung).</summary>
	public void ResetBite() { BiteShown = 0f; BiteNow = 0f; _bitePhase = 0f; Root.Position = Vector3.Zero; }

	/// <summary>Runde 43: Werkzeugspitze in Weltkoordinaten (Selbsttest: Abstand Spitze↔Felsoberfläche).</summary>
	public Vector3 TipWorld() => Root.GlobalTransform * TipLocal;

	/// <summary>Runde 43: Bohrerachsen in Weltkoordinaten (Selbsttest: Seilstart außerhalb des Bohrerbereichs).</summary>
	public IEnumerable<(Vector3 Axis, float Radius)> DrillAxesWorld()
	{
		float s = Root.Scale.X;   // Root hängt direkt am Marker (Maßstab 1) — Weltmaßstab = lokaler Maßstab
		foreach (var (axis, r) in DrillAxes) yield return (Root.GlobalTransform * axis, r * s);
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
	public const float RotorSpeed = 8f;   // Runde 18: ≈ 1,3 U/s (vorher 14 — bei 30 Bildern/s zu schnell für das Auge)
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
				string mining = v.TryGetProperty("mining", out var mi) ? mi.GetString() ?? "" : "";
				float contactRadius = v.TryGetProperty("contactRadius", out var cr) && cr.ValueKind == JsonValueKind.Number ? cr.GetSingle() : 0f;
				_info[m.Name] = new ShipModelInfo(m.Name, size, scale, HardpointSet.Parse(v, "slot"), Names(v, "rotors"), Names(v, "jaws"), mining, contactRadius);
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

	static readonly Dictionary<string, float> _tipCache = new();
	/// <summary>
	/// Runde 43: Abstand Schiffsmitte → Bohrerspitze in Welteinheiten entlang der Bugachse (aus dem Modell gemessen, je Modell gecacht).
	/// Ohne Modell (Grundkörper) die Halblänge des Grundkörpers (≈ Kollisionskreis).
	/// </summary>
	public static float TipOffsetOf(string modelKey)
	{
		if (_tipCache.TryGetValue(modelKey, out var t)) return t;
		if (modelKey.Length == 0 || !Has(modelKey)) return 2.5f;
		var v = Create(modelKey, new Color(0.6f, 0.6f, 0.65f));
		if (v is null) return 2.5f;
		t = v.TipOffset;
		v.Root.QueueFree();
		_tipCache[modelKey] = t;
		return t;
	}

	/// <summary>Runde 43: Modelle mit Kontaktwerkzeug (Abbauart „contact“ in ships.json).</summary>
	public static IEnumerable<ShipModelInfo> ContactModels() => Info().Values.Where(i => i.Mining == "contact");

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
		SurfaceDetail.Apply(node, ScaleOf(modelKey), SurfaceDetail.ShipTile, false);   // gemeinsame Oberflächen-Texturen (einmal je Modell)
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
					else if (name.StartsWith("Harpoon_")) v.Harpoons.Add(c3);
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
		foreach (var r in v.Rotors) AddRotorMarker(r);   // Runde 18
		ComputeToolTip(v, info);   // Runde 43: Bohrerspitze und Bohrerachsen messen
		ComputeHull(v);            // Runde 45: Rumpfhülle für die Ankertrossen (Seilstart am Rumpf)
		v.Hardpoints = HardpointSet.Bind(node, info.Hardpoints);
		v.SetAccent(accent);
		v.SetEngine(0.2f);
		return v;
	}

	/// <summary>Runde 43: misst die Werkzeugspitze (vorderster Punkt aller Rotor-Meshes im Root-Raum) und die Bohrerachsen einer Modellinstanz.</summary>
	/// <summary>Runde 45: Rumpfhülle im Modellraum — Vereinigung der Mesh-AABBs unterhalb von <c>Hull</c>, ohne Werkzeuge/Werfer/Türme und ohne Drehmarken.</summary>
	static void ComputeHull(ShipVisual v)
	{
		Aabb box = new(new Vector3(-0.5f, -0.3f, -1f), new Vector3(1f, 0.6f, 2f));
		bool any = false;
		var tris = new List<Vector3>();
		void Walk(Node n, Transform3D t)
		{
			foreach (var c in n.GetChildren())
			{
				if (c is not Node3D c3) continue;
				string name = c3.Name;
				if (name.StartsWith("Rotor_") || name.StartsWith("Jaw_") || name.StartsWith("Harpoon_") || name.StartsWith("Turret_") || name.StartsWith("RotorMark")) continue;
				var ct = t * c3.Transform;
				if (c3 is MeshInstance3D { Mesh: { } mesh })
				{
					foreach (var f in mesh.GetFaces()) tris.Add(ct * f);   // Dreiecke im Modellraum: Seilstart auf der echten Rumpffläche (Runde 46)
					var aabb = mesh.GetAabb();
					for (int cx = 0; cx < 2; cx++) for (int cy = 0; cy < 2; cy++) for (int cz = 0; cz < 2; cz++)
					{
							var corner = new Vector3(cx == 0 ? aabb.Position.X : aabb.End.X, cy == 0 ? aabb.Position.Y : aabb.End.Y, cz == 0 ? aabb.Position.Z : aabb.End.Z);
						var w = ct * corner;
						box = any ? box.Expand(w) : new Aabb(w, Vector3.Zero);
						any = true;
					}
				}
				Walk(c3, ct);
			}
		}
		Walk(v.Root, Transform3D.Identity);
		if (any) v.HullLocal = box;
		v.HullTris = tris.ToArray();
	}

	static void ComputeToolTip(ShipVisual v, ShipModelInfo info)
	{
		float minZ = -1f;
		foreach (var r in v.Rotors)
		{
			var mesh = r as MeshInstance3D ?? r.GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
			if (mesh?.Mesh is null) continue;
			var t = r.Transform;
			for (var p = r.GetParent(); p is Node3D p3 && p != v.Root; p = p.GetParent()) t = p3.Transform * t;
			var aabb = mesh.Mesh.GetAabb();
			float z = float.MaxValue;
			for (int cx = 0; cx < 2; cx++) for (int cy = 0; cy < 2; cy++) for (int cz = 0; cz < 2; cz++)
			{
				var corner = new Vector3(cx == 0 ? aabb.Position.X : aabb.End.X, cy == 0 ? aabb.Position.Y : aabb.End.Y, cz == 0 ? aabb.Position.Z : aabb.End.Z);
				z = Mathf.Min(z, (t * corner).Z);
			}
			minZ = Mathf.Min(minZ, z);
			float axR = info.ContactRadius > 0f ? info.ContactRadius : Mathf.Max(aabb.Size.X, aabb.Size.Y) * 0.5f;
			v.DrillAxes.Add((t.Origin, axR));
		}
		v.TipLocal = new Vector3(0f, 0f, minZ);
		v.TipOffset = -minZ * v.Root.Scale.X;
	}

	static StandardMaterial3D? _rotorMarkMat;
	/// <summary>
	/// Runde 18 (Handy-Test: Bohrerdrehung kaum erkennbar — die Werkzeugköpfe sind drehsymmetrisch): ein leuchtender Strich außerhalb der Drehachse auf jedem
	/// Werkzeug macht die Drehung sichtbar (Größe aus dem Mesh des Werkzeugs, ein geteiltes Material).
	/// </summary>
	static void AddRotorMarker(Node3D rotor)
	{
		var mesh = rotor as MeshInstance3D ?? rotor.GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
		if (mesh?.Mesh is null) return;
		var box = mesh.Mesh.GetAabb();
		if (mesh != rotor) box = new Aabb(box.Position + mesh.Position, box.Size);
		float r = Mathf.Max(box.Size.X, box.Size.Y) * 0.5f;
		if (r <= 1e-4f) return;
		_rotorMarkMat ??= Gfx.Mat(new Color(0.55f, 1f, 1f), 3f, 0f);   // Runde 19: kühles Weißblau hebt sich vom gelben Werkzeug ab
		var c = box.GetCenter();
		// Runde 19 (Handy-Test d_crops: Marke unter Funken/Trümmern an der Spitze nicht zu sehen): zwei breite Leisten außen am hinteren Teil des Werkzeugs,
		// gegenüberliegend und über den Rand hinaus — sie laufen sichtbar um, auch wenn die Spitze im Abraum steckt
		float len = MathF.Max(box.Size.Z * 0.55f, r * 0.8f);
		float zBack = box.End.Z - len * 0.5f;
		for (int k = 0; k < 2; k++)
		{
			float sgn = k == 0 ? 1f : -1f;
			rotor.AddChild(new MeshInstance3D
			{
				Name = $"RotorMark{k}", Mesh = new BoxMesh { Size = new Vector3(r * 0.45f, r * 0.35f, len) }, MaterialOverride = _rotorMarkMat,
				Position = new Vector3(c.X, c.Y + sgn * r * 1.08f, zBack), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}
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
