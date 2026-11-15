namespace SpaceSim.Core.Ui;

/// <summary>Runde 33 (UI-ANPASSUNG.md Etappe 2): Rechteck in logischen Einheiten (x, y nach unten).</summary>
public readonly record struct UiRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool Intersects(UiRect o, float gap = 0f) =>
        X < o.Right + gap && o.X < Right + gap && Y < o.Bottom + gap && o.Y < Bottom + gap;
    public bool Inside(UiRect area, float eps = 0.01f) =>
        X >= area.X - eps && Y >= area.Y - eps && Right <= area.Right + eps && Bottom <= area.Bottom + eps;
    public UiRect Offset(float dx, float dy) => new(X + dx, Y + dy, W, H);
}

/// <summary>Runde 33: Elemente des Flug-HUD, deren Lage die Zonenrechnung bestimmt.</summary>
public enum HudItem
{
    Status, Mode0, Mode1, Mode2,
    Chip, Security, Wanted, Fire,
    Command, Map, Fast,
    Card, Autopilot, AutoMine, Context,
    TurnLabel, TurnLeft, TurnRight, ReverseLabel, Reverse, Hamburger, Slider,
    Bay0, Bay1, Bay2, Bay3, Bay4, Bay5,
    AllFire, AutoFire, Ready, NextEnemy, Attack, AimAssist, Surrender,
    HitWarning, Escape,
}

/// <summary>
/// Runde 33 — Zonen des Flug-HUD als reine Rechnung (UI-ANPASSUNG.md Abschnitt 3.4, Etappe 2). Eingabe: logische Fläche der HUD-Schicht (nach
/// Stretch und UI-Faktor), Safe-Area-Ränder an allen vier Seiten, Zahl der Waffenschächte. Ausgabe: Rechteck je <see cref="HudItem"/>.
/// <para>
/// <b>Klassisch</b> (nutzbare Fläche mindestens 1280 × 720): die bisherigen Lagen des 1280 × 720-Entwurfs, links/mittig/rechts verankert (wie bisher)
/// und nun auch senkrecht — die untere Zone (Wenden, RÜCK, Menü, Schubregler, Beschuss-/Fluchtzeile) sitzt am unteren Rand, alles andere am oberen.
/// Bei genau 1280 × 720 ohne Ränder ist jede Lage unverändert.
/// </para>
/// <para>
/// <b>Kompakt</b> (kleiner, d. h. UI-Faktor &gt; 1 oder Safe-Area-Ränder): feste Ecken (Status oben links, Kommando/Karte/Zeit + Zielkarte oben rechts,
/// Wenden/RÜCK/Menü unten links, Schubregler unten rechts), der Sektorchip schrumpft auf den Raum zwischen Status und Kommandotaste, und die
/// Kampfspalte (Schächte, Alle Feuer, Dauerfeuer, Ergeben, Nächster Gegner, Angriff, Ziel folgen) fließt in ein Raster von 176 × 56-Zellen, das
/// von unten rechts (über dem Schubregler) zeilenweise nach links und oben gefüllt wird und keine feste Fläche berührt. Die Zellen sind für alle
/// Kampftasten reserviert (auch unsichtbare), damit nichts springt, wenn eine Taste erscheint.
/// </para>
/// </summary>
public sealed class FlightHudLayout
{
    public const float BaseW = UiLayout.BaseWidth, BaseH = UiLayout.BaseHeight;
    public const float CellW = 176f, CellH = 56f, CellStepX = 182f, CellStepY = 60f;
    /// <summary>Mindestabstand zwischen einer Kampfzelle und einer festen Fläche.</summary>
    public const float Gap = 3f;
    public const int MaxBays = 6;

    readonly Dictionary<HudItem, UiRect> _rects = new();
    readonly HashSet<HudItem> _hidden = new();

    /// <summary>Kompakte Anordnung (Fläche kleiner als 1280 × 720)?</summary>
    public bool Compact { get; private init; }
    /// <summary>Nutzbare Fläche (nach Safe Area).</summary>
    public UiRect Area { get; private init; }
    /// <summary>
    /// Kampftasten ohne eigene Zelle (die letzten in <see cref="CombatItems"/>-Reihenfolge — Angriff, Ergeben —, sie lägen auf der Zelle einer anderen
    /// Taste). Runde 33b: kommt im Spiel nicht vor, weil <see cref="FitFactor"/> den Faktor der Schicht so weit senkt, bis jede Taste eine eigene Zelle hat.
    /// </summary>
    public int Overflow { get; private set; }

    public UiRect this[HudItem item] => _rects[item];
    public bool Has(HudItem item) => _rects.ContainsKey(item);
    /// <summary>Element in dieser Anordnung ausgeblendet (z. B. die Beschriftung „WENDEN“ im Kompaktmodus).</summary>
    public bool Hidden(HudItem item) => _hidden.Contains(item);
    public IReadOnlyDictionary<HudItem, UiRect> Rects => _rects;

    /// <summary>Tasten (antippbar) — für Überlappungsprüfungen.</summary>
    public static bool IsInteractive(HudItem i) => i is not (HudItem.Status or HudItem.Chip or HudItem.Security or HudItem.Wanted or HudItem.Fire
        or HudItem.Card or HudItem.TurnLabel or HudItem.ReverseLabel or HudItem.Ready or HudItem.HitWarning or HudItem.Escape);

    /// <summary>Kampfspalte in der Reihenfolge, in der sie die Zellen belegt.</summary>
    public static IEnumerable<HudItem> CombatItems(int bays)
    {
        for (int i = 0; i < Math.Clamp(bays, 0, MaxBays); i++) yield return HudItem.Bay0 + i;
        yield return HudItem.AllFire;
        yield return HudItem.AutoFire;
        yield return HudItem.NextEnemy;
        yield return HudItem.AimAssist;
        yield return HudItem.Attack;
        yield return HudItem.Surrender;
    }

    // ── 1280 × 720-Entwurf (Lagen wie bis Runde 32) ──
    static readonly (HudItem Item, float X, float Y, float W, float H)[] Base =
    {
        (HudItem.Status, 14, 12, 300, 200),
        (HudItem.Mode0, 14, 222, 96, 56), (HudItem.Mode1, 116, 222, 96, 56), (HudItem.Mode2, 218, 222, 96, 56),
        (HudItem.Chip, 420, 12, 430, 42), (HudItem.Security, 420, 56, 430, 18), (HudItem.Wanted, 420, 74, 430, 20), (HudItem.Fire, 300, 94, 670, 20),
        (HudItem.Command, 862, 8, 160, 56), (HudItem.Map, 1030, 12, 110, 50), (HudItem.Fast, 1150, 12, 110, 50),
        (HudItem.Card, 930, 72, 340, 132), (HudItem.Autopilot, 930, 212, 160, 42), (HudItem.AutoMine, 1100, 212, 170, 42), (HudItem.Context, 930, 262, 340, 52),
        (HudItem.TurnLabel, 100, 560, 260, 20), (HudItem.TurnLeft, 96, 584, 120, 100), (HudItem.TurnRight, 230, 584, 120, 100),
        (HudItem.ReverseLabel, 410, 566, 200, 20), (HudItem.Reverse, 470, 590, 80, 56), (HudItem.Hamburger, 16, 644, 60, 60), (HudItem.Slider, 660, 646, 600, 68),
        (HudItem.Ready, 1090, 314, 176, 16),
        (HudItem.NextEnemy, 764, 206, 160, 56), (HudItem.Attack, 764, 330, 136, 56), (HudItem.AimAssist, 764, 392, 136, 56), (HudItem.Surrender, 906, 398, 176, 56),
        (HudItem.HitWarning, 590, 534, 310, 28), (HudItem.Escape, 590, 566, 310, 22),
    };
    static readonly HashSet<HudItem> BottomZone = new()
    {
        HudItem.TurnLabel, HudItem.TurnLeft, HudItem.TurnRight, HudItem.ReverseLabel, HudItem.Reverse, HudItem.Hamburger, HudItem.Slider,
        HudItem.HitWarning, HudItem.Escape,
    };

    /// <summary>Klassische Lage eines Waffenschachts (vier rechts, weitere links daneben ab y 456 — wie bis Runde 32).</summary>
    static (float X, float Y) ClassicBay(int i) => i < 4 ? (1090f, 330f + i * CellStepY) : (906f, 456f + (i - 4) * CellStepY);
    /// <summary>„Alle Feuer“/„Dauerfeuer“ unter der rechten Schachtspalte (wie bis Runde 32).</summary>
    public static float ClassicFireY(int bays) => MathF.Max(462f, 330f + Math.Min(Math.Max(0, bays), 4) * CellStepY + 4f);

    FlightHudLayout() { }

    /// <summary>Anordnung für eine HUD-Fläche <paramref name="w"/> × <paramref name="h"/> (logisch) mit Safe-Area-Rändern und <paramref name="bays"/> Schächten.</summary>
    public static FlightHudLayout Compute(float w, float h, UiInsets safe, int bays)
    {
        var area = new UiRect(safe.Left, safe.Top, MathF.Max(0f, w - safe.Left - safe.Right), MathF.Max(0f, h - safe.Top - safe.Bottom));
        bool compact = area.W < BaseW - 0.5f || area.H < BaseH - 0.5f;
        var l = new FlightHudLayout { Compact = compact, Area = area };
        if (compact) l.Fill(area, bays); else l.Classic(w, h, safe, bays);
        return l;
    }

    void Classic(float w, float h, UiInsets s, int bays)
    {
        float dxL = s.Left, dxR = w - BaseW - s.Right, dxC = (w - BaseW + s.Left - s.Right) / 2f;
        float dyT = s.Top, dyB = h - BaseH - s.Bottom;
        UiRect Place(float x, float y, float rw, float rh, bool bottom)
        {
            float mid = x + rw / 2f;   // Zuordnung wie bisher nach der Mitte im 1280-Entwurf: links (< 400), rechts (> 820), sonst mittig
            float dx = mid > 820f ? dxR : mid >= 400f ? dxC : dxL;
            return new UiRect(x + dx, y + (bottom ? dyB : dyT), rw, rh);
        }
        foreach (var (item, x, y, rw, rh) in Base) _rects[item] = Place(x, y, rw, rh, BottomZone.Contains(item));
        for (int i = 0; i < MaxBays; i++) { var (bx, by) = ClassicBay(i); _rects[HudItem.Bay0 + i] = Place(bx, by, CellW, CellH, false); }
        float fy = ClassicFireY(bays);
        _rects[HudItem.AllFire] = Place(1090f, fy, CellW, CellH, false);
        _rects[HudItem.AutoFire] = Place(906f, fy, CellW, CellH, false);
    }

    void Fill(UiRect a, int bays)
    {
        float x0 = a.X, y0 = a.Y, x1 = a.Right, y1 = a.Bottom;
        void Set(HudItem i, float x, float y, float rw, float rh) => _rects[i] = new UiRect(x, y, rw, rh);
        // oben links: Status, Leistungsverteilung
        Set(HudItem.Status, x0 + 14, y0 + 12, 300, 200);
        for (int i = 0; i < 3; i++) Set(HudItem.Mode0 + i, x0 + 14 + i * 102, y0 + 222, 96, 56);
        // oben rechts: Kommando, Karte, Zeit; Zielkarte mit Autopilot/Auto-Mining und Kontexttaste
        Set(HudItem.Fast, x1 - 130, y0 + 12, 110, 50);
        Set(HudItem.Map, x1 - 250, y0 + 12, 110, 50);
        Set(HudItem.Command, x1 - 418, y0 + 8, 160, 56);
        Set(HudItem.Card, x1 - 350, y0 + 72, 340, 132);
        Set(HudItem.Autopilot, x1 - 350, y0 + 212, 160, 42);
        Set(HudItem.AutoMine, x1 - 180, y0 + 212, 170, 42);
        Set(HudItem.Context, x1 - 350, y0 + 262, 340, 52);
        // oben Mitte: Sektorchip und Zeilen zwischen Status und Kommandotaste, so mittig wie möglich
        float lo = x0 + 320f, hi = x1 - 424f;
        float cw = MathF.Max(0f, MathF.Min(430f, hi - lo));
        float cx = Math.Clamp((x0 + x1) / 2f - cw / 2f, lo, MathF.Max(lo, hi - cw));
        Set(HudItem.Chip, cx, y0 + 12, cw, 42);
        // Lage-, Fahndungs- und Brandzeile unter der oberen Tastenleiste: dort reichen sie bis vor die Zielkarte (breiter als der Chip)
        float lhi = x1 - 356f;
        float lw = MathF.Max(0f, MathF.Min(430f, lhi - lo));
        float lx = Math.Clamp((x0 + x1) / 2f - lw / 2f, lo, MathF.Max(lo, lhi - lw));
        Set(HudItem.Security, lx, y0 + 66, lw, 18);
        Set(HudItem.Wanted, lx, y0 + 86, lw, 20);
        Set(HudItem.Fire, lx, y0 + 108, lw, 20);
        // unten links: Menü, Wenden, RÜCK (eng daneben, Beschriftung darüber); „WENDEN“ entfällt, dort stehen Beschuss- und Fluchtzeile
        Set(HudItem.Hamburger, x0 + 16, y1 - 76, 60, 60);
        Set(HudItem.TurnLeft, x0 + 96, y1 - 136, 120, 100);
        Set(HudItem.TurnRight, x0 + 230, y1 - 136, 120, 100);
        Set(HudItem.TurnLabel, x0 + 100, y1 - 160, 250, 20);
        _hidden.Add(HudItem.TurnLabel);
        Set(HudItem.ReverseLabel, x0 + 356, y1 - 134, 112, 20);
        Set(HudItem.Reverse, x0 + 364, y1 - 110, 80, 56);
        Set(HudItem.HitWarning, x0 + 14, y1 - 196, 336, 26);
        Set(HudItem.Escape, x0 + 14, y1 - 166, 336, 22);
        // unten rechts: Schubregler rechts der RÜCK-Taste, höchstens 600 breit (dann rechtsbündig wie im Entwurf)
        float sx = x0 + 456f, sw = x1 - 20f - sx;
        if (sw > 600f) { sx = x1 - 20f - 600f; sw = 600f; }
        Set(HudItem.Slider, sx, y1 - 74, MathF.Max(0f, sw), 68);
        // „n von m bereit“ steht im Kompaktmodus in „Alle Feuer“ (die Zeile hätte keinen eigenen Platz)
        Set(HudItem.Ready, 0, 0, 0, 0);
        _hidden.Add(HudItem.Ready);

        var fixedRects = _rects.Where(kv => !_hidden.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        var cells = Cells(a, fixedRects, _rects[HudItem.Slider].Y);
        var items = CombatItems(bays).ToList();
        // nicht belegte Schächte bekommen keine eigene Zelle (sie sind unsichtbar) — Ersatzlage auf Schacht 1
        int k = 0;
        foreach (var item in items)
        {
            if (k < cells.Count) { _rects[item] = cells[k++]; continue; }
            Overflow++;
            _rects[item] = cells.Count > 0 ? cells[^1] : new UiRect(x1 - 14 - CellW, y0 + 330, CellW, CellH);
        }
        for (int i = Math.Clamp(bays, 0, MaxBays); i < MaxBays; i++)
            _rects[HudItem.Bay0 + i] = _rects.TryGetValue(HudItem.Bay0, out var b0) ? b0 : new UiRect(x1 - 14 - CellW, y0 + 330, CellW, CellH);
    }

    /// <summary>
    /// Freie Zellen 176 × 56 für die Kampfspalte: Zeilen im Abstand 60 von über dem Schubregler nach oben, in jeder Zeile von rechts nach links
    /// (Schritt 4, bis eine Zelle frei ist, dann eine Zellenbreite weiter) — keine Zelle berührt eine feste Fläche.
    /// </summary>
    static List<UiRect> Cells(UiRect a, List<UiRect> fixedRects, float sliderY)
    {
        var list = new List<UiRect>();
        for (float y = sliderY - 6f - CellH; y >= a.Y + 8f; y -= CellStepY)
        {
            float x = a.Right - 14f - CellW;
            while (x >= a.X + 14f)
            {
                var c = new UiRect(x, y, CellW, CellH);
                if (fixedRects.Exists(f => f.W > 0f && c.Intersects(f, Gap)) || list.Exists(o => c.Intersects(o, 2f))) { x -= 4f; continue; }
                list.Add(c);
                x -= CellStepX;
            }
        }
        return list;
    }

    /// <summary>
    /// Runde 33b (Prüfung: bei 985 × 554 und fünf Schächten lag „Ergeben“ auf „Station angreifen“): größter UI-Faktor ≤ <paramref name="wanted"/>, bei dem auf der
    /// Fläche <paramref name="w1"/> × <paramref name="h1"/> (logisch bei Faktor 1) mit den Rändern <paramref name="safe1"/> (ebenfalls bei Faktor 1) jede
    /// Kampftaste und jeder der <paramref name="bays"/> Schächte eine eigene Zelle hat (<see cref="Overflow"/> = 0). Schritte von 0,01; nie unter 1.
    /// Gilt für jede Fläche, jeden Rand und jede Schachtzahl — die Tasten werden lieber etwas kleiner als übereinander gelegt.
    /// </summary>
    public static float FitFactor(float w1, float h1, UiInsets safe1, int bays, float wanted)
    {
        if (wanted <= 1f) return MathF.Max(1f, wanted <= 0f ? 1f : wanted);
        for (int k = (int)MathF.Round(wanted * 100f); k > 100; k--)
        {
            float f = k / 100f;
            var l = Compute(w1 / f, h1 / f, new UiInsets(safe1.Left / f, safe1.Top / f, safe1.Right / f, safe1.Bottom / f), bays);
            if (l.Overflow == 0) return f;
        }
        return 1f;
    }

    /// <summary>
    /// Runde 33: freie Lage für ein Feld <paramref name="w"/> × <paramref name="h"/> (z. B. die Meldung im Flug): möglichst bei
    /// (<paramref name="prefX"/> Mitte, <paramref name="prefY"/> oben), erst nach oben, dann nach unten gesucht, seitlich verschoben und notfalls bis
    /// <paramref name="minW"/> schmaler — ohne eine der <paramref name="occupied"/>-Flächen zu berühren. null = nirgends Platz.
    /// </summary>
    public static UiRect? FindSpot(UiRect area, IReadOnlyList<UiRect> occupied, float w, float h, float minW, float prefX, float prefY, float step = 8f)
    {
        for (float ww = w; ww >= minW - 0.01f; ww -= 40f)
        {
            float maxDy = area.H;
            for (int iy = 0; iy * step <= maxDy * 2f; iy++)
            {
                // 0, −step, +step, −2·step … (zuerst nach oben)
                float dy = (iy % 2 == 1 ? -1f : 1f) * ((iy + 1) / 2) * step;
                float y = prefY + dy;
                if (y < area.Y || y + h > area.Bottom) continue;
                for (int ix = 0; ix * 24f <= area.W * 2f; ix++)
                {
                    float dx = (ix % 2 == 1 ? -1f : 1f) * ((ix + 1) / 2) * 24f;
                    float x = prefX - ww / 2f + dx;
                    if (x < area.X || x + ww > area.Right) continue;
                    var r = new UiRect(x, y, ww, h);
                    bool hit = false;
                    for (int i = 0; i < occupied.Count && !hit; i++) hit = r.Intersects(occupied[i]);
                    if (!hit) return r;
                }
            }
        }
        return null;
    }
}
