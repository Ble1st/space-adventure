using System;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;

namespace SpaceAdventure.Game;

/// <summary>Dateizugriff für Spielstände (user://). Der Core selbst liest und schreibt keine Dateien.</summary>
public static class SaveService
{
	static string _name = "savegame.json";
	static string File => "user://" + _name;
	static string Temp => "user://" + _name + ".tmp";

	/// <summary>Selbsttest und Werkzeuge schreiben in eine eigene Datei und lassen den echten Spielstand unberührt.</summary>
	public static void UseFile(string name) => _name = name;

	public static bool Exists => FileAccess.FileExists(File);

	/// <summary>Schreibt zuerst in eine temporäre Datei und benennt dann um, damit ein Absturz keinen halben Spielstand hinterlässt.</summary>
	public static bool Save(Universe u, out string error)
	{
		error = "";
		try
		{
			var json = SaveGameJson.Write(u, DateTimeOffset.UtcNow);
			using (var f = FileAccess.Open(Temp, FileAccess.ModeFlags.Write))
			{
				if (f == null) { error = "Datei nicht beschreibbar: " + FileAccess.GetOpenError(); return false; }
				f.StoreString(json);
			}
			var err = DirAccess.Open("user://").Rename(_name + ".tmp", _name);
			if (err != Error.Ok) { error = "Umbenennen fehlgeschlagen: " + err; return false; }
			return true;
		}
		catch (Exception e) { error = e.Message; return false; }
	}

	public static bool TryLoad(out Universe? universe, out DateTimeOffset savedAt, out string error)
	{
		universe = null; savedAt = default; error = "";
		if (!Exists) { error = "Kein Spielstand vorhanden"; return false; }
		try
		{
			using var f = FileAccess.Open(File, FileAccess.ModeFlags.Read);
			if (f == null) { error = "Datei nicht lesbar: " + FileAccess.GetOpenError(); return false; }
			(universe, savedAt) = SaveGameJson.Read(f.GetAsText());
			return true;
		}
		catch (SaveFormatException e) { error = e.Message; return false; }
	}
}
