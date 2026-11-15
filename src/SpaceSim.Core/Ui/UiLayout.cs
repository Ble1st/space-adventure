namespace SpaceSim.Core.Ui;

/// <summary>Runde 31 (UI-ANPASSUNG.md, Etappe 1): Stufe der Bedienelemente — vom Spieler gewählt (System-Bereich, Menü).</summary>
public enum UiSize { Small, Normal, Large }

/// <summary>Runde 31: Layout-Klasse der nutzbaren Fläche.</summary>
public enum UiClass { Compact, Normal }

/// <summary>Runde 31: Ränder in logischen Einheiten (links, oben, rechts, unten).</summary>
public readonly record struct UiInsets(float Left, float Top, float Right, float Bottom);

/// <summary>Runde 31: Spaltenwunsch für <see cref="UiLayout.Columns"/> — Mindestbreite, Wunschbreite, Anteil am Rest (0 = wächst nicht).</summary>
public readonly record struct UiColumn(float Min, float Want, float Grow);

/// <summary>
/// Runde 31 — Layout-Modul (reine Funktionen, ohne Godot; UI-ANPASSUNG.md Abschnitt 3). Raster 1280 × 720 mit Stretch „canvas_items“ + „expand“:
/// die logische Fläche ist das Fenster geteilt durch Stretch-Maßstab × UI-Faktor. Daraus: nutzbare Fläche (Safe Area an allen vier Rändern),
/// UI-Faktor aus DPI (Ziel: Mindest-Tippfläche) und Stufe, Klasse kompakt/normal, Spaltenbreiten.
/// </summary>
public static class UiLayout
{
    public const float BaseWidth = 1280f, BaseHeight = 720f;
    /// <summary>Tippfläche im Raster (px) — die Tasten der Spielansicht sind mindestens so hoch.</summary>
    public const float BaseTapPx = 56f;
    /// <summary>Ziel-Mindestmaß einer Tippfläche (Android 48 dp ≈ 7,6 mm).</summary>
    public const float MinTapMm = 7f;
    /// <summary>Mindestauflösung (logisch, nach Skalierung) — vom Nutzer angenommen (UI-ANPASSUNG.md).</summary>
    public const float MinLogicalWidth = 960f, MinLogicalHeight = 540f;

    /// <summary>Stretch-Maßstab von Godot („expand“): das Raster passt in der knapperen Richtung genau.</summary>
    public static float StretchScale(float windowW, float windowH) => MathF.Max(1e-3f, MathF.Min(windowW / BaseWidth, windowH / BaseHeight));

    /// <summary>Logische Fläche bei UI-Faktor <paramref name="factor"/> (1 = heute).</summary>
    public static (float W, float H) Logical(float windowW, float windowH, float factor)
    {
        float s = StretchScale(windowW, windowH) * MathF.Max(1e-3f, factor);
        return (windowW / s, windowH / s);
    }

    /// <summary>Physische Größe von <paramref name="logicalPx"/> logischen Pixeln in mm.</summary>
    public static float Millimetres(float logicalPx, float windowW, float windowH, float factor, float ppi) =>
        ppi <= 0f ? float.NaN : logicalPx * StretchScale(windowW, windowH) * factor / ppi * 25.4f;

    /// <summary>Faktor, bei dem eine Raster-Taste (<see cref="BaseTapPx"/>) genau <see cref="MinTapMm"/> groß ist.</summary>
    public static float DpiFactor(float windowW, float windowH, float ppi) =>
        ppi <= 0f ? 1f : MinTapMm / 25.4f * ppi / (BaseTapPx * StretchScale(windowW, windowH));

    /// <summary>Größter Faktor, bei dem die logische Fläche mindestens <paramref name="minW"/> × <paramref name="minH"/> bleibt.</summary>
    public static float MaxFactor(float windowW, float windowH, float minW, float minH)
    {
        var (w, h) = Logical(windowW, windowH, 1f);
        return MathF.Min(w / minW, h / minH);
    }

    /// <summary>
    /// Wunschfaktor einer Stufe: klein = 1 (der heutige Stand), normal = min(1,15; DPI-Ziel), groß = min(1,3; DPI-Ziel × 1,1), jeweils mindestens 1;
    /// dann begrenzt durch <paramref name="cap"/> (die größte Skalierung, bei der die Bildschirme noch ins Bild passen — solange sie starr
    /// 1280 × 720 sind, ist das 1, siehe <see cref="FixedScreensCap"/>).
    /// </summary>
    public static float Factor(UiSize size, float windowW, float windowH, float ppi, float cap)
    {
        float dpi = DpiFactor(windowW, windowH, ppi);
        float want = size switch
        {
            UiSize.Small => 1f,
            UiSize.Normal => MathF.Max(1f, MathF.Min(1.15f, dpi)),
            _ => MathF.Max(1f, MathF.Min(1.3f, dpi * 1.1f)),
        };
        // nie unter 1: ein Faktor < 1 verkleinerte die Tasten (Runde 31 gefunden: headless meldet das Fenster als 0 × 0 → Obergrenze 0,78)
        if (windowW <= 1f || windowH <= 1f) return 1f;
        return MathF.Max(1f, MathF.Min(want, cap));
    }

    /// <summary>Obergrenze, solange Bildschirme starr 1280 × 720 groß sind: die logische Fläche darf nicht kleiner werden.</summary>
    public static float FixedScreensCap(float windowW, float windowH) => MaxFactor(windowW, windowH, BaseWidth, BaseHeight);

    /// <summary>Nutzbare Ränder in logischen Einheiten aus dem Fenster und der Safe Area (Pixelrechteck des Fensters; leer = keine).</summary>
    public static UiInsets Usable(float windowW, float windowH, float safeX, float safeY, float safeW, float safeH, float factor)
    {
        if (safeW <= 0f || safeH <= 0f) return new UiInsets(0f, 0f, 0f, 0f);
        float k = 1f / (StretchScale(windowW, windowH) * MathF.Max(1e-3f, factor));
        return new UiInsets(MathF.Max(0f, safeX) * k, MathF.Max(0f, safeY) * k, MathF.Max(0f, windowW - (safeX + safeW)) * k, MathF.Max(0f, windowH - (safeY + safeH)) * k);
    }

    /// <summary>Klasse der logischen Fläche: kompakt unter 1100 × 640, sonst normal.</summary>
    public static UiClass Class(float logicalW, float logicalH) => logicalW < 1100f || logicalH < 640f ? UiClass.Compact : UiClass.Normal;

    /// <summary>
    /// Spaltenbreiten für eine Zeile der Breite <paramref name="width"/>: erst Mindestbreiten, dann bis zur Wunschbreite, der Rest nach Anteil
    /// (<see cref="UiColumn.Grow"/>). Reicht die Breite nicht für die Mindestbreiten, werden alle anteilig gekürzt.
    /// </summary>
    public static float[] Columns(float width, float gap, params UiColumn[] cols)
    {
        int n = cols.Length;
        var w = new float[n];
        if (n == 0) return w;
        float avail = MathF.Max(0f, width - gap * (n - 1));
        float min = cols.Sum(c => c.Min);
        if (min >= avail)
        {
            float k = min > 0f ? avail / min : 0f;
            for (int i = 0; i < n; i++) w[i] = cols[i].Min * k;
            return w;
        }
        for (int i = 0; i < n; i++) w[i] = cols[i].Min;
        float rest = avail - min;
        float wantExtra = cols.Sum(c => MathF.Max(0f, c.Want - c.Min));
        float k2 = wantExtra > 0f ? MathF.Min(1f, rest / wantExtra) : 0f;
        for (int i = 0; i < n; i++) { float add = MathF.Max(0f, cols[i].Want - cols[i].Min) * k2; w[i] += add; rest -= add; }
        float grow = cols.Sum(c => c.Grow);
        if (grow > 0f && rest > 0f) for (int i = 0; i < n; i++) w[i] += rest * cols[i].Grow / grow;
        return w;
    }

    /// <summary>
    /// Runde 34b: Schriftgröße, bei der ein Text in <paramref name="room"/> passt — von <paramref name="size"/> schrittweise bis <paramref name="min"/> verkleinert
    /// (<paramref name="width"/> misst die Breite bei einer Größe, z. B. mit der Godot-Schrift). Passt er auch dann nicht, <paramref name="min"/> (der Rest wird gekürzt).
    /// </summary>
    /// <summary>
    /// Runde 35b (Prüfung: automatisches Verkleinern hob die UI-Vergrößerung wieder auf): kleinste erlaubte Schriftgröße eines Elements der Grundgröße
    /// <paramref name="baseSize"/> auf einer Ebene mit dem Faktor <paramref name="factor"/>. Bei „klein“ (Faktor ≤ 1) bis 11; bei „normal“/„groß“ nie unter
    /// Grundgröße ÷ Faktor (aufgerundet, mindestens 11) — die wirksame Größe (Schrift × Faktor) ist dann nie kleiner als bei „klein“ (dort höchstens die
    /// Grundgröße). Passt der Text darauf nicht, wird umgebrochen oder zuletzt gekürzt, nicht weiter verkleinert.
    /// </summary>
    public static int FontFloor(int baseSize, float factor)
    {
        if (factor <= 1.001f) return Math.Min(baseSize, 11);
        return Math.Min(baseSize, Math.Max(11, (int)Math.Ceiling(baseSize / factor - 0.001f)));
    }

    public static int FitFontSize(Func<int, float> width, float room, int size, int min = 11)
    {
        int s = size;
        while (s > min && width(s) > room + 0.5f) s--;
        return s;
    }

    /// <summary>
    /// Runde 34b (Prüfung B3): passt <paramref name="text"/> nicht in <paramref name="room"/>, wird er in der Mitte gekürzt („Universalfr…raum 100“) — Anfang und
    /// das unterscheidende Ende bleiben sichtbar. <paramref name="width"/> misst eine Fassung.
    /// </summary>
    public static string MiddleEllipsis(string text, Func<string, float> width, float room)
    {
        if (width(text) <= room + 0.5f || text.Length <= 3) return text;
        for (int keep = text.Length - 1; keep >= 2; keep--)
        {
            int tail = Math.Max(1, keep * 2 / 5), head = keep - tail;
            string t = text[..head].TrimEnd() + "…" + text[^tail..].TrimStart();
            if (width(t) <= room + 0.5f) return t;
        }
        return "…";
    }
}
