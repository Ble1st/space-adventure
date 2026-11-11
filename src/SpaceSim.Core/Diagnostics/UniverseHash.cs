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
            Mix((ulong)st.Id.Value); MixF(st.Credits); Mix((ulong)st.Owner.Value);   // Phase 9: Besitzer (Akteur)
            foreach (var (g, e) in st.Market.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(e.Amount); MixF(e.Price); }
            // Phase 8: Schutz, Baustelle (Typ, Stückliste, Bauschiff, Zeiten, Zustand) und Spielerlager gehören zum Spielstand
            Mix(st.BankruptcyProtected ? 1UL : 0UL); MixString(st.TypeKey);
            if (st.Site is { } bs)
            {
                MixString(bs.TypeKey); Mix((ulong)bs.Builder.Value); Mix((ulong)bs.LastProgressTick); Mix((ulong)bs.BlockedSince);
                Mix((ulong)bs.AssemblyTicks); Mix((ulong)bs.InvestorActor); Mix(bs.Emergency ? 1UL : 0UL); Mix(bs.Abandoned ? 1UL : 0UL); MixF(bs.Budget);
                foreach (var (g, n) in bs.Required.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(n); }
            }
            foreach (var (g, n) in st.PlayerStorage.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(n); }
        }
        foreach (var f in u.Factions.Values.OrderBy(f => f.Id.Value)) Mix((ulong)f.LastEmergencyBuildTick);
        foreach (var s in u.Ships.Values.OrderBy(s => s.Id.Value))
        {
            Mix((ulong)s.Id.Value); Mix((ulong)s.Owner.Value); Mix((ulong)s.Sector.Value); MixF(s.Position.X); MixF(s.Position.Y); MixF(s.Credits);
            Mix(s.Transit is null ? 0UL : (ulong)s.Transit.ArrivalTick); Mix((ulong)s.Phase); Mix((ulong)s.MinePhase); MixF(s.ShotTimer); Mix((ulong)s.MineAsteroid); Mix((ulong)s.Capabilities); Mix((ulong)s.Controller);
            MixString(s.ModelKey);                                   // Phase 3: Modell und Fit gehören zum Spielstand
            foreach (var f in s.Fit) { if (f is null) Mix(0UL); else MixString(f); }
            foreach (var (g, a) in s.Cargo.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(a); }
            foreach (var (g, a) in s.Fuel.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(a); }   // Phase 4: Tank gehört zum Spielstand
            Mix((ulong)s.Role); Mix((ulong)s.JobSince);   // Phase 6: Aufgabe (Etikett) und Beginn der Aufgabe wechseln bei Multirole-Schiffen
            Mix((ulong)s.Quality);                          // Phase 7: Güteklasse, Slotliste (Bonusslots, vergrößerte Slots) und Mitleidszähler
            foreach (var sl in s.Slots) { MixString(sl.Type); Mix((ulong)sl.Size); }
            foreach (var (k, v) in s.UpgradePity.OrderBy(k => k.Key)) { Mix((ulong)(long)k); Mix((ulong)v); }
        }
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value))
            foreach (var a in sec.Asteroids.OrderBy(a => a.Id)) { Mix((ulong)a.Id); MixF(a.Ore); MixF(a.Position.X); MixF(a.Position.Y); MixF(a.BaseRadius); }
        Mix((ulong)u.NextAsteroidId);   // Phase 8: Fragment-Ids sind fortlaufend
        // Phase 9: Akteure (Art, Mitgliedschaft, Kasse — darunter die Spielerkasse aus Phase 5b —, Persönlichkeit, Beziehungsabweichungen)
        foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value))
        {
            Mix((ulong)a.Id.Value); Mix((ulong)a.Kind); Mix((ulong)(a.Membership?.Value ?? -1)); MixF(a.Treasury.Credits); Mix(a.ShipsUseTreasury ? 1UL : 0UL);
            MixF(a.Personality.Aggression); MixF(a.Personality.Greed); MixF(a.Personality.Cooperation); MixF(a.Personality.Lawfulness);
            foreach (var (k, v) in a.Relations.OrderBy(k => k.Key.Value)) { Mix((ulong)k.Value); MixF(v); }
        }
        foreach (var f in u.Factions.Values.OrderBy(f => f.Id.Value))
            foreach (var (k, v) in f.Relations.OrderBy(k => k.Key.Value)) { Mix((ulong)k.Value); MixF(v); }
        Mix((ulong)u.PlayerActor.Value); Mix((ulong)u.FreeActor.Value); Mix((ulong)u.NextActorId);
        Mix((ulong)u.PlayerShip.Value);   // Phase 5c: aktives Schiff kann wechseln (Kauf, Hangar)
        return h;
    }
}
