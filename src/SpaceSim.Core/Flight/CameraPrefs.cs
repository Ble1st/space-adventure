using System.Globalization;

namespace SpaceSim.Core.Flight;

/// <summary>
/// Runde 44: gespeicherte Kamera-Einstellungen (Modus, Rückansicht-Höhe/-Abstand, Schwenk-Invertierung). Reiner Datensatz — die Ansicht legt ihn in
/// <c>user://settings.cfg</c> (Abschnitt <c>camera</c>), nie in den Spielstand. Werte werden beim Lesen begrenzt; Unbekanntes wird zu
/// <see cref="CameraMode.Free"/> bzw. den Standardwerten.
/// </summary>
public sealed record CameraPrefs(CameraMode Mode, float ChaseHeight, float ChaseBack, bool InvertDrag)
{
    public static CameraPrefs Default => new(CameraMode.Free, OrbitCamera.DefaultChaseHeight, OrbitCamera.DefaultChaseBack, false);

    /// <summary>Aus gespeicherten Werten (Modus als Text, Zahlen beliebig) — begrenzt, kaputte Werte → Standard.</summary>
    public static CameraPrefs FromValues(string? mode, float height, float back, bool invert)
    {
        var m = mode switch { "chase" => CameraMode.Chase, "lock" => CameraMode.TargetLock, _ => CameraMode.Free };
        static float C(float v, float min, float max, float def) => !float.IsFinite(v) ? def : MathF.Min(max, MathF.Max(min, v));
        return new CameraPrefs(m,
            C(height, OrbitCamera.MinChaseHeight, OrbitCamera.MaxChaseHeight, OrbitCamera.DefaultChaseHeight),
            C(back, OrbitCamera.MinChaseBack, OrbitCamera.MaxChaseBack, OrbitCamera.DefaultChaseBack), invert);
    }

    /// <summary>Modus als gespeicherter Text.</summary>
    public string ModeKey => Mode switch { CameraMode.Chase => "chase", CameraMode.TargetLock => "lock", _ => "free" };

    /// <summary>Werte zum Speichern (Modus, Höhe, Abstand, Invertierung).</summary>
    public (string Mode, float Height, float Back, bool Invert) ToValues() => (ModeKey, ChaseHeight, ChaseBack, InvertDrag);

    /// <summary>Liest den Zustand einer Kamera.</summary>
    public static CameraPrefs Of(OrbitCamera cam) => new(cam.Mode, cam.ChaseHeight, cam.ChaseBack, cam.InvertDrag);

    /// <summary>Überträgt die Einstellungen auf eine Kamera (die freie Bahn bleibt, wie sie ist).</summary>
    public void ApplyTo(OrbitCamera cam)
    {
        cam.Mode = Mode;
        cam.InvertDrag = InvertDrag;
        cam.SetChase(ChaseHeight, ChaseBack);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{ModeKey} {ChaseHeight:0.#}/{ChaseBack:0.#}{(InvertDrag ? " inv" : "")}");
}
