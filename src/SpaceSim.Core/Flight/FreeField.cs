namespace SpaceSim.Core.Flight;

/// <summary>
/// Runde 16 (dritter Handy-Test: die Gefechtskamera fand nur ein Feld von 35 % der Bildhöhe, weil die Tastenspalte rechts oben „oben“ abschnitt): das freie
/// Bildfeld als größtes achsparalleles Rechteck um einen Mittelpunkt, das keine HUD-Fläche schneidet. Rein: Kandidaten für links/rechts sind die Kanten der
/// Flächen (und der Bildrand); zu jedem Paar wird das höchste freie Band um den Mittelpunkt bestimmt; gewinnt die größte Fläche.
/// </summary>
public static class FreeField
{
    public readonly record struct Box(float X, float Y, float W, float H)
    {
        public float X1 => X + W;
        public float Y1 => Y + H;
    }

    /// <summary>
    /// Größtes freies Rechteck im Bild (<paramref name="width"/> × <paramref name="height"/>, y nach unten), das den Punkt (<paramref name="cx"/>, <paramref name="cy"/>)
    /// enthält und keine der <paramref name="occupied"/> Flächen schneidet. Liegt der Punkt selbst unter einer Fläche, wird das ganze Bild geliefert.
    /// </summary>
    public static Box Largest(float width, float height, IReadOnlyList<Box> occupied, float cx, float cy)
    {
        foreach (var r in occupied) if (cx > r.X && cx < r.X1 && cy > r.Y && cy < r.Y1) return new Box(0f, 0f, width, height);
        var lefts = new List<float> { 0f }; var rights = new List<float> { width };
        foreach (var r in occupied)
        {
            if (r.X1 <= cx) lefts.Add(MathF.Max(0f, r.X1));
            if (r.X >= cx) rights.Add(MathF.Min(width, r.X));
        }
        Box best = new(cx, cy, 0f, 0f); float bestArea = -1f;
        foreach (var l in lefts)
            foreach (var rt in rights)
            {
                if (rt <= l) continue;
                // höchstes Band um cy in [l, rt]: oben die tiefste Unterkante darüber, unten die höchste Oberkante darunter
                float top = 0f, bottom = height; bool blocked = false;
                foreach (var r in occupied)
                {
                    if (r.X1 <= l || r.X >= rt) continue;   // überlappt die Spalte nicht
                    if (r.Y1 <= cy) top = MathF.Max(top, r.Y1);
                    else if (r.Y >= cy) bottom = MathF.Min(bottom, r.Y);
                    else { blocked = true; break; }   // schneidet die Mittellinie
                }
                if (blocked || bottom <= top) continue;
                float area = (rt - l) * (bottom - top);
                if (area > bestArea) { bestArea = area; best = new Box(l, top, rt - l, bottom - top); }
            }
        return bestArea < 0f ? new Box(0f, 0f, width, height) : best;
    }
}
