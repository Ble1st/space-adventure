namespace SpaceSim.Core.Ui;

/// <summary>
/// Runde 34 (UI-ANPASSUNG.md Etappe E3): Gerüst eines Dock-Bereichs — Kopfleiste (Stationsname, Kasse), linke Bereichsleiste, Mittelteil, rechte
/// Schiffsspalte (Handel, Reparatur …), „← Menü“ und „Abdocken“. <see cref="InnerW"/>/<see cref="InnerH"/> ist die Fläche, die der Bereich selbst füllt.
/// </summary>
public sealed record DockShell(bool Compact, UiRect Area, UiRect Top, UiRect Rail, UiRect Center, UiRect Ship, UiRect Back, UiRect Undock, UiRect Credits, UiRect AlertSlot)
{
    /// <summary>Runde 34b: der Bereich hätte eine Schiffsspalte, sie ist aber eingeklappt (zu schmal) — Laderaum steht dann in der Kopfleiste.</summary>
    public bool ShipCollapsed { get; init; }
    /// <summary>
    /// Runde 34b (Prüfung B2: Hinweise lagen im kompakten Dock über „Alles verkaufen“, „Tempo …“): Streifen für Meldung und Hinweis, der keine Taste und keinen
    /// Text trägt — klassisch die Fußleiste zwischen „← Menü“ und „Abdocken“, kompakt ein Streifen unter dem Mittelteil.
    /// </summary>
    public UiRect MessageStrip { get; init; }
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
    /// Runde 34b (Prüfung B1: mit 1008 blieb das Dock auf 16:9-Handys bei 1,02): die Bereiche passen Texte jetzt mit der Schrift ein und brechen Spalten um —
    /// Mindestbreite 740 für jeden Bereich; die Schiffsspalte erscheint nur, wenn daneben noch 600 bleiben, sonst ist sie eingeklappt.
    public const float MinInnerShipW = 600f, MinInnerW = 740f, MinInnerH = 380f;
    /// <summary>Höhe des Meldungsstreifens im Kompaktmodus.</summary>
    public const float StripH = 50f;
    /// <summary>Breite der Bereichsleiste: Entwurf 200, kompakt 170.</summary>
    public const float RailW = 200f, RailCompactW = 170f;
    /// <summary>Dock-Menü: Kartenbreite und -höhe im 1280 × 720-Entwurf; Mindesthöhe, bei der die Bereichskacheln noch rollen können.</summary>
    public const float MenuW = 580f, MenuH = 662f, MenuMinH = 420f;
    /// <summary>Runde 34b: so breit muss der Platz links der Dock-Menü-Karte sein, damit Meldung/Hinweis dort stehen; sonst ein Streifen unter der Karte.</summary>
    public const float MenuSideMin = 240f;

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
            var strip = new UiRect(back.Right + 10, y1 - 72, undock.X - 10 - (back.Right + 10), 58);
            return new DockShell(false, a, top, rail, center, shipCol, back, undock, credits, slot) { MessageStrip = strip };
        }
        else
        {
            var undock = new UiRect(x1 - 14 - 180, y0 + 8, 180, 56);
            var top = new UiRect(x0 + 14, y0 + 6, undock.X - 10 - (x0 + 14), 60);
            var credits = new UiRect(top.Right - 280, top.Y, 280, 60);
            var back = new UiRect(x0 + 14, y1 - 14 - 58, RailCompactW, 58);
            var rail = new UiRect(x0 + 14, y0 + 74, RailCompactW, SnapRail(back.Y - 8 - (y0 + 74)));
            float bodyH = y1 - 14 - (y0 + 74) - StripH - 6f;
            float cx = x0 + 14 + RailCompactW + 10;
            bool collapse = ship && x1 - 14 - cx - 270f - 32f < MinInnerShipW - 0.01f;
            if (collapse) ship = false;
            float shipW = ship ? 260f : 0f;
            float cw = x1 - 14 - cx - (ship ? 10f + shipW : 0f);
            var center = new UiRect(cx, y0 + 74, MathF.Max(0f, cw), bodyH);
            var shipCol = new UiRect(center.Right + (ship ? 10f : 0f), y0 + 74, shipW, bodyH);
            // Meldung über dem Dock: über dem Stationsnamen bis vor die Kasse (die Kopfzeile ist schmal; „Abdocken“ bleibt frei)
            var slot = new UiRect(x0 + 14, y0, MathF.Max(200f, MathF.Min(600f, credits.X - 10 - (x0 + 14))), 56);
            var strip = new UiRect(cx, y1 - 14 - StripH, x1 - 14 - cx, StripH);
            return new DockShell(true, a, top, rail, center, shipCol, back, undock, credits, slot) { ShipCollapsed = collapse, MessageStrip = strip };
        }
    }

    /// <summary>Karte des Dock-Menüs: höchstens 580 × 662 (Entwurf), waagerecht mittig, senkrecht mittig mit dem Entwurfsabstand 30 bei 720 Höhe.</summary>
    public static UiRect MenuCard(float w, float h, UiInsets safe)
    {
        var a = Usable(w, h, safe);
        float cw = MathF.Min(MenuW, a.W - 20f), ch = MathF.Min(MenuH, a.H - 58f);
        // Runde 34b (Prüfung B2): reicht der Platz links der Karte nicht für Meldung/Hinweis, bleibt unter der Karte ein Streifen frei
        if ((a.W - cw) / 2f - 20f < MenuSideMin) ch = MathF.Min(ch, a.H - 58f - StripH - 6f);
        float y = a.Y + MathF.Max(4f, (a.H - ch) / 2f + 1f);
        if (y + ch > a.Bottom) ch = MathF.Max(0f, a.Bottom - y - 4f);
        return new UiRect(a.Right - cw - 10f, y, cw, ch);   // Runde 45: Karte ganz rechts — links bleibt die Stations-Ansicht frei
    }

    /// <summary>Runde 34b: Meldungsstreifen des Dock-Menüs — links der Karte, wenn dort ≥ 240 Platz ist, sonst unter der Karte.</summary>
    public static UiRect MenuStrip(float w, float h, UiInsets safe)
    {
        var a = Usable(w, h, safe);
        var c = MenuCard(w, h, safe);
        float side = c.X - a.X - 20f;
        return side >= MenuSideMin ? new UiRect(a.X + 10f, c.Y + 60f, side, 150f) : new UiRect(c.X, c.Bottom + 6f, c.W, StripH);
    }

    /// <summary>Hat jeder Dock-Bereich (mit und ohne Schiffsspalte) und das Dock-Menü auf dieser Fläche seine Mindestfläche?</summary>
    public static bool Fits(float w, float h, UiInsets safe)
    {
        var s = Shell(w, h, safe, ship: true);
        var n = Shell(w, h, safe, ship: false);
        var m = MenuCard(w, h, safe);
        return (s.ShipCollapsed ? s.InnerW >= MinInnerW - 0.01f : s.InnerW >= MinInnerShipW - 0.01f) && n.InnerW >= MinInnerW - 0.01f && n.InnerH >= MinInnerH - 0.01f && s.InnerH >= MinInnerH - 0.01f
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

    /// <summary>
    /// Runde 34b (Handytest r34 #5: „Aufträge“ halb sichtbar): Höhe der kompakten Bereichsleiste so, dass nur ganze Einträge (56 hoch, Abstand 6, Innenrand 8)
    /// sichtbar sind — der Rest der Höhe bleibt frei über „← Menü“.
    /// </summary>
    public static float SnapRail(float avail)
    {
        int n = Math.Max(1, (int)MathF.Floor((avail - 16f + 6f) / 62f));
        return MathF.Min(avail, n * 62f - 6f + 16f);
    }

    /// <summary>Spaltenzahl eines Rasters aus Kacheln der Breite <paramref name="tileW"/> mit Abstand <paramref name="gap"/> (mindestens 1).</summary>
    public static int GridColumns(float width, float tileW, float gap) => Math.Max(1, (int)MathF.Floor((width + gap) / (tileW + gap)));
}

/// <summary>Runde 35 (E4): Gerüst der Kommandozentrale — Kopfleiste, Bereichsleiste, Inhalt, „← Flug“, Meldungsstreifen.</summary>
public sealed record CommandShell(bool Compact, UiRect Area, UiRect Top, UiRect Rail, UiRect Center, UiRect Back, UiRect Credits, UiRect MessageStrip)
{
    /// <summary>Innenfläche des Inhalts (Rand 10 rundum).</summary>
    public float InnerW => Center.W - 20f;
    public float InnerH => Center.H - 20f;
}

/// <summary>
/// Runde 35 (UI-ANPASSUNG.md Etappe E4) — Gerüst der Kommandozentrale als reine Rechnung, Prinzip wie <see cref="DockLayout"/>. <b>Klassisch</b> (≥ 1280 × 720):
/// die bisherigen Lagen (Kopfleiste, Leiste 200, Inhalt 1042 × 540, „← Flug“ unten links), auf die ganze Fläche gedehnt (höchstens 1600 breit), Safe Area rundum;
/// bei 1280 × 720 exakt wie bisher. <b>Kompakt</b>: Leiste 170, „← Flug“ unter der Leiste, Meldungsstreifen unter dem Inhalt. Aus E3 gelernt: die Bereiche
/// kommen mit einer Innenfläche ab 740 × 380 aus (Spalten aus <see cref="UiLayout.Columns"/>, Texte mit der Schrift eingepasst), damit auch 16:9-Handys bei
/// „groß“ den vollen Faktor bekommen.
/// </summary>
public static class CommandLayout
{
    public const float MinInnerW = 740f, MinInnerH = 380f, StripH = 50f, RailCompactW = 170f;

    public static CommandShell Shell(float w, float h, UiInsets safe)
    {
        var a = new UiRect(safe.Left, safe.Top, MathF.Max(0f, w - safe.Left - safe.Right), MathF.Max(0f, h - safe.Top - safe.Bottom));
        bool compact = a.W < UiLayout.BaseWidth - 0.5f || a.H < UiLayout.BaseHeight - 0.5f;
        if (a.W > DockLayout.MaxWidth) a = new UiRect(a.X + (a.W - DockLayout.MaxWidth) / 2f, a.Y, DockLayout.MaxWidth, a.H);
        float x0 = a.X, y0 = a.Y, x1 = a.Right, y1 = a.Bottom;
        if (!compact)
        {
            var top = new UiRect(x0 + 20, y0 + 10, a.W - 40, 64);
            float bodyH = a.H - 180f;
            var rail = new UiRect(x0 + 14, y0 + 84, 200, bodyH);
            var center = new UiRect(x0 + 224, y0 + 84, a.W - 238f, bodyH);
            var back = new UiRect(x0 + 14, y1 - 72, 160, 58);
            var credits = new UiRect(top.Right - 300, top.Y, 300, 60);
            var strip = new UiRect(back.Right + 10, y1 - 72, x1 - 14 - (back.Right + 10), 58);
            return new CommandShell(false, a, top, rail, center, back, credits, strip);
        }
        else
        {
            var top = new UiRect(x0 + 14, y0 + 6, a.W - 28, 60);
            var credits = new UiRect(top.Right - 280, top.Y, 280, 60);
            var back = new UiRect(x0 + 14, y1 - 14 - 58, RailCompactW, 58);
            var rail = new UiRect(x0 + 14, y0 + 74, RailCompactW, DockLayout.SnapRail(back.Y - 8 - (y0 + 74)));
            float cx = x0 + 14 + RailCompactW + 10;
            float bodyH = y1 - 14 - (y0 + 74) - StripH - 6f;
            var center = new UiRect(cx, y0 + 74, MathF.Max(0f, x1 - 14 - cx), bodyH);
            var strip = new UiRect(cx, y1 - 14 - StripH, x1 - 14 - cx, StripH);
            return new CommandShell(true, a, top, rail, center, back, credits, strip);
        }
    }

    public static bool Fits(float w, float h, UiInsets safe)
    {
        var s = Shell(w, h, safe);
        return s.InnerW >= MinInnerW - 0.01f && s.InnerH >= MinInnerH - 0.01f;
    }

    /// <summary>Größter Faktor ≤ <paramref name="wanted"/> (Schritte 0,01, nie unter 1), bei dem <see cref="Fits"/> gilt.</summary>
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
}
