namespace SpaceSim.Core.Ui;

/// <summary>
/// Runde 34 (UI-ANPASSUNG.md Etappe E3): Gerüst eines Dock-Bereichs — Kopfleiste (Stationsname, Kasse), linke Bereichsleiste, Mittelteil, rechte
/// Schiffsspalte (Handel, Reparatur …), „← Menü“ und „Abdocken“. <see cref="InnerW"/>/<see cref="InnerH"/> ist die Fläche, die der Bereich selbst füllt.
/// </summary>
public sealed record DockShell(bool Compact, UiRect Area, UiRect Top, UiRect Rail, UiRect Center, UiRect Ship, UiRect Back, UiRect Undock, UiRect Credits, UiRect AlertSlot)
{
    /// <summary>Innenfläche des Mittelteils (Rand 16 links/rechts, 10 oben/unten).</summary>
    public float InnerW => Center.W - 32f;
    public float InnerH => Center.H - 20f;
    public bool HasShip => Ship.W > 0f;
}

/// <summary>
/// Runde 34 — Layout der Dock-Schicht als reine Rechnung (wie <see cref="FlightHudLayout"/>). <b>Klassisch</b> (nutzbare Fläche ≥ 1280 × 720): die bisherige
/// Anordnung, nun auf die ganze Fläche gedehnt (Mittelteil wächst in Breite und Höhe, Schiffsspalte fest 342 breit, Fußleiste am unteren Rand), höchstens
/// <see cref="MaxWidth"/> breit (Desktop-Fenster), Safe Area an allen Seiten; bei 1280 × 720 ohne Ränder exakt die bisherigen Lagen. <b>Kompakt</b>
/// (kleiner, d. h. UI-Faktor &gt; 1): dieselbe Anordnung mit schmalerer Kopfleiste, „Abdocken“ oben rechts, „← Menü“ unter der Bereichsleiste und einer
/// schmaleren Schiffsspalte (260) — so bleibt dem Mittelteil die Höhe. <see cref="FitFactor"/> senkt den Faktor, bis jeder Bereich seine Mindestfläche hat.
/// </summary>
public static class DockLayout
{
    public const float BaseW = UiLayout.BaseWidth, BaseH = UiLayout.BaseHeight;
    /// <summary>Breiter als dies wird das Dock nicht (Desktop-Fenster); darüber liegt es mittig.</summary>
    public const float MaxWidth = 1600f;
    /// <summary>Mindest-Innenfläche eines Bereichs mit Schiffsspalte (Handel, Reparatur, Lager, Behörde) bzw. ohne (Werft, Ausrüster, Hangar, Stationsinfo …).</summary>
    /// Ohne Schiffsspalte gilt die Entwurfsbreite 1008: schmaler kürzten Stationsinfo, Ausrüster, Werft und Hangar ihre Texte (Prüfwerkzeug Runde 34).
    public const float MinInnerShipW = 600f, MinInnerW = 1008f, MinInnerH = 400f;
    /// <summary>Breite der Bereichsleiste: Entwurf 200, kompakt 170.</summary>
    public const float RailW = 200f, RailCompactW = 170f;
    /// <summary>Dock-Menü: Kartenbreite und -höhe im 1280 × 720-Entwurf; Mindesthöhe, bei der die Bereichskacheln noch rollen können.</summary>
    public const float MenuW = 580f, MenuH = 662f, MenuMinH = 480f;

    static UiRect Usable(float w, float h, UiInsets s) =>
        new(s.Left, s.Top, MathF.Max(0f, w - s.Left - s.Right), MathF.Max(0f, h - s.Top - s.Bottom));

    /// <summary>Gerüst für die Fläche <paramref name="w"/> × <paramref name="h"/> (logisch, Einheiten der Dock-Schicht).</summary>
    public static DockShell Shell(float w, float h, UiInsets safe, bool ship)
    {
        var a = Usable(w, h, safe);
        bool compact = a.W < BaseW - 0.5f || a.H < BaseH - 0.5f;
        if (a.W > MaxWidth) a = new UiRect(a.X + (a.W - MaxWidth) / 2f, a.Y, MaxWidth, a.H);
        float x0 = a.X, y0 = a.Y, x1 = a.Right, y1 = a.Bottom;
        if (!compact)
        {
            var top = new UiRect(x0 + 20, y0 + 10, a.W - 40, 64);
            float bodyH = a.H - 180f;
            var rail = new UiRect(x0 + 14, y0 + 84, 200, bodyH);
            float cw = ship ? a.W - 590f : a.W - 240f;
            var center = new UiRect(x0 + 224, y0 + 84, cw, bodyH);
            var shipCol = ship ? new UiRect(center.Right + 10, y0 + 84, 342, bodyH) : new UiRect(center.Right, y0 + 84, 0, bodyH);
            var back = new UiRect(x0 + 14, y1 - 72, 160, 58);
            var undock = new UiRect(x1 - 214, y1 - 72, 200, 58);
            var credits = new UiRect(top.Right - 300, top.Y, 300, 60);
            var slot = new UiRect(x0 + 420, y0, MathF.Max(200f, MathF.Min(600f, credits.X - 20 - (x0 + 420))), 56);
            return new DockShell(false, a, top, rail, center, shipCol, back, undock, credits, slot);
        }
        else
        {
            var undock = new UiRect(x1 - 14 - 180, y0 + 8, 180, 56);
            var top = new UiRect(x0 + 14, y0 + 6, undock.X - 10 - (x0 + 14), 60);
            var credits = new UiRect(top.Right - 280, top.Y, 280, 60);
            var back = new UiRect(x0 + 14, y1 - 14 - 58, RailCompactW, 58);
            var rail = new UiRect(x0 + 14, y0 + 74, RailCompactW, back.Y - 8 - (y0 + 74));
            float bodyH = y1 - 14 - (y0 + 74);
            float shipW = ship ? 260f : 0f;
            float cx = x0 + 14 + RailCompactW + 10;
            float cw = x1 - 14 - cx - (ship ? 10f + shipW : 0f);
            var center = new UiRect(cx, y0 + 74, MathF.Max(0f, cw), bodyH);
            var shipCol = new UiRect(center.Right + (ship ? 10f : 0f), y0 + 74, shipW, bodyH);
            // Meldung über dem Dock: über dem Stationsnamen bis vor die Kasse (die Kopfzeile ist schmal; „Abdocken“ bleibt frei)
            var slot = new UiRect(x0 + 14, y0, MathF.Max(200f, MathF.Min(600f, credits.X - 10 - (x0 + 14))), 56);
            return new DockShell(true, a, top, rail, center, shipCol, back, undock, credits, slot);
        }
    }

    /// <summary>Karte des Dock-Menüs: höchstens 580 × 662 (Entwurf), waagerecht mittig, senkrecht mittig mit dem Entwurfsabstand 30 bei 720 Höhe.</summary>
    public static UiRect MenuCard(float w, float h, UiInsets safe)
    {
        var a = Usable(w, h, safe);
        float cw = MathF.Min(MenuW, a.W - 20f), ch = MathF.Min(MenuH, a.H - 58f);
        float y = a.Y + MathF.Max(4f, (a.H - ch) / 2f + 1f);
        if (y + ch > a.Bottom) ch = MathF.Max(0f, a.Bottom - y - 4f);
        return new UiRect(a.X + (a.W - cw) / 2f, y, cw, ch);
    }

    /// <summary>Hat jeder Dock-Bereich (mit und ohne Schiffsspalte) und das Dock-Menü auf dieser Fläche seine Mindestfläche?</summary>
    public static bool Fits(float w, float h, UiInsets safe)
    {
        var s = Shell(w, h, safe, ship: true);
        var n = Shell(w, h, safe, ship: false);
        var m = MenuCard(w, h, safe);
        return s.InnerW >= MinInnerShipW - 0.01f && n.InnerW >= MinInnerW - 0.01f && n.InnerH >= MinInnerH - 0.01f && s.InnerH >= MinInnerH - 0.01f
            && m.H >= MenuMinH - 0.01f && m.W >= MenuW - 0.01f;
    }

    /// <summary>
    /// Größter Faktor ≤ <paramref name="wanted"/> (Schritte 0,01, nie unter 1), bei dem <see cref="Fits"/> gilt — gleiche Idee wie
    /// <see cref="FlightHudLayout.FitFactor"/>: lieber etwas kleiner als ein zerquetschter Bereich. <paramref name="w1"/>/<paramref name="h1"/> und
    /// <paramref name="safe1"/> bei Faktor 1. Ein Faktor für die ganze Dock-Schicht (alle Bereiche gleich groß, nichts springt beim Wechsel).
    /// </summary>
    public static float FitFactor(float w1, float h1, UiInsets safe1, float wanted)
    {
        if (wanted <= 1f) return 1f;
        for (int k = (int)MathF.Round(wanted * 100f); k > 100; k--)
        {
            float f = k / 100f;
            if (Fits(w1 / f, h1 / f, new UiInsets(safe1.Left / f, safe1.Top / f, safe1.Right / f, safe1.Bottom / f))) return f;
        }
        return 1f;
    }

    /// <summary>Spaltenzahl eines Rasters aus Kacheln der Breite <paramref name="tileW"/> mit Abstand <paramref name="gap"/> (mindestens 1).</summary>
    public static int GridColumns(float width, float tileW, float gap) => Math.Max(1, (int)MathF.Floor((width + gap) / (tileW + gap)));
}
