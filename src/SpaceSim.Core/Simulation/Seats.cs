using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 9 — Firmensitz (docs/POLITIK.md „Firmensitz und Büro“). Aufgestiegene unabhängige Akteure (Firmen) ab Größenklasse
/// <see cref="EconomyRules.FirmSeatMinClass"/> gründen genau einen Firmensitz (Typ <see cref="EconomyRules.FirmSeatType"/>, Einfluss 3 für ihre
/// Mitgliedsfraktion) in einem Sektor ihrer Fraktion, in dem sie Stationen besitzen oder mit Schiffen arbeiten. Der Sitz gibt der Firma eine eigene Kasse:
/// er schöpft Überschüsse ihrer Schiffe (über <see cref="EconomyRules.FirmShipKeep"/>) und Stationen (über dem Betriebskapital) anteilig ab, und aus dieser
/// Kasse kauft die Firma Schiffe (<see cref="Investors.ForShip"/>) und finanziert Stationen (<see cref="Investors.ForStation"/>). Der Sitz zahlt einen Anteil
/// seines Unterhalts aus der Firmenkasse; reicht sie zu lange nicht, wird er aufgegeben (Grund b). Ohne Sitz verhält sich eine Firma wie vorher.
/// </summary>
public static class FirmSeats
{
    public static StationType? SeatType(Universe u) => u.StationTypes.TryGetValue(u.Rules.FirmSeatType, out var t) ? t : null;

    /// <summary>Gründungskosten (Baukosten × <see cref="EconomyRules.StationBuildCostFactor"/>).</summary>
    public static float Cost(Universe u, StationType t) => t.BuildCost * u.Rules.StationBuildCostFactor;

    /// <summary>Was eine KI-Firma für den Sitz zahlt (Kosten × <see cref="EconomyRules.FirmSeatCostShare"/>).</summary>
    public static float FirmCost(Universe u, StationType t) => Cost(u, t) * Math.Clamp(u.Rules.FirmSeatCostShare, 0f, 1f);

    /// <summary>Ist die Station ein (fertiger) Firmensitz?</summary>
    public static bool IsSeat(Universe u, Station st) => StationProtection.KindOf(u, st) == GovernmentKind.CompanySeat;

    /// <summary>Firmensitz eines Akteurs (kleinste Id), null = keiner.</summary>
    public static Station? SeatOf(Universe u, ActorId actor)
    {
        foreach (var st in u.StationsById()) if (st.Owner == actor && IsSeat(u, st)) return st;
        return null;
    }

    /// <summary>Ist der Akteur eine KI-Firma (aufgestiegener unabhängiger Akteur, keine Fraktion)?</summary>
    public static bool IsFirm(Universe u, Actor a) => a.Kind == ActorKind.Independent && !a.IsFaction(u);

    /// <summary>Hat die Firma einen Sitz, der ihr die eigene Kasse gibt (Regel an)?</summary>
    public static bool HasTreasury(Universe u, ActorId actor) =>
        u.Rules.FirmSeats && u.Actors.TryGetValue(actor, out var a) && IsFirm(u, a) && SeatOf(u, actor) is not null;

    /// <summary>Firmen-Regierungsstation (Sitz oder Büro einer Firma): kein Zuschuss der Mitgliedsfraktion, Unterhaltsanteil für den Sitz.</summary>
    public static bool IsFirmGovernment(Universe u, Station st) =>
        u.Rules.FirmSeats && StationProtection.KindOf(u, st) is GovernmentKind.CompanySeat or GovernmentKind.Office
        && u.Actors.TryGetValue(st.Owner, out var a) && IsFirm(u, a);

    /// <summary>Unterhalt je Tick, den ein KI-Firmensitz trägt (Anteil <see cref="EconomyRules.FirmSeatUpkeepShare"/>); andere Stationen: Typ-Unterhalt.</summary>
    public static float UpkeepPerTick(Universe u, Station st, StationType t) =>
        IsFirmGovernment(u, st) && t.Government == GovernmentKind.CompanySeat ? t.UpkeepPerTick * Math.Clamp(u.Rules.FirmSeatUpkeepShare, 0f, 1f) : t.UpkeepPerTick;

    /// <summary>Was eine Firma jetzt für Investitionen aufbringen kann: Kasse + Überschuss der Schiffe über dem Rückhalt + Stationskassen über dem Betriebskapital.</summary>
    public static float Liquid(Universe u, Actor a)
    {
        float m = MathF.Max(0f, a.Treasury.Credits);
        foreach (var s in u.ShipsById()) if (s.Owner == a.Id && s.IsEconomyAi) m += MathF.Max(0f, s.Credits - u.Rules.FirmShipKeep);
        foreach (var st in u.StationsById()) if (st.Owner == a.Id && !st.IsBuildSite && StationProtection.IsEconomic(u, st)) m += MathF.Max(0f, st.Credits - WorkingCapital.Of(u, st));
        return m;
    }

    /// <summary>
    /// Sektor für den Sitz einer Firma: Sektor ihrer Mitgliedsfraktion mit dem meisten eigenen Besitz (Stationen × 3 + Schiffe), Gleichstand kleinste Id;
    /// null ohne Mitgliedschaft oder ohne Präsenz in einem Sektor der eigenen Fraktion (der Sitz verschiebt keine Grenzen fremder Fraktionen).
    /// </summary>
    public static Sector? HomeSector(Universe u, Actor a)
    {
        if (a.Membership is not { } f) return null;
        var score = new SortedDictionary<int, int>();
        foreach (var st in u.StationsById()) if (st.Owner == a.Id && !st.IsBuildSite) score[st.Sector.Value] = score.GetValueOrDefault(st.Sector.Value) + 3;
        foreach (var s in u.ShipsById()) if (s.Owner == a.Id && s.IsEconomyAi) score[s.Sector.Value] = score.GetValueOrDefault(s.Sector.Value) + 1;
        Sector? best = null; int bestScore = 0;
        foreach (var (sid, sc) in score)
        {
            if (!u.Sectors.TryGetValue(new SectorId(sid), out var sec) || sec.Owner != f) continue;
            int seats = sec.Stations.Count(id => u.Stations.TryGetValue(id, out var st) && IsSeat(u, st));
            if (seats >= u.Rules.FirmSeatsPerSector) continue;
            if (sc > bestScore) { best = sec; bestScore = sc; }
        }
        return best;
    }

    /// <summary>Freier Platz für eine Regierungsstation im Sektor: um eigene Stationen, sonst um die Sektormitte (feste Reihenfolge, kein Zufall).</summary>
    public static Vec2? Spot(Universe u, Sector sec, StationType t, ActorId owner)
    {
        var anchors = sec.Stations.Select(id => u.Stations[id]).Where(st => st.Owner == owner).OrderBy(st => st.Id.Value).Select(st => st.Position)
            .Append(new Vec2(0f, 0f)).ToList();
        foreach (var an in anchors)
            if (Outposts.FreeSpot(u, sec, an, t.Radius, t.Radius + 18f, 150f) is { } p) return p;
        return null;
    }

    /// <summary>Zahlt <paramref name="cost"/> aus Kasse, dann Stations- und Schiffsüberschüssen der Firma (Id-Reihenfolge, Rentabilität neutral).</summary>
    public static void Pay(Universe u, Actor a, float cost)
    {
        float rest = cost;
        float t = MathF.Min(rest, MathF.Max(0f, a.Treasury.Credits));
        a.Treasury.Credits -= t; rest -= t;
        if (t > 0f) u.Events.Publish(new TreasuryUpkeep(a.Id, "company_seat", t, 0f));
        foreach (var st in u.StationsById())
        {
            if (rest <= 0f) break;
            if (st.Owner != a.Id || st.IsBuildSite || !StationProtection.IsEconomic(u, st)) continue;
            float take = MathF.Min(rest, MathF.Max(0f, st.Credits - WorkingCapital.Of(u, st)));
            if (take <= 0f) continue;
            st.Credits -= take; rest -= take; Profitability.Transfer(st.Profit, -take);
        }
        foreach (var s in u.ShipsById())
        {
            if (rest <= 0f) break;
            if (s.Owner != a.Id || !s.IsEconomyAi) continue;
            float take = MathF.Min(rest, MathF.Max(0f, s.Credits - u.Rules.FirmShipKeep));
            if (take <= 0f) continue;
            s.Credits -= take; rest -= take; Profitability.Transfer(s.Profit, -take);
        }
    }

    /// <summary>Gründet den Sitz (ohne Prüfung) für <paramref name="owner"/> an <paramref name="spot"/>.</summary>
    public static Station Build(Universe u, ActorId owner, StationType t, Sector sec, Vec2 spot, float cost)
    {
        var id = u.NewStationId();
        var st = StationFactory.Create(u, id, t, StationFactory.BuiltName(u, t, sec.Id), sec.Id, spot, owner);
        st.Credits = 0f;
        u.AddStation(st);
        u.Events.Publish(new StationBuilt(id, sec.Id, t.Key));
        u.Events.Publish(new CompanySeatFounded(owner, id, sec.Id, cost));
        if (SectorControl.Update(u, sec)) u.TouchAccess();
        return st;
    }

    /// <summary>Stationsinfo: Zeile zu einem Firmensitz oder Büro (null bei anderen Stationen).</summary>
    public static string? InfoLine(Universe u, Station st)
    {
        if (Offices.IsOffice(u, st)) return Offices.InfoLine(u, st);
        if (!IsSeat(u, st)) return null;
        if (st.Owner == u.PlayerActor) return $"Dein Firmensitz — Befehlsstufe +{u.Rules.FirmSeatCommandBonus}, Fehden freigeschaltet.";
        float cash = u.Actors.TryGetValue(st.Owner, out var a) ? a.Treasury.Credits : 0f;
        return $"Firmensitz von {ActorNames.Tagged(u, st.Owner)} — Firmenkasse {cash:0} Cr; schöpft Gewinne der Firmenschiffe ab und kauft daraus Schiffe.";
    }

    /// <summary>Anzeige: Zeile zum Firmensitz eines Akteurs (leer ohne Sitz).</summary>
    public static string Text(Universe u, ActorId actor)
    {
        if (SeatOf(u, actor) is not { } seat) return "";
        string sec = u.Sectors.TryGetValue(seat.Sector, out var s) ? s.Name : "?";
        float cash = u.Actors.TryGetValue(actor, out var a) ? a.Treasury.Credits : 0f;
        return actor == u.PlayerActor ? $"Firmensitz: {seat.Name} ({sec}) — Befehlsstufe +{u.Rules.FirmSeatCommandBonus}, Fehden freigeschaltet"
            : $"Firmensitz: {seat.Name} ({sec}) · Firmenkasse {cash:0} Cr";
    }
}

/// <summary>
/// Ausbau Punkt 9: Firmensitze der KI (Viabilitäts-Modul, Intervall 50, nach dem Kapitalausgleich). Je Firma (Id-Reihenfolge): mit Sitz Abschöpfung in die
/// Firmenkasse und Prüfung des Unterhaltsrückstands; ohne Sitz Gründungsversuch (Klasse, Geld, Sektor, Abklingzeit). Kein Zufallswurf.
/// </summary>
public sealed class FirmSeatSystem : ISimSystem
{
    public const int Interval = 50;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.FirmSeats || FirmSeats.SeatType(u) is not { } type) return;
        Dictionary<ActorId, Holdings>? index = null;
        foreach (var a in u.Actors.Values.OrderBy(x => x.Id.Value).ToList())
        {
            if (!FirmSeats.IsFirm(u, a)) continue;
            var seat = FirmSeats.SeatOf(u, a.Id);
            if (seat is not null && u.StationTypes.TryGetValue(seat.TypeKey, out var st)) { Skim(u, a, seat, st, dt); Arrears(u, a, seat, st); continue; }
            a.SeatArrearsSince = -1;
            if (a.LastFoundTick != 0 && u.Tick - a.LastFoundTick < r.FirmFoundCooldownTicks) continue;
            index ??= ActorRules.Index(u);
            if ((int)ActorRules.ClassOf(u, index, a.Id) < r.FirmSeatMinClass) continue;
            float cost = FirmSeats.FirmCost(u, type);
            if (FirmSeats.Liquid(u, a) < cost * r.FirmSeatMoneyFactor) continue;
            if (FirmSeats.HomeSector(u, a) is not { } sec) continue;
            if (FirmSeats.Spot(u, sec, type, a.Id) is not { } spot) continue;
            a.LastFoundTick = u.Tick;
            FirmSeats.Pay(u, a, cost);
            FirmSeats.Build(u, a.Id, type, sec, spot, cost);
            index = null;
        }
    }

    /// <summary>Rücklage der Firmenkasse für den Sitz-Unterhalt (ein Spieltag), die beim Aufstocken der Schiffe bleibt.</summary>
    public static float Reserve(Universe u, Station seat, StationType type) => FirmSeats.UpkeepPerTick(u, seat, type) * 2400f;

    /// <summary>
    /// Abschöpfung (nur solange die Kasse unter <see cref="EconomyRules.FirmTreasuryTarget"/> liegt): Anteil des positiven Gewinns jedes Firmenschiffs über dem
    /// Rückhalt, Anteil des Stationsüberschusses über dem Betriebskapital. Danach Aufstocken: Firmenschiffe unter <see cref="EconomyRules.FirmShipKeep"/> bekommen
    /// aus der Kasse (über der Rücklage) bis zum Rückhalt — die Kasse ist der Geldpool der Firma, kein Lager. Rentabilität beidseitig neutral.
    /// </summary>
    static void Skim(Universe u, Actor a, Station seat, StationType type, int dt)
    {
        var r = u.Rules;
        float share = Math.Clamp(r.FirmSkimPerTick * dt, 0f, 1f);
        float total = 0f;
        float profitShare = Math.Clamp(r.FirmSkimShare, 0f, 1f);
        bool room = r.FirmTreasuryTarget <= 0f || a.Treasury.Credits < r.FirmTreasuryTarget;
        if (room)
        {
            foreach (var s in u.ShipsById())
            {
                if (s.Owner != a.Id || !s.IsEconomyAi || s.Role == ShipRole.Smuggler || s.Credits <= r.FirmShipKeep || s.Profit.Rate <= 0f) continue;   // Ausbau Punkt 5: Schmuggler behalten ihr Betriebsgeld (sonst fehlt das Geld für die nächste Ladung)
                float take = MathF.Min(s.Profit.Rate * dt * profitShare, s.Credits - r.FirmShipKeep);
                s.Credits -= take; total += take; Profitability.Transfer(s.Profit, -take);
            }
            foreach (var st in u.StationsById())
            {
                if (st.Owner != a.Id || st.IsBuildSite || !StationProtection.IsEconomic(u, st)) continue;
                float excess = st.Credits - WorkingCapital.Of(u, st);
                if (excess <= 0f) continue;
                float take = excess * share;
                st.Credits -= take; total += take; Profitability.Transfer(st.Profit, -take);
            }
            if (total > 0f)
            {
                a.Treasury.Credits += total;
                u.Events.Publish(new TreasuryIncome(a.Id, "firm_skim", total));
            }
        }
        // Aufstocken armer Firmenschiffe aus der Kasse (über der Rücklage für den Sitz)
        float spare = a.Treasury.Credits - Reserve(u, seat, type);
        if (spare <= 0f || r.FirmShipKeep <= 0f) return;
        float given = 0f;
        foreach (var s in u.ShipsById())
        {
            if (spare <= 0f) break;
            if (s.Owner != a.Id || !s.IsEconomyAi || s.Credits >= r.FirmShipKeep) continue;
            float give = MathF.Min(r.FirmShipKeep - s.Credits, spare);
            s.Credits += give; spare -= give; given += give; Profitability.Transfer(s.Profit, give);
        }
        if (given <= 0f) return;
        a.Treasury.Credits -= given;
        u.Events.Publish(new TreasuryUpkeep(a.Id, "firm_capital", given, 0f));
    }

    /// <summary>Trägt die Firmenkasse den Sitz-Unterhalt eines Upkeep-Schritts nicht, läuft der Rückstand; nach der Frist wird der Sitz aufgegeben (Grund b).</summary>
    static void Arrears(Universe u, Actor a, Station seat, StationType type)
    {
        float need = FirmSeats.UpkeepPerTick(u, seat, type) * 10f;
        if (a.Treasury.Credits >= need) { a.SeatArrearsSince = -1; return; }
        if (a.SeatArrearsSince < 0) { a.SeatArrearsSince = u.Tick; return; }
        if (u.Tick - a.SeatArrearsSince < u.Rules.FirmSeatArrearsTicks) return;
        a.SeatArrearsSince = -1;
        var sec = u.Sectors.GetValueOrDefault(seat.Sector);
        u.Events.Publish(new CompanySeatLost(a.Id, seat.Id, "unpaid"));
        Departures.RemoveStation(u, seat, DepartureReason.Bankrupt, "seat_unpaid");
        if (sec is not null && SectorControl.Update(u, sec)) u.TouchAccess();
    }
}

/// <summary>
/// Ausbau Punkt 9 — Büro (docs/POLITIK.md „Firmensitz und Büro“): kleine Außenstelle (Typ <see cref="EconomyRules.OfficeType"/>, Einfluss 1,5, Hangar,
/// Auftragsbrett; später Schutzverträge — Punkt 3) in einem <b>fremden</b> Sektor, dessen Besitzer zustimmt: Beziehung beider Seiten mindestens
/// <see cref="EconomyRules.OfficeMinRelation"/>, kein Krieg, keine durchgesetzte Sperre gegen den Gründer. KI-Fraktionen gründen Büros, wo ihre Fraktion
/// (samt Mitgliedsfirmen) viele Aufträge hat, aber keine Station; Firmen mit Sitz ebenso (höchstens eins). Wird der Gastgeber feindlich, schließt das Büro.
/// </summary>
public static class Offices
{
    public static StationType? OfficeType(Universe u) => u.StationTypes.TryGetValue(u.Rules.OfficeType, out var t) ? t : null;

    public static float Cost(Universe u, StationType t) => t.BuildCost * u.Rules.StationBuildCostFactor;

    public static bool IsOffice(Universe u, Station st) => StationProtection.KindOf(u, st) == GovernmentKind.Office;

    /// <summary>Büros eines Akteurs (Id-Reihenfolge).</summary>
    public static List<Station> Of(Universe u, ActorId owner) => u.StationsById().Where(s => s.Owner == owner && IsOffice(u, s)).ToList();

    /// <summary>Fraktion, als die ein Akteur gilt (Fraktionsakteur selbst oder Mitgliedschaft; Spieler = Spielerfraktion).</summary>
    static FactionId? FactionFor(Universe u, ActorId actor) => u.FactionOf(actor);

    /// <summary>
    /// Zustimmung des Gastgebers: warum <paramref name="founder"/> im Sektor <paramref name="sec"/> kein Büro haben darf (null = darf). Nur Sektoren einer anderen
    /// KI-Fraktion; Krieg, Beziehung unter <see cref="EconomyRules.OfficeMinRelation"/> (eine Richtung genügt) oder eine durchgesetzte Sperre (Stufe ≥ 4,
    /// oder ≥ 1 gegen den Gründer) verweigern es. Rein.
    /// </summary>
    public static string? HostIssue(Universe u, Sector sec, ActorId founder)
    {
        var host = sec.Owner;
        if (!u.IsNpcFaction(host)) return $"{sec.Name} gehört keiner fremden Fraktion — Büros nur in fremden Sektoren.";
        if (FactionFor(u, founder) is { } ff && ff == host) return $"{sec.Name} gehört deiner eigenen Fraktion.";
        string name = u.Factions[host].Name;
        if (FactionFor(u, founder) is { } f && Wars.AtWar(u, host, f)) return $"Krieg mit {name} — kein Büro.";
        float min = u.Rules.OfficeMinRelation;
        if (Relations.Effective(u, (ActorId)host, founder) < min || Relations.Effective(u, founder, (ActorId)host) < min)
            return $"{name} stimmt nicht zu (Beziehung unter {min:0.00}).";
        var acc = sec.Access;
        if (acc.Enforced && (acc.EntryLevel >= 4 || (acc.EntryLevel >= 1 && FactionFor(u, founder) is { } g && acc.Against == g)))
            return $"{name} sperrt {sec.Name} (Stufe {acc.EntryLevel}) — kein Büro.";
        return null;
    }

    /// <summary>Hat der Akteur oder eine Station seiner Fraktion (Fraktionsakteur oder Mitgliedsfirma) schon eine Station im Sektor?</summary>
    public static bool HasPresence(Universe u, Sector sec, ActorId actor)
    {
        var f = FactionFor(u, actor);
        foreach (var id in sec.Stations)
            if (u.Stations.TryGetValue(id, out var st) && (st.Owner == actor || (f is { } fv && u.FactionOf(st) == fv && !u.IsPlayerFaction(fv)))) return true;
        return false;
    }

    /// <summary>Handelsaufträge (Kauf/Verkauf/Lieferung, Schürffeld) von Schiffen der Akteure in <paramref name="who"/> mit Ziel je Sektor.</summary>
    public static Dictionary<int, int> TradeBySector(Universe u, Func<Ship, bool> who)
    {
        var d = new Dictionary<int, int>();
        void Add(SectorId s) => d[s.Value] = d.GetValueOrDefault(s.Value) + 1;
        void AddSt(StationId id) { if (id.Value != 0 && u.Stations.TryGetValue(id, out var st)) Add(st.Sector); }
        foreach (var s in u.ShipsById())
        {
            if (!s.IsEconomyAi || !who(s)) continue;
            switch (s.Order)
            {
                case TradeOrder t: AddSt(t.Buy); AddSt(t.Sell); break;
                case MineOrder m: Add(m.Sector); AddSt(m.DeliverTo); break;
            }
        }
        return d;
    }

    /// <summary>Büro gründen (ohne Prüfung): Station anlegen, Ereignisse, Sektorkontrolle neu.</summary>
    public static Station Build(Universe u, ActorId owner, StationType t, Sector sec, Vec2 spot, float cost)
    {
        var id = u.NewStationId();
        var st = StationFactory.Create(u, id, t, StationFactory.BuiltName(u, t, sec.Id), sec.Id, spot, owner);
        st.Credits = 0f;
        u.AddStation(st);
        u.Events.Publish(new StationBuilt(id, sec.Id, t.Key));
        u.Events.Publish(new OfficeFounded(owner, id, sec.Id, cost));
        if (SectorControl.Update(u, sec)) u.TouchAccess();
        return st;
    }

    /// <summary>Büro schließen (Abgang, kein Erlös), Sektorkontrolle neu.</summary>
    public static void Close(Universe u, Station st, string reason)
    {
        var sec = u.Sectors.GetValueOrDefault(st.Sector);
        u.Events.Publish(new OfficeClosed(st.Owner, st.Id, st.Sector, reason));
        Departures.RemoveStation(u, st, reason == "closed" ? DepartureReason.Sold : DepartureReason.Unprofitable, "office_" + reason);
        if (sec is not null && SectorControl.Update(u, sec)) u.TouchAccess();
    }

    /// <summary>Hook für Punkt 3 (Schutzverträge): bietet das Büro schon Schutzverträge an? Noch nicht — die Zeile erklärt es.</summary>
    public static bool ProtectionContracts(Universe u, Station st) =>
        u.Rules.ProtectionContracts && u.FactionOf(st) is { } f && Protection.StandingIssue(u, f) is null;

    /// <summary>Anzeigezeile eines Büros (Stationsinfo).</summary>
    public static string InfoLine(Universe u, Station st) =>
        ProtectionContracts(u, st) && u.FactionOf(st) is { } f
            ? $"Büro von {ActorNames.Tagged(u, st.Owner)} — Hangar, Auftragsbrett, Schutzvertrag ({u.Rules.ProtectionFeePerDay:0} Cr/Tag, Comms){(Protection.Of(u, f) is not null ? " · läuft" : "")}."
            : $"Büro von {ActorNames.Tagged(u, st.Owner)} — Hangar und Auftragsbrett.";
}

/// <summary>
/// Ausbau Punkt 9: Büros der KI (Politik-Modul, Intervall <see cref="Interval"/>). Schließt Büros, deren Gastgeber nicht mehr zustimmt (Krieg, feindlich,
/// Sperre — auch Spielerbüros), und gründet je Fraktion/Firma mit Sitz höchstens ein Büro pro Schritt im fremden Sektor mit den meisten eigenen Aufträgen
/// (ab <see cref="EconomyRules.OfficeMinTrade"/>), wo sie noch keine Station hat. Bezahlt aus <see cref="FactionViability.Spendable"/>. Kein Zufallswurf.
/// </summary>
public sealed class OfficeSystem : ISimSystem
{
    public const int Interval = 200;

    public void Step(Universe u, int dt)
    {
        var r = u.Rules;
        if (!r.Offices && !r.PlayerOffices) return;
        if (Offices.OfficeType(u) is not { } type) return;
        // Gastgeber stimmt nicht mehr zu → schließen (KI und Spieler)
        foreach (var st in u.StationsById().ToList())
        {
            if (!Offices.IsOffice(u, st) || !u.Sectors.TryGetValue(st.Sector, out var sec)) continue;
            if (!u.IsNpcFaction(sec.Owner)) continue;   // herrenlos oder eigener Sektor geworden: bleibt
            if (u.FactionOf(st.Owner) is { } own && own == sec.Owner) continue;
            if (Offices.HostIssue(u, sec, st.Owner) is not null && SpaceSim.Core.Combat.Siege.Of(u, st.Id) is null) Offices.Close(u, st, "unwelcome");
        }
        if (!r.Offices) return;
        float cost = Offices.Cost(u, type);
        foreach (var a in u.Actors.Values.OrderBy(x => x.Id.Value).ToList())
        {
            bool faction = a.IsNpcFaction(u);
            bool firm = !faction && FirmSeats.HasTreasury(u, a.Id);
            if (!faction && !firm) continue;
            if (a.LastFoundTick != 0 && u.Tick - a.LastFoundTick < (faction ? r.OfficeCooldownTicks : r.FirmFoundCooldownTicks)) continue;
            if (Offices.Of(u, a.Id).Count >= (faction ? r.OfficesPerFaction : 1)) continue;
            float budget = faction ? FactionViability.Spendable(u, a) : a.Treasury.Credits - cost;   // Firmen: Kasse muss danach noch die Kosten tragen
            if (budget < cost) continue;
            var f = u.FactionOf(a.Id);
            var trade = Offices.TradeBySector(u, s => faction ? (f is { } fv && u.FactionOf(s.Owner) == fv) : s.Owner == a.Id);
            int min = faction ? r.OfficeMinTrade : Math.Max(1, r.OfficeMinTrade - 1);
            foreach (var (sid, n) in trade.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
            {
                if (n < min) break;
                if (!u.Sectors.TryGetValue(new SectorId(sid), out var sec)) continue;
                if (Offices.HostIssue(u, sec, a.Id) is not null || Offices.HasPresence(u, sec, a.Id)) continue;
                if (FirmSeats.Spot(u, sec, type, a.Id) is not { } spot) continue;
                a.Treasury.Credits -= cost;
                u.Events.Publish(new TreasuryUpkeep(a.Id, "office", cost, 0f));
                a.LastFoundTick = u.Tick;
                Offices.Build(u, a.Id, type, sec, spot, cost);
                break;
            }
        }
    }
}

/// <summary>
/// Ausbau Punkt 9: Firmensitz und Büros der Spielerfraktion (über Comms und „Meine Fraktion“). Firmensitz: genau einer, im eigenen oder herrenlosen Sektor
/// (ohne Piratenversteck), ab Größenklasse <see cref="EconomyRules.PlayerFirmSeatMinClass"/>; hebt die Befehlsstufe um <see cref="EconomyRules.FirmSeatCommandBonus"/>
/// und schaltet Fehden frei (Punkt 6). Büro: im fremden Sektor mit Zustimmung des Gastgebers (<see cref="Offices.HostIssue"/>), eines je Sektor, ab
/// <see cref="EconomyRules.PlayerOfficeMinClass"/>. Kosten = Baukosten × Baukostenfaktor aus der Spielerkasse (Senke), Unterhalt als Regierungsunterhalt
/// (Spieleranteil).
/// </summary>
public static class PlayerSeatsOffices
{
    /// <summary>Firmensitz der Spielerfraktion (null = keiner).</summary>
    public static Station? Seat(Universe u) => FirmSeats.SeatOf(u, u.PlayerActor);

    /// <summary>Sind Fehden freigeschaltet (Spieler besitzt einen Firmensitz)? Hook für Punkt 6.</summary>
    public static bool FeudUnlocked(Universe u) => u.Rules.PlayerFirmSeat && Seat(u) is not null;

    /// <summary>Befehlsstufen-Bonus durch den eigenen Firmensitz (0 ohne).</summary>
    public static int CommandBonus(Universe u, ActorId actor) =>
        actor == u.PlayerActor && u.Rules.PlayerFirmSeat && Seat(u) is not null ? Math.Max(0, u.Rules.FirmSeatCommandBonus) : 0;

    static Ship? ActiveShipIn(Universe u, SectorId sector) =>
        u.Ships.TryGetValue(u.PlayerShip, out var s) && s.Sector == sector && s.Transit is null ? s : null;

    static string? ClassIssue(Universe u, int min)
    {
        var cls = ActorRules.ClassOf(u, ActorRules.Index(u), u.PlayerActor);
        return (int)cls < min ? $"Erst ab Größenklasse {ActorNames.Size((ActorSizeClass)min)} (du: {ActorNames.Size(cls)})." : null;
    }

    static Vec2? NearShip(Universe u, Sector sec, Ship ship, StationType t) => Outposts.FreeSpot(u, sec, ship.Position, t.Radius, t.Radius + 18f, 150f);

    /// <summary>Warum der Spieler in <paramref name="sector"/> keinen Firmensitz gründen kann (null = er kann). Rein.</summary>
    public static string? SeatIssue(Universe u, SectorId sector)
    {
        var r = u.Rules;
        if (!r.PlayerFirmSeat) return "Eigener Firmensitz ist abgeschaltet.";
        if (u.PlayerFaction is null) return "Du bist keine eigene Fraktion.";
        if (FirmSeats.SeatType(u) is not { } t) return "Kein Firmensitz-Typ in dieser Welt.";
        if (Seat(u) is { } have) return $"Du hast schon einen Firmensitz ({have.Name}).";
        if (!u.Sectors.TryGetValue(sector, out var sec)) return "Unbekannter Sektor.";
        if (ActiveShipIn(u, sector) is not { } ship) return "Dein aktives Schiff muss im Sektor sein.";
        if (u.IsNpcFaction(sec.Owner)) return $"{sec.Name} gehört {u.Factions[sec.Owner].Name} — nur eigene oder herrenlose Sektoren.";
        if (sec.Stations.Any(id => u.Stations.TryGetValue(id, out var st) && StationProtection.IsHideout(u, st))) return "Piratenversteck im Sektor — kein Firmensitz.";
        if (ClassIssue(u, r.PlayerFirmSeatMinClass) is { } ci) return ci;
        float cost = FirmSeats.Cost(u, t);
        if (u.Player.Treasury.Credits < cost) return $"Kasse reicht nicht ({cost:0} Cr nötig).";
        if (NearShip(u, sec, ship, t) is null) return "Kein freier Platz in der Nähe des Schiffs.";
        return null;
    }

    public static Station FoundSeat(Universe u, SectorId sector)
    {
        var t = FirmSeats.SeatType(u)!;
        var sec = u.Sectors[sector];
        var spot = NearShip(u, sec, u.Ships[u.PlayerShip], t)!.Value;
        float cost = FirmSeats.Cost(u, t);
        u.Player.Treasury.Credits -= cost;
        return FirmSeats.Build(u, u.PlayerActor, t, sec, spot, cost);
    }

    /// <summary>Warum die Station kein eigener schließbarer Firmensitz/Büro ist (null = schließbar).</summary>
    public static string? CloseIssue(Universe u, StationId id, bool office)
    {
        if (!u.Stations.TryGetValue(id, out var st) || st.Owner != u.PlayerActor || (office ? !Offices.IsOffice(u, st) : !FirmSeats.IsSeat(u, st)))
            return office ? "Kein eigenes Büro." : "Kein eigener Firmensitz.";
        if (SpaceSim.Core.Combat.Siege.Of(u, id) is not null) return "Wird belagert.";
        return null;
    }

    public static void CloseSeat(Universe u, StationId id)
    {
        var st = u.Stations[id];
        var sec = u.Sectors[st.Sector];
        u.Events.Publish(new CompanySeatLost(u.PlayerActor, id, "closed"));
        Departures.RemoveStation(u, st, DepartureReason.Sold, "closed");
        if (SectorControl.Update(u, sec)) u.TouchAccess();
        if (sec.Access.EntryLevel > SectorControl.MaxLevel(u, sec)) FactionPolicySystem.SetLevel(u, sec, SectorControl.MaxLevel(u, sec));
    }

    /// <summary>Warum der Spieler in <paramref name="sector"/> kein Büro gründen kann (null = er kann). Rein.</summary>
    public static string? OfficeIssue(Universe u, SectorId sector)
    {
        var r = u.Rules;
        if (!r.PlayerOffices) return "Eigene Büros sind abgeschaltet.";
        if (u.PlayerFaction is null) return "Du bist keine eigene Fraktion.";
        if (Offices.OfficeType(u) is not { } t) return "Kein Bürotyp in dieser Welt.";
        if (!u.Sectors.TryGetValue(sector, out var sec)) return "Unbekannter Sektor.";
        if (ActiveShipIn(u, sector) is not { } ship) return "Dein aktives Schiff muss im Sektor sein.";
        if (Offices.HostIssue(u, sec, u.PlayerActor) is { } hi) return hi;
        if (Offices.Of(u, u.PlayerActor).Any(s => s.Sector == sector)) return "Hier hast du schon ein Büro.";
        if (ClassIssue(u, r.PlayerOfficeMinClass) is { } ci) return ci;
        float cost = Offices.Cost(u, t);
        if (u.Player.Treasury.Credits < cost) return $"Kasse reicht nicht ({cost:0} Cr nötig).";
        if (NearShip(u, sec, ship, t) is null) return "Kein freier Platz in der Nähe des Schiffs.";
        return null;
    }

    public static Station FoundOffice(Universe u, SectorId sector)
    {
        var t = Offices.OfficeType(u)!;
        var sec = u.Sectors[sector];
        var spot = NearShip(u, sec, u.Ships[u.PlayerShip], t)!.Value;
        float cost = Offices.Cost(u, t);
        u.Player.Treasury.Credits -= cost;
        return Offices.Build(u, u.PlayerActor, t, sec, spot, cost);
    }

    /// <summary>Unterhalt je Minute, den der Spieler für einen Typ zahlt (Anzeige).</summary>
    public static float UpkeepPerMinute(Universe u, StationType t) => PlayerUpkeep.PlayerPerTick(u, t) * SimClock.TicksPerSecond * 60f;
}
