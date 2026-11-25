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

/// <summary>
/// HUD-Neu (Entwurf A, Nutzerentscheidung): Elemente des Flug-HUD. Links Status und senkrechter Schubregler mit ◀ ▶ und RÜCK; oben Mitte Sektorchip mit
/// Lage-, Fahndungs- und Brandzeile; oben rechts Zielkarte, Karte und Menü; darunter Autopilot, Auto-Mining, Kontexttaste und rechts eine Spalte aus
/// Leistung (⚡), Nächster Gegner, Ziel folgen, Dauerfeuer, Ergeben, Angriff; unten rechts der Daumenbogen: FEUER und die Gruppenknöpfe nach Waffentyp
/// (aus Entwurf B: „Einzeln“ klappt die einzelnen Schächte als Leiste auf).
/// </summary>
public enum HudItem
{
    Status, Chip, Security, Wanted, Fire,
    Map, Hamburger, Card, Autopilot, AutoMine, Context,
    Power, NextEnemy, AimAssist, AutoFire, Surrender, Attack,
    Slider, TurnLeft, TurnRight, Reverse, ReverseLabel,
    FireBtn, Group0, Group1, Group2, Group3, Group4, Group5, Expand,
    Bay0, Bay1, Bay2, Bay3, Bay4, Bay5, Bay6, Bay7, Bay8, Bay9, Bay10, Bay11, Bay12, Bay13, Bay14,
    HitWarning, Escape,
    /// <summary>Runde 41: Auftragszeile unter der Lagezeile (Gold, leer = ausgeblendet).</summary>
    Mission,
    /// <summary>UI-Umbau (Interstellar-Pilot-2-Aufbau): Kopfleiste über die ganze Breite, Kasse darin, Symboltasten Nachrichten/Befehle/Zeitraffer.</summary>
    TopBar, Credits, Comms, Command, Fast,
    /// <summary>UI-Umbau: Taste „☰ Sektor“ unten links und die Fläche der Scanliste (offen).</summary>
    Scan, ScanPanel,
    /// <summary>Mehrpunkt-Route: Zeile „Route 2/4 → …“ unter der Auftragszeile (leer = ausgeblendet).</summary>
    Route,
}

/// <summary>
/// HUD-Neu — Zonen des Flug-HUD als reine Rechnung. Eingabe: logische Fläche der HUD-Schicht (nach Stretch und UI-Faktor), Safe-Area-Ränder an allen vier
/// Seiten, Zahl der Gruppen im Daumenbogen und der Einzelschächte (aufgeklappt oder nicht). Ausgabe: Rechteck je <see cref="HudItem"/>. Jede Taste ist
/// mindestens <see cref="Tap"/> (64) logische Punkte groß — bei „normal“ (Faktor 1,15) auf dem A15 ≥ 7 mm. Die Ecken sind fest verankert (links Status und
/// Regler, oben rechts Karte/Menü und Zielkarte, unten rechts der Daumenbogen); die aufgeklappten Einzelschächte füllen ein Raster von rechts nach links
/// zwischen Zielkarte und Daumenbogen und verdecken solange Autopilot, Auto-Mining und Kontexttaste.
/// </summary>
public sealed class FlightHudLayout
{
    public const float BaseW = UiLayout.BaseWidth, BaseH = UiLayout.BaseHeight;
    /// <summary>Mindestmaß einer Taste (logisch).</summary>
    public const float Tap = 64f;
    public const float CellW = 176f, CellH = 64f, CellStepX = 184f, CellStepY = 72f;
    public const float GroupW = 128f, GroupH = 64f, FireSize = 128f, IconW = 64f;
    public const int MaxGroups = 6, MaxBays = 15;
    public const float Gap = 3f;

    readonly Dictionary<HudItem, UiRect> _rects = new();
    readonly HashSet<HudItem> _hidden = new();

    /// <summary>Kompakt (kleiner als 1280 × 720) — nur zur Auskunft, die Anordnung ist dieselbe.</summary>
    public bool Compact { get; private init; }
    /// <summary>Sektorchip und Lagezeilen liegen unter dem Status (zu schmal oben); siehe <see cref="ChipMinW"/>.</summary>
    public bool ChipBelowStatus { get; private set; }
    /// <summary>Mindestbreite des Sektorchips oben zwischen Status und Zielkarte.</summary>
    public const float ChipMinW = 300f;
    /// <summary>Mindestbreite der Beschuss-/Fluchtzeile unten zwischen RÜCK und Daumenbogen.</summary>
    public const float WarnMinW = 320f;
    public UiRect Area { get; private init; }
    /// <summary>Zahl der Tasten, die keinen eigenen Platz fanden (überlappen oder aus der Fläche ragen); <see cref="FitFactor"/> senkt den Faktor, bis 0.</summary>
    public int Overflow { get; private set; }
    /// <summary>Einzelschächte aufgeklappt?</summary>
    public bool Expanded { get; private init; }

    public UiRect this[HudItem item] => _rects[item];
    public bool Has(HudItem item) => _rects.ContainsKey(item);
    public bool Hidden(HudItem item) => _hidden.Contains(item);
    public IReadOnlyDictionary<HudItem, UiRect> Rects => _rects;

    /// <summary>Tasten (antippbar) — für Überlappungs- und Größenprüfungen.</summary>
    public static bool IsInteractive(HudItem i) => i is not (HudItem.Status or HudItem.Chip or HudItem.Security or HudItem.Wanted or HudItem.Fire
        or HudItem.Card or HudItem.ReverseLabel or HudItem.HitWarning or HudItem.Escape or HudItem.Mission or HudItem.Route or HudItem.TopBar or HudItem.Credits or HudItem.ScanPanel);

    FlightHudLayout() { }

    /// <summary>
    /// Anordnung für eine HUD-Fläche <paramref name="w"/> × <paramref name="h"/> (logisch) mit Safe-Area-Rändern, <paramref name="groups"/> Gruppenknöpfen
    /// (ohne FEUER), <paramref name="bays"/> Einzelschächten und dem Zustand der Leiste (<paramref name="expanded"/>). <paramref name="fire"/>: FEUER sichtbar
    /// (Schiff hat Kampfwaffen). <paramref name="heat"/>: Hitzezeile im Status (Schalter <c>shipHeat</c>; der Status wird um <see cref="HeatRowH"/> höher).
    /// </summary>
    public static FlightHudLayout Compute(float w, float h, UiInsets safe, int groups, int bays, bool expanded, bool fire = true, bool heat = false)
    {
        var area = new UiRect(safe.Left, safe.Top, MathF.Max(0f, w - safe.Left - safe.Right), MathF.Max(0f, h - safe.Top - safe.Bottom));
        var l = new FlightHudLayout { Compact = area.W < BaseW - 0.5f || area.H < BaseH - 0.5f, Area = area, Expanded = expanded };
        l.Fill(area, Math.Clamp(groups, 0, MaxGroups), Math.Clamp(bays, 0, MaxBays), expanded, fire, heat);
        return l;
    }

    void Set(HudItem i, float x, float y, float rw, float rh) => _rects[i] = new UiRect(x, y, rw, rh);

    /// <summary>UI-Umbau: Höhe der Kopfleiste, Breite/Höhe der Randzellen.</summary>
    public const float BarH = 70f, RailW = 96f, RailH = 66f, RailStep = 72f, RailColStep = 104f;
    /// <summary>Höhe des Statusfelds; mit Hitzezeile um <see cref="HeatRowH"/> höher.</summary>
    public const float StatusH = 140f, HeatRowH = 20f;

    void Fill(UiRect a, int groups, int bays, bool expanded, bool fire, bool heat)
    {
        float x0 = a.X, y0 = a.Y, x1 = a.Right, y1 = a.Bottom;
        ChipBelowStatus = false;
        // ── Kopfleiste: links Sektor/Lage, rechts Kasse und fünf Symboltasten (Nachrichten, Karte, Befehle, Zeit, Pause) ──
        Set(HudItem.TopBar, x0, y0, a.W, BarH);
        const float iw = 72f, ih = Tap, ig = 6f;
        float ix = x1 - 6f - iw;
        // Nutzerwunsch: Karte und Nachrichten stehen im Regal über „☰“ unten links — in der Kopfleiste nur noch Befehle, Zeit und Pause
        foreach (var it in new[] { HudItem.Hamburger, HudItem.Fast, HudItem.Command })
        { Set(it, ix, y0 + 3, iw, ih); ix -= iw + ig; }
        foreach (var it in new[] { HudItem.Map, HudItem.Comms }) { Set(it, 0, 0, 0, 0); _hidden.Add(it); }
        float creditsW = 170f;
        Set(HudItem.Credits, ix + iw + ig - 12f - creditsW, y0 + 14, creditsW, 42);
        float chipX = x0 + 12f, chipW = MathF.Max(0f, _rects[HudItem.Credits].X - 8f - chipX);
        Set(HudItem.Chip, chipX, y0 + 14, chipW, 42);

        // ── links unter der Leiste: Status mit Fracht, darunter die Lagezeilen ──
        float sy = y0 + BarH + 8f;
        float statusH = StatusH + (heat ? HeatRowH : 0f);
        Set(HudItem.Status, x0 + 10, sy, 260, statusH);
        float ly = sy + statusH + 8f, lw = 260f;
        Set(HudItem.Security, x0 + 10, ly, lw, 20);
        Set(HudItem.Wanted, x0 + 10, ly + 22, lw, 20);
        Set(HudItem.Fire, x0 + 10, ly + 44, lw, 20);
        // Auftragszeile breiter als der Status (Ziel und Restzeit lesbar), höchstens bis vor die Zielkarte
        Set(HudItem.Mission, x0 + 10, ly + 66, MathF.Max(lw, MathF.Min(440f, x1 - 10 - 330 - 12 - (x0 + 10))), 18);
        Set(HudItem.Route, x0 + 10, ly + 86, _rects[HudItem.Mission].W, 18);   // Mehrpunkt-Route

        // ── rechts unter der Leiste: Zielkarte ──
        Set(HudItem.Card, x1 - 10 - 330, sy, 330, 118);

        // ── unten: links „☰ Sektor“ + ◀ ▶, rechts RÜCK + waagerechter Regler ──
        float by = y1 - 10 - 68;
        Set(HudItem.Scan, x0 + 10, by, 84, 68);
        Set(HudItem.TurnLeft, x0 + 102, by, 76, 68);
        Set(HudItem.TurnRight, x0 + 184, by, 76, 68);
        float sliderW = MathF.Min(360f, MathF.Max(240f, a.W * 0.3f));
        Set(HudItem.Slider, x1 - 10 - sliderW, y1 - 10 - 86, sliderW, 86);
        Set(HudItem.Reverse, _rects[HudItem.Slider].X - 8 - 72, by, 72, 68);
        Set(HudItem.ReverseLabel, 0, 0, 0, 0); _hidden.Add(HudItem.ReverseLabel);

        // ── rechter Rand: Zellen (76 × 66) in Spalten von rechts nach links zwischen Zielkarte und Regler ──
        float railTop = _rects[HudItem.Card].Bottom + 8f, railBottom = _rects[HudItem.Slider].Y - 8f;
        int rows = Math.Max(1, (int)MathF.Floor((railBottom - railTop + (RailStep - RailH)) / RailStep));
        var taken = new HashSet<(int Col, int Row)>();
        float ColX(int c) => x1 - 10 - RailW - c * RailColStep;
        float RowY(int r) => railTop + r * RailStep;
        int maxCol = 0;
        (int, int) TakeDown(int startCol)
        {
            for (int c = startCol; c < 40; c++)
                for (int r = 0; r < rows; r++)
                    if (taken.Add((c, r))) { maxCol = Math.Max(maxCol, c); return (c, r); }
            return (0, 0);
        }
        (int, int) TakeUp(int startCol)
        {
            for (int c = startCol; c < 40; c++)
                for (int r = rows - 1; r >= 0; r--)
                    if (taken.Add((c, r))) { maxCol = Math.Max(maxCol, c); return (c, r); }
            return (0, 0);
        }
        void Cell(HudItem it, (int C, int R) p) => Set(it, ColX(p.C), RowY(p.R), RailW, RailH);
        // Symboltasten außen von oben (Leistung, Nächster Gegner, Ziel folgen, Dauerfeuer, Ergeben, Angriff); ohne Kampfwaffen stehen die Geräte
        // (Bohrer, Traktor …) direkt unter ⚡ und die übrigen Symbole danach
        Cell(HudItem.Power, TakeDown(0));
        if (!fire)
        {
            for (int g = 0; g < MaxGroups; g++)
                if (g < groups) Cell(HudItem.Group0 + g, TakeDown(0));
        }
        foreach (var it in new[] { HudItem.NextEnemy, HudItem.AimAssist, HudItem.AutoFire, HudItem.Surrender, HudItem.Attack })
            Cell(it, TakeDown(0));
        // FEUER (zwei Zellen hoch) innen unten, Gruppen darüber von unten nach oben, dann „Einzeln“
        int fireCol = Math.Max(1, maxCol + (taken.Count(t => t.Col == maxCol) >= rows ? 1 : 0));
        if (fire && rows >= 2)
        {
            int fc = fireCol;
            while (taken.Contains((fc, rows - 1)) || taken.Contains((fc, rows - 2))) fc++;
            taken.Add((fc, rows - 1)); taken.Add((fc, rows - 2)); maxCol = Math.Max(maxCol, fc);
            Set(HudItem.FireBtn, ColX(fc), RowY(rows - 2), RailW, RowY(rows - 1) + RailH - RowY(rows - 2));
            fireCol = fc;
        }
        else { Set(HudItem.FireBtn, 0, 0, 0, 0); _hidden.Add(HudItem.FireBtn); }
        for (int g = 0; g < MaxGroups; g++)
        {
            if (g < groups) { if (fire) Cell(HudItem.Group0 + g, TakeUp(fireCol)); }
            else { Set(HudItem.Group0 + g, 0, 0, 0, 0); _hidden.Add(HudItem.Group0 + g); }
        }
        if (bays >= 2) Cell(HudItem.Expand, TakeUp(fireCol));
        else { Set(HudItem.Expand, 0, 0, 0, 0); _hidden.Add(HudItem.Expand); }
        float railLeft = ColX(maxCol);

        // ── unten Mitte: Kontexttaste, darunter Autopilot und Auto-Mining; darüber Beschuss- und Fluchtzeile ──
        float lo = x0 + 268f, hi = MathF.Min(_rects[HudItem.Reverse].X - 10f, railLeft - 8f + 0f);
        hi = _rects[HudItem.Reverse].X - 10f;
        float cw = MathF.Max(0f, MathF.Min(340f, hi - lo));
        float cx = Math.Clamp((x0 + x1) / 2f - cw / 2f, lo, MathF.Max(lo, hi - cw));
        float cy = y1 - 10 - 64 - 8 - 64;
        // die Mitte darf nicht in die rechten Spalten ragen (sie reichen bis über den Regler)
        if (cx + cw > railLeft - 8f && cy < railBottom) cy = MathF.Min(cy, y1 - 10 - 64 - 8 - 64);
        Set(HudItem.Context, cx, cy, cw, Tap);
        float half = MathF.Max(0f, (cw - 8f) / 2f);
        Set(HudItem.Autopilot, cx, y1 - 10 - Tap, half, Tap);
        Set(HudItem.AutoMine, cx + half + 8f, y1 - 10 - Tap, half, Tap);
        if (cw < 200f) Overflow++;
        float wl = lo, wr = MathF.Min(hi, railLeft - 8f);
        Set(HudItem.HitWarning, wl, cy - 62, MathF.Max(0f, wr - wl), 28);
        Set(HudItem.Escape, wl, cy - 32, MathF.Max(0f, wr - wl), 22);

        // ── Scanliste (offen): links zwischen Leiste und „☰ Sektor“ ──
        Set(HudItem.ScanPanel, x0 + 10, sy, MathF.Min(450f, MathF.Max(300f, a.W * 0.38f)), by - 8f - sy);

        // ── Einzelschächte (aufgeklappt): Zellen links der rechten Spalten zwischen Status und Spalten, über der unteren Reihe ──
        if (expanded && bays > 0)
        {
            _hidden.Add(HudItem.Context); _hidden.Add(HudItem.HitWarning); _hidden.Add(HudItem.Escape);
            float left = x0 + 10 + 260 + 8, right = MathF.Min(railLeft, _rects[HudItem.Card].X) - 8f, top = sy;
            // ui-check (2340×1080 mit oberem Rand, Leviathan): die unterste Zeile reichte bis 8 über die untere Reihe und damit in den höheren Regler
            float bottom = MathF.Min(by, _rects[HudItem.Slider].X < right ? _rects[HudItem.Slider].Y : by) - 8f;
            int brows = Math.Max(1, (int)MathF.Floor((bottom - top + 8f) / CellStepY));
            int cols = (int)MathF.Ceiling(bays / (float)brows);
            float cellW = MathF.Min(CellW, (right - left - (cols - 1) * 8f) / Math.Max(1, cols));
            if (cellW < 120f) { Overflow++; cellW = 120f; }
            for (int b = 0; b < MaxBays; b++)
            {
                if (b >= bays) { Set(HudItem.Bay0 + b, 0, 0, 0, 0); _hidden.Add(HudItem.Bay0 + b); continue; }
                int col = b / brows, row = b % brows;
                Set(HudItem.Bay0 + b, right - cellW - col * (cellW + 8f), top + row * CellStepY, cellW, CellH);
            }
        }
        else
            for (int b = 0; b < MaxBays; b++) { Set(HudItem.Bay0 + b, 0, 0, 0, 0); _hidden.Add(HudItem.Bay0 + b); }

        // Überlauf: sichtbare Tasten, die sich überlappen oder aus der Fläche ragen (Scanliste ist eine Überlagerung und zählt nicht)
        var vis = _rects.Where(kv => !_hidden.Contains(kv.Key) && kv.Value.W > 0f && kv.Key != HudItem.ScanPanel).ToList();
        for (int i = 0; i < vis.Count; i++)
        {
            if (!vis[i].Value.Inside(a, 0.5f)) { Overflow++; continue; }
            if (!IsInteractive(vis[i].Key)) continue;
            for (int j = 0; j < vis.Count; j++)
                if (j != i && (j > i || !IsInteractive(vis[j].Key)) && vis[j].Key != HudItem.TopBar && vis[i].Value.Intersects(vis[j].Value, -0.5f)) { Overflow++; break; }
        }
    }

    /// <summary>
    /// Größter UI-Faktor ≤ <paramref name="wanted"/>, bei dem auf der Fläche <paramref name="w1"/> × <paramref name="h1"/> (logisch bei Faktor 1) mit den Rändern
    /// <paramref name="safe1"/> jede Taste ihren Platz hat — geprüft im ungünstigsten Fall (Einzelschächte aufgeklappt). Schritte 0,01, nie unter 1.
    /// </summary>
    public static float FitFactor(float w1, float h1, UiInsets safe1, int groups, int bays, float wanted, bool heat = false)
    {
        if (wanted <= 1f) return MathF.Max(1f, wanted <= 0f ? 1f : wanted);
        for (int k = (int)MathF.Round(wanted * 100f); k > 100; k--)
        {
            float f = k / 100f;
            var s = new UiInsets(safe1.Left / f, safe1.Top / f, safe1.Right / f, safe1.Bottom / f);
            if (Compute(w1 / f, h1 / f, s, groups, bays, expanded: true, heat: heat).Overflow == 0 && Compute(w1 / f, h1 / f, s, groups, bays, expanded: false, heat: heat).Overflow == 0) return f;
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
        return EdgeSpot(area, occupied, w, h, minW, prefX, prefY);
    }

    /// <summary>
    /// Runde 35b: zweiter Durchgang, wenn das Raster nichts fand — Lagen an den Kanten der belegten Flächen (rechts/links neben, unter/über einer Fläche,
    /// am Bereichsrand), damit enge Lücken (eine Meldung zwischen zwei Tastenreihen) nicht durch das 8/24-Raster fallen. Die Lage mit dem kleinsten Abstand zur
    /// Wunschlage gewinnt.
    /// </summary>
    static UiRect? EdgeSpot(UiRect area, IReadOnlyList<UiRect> occupied, float w, float h, float minW, float prefX, float prefY)
    {
        for (float ww = w; ww >= minW - 0.01f; ww -= 40f)
        {
            var xs = new List<float> { area.X, area.Right - ww };
            var ys = new List<float> { area.Y, area.Bottom - h };
            foreach (var o in occupied) { xs.Add(o.Right); xs.Add(o.X - ww); ys.Add(o.Bottom); ys.Add(o.Y - h); }
            UiRect? best = null; float bestD = float.MaxValue;
            foreach (float y in ys)
            {
                if (y < area.Y - 0.01f || y + h > area.Bottom + 0.01f) continue;
                foreach (float x in xs)
                {
                    if (x < area.X - 0.01f || x + ww > area.Right + 0.01f) continue;
                    var r = new UiRect(x, y, ww, h);
                    bool hit = false;
                    for (int i = 0; i < occupied.Count && !hit; i++) hit = r.Intersects(occupied[i]);
                    if (hit) continue;
                    float d = MathF.Abs(x + ww / 2f - prefX) + MathF.Abs(y - prefY);
                    if (d < bestD) { bestD = d; best = r; }
                }
            }
            if (best is not null) return best;
        }
        return null;
    }
}
