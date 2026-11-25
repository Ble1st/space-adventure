using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpaceAdventure.Game;

/// <summary>
/// Hilfe und Tutorial: Kontexthinweise an/aus und die Liste der schon gezeigten Hinweise — gespeichert in der Einstellungsdatei (<c>user://settings.cfg</c>,
/// Abschnitt <c>hints</c>; Prüfläufe <c>settings-test.cfg</c>), nicht im Spielstand: man lernt das Spiel einmal, nicht je Spielstand. „Zurücksetzen“ leert die
/// Liste, damit jeder Hinweis wieder einmal erscheint.
/// </summary>
public static class HintSettings
{
	static bool? _enabled;
	static HashSet<string>? _seen;

	static ConfigFile Load()
	{
		var cfg = new ConfigFile();
		cfg.Load(SpaceAdventure.Audio.AudioSettings.Path);
		return cfg;
	}

	static void Store()
	{
		var cfg = Load();   // andere Abschnitte bleiben
		cfg.SetValue("hints", "enabled", Enabled);
		cfg.SetValue("hints", "seen", string.Join(",", Seen.OrderBy(k => k, System.StringComparer.Ordinal)));
		cfg.Save(SpaceAdventure.Audio.AudioSettings.Path);
	}

	public static bool Enabled
	{
		get
		{
			if (_enabled is { } e) return e;
			_enabled = (bool)Load().GetValue("hints", "enabled", true);
			return _enabled.Value;
		}
	}

	public static IReadOnlySet<string> Seen
	{
		get
		{
			if (_seen is null)
			{
				string raw = (string)Load().GetValue("hints", "seen", "");
				_seen = new HashSet<string>(raw.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries), System.StringComparer.Ordinal);
			}
			return _seen;
		}
	}

	public static void SetEnabled(bool on) { _enabled = on; Store(); }
	public static void Toggle() => SetEnabled(!Enabled);

	public static void MarkSeen(string key)
	{
		_ = Seen;
		if (_seen!.Add(key)) Store();
	}

	public static void Reset()
	{
		_ = Seen;
		_seen!.Clear();
		Store();
	}

	/// <summary>Prüfläufe: Zwischenspeicher verwerfen (die Testdatei wurde gewechselt oder gelöscht).</summary>
	public static void Forget() { _enabled = null; _seen = null; }

	/// <summary>Selbsttest: gespeicherte Liste in der Datei (ohne Zwischenspeicher).</summary>
	public static string FileSeen() => (string)Load().GetValue("hints", "seen", "");

	public static string Text => Enabled ? "Hinweise an" : "Hinweise aus";
}
