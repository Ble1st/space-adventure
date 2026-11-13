using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using SpaceSim.Core.Audio;

namespace SpaceAdventure.Audio;

/// <summary>
/// Runde 12: Musikregler (eigene Komponente, nicht Teil von Sfx). Liest <c>res://assets/music/music.json</c> als Daten: Schichten (<c>layer_*</c>) und
/// Stimmungen (<c>mood_*</c>) laufen alle synchron in Schleife (ein gemeinsamer Start), ihre Lautstärken werden je Zustand der Tabelle <c>states</c> weich
/// geblendet (<c>crossfade_seconds</c>); Signale (<c>sting_*</c>) sind One-Shots mit Ducking der Musik (<c>sting_duck_music_db</c>). Der Zustand kommt aus der
/// reinen Core-Funktion <see cref="MusicState"/> (mit Hysterese) — der Regler entscheidet nichts selbst. Bus „Musik“ (<see cref="AudioSettings"/>).
/// </summary>
public partial class MusicDirector : Node
{
	public const string Dir = "res://assets/music/";
	const float Silent = -80f;

	sealed class Track { public string File = ""; public string Role = ""; public float FileDb; public AudioStreamPlayer? Player; public float Gain; public float TargetGain; }

	readonly Dictionary<string, Track> _tracks = new();
	readonly Dictionary<string, (string? Mood, Dictionary<string, float> Layers)> _states = new();
	readonly Dictionary<string, AudioStreamPlayer> _stings = new();
	float _crossfade = 3f, _duckDb = -4f, _duck;
	double _duckUntil;
	bool _started;

	/// <summary>Aktueller (gehaltener) Zustand.</summary>
	public MusicMood Mood { get; private set; } = MusicMood.Menu;
	/// <summary>Hysterese des Zustands (Selbsttest: kurze Zeiten).</summary>
	public MusicState.Hysteresis Hysteresis { get; } = new();
	/// <summary>Sind die Musikdaten geladen?</summary>
	public bool Loaded => _tracks.Count > 0;
	/// <summary>Zuletzt gespieltes Signal (Selbsttest).</summary>
	public string LastSting { get; private set; } = "";

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Load();
	}

	void Load()
	{
		const string path = Dir + "music.json";
		if (!FileAccess.FileExists(path)) return;
		try
		{
			using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
			var root = doc.RootElement;
			if (root.TryGetProperty("crossfade_seconds", out var cf)) _crossfade = Math.Max(0.1f, cf.GetSingle());
			if (root.TryGetProperty("sting_duck_music_db", out var dk)) _duckDb = dk.GetSingle();
			foreach (var f in root.GetProperty("files").EnumerateArray())
			{
				string file = f.GetProperty("file").GetString() ?? "";
				string role = f.GetProperty("role").GetString() ?? "";
				float db = f.TryGetProperty("volume_db", out var v) ? v.GetSingle() : 0f;
				string key = file.Replace(".wav", "");
				var stream = ResourceLoader.Exists(Dir + file) ? ResourceLoader.Load<AudioStream>(Dir + file) : null;
				if (role == "sting")
				{
					if (stream is null) continue;
					var sp = new AudioStreamPlayer { Stream = stream, Bus = AudioSettings.MusicBus, VolumeDb = db };
					AddChild(sp);
					_stings[key] = sp;
					continue;
				}
				var t = new Track { File = key, Role = role, FileDb = db };
				if (stream is not null)
				{
					t.Player = new AudioStreamPlayer { Stream = stream, Bus = AudioSettings.MusicBus, VolumeDb = Silent };
					AddChild(t.Player);
				}
				_tracks[key] = t;
			}
			foreach (var s in root.GetProperty("states").EnumerateObject())
			{
				string? mood = s.Value.TryGetProperty("mood", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
				var layers = new Dictionary<string, float>();
				foreach (var part in new[] { "layers", "optional_layers" })
					if (s.Value.TryGetProperty(part, out var l) && l.ValueKind == JsonValueKind.Object)
						foreach (var p in l.EnumerateObject()) layers[p.Name] = p.Value.GetSingle();
				_states[s.Name] = (mood, layers);
			}
		}
		catch (Exception ex) { GD.PushWarning($"music.json nicht lesbar: {ex.Message}"); }
	}

	/// <summary>Alle Schleifen gemeinsam starten (stumm) — danach nur noch Lautstärken.</summary>
	void Start()
	{
		if (_started) return;
		_started = true;
		foreach (var t in _tracks.Values) t.Player?.Play();
	}

	/// <summary>Ziel-Lautstärke (dB, ohne Ducking) einer Schleife im Zustand <paramref name="mood"/>; null = stumm.</summary>
	public float? TargetDb(MusicMood mood, string track)
	{
		if (!_states.TryGetValue(MusicState.Key(mood), out var st)) return null;
		if (st.Mood == track) return 0f;
		return st.Layers.TryGetValue(track, out var db) ? db : null;
	}

	/// <summary>Aktueller Lautstärkeanteil (0..1) einer Schleife (Selbsttest).</summary>
	public float GainOf(string track) => _tracks.TryGetValue(track, out var t) ? t.Gain : 0f;
	/// <summary>Ziel-Lautstärkeanteil einer Schleife (Selbsttest).</summary>
	public float TargetGainOf(string track) => _tracks.TryGetValue(track, out var t) ? t.TargetGain : 0f;
	/// <summary>Namen aller Schleifen.</summary>
	public IEnumerable<string> Tracks => _tracks.Keys;
	/// <summary>Laufen die Schleifen?</summary>
	public bool Playing => _started;

	/// <summary>Je Bild: neuen Rohzustand einspeisen, Ziele setzen, weich blenden.</summary>
	public void Step(MusicMood raw, double now, float delta)
	{
		if (!Loaded) return;
		Start();
		var mood = Hysteresis.Update(raw, now);
		if (mood != Mood || !_targetsSet) { Mood = mood; SetTargets(mood); }
		float rate = delta / _crossfade;
		_duck = now < _duckUntil ? Mathf.MoveToward(_duck, 1f, delta * 4f) : Mathf.MoveToward(_duck, 0f, delta);
		foreach (var t in _tracks.Values)
		{
			t.Gain = Mathf.MoveToward(t.Gain, t.TargetGain, rate);
			if (t.Player is null) continue;
			t.Player.VolumeDb = t.Gain <= 0.001f ? Silent : Mathf.LinearToDb(t.Gain) + t.FileDb + _duck * _duckDb;
		}
	}

	bool _targetsSet;
	void SetTargets(MusicMood mood)
	{
		_targetsSet = true;
		foreach (var t in _tracks.Values) t.TargetGain = TargetDb(mood, t.File) is { } db ? Mathf.DbToLinear(db) : 0f;
	}

	/// <summary>Signal abspielen (Schlüssel z. B. „sting_victory“); die Musik weicht für seine Dauer zurück.</summary>
	public void Sting(string key, double now)
	{
		if (!_stings.TryGetValue(key, out var sp)) return;
		LastSting = key;
		sp.Stop();
		sp.Play();
		double len = sp.Stream?.GetLength() ?? 3.0;
		_duckUntil = now + Math.Max(1.0, len);
	}

	/// <summary>App-Pause: Musik anhalten bzw. fortsetzen (Position bleibt, Schichten bleiben synchron).</summary>
	public void SetPaused(bool paused)
	{
		foreach (var t in _tracks.Values) if (t.Player is not null) t.Player.StreamPaused = paused;
		foreach (var s in _stings.Values) s.StreamPaused = paused;
	}
}
