using SpaceSim.Core.Model;

namespace SpaceSim.Core.Diagnostics;

/// <summary>Stabiler Hash über den simulationsrelevanten Zustand. Für Determinismus- und Speichern/Laden-Tests.</summary>
public static class UniverseHash
{
    public static ulong Compute(Universe u)
    {
        ulong h = 14695981039346656037UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        void MixF(float f) => Mix((ulong)BitConverter.SingleToInt32Bits(MathF.Round(f, 3)));
        void MixString(string t) { Mix((ulong)t.Length); foreach (var c in t) Mix(c); }

        Mix((ulong)u.Tick); Mix(u.Rng.State);
        foreach (var st in u.Stations.Values.OrderBy(s => s.Id.Value))
        {
            Mix((ulong)st.Id.Value); MixF(st.Credits);
            foreach (var (g, e) in st.Market.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(e.Amount); MixF(e.Price); }
        }
        foreach (var s in u.Ships.Values.OrderBy(s => s.Id.Value))
        {
            Mix((ulong)s.Id.Value); Mix((ulong)s.Sector.Value); MixF(s.Position.X); MixF(s.Position.Y); MixF(s.Credits);
            Mix(s.Transit is null ? 0UL : (ulong)s.Transit.ArrivalTick); Mix((ulong)s.Phase); Mix((ulong)s.MinePhase); MixF(s.ShotTimer); Mix((ulong)s.MineAsteroid); Mix((ulong)s.Capabilities); Mix((ulong)s.Controller);
            MixString(s.ModelKey);                                   // Phase 3: Modell und Fit gehören zum Spielstand
            foreach (var f in s.Fit) { if (f is null) Mix(0UL); else MixString(f); }
            foreach (var (g, a) in s.Cargo.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(a); }
            foreach (var (g, a) in s.Fuel.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(a); }   // Phase 4: Tank gehört zum Spielstand
        }
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value))
            foreach (var a in sec.Asteroids.OrderBy(a => a.Id)) { Mix((ulong)a.Id); MixF(a.Ore); MixF(a.Position.X); MixF(a.Position.Y); }
        MixF(u.PlayerWallet.Credits); Mix((ulong)u.PlayerWallet.Faction.Value);   // Phase 5b: Spielerkasse gehört zum Spielstand
        Mix((ulong)u.PlayerShip.Value);   // Phase 5c: aktives Schiff kann wechseln (Kauf, Hangar)
        return h;
    }
}
