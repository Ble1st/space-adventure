using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 24 (KAMPF.md 3.4): NPCs laden Munition nach und zahlen dafür. An einer Station mit Munitionslager wird zum Marktpreis aus dem Lager gekauft (die
/// Station erhält das Geld); ohne Lager (Piratenversteck, Heimatbasis ohne Munition) zum Basispreis × <see cref="EconomyRules.AmmoBaseMarkup"/> als Geldsenke
/// (wie die Reparatur an der Basis). Händler zahlen selbst, Polizei und Piraten aus der Kasse ihres Besitzers (<see cref="TreasuryUpkeep"/> „ammo“).
/// Mit abgeschaltetem Schalter <see cref="EconomyRules.AmmoActive"/> gibt es nichts zu laden. Der Spielerdienst <c>rearm</c> kommt mit Phase 25.
/// </summary>
public static class Rearm
{
    public const string Purpose = "ammo";

    /// <summary>Munitionsware der Welt (kleinste Id der Kategorie „ammo“); null = keine.</summary>
    public static Good? AmmoGood(Universe u)
    {
        Good? best = null;
        foreach (var g in u.Goods.Values)
            if (g.Category == FeatureSwitches.AmmoCategory && (best is null || g.Id.Value < best.Id.Value)) best = g;
        return best;
    }

    /// <summary>Wie viel Munition fehlt (0 mit abgeschaltetem Schalter)?</summary>
    public static float Missing(Universe u, Ship ship) => u.Rules.AmmoActive ? MathF.Min(ship.Systems.AmmoMissing, ship.Stats.AmmoCapacity) : 0f;

    /// <summary>
    /// Kauft fehlende Munition aus dem Lager der Station (Marktpreis, höchstens Bestand und was <paramref name="payer"/> zahlen kann). Liefert die Einheiten.
    /// </summary>
    public static float AtStation(Universe u, Ship ship, Station st, ICreditAccount payer, ActorId payerActor, bool treasury, float maxUnits = float.MaxValue)
    {
        float missing = MathF.Min(Missing(u, ship), MathF.Max(0f, maxUnits));
        if (missing <= 0f || AmmoGood(u) is not { } good || !st.Market.TryGetValue(good.Id, out var entry) || entry.Amount <= 0f) return 0f;
        float price = entry.Price > 0f ? entry.Price : good.BasePrice;
        float units = MathF.Min(missing, entry.Amount);
        if (price > 0f) units = MathF.Min(units, MathF.Max(0f, payer.Credits) / price);
        if (units <= 0.001f) return 0f;
        float cost = units * price;
        entry.Amount -= units;
        st.Credits += cost;
        payer.Credits -= cost;
        ship.Systems.AmmoMissing = MathF.Max(0f, ship.Systems.AmmoMissing - units);
        u.Events.Publish(new AmmoRearmed(ship.Id, st.Id, units, cost, payerActor, treasury));
        if (treasury && cost > 0f) u.Events.Publish(new TreasuryUpkeep(payerActor, Purpose, cost, 0f));
        return units;
    }

    /// <summary>Lädt ohne Lager zum Basispreis × Aufschlag nach (Geldsenke), so weit <paramref name="payer"/> zahlen kann. Liefert die Einheiten.</summary>
    public static float AtBase(Universe u, Ship ship, ICreditAccount payer, ActorId payerActor, bool treasury)
    {
        float missing = Missing(u, ship);
        if (missing <= 0f || AmmoGood(u) is not { } good) return 0f;
        float price = good.BasePrice * MathF.Max(0f, u.Rules.AmmoBaseMarkup);
        float units = price > 0f ? MathF.Min(missing, MathF.Max(0f, payer.Credits) / price) : missing;
        if (units <= 0.001f) return 0f;
        float cost = units * price;
        payer.Credits -= cost;
        ship.Systems.AmmoMissing = MathF.Max(0f, ship.Systems.AmmoMissing - units);
        u.Events.Publish(new AmmoRearmed(ship.Id, default, units, cost, payerActor, treasury));
        if (treasury && cost > 0f) u.Events.Publish(new TreasuryUpkeep(payerActor, Purpose, cost, 0f));
        return units;
    }

    /// <summary>
    /// Phase 25: Munition als Fracht ins Magazin umladen (auch im Flug): höchstens was fehlt und was an Bord ist. Liefert die umgeladenen Einheiten
    /// (0 = nichts möglich; ändert dann nichts). Ereignis <see cref="AmmoTransferred"/>.
    /// </summary>
    public static float FromCargo(Universe u, Ship ship, float maxUnits = float.MaxValue)
    {
        if (AmmoGood(u) is not { } good) return 0f;
        float units = MathF.Min(MathF.Min(Missing(u, ship), ship.Cargo.GetValueOrDefault(good.Id)), MathF.Max(0f, maxUnits));
        if (units <= 0.001f) return 0f;
        float left = ship.Cargo[good.Id] - units;
        if (left <= 0.001f) ship.Cargo.Remove(good.Id); else ship.Cargo[good.Id] = left;
        ship.Systems.AmmoMissing = MathF.Max(0f, ship.Systems.AmmoMissing - units);
        u.Events.Publish(new AmmoTransferred(ship.Id, units));
        return units;
    }

    /// <summary>Warum nichts aus der Fracht umgeladen werden kann (null = es geht).</summary>
    public static string? FromCargoIssue(Universe u, Ship ship)
    {
        if (!u.Rules.AmmoActive || AmmoGood(u) is not { } good) return "Munition ist abgeschaltet.";
        if (ship.Stats.AmmoCapacity <= 0f) return "Kein Magazin an Bord.";
        if (Missing(u, ship) <= 0.001f) return "Magazin ist voll.";
        if (ship.Cargo.GetValueOrDefault(good.Id) <= 0.001f) return "Keine Munition im Frachtraum.";
        return null;
    }

    /// <summary>Polizei und Piraten an der Heimatbasis: erst aus deren Lager, der Rest ohne Lager — bezahlt aus der Kasse des Besitzers.</summary>
    public static void AtHome(Universe u, Ship ship, Station? home)
    {
        if (Missing(u, ship) <= 0f || !u.Actors.TryGetValue(ship.Owner, out var owner)) return;
        if (home is not null) AtStation(u, ship, home, owner.Treasury, owner.Id, true);
        AtBase(u, ship, owner.Treasury, owner.Id, true);
    }
}

/// <summary>
/// Phase 24: Händler und Schürfer mit Geschützen laden beim Andocken an einer Station mit Munitionslager nach (zahlen aus der eigenen Betriebskasse). Polizei
/// und Piraten laden an ihrer Heimatbasis (<see cref="PirateSystem.ServiceAtBase"/>); Spielerschiffe erst mit dem Dienst <c>rearm</c> (Phase 25).
/// </summary>
public sealed class RearmSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        if (!u.Rules.AmmoActive) return;
        foreach (var ship in u.ShipsById())
        {
            if (ship.Systems.AmmoMissing <= 0f || ship.Controller != Controller.Ai || ship.Transit is not null || ship.DockedAt.Value == 0) continue;
            if (ThreatMap.IsPolice(ship) || ThreatMap.IsRaider(ship) || u.Fights.IsEngaged(ship.Id)) continue;
            if (!u.Stations.TryGetValue(ship.DockedAt, out var st) || st.IsBuildSite) continue;
            Rearm.AtStation(u, ship, st, u.WalletOf(ship), ship.Owner, false);
        }
    }
}
