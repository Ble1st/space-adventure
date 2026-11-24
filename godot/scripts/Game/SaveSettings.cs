using Godot;
using SpaceSim.Core.Persistence;

namespace SpaceAdventure.Game;

/// <summary>
/// Mehrere Spielstände: Autosave-Intervall (aus / 30 s / 1 min / 5 min), gespeichert in der Einstellungsdatei (<c>user://settings.cfg</c>, Abschnitt
/// <c>save</c>; Prüfläufe nutzen <c>settings-test.cfg</c> wie Ton und UI-Größe). „aus“ schaltet nur das regelmäßige Speichern ab — beim Schließen/Pausieren
/// und „Zum Hauptmenü“ wird weiter gespeichert.
/// </summary>
public static class SaveSettings
{
	static int? _seconds;

	public static int AutosaveSeconds
	{
		get
		{
			if (_seconds is { } s) return s;
			var cfg = new ConfigFile();
			int v = SaveSlots.DefaultAutosaveSeconds;
			if (cfg.Load(SpaceAdventure.Audio.AudioSettings.Path) == Error.Ok) v = (int)cfg.GetValue("save", "autosave_seconds", v);
			_seconds = SaveSlots.NormalizeAutosave(v);
			return _seconds.Value;
		}
	}

	public static void SetAutosave(int seconds)
	{
		_seconds = SaveSlots.NormalizeAutosave(seconds);
		var cfg = new ConfigFile();
		cfg.Load(SpaceAdventure.Audio.AudioSettings.Path);   // andere Abschnitte bleiben
		cfg.SetValue("save", "autosave_seconds", _seconds.Value);
		cfg.Save(SpaceAdventure.Audio.AudioSettings.Path);
	}

	/// <summary>Selbsttest: gespeicherter Wert in der Datei (ohne Zwischenspeicher).</summary>
	public static int? FileValue()
	{
		var cfg = new ConfigFile();
		return cfg.Load(SpaceAdventure.Audio.AudioSettings.Path) == Error.Ok && cfg.HasSectionKey("save", "autosave_seconds") ? (int)cfg.GetValue("save", "autosave_seconds") : null;
	}

	public static string Text => "Autosave " + SaveSlots.AutosaveText(AutosaveSeconds);
}
