using System;
using Godot;
using SpaceSim.Core.Ui;

namespace SpaceAdventure.Ui.Kit;

/// <summary>
/// Runde 31 (UI-ANPASSUNG.md Etappe 1): UI-Größe klein/normal/groß und der daraus folgende UI-Faktor. Rechnung im Core (<see cref="UiLayout.Factor"/>):
/// Ziel ist eine Tippfläche von 7 mm aus der Geräte-DPI. Gespeichert in <c>user://settings.cfg</c> (Abschnitt „ui“) wie die Tonwerte, nie im Spielstand.
/// <para>
/// Runde 33 (Etappe 2): <b>Faktor je Schicht</b> statt global über <c>content_scale_factor</c>. Die Wurzel bleibt bei 1 (Kamera, Antippen, 3D-Beschriftungen
/// rechnen unverändert in Bildpunkten); eine anpassungsfähige Schicht (heute das Flug-HUD samt Meldung/Hinweis im Flug) skaliert sich selbst mit
/// <see cref="Factor"/> und ordnet sich in der verkleinerten logischen Fläche neu an (<see cref="FlightHudLayout"/>). Die noch starren Vollbild-Bildschirme
/// (Dock, Kommandozentrale, Menü) bleiben bei <see cref="ScreensFactor"/> = 1, bis E3–E5 sie anpassen. Obergrenze der Flug-Schicht: die Mindestfläche
/// 960 × 540 (<see cref="UiLayout.MaxFactor"/>).
/// </para>
/// </summary>
public static class UiScale
{
	public static UiSize Size { get; private set; } = UiSize.Normal;
	/// <summary>Faktor der anpassungsfähigen Schichten (Flug-HUD); 1 = heutiges Raster.</summary>
	public static float Factor { get; private set; } = 1f;
	/// <summary>Faktor der noch starren Vollbild-Bildschirme (E3–E5): 1.</summary>
	public static float ScreensFactor => 1f;
	/// <summary>Faktor, den die Stufe ohne Obergrenze wollte (DPI-Ziel).</summary>
	public static float Wanted { get; private set; } = 1f;
	/// <summary>Selbsttest/Prüfwerkzeug: feste Obergrenze statt der für die Flug-Schicht (null = normal).</summary>
	public static float? TestCap;
	/// <summary>Selbsttest/Prüfwerkzeug: angenommene DPI statt der des Bildschirms (null = Gerät).</summary>
	public static float? TestPpi;
	/// <summary>
	/// Runde 33: Prüfläufe (Selbsttest, UI-Shots, Handy-Fotos) nehmen diesen Faktor, solange kein <see cref="TestPpi"/>/<see cref="TestForce"/> gesetzt ist —
	/// sonst hinge ihr Bild vom Prüfrechner ab (headless ist das Fenster 64 × 64 groß, die DPI-Rechnung ergäbe „normal“ = 1,15 und ein kompaktes HUD).
	/// </summary>
	public static float? PinnedFactor;
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
	/// <summary>Runde 33 (Handytest: „UI-Größe normal (1,00×, Tasten …“ war im Menü abgeschnitten): kurze Zeile; Faktor und mm stehen in <see cref="SubText"/>.</summary>
	public static string Text => $"UI-Größe: {Name(Size)}";
	public static string SubText => $"{Factor:0.00}×" + (float.IsNaN(TapMm) ? "" : $" · Taste etwa {TapMm:0.0} mm");
	/// <summary>Runde 32: Größe einer Raster-Taste (56 px) im Flug bei der aktuellen Stufe in mm (NaN ohne bekannte DPI).</summary>
	public static float TapMm { get; private set; } = float.NaN;
	/// <summary>
	/// Runde 33: ehrlicher Text. Die DPI kommt von <see cref="DisplayServer.ScreenGetDpi"/>; auf Android ist das die Dichteklasse des Geräts (z. B. 450 statt der
	/// physischen 396 ppi des A15) — daher „etwa“. Die Stufe wirkt im Flug; Dock und Kommandozentrale folgen mit den nächsten Ausbaustufen.
	/// </summary>
	public static string Describe() =>
		$"UI-Größe {Name(Size)}: Flug {Factor:0.00}×, Dock {DockFactor:0.00}×" + (float.IsNaN(TapMm) ? "" : $", Tasten etwa {TapMm:0.0} mm") + "."
		+ (Wanted > Factor + 0.001f ? $" Mehr ({Wanted:0.00}×) passt nicht auf diese Fläche." : "")
		+ $" Kommandozentrale {CommandFactor:0.00}×.";
	/// <summary>Runde 34 (Handytest r33: der lange Hinweis über dem Menü war abgeschnitten): kurze Fassung für den Hinweis nach dem Umschalten.</summary>
	public static string ShortText() => $"UI-Größe {Name(Size)}: Flug {Factor:0.00}×, Dock {DockFactor:0.00}×, Zentrale {CommandFactor:0.00}×" + (float.IsNaN(TapMm) ? "" : $" · Taste etwa {TapMm:0.0} mm");
	/// <summary>Runde 34: wirksamer Faktor der Dock-Schicht (setzt die Oberfläche; kann wegen der Mindestfläche der Bereiche unter <see cref="Factor"/> liegen).</summary>
	public static float DockFactor { get; set; } = 1f;
	/// <summary>Runde 35: wirksamer Faktor der Kommandozentrale.</summary>
	public static float CommandFactor { get; set; } = 1f;
	/// <summary>Runde 37 (E5): wirksamer Faktor der Menü-Schicht (Hamburger-Menü, Sternenkarte).</summary>
	public static float MenuFactor { get; set; } = 1f;

	/// <summary>Faktor neu rechnen und setzen (Fenstergröße, DPI, Stufe). Die Wurzel bleibt bei 1 (Faktor je Schicht).</summary>
	public static void Apply()
	{
		if (_root is null) return;
		var win = DisplayServer.WindowGetSize();
		if (win.X <= 1 || win.Y <= 1) win = _root.Size;   // headless: das Fenster meldet 0 × 0
		float w = Math.Max(1, win.X), h = Math.Max(1, win.Y);
		float ppi = TestPpi ?? DisplayServer.ScreenGetDpi();
		float cap = TestCap ?? UiLayout.MaxFactor(w, h, UiLayout.MinLogicalWidth, UiLayout.MinLogicalHeight);
		Wanted = UiLayout.Factor(Size, w, h, ppi, 9f);
		float f = TestForce ?? (TestPpi is null && PinnedFactor is { } pf ? pf : UiLayout.Factor(Size, w, h, ppi, cap));
		Factor = f;
		TapMm = ppi > 0f ? UiLayout.Millimetres(UiLayout.BaseTapPx, w, h, f, ppi) : float.NaN;
		if (Math.Abs(_root.ContentScaleFactor - 1f) > 0.0001f) _root.ContentScaleFactor = 1f;
		// Runde 34b (Handytest r34: Dock „groß“ nur 1,20× — vermutlich Safe-Area-Ränder): gemeldete Ränder ins Protokoll und in die Diagnosezeile
		var ins = SafeInsets1();
		InsetsText = $"Safe Area L{ins.Left:0} O{ins.Top:0} R{ins.Right:0} U{ins.Bottom:0} (Fenster {w:0}×{h:0}, DPI {ppi:0})";
		if (InsetsText != _lastInsetsLog) { _lastInsetsLog = InsetsText; GD.Print("[ui] " + InsetsText); }
		Changed?.Invoke();
	}

	/// <summary>Runde 33b/34, Selbsttest: Safe-Area-Ränder vorgeben (Bildpunkte der Wurzel bei Faktor 1, null = Gerät).</summary>
	public static UiInsets? TestInsets;

	/// <summary>
	/// Runde 34: Safe-Area-Ränder (Kamera-Aussparung, Gestenleiste) an allen vier Seiten in Bildpunkten der Wurzel (Faktor 1); nur auf Mobilgeräten (am Desktop
	/// meldet die Safe Area den Arbeitsbereich des Bildschirms, nicht das Fenster). Jede Schicht teilt durch ihren Faktor.
	/// </summary>
	public static UiInsets SafeInsets1()
	{
		if (TestInsets is { } t) return t;
		if (!OS.HasFeature("mobile")) return new UiInsets(0, 0, 0, 0);
		var win = DisplayServer.WindowGetSize();
		var safe = DisplayServer.GetDisplaySafeArea();
		if (win.X <= 0 || win.Y <= 0 || safe.Size.X <= 0 || safe.Size.Y <= 0) return new UiInsets(0, 0, 0, 0);
		return UiLayout.Usable(win.X, win.Y, safe.Position.X, safe.Position.Y, safe.Size.X, safe.Size.Y, 1f);
	}

	public static UiInsets Div(UiInsets i, float f) => f <= 0f ? i : new UiInsets(i.Left / f, i.Top / f, i.Right / f, i.Bottom / f);

	/// <summary>Runde 34b: zuletzt gemeldete Safe-Area-Ränder (Bildpunkte bei Faktor 1), Fenster und DPI — für die Kamera-Diagnosezeile.</summary>
	public static string InsetsText { get; private set; } = "";
	static string _lastInsetsLog = "";

	static void Save()
	{
		var cfg = new ConfigFile();
		cfg.Load(SpaceAdventure.Audio.AudioSettings.Path);   // andere Abschnitte (Ton) bleiben erhalten
		cfg.SetValue("ui", "size", (int)Size);
		cfg.Save(SpaceAdventure.Audio.AudioSettings.Path);
	}
}
