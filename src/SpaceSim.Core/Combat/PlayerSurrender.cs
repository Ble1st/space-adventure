using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Phase 27 (Lücke aus Phase 25, KAMPF.md Stufe 27): Der Spieler kann sich ergeben — vor und im Gefecht.
/// <list type="bullet">
/// <item>Gegenüber der Polizei (Vorwarnung der Fahndung oder Polizeigefecht): Schmuggelware wird beschlagnahmt, die Strafe aus der Kasse eingezogen
/// (<see cref="SecuritySystem.InterceptPlayer(Universe, Ship, Sector, FactionId)"/>); das Gefecht endet.</item>
/// <item>Gegenüber Kaperern (Überfallwarnung oder Überfallgefecht): Der Spieler wirft <see cref="EconomyRules.PlayerSurrenderDropShare"/> jeder Ware ab, die
/// Kaperer nehmen sie (Hehlerei in ihre Kasse, gedeckelt wie jede Beute) und drehen ab.</item>
/// </list>
/// Ohne <see cref="EconomyRules.FleetCombat"/> gibt es kein Ergeben (Verhalten bis Phase 26). Keine Zufallszahl.
/// </summary>
public static class PlayerSurrender
{
    /// <summary>Warum der Spieler sich jetzt nicht ergeben kann (null = er kann; die Ansicht zeigt dann die Taste „Ergeben“).</summary>
    public static string? Issue(Universe u)
    {
        if (!u.Rules.FleetCombat) return "Ergeben gibt es erst mit dem Flottenkampf.";
        if (!u.Ships.TryGetValue(u.PlayerShip, out var me)) return "Kein Schiff.";
        if (u.Fights.Of(me.Id) is { } e && e.Of(me.Id) is { Active: true } p)
        {
            if (Enemy(u, e, p, police: true) is not null) return null;
            if (Enemy(u, e, p, police: false) is not null) return ThreatMap.CargoValue(u, me) > 0f ? null : "Keine Fracht zum Abwerfen.";
            return "Dieser Gegner nimmt keine Aufgabe an.";
        }
        var pending = u.PendingEncounters.FirstOrDefault(x => x.Target == me.Id);
        if (pending is null) return "Niemand verlangt deine Aufgabe.";
        if (pending.Kind == EncounterKind.Intercept) return null;
        return ThreatMap.CargoValue(u, me) > 0f ? null : "Keine Fracht zum Abwerfen.";
    }

    /// <summary>
    /// Gerätetest Phase 28: Ist Ergeben hier ein Thema (Gefecht mit Gegnern oder angekündigte Begegnung)? Dann zeigt die Ansicht die Taste — wenn
    /// <see cref="Issue"/> einen Grund nennt (z. B. „Keine Fracht zum Abwerfen.“), deaktiviert mit diesem Grund.
    /// </summary>
    public static bool Relevant(Universe u)
    {
        if (!u.Rules.FleetCombat || !u.Ships.TryGetValue(u.PlayerShip, out var me)) return false;
        if (u.Fights.Of(me.Id) is { } e && e.Of(me.Id) is { Active: true } p && CombatSense.Enemies(u, e, p).Any()) return true;
        return u.PendingEncounters.Any(x => x.Target == me.Id);
    }

    static CombatParticipant? Enemy(Universe u, Encounter e, CombatParticipant me, bool police)
    {
        foreach (var q in CombatSense.Enemies(u, e, me))
            if (police ? ThreatMap.IsPolice(u.Ships[q.Ship]) : q.Tactic == CombatTactics.Raider) return q;
        return null;
    }

    /// <summary>Ergibt sich (siehe Klassenbeschreibung). null = nicht möglich (Grund über <see cref="Issue"/>); sonst das veröffentlichte Ereignis.</summary>
    public static PlayerSurrendered? Surrender(Universe u)
    {
        if (Issue(u) is not null) return null;
        var me = u.Ships[u.PlayerShip];
        PlayerSurrendered ev;
        if (u.Fights.Of(me.Id) is { } e && e.Of(me.Id) is { Active: true } p)
        {
            var sector = u.Sectors[e.Sector];
            if (Enemy(u, e, p, police: true) is { } cop)
            {
                var f = u.FactionOf(u.Ships[cop.Ship]) ?? sector.Owner;
                var res = SecuritySystem.InterceptPlayer(u, me, sector, f, u.Rules.SurrenderFineAsDebt);
                Engage.Leave(u, e, p, CombatExit.Surrendered);
                CombatSystem.Finish(u, e, "surrendered");
                ev = new PlayerSurrendered(true, sector.Id, res.FineCollected, "Ergeben: " + res.Message);
            }
            else
            {
                float value = ThreatMap.CargoValue(u, me);
                p.DropShare = Math.Clamp(u.Rules.PlayerSurrenderDropShare, 0f, 1f);
                CombatSystem.Surrender(u, e, p);
                value -= ThreatMap.CargoValue(u, me);
                CombatSystem.Finish(u, e, "surrendered");
                ev = new PlayerSurrendered(false, sector.Id, value, $"Ergeben: Fracht im Wert von {value:0} Cr abgeworfen — die Piraten drehen ab.");
            }
        }
        else
        {
            var pending = u.PendingEncounters.First(x => x.Target == me.Id);
            u.PendingEncounters.Remove(pending);
            var sector = u.Sectors[pending.Sector];
            if (pending.Kind == EncounterKind.Intercept)
            {
                var res = SecuritySystem.InterceptPlayer(u, me, sector, sector.Owner, u.Rules.SurrenderFineAsDebt);
                SecuritySystem.ArrestCooldown(u, me.Owner);
                ev = new PlayerSurrendered(true, sector.Id, res.FineCollected, "Gestellt: " + res.Message);
            }
            else
            {
                float value = DropToRaider(u, me, u.Ships.GetValueOrDefault(pending.Source));
                if (u.Ships.TryGetValue(pending.Source, out var raider)) PirateCommands.AfterFight(u, raider);
                ev = new PlayerSurrendered(false, sector.Id, value, $"Fracht im Wert von {value:0} Cr abgeworfen — die Piraten drehen ab.");
            }
        }
        u.Events.Publish(ev);
        return ev;
    }

    /// <summary>Wirft den Abgabeanteil jeder Ware ab; die Kasse des Kaperers bekommt die Hehlerei (gedeckelt). Liefert den Wert der Beute (Basispreis).</summary>
    static float DropToRaider(Universe u, Ship me, Ship? raider)
    {
        float share = Math.Clamp(u.Rules.PlayerSurrenderDropShare, 0f, 1f), value = 0f;
        foreach (var g in me.Cargo.Keys.OrderBy(k => k.Value).ToList())
        {
            float take = me.Cargo[g] * share;
            if (take <= 0f) continue;
            float left = me.Cargo[g] - take;
            if (left <= 0.001f) me.Cargo.Remove(g); else me.Cargo[g] = left;
            value += take * u.Goods[g].BasePrice;
        }
        if (raider is not null && u.Actors.TryGetValue(raider.Owner, out var pirates))
        {
            float fenced = value * u.Rules.FenceShare;
            if (u.Rules.PirateTreasuryCap > 0f) fenced = MathF.Min(fenced, MathF.Max(0f, u.Rules.PirateTreasuryCap - pirates.Treasury.Credits));
            if (fenced > 0f)
            {
                pirates.Treasury.Credits += fenced;
                u.Events.Publish(new TreasuryIncome(pirates.Id, "fence", fenced));
            }
        }
        return value;
    }
}
