using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using SpaceSim.Core.Model;

namespace SpaceAdventure.World;

/// <summary>
/// Himmelskugel (Skybox) des Spielersektors: ein equirektangulares Panorama als Hintergrund des <see cref="Godot.Environment"/>
/// (<see cref="PanoramaSkyMaterial"/>). Welcher Himmel zu welchem Sektor gehört, steht in der Welt-Datei (<c>sectors[].skybox</c>,
/// <c>skyboxYaw</c>); Datei, Planet ja/nein und der Bildpunkt, der in Blickrichtung liegen soll, stehen im Verzeichnis
/// <c>res://assets/skyboxes/skyboxes.json</c>. Reine Darstellung: liest Core-Daten, schreibt nichts.
/// <para>
/// Die Kamera blickt schräg <b>nach unten</b> auf die Spielebene (Neigung 15–85°), der sichtbare Himmel liegt also vor allem unterhalb des
/// Horizonts. Deshalb dreht die Komponente jedes Panorama so, dass der Fokus-Bildpunkt (Planetenmitte bzw. Galaxienband) in Höhe
/// <c>elevation</c> vor der Standardkamera (Blick nach -Z, gedreht um <c>skyboxYaw</c>) liegt. Planeten stehen <b>unter</b> der Ebene, aber so hoch, dass die
/// Standardansicht (Neigung 57,7°, Bildoberkante <see cref="DefaultTopEdgeDeg"/>) und die Rückansicht ihre beleuchtete Sichel am oberen Bildrand zeigen und das
/// Schiff außerhalb der Scheibe bleibt; in sehr flachen Ansichten steht der Planet hinter dem Schiff — die Planetentexturen sind dafür abgedunkelt. Das Umgebungslicht bleibt eine feste Farbe, Spiegelungen aus dem Himmel sind aus —
/// die Textur färbt Schiffe, Stationen und Felsen nicht ein.
/// </para>
/// <para>
/// Rückansicht (<c>CameraMode.Chase</c>): dort folgt der Kamera-Gier dem Kurs, und Höhe/Abstand (einstellbar) bestimmen die Neigung — ein weltfester
/// Himmel zeigte den Planeten nur beim Flug in seine Richtung und bei steiler Rückansicht gar nicht. In der Rückansicht dreht sich deshalb jeder Himmel
/// (Planeten- wie Sternhimmel, einheitlich) mit der echten Kamera (<see cref="ChaseFollow"/>: Gier um die Hochachse, dann um die waagrechte Rechtsachse
/// der Kamera um Neigung − Standardneigung) — der Planet bleibt bei jedem Kurs und jeder Höhe/Abstand an derselben Bildstelle wie in der
/// Standard-Rückansicht (Höhe 45 / Abstand 35, Kurs -Z). Bewegung zeigen weiter die weltfesten Sternstaub-Kacheln. Frei und Zielfolge: weltfest.
/// Beim Moduswechsel blendet der Anteil in <see cref="BlendSeconds"/> über.
/// </para>
/// Die Textur wird nur beim Wechsel des Himmels geladen (<see cref="Loads"/>); der vorige Himmel wird freigegeben (ein Himmel im Speicher).
/// </summary>
public partial class SkyboxView : Node
{
	/// <param name="Radius">Winkelradius des Planeten in Grad (0 = kein Planet); mit <see cref="Elevation"/> legt er fest, wie weit die Scheibe in die
	/// Standardansicht hineinreicht (<see cref="LowerLimb"/>).</param>
	public sealed record Entry(string Key, string File, bool Planet, float FocusU, float FocusV, float Elevation, float Radius = 0f)
	{
		/// <summary>Höhe des unteren Planetenrands in Grad über der Spielebene (Elevation − Radius).</summary>
		public float LowerLimb => Elevation - Radius;
	}

	/// <summary>Höhe der Bildoberkante der Standardkamera in Grad über der Ebene (Neigung 57,7°, vertikales Sichtfeld 50°): −32,7°.</summary>
	public static float DefaultTopEdgeDeg => -SpaceSim.Core.Flight.OrbitCamera.DefaultPitchDeg + 25f;

	const string Dir = "res://assets/skyboxes/";
	static Dictionary<string, Entry>? _manifest;

	/// <summary>Alle bekannten Himmel (aus <c>skyboxes.json</c>, einmal gelesen).</summary>
	public static IReadOnlyDictionary<string, Entry> Manifest => _manifest ??= LoadManifest();

	/// <summary>True, wenn der Schlüssel einen Himmel mit vorhandener Datei ergibt (dann zeigt die Sektoransicht keinen eigenen Planeten).</summary>
	public static bool Resolves(string? key) => !string.IsNullOrEmpty(key) && Manifest.ContainsKey(key);

	readonly Godot.Environment _env;
	readonly Func<Universe?> _universe;
	readonly Color _fallbackColor;
	readonly Sky _sky;
	readonly PanoramaSkyMaterial _mat;

	/// <summary>Schlüssel des gerade gezeigten Himmels ("" = keiner, einfacher Farbhintergrund).</summary>
	public string CurrentKey { get; private set; } = "";
	/// <summary>Der gezeigte Himmel enthält einen Planeten.</summary>
	public bool CurrentHasPlanet { get; private set; }
	/// <summary>Sektor, für den der Himmel zuletzt gesetzt wurde.</summary>
	public SectorId CurrentSector { get; private set; }
	/// <summary>Zahl der Texturladevorgänge seit dem Start (Selbsttest: nicht pro Bild).</summary>
	public int Loads { get; private set; }
	/// <summary>Die gerade gezeigte Textur (null ohne Himmel).</summary>
	public Texture2D? Texture => _mat.Panorama;
	/// <summary>Drehung des Himmels (Basis Welt → Panorama), für den Selbsttest.</summary>
	public Basis Rotation { get; private set; } = Basis.Identity;

	float _yaw = float.NaN;
	readonly HashSet<string> _missing = new();   // Diagnose: fehlende Textur nur einmal melden (Apply versucht es sonst jedes Bild)

	/// <summary>Überblendzeit beim Wechsel Rückansicht ↔ weltfest (Sekunden).</summary>
	public const float BlendSeconds = 0.5f;

	readonly Func<Vector3?>? _chaseForward;
	Entry? _entry;
	float _follow;          // 0 = weltfest, 1 = folgt der Kamera (Rückansicht)
	float _appliedYaw = float.NaN;

	/// <summary>Gier, mit dem der Himmel gerade ausgerichtet ist (Sektor-<c>skyboxYaw</c> + Gieranteil der Kamera in der Rückansicht), Grad.</summary>
	public float AppliedYaw => _appliedYaw;
	/// <summary>Anteil, mit dem der Himmel dem Kamera-Gier folgt (0 weltfest … 1 Rückansicht).</summary>
	public float Follow => _follow;

	/// <param name="chaseForward">Blickrichtung der echten Kamera (Welt), solange die Rückansicht aktiv ist; sonst null (weltfester Himmel).</param>
	public SkyboxView(Godot.Environment env, Func<Universe?> universe, Func<Vector3?>? chaseForward = null)
	{
		_chaseForward = chaseForward;
		Name = "Himmel";
		_env = env;
		_universe = universe;
		_fallbackColor = env.BackgroundColor;
		_mat = new PanoramaSkyMaterial { Filter = true, EnergyMultiplier = 1f };
		// Spiegelungen/Umgebung kommen nicht aus dem Himmel (siehe unten) — kleinste Radiance-Größe, nur bei Bedarf neu berechnet
		_sky = new Sky { SkyMaterial = _mat, RadianceSize = Sky.RadianceSizeEnum.Size32, ProcessMode = Sky.ProcessModeEnum.Automatic };
		_env.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled;
	}

	public override void _Ready() =>
		GD.Print($"[sky] Start: Verzeichnis {Manifest.Count} Himmel, Renderer {RenderingServer.GetCurrentRenderingMethod()}/{RenderingServer.GetCurrentRenderingDriverName()}, " +
			$"Grafik {RenderingServer.GetVideoAdapterName()}, ETC2 {OS.HasFeature("etc2")}, S3TC {OS.HasFeature("s3tc")}, ASTC {OS.HasFeature("astc")}");

	public override void _Process(double delta)
	{
		var u = _universe();
		if (u is null || !u.Sectors.TryGetValue(u.PlayerSector, out var sec)) return;
		if (sec.Id != CurrentSector || sec.Skybox != CurrentKey || sec.SkyboxYaw != _yaw) Apply(sec);
		// Rückansicht: Himmel dreht und neigt sich mit der (bereits geglätteten) Kamera; Wechsel weich überblenden
		Vector3? fwd = _chaseForward?.Invoke();
		_follow = Mathf.MoveToward(_follow, fwd.HasValue ? 1f : 0f, (float)delta / BlendSeconds);
		if (fwd.HasValue) _lastForward = fwd.Value;
		Orient();
	}

	Vector3 _lastForward = WorldDir(0f, -DefaultChasePitchDeg);
	Basis _appliedFollow = new(new Vector3(float.NaN, 0, 0), Vector3.Up, Vector3.Back);

	/// <summary>Neigung der Standard-Rückansicht (Höhe 45 / Abstand 35): ≈ 52,1° unter der Waagrechten.</summary>
	public static float DefaultChasePitchDeg => Mathf.RadToDeg(Mathf.Atan2(SpaceSim.Core.Flight.OrbitCamera.DefaultChaseHeight, SpaceSim.Core.Flight.OrbitCamera.DefaultChaseBack));

	/// <summary>
	/// Drehung (Welt), die die Standard-Rückansicht (Blick nach -Z, Neigung <see cref="DefaultChasePitchDeg"/>, kein Rollen) auf die Kamera mit
	/// Blickrichtung <paramref name="forward"/> abbildet: Gier um die Hochachse, dann um die waagrechte Rechtsachse der Kamera um (Neigung − Standardneigung).
	/// Null bei senkrechtem Blick (dort ist die Rechtsachse unbestimmt).
	/// </summary>
	public static Basis? ChaseFollow(Vector3 forward)
	{
		if (forward.X * forward.X + forward.Z * forward.Z < 1e-6f) return null;
		return Frame(forward) * Frame(WorldDir(0f, -DefaultChasePitchDeg)).Transposed();
	}

	/// <summary>Richtet den aktuellen Himmel aus: Sektor-Gier, in der Rückansicht anteilig (<see cref="Follow"/>) der Kamera nachgeführt (nur bei Änderung).</summary>
	void Orient()
	{
		if (_entry is null || _env.Sky is null) return;
		var full = ChaseFollow(_lastForward) ?? Basis.Identity;
		var follow = _follow >= 0.9999f ? full : _follow <= 0.0001f ? Basis.Identity
			: new Basis(Quaternion.Identity.Slerp(full.GetRotationQuaternion(), _follow));
		if (!float.IsNaN(_appliedFollow.X.X) && follow.X.DistanceTo(_appliedFollow.X) < 1e-4f && follow.Y.DistanceTo(_appliedFollow.Y) < 1e-4f
			&& follow.Z.DistanceTo(_appliedFollow.Z) < 1e-4f) return;
		_appliedFollow = follow;
		var fz = follow * Vector3.Forward;   // Blick nach -Z der Standard-Rückansicht, mitgedreht → Gieranteil
		_appliedYaw = _yaw + Mathf.RadToDeg(Mathf.Atan2(fz.X, -fz.Z));
		var fwd = follow * WorldDir(0f, -DefaultChasePitchDeg);
		AppliedTilt = (_follow <= 0.0001f) ? 0f : Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(-fwd.Y, -1f, 1f))) - DefaultChasePitchDeg;
		// Welt → Panorama: Planetenrichtung in der Welt = follow · (Richtung in der Standardausrichtung)
		Rotation = Orientation(_entry, _yaw) * follow.Transposed();
		// Godot dreht die Blickrichtung mit der Inversen der Himmelsorientierung in den Himmelsraum (sky.glsl) — also die Inverse setzen
		_env.SkyRotation = Rotation.Transposed().GetEuler();
	}

	/// <summary>Gerade angewendete Nachführung (Welt; Identität = weltfest): Planetenrichtung = Nachführung · Richtung der Standardausrichtung.</summary>
	public Basis AppliedFollow => _appliedFollow;

	/// <summary>Zusätzliche Neigung des Himmels in der Rückansicht (Grad; Kameraneigung − Standardneigung, mal Anteil).</summary>
	public float AppliedTilt { get; private set; }

	/// <summary>Setzt den Himmel des Sektors (lädt die Textur nur, wenn sich der Himmel ändert).</summary>
	public void Apply(Sector sector)
	{
		CurrentSector = sector.Id;
		_yaw = sector.SkyboxYaw;
		if (!Manifest.TryGetValue(sector.Skybox, out var e))
		{
			Log(sector, $"kein Himmel (Schlüssel '{sector.Skybox}', Verzeichnis {Manifest.Count} Einträge) → Farbhintergrund");
			CurrentKey = sector.Skybox; CurrentHasPlanet = false; _entry = null;
			_mat.Panorama = null;
			_env.BackgroundMode = Godot.Environment.BGMode.Color;
			_env.BackgroundColor = _fallbackColor;
			_env.Sky = null;
			return;
		}
		if (e.Key != CurrentKey || _mat.Panorama is null)
		{
			var tex = ResourceLoader.Load<Texture2D>(Dir + e.File);
			Loads++;
			if (tex is null)
			{
				if (_missing.Add(e.File)) { GD.PushWarning($"Himmel {e.File} fehlt"); Log(sector, $"Textur {e.File} NICHT geladen"); }
				CurrentKey = ""; _entry = null; return;
			}
			_mat.Panorama = tex;   // der vorige Himmel verliert damit seine letzte Referenz und wird freigegeben
		}
		CurrentKey = e.Key; CurrentHasPlanet = e.Planet; _entry = e;
		_env.Sky = _sky;
		_env.BackgroundMode = Godot.Environment.BGMode.Sky;
		_appliedFollow.X = new Vector3(float.NaN, 0, 0);   // neu ausrichten
		Orient();
		Log(sector, $"Himmel '{e.Key}' {Describe(_mat.Panorama)}");
	}

	/// <summary>Diagnosezeile „[sky] …“ (läuft auch auf dem Gerät, je Himmelswechsel eine Zeile).</summary>
	void Log(Sector sector, string what)
	{
		string drv = RenderingServer.GetCurrentRenderingDriverName(), meth = RenderingServer.GetCurrentRenderingMethod();
		GD.Print($"[sky] Sektor {sector.Name} ({sector.Id.Value}): {what} · Hintergrund {_env.BackgroundMode}, Himmel {(_env.Sky is null ? "keiner" : "gesetzt")}, " +
			$"Material {(_env.Sky?.SkyMaterial is PanoramaSkyMaterial pm && pm.Panorama is not null ? "Panorama" : "leer")} · Renderer {meth}/{drv}, Ladevorgänge {Loads}");
	}

	static string Describe(Texture2D? t)
	{
		if (t is null) return "Textur keine";
		string fmt = "?";
		try { var rid = t.GetRid(); fmt = RenderingServer.TextureGetFormat(rid).ToString(); } catch (Exception ex) { fmt = "Fehler " + ex.Message; }
		return $"Textur {t.ResourcePath} {t.GetWidth()}×{t.GetHeight()} Format {fmt}";
	}

	/// <summary>Richtung eines Panorama-Bildpunkts (u, v) im Himmelsraum (Godot: u = atan2(x, -z)/2π, v = acos(y)/π).</summary>
	public static Vector3 PanoramaDir(float u, float v)
	{
		float az = u * Mathf.Tau, pol = v * Mathf.Pi;
		return new Vector3(Mathf.Sin(pol) * Mathf.Sin(az), Mathf.Cos(pol), -Mathf.Sin(pol) * Mathf.Cos(az));
	}

	/// <summary>Weltrichtung zu Gierwinkel (Grad, 0 = -Z, positiv = nach rechts/+X) und Höhe (Grad über der Ebene).</summary>
	public static Vector3 WorldDir(float yawDeg, float elevationDeg)
	{
		float y = Mathf.DegToRad(yawDeg), el = Mathf.DegToRad(elevationDeg);
		return new Vector3(Mathf.Cos(el) * Mathf.Sin(y), Mathf.Sin(el), -Mathf.Cos(el) * Mathf.Cos(y));
	}

	/// <summary>
	/// Drehung Welt → Panorama: die Weltrichtung (Gier <paramref name="yawDeg"/>, Höhe <see cref="Entry.Elevation"/>) zeigt auf den Fokus-Bildpunkt,
	/// „oben“ bleibt oben (kein Rollen). Godot dreht mit <see cref="Godot.Environment.SkyRotation"/> die Blickrichtung in den Himmelsraum.
	/// </summary>
	public static Basis Orientation(Entry e, float yawDeg)
	{
		var local = Frame(PanoramaDir(e.FocusU, e.FocusV));
		var world = Frame(WorldDir(yawDeg, e.Elevation));
		return local * world.Transposed();
	}

	/// <summary>Orthonormale Basis, deren -Z-Achse <paramref name="dir"/> ist und deren Y-Achse möglichst nach oben zeigt.</summary>
	static Basis Frame(Vector3 dir)
	{
		var z = -dir.Normalized();
		var x = Vector3.Up.Cross(z);
		if (x.LengthSquared() < 1e-6f) x = Vector3.Right;
		x = x.Normalized();
		var y = z.Cross(x).Normalized();
		return new Basis(x, y, z);
	}

	static Dictionary<string, Entry> LoadManifest()
	{
		var map = new Dictionary<string, Entry>(StringComparer.Ordinal);
		const string path = Dir + "skyboxes.json";
		if (!FileAccess.FileExists(path)) { GD.Print("[sky] " + path + " fehlt — kein Himmel"); return map; }
		try
		{
			using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
			foreach (var s in doc.RootElement.GetProperty("skyboxes").EnumerateArray())
			{
				string key = s.GetProperty("key").GetString() ?? "";
				string file = s.GetProperty("file").GetString() ?? "";
				if (key.Length == 0 || file.Length == 0 || !ResourceLoader.Exists(Dir + file)) { GD.PushWarning($"Himmel '{key}' ohne Datei"); GD.Print($"[sky] Himmel '{key}' ohne Datei {file}"); continue; }
				map[key] = new Entry(key, file, s.GetProperty("planet").GetBoolean(),
					s.GetProperty("focusU").GetSingle(), s.GetProperty("focusV").GetSingle(), s.GetProperty("elevation").GetSingle(),
					s.TryGetProperty("radius", out var rad) ? rad.GetSingle() : 0f);
			}
		}
		catch (Exception ex) { GD.PushWarning("skyboxes.json unlesbar: " + ex.Message); GD.Print("[sky] skyboxes.json unlesbar: " + ex.Message); }
		return map;
	}
}
