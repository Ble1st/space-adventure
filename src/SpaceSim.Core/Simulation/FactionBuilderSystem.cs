using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 20: Bauschiffe der Fraktionen. Bis Phase 19 lebten Bauschiffe von der Baugebühr in ihrer Betriebskasse, lagen zwischen den Bauwellen untätig, wurden
/// als pleite aufgegeben und je Baustelle neu gekauft (am Ende langer Läufe gab es nie ein Bauschiff). Jetzt gehören die Bauschiffe einer Fraktion (Besitzer =
/// Fraktionsakteur) zum Bestand der Fraktion: Ihre Kasse zahlt einen kleinen Unterhalt (<see cref="EconomyRules.BuilderUpkeepPerTick"/>, <c>TreasuryUpkeep</c>
/// „builder“, wie bei der Polizei), hält einen Mindestbestand (<see cref="EconomyRules.MinBuildersPerFaction"/>, Kauf über die gemeinsame Regel
/// <see cref="ConstructionRules.CommissionBuilder(Universe, FactionId, SectorId, StationId)"/>), und der <see cref="FleetSystem"/> gibt sie nicht als pleite
/// auf. Überzählige Bauschiffe, die lange untätig sind (<see cref="EconomyRules.IdleRetireTicks"/>), mustert die Fraktion aus (Betriebsgeld in die Kasse).
/// </summary>
public sealed class FactionBuilderSystem : ISimSystem
{
    /// <summary>Takt in Ticks (Unterhalt und Bestand).</summary>
    public const int Interval = 100;

    /// <summary>KI-Bauschiff, das einer Fraktion gehört (Besitzer = Fraktionsakteur)?</summary>
    public static bool IsFactionBuilder(Universe u, Ship ship) =>
        ship.Controller == Controller.Ai && ship.Role == ShipRole.Builder && u.Factions.ContainsKey(new FactionId(ship.Owner.Value));

    /// <summary>Bauschiffe einer Fraktion (nach Id).</summary>
    public static List<Ship> Of(Universe u, FactionId f) =>
        u.ShipsById().Where(s => IsFactionBuilder(u, s) && s.Owner.Value == f.Value).ToList();

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (r.MinBuildersPerFaction <= 0 && r.BuilderUpkeepPerTick <= 0f) return;
        foreach (var f in u.Factions.Keys.OrderBy(k => k.Value).ToList())
        {
            if (!u.Actors.TryGetValue(f, out var actor) || actor.Kind == ActorKind.Outlaw) continue;
            var builders = Of(u, f);
            float upkeep = r.BuilderUpkeepPerTick * builders.Count * dt;
            if (upkeep > 0f)
            {
                float paid = MathF.Min(upkeep, MathF.Max(0f, actor.Treasury.Credits));
                actor.Treasury.Credits -= paid;
                u.Events.Publish(new TreasuryUpkeep(actor.Id, "builder", paid, upkeep - paid));
            }
            if (builders.Count < r.MinBuildersPerFaction)
            {
                // Bezug für die Erreichbarkeit der Werft: Hauptquartier/Regierungsstation der Fraktion, sonst irgendeine ihrer Stationen
                var home = SecuritySystem.HomeBaseFor(u, f, null)
                           ?? u.StationsById().FirstOrDefault(st => !st.IsBuildSite && st.Owner.Value == f.Value);
                if (home is not null) ConstructionRules.CommissionBuilder(u, f, home.Sector, default, priority: true);   // Phase 22: Ersatz hat Vorrang
            }
            else if (builders.Count > r.MinBuildersPerFaction && r.IdleRetireTicks > 0)
            {
                var spare = builders.Where(s => s.Transit is null && s.Order is null && s.IdleSince >= 0 && u.Tick - s.IdleSince > r.IdleRetireTicks
                                                && !u.Stations.Values.Any(st => st.Site is { } bs && bs.Builder == s.Id))
                    .OrderByDescending(s => s.Id.Value).FirstOrDefault();
                if (spare is not null)
                {
                    float credits = MathF.Max(0f, spare.Credits);
                    actor.Treasury.Credits += credits;
                    spare.Credits = 0f;
                    u.Events.Publish(new ShipRetired(spare.Id, spare.Owner, credits, true));
                    // Phase 22: Grund c für einen Dienst der Fraktion — lange ohne Nutzen und überzählig (der Mindestbestand bleibt)
                    Departures.RemoveShip(u, spare, DepartureReason.Unprofitable, "spare_builder");
                }
            }
        }
    }
}
