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
        or HudItem.Card or HudItem.ReverseLabel or HudItem.HitWarning or HudItem.Escape);

    FlightHudLayout() { }

    /// <summary>
    /// Anordnung für eine HUD-Fläche <paramref name="w"/> × <paramref name="h"/> (logisch) mit Safe-Area-Rändern, <paramref name="groups"/> Gruppenknöpfen
    /// (ohne FEUER), <paramref name="bays"/> Einzelschächten und dem Zustand der Leiste (<paramref name="expanded"/>). <paramref name="fire"/>: FEUER sichtbar
    /// (Schiff hat Kampfwaffen).
    /// </summary>
    public static FlightHudLayout Compute(float w, float h, UiInsets safe, int groups, int bays, bool expanded, bool fire = true)
    {
        var area = new UiRect(safe.Left, safe.Top, MathF.Max(0f, w - safe.Left - safe.Right), MathF.Max(0f, h - safe.Top - safe.Bottom));
        var l = new FlightHudLayout { Compact = area.W < BaseW - 0.5f || area.H < BaseH - 0.5f, Area = area, Expanded = expanded };
        l.Fill(area, Math.Clamp(groups, 0, MaxGroups), Math.Clamp(bays, 0, MaxBays), expanded, fire);
        return l;
    }

    void Set(HudItem i, float x, float y, float rw, float rh) => _rects[i] = new UiRect(x, y, rw, rh);

    void Fill(UiRect a, int groups, int bays, bool expanded, bool fire)
    {
        float x0 = a.X, y0 = a.Y, x1 = a.Right, y1 = a.Bottom;
        // ── links: Status, senkrechter Schubregler, ◀ ▶ und RÜCK unten ──
        Set(HudItem.Status, x0 + 12, y0 + 8, 300, 132);
        Set(HudItem.Slider, x0 + 12, y0 + 152, 88, MathF.Max(Tap * 2f, y1 - 12 - (y0 + 152)));
        Set(HudItem.TurnLeft, x0 + 112, y1 - 112, 104, 100);
        Set(HudItem.TurnRight, x0 + 224, y1 - 112, 104, 100);
        Set(HudItem.ReverseLabel, x0 + 332, y1 - 108, 112, 18);
        Set(HudItem.Reverse, x0 + 336, y1 - 84, 96, 72);
        // ── oben rechts: Menü, Karte; Zielkarte links daneben; darunter Autopilot, Auto-Mining, Kontexttaste ──
        Set(HudItem.Hamburger, x1 - 12 - IconW, y0 + 8, IconW, IconW);
        Set(HudItem.Map, x1 - 12 - 2 * IconW - 8, y0 + 8, IconW, IconW);
        float cardX = x1 - 12 - 2 * IconW - 8 - 8 - 330;
        Set(HudItem.Card, cardX, y0 + 8, 330, 118);
        Set(HudItem.Autopilot, cardX, y0 + 134, 161, Tap);
        Set(HudItem.AutoMine, cardX + 169, y0 + 134, 161, Tap);
        Set(HudItem.Context, cardX, y0 + 206, 330, Tap);
        // rechte Spalte (2 × 3 Symboltasten) unter Karte/Menü: Leistung, Nächster Gegner, Ziel folgen, Dauerfeuer, Ergeben, Angriff
        float cx0 = x1 - 12 - 2 * IconW - 8, cx1 = x1 - 12 - IconW;
        Set(HudItem.Power, cx0, y0 + 80, IconW, IconW);
        Set(HudItem.NextEnemy, cx1, y0 + 80, IconW, IconW);
        Set(HudItem.AimAssist, cx0, y0 + 152, IconW, IconW);
        Set(HudItem.AutoFire, cx1, y0 + 152, IconW, IconW);
        Set(HudItem.Surrender, cx0, y0 + 224, IconW, IconW);
        Set(HudItem.Attack, cx1, y0 + 224, IconW, IconW);
        // ── oben Mitte: Sektorchip und Zeilen zwischen Status und Zielkarte ──
        float lo = x0 + 324f, hi = cardX - 8f;
        float cw = MathF.Max(0f, MathF.Min(430f, hi - lo));
        float chipY = y0 + 8;
        float chipX = Math.Clamp((x0 + x1) / 2f - cw / 2f, lo, MathF.Max(lo, hi - cw));
        // schmal (UI-Faktor 1,3 auf 16:9 und schmaler): oben zwischen Status und Zielkarte ist kein Platz für Sektorname, Fraktion und Lagezeile — der Block
        // rückt unter den Status rechts neben den Regler (dort ist bis zur Zielkarten-Spalte Platz)
        ChipBelowStatus = cw < ChipMinW;
        if (ChipBelowStatus)
        {
            chipX = x0 + 112f; chipY = y0 + 148f;
            cw = MathF.Max(0f, MathF.Min(430f, cardX - 8f - chipX));
        }
        Set(HudItem.Chip, chipX, chipY, cw, 42);
        Set(HudItem.Security, chipX, chipY + 46, cw, 18);
        Set(HudItem.Wanted, chipX, chipY + 66, cw, 20);
        Set(HudItem.Fire, chipX, chipY + 88, cw, 20);
        // ── unten rechts: Daumenbogen — FEUER in der Ecke, Gruppen im Bogen darum, „Einzeln“ in der nächsten freien Stelle ──
        float fx = x1 - 12 - FireSize, fy = y1 - 12 - FireSize;
        Set(HudItem.FireBtn, fx, fy, FireSize, FireSize);
        if (!fire) _hidden.Add(HudItem.FireBtn);
        var slots = new List<UiRect>
        {
            new(fx - 8 - GroupW, y1 - 12 - GroupH, GroupW, GroupH),                    // links unten neben FEUER
            new(fx - 8 - GroupW, y1 - 12 - 2 * GroupH - 8, GroupW, GroupH),            // links oben neben FEUER
            new(x1 - 12 - GroupW, fy - 8 - GroupH, GroupW, GroupH),                    // über FEUER
            new(fx - 16 - 2 * GroupW, y1 - 12 - GroupH, GroupW, GroupH),               // zweiter Ring links unten
            new(fx - 16 - 2 * GroupW, y1 - 12 - 2 * GroupH - 8, GroupW, GroupH),       // zweiter Ring links oben
            new(fx - 8 - GroupW, fy - 8 - GroupH, GroupW, GroupH),                     // links über dem ersten Ring
            new(fx - 16 - 2 * GroupW, fy - 8 - GroupH, GroupW, GroupH),
        };
        // ohne FEUER rückt der Bogen in die Ecke (erste Stelle = FEUER-Platz unten rechts)
        if (!fire) slots.Insert(0, new UiRect(x1 - 12 - GroupW, y1 - 12 - GroupH, GroupW, GroupH));
        int si = 0;
        for (int g = 0; g < MaxGroups; g++)
        {
            if (g < groups && si < slots.Count) Set(HudItem.Group0 + g, slots[si].X, slots[si].Y, slots[si].W, slots[si].H);
            else { Set(HudItem.Group0 + g, 0, 0, 0, 0); _hidden.Add(HudItem.Group0 + g); }
            if (g < groups) si++;
        }
        if (bays >= 2 && si < slots.Count) Set(HudItem.Expand, slots[si].X, slots[si].Y, slots[si].W, slots[si].H);
        else { Set(HudItem.Expand, 0, 0, 0, 0); _hidden.Add(HudItem.Expand); }
        // Beschuss- und Fluchtzeile unten Mitte zwischen RÜCK und Daumenbogen
        float arcLeft = MathF.Min(fx, _rects.Where(kv => kv.Key is >= HudItem.Group0 and <= HudItem.Expand && !_hidden.Contains(kv.Key)).Select(kv => kv.Value.X).DefaultIfEmpty(fx).Min());
        float wl = x0 + 452f, wr = arcLeft - 12f;
        if (wr - wl >= WarnMinW)
        {
            Set(HudItem.HitWarning, wl, y1 - 64, MathF.Max(0f, wr - wl), 28);
            Set(HudItem.Escape, wl, y1 - 32, MathF.Max(0f, wr - wl), 22);
        }
        else
        {
            // schmal (Bogen mit mehreren Gruppen bei großem UI-Faktor): die Zeilen rücken über die Wendetasten, dort reichen sie vom Regler bis zum Bogen
            wl = x0 + 112f;
            Set(HudItem.HitWarning, wl, y1 - 178, MathF.Max(0f, wr - wl), 28);
            Set(HudItem.Escape, wl, y1 - 146, MathF.Max(0f, wr - wl), 22);
        }

        // ── Einzelschächte (aufgeklappt): Raster 176 × 64, von rechts (links der Symbolspalte) nach links, von oben (unter der Zielkarte) nach unten bis über
        // den Daumenbogen; solange offen, sind Autopilot, Auto-Mining und Kontexttaste verdeckt ──
        float arcTop = _rects.Where(kv => kv.Key is >= HudItem.FireBtn and <= HudItem.Expand && !_hidden.Contains(kv.Key)).Select(kv => kv.Value.Y).DefaultIfEmpty(fy).Min();
        if (expanded && bays > 0)
        {
            _hidden.Add(HudItem.Autopilot); _hidden.Add(HudItem.AutoMine); _hidden.Add(HudItem.Context);
            float top = y0 + 134, bottom = arcTop - 8f, right = cx0 - 8f;
            int rows = Math.Max(1, (int)MathF.Floor((bottom - top + 8f) / CellStepY));
            for (int b = 0; b < MaxBays; b++)
            {
                if (b >= bays) { Set(HudItem.Bay0 + b, 0, 0, 0, 0); _hidden.Add(HudItem.Bay0 + b); continue; }
                int col = b / rows, row = b % rows;
                Set(HudItem.Bay0 + b, right - CellW - col * CellStepX, top + row * CellStepY, CellW, CellH);
            }
        }
        else
            for (int b = 0; b < MaxBays; b++) { Set(HudItem.Bay0 + b, 0, 0, 0, 0); _hidden.Add(HudItem.Bay0 + b); }

        // Überlauf: sichtbare Tasten, die sich überlappen oder aus der Fläche ragen
        var vis = _rects.Where(kv => !_hidden.Contains(kv.Key) && kv.Value.W > 0f).ToList();
        for (int i = 0; i < vis.Count; i++)
        {
            if (!vis[i].Value.Inside(a, 0.5f)) { Overflow++; continue; }
            // jede Taste gegen jede andere Fläche (auch Texte wie Chip und Lagezeilen, die vor ihr in der Liste stehen)
            if (!IsInteractive(vis[i].Key)) continue;
            for (int j = 0; j < vis.Count; j++)
                if (j != i && (j > i || !IsInteractive(vis[j].Key)) && vis[i].Value.Intersects(vis[j].Value, -0.5f)) { Overflow++; break; }
        }
    }

    /// <summary>
    /// Größter UI-Faktor ≤ <paramref name="wanted"/>, bei dem auf der Fläche <paramref name="w1"/> × <paramref name="h1"/> (logisch bei Faktor 1) mit den Rändern
    /// <paramref name="safe1"/> jede Taste ihren Platz hat — geprüft im ungünstigsten Fall (Einzelschächte aufgeklappt). Schritte 0,01, nie unter 1.
    /// </summary>
    public static float FitFactor(float w1, float h1, UiInsets safe1, int groups, int bays, float wanted)
    {
        if (wanted <= 1f) return MathF.Max(1f, wanted <= 0f ? 1f : wanted);
        for (int k = (int)MathF.Round(wanted * 100f); k > 100; k--)
        {
            float f = k / 100f;
            var s = new UiInsets(safe1.Left / f, safe1.Top / f, safe1.Right / f, safe1.Bottom / f);
            if (Compute(w1 / f, h1 / f, s, groups, bays, expanded: true).Overflow == 0 && Compute(w1 / f, h1 / f, s, groups, bays, expanded: false).Overflow == 0) return f;
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
