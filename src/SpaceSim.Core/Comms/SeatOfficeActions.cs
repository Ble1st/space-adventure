using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Comms;

/// <summary>
/// Ausbau Punkt 9: Comms-Aktion „firmseat“ — Firmensitz der Spielerfraktion gründen/aufgeben (<see cref="PlayerSeatsOffices"/>). Schlüssel streng:
/// <c>firmseat:found:&lt;SectorId&gt;</c>, <c>firmseat:close:&lt;StationId&gt;</c>.
/// </summary>
public sealed class FirmSeatAction : ICommsAction
{
    public const string KeyId = "firmseat";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (u.PlayerFaction is null || !u.Rules.PlayerFirmSeat) return list;
        if (PlayerSeatsOffices.Seat(u) is { } seat) list.Add(Preview(u, $"{KeyId}:close:{seat.Id.Value}"));
        else if (u.Ships.TryGetValue(u.PlayerShip, out var ship)) list.Add(Preview(u, $"{KeyId}:found:{ship.Sector.Value}"));
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (CommsActions.TryId(offer, $"{KeyId}:found", out var sid))
        {
            var sector = new SectorId(sid);
            string name = u.Sectors.TryGetValue(sector, out var sec) ? sec.Name : "?";
            string? issue = PlayerSeatsOffices.SeatIssue(u, sector);
            var t = FirmSeats.SeatType(u);
            float cost = t is null ? 0f : FirmSeats.Cost(u, t);
            float upkeep = t is null ? 0f : PlayerSeatsOffices.UpkeepPerMinute(u, t);
            return new(offer, $"Firmensitz gründen – {name}",
                issue ?? $"{cost:0} Cr einmalig, Unterhalt {upkeep:0} Cr/min aus deiner Kasse — Einfluss 3, Befehlsstufe +{u.Rules.FirmSeatCommandBonus}, Fehden freigeschaltet.",
                -cost, issue is null, issue);
        }
        if (CommsActions.TryId(offer, $"{KeyId}:close", out var stid))
        {
            string? issue = PlayerSeatsOffices.CloseIssue(u, new StationId(stid), office: false);
            string name = u.Stations.TryGetValue(new StationId(stid), out var st) ? st.Name : "?";
            return new(offer, $"Firmensitz aufgeben – {name}", issue ?? "Kein Erlös; Befehlsstufe und Fehden fallen weg, der Unterhalt endet.", 0f, issue is null, issue);
        }
        return new(offer, "Firmensitz", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (CommsActions.TryId(offer, $"{KeyId}:found", out var sid))
        {
            var sector = new SectorId(sid);
            if (PlayerSeatsOffices.SeatIssue(u, sector) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
            float cost = FirmSeats.Cost(u, FirmSeats.SeatType(u)!);
            var st = PlayerSeatsOffices.FoundSeat(u, sector);
            string msg = $"{st.Name} gegründet ({cost:0} Cr) — Befehlsstufe +{u.Rules.FirmSeatCommandBonus}, Fehden freigeschaltet.";
            u.Events.Publish(new CommsActionPerformed(KeyId, sid, -cost, msg));
            return new ServiceResult(true, 1f, -cost, msg);
        }
        if (CommsActions.TryId(offer, $"{KeyId}:close", out var stid))
        {
            if (PlayerSeatsOffices.CloseIssue(u, new StationId(stid), office: false) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
            string name = u.Stations[new StationId(stid)].Name;
            PlayerSeatsOffices.CloseSeat(u, new StationId(stid));
            string msg = $"{name} aufgegeben.";
            u.Events.Publish(new CommsActionPerformed(KeyId, stid, 0f, msg));
            return new ServiceResult(true, 1f, 0f, msg);
        }
        return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
    }
}

/// <summary>
/// Ausbau Punkt 9: Comms-Aktion „office“ — Büro der Spielerfraktion in einem fremden Sektor gründen/schließen (Zustimmung des Gastgebers,
/// <see cref="Offices.HostIssue"/>). Schlüssel streng: <c>office:found:&lt;SectorId&gt;</c>, <c>office:close:&lt;StationId&gt;</c>.
/// </summary>
public sealed class OfficeAction : ICommsAction
{
    public const string KeyId = "office";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (u.PlayerFaction is null || !u.Rules.PlayerOffices) return list;
        if (u.Ships.TryGetValue(u.PlayerShip, out var ship) && !Offices.Of(u, u.PlayerActor).Any(s => s.Sector == ship.Sector))
            list.Add(Preview(u, $"{KeyId}:found:{ship.Sector.Value}"));
        foreach (var st in Offices.Of(u, u.PlayerActor)) list.Add(Preview(u, $"{KeyId}:close:{st.Id.Value}"));
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (CommsActions.TryId(offer, $"{KeyId}:found", out var sid))
        {
            var sector = new SectorId(sid);
            string name = u.Sectors.TryGetValue(sector, out var sec) ? sec.Name : "?";
            string? issue = PlayerSeatsOffices.OfficeIssue(u, sector);
            var t = Offices.OfficeType(u);
            float cost = t is null ? 0f : Offices.Cost(u, t);
            float upkeep = t is null ? 0f : PlayerSeatsOffices.UpkeepPerMinute(u, t);
            return new(offer, $"Büro gründen – {name}",
                issue ?? $"{cost:0} Cr einmalig, Unterhalt {upkeep:0} Cr/min aus deiner Kasse — Einfluss 1,5, Hangar und Auftragsbrett; schließt, wenn der Gastgeber feindlich wird.",
                -cost, issue is null, issue);
        }
        if (CommsActions.TryId(offer, $"{KeyId}:close", out var stid))
        {
            string? issue = PlayerSeatsOffices.CloseIssue(u, new StationId(stid), office: true);
            string name = u.Stations.TryGetValue(new StationId(stid), out var st) ? st.Name : "?";
            return new(offer, $"Büro schließen – {name}", issue ?? "Kein Erlös; der Unterhalt endet.", 0f, issue is null, issue);
        }
        return new(offer, "Büro", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (CommsActions.TryId(offer, $"{KeyId}:found", out var sid))
        {
            var sector = new SectorId(sid);
            if (PlayerSeatsOffices.OfficeIssue(u, sector) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
            float cost = Offices.Cost(u, Offices.OfficeType(u)!);
            var st = PlayerSeatsOffices.FoundOffice(u, sector);
            string msg = $"{st.Name} gegründet ({cost:0} Cr) — Hangar und Auftragsbrett in {u.Sectors[sector].Name}.";
            u.Events.Publish(new CommsActionPerformed(KeyId, sid, -cost, msg));
            return new ServiceResult(true, 1f, -cost, msg);
        }
        if (CommsActions.TryId(offer, $"{KeyId}:close", out var stid))
        {
            if (PlayerSeatsOffices.CloseIssue(u, new StationId(stid), office: true) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
            var st = u.Stations[new StationId(stid)];
            string name = st.Name;
            Offices.Close(u, st, "closed");
            string msg = $"{name} geschlossen.";
            u.Events.Publish(new CommsActionPerformed(KeyId, stid, 0f, msg));
            return new ServiceResult(true, 1f, 0f, msg);
        }
        return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
    }
}
