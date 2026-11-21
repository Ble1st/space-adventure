using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Services;

/// <summary>Angebot einer Taxifahrt: Ziel, Preis (Gebühr + Zoll), Dauer, Zoll, Lage, was dort wartet und warum es (nicht) buchbar ist.</summary>
public sealed record TaxiQuote(Station To, float Price, float Toll, long Ticks, int Jumps, Ship? WaitingShip, bool RentalThere, float RentalUpfront,
    string Situation, string? Issue)
{
    public bool Bookable => Issue is null;
    /// <summary>Gesamtkosten = Fahrpreis (inkl. Zoll). Das Leihschiff am Ziel zahlt die Person erst dort selbst (Dienst „rental“ im Dock-Menü der Zielstation).</summary>
    public float Total => Price;
    /// <summary>Aufschlüsselung des Fahrpreises („Fahrt 99 (inkl. 12 Zoll)“); Leihschiff-Kosten nennt die Ansicht am Ziel (Hangar).</summary>
    public string Breakdown(EconomyRules r) => $"Fahrt {Price:0} Cr{(Toll > 0f ? $" (inkl. {Toll:0} Zoll)" : "")}";
}

/// <summary>
/// Phase 12, Dienst „taxi“ (UI.md Abschnitt 3 „Taxi“, entschieden): Das Taxi bringt den <b>Spieler allein</b> zu einer anderen Station; das Schiff bleibt
/// hier angedockt (Liegegebühr an fremden Stationen). Ziele: Stationen mit Hangar, an denen ein eigenes Schiff wartet oder ein Leihschiff zu haben ist.
/// Preis = Grundgebühr + je Sprung + je Streckeneinheit (an diese Station) + Zoll der Route (an die Sektorbesitzer). Gesperrte Routen sind nicht buchbar
/// (Grund). Am Ziel steigt die Person zu Fuß in der Station aus (kein fertiges Schiff mehr): Wartet dort ein eigenes Schiff, übernimmt sie es im
/// Hangar selbst, sonst mietet sie ein Leihschiff (Dienst „rental“). Die Reisezeit liefert
/// <see cref="ServiceResult.Units"/> (Ticks); die Ansicht lässt die Zeit im Zeitraffer vergehen. Schlüssel streng: <c>taxi:&lt;StationId&gt;</c>.
/// </summary>
public sealed class TaxiService : IStationService
{
    public const string ServiceKey = "taxi";
    public const string Ride = "taxi";

    public string Key => ServiceKey;
    public bool IsAvailable(Universe u, Station station) => !station.IsBuildSite;

    /// <summary>Ziele mit Angebot (Stationen mit Dienst „hangar“, außer dieser), sortiert nach Dauer, dann Id.</summary>
    public static IReadOnlyList<TaxiQuote> Quotes(Universe u, Station from, Ship ship) =>
        u.Stations.Values.Where(st => st.Id != from.Id && Offers(u, st, HangarService.ServiceKey))
            .Select(st => Quote(u, from, ship, st)).Where(q => q is not null).Select(q => q!)
            .OrderBy(q => q.Issue is null ? 0 : 1).ThenBy(q => q.Ticks).ThenBy(q => q.To.Id.Value).ToList();

    static bool Offers(Universe u, Station st, string service) =>
        !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Services.Contains(service);

    /// <summary>Angebot für ein Ziel (null = kein Ziel für das Taxi).</summary>
    public static TaxiQuote? Quote(Universe u, Station from, Ship ship, Station to)
    {
        if (to.Id == from.Id || !Offers(u, to, HangarService.ServiceKey) || !Intel.Knowledge.KnowsStation(u, to)) return null;   // Ausbau Punkt 16: nur bekannte Ziele
        var r = u.Rules;
        var profile = GateAccess.ProfileOf(u, ship) with { Laden = false };   // die Person reist ohne Fracht
        var route = u.FindRoute(from.Sector, to.Sector, profile);
        var waiting = PlayerFleet.ParkedAt(u, to).FirstOrDefault(s => !s.IsRental) ?? PlayerFleet.ParkedAt(u, to).FirstOrDefault();
        bool rental = Offers(u, to, RentalService.ServiceKey) && u.ShipModels.ContainsKey(r.RentalModel);
        string situation = SecurityInfo.ForSector(u, to.Sector, ship).Summary;
        if (route is null)
        {
            string reason = GateAccess.BlockReason(u, ship, from.Sector, to.Sector) ?? "Sperre auf dem Weg";
            return new TaxiQuote(to, 0f, 0f, 0, -1, waiting, rental, Hangar.RentalUpfront(r), situation, $"Route gesperrt: {reason}");
        }
        float toll = 0f;
        if (u.AccessRestricted)
            foreach (var gid in route) toll += GateAccess.Evaluate(u, u.Gates[gid], profile, null).Toll;
        float dist = Travel.EstimateDistance(u, from.Sector, from.Position, to.Sector, to.Position, profile);
        float price = r.TaxiBaseFee + r.TaxiPerJump * route.Count + r.TaxiPerUnit * dist;
        long ticks = (long)MathF.Ceiling(dist / MathF.Max(0.1f, r.TaxiSpeed)) + route.Count * r.TaxiJumpTicks + 1;
        string? issue = null;
        // Kein Ziel-Ausschluss mehr: Die Person steigt am Ziel zu Fuß in der Station aus und wählt dort selbst — wartendes eigenes Schiff übernehmen
        // oder Leihschiff mieten (beides reine Anzeige, kein Buchungsgrund). Bezahlt wird nur die Fahrt.
        float total = price + toll;
        if (u.WalletOf(ship).Credits < total) issue = $"Nicht genügend Credits ({total:0} Cr nötig).";
        return new TaxiQuote(to, price + toll, toll, ticks, route.Count, waiting, rental, Hangar.RentalUpfront(r), situation, issue);
    }

    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship) =>
        Quotes(u, station, ship).Select(q => new ServiceOffer($"{Ride}:{q.To.Id.Value}", $"Taxi nach {q.To.Name}", q.Price, q.Bookable ? 1f : 0f, default, q.Ticks, q.Jumps)).ToList();

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!Keys.TryParse(request.Offer, new[] { Ride }, out _, out int v)) return Fail($"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station)) return Fail("Kein Taxi an dieser Station.");
        if (!ship.IsPlayer || ship.Id != u.PlayerShip) return Fail("Das Taxi nimmt nur dich mit.");
        if (!u.Stations.TryGetValue(new StationId(v), out var to) || Quote(u, station, ship, to) is not { } q) return Fail("Kein Taxiziel.");
        if (q.Issue is not null) return Fail(q.Issue);

        // Bezahlen: Gebühr an diese Station, Zoll an die Sektorbesitzer (Kasse der Person)
        var wallet = u.WalletOf(ship);
        float fee = q.Price - q.Toll;
        wallet.Credits -= fee;
        station.Credits += fee;
        if (q.Toll > 0f)
        {
            var profile = GateAccess.ProfileOf(u, ship) with { Laden = false };
            foreach (var gid in u.FindRoute(station.Sector, to.Sector, profile) ?? new List<GateId>())
            {
                var pass = GateAccess.Evaluate(u, u.Gates[gid], profile, null);
                if (pass.Toll > 0f) GateAccess.PayToll(u, ship, u.Gates[gid], pass);
            }
        }
        // Das Schiff bleibt hier angedockt; die Person steigt am Ziel zu Fuß in der Station aus (kein automatisches Übernehmen, kein automatisches
        // Leihschiff): Sie wählt im Dock-Menü der Zielstation selbst — wartendes eigenes Schiff (Hangar) oder Leihschiff (Dienst „rental“).
        PlayerFleet.Dock(u, ship, station);
        u.PlayerAtStation = to.Id;
        u.PlayerSector = to.Sector;
        string arrive = q.WaitingShip is { } waiting
            ? $"Dort wartet {OrderMove.ShipName(u, waiting.Id)} — nimm es im Hangar."
            : $"Nimm dort ein Schiff: Hangar oder Leihschiff ({u.Rules.RentalUpfront:0} Cr).";
        u.Events.Publish(new TaxiRide(station.Id, to.Id, q.Price, q.Ticks));
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Ride, default, 1f, -q.Price));
        return new ServiceResult(true, q.Ticks, -q.Total, $"Taxi nach {to.Name} ({q.Breakdown(u.Rules)}, {q.Ticks / Simulation.SimClock.TicksPerSecond:0} s). Du stehst zu Fuß in {to.Name}. {arrive}");
    }

    static ServiceResult Fail(string m) => new(false, 0f, 0f, m);
}

/// <summary>
/// Phase 12, Dienst „rental“ (SCHIFFSSLOTS.md: Leihschiff, Standardmodell gegen Gebühr und Pfand, damit der Spieler nie ohne Schiff festsitzt). Angebote:
/// <c>rent:1</c> (Leihschiff <see cref="EconomyRules.RentalModel"/> mieten, wird aktiv) und <c>return:&lt;ShipId&gt;</c> (Leihschiff hier zurückgeben;
/// Miete je angefangenem Tag aus dem Pfand, Rest zurück). Strenge Schlüssel.
/// </summary>
public sealed class RentalService : IStationService
{
    public const string ServiceKey = "rental";
    public const string Rent = "rent";
    public const string Return = "return";

    public string Key => ServiceKey;
    public bool IsAvailable(Universe u, Station station) => !station.IsBuildSite && u.ShipModels.ContainsKey(u.Rules.RentalModel);

    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var list = new List<ServiceOffer>();
        string model = u.ShipModels.TryGetValue(u.Rules.RentalModel, out var m) ? m.Name : u.Rules.RentalModel;
        list.Add(new ServiceOffer($"{Rent}:1", $"Leihschiff {model} mieten ({u.Rules.RentalFee:0} Cr Gebühr + {u.Rules.RentalDeposit:0} Cr Pfand + {u.Rules.RentalPerDay:0} Cr erster Tag, danach {u.Rules.RentalPerDay:0} Cr/Tag)",
            Hangar.RentalUpfront(u.Rules), Hangar.RentIssue(u, station, ship) is null ? 1f : 0f, default, 0f, 0f, Hangar.RentIssue(u, station, ship)));
        // Runde 38a (Handytest: „Zurückgeben“ ausgegraut ohne Grund): der Grund steht im Angebot (Note) und damit unter der Taste
        foreach (var s in u.Ships.Values.Where(s => s.IsPlayer && s.IsRental).OrderBy(s => s.Id.Value))
            list.Add(new ServiceOffer($"{Return}:{s.Id.Value}", $"Leihschiff #{s.Id.Value} zurückgeben ({Hangar.RentalDays(u, s)} Tag(e))",
                Hangar.RentalDays(u, s) * u.Rules.RentalPerDay, Hangar.ReturnIssue(u, station, s) is null ? 1f : 0f, default, 0f, 0f, Hangar.ReturnIssue(u, station, s)));
        return list;
    }

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!Keys.TryParse(request.Offer, new[] { Rent, Return }, out var action, out int v)) return Fail($"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station)) return Fail("Kein Leihdienst an dieser Station.");
        if (!ship.IsPlayer) return Fail("Nur für den Spieler.");
        if (action == Rent)
        {
            if (v != 1) return Fail($"Unbekanntes Angebot '{request.Offer}'.");
            float before = u.WalletOf(ship).Credits;
            if (Hangar.Rent(u, station, ship, out var msg) is not { } rented) return Fail(msg);
            u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Rent, default, 1f, -Hangar.RentalUpfront(u.Rules)));
            return new ServiceResult(true, 1f, u.WalletOf(rented).Credits - before, msg);
        }
        if (!u.Ships.TryGetValue(new ShipId(v), out var target)) return Fail("Unbekanntes Leihschiff.");
        float b = u.WalletOf(target).Credits;
        var wallet = u.WalletOf(target);
        if (!Hangar.ReturnRental(u, station, target, out var rmsg)) return Fail(rmsg);
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Return, default, 1f, wallet.Credits - b));
        return new ServiceResult(true, 1f, wallet.Credits - b, rmsg);
    }

    static ServiceResult Fail(string m) => new(false, 0f, 0f, m);
}
