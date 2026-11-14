using System;
using Godot;
using SpaceSim.Core.Ui;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Runde 31 (UI-ANPASSUNG.md Etappe 1): UI-Größe klein/normal/groß und der daraus folgende Godot-<c>content_scale_factor</c>. Rechnung im Core
/// (<see cref="UiLayout.Factor"/>): Ziel ist eine Tippfläche von 7 mm aus der Geräte-DPI; begrenzt, solange Bildschirme starr 1280 × 720 sind
/// (<see cref="UiLayout.FixedScreensCap"/> — heute 1, d. h. noch keine Vergrößerung; die Etappen E2–E5 heben die Grenze je Bildschirm). Gespeichert in
/// <c>user://settings.cfg</c> (Abschnitt „ui“) wie die Tonwerte, nie im Spielstand. Neu berechnet bei Größenwechsel des Fensters.
/// </summary>
public static class UiScale
{
	public static UiSize Size { get; private set; } = UiSize.Normal;
	/// <summary>Zuletzt gesetzter Faktor (1 = heutiges Raster).</summary>
	public static float Factor { get; private set; } = 1f;
	/// <summary>Faktor, den die Stufe ohne Obergrenze wollte (DPI-Ziel).</summary>
	public static float Wanted { get; private set; } = 1f;
	/// <summary>Selbsttest/Prüfwerkzeug: feste Obergrenze statt der für starre Bildschirme (null = normal).</summary>
	public static float? TestCap;
	/// <summary>Selbsttest/Prüfwerkzeug: angenommene DPI statt der des Bildschirms (null = Gerät).</summary>
	public static float? TestPpi;
	/// <summary>Prüfwerkzeug: Faktor fest vorgeben (null = aus Stufe, DPI und Obergrenze).</summary>
	public static float? TestForce;
	public static event Action? Changed;
	static Window? _root;

	public static void Init(Window root)
	{
		_root = root;
		var cfg = new ConfigFile();
		if (cfg.Load(SpaceAdventure.Audio.AudioSettings.Path) == Error.Ok)
			Size = (UiSize)Mathf.Clamp((int)cfg.GetValue("ui", "size", (int)Size), 0, 2);
		root.SizeChanged += Apply;
		Apply();
	}

	public static void Set(UiSize size) { Size = size; Save(); Apply(); }
	public static void Cycle() => Set((UiSize)(((int)Size + 1) % 3));

	public static string Name(UiSize s) => s switch { UiSize.Small => "klein", UiSize.Normal => "normal", _ => "groß" };
	public static string Text => $"UI-Größe {Name(Size)} ({Factor:0.00}×)";

	/// <summary>Faktor neu rechnen und setzen (Fenstergröße, DPI, Stufe).</summary>
	public static void Apply()
	{
		if (_root is null) return;
		var win = DisplayServer.WindowGetSize();
		if (win.X <= 1 || win.Y <= 1) win = _root.Size;   // headless: das Fenster meldet 0 × 0
		float w = Math.Max(1, win.X), h = Math.Max(1, win.Y);
		float ppi = TestPpi ?? DisplayServer.ScreenGetDpi();
		float cap = TestCap ?? UiLayout.FixedScreensCap(w, h);
		Wanted = UiLayout.Factor(Size, w, h, ppi, 9f);
		float f = TestForce ?? UiLayout.Factor(Size, w, h, ppi, cap);
		Factor = f;
		if (Math.Abs(_root.ContentScaleFactor - f) > 0.0001f) _root.ContentScaleFactor = f;
		Changed?.Invoke();
	}

	static void Save()
	{
		var cfg = new ConfigFile();
		cfg.Load(SpaceAdventure.Audio.AudioSettings.Path);   // andere Abschnitte (Ton) bleiben erhalten
		cfg.SetValue("ui", "size", (int)Size);
		cfg.Save(SpaceAdventure.Audio.AudioSettings.Path);
	}
}
