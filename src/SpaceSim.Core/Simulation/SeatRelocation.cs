using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Kapitulation (docs/KAMPF.md „Kapitulation“): Verliert eine KI-Fraktion ihren Sitz (Hauptquartier/Firmensitz zerstört oder kapituliert), bleibt sie
/// handlungsfähig (Phase 22, <see cref="ViabilityIssueKind.NoSeat"/>): sofort entsteht ein Not-Sitz (Hauptquartier-Typ der Welt) in ihrem Gebiet — Sektor mit
/// den meisten eigenen Stationen unter ihren eigenen Sektoren, sonst der Sektor mit den meisten eigenen Stationen, sonst der erste herrenlose Sektor ohne
/// Piratenversteck. Die Kasse zahlt höchstens <see cref="EconomyRules.SeatRelocationShare"/> der Baukosten (nie unter 0), der Rest wird erlassen — eine
/// ausdrückliche Geldquelle wie der Notkauf (<see cref="SeatRelocated"/>, <see cref="TreasuryUpkeep"/> „seat“). Kein Zufall.
/// </summary>
public static class SeatRelocation
{
    public static bool HasSeat(Universe u, FactionId f) =>
        u.Stations.Values.Any(st => !st.IsBuildSite && st.Owner == (ActorId)f && StationProtection.KindOf(u, st) is GovernmentKind.Headquarters or GovernmentKind.CompanySeat);

    /// <summary>Hauptquartier-Typ der Welt (erster nach Schlüssel), null = keiner.</summary>
    public static StationType? SeatType(Universe u) =>
        u.StationTypes.Values.Where(t => t.Government == GovernmentKind.Headquarters).OrderBy(t => t.Key, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>Stellt den Sitz einer KI-Fraktion wieder her, wenn er fehlt. null = nichts zu tun oder kein Platz/Typ.</summary>
    public static Station? Ensure(Universe u, FactionId f)
    {
        if (!u.IsNpcFaction(f) || !u.Actors.TryGetValue(f, out var actor) || actor.Kind == ActorKind.Outlaw || HasSeat(u, f)) return null;
        if (SeatType(u) is not { } t) return null;
        foreach (var sec in Candidates(u, f))
        {
            var own = sec.Stations.Where(id => u.Stations.TryGetValue(id, out var s) && s.Owner == (ActorId)f).Select(id => u.Stations[id].Position).ToList();
            var anchor = own.Count > 0 ? new Vec2(own.Average(p => p.X), own.Average(p => p.Y)) : new Vec2(0f, 0f);
            float max = MathF.Max(220f, sec.Extent);
            var spot = Outposts.FreeSpot(u, sec, anchor, t.Radius, t.Radius + 30f, max) ?? Outposts.FreeSpot(u, sec, new Vec2(0f, 0f), t.Radius, 0f, max);
            if (spot is not { } p) continue;
            float cost = t.BuildCost * MathF.Max(0f, u.Rules.StationBuildCostFactor);
            float paid = MathF.Min(cost * Math.Clamp(u.Rules.SeatRelocationShare, 0f, 1f), MathF.Max(0f, actor.Treasury.Credits));
            actor.Treasury.Credits -= paid;
            var id = u.NewStationId();
            string name = u.Factions.TryGetValue(f, out var fac) ? $"{t.Name} – {fac.Name}" : StationFactory.BuiltName(u, t, sec.Id);
            var st = StationFactory.Create(u, id, t, name, sec.Id, p, actor.Id);
            st.BankruptcyProtected = true;
            st.Credits = 2000f;
            u.AddStation(st);
            u.Events.Publish(new StationBuilt(id, sec.Id, t.Key));
            if (paid > 0f) u.Events.Publish(new TreasuryUpkeep(actor.Id, "seat", paid, 0f));
            u.Events.Publish(new SeatRelocated(f, id, sec.Id, paid, cost - paid));
            if (SectorControl.Update(u, sec)) u.TouchAccess();
            return st;
        }
        return null;
    }

    static IEnumerable<Sector> Candidates(Universe u, FactionId f)
    {
        int Count(Sector s) => s.Stations.Count(id => u.Stations.TryGetValue(id, out var st) && !st.IsBuildSite && st.Owner == (ActorId)f);
        bool Hideout(Sector s) => s.Stations.Any(id => u.Stations.TryGetValue(id, out var st) && StationProtection.IsHideout(u, st));
        var all = u.Sectors.Values.OrderBy(s => s.Id.Value).ToList();
        foreach (var s in all.Where(s => s.Owner == f).OrderByDescending(Count).ThenBy(s => s.Id.Value)) yield return s;
        foreach (var s in all.Where(s => s.Owner != f && Count(s) > 0 && !Hideout(s)).OrderByDescending(Count).ThenBy(s => s.Id.Value)) yield return s;
        foreach (var s in all.Where(s => !u.IsNpcFaction(s.Owner) && !u.IsPlayerFaction(s.Owner) && Count(s) == 0 && !Hideout(s))) yield return s;
        foreach (var s in all.Where(s => !Hideout(s) && s.Owner != f && Count(s) == 0)) yield return s;
    }
}
