using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 17 — zwei Banditenarten (docs/BANDITEN.md). Reine Regeln über die etablierten Banditen: eine gesetzlose Fraktion
/// (ActorKind.Outlaw **mit** Faction-Eintrag) mit Sitz (Versteck), Wirtschaftsstationen, Schutz- und Kaperflotte, Zoll und Gebiet.
/// Die heutigen kleinen Piratengruppen haben keinen Faction-Eintrag — für sie ist <see cref="IsEstablished(Universe, ActorId)"/>
/// false und jeder neue Pfad bleibt wirkungslos (die NPC-Welt ohne solche Fraktion rechnet bitgleich wie vorher).
/// </summary>
public static class Bandits
{
    /// <summary>Etablierte Banditen: gesetzlos UND Fraktion (die kleinen Gruppen haben keinen Faction-Eintrag).</summary>
    public static bool IsEstablished(Universe u, ActorId id) =>
        u.Actors.TryGetValue(id, out var a) && a.Kind == ActorKind.Outlaw && u.Factions.ContainsKey(new FactionId(id.Value));

    /// <summary>Wie oben, mit Fraktions-Id.</summary>
    public static bool IsEstablished(Universe u, FactionId f) => IsEstablished(u, new ActorId(f.Value));

    /// <summary>Erstes eigenes Versteck des Akteurs (Id-Reihenfolge) — der Sitz; null = keiner.</summary>
    public static Station? Seat(Universe u, ActorId id) =>
        u.StationsById().FirstOrDefault(st => st.Owner == id && StationProtection.IsHideout(u, st));

    /// <summary>Eigene fertige Stationen mit Rezepten (die Werke), ohne Versteck (Id-Reihenfolge).</summary>
    public static List<Station> Economic(Universe u, ActorId id) =>
        u.StationsById().Where(st => st.Owner == id && !st.IsBuildSite && !StationProtection.IsHideout(u, st) && st.Production.Count > 0).ToList();

    /// <summary>Gehört der Sektor einer etablierten Banditenfraktion?</summary>
    public static bool HoldsSector(Universe u, Sector s) => IsEstablished(u, s.Owner);

    /// <summary>Kapazitätseinheiten der Kaperflotte: für etablierte Banditen <see cref="EconomyRules.BanditMaxRaiderUnits"/>, sonst <see cref="EconomyRules.MaxRaiderUnitsPerActor"/>.</summary>
    public static float RaiderUnitsCap(Universe u, Actor a) =>
        IsEstablished(u, a.Id) ? u.Rules.BanditMaxRaiderUnits : u.Rules.MaxRaiderUnitsPerActor;

    /// <summary>Deckel der Hehlerei: für etablierte Banditen <see cref="EconomyRules.BanditFenceCap"/>, sonst <see cref="EconomyRules.PirateTreasuryCap"/> (0 = ungedeckelt).</summary>
    public static float FenceCap(Universe u, Actor? a) =>
        a is not null && IsEstablished(u, a.Id) ? u.Rules.BanditFenceCap : u.Rules.PirateTreasuryCap;

    /// <summary>
    /// Warum der Akteur jetzt keine Wirtschaftsstation vom Typ <paramref name="t"/> nachbauen darf (null = er darf): Abklingzeit
    /// (<see cref="Actor.LastBanditTick"/>), Kasse (Baukosten × <see cref="EconomyRules.BanditRebuildMoneyFactor"/>) und freier Platz
    /// im Sitzsektor. Rein.
    /// </summary>
    public static string? BuildIssue(Universe u, Actor a, StationType t)
    {
        if (u.Tick - a.LastBanditTick < u.Rules.BanditRebuildCooldownTicks) return "Abklingzeit läuft";
        if (a.Treasury.Credits < t.BuildCost * u.Rules.BanditRebuildMoneyFactor) return "Kasse reicht nicht";
        if (Seat(u, a.Id) is not { } seat || !u.Sectors.TryGetValue(seat.Sector, out var sec)) return "kein Sitz";
        if (Spot(u, sec, t.Radius, seat.Position) is null) return "kein freier Platz";
        return null;
    }

    /// <summary>Freier Bauplatz im Sektor um <paramref name="anchor"/> (Sitzposition), sonst Sektormitte (Outposts.FreeSpot, kein Zufall); null = keiner.</summary>
    public static Vec2? Spot(Universe u, Sector s, float radius, Vec2? anchor = null) =>
        Outposts.FreeSpot(u, s, anchor ?? new Vec2(0f, 0f), radius, radius + 18f, 70f);

    /// <summary>Anzeigezeile einer etablierten Banditenfraktion: „Sitz Freistatt · N Werke · Zoll X · Kasse …“.</summary>
    public static string Text(Universe u, ActorId id)
    {
        var a = u.Actors[id];
        var seat = Seat(u, id);
        int eco = Economic(u, id).Count;
        int toll = 0;
        foreach (var s in u.Sectors.Values.OrderBy(x => x.Id.Value))
            if (s.Owner.Value == id.Value) toll = Math.Max(toll, s.Access.EntryLevel);
        // kurz (Typname statt Stationsname — der trägt den Fraktionsnamen ohnehin): passt in die Fraktionszeile und die Stations-Info
        string seatName = seat is null ? "kein Sitz" : "Sitz " + (u.StationTypes.TryGetValue(seat.TypeKey, out var st) ? st.Name : seat.Name)
            + (seat.Condition is { Disabled: true } ? " (aus)" : "");
        return $"{seatName} · {eco} Werk{(eco == 1 ? "" : "e")} · Zoll {toll} · Kasse {a.Treasury.Credits:0} Cr";
    }
}
