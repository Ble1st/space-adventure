using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Eigenes Hauptquartier der Spielerfraktion (docs/POLITIK.md „Eigenes Hauptquartier“). Die Spielerfraktion darf in einem <b>eigenen</b> Sektor (gehört schon
/// ihr, z. B. über eine Sektorbehörde, <see cref="PlayerSeats"/>), in dem ihr aktives Schiff steht, genau ein Hauptquartier (<see cref="EconomyRules.PlayerHqType"/>,
/// Größe XL, Einfluss 6, Sicherheit 1,5, Verteidigung und Geschütze des Typs, Dienste Gericht/Kopfgeld/Reparatur/Hangar/Taxi) gründen — fertig gekauft wie die
/// Sektorbehörde. Kosten = Baukosten × <see cref="EconomyRules.StationBuildCostFactor"/> aus der Spielerkasse (Senke), Unterhalt = Regierungsunterhalt aus der
/// Spielerkasse (mit <see cref="EconomyRules.PlayerGovernmentDebt"/> notfalls ins Minus). Ein übernommenes KI-Hauptquartier (Kapitulation) zählt als sein
/// Hauptquartier: solange er eines besitzt, gründet er keins. Wirkung: Einfluss über <see cref="SectorControl"/> und — die Sicherheit 1,5 ist Abdeckung —
/// Kontrollstufe „Kontrolle“, also Sperrstufe bis 5; Heimatbasis eigener bewaffneter Schiffe. Schließen über Comms ohne Erlös.
/// </summary>
public static class PlayerHq
{
    public static StationType? HqType(Universe u) => u.StationTypes.TryGetValue(u.Rules.PlayerHqType, out var t) ? t : null;

    /// <summary>Gründungskosten (Baukosten des Typs × <see cref="EconomyRules.StationBuildCostFactor"/>).</summary>
    public static float Cost(Universe u, StationType t) => t.BuildCost * u.Rules.StationBuildCostFactor;

    /// <summary>Ist die Station ein Hauptquartier (Regierungsart Hauptquartier, gleich welcher Typschlüssel)?</summary>
    public static bool IsHq(Universe u, Station st) =>
        !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Government == GovernmentKind.Headquarters;

    /// <summary>Das Hauptquartier des Spielers (gegründet oder übernommen), null = keins. Bei mehreren (zweite Kapitulation) das mit der kleinsten Id.</summary>
    public static Station? Current(Universe u) =>
        u.StationsById().FirstOrDefault(s => s.Owner == u.PlayerActor && IsHq(u, s));

    /// <summary>Alle Hauptquartiere des Spielers (Id-Reihenfolge; mehr als eins nur durch Kapitulationen).</summary>
    public static List<Station> All(Universe u) => u.StationsById().Where(s => s.Owner == u.PlayerActor && IsHq(u, s)).ToList();

    static Vec2? Spot(Universe u, Sector sec, Ship ship, StationType t) => Outposts.FreeSpot(u, sec, ship.Position, t.Radius, t.Radius + 18f, 150f);

    /// <summary>Warum die Spielerfraktion in <paramref name="sector"/> jetzt kein Hauptquartier gründen kann (null = sie kann). Rein.</summary>
    public static string? FoundIssue(Universe u, SectorId sector)
    {
        var r = u.Rules;
        if (!r.PlayerHq) return "Eigenes Hauptquartier ist abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (HqType(u) is not { } t) return "Kein Hauptquartier-Typ in dieser Welt.";
        if (!u.Sectors.TryGetValue(sector, out var sec)) return "Unbekannter Sektor.";
        if (Current(u) is { } hq) return $"Du hast schon ein Hauptquartier ({hq.Name}).";
        if (sec.Owner != me) return $"{sec.Name} gehört dir nicht — erst eine Sektorbehörde gründen.";
        if (!u.Ships.TryGetValue(u.PlayerShip, out var ship) || ship.Sector != sector || ship.Transit is not null) return "Dein aktives Schiff muss im Sektor sein.";
        var cls = ActorRules.ClassOf(u, ActorRules.Index(u), u.PlayerActor);
        if ((int)cls < r.PlayerHqMinClass) return $"Erst ab Größenklasse {ActorNames.Size((ActorSizeClass)r.PlayerHqMinClass)} (du: {ActorNames.Size(cls)}).";
        float cost = Cost(u, t);
        if (u.Player.Treasury.Credits < cost) return $"Kasse reicht nicht ({cost:0} Cr nötig).";
        if (Spot(u, sec, ship, t) is null) return "Kein freier Platz in der Nähe des Schiffs.";
        return null;
    }

    /// <summary>
    /// Gründet das Hauptquartier (ohne Prüfung; <see cref="FoundIssue"/> vorher): Kasse belastet, Station gebaut, eigene bewaffnete Schiffe ohne eigene Heimatbasis
    /// bekommen sie als Heimat, Sektorkontrolle sofort neu.
    /// </summary>
    public static Station Found(Universe u, SectorId sector)
    {
        var t = HqType(u)!;
        var sec = u.Sectors[sector];
        var ship = u.Ships[u.PlayerShip];
        var spot = Spot(u, sec, ship, t)!.Value;
        float cost = Cost(u, t);
        u.Player.Treasury.Credits -= cost;
        var id = u.NewStationId();
        var st = StationFactory.Create(u, id, t, StationFactory.BuiltName(u, t, sector), sector, spot, u.PlayerActor);
        st.Credits = 0f;
        u.AddStation(st);
        foreach (var s in u.ShipsById())
            if (s.Owner == u.PlayerActor && s.Has(Capability.Combat)
                && !(u.Stations.TryGetValue(s.HomeBase, out var home) && home.Owner == u.PlayerActor))
                s.HomeBase = id;
        u.Events.Publish(new StationBuilt(id, sector, t.Key));
        u.Events.Publish(new PlayerHqFounded(id, sector, cost));
        if (SectorControl.Update(u, sec)) u.TouchAccess();
        return st;
    }

    /// <summary>Warum das Hauptquartier <paramref name="id"/> nicht aufgegeben werden kann (null = es kann).</summary>
    public static string? CloseIssue(Universe u, StationId id)
    {
        if (!u.Stations.TryGetValue(id, out var st) || st.Owner != u.PlayerActor || !IsHq(u, st)) return "Kein eigenes Hauptquartier.";
        if (SpaceSim.Core.Combat.Siege.Of(u, id) is not null) return "Wird belagert.";
        return null;
    }

    /// <summary>Gibt das Hauptquartier auf (Abgang „geschlossen“, kein Erlös); Sektorkontrolle sofort neu, eine zu hohe Sperrstufe fällt auf den neuen Deckel.</summary>
    public static void Close(Universe u, StationId id)
    {
        var st = u.Stations[id];
        var sec = u.Sectors[st.Sector];
        Departures.RemoveStation(u, st, DepartureReason.Sold, "closed");
        if (SectorControl.Update(u, sec)) u.TouchAccess();
        if (sec.Access.EntryLevel > SectorControl.MaxLevel(u, sec)) FactionPolicySystem.SetLevel(u, sec, SectorControl.MaxLevel(u, sec));
    }

    /// <summary>Unterhalt je Minute (Anzeige).</summary>
    public static float UpkeepPerMinute(StationType t) => t.UpkeepPerTick * SimClock.TicksPerSecond * 60f;
}
