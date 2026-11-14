using Godot;

namespace SpaceAdventure.Audio;

/// <summary>
/// Runde 12: Audio-Busse „Musik“ und „Effekte“ (beide an Master) und ihre Lautstärke/Stummschaltung. Gespeichert in einer kleinen Einstellungsdatei
/// (<c>user://settings.cfg</c>), nie im Spielstand. Lautstärke in Prozent (0–100) in Schritten von 10.
/// </summary>
public static class AudioSettings
{
	public const string MusicBus = "Musik", EffectsBus = "Effekte";
	public const string DefaultPath = "user://settings.cfg";
	public const int Step = 10;

	/// <summary>Datei der Einstellungen (Selbsttest: eigene Datei, damit die echten Einstellungen unberührt bleiben).</summary>
	public static string Path = DefaultPath;

	public static int MusicVolume { get; private set; } = 70;
	public static int EffectsVolume { get; private set; } = 80;
	public static bool MusicMuted { get; private set; }
	public static bool EffectsMuted { get; private set; }

	/// <summary>Busse anlegen (falls nötig), Einstellungen laden und anwenden.</summary>
	public static void Init()
	{
		EnsureBus(MusicBus);
		EnsureBus(EffectsBus);
		// Selbsttest/UI-Bilder: eigene, frische Datei — die echten Einstellungen bleiben unberührt
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a == "--selftest" || a == "--ui-shots" || a == "--ui-check") { Path = "user://settings-test.cfg"; DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Path)); break; }
		var cfg = new ConfigFile();
		if (cfg.Load(Path) == Error.Ok)
		{
			MusicVolume = Mathf.Clamp((int)cfg.GetValue("audio", "music_volume", MusicVolume), 0, 100);
			EffectsVolume = Mathf.Clamp((int)cfg.GetValue("audio", "effects_volume", EffectsVolume), 0, 100);
			MusicMuted = (bool)cfg.GetValue("audio", "music_muted", false);
			EffectsMuted = (bool)cfg.GetValue("audio", "effects_muted", false);
		}
		Apply();
	}

	static void EnsureBus(string name)
	{
		if (AudioServer.GetBusIndex(name) >= 0) return;
		AudioServer.AddBus();
		int i = AudioServer.BusCount - 1;
		AudioServer.SetBusName(i, name);
		AudioServer.SetBusSend(i, "Master");
	}

	static void Apply()
	{
		Set(MusicBus, MusicVolume, MusicMuted);
		Set(EffectsBus, EffectsVolume, EffectsMuted);
	}

	static void Set(string bus, int volume, bool muted)
	{
		int i = AudioServer.GetBusIndex(bus);
		if (i < 0) return;
		AudioServer.SetBusVolumeDb(i, volume <= 0 ? -80f : Mathf.LinearToDb(volume / 100f));
		AudioServer.SetBusMute(i, muted || volume <= 0);
	}

	/// <summary>Lautstärke ändern (Schritt ±10) und speichern.</summary>
	public static void ChangeMusic(int delta) { MusicVolume = Mathf.Clamp(MusicVolume + delta, 0, 100); Save(); }
	public static void ChangeEffects(int delta) { EffectsVolume = Mathf.Clamp(EffectsVolume + delta, 0, 100); Save(); }
	public static void ToggleMusicMute() { MusicMuted = !MusicMuted; Save(); }
	public static void ToggleEffectsMute() { EffectsMuted = !EffectsMuted; Save(); }

	/// <summary>Text für Menüs: „Musik 70 %“ bzw. „Musik stumm“.</summary>
	public static string MusicText => MusicMuted ? "Musik stumm" : $"Musik {MusicVolume} %";
	public static string EffectsText => EffectsMuted ? "Effekte stumm" : $"Effekte {EffectsVolume} %";

	static void Save()
	{
		Apply();
		var cfg = new ConfigFile();
		cfg.Load(Path);   // Runde 31: andere Abschnitte (UI-Größe) bleiben erhalten
		cfg.SetValue("audio", "music_volume", MusicVolume);
		cfg.SetValue("audio", "effects_volume", EffectsVolume);
		cfg.SetValue("audio", "music_muted", MusicMuted);
		cfg.SetValue("audio", "effects_muted", EffectsMuted);
		cfg.Save(Path);
	}
}
