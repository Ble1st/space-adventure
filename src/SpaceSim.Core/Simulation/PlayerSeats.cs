using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Runde 45 (docs/POLITIK.md „Runde 45“): der kleinste ehrliche Weg der Spielerfraktion zu eigenen Sperren. Die Spielerfraktion besitzt anfangs keinen Sektor;
/// sie darf in einem <b>herrenlosen</b> Sektor ohne Piratenversteck, in dem ihr aktives Schiff steht, eine <b>Sektorbehörde</b> gründen
/// (<see cref="EconomyRules.PlayerSeatType"/>, fertig gekauft wie eine Kleinstation, Kosten = Baukosten aus der Spielerkasse, Senke). Die Behörde gibt
/// Einfluss über <see cref="SectorControl"/> (Besitz, Stufe Dominanz → Sperrstufe 1–2 nach <see cref="EconomyRules.StageMaxLevel"/>); Stufe 3–5 bräuchte
/// Patrouillenabdeckung, die der Spieler ohne eigene Polizei nicht hat. Danach setzt er die Stufe über <see cref="FactionPolicySystem.SetLevel"/> — dieselbe
/// Regel wie die KI: Deckel nach Kontrollstufe, Mindestdauer <see cref="EconomyRules.LockMinTicks"/>, Ablauf <see cref="EconomyRules.LockMaxTicks"/>, Abkühlzeit
/// <see cref="EconomyRules.LockCooldownTicks"/>, Durchsetzung <see cref="EconomyRules.LockUpkeepPerLevel"/> je Stufe und Tick aus der Kasse (sonst Papier),
/// Zoll in die Spielerkasse, Groll der KI-Fraktionen wie bei KI-Sperren (<see cref="FactionPolicySystem.StepRelations"/>). Die Behörde zahlt ihren Unterhalt aus
/// der Spielerkasse (Regierungsunterhalt) und kann wieder geschlossen werden.
/// </summary>
public static class PlayerSeats
{
    public static StationType? SeatType(Universe u) => u.StationTypes.TryGetValue(u.Rules.PlayerSeatType, out var t) ? t : null;

    /// <summary>Gründungskosten (Baukosten des Typs × <see cref="EconomyRules.StationBuildCostFactor"/>).</summary>
    public static float Cost(Universe u, StationType t) => t.BuildCost * u.Rules.StationBuildCostFactor;

    /// <summary>Sektorbehörden der Spielerfraktion (Id-Reihenfolge).</summary>
    public static List<Station> Seats(Universe u) =>
        u.StationsById().Where(s => !s.IsBuildSite && s.Owner == u.PlayerActor && s.TypeKey == u.Rules.PlayerSeatType).ToList();

    /// <summary>Sektoren der Spielerfraktion (Id-Reihenfolge).</summary>
    public static List<Sector> OwnSectors(Universe u) =>
        u.PlayerFaction is { } me ? u.Sectors.Values.Where(s => s.Owner == me).OrderBy(s => s.Id.Value).ToList() : new List<Sector>();

    /// <summary>Warum die Spielerfraktion in <paramref name="sector"/> jetzt keine Sektorbehörde gründen kann (null = sie kann). Rein.</summary>
    public static string? FoundIssue(Universe u, SectorId sector)
    {
        var r = u.Rules;
        if (!r.PlayerSeats) return "Eigene Sektorbehörden sind abgeschaltet.";
        if (u.PlayerFaction is null) return "Du bist keine eigene Fraktion.";
        if (SeatType(u) is not { } t) return "Kein Behördentyp in dieser Welt.";
        if (!u.Sectors.TryGetValue(sector, out var sec)) return "Unbekannter Sektor.";
        if (!u.Ships.TryGetValue(u.PlayerShip, out var ship) || ship.Sector != sector || ship.Transit is not null) return "Dein aktives Schiff muss im Sektor sein.";
        if (u.IsNpcFaction(sec.Owner)) return $"{sec.Name} gehört {u.Factions[sec.Owner].Name} — nur herrenlose Sektoren.";
        if (Seats(u).Any(s => s.Sector == sector)) return "Hier hast du schon eine Sektorbehörde.";
        if (sec.Stations.Any(id => u.Stations.TryGetValue(id, out var st) && StationProtection.IsHideout(u, st))) return "Piratenversteck im Sektor — keine Behörde möglich.";
        var cls = ActorRules.ClassOf(u, ActorRules.Index(u), u.PlayerActor);
        if ((int)cls < r.PlayerSeatMinClass) return $"Erst ab Größenklasse {ActorNames.Size((ActorSizeClass)r.PlayerSeatMinClass)} (du: {ActorNames.Size(cls)}).";
        float cost = Cost(u, t);
        if (u.Player.Treasury.Credits < cost) return $"Kasse reicht nicht ({cost:0} Cr nötig).";
        if (Outposts.FreeSpot(u, sec, ship.Position, t.Radius, t.Radius + 18f, 90f) is null) return "Kein freier Platz in der Nähe des Schiffs.";
        return null;
    }

    /// <summary>Gründet die Sektorbehörde (ohne Prüfung; <see cref="FoundIssue"/> vorher) und rechnet die Sektorkontrolle sofort neu.</summary>
    public static Station Found(Universe u, SectorId sector)
    {
        var t = SeatType(u)!;
        var sec = u.Sectors[sector];
        var ship = u.Ships[u.PlayerShip];
        var spot = Outposts.FreeSpot(u, sec, ship.Position, t.Radius, t.Radius + 18f, 90f)!.Value;
        float cost = Cost(u, t);
        u.Player.Treasury.Credits -= cost;
        var id = u.NewStationId();
        var st = StationFactory.Create(u, id, t, StationFactory.BuiltName(u, t, sector), sector, spot, u.PlayerActor);
        st.Credits = 0f;
        u.AddStation(st);
        u.Events.Publish(new StationBuilt(id, sector, t.Key));
        u.Events.Publish(new PlayerSeatFounded(id, sector, cost));
        if (SectorControl.Update(u, sec)) u.TouchAccess();
        return st;
    }

    /// <summary>Warum die Behörde <paramref name="id"/> nicht geschlossen werden kann (null = sie kann).</summary>
    public static string? CloseIssue(Universe u, StationId id)
    {
        if (!u.Stations.TryGetValue(id, out var st) || st.Owner != u.PlayerActor || st.TypeKey != u.Rules.PlayerSeatType) return "Keine eigene Sektorbehörde.";
        if (SpaceSim.Core.Combat.Siege.Of(u, id) is not null) return "Wird belagert.";
        return null;
    }

    /// <summary>Schließt die Behörde (Abgang „verkauft/geschlossen“, kein Erlös); Sektorkontrolle sofort neu — die Stufe fällt mit der Kontrolle.</summary>
    public static void Close(Universe u, StationId id)
    {
        var st = u.Stations[id];
        var sec = u.Sectors[st.Sector];
        Departures.RemoveStation(u, st, DepartureReason.Sold, "closed");
        if (SectorControl.Update(u, sec)) u.TouchAccess();
        if (sec.Access.EntryLevel > SectorControl.MaxLevel(u, sec)) FactionPolicySystem.SetLevel(u, sec, SectorControl.MaxLevel(u, sec));
    }

    /// <summary>
    /// Warum die Spielerfraktion in <paramref name="sector"/> jetzt nicht Stufe <paramref name="level"/> setzen kann (null = sie kann). Rein. Dieselben Regeln wie die
    /// KI-Politik: eigener Sektor, Deckel nach Kontrollstufe (<see cref="SectorControl.MaxLevel"/>), Mindestdauer zwischen Änderungen, nach Ablauf/Aufheben
    /// Abkühlzeit vor einer neuen Sperre.
    /// </summary>
    public static string? LockIssue(Universe u, SectorId sector, int level)
    {
        var r = u.Rules;
        if (!r.PlayerSeats) return "Eigene Sperren sind abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (!u.Sectors.TryGetValue(sector, out var sec)) return "Unbekannter Sektor.";
        if (sec.Owner != me) return "Nicht dein Sektor — erst eine Sektorbehörde in einem herrenlosen Sektor gründen.";
        if (level < 0 || level > 5) return "Stufe 0 bis 5.";
        var acc = sec.Access;
        if (level == acc.EntryLevel) return "Diese Stufe gilt schon.";
        int cap = SectorControl.MaxLevel(u, sec);
        if (level > cap)
            return cap == 0 ? $"Kontrollstufe {AccessNames.Stage(sec.Control)} erlaubt keine Sperre (Dominanz nötig)."
                : $"Kontrollstufe {AccessNames.Stage(sec.Control)} erlaubt höchstens Stufe {cap} (Stufe 3–5 brauchen Patrouillenabdeckung, z. B. ein eigenes Hauptquartier).";
        if (acc.EntryLevel == 0 && u.Tick < acc.CooldownUntil) return $"Abkühlzeit nach der letzten Sperre (noch {(acc.CooldownUntil - u.Tick) / 240 + 1} min).";
        if (acc.EntryLevel > 0 && u.Tick - acc.SinceTick < r.LockMinTicks) return $"Mindestdauer der Stufe (noch {(r.LockMinTicks - (u.Tick - acc.SinceTick)) / 240 + 1} min).";
        return null;
    }

    /// <summary>Setzt die Stufe (ohne Prüfung; <see cref="LockIssue"/> vorher) über <see cref="FactionPolicySystem.SetLevel"/>. Aufheben auf 0 startet die Abkühlzeit (kein Umgehen des Ablaufs).</summary>
    public static int SetLock(Universe u, SectorId sector, int level)
    {
        var sec = u.Sectors[sector];
        int set = FactionPolicySystem.SetLevel(u, sec, level);
        if (set == 0) sec.Access.CooldownUntil = u.Tick + u.Rules.LockCooldownTicks;
        return set;
    }

    /// <summary>Durchsetzungskosten je Minute bei Stufe <paramref name="level"/> (Anzeige).</summary>
    public static float UpkeepPerMinute(Universe u, int level) => u.Rules.LockUpkeepPerLevel * level * SimClock.TicksPerSecond * 60f;
}
