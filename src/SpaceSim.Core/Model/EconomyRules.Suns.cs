namespace SpaceSim.Core.Model;

/// <summary>
/// Sonnen (ERKUNDUNG.md „Sonnen“): Stellschrauben als eigener Teil der Regeln. Code-Vorgabe AUS = altes Verhalten (die Weltdatei lässt die Sonnen weg,
/// Welt bitgleich); die Standardwelt schaltet es an. Zonen als Vielfaches des Sonnenradius: Korona (Hüllenschaden) bis <see cref="SunCoronaFactor"/>,
/// Atmosphäre (Hitze) bis <see cref="SunAtmosphereFactor"/>; die Wegplanung meidet den Kreis bis <see cref="SunAvoidFactor"/>.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Hauptschalter Sonnen. Aus: keine Sonnen in der Welt (keine Hindernisse, keine Hitze, kein Schaden).</summary>
    public bool Suns;

    /// <summary>Außenrand der Atmosphäre (Hitze) als Vielfaches des Sonnenradius.</summary>
    public float SunAtmosphereFactor = 2.2f;
    /// <summary>Außenrand der Korona (Hüllenschaden) als Vielfaches des Sonnenradius.</summary>
    public float SunCoronaFactor = 1.35f;
    /// <summary>Wegplanung (NPC und Autopilot): Hindernisradius als Vielfaches des Sonnenradius (außerhalb der Korona).</summary>
    public float SunAvoidFactor = 1.5f;

    /// <summary>Hitze je Sekunde am Rand der Korona (linear von 0 am Atmosphärenrand; in der Korona weiter bis × 2 an der Oberfläche).</summary>
    public float SunHeatMax = 30f;
    /// <summary>Hüllenschaden in der Korona: Anteil der Höchsthülle je Sekunde am Koronarand (bis × 2 an der Oberfläche; nie unter 1 Hülle).</summary>
    public float SunHullPerSecond = 0.03f;
    /// <summary>Abstand von Stationen, Toren und Feldern zum Atmosphärenrand (Weltprüfung, Bauplätze).</summary>
    public float SunClearance = 40f;
}
