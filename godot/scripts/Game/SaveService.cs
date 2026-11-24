using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;

namespace SpaceAdventure.Game;

/// <summary>
/// Dateizugriff für Spielstände (user://). Der Core selbst liest und schreibt keine Dateien; Namensregel, Sortierung und Metadaten liegen in
/// <see cref="SaveSlots"/>. Mehrere Spielstände: je Stand <c>&lt;id&gt;.json</c> und eine kleine <c>&lt;id&gt;.meta.json</c> in <c>user://saves/</c>;
/// jedes laufende Spiel hat einen Autosave-Stand (<see cref="SaveSlots.AutosaveId"/>), benannte Stände entstehen über „Speichern unter“.
/// Die alte Einzeldatei <c>user://savegame.json</c> wird beim ersten Zugriff als Stand übernommen (die Datei, nicht ihr Format).
/// </summary>
public static class SaveService
{
	const string DefaultDir = "user://saves";
	const string LegacyFile = "savegame.json";
	static string _dir = DefaultDir;
	static bool _test;
	static bool _migrated;

	/// <summary>Laufendes Spiel: Kennung (Autosave-Stand), Startpaket.</summary>
	public static string CurrentGameId { get; private set; } = "";
	public static string CurrentPackage { get; private set; } = "";
	public static string CurrentPackageName { get; private set; } = "";

	/// <summary>Selbsttest und Werkzeuge schreiben in ein eigenes Verzeichnis (<c>user://test-saves/&lt;name&gt;</c>) und lassen die echten Stände unberührt.</summary>
	public static void UseFile(string name)
	{
		_test = true;
		_dir = "user://test-saves/" + System.IO.Path.GetFileNameWithoutExtension(name);
		_migrated = true;   // Prüfläufe übernehmen nie die alte Einzeldatei
		CurrentGameId = "";
	}

	/// <summary>Prüfläufe: Verzeichnis leeren (immer frisch beginnen).</summary>
	public static void ClearDir()
	{
		var d = DirAccess.Open(_dir);
		if (d == null) return;
		foreach (var f in d.GetFiles()) d.Remove(f);
	}

	public static string DirPath => _dir;
	static string PathOf(string id) => _dir + "/" + id + ".json";
	static string MetaOf(string id) => _dir + "/" + id + ".meta.json";

	static void EnsureDir()
	{
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(_dir));
		if (!_migrated) { _migrated = true; MigrateLegacy(); }
	}

	/// <summary>Alte Einzeldatei als Stand übernehmen; das Original bleibt als <c>savegame.json.migrated</c> liegen.</summary>
	static void MigrateLegacy()
	{
		if (_test || !FileAccess.FileExists("user://" + LegacyFile)) return;
		try
		{
			string json;
			using (var f = FileAccess.Open("user://" + LegacyFile, FileAccess.ModeFlags.Read)) { if (f == null) return; json = f.GetAsText(); }
			var ids = ListIds();
			string id = SaveSlots.IdFor(SaveSlots.MigratedName, ids);
			SaveSlotMeta meta;
			string gameId = SaveSlots.NewGameId(DateTimeOffset.UtcNow, Array.Empty<string>());
			try
			{
				var (u, at) = SaveGameJson.Read(json);
				meta = SaveSlots.Describe(u, id, SaveSlots.MigratedName, gameId, "", "", autosave: false, at);
			}
			catch (Exception)   // nicht lesbar: trotzdem übernehmen, die Liste zeigt ihn, Laden meldet den Fehler
			{
				meta = new SaveSlotMeta { Id = id, Name = SaveSlots.MigratedName, GameId = gameId,
					SavedAt = DateTimeOffset.FromUnixTimeSeconds((long)FileAccess.GetModifiedTime("user://" + LegacyFile)) };
			}
			if (!WriteAtomic(PathOf(id), json, out var e1) || !WriteAtomic(MetaOf(id), SaveSlots.WriteMeta(meta), out var e2))
			{ GD.PushWarning("Spielstand-Übernahme fehlgeschlagen"); return; }
			DirAccess.Open("user://")?.Rename(LegacyFile, LegacyFile + ".migrated");
			GD.Print($"[save] alte Datei {LegacyFile} als Stand „{meta.Name}“ ({id}) übernommen");
		}
		catch (Exception e) { GD.PushWarning("Spielstand-Übernahme: " + e.Message); }
	}

	static List<string> ListIds()
	{
		var d = DirAccess.Open(_dir);
		if (d == null) return new();
		return d.GetFiles().Where(f => f.EndsWith(".json") && !f.EndsWith(".meta.json")).Select(f => f[..^5]).ToList();
	}

	/// <summary>Alle Stände, neueste zuerst.</summary>
	public static List<SaveSlotMeta> List()
	{
		EnsureDir();
		var list = new List<SaveSlotMeta>();
		foreach (var id in ListIds())
		{
			SaveSlotMeta? m = null;
			if (FileAccess.FileExists(MetaOf(id)))
				using (var f = FileAccess.Open(MetaOf(id), FileAccess.ModeFlags.Read)) if (f != null) m = SaveSlots.ReadMeta(f.GetAsText());
			m ??= new SaveSlotMeta { Name = id, SavedAt = DateTimeOffset.FromUnixTimeSeconds((long)FileAccess.GetModifiedTime(PathOf(id))), Autosave = id.StartsWith(SaveSlots.AutosavePrefix) };
			list.Add(m with { Id = id });
		}
		return SaveSlots.Sorted(list);
	}

	public static SaveSlotMeta? Newest => SaveSlots.Newest(List());
	public static bool Exists => Newest is not null;
	/// <summary>Zeitpunkt des neuesten Stands, null ohne Stand — für „Weiter“ auf dem Startbildschirm, ohne den Stand zu laden.</summary>
	public static DateTimeOffset? SavedAt => Newest?.SavedAt;

	/// <summary>Neues Spiel: neue Spielkennung (eigener Autosave-Stand).</summary>
	public static void BeginNewGame(string package, string packageName)
	{
		CurrentGameId = SaveSlots.NewGameId(DateTimeOffset.UtcNow, List().Select(m => m.GameId));
		CurrentPackage = package;
		CurrentPackageName = packageName;
	}

	static void EnsureGame() { if (CurrentGameId.Length == 0) BeginNewGame(CurrentPackage, CurrentPackageName); }

	/// <summary>Autosave: in den Autosave-Stand des laufenden Spiels (Name „Autosave · Paket“).</summary>
	public static bool Save(Universe u, out string error)
	{
		EnsureGame();
		string name = "Autosave · " + (CurrentPackageName.Length > 0 ? CurrentPackageName : "Spiel");
		return Write(u, SaveSlots.AutosaveId(CurrentGameId), name, autosave: true, out error);
	}

	/// <summary>„Speichern unter“: neuer benannter Stand oder <paramref name="overwriteId"/> überschreiben. Gibt die Kennung zurück (null bei Fehler).</summary>
	public static string? SaveAs(Universe u, string name, string? overwriteId, out string error)
	{
		EnsureGame();
		var list = List();
		if (SaveSlots.NameIssue(name, list, overwriteId) is { } issue) { error = issue; return null; }
		string id = overwriteId ?? SaveSlots.IdFor(SaveSlots.CleanName(name), list.Select(m => m.Id));
		return Write(u, id, name, autosave: false, out error) ? id : null;
	}

	static bool Write(Universe u, string id, string name, bool autosave, out string error)
	{
		error = "";
		try
		{
			EnsureDir();
			var now = DateTimeOffset.UtcNow;
			var json = SaveGameJson.Write(u, now);
			var meta = SaveSlots.Describe(u, id, name, CurrentGameId, CurrentPackage, CurrentPackageName, autosave, now);
			return WriteAtomic(PathOf(id), json, out error) && WriteAtomic(MetaOf(id), SaveSlots.WriteMeta(meta), out error);
		}
		catch (Exception e) { error = e.Message; return false; }
	}

	/// <summary>Schreibt zuerst in eine temporäre Datei und benennt dann um, damit ein Absturz keinen halben Spielstand hinterlässt.</summary>
	static bool WriteAtomic(string path, string text, out string error)
	{
		error = "";
		string tmp = path + ".tmp";
		using (var f = FileAccess.Open(tmp, FileAccess.ModeFlags.Write))
		{
			if (f == null) { error = "Datei nicht beschreibbar: " + FileAccess.GetOpenError(); return false; }
			f.StoreString(text);
		}
		var err = DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(tmp), ProjectSettings.GlobalizePath(path));
		if (err != Error.Ok) { error = "Umbenennen fehlgeschlagen: " + err; return false; }
		return true;
	}

	/// <summary>Stand laden; danach ist sein Spiel das laufende (Autosave geht in dessen Autosave-Stand).</summary>
	public static bool TryLoad(string id, out Universe? universe, out DateTimeOffset savedAt, out string error)
	{
		universe = null; savedAt = default; error = "";
		EnsureDir();
		if (!FileAccess.FileExists(PathOf(id))) { error = "Kein Spielstand vorhanden"; return false; }
		try
		{
			using (var f = FileAccess.Open(PathOf(id), FileAccess.ModeFlags.Read))
			{
				if (f == null) { error = "Datei nicht lesbar: " + FileAccess.GetOpenError(); return false; }
				(universe, savedAt) = SaveGameJson.Read(f.GetAsText());
			}
			var meta = List().FirstOrDefault(m => m.Id == id);
			CurrentGameId = meta?.GameId is { Length: > 0 } g ? g : SaveSlots.NewGameId(DateTimeOffset.UtcNow, Array.Empty<string>());
			CurrentPackage = meta?.Package ?? "";
			CurrentPackageName = meta?.PackageName ?? "";
			return true;
		}
		catch (SaveFormatException e) { error = e.Message; universe = null; return false; }
		catch (Exception e) { error = e.Message; universe = null; return false; }
	}

	/// <summary>„Weiter“: den neuesten Stand laden.</summary>
	public static bool TryLoadNewest(out Universe? universe, out DateTimeOffset savedAt, out string error)
	{
		universe = null; savedAt = default;
		if (Newest is not { } m) { error = "Kein Spielstand vorhanden"; return false; }
		return TryLoad(m.Id, out universe, out savedAt, out error);
	}

	public static bool Delete(string id)
	{
		EnsureDir();
		bool ok = DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(PathOf(id))) == Error.Ok;
		DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(MetaOf(id)));
		return ok;
	}
}
