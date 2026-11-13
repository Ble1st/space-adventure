using System.Linq;
using Godot;
using SpaceAdventure.Audio;
using SpaceAdventure.World;
using SpaceSim.Core.Audio;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure;

/// <summary>Selbsttest-Stufe Musik und Ton (Runde 12, eigene Datei): Zustandswechsel → Ziel-Lautstärken (ohne zu hören), Signale, Busse und Einstellungen.</summary>
public partial class SelfTest
{
	int _auStep;
	const float ShipModels_JawQuarter = ShipVisual.JawSeconds * 0.4f;
	float _auVol;

	/// <summary>Rückgabe true = Stufe fertig.</summary>
	bool AudioStage(int f)
	{
		var u = _m.Session.U;
		var mu = _m.TestMusic;
		bool Target(string track, float db) => Mathf.Abs(mu.TargetGainOf(track) - Mathf.DbToLinear(db)) < 0.01f;
		switch (f)
		{
			case 1:
				_m.TestMenu("new");
				mu.Hysteresis.MinHoldSeconds = 0f; mu.Hysteresis.CalmSeconds = 0f;   // Zustände sofort (die Hysterese prüfen die Core-Tests)
				return false;
			case 20:
				Check(mu.Loaded && mu.Playing, "Musik geladen, alle Schleifen laufen synchron");
				Check(AudioServer.GetBusIndex(AudioSettings.MusicBus) >= 0 && AudioServer.GetBusIndex(AudioSettings.EffectsBus) >= 0, "Busse Musik und Effekte");
				Check(mu.Mood == MusicMood.SafeFlight && Target("mood_safe", 0f) && mu.TargetGainOf("layer_combat") == 0f,
					$"sicherer Flug: Stimmung mood_safe, Gefechtsschicht stumm ({mu.Mood})");
				_m.TestTouchNearestRock();
				return false;
			case 60:
				Check(mu.Mood == MusicMood.Mining && Target("mood_mining", 0f) && mu.TargetGainOf("mood_safe") == 0f, $"Bergbau beim Bohren ({mu.Mood})");
				Check(mu.GainOf("mood_mining") > 0.1f && mu.GainOf("mood_mining") < 1f,   // Runde 13: Musik-Schritt mit 10 Hz — die Blende läuft in gröberen Stufen
					 $"weiche Überblendung ({mu.GainOf("mood_mining"):0.00})");
				{
					var st = u.Sectors[u.PlayerSector].Stations.Select(id => u.Stations[id]).First(s => !s.IsBuildSite);
					_m.TestDockAt(st.Id);
				}
				return false;
			case 80:
				Check(mu.Mood == MusicMood.Docked && Target("mood_docked", 0f), $"angedockt: mood_docked ({mu.Mood})");
				_m.TestUndock();
				return false;
			case 120:
			{
				// Überfall auf den Spieler: Gefecht → Schichtmischung laut Tabelle (pad −3, pulse −3, combat 0, tension −6), keine Stimmung
				var me = u.Ships[u.PlayerShip];
				var raider = u.ShipsById().First(ThreatMap.IsRaider);
				if (raider.Transit is not null) u.FinishTransit(raider);
				if (raider.Sector != me.Sector) u.MoveShip(raider, me.Sector);
				raider.Position = me.Position + new Vec2(25f, 0f); raider.DockedAt = default;
				Engage.StartRaid(u, raider, me, u.Sectors[me.Sector]);
				return false;
			}
			case 125:
				Check(mu.Mood == MusicMood.Combat && Target("layer_combat", 0f) && Target("layer_pad", -3f) && Target("layer_tension", -6f) && mu.TargetGainOf("mood_safe") == 0f,
					$"Gefecht: Schichten laut music.json ({mu.Mood})");
				mu.Sting("sting_jump", Time.GetTicksMsec() / 1000.0);
				Check(mu.LastSting == "sting_jump", "Signal spielt");
				foreach (var id in u.Fights.Encounters.SelectMany(e => e.Participants.Select(p => p.Ship)).ToList())
					if (u.Fights.Of(id) is { } fe && fe.Of(id) is { Active: true } fp) Engage.Leave(u, fe, fp, CombatExit.Fled);
				_m.TestOpenCommand(); _m.TestOpenCcArea("system");
				return false;
			case 140:
			{
				var (minus, mute, eff) = _m.HudRef.CcSystem.TestAudioButtons;
				Check(minus is not null && mute is not null && eff is not null, "Tontasten im System-Bereich");
				if (minus is null || mute is null) return true;
				_auVol = AudioSettings.MusicVolume;
				minus.TestPress();
				int bus = AudioServer.GetBusIndex(AudioSettings.MusicBus);
				Check(AudioSettings.MusicVolume == _auVol - AudioSettings.Step && Mathf.Abs(AudioServer.GetBusVolumeDb(bus) - Mathf.LinearToDb(AudioSettings.MusicVolume / 100f)) < 0.01f,
					$"Musik leiser: {AudioSettings.MusicVolume} %, Bus {AudioServer.GetBusVolumeDb(bus):0.0} dB");
				mute.TestPress();
				Check(AudioSettings.MusicMuted && AudioServer.IsBusMute(bus), "Musik stumm");
				var cfg = new ConfigFile();
				Check(cfg.Load(AudioSettings.Path) == Error.Ok && (bool)cfg.GetValue("audio", "music_muted", false), $"Einstellung gespeichert ({AudioSettings.Path})");
				Check(AudioSettings.Path != AudioSettings.DefaultPath, "Selbsttest schreibt nicht in die echte Einstellungsdatei");
				return false;
			}
			case 150:
			{
				var (_, mute, _) = _m.HudRef.CcSystem.TestAudioButtons;
				mute?.TestPress();
				AudioSettings.ChangeMusic(AudioSettings.Step);
				Check(!AudioSettings.MusicMuted && AudioSettings.MusicVolume == (int)_auVol, "Musik wieder an");
				_m.TestCloseCommand();
				// Spaltbacken: ein Felsbrecher II spaltet → Backen klappen auf und wieder zu
				var rb = ShipModels.Create("rockbreaker_ii", ShipModels.PlayerAccent);
				Check(rb is not null && rb.Jaws.Count >= 2, "Felsbrecher II hat Spaltbacken");
				if (rb is not null)
				{
					_m.AddChild(rb.Root);
					rb.SnapJaws();
					rb.AnimateJaws(ShipModels_JawQuarter);
					Check(rb.JawOpen > 0.1f, $"Spaltbacken öffnen sich beim Spalten ({rb.JawOpen:0.00} rad)");
					rb.AnimateJaws(1f);
					Check(rb.JawOpen == 0f, "Spaltbacken schließen wieder");
					rb.Root.QueueFree();
				}
				return false;
			}
			case 160:
			{
				var v = _m.Player.Visual;
				var acc = v?.Accents.Select(a => a.Mesh.GetSurfaceOverrideMaterial(a.Surface)).OfType<BaseMaterial3D>().FirstOrDefault();
				Check(acc is not null && acc.EmissionEnergyMultiplier >= 1f, $"Spieler-Akzent leuchtet kräftig ({acc?.EmissionEnergyMultiplier:0.0})");
				mu.Hysteresis.MinHoldSeconds = 6f; mu.Hysteresis.CalmSeconds = 5f;
				return true;
			}
		}
		return false;
	}
}
