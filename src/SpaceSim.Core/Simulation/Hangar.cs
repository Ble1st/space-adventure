using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 12: Hangar-Regeln (SCHIFFSSLOTS.md Dienste „hangar“, „taxi“, „rental“; UI.md Abschnitt 3): wer an einer Station angedockt liegt, Liegegebühr je
/// Klasse, Wert und Verkaufspreis eines Schiffs, Leihschiffe. Reine Funktionen bzw. Buchungen über das Universum; die Dienste rufen sie auf.
/// </summary>
public static class Hangar
{
    /// <summary>Alle Schiffe, die an dieser Station angedockt liegen (nicht unterwegs), nach Id — eigene wie fremde.</summary>
    public static IReadOnlyList<Ship> DockedShips(Universe u, Station st) =>
        u.ShipsById().Where(s => s.Transit is null && s.DockedAt == st.Id).ToList();

    /// <summary>Fremde (nicht eigene) Schiffe an dieser Station.</summary>
    public static IReadOnlyList<Ship> ForeignDocked(Universe u, Station st, ActorId me) =>
        DockedShips(u, st).Where(s => s.Owner != me).ToList();

    /// <summary>Liegegebühr je Tag: Satz der Klasse, sonst nach Größe (<see cref="EconomyRules.BerthFeeBySize"/>); Schiffe ohne Modell: Satz für S.</summary>
    public static float BerthFeePerDay(Universe u, Ship ship)
    {
        var cls = u.ShipModels.TryGetValue(ship.ModelKey, out var m) ? u.ShipClasses.GetValueOrDefault(m.ClassKey) : null;
        if (cls is { BerthFee: > 0f }) return cls.BerthFee;
        string size = (cls?.Size ?? SlotSize.S).ToString();
        return u.Rules.BerthFeeBySize.TryGetValue(size, out var f) ? f : 0f;
    }

    /// <summary>Gebühr je Tag, die das Schiff dort, wo es liegt, tatsächlich zahlt (eigene Station, aktiv, unterwegs, Leihschiff: 0).</summary>
    public static float BerthFeeNow(Universe u, Ship ship) =>
        ship.IsPlayer && ship.Id != u.PlayerShip && ship.Transit is null && !ship.IsRental && u.Stations.TryGetValue(ship.DockedAt, out var st) && st.Owner != ship.Owner
            ? BerthFeePerDay(u, ship) : 0f;

    /// <summary>Liegegebühr aller Schiffe eines Akteurs je Tag (Anzeige „Meine Fraktion“).</summary>
    public static float BerthFeesPerDay(Universe u, ActorId owner) =>
        u.Ships.Values.Where(s => s.Owner == owner).Sum(s => BerthFeeNow(u, s));

    /// <summary>Materialwert eines Schiffs: Bausatz der Klasse + eingebaute Module zum Basispreis (0 ohne Modell oder ohne auflösbare Waren).</summary>
    public static float MaterialValue(Universe u, Ship ship)
    {
        if (!u.ShipModels.TryGetValue(ship.ModelKey, out var model)) return 0f;
        var needs = ShipPurchase.Materials(u, u.ShipClasses.GetValueOrDefault(model.ClassKey), ship.Fit);
        return needs?.Sum(n => n.Units * u.Goods[n.Good].BasePrice) ?? 0f;
    }

    /// <summary>
    /// Verkaufserlös: <see cref="EconomyRules.ShipSellShare"/> × Material, bewertet je Ware mit dem niedrigsten aktuellen Marktpreis aller Stationen
    /// (höchstens Basispreis). So liegt der Erlös immer unter jedem möglichen Kaufpreis – Kaufen und sofort Verkaufen bringt nie Gewinn.
    /// </summary>
    public static float SellPrice(Universe u, Ship ship)
    {
        if (!u.ShipModels.TryGetValue(ship.ModelKey, out var model)) return 0f;
        var needs = ShipPurchase.Materials(u, u.ShipClasses.GetValueOrDefault(model.ClassKey), ship.Fit);
        if (needs is null) return 0f;
        float sum = 0f;
        foreach (var n in needs)
        {
            float price = u.Goods[n.Good].BasePrice;
            foreach (var st in u.Stations.Values)
                if (st.Market.TryGetValue(n.Good, out var e) && e.Price > 0f && e.Price < price) price = e.Price;
            sum += n.Units * price;
        }
        return sum * u.Rules.ShipSellShare * DamageFactor(ship);
    }

    /// <summary>Phase 26 (Prüfung): Schadensabschlag beim Verkauf = Hüllenquote × mittlerer Zustand der eingebauten Module (einfache Regel).</summary>
    public static float DamageFactor(Ship ship)
    {
        float hull = ship.MaxHull > 0f ? Math.Clamp(ship.Hull / ship.MaxHull, 0f, 1f) : 1f;
        float sum = 0f; int n = 0;
        for (int i = 0; i < ship.Fit.Count; i++) if (ship.Fit[i] is not null) { sum += ship.ConditionOf(i); n++; }
        return hull * (n > 0 ? sum / n : 1f);
    }

    /// <summary>Warum dieses eigene Schiff hier nicht verkauft werden kann (null = es geht).</summary>
    public static string? SellIssue(Universe u, Station st, Ship ship)
    {
        if (!ship.IsPlayer) return "Nicht dein Schiff.";
        if (ship.Id == u.PlayerShip) return "Das aktive Schiff kannst du nicht verkaufen.";
        if (ship.IsRental) return "Leihschiffe gibst du am Leihdienst zurück.";
        if (ship.Transit is not null || ship.DockedAt != st.Id) return "Das Schiff liegt nicht hier.";
        if (ship.Cargo.Values.Sum() > 0.001f) return "Erst die Fracht verkaufen oder einlagern.";
        float price = SellPrice(u, ship);
        if (price <= 0f) return "Dieses Schiff hat keinen Marktwert.";
        if (st.Credits < price) return "Die Station kann den Preis nicht zahlen.";
        return null;
    }

    /// <summary>Verkauft ein eigenes Schiff an die Station (Erlös in die Kasse des Besitzers, Schiff weg). false = <see cref="SellIssue"/>.</summary>
    public static bool Sell(Universe u, Station st, Ship ship, out string message)
    {
        if (SellIssue(u, st, ship) is { } issue) { message = issue; return false; }
        float price = SellPrice(u, ship);
        st.Credits -= price;
        u.WalletOf(ship).Credits += price;
        if (ship.Command is not null) Commands.Cancel(u, ship, "verkauft");
        Departures.RemoveShip(u, ship, DepartureReason.Sold, "hangar");   // Phase 22: Entscheidung der Spielerperson, kein Verfall
        u.Events.Publish(new ShipSold(ship.Id, st.Id, price));
        message = $"Schiff #{ship.Id.Value} für {price:0} Cr verkauft.";
        return true;
    }

    // ───────── Leihschiff ─────────

    /// <summary>Kosten beim Mieten: einmalige Gebühr plus Pfand.</summary>
    public static float RentalUpfront(EconomyRules r) => r.RentalFee + r.RentalDeposit;

    /// <summary>Angefangene Miettage seit Beginn (mindestens 1).</summary>
    public static int RentalDays(Universe u, Ship ship) =>
        (int)Math.Max(1, (u.Tick - ship.RentedSince + u.Rules.DayTicks - 1) / Math.Max(1, u.Rules.DayTicks));

    /// <summary>Phase 26: noch nicht (täglich) abgerechnete Miete.</summary>
    public static float OpenRent(Universe u, Ship ship) => Math.Max(0, RentalDays(u, ship) - ship.RentPaidDays) * u.Rules.RentalPerDay;

    /// <summary>Phase 26: Schulden der Spielerperson (negative Kasse, z. B. aus laufender Leihmiete); 0 = keine.</summary>
    public static float Debt(Universe u) => MathF.Max(0f, -u.PlayerWallet.Credits);

    /// <summary>Warum hier kein Leihschiff zu haben ist (null = es geht).</summary>
    public static string? RentIssue(Universe u, Station st, Ship payer, ShipId ignore = default)
    {
        if (!payer.IsPlayer) return "Nur für den Spieler.";
        if (!u.ShipModels.ContainsKey(u.Rules.RentalModel)) return "Kein Leihmodell in dieser Welt.";
        // Phase 25 (Korrektur): ein gerade verlorenes Leihschiff zählt nicht mehr (ignore)
        if (u.Ships.Values.Count(s => s.IsPlayer && s.IsRental && s.Id != ignore) >= u.Rules.RentalMaxPerPlayer) return "Du hast schon ein Leihschiff.";
        if (u.WalletOf(payer).Credits < RentalUpfront(u.Rules)) return $"Nicht genügend Credits ({RentalUpfront(u.Rules):0} Cr nötig).";
        return null;
    }

    /// <summary>Mietet ein Leihschiff an der Station (Gebühr an die Station, Pfand hinterlegt) und macht es zum aktiven Schiff. null = <see cref="RentIssue"/>.</summary>
    public static Ship? Rent(Universe u, Station st, Ship payer, out string message, ShipId ignore = default)
    {
        if (RentIssue(u, st, payer, ignore) is { } issue) { message = issue; return null; }
        var model = u.ShipModels[u.Rules.RentalModel];
        var cls = u.ShipClasses[model.ClassKey];
        var wallet = u.WalletOf(payer);
        wallet.Credits -= RentalUpfront(u.Rules);
        st.Credits += u.Rules.RentalFee;
        var ship = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Player, Controller.Player, payer.Owner, st, 0f);
        ship.RentedFrom = st.Id;
        ship.RentedSince = u.Tick;
        ship.RentalDeposit = u.Rules.RentalDeposit;
        ship.HomeBase = st.Id;
        u.AddShip(ship);
        u.Events.Publish(new ShipRented(ship.Id, st.Id, u.Rules.RentalFee, u.Rules.RentalDeposit));
        var old = u.Ships.GetValueOrDefault(u.PlayerShip);
        if (old is not null && old.Transit is null && old.Id != ship.Id && old.Sector == st.Sector && Vec2.Distance(old.Position, st.Position) <= Universe.DockRadius * 2f)
            PlayerFleet.Dock(u, old, st);
        PlayerFleet.Activate(u, ship, st.Id);
        message = $"Leihschiff {model.Name} #{ship.Id.Value} gemietet: {u.Rules.RentalFee:0} Cr Gebühr, {u.Rules.RentalDeposit:0} Cr Pfand, {u.Rules.RentalPerDay:0} Cr je Tag.";
        return ship;
    }

    /// <summary>Warum dieses Leihschiff hier nicht zurückgegeben werden kann (null = es geht).</summary>
    public static string? ReturnIssue(Universe u, Station st, Ship ship)
    {
        if (!ship.IsRental || !ship.IsPlayer) return "Kein Leihschiff.";
        bool active = ship.Id == u.PlayerShip;
        if (!active && (ship.Transit is not null || ship.DockedAt != st.Id)) return "Das Leihschiff liegt nicht hier.";
        if (ship.Cargo.Values.Sum() > 0.001f) return "Erst die Fracht verkaufen oder einlagern.";
        if (active && ParkedOwn(u, st) is null) return "Du brauchst hier ein eigenes Schiff, in das du wechselst.";
        return null;
    }

    /// <summary>Phase 25: Notfall-Leihschiff (nach dem Verlust des aktiven Schiffs ohne Gebühr und Pfand vergeben).</summary>
    public static bool IsEmergencyRental(Ship ship) => ship.IsRental && ship.RentalDeposit <= 0f;

    /// <summary>
    /// Phase 25 (Korrektur): ein Leihschiff geht verloren — das Pfand verfällt ganz an die Verleihstation, die bis jetzt angefallene Miete über das Pfand hinaus
    /// zahlt die Kasse (notfalls ins Minus). Ereignis <see cref="RentalLost"/>. Liefert (verfallenes Pfand, aus der Kasse gezahlte Miete).
    /// </summary>
    public static (float Deposit, float Rent) SettleLostRental(Universe u, Ship ship)
    {
        if (!ship.IsRental) return (0f, 0f);
        var wallet = u.WalletOf(ship);
        float rent = OpenRent(u, ship);   // Phase 26: bereits täglich abgerechnete Tage zählen nicht doppelt
        float deposit = ship.RentalDeposit;
        float fromWallet = MathF.Max(0f, rent - deposit);
        wallet.Credits -= fromWallet;
        if (u.Stations.TryGetValue(ship.RentedFrom, out var st)) st.Credits += deposit + fromWallet;
        ship.RentalDeposit = 0f;
        u.Events.Publish(new RentalLost(ship.Id, ship.RentedFrom, deposit, fromWallet));
        return (deposit, fromWallet);
    }

    static Ship? ParkedOwn(Universe u, Station st) => PlayerFleet.ParkedAt(u, st).FirstOrDefault(s => !s.IsRental);

    /// <summary>
    /// Gibt ein Leihschiff zurück: Miete je angefangenem Tag aus dem Pfand an die Station, Rest erstattet (reicht das Pfand nicht, zahlt die Kasse den
    /// Rest, soweit sie kann). War es das aktive Schiff, wechselt der Spieler in ein eigenes Schiff an dieser Station. false = <see cref="ReturnIssue"/>.
    /// </summary>
    public static bool ReturnRental(Universe u, Station st, Ship ship, out string message)
    {
        if (ReturnIssue(u, st, ship) is { } issue) { message = issue; return false; }
        if (ship.Id == u.PlayerShip && ParkedOwn(u, st) is { } own)
        {
            PlayerFleet.Dock(u, ship, st);
            PlayerFleet.Activate(u, own, st.Id);
        }
        var wallet = u.WalletOf(ship);
        float rent = OpenRent(u, ship);   // Phase 26: bereits täglich abgerechnete Tage zählen nicht doppelt
        float fromDeposit = MathF.Min(rent, ship.RentalDeposit);
        // Phase 25 (Korrektur): ein Notfall-Leihschiff (ohne Pfand) zahlt die Miete ganz — notfalls ins Minus (Schulden bei der Verleihstation), kein
        // Gratis-Kreislauf; reguläre Leihschiffe wie bisher, soweit die Kasse reicht
        float fromWallet = IsEmergencyRental(ship) ? rent - fromDeposit : MathF.Min(rent - fromDeposit, MathF.Max(0f, wallet.Credits));
        float refund = ship.RentalDeposit - fromDeposit;
        wallet.Credits += refund - fromWallet;
        st.Credits += fromDeposit + fromWallet;
        if (ship.Command is not null) Commands.Cancel(u, ship, "zurückgegeben");
        Departures.RemoveShip(u, ship, DepartureReason.Returned, "rental");   // Phase 22: Entscheidung der Spielerperson, kein Verfall
        u.Events.Publish(new RentalReturned(ship.Id, st.Id, fromDeposit + fromWallet, refund));
        message = $"Leihschiff zurückgegeben: {RentalDays(u, ship)} Tag(e) Miete {fromDeposit + fromWallet:0} Cr, Pfand-Rest {refund:0} Cr erstattet.";
        return true;
    }
}
