using Godot;
using SpaceSim.Core.Flight;

namespace SpaceAdventure.Game;

/// <summary>
/// Runde 44: Kamera-Einstellungen (Modus, Rückansicht-Höhe/-Abstand, Invertierung) in <c>user://settings.cfg</c>, Abschnitt <c>camera</c> —
/// wie Ton und UI-Größe, nie im Spielstand. Selbsttest/UI-Bilder nutzen die Testdatei (<see cref="SpaceAdventure.Audio.AudioSettings.Path"/>).
/// </summary>
public static class CameraSettings
{
	const string Section = "camera";
	/// <summary>Zuletzt geschriebener Stand (Gesten speichern nur bei Änderung).</summary>
	static CameraPrefs? _last;

	public static CameraPrefs Load()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(SpaceAdventure.Audio.AudioSettings.Path) != Error.Ok) return _last = CameraPrefs.Default;
		var p = CameraPrefs.FromValues((string)cfg.GetValue(Section, "mode", "free"),
			(float)(double)cfg.GetValue(Section, "chase_height", (double)OrbitCamera.DefaultChaseHeight),
			(float)(double)cfg.GetValue(Section, "chase_back", (double)OrbitCamera.DefaultChaseBack),
			(bool)cfg.GetValue(Section, "invert", false));
		return _last = p;
	}

	/// <summary>Speichert, wenn sich der Stand geändert hat; andere Abschnitte bleiben erhalten.</summary>
	public static void Save(CameraPrefs p)
	{
		if (_last == p) return;
		var cfg = new ConfigFile();
		cfg.Load(SpaceAdventure.Audio.AudioSettings.Path);
		var v = p.ToValues();
		cfg.SetValue(Section, "mode", v.Mode);
		cfg.SetValue(Section, "chase_height", (double)v.Height);
		cfg.SetValue(Section, "chase_back", (double)v.Back);
		cfg.SetValue(Section, "invert", v.Invert);
		cfg.Save(SpaceAdventure.Audio.AudioSettings.Path);
		_last = p;
	}

	/// <summary>Selbsttest: liest die Datei neu (ohne Zwischenspeicher).</summary>
	public static CameraPrefs ReadFile() { _last = null; return Load(); }
}
