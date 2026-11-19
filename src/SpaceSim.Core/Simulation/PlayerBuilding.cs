using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Ein Eintrag des Baufensters: Typ, Bausumme (Treuhandgeld), Stückliste und — falls der Bau hier nicht geht — der Grund.</summary>
public sealed record BuildOption(StationType Type, float Cost, IReadOnlyDictionary<GoodId, float> Bill, string? Issue);

/// <summary>
/// Eigene Stationen des Spielers (docs/STATIONEN.md „Eigene Stationen bauen“, Regel <see cref="EconomyRules.PlayerBuilding"/>). Der Spieler wählt im Baufenster
/// einen Wirtschaftstyp, setzt ihn im Flug als Geist an einen freien Platz (<see cref="StationPlacement"/> — dieselbe Platzregel wie der KI-Bau) und legt damit
/// über den gewöhnlichen Weg (<see cref="ConstructionRules.StartSite"/>) eine Baustelle an, die ihm gehört: Treuhandgeld = Baukosten ×
/// <see cref="EconomyRules.StationBuildCostFactor"/> aus der Spielerkasse; Material bringen eigene Bauschiffe mit Befehl „Bauen“ (aus dem Treuhandgeld bezahlt)
/// oder Händler über den normalen Handel; die Montage braucht ein eigenes Bauschiff vor Ort (KI-Bauschiffe übernehmen Spielerbaustellen nicht, es gibt keinen
/// Notbau und keinen Verfall). Die fertige Station gehört dem Spieler, beginnt ohne Startkapital (kein neues Geld) und zählt nicht gegen die Obergrenzen der
/// KI (<see cref="StationProtection.CountEconomic(Universe)"/>), sondern gegen eigene (<see cref="EconomyRules.PlayerMaxStations"/>, je Sektor, Baustellen; 0 = unbegrenzt — Standardwelt ohne Baulimit).
/// </summary>
public static class PlayerBuilding
{
    /// <summary>Gehört die Station dem Spieler und ist sie eine Wirtschaftsstation (keine Regierung/Wache/Versteck; Baustellen zählen mit)?</summary>
    public static bool IsPlayerEconomic(Universe u, Station st) => st.Owner == u.PlayerActor && StationProtection.IsEconomic(u, st);

    /// <summary>Spielerstation, die die Sonderregeln des Spielerbaus trifft (Regel an).</summary>
    public static bool Applies(Universe u, Station st) => u.Rules.PlayerBuilding && st.Owner == u.PlayerActor;

    /// <summary>Darf der Spieler diesen Typ überhaupt bauen (Wirtschaftstyp mit auflösbarer Stückliste; keine Regierung, kein Versteck, keine Forschungsstation)?</summary>
    public static bool IsBuildableKind(Universe u, StationType t) =>
        t.Government == GovernmentKind.None && !t.Hideout && t.Research is null && t.Dockable
        && t.Key != u.Rules.ConstructionSiteType && !FeatureSwitches.OmitFromWorld(u, t) && ConstructionRules.UsesSites(u, t);

    /// <summary>Alle Typen des Baufensters (Baukosten, dann Schlüssel).</summary>
    public static List<StationType> Catalog(Universe u) =>
        u.StationTypes.Values.Where(t => IsBuildableKind(u, t)).OrderBy(t => t.BuildCost).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();

    /// <summary>Bausumme (Treuhandgeld) = Baukosten des Typs (schon mit dem Größenfaktor) × <see cref="EconomyRules.StationBuildCostFactor"/>.</summary>
    public static float Cost(Universe u, StationType t) => t.BuildCost * u.Rules.StationBuildCostFactor;

    /// <summary>Eigene Wirtschaftsstationen und laufende eigene Baustellen (Id-Reihenfolge).</summary>
    public static List<Station> OwnEconomic(Universe u) =>
        u.StationsById().Where(s => IsPlayerEconomic(u, s) && s.Site is not { Abandoned: true }).ToList();

    /// <summary>Laufende eigene Baustellen.</summary>
    public static List<Station> OwnSites(Universe u) => u.StationsById().Where(s => s.Owner == u.PlayerActor && s.Site is { Abandoned: false }).ToList();

    /// <summary>Warum der Spieler in diesem Sektor nicht bauen darf (null = er darf): eigener oder herrenloser Sektor ohne Piratenversteck; mit Regel
    /// <see cref="EconomyRules.PlayerBuildForeignSectors"/> auch der Sektor einer KI-Fraktion, die ihn lässt (<see cref="ForeignIssue"/>).</summary>
    public static string? SectorIssue(Universe u, SectorId sector)
    {
        if (!u.Sectors.TryGetValue(sector, out var sec)) return "Unbekannter Sektor.";
        if (u.IsNpcFaction(sec.Owner))
        {
            if (!u.Rules.PlayerBuildForeignSectors) return $"{sec.Name} gehört {u.Factions[sec.Owner].Name} — nur eigene oder herrenlose Sektoren.";
            if (ForeignIssue(u, sec) is { } fi) return fi;
        }
        // Nutzerentscheidung „auch in Sektoren mit Piratenversteck“ (Regel PlayerBuildHideoutSectors): die Station lebt dort mit Überfällen; Abstand zum
        // Versteck regelt die gemeinsame Platzregel wie zu jeder Station
        if (!u.Rules.PlayerBuildHideoutSectors && sec.Stations.Any(id => u.Stations.TryGetValue(id, out var st) && StationProtection.IsHideout(u, st))) return "Piratenversteck im Sektor.";
        return null;
    }

    /// <summary>
    /// Nutzerentscheidung „Bau auch möglich in Sektoren anderer Fraktionen“ (Regel <see cref="EconomyRules.PlayerBuildForeignSectors"/>): warum der Spieler im
    /// Sektor einer KI-Fraktion nicht bauen darf (null = er darf) — Krieg, feindliche Beziehung oder eine durchgesetzte Sperre, die ihn ausschließt
    /// (Stufe ≥ 4 schließt alle Fremden aus, Stufe ≥ 1 gegen ihn gerichtet ebenso). Rein.
    /// </summary>
    public static string? ForeignIssue(Universe u, Sector sec)
    {
        var owner = sec.Owner;
        string name = u.Factions.TryGetValue(owner, out var fa) ? fa.Name : "die Fraktion";
        if (u.PlayerFaction is { } me && Wars.AtWar(u, owner, me)) return $"Krieg mit {name} — kein Bau in {sec.Name}.";
        if (Relations.Effective(u, (ActorId)owner, u.PlayerActor) <= u.Rules.HostileRelation) return $"{name} ist dir feindlich gesinnt — kein Bau in {sec.Name}.";
        var acc = sec.Access;
        if (acc.Enforced && (acc.EntryLevel >= 4 || (acc.EntryLevel >= 1 && u.PlayerFaction is { } pf && acc.Against == pf)))
            return $"{name} sperrt {sec.Name} gegen dich (Stufe {acc.EntryLevel}) — kein Bau.";
        return null;
    }

    /// <summary>
    /// Warum der Spieler den Typ <paramref name="t"/> jetzt nicht im Sektor des aktiven Schiffs bauen kann (null = er kann; der Platz wird beim Platzieren
    /// geprüft). Rein.
    /// </summary>
    public static string? TypeIssue(Universe u, StationType t)
    {
        var r = u.Rules;
        if (!r.PlayerBuilding) return "Eigene Stationen sind abgeschaltet.";
        if (u.PlayerFaction is not { } me) return "Du bist keine eigene Fraktion.";
        if (!IsBuildableKind(u, t)) return "Dieser Typ ist nicht baubar.";
        if (!Research.IsUnlocked(u, me, UnlockKind.StationType, t.Key)) return $"Nicht erforscht ({u.Research.LockOf(UnlockKind.StationType, t.Key)?.Name ?? t.Key}).";
        if (!u.Ships.TryGetValue(u.PlayerShip, out var ship) || ship.Transit is not null) return "Kein aktives Schiff im Raum.";
        if (ship.DockedAt.Value != 0) return "Erst abdocken — der Bauplatz wird im Flug gewählt.";
        if (SectorIssue(u, ship.Sector) is { } si) return si;
        var sec = u.Sectors[ship.Sector];
        if (t.Export is not null && sec.Planet is null) return "Exportstation nur in einem Sektor mit Planet.";
        // Nutzerentscheidung „Spieler soll kein Baulimit haben“: 0 = unbegrenzt (Standardwelt); Platzregel, Sektor und Kasse gelten weiter
        if (r.PlayerMaxSites > 0 && OwnSites(u).Count >= r.PlayerMaxSites) return $"Höchstens {r.PlayerMaxSites} eigene Baustellen gleichzeitig.";
        if (r.PlayerMaxStations > 0 || r.PlayerMaxStationsPerSector > 0)
        {
            var own = OwnEconomic(u);
            if (r.PlayerMaxStations > 0 && own.Count >= r.PlayerMaxStations) return $"Höchstens {r.PlayerMaxStations} eigene Stationen.";
            if (r.PlayerMaxStationsPerSector > 0 && own.Count(s => s.Sector == ship.Sector) >= r.PlayerMaxStationsPerSector) return $"Höchstens {r.PlayerMaxStationsPerSector} eigene Stationen je Sektor.";
        }
        float cost = Cost(u, t);
        if (u.Player.Treasury.Credits < cost) return $"Kasse reicht nicht ({cost:0} Cr nötig).";
        return null;
    }

    /// <summary>Alle Einträge des Baufensters mit Kosten, Stückliste und Grund.</summary>
    public static List<BuildOption> Options(Universe u) =>
        Catalog(u).Select(t => new BuildOption(t, Cost(u, t), ConstructionRules.Bill(u, t) ?? new Dictionary<GoodId, float>(), TypeIssue(u, t))).ToList();

    /// <summary>Abstand des Geists zum aktiven Schiff (Schiffskörper + Rand), damit das Schiff nicht in der Baustelle steckt.</summary>
    public const float ShipClearance = 10f;

    /// <summary>
    /// Die Prüfung des Geists: erst die Typ-/Sektorbedingungen (<see cref="TypeIssue"/>), dann die gemeinsame Platzregel (<see cref="StationPlacement.Check"/>),
    /// zuletzt der Abstand zum eigenen Schiff. null = gültig. Rein.
    /// </summary>
    public static string? PlaceIssue(Universe u, StationType t, Vec2 pos)
    {
        if (TypeIssue(u, t) is { } ti) return ti;
        var ship = u.Ships[u.PlayerShip];
        var check = StationPlacement.Check(u, u.Sectors[ship.Sector], pos, t.Radius);
        if (!check.Ok) return check.Reason;
        if (Vec2.Distance(pos, ship.Position) < t.Radius + ShipClearance) return "zu nah an deinem Schiff";
        return null;
    }

    /// <summary>Drehung auf [0, 360) Grad (rein optisch).</summary>
    public static float NormalizeRotation(float degrees)
    {
        float r = degrees % 360f;
        if (r < 0f) r += 360f;
        return r >= 360f ? 0f : r;
    }

    /// <summary>
    /// Legt die eigene Baustelle an (ohne Prüfung; <see cref="PlaceIssue"/> vorher): Bausumme aus der Spielerkasse als Treuhandgeld, gewöhnliche Baustelle des
    /// Typs (keine Übernahme einer verlassenen Baustelle — der Platz gilt), Besitzer und Investor der Spielerakteur, Drehung am Platz.
    /// </summary>
    public static Station Start(Universe u, StationType t, Vec2 pos, float rotation)
    {
        var ship = u.Ships[u.PlayerShip];
        float cost = Cost(u, t);
        u.Player.Treasury.Credits -= cost;
        var site = ConstructionRules.StartSite(u, t, ship.Sector, pos, u.PlayerActor, cost, null, null, u.PlayerActor, reuseAbandoned: false);
        site.Rotation = NormalizeRotation(rotation);
        u.Events.Publish(new PlayerSiteStarted(site.Id, site.Sector, t.Key, cost));
        return site;
    }

    /// <summary>Warum die eigene Baustelle nicht aufgegeben werden kann (null = sie kann).</summary>
    public static string? CancelIssue(Universe u, StationId id) =>
        !u.Stations.TryGetValue(id, out var st) || st.Owner != u.PlayerActor || st.Site is not { Abandoned: false } ? "Keine eigene Baustelle." : null;

    /// <summary>Gibt die eigene Baustelle auf: das restliche Treuhandgeld zurück in die Spielerkasse, die Baustelle verschwindet (geliefertes Material verfällt).</summary>
    public static float Cancel(Universe u, StationId id)
    {
        var st = u.Stations[id];
        float refund = MathF.Max(0f, st.Credits);
        st.Credits = 0f;
        u.Player.Treasury.Credits += refund;
        foreach (var s in u.ShipsById()) if (st.Site!.Builder == s.Id) st.Site.Builder = default;
        Departures.RemoveStation(u, st, DepartureReason.Sold, "cancelled");
        u.Events.Publish(new PlayerSiteCancelled(id, refund));
        return refund;
    }

    /// <summary>
    /// Warum zwischen Spielerkasse und der Kasse der eigenen Station <paramref name="id"/> nicht <paramref name="amount"/> Cr umgebucht werden können
    /// (positiv = einzahlen, negativ = auszahlen; null = es geht). Fertige eigene Stationen; an einer Baustelle nur Einzahlen (Nachschuss ins Treuhandgeld — Abheben bleibt gebunden).
    /// </summary>
    public static string? TransferIssue(Universe u, StationId id, float amount)
    {
        if (!u.Stations.TryGetValue(id, out var st) || st.Owner != u.PlayerActor) return "Keine eigene Station.";
        if (st.IsBuildSite && amount <= 0f) return "Treuhandgeld einer Baustelle ist gebunden (nur einzahlen).";   // Bot-Fund: ein aufgebrauchtes Treuhandgeld ließ die Baustelle für immer stehen — Nachschießen geht, Abheben nicht
        if (amount == 0f || float.IsNaN(amount)) return "Kein Betrag.";
        if (amount > 0f && u.Player.Treasury.Credits < amount) return "Kasse reicht nicht.";
        if (amount < 0f && st.Credits < -amount) return "Stationskasse reicht nicht.";
        return null;
    }

    /// <summary>Bucht um (ohne Prüfung; <see cref="TransferIssue"/> vorher): reine Umbuchung, kein neues Geld; die Rentabilitätsmessung bleibt neutral.</summary>
    public static void Transfer(Universe u, StationId id, float amount)
    {
        var st = u.Stations[id];
        u.Player.Treasury.Credits -= amount;
        st.Credits += amount;
        Profitability.Transfer(st.Profit, amount);
        u.Events.Publish(new StationCashMoved(id, amount));
    }

    /// <summary>
    /// Grundeinkommen einer eigenen Wirtschaftsstation je Tick: höchstens ihr Unterhalt (es deckt den Betrieb, schafft aber kein Geld ohne Ware — sonst wäre jede
    /// Spielerstation eine Geldquelle zum Abschöpfen). KI-Stationen unverändert.
    /// </summary>
    public static float IncomePerTick(Universe u, Station st, StationType type) =>
        Applies(u, st) ? MathF.Min(type.IncomePerTick, type.UpkeepPerTick) : type.IncomePerTick;

    /// <summary>Rezeptzeile für die Anzeige: „2 Eisenerz + 1 Energie → 2 Stahl / 5 Ticks“.</summary>
    public static string RecipeText(Universe u, Recipe r)
    {
        string Side(Dictionary<GoodId, float> d) => d.Count == 0 ? "—" : string.Join(" + ", d.OrderBy(kv => kv.Key.Value).Select(kv => $"{kv.Value:0.##} {u.Goods[kv.Key].Name}"));
        return $"{Side(r.Inputs)} → {Side(r.Outputs)} / {r.TicksPerCycle} Ticks";
    }

    /// <summary>Stückliste als Text („40 Stahl, 12 Rumpfplatten“).</summary>
    public static string BillText(Universe u, IReadOnlyDictionary<GoodId, float> bill) =>
        bill.Count == 0 ? "—" : string.Join(", ", bill.OrderBy(kv => kv.Key.Value).Select(kv => $"{kv.Value:0} {u.Goods[kv.Key].Name}"));
}
