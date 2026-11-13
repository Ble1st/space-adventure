using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Godot;

namespace SpaceAdventure.World;

public enum SfxId { Hit, Jump, Dock, Click, Explosion, Pickup, Zap, Crack }

/// <summary>
/// Prozedural erzeugte Klänge (keine Audiodateien nötig). Später durch echte Assets ersetzbar,
/// solange die Aufrufer weiter nur <see cref="Play"/> und <see cref="SetThrust"/> nutzen.
/// </summary>
public partial class Sfx : Node
{
	const int Rate = 22050;
	readonly Dictionary<SfxId, AudioStreamWav> _sounds = new();
	AudioStreamPlayer _oneShot = null!, _thrust = null!, _laser = null!;

	public override void _Ready()
	{
		var rng = new Random(1);
		_sounds[SfxId.Hit] = Make(0.3f, t => (Noise(rng) * 0.7f + Sine(90, t) * 0.5f) * Decay(t, 0.3f, 6f));
		_sounds[SfxId.Explosion] = Make(0.9f, t => (Noise(rng) * 0.8f + Sine(55, t) * 0.6f) * Decay(t, 0.9f, 3.5f));
		_sounds[SfxId.Jump] = Make(0.7f, t => Sine(200 + 1100 * t / 0.7f * (t / 0.7f), t) * 0.5f * Mathf.Sin(Mathf.Pi * t / 0.7f));
		_sounds[SfxId.Dock] = Make(0.35f, t => (t < 0.15f ? Sine(660, t) : t < 0.2f ? 0f : Sine(880, t)) * 0.4f * Decay(t, 0.35f, 2f));
		_sounds[SfxId.Pickup] = Make(0.09f, t => Sine(700 + 500 * t / 0.09f, t) * 0.35f * Decay(t, 0.09f, 3f));
		_sounds[SfxId.Zap] = Make(0.11f, t => Sine(1500 - 1100 * t / 0.11f, t) * 0.3f * Decay(t, 0.11f, 4f));
		_sounds[SfxId.Crack] = Make(0.25f, t => (Noise(rng) * 0.6f + Sine(160, t) * 0.4f) * Decay(t, 0.25f, 5f));
		_sounds[SfxId.Click] = Make(0.05f, t => Sine(1000, t) * 0.4f * Decay(t, 0.05f, 4f));

		_oneShot = new AudioStreamPlayer { Bus = SpaceAdventure.Audio.AudioSettings.EffectsBus, VolumeDb = -6f };
		AddChild(_oneShot);

		// Triebwerk: gedämpftes Rauschen als Schleife, Lautstärke folgt dem Schub
		float lp = 0f;
		var loop = Make(1f, _ => { lp += (Noise(rng) - lp) * 0.08f; return lp * 1.6f; }, loop: true);
		_thrust = new AudioStreamPlayer { Bus = SpaceAdventure.Audio.AudioSettings.EffectsBus, Stream = loop, VolumeDb = -80f };
		AddChild(_thrust);
		_thrust.Play();

		// Laser: gesägter Brummton mit leichtem Vibrato, nur hörbar während des Feuerns
		var buzz = Make(0.5f, t => ((t * 140f % 1f) * 2f - 1f) * 0.35f * (0.8f + 0.2f * Sine(14, t)), loop: true);
		_laser = new AudioStreamPlayer { Bus = SpaceAdventure.Audio.AudioSettings.EffectsBus, Stream = buzz, VolumeDb = -80f };
		AddChild(_laser);
		_laser.Play();
	}

	public void SetLaser(bool on) => _laser.VolumeDb = on ? -16f : -80f;

	public void Play(SfxId id)
	{
		if (!_sounds.TryGetValue(id, out var s) || _oneShot == null) return;
		_oneShot.Stream = s;
		_oneShot.Play();
	}

	/// <summary>0 = aus, 1 = voller Schub.</summary>
	public void SetThrust(float level) => _thrust.VolumeDb = level <= 0.01f ? -80f : Mathf.LinearToDb(level * 0.25f);

	static float Noise(Random r) => (float)(r.NextDouble() * 2 - 1);
	static float Sine(float hz, float t) => Mathf.Sin(Mathf.Tau * hz * t);
	static float Decay(float t, float len, float k) => Mathf.Exp(-k * t / len);

	static AudioStreamWav Make(float seconds, Func<float, float> sample, bool loop = false)
	{
		int n = (int)(seconds * Rate);
		var data = new byte[n * 2];
		for (int i = 0; i < n; i++)
		{
			float v = Mathf.Clamp(sample(i / (float)Rate), -1f, 1f);
			BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), (short)(v * 32767));
		}
		return new AudioStreamWav
		{
			Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data,
			LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
			LoopBegin = 0, LoopEnd = n,
		};
	}
}
