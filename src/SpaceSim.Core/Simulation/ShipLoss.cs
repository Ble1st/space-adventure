using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 25 (KAMPF.md 4.3, Entscheidung 1): Verlust des aktiven Schiffs. Schiff und Fracht sind weg (Abgang über <see cref="Departures"/>, Grund
/// „zerstört“), das Geld bleibt bei der Person (Spielerkasse). Die Person landet an der nächsten erreichbaren <b>eigenen</b> Station, sonst an der nächsten
/// <b>neutralen</b> Station (Besitzer ihr gegenüber nicht feindlich, dort nicht gesucht ab <see cref="EconomyRules.TradeRefusalWantedLevel"/>, mit Hangar);
/// gibt es auch die nicht, an der nächsten Station mit Hangar überhaupt — niemals handlungsunfähig. Dort wechselt sie in ein eigenes Schiff, das dort liegt,
/// sonst mietet sie ein Leihschiff (mit Leihdienst und genug Geld regulär), sonst bekommt sie ein Notfall-Leihschiff ohne Gebühr und Pfand
/// (<see cref="EconomyRules.EmergencyRentalModel"/>), dessen Miete je Tag wie bei jedem Leihschiff läuft.
/// </summary>
public static class ShipLoss
{
    /// <summary>Wie die Person nach dem Verlust weiterfliegt.</summary>
    public const string OwnShip = "own", Rented = "rented", Emergency = "emergency";

    static bool HasHangar(Universe u, Station st) =>
        !st.IsBuildSite && u.StationTypes.TryGetValue(st.TypeKey, out var t) && t.Services.Contains(HangarService.ServiceKey) && !StationProtection.IsHideout(u, st);

    /// <summary>Ist die Station für die Landung neutral (Besitzer nicht feindlich, dort nicht gesucht)?</summary>
    public static bool IsNeutral(Universe u, Station st)
    {
        var faction = u.FactionOf(st);
        if (Relations.Effective(u, st.Owner, u.PlayerActor) <= u.Rules.HostileRelation) return false;
        int refuse = u.Rules.TradeRefusalWantedLevel > 0 ? u.Rules.TradeRefusalWantedLevel : 3;
        return faction is not { } f || Wanted.LevelOf(u, u.PlayerActor, f) < refuse;
    }

    /// <summary>
    /// Landestation nach dem Verlust eines Schiffs in <paramref name="sector"/> bei <paramref name="pos"/> (rein): eigene Station, sonst neutrale, sonst jede mit
    /// Hangar, sonst irgendeine — jeweils die nächste erreichbare (wenigste Sprünge, dann Abstand im Sektor bzw. Id). null nur ohne jede Station.
    /// </summary>
    public static Station? LandingStation(Universe u, SectorId sector, Vec2 pos) => Landing(u, sector, pos)?.Station;

    /// <summary>Phase 25 (Korrektur): Wahl der Landestation mit Begründung — erreichbar (Route mit dem Profil der Person, ohne geschlossene Tore) und neutral.</summary>
    public sealed record LandingChoice(Station Station, bool Reachable, bool Neutral, bool Own);

    /// <summary>Wie <see cref="LandingStation"/>, mit der Angabe, ob die Station erreichbar und neutral bzw. eigen ist (für die Meldung).</summary>
    public static LandingChoice? Landing(Universe u, SectorId sector, Vec2 pos)
    {
        var profile = MobilityProfile.For(u.PlayerActor, false, 0f);
        var reachable = new List<(Station St, int Hops, float Dist)>();
        var all = new List<(Station St, int Hops, float Dist)>();
        foreach (var st in u.StationsById())
        {
            if (st.IsBuildSite) continue;
            float dist = st.Sector == sector ? Vec2.Distance(st.Position, pos) : float.MaxValue;
            int hops = st.Sector == sector ? 0 : u.FindRoute(sector, st.Sector, profile)?.Count ?? -1;
            all.Add((st, hops < 0 ? int.MaxValue : hops, dist));
            if (hops >= 0) reachable.Add((st, hops, dist));
        }
        static Station? Best(IEnumerable<(Station St, int Hops, float Dist)> list) =>
            list.OrderBy(x => x.Hops).ThenBy(x => x.Dist).ThenBy(x => x.St.Id.Value).Select(x => x.St).FirstOrDefault();
        if (Best(reachable.Where(x => x.St.Owner == u.PlayerActor && HasHangar(u, x.St))) is { } own) return new(own, true, true, true);
        if (Best(reachable.Where(x => HasHangar(u, x.St) && IsNeutral(u, x.St))) is { } neutral) return new(neutral, true, true, false);
        if (Best(reachable.Where(x => HasHangar(u, x.St))) is { } any) return new(any, true, false, false);
        // letzter Ausweg (nie handlungsunfähig): über nicht passierbare Tore hinweg — erst eigene/neutrale, dann jede mit Hangar, dann jede
        var far = Best(all.Where(x => x.St.Owner == u.PlayerActor && HasHangar(u, x.St))) ?? Best(all.Where(x => HasHangar(u, x.St) && IsNeutral(u, x.St)))
                  ?? Best(all.Where(x => HasHangar(u, x.St))) ?? Best(all);
        return far is null ? null : new(far, false, far.Owner == u.PlayerActor || IsNeutral(u, far), far.Owner == u.PlayerActor);
    }

    /// <summary>Phase 25 (Korrektur): Hinweis, wo die Strafe zu zahlen ist (Station mit Gericht der Fraktion, bei der die Person gesucht wird), null = nicht gesucht dort.</summary>
    public static string? FineHint(Universe u, Station landed)
    {
        if (u.FactionOf(landed) is not { } f || Wanted.LevelOf(u, u.PlayerActor, f) <= 0) return null;
        var court = u.StationsById().FirstOrDefault(x => u.FactionOf(x) == f && !x.IsBuildSite && u.StationTypes.TryGetValue(x.TypeKey, out var t)
            && t.Services.Contains(CourtService.ServiceKey));
        string fname = u.Factions.TryGetValue(f, out var fa) ? fa.Name : "der Fraktion";
        return court is null ? $"Du wirst bei {fname} gesucht." : $"Du wirst bei {fname} gesucht — die Strafe zahlst du in der Behörde von {court.Name}.";
    }

    /// <summary>
    /// Das aktive Schiff ist verloren: Person landen, in ein eigenes oder ein Leihschiff setzen (aktiv), dann das alte Schiff über <see cref="Departures"/>
    /// entfernen. Veröffentlicht <see cref="PlayerShipLost"/>. Liefert das neue aktive Schiff (null nur, wenn es keine Station gibt — dann bleibt alles, wie es war).
    /// </summary>
    public static Ship? LoseActive(Universe u, Ship lost, string detail)
    {
        var (sector, pos) = ThreatMap.Where(u, lost);
        var landing = Landing(u, sector, pos);
        if (landing is null) return null;
        var st = landing.Station;
        // Phase 25 (Korrektur): ein verlorenes Leihschiff — Pfand verfällt an die Verleihstation, offene Miete aus der Kasse
        var (forfeit, rentPaid) = Hangar.SettleLostRental(u, lost);
        string how;
        Ship? next = PlayerFleet.ParkedAt(u, st).Where(s => s.Id != lost.Id).OrderBy(s => s.IsRental ? 1 : 0).ThenBy(s => s.Id.Value).FirstOrDefault();
        if (next is not null)
        {
            PlayerFleet.Activate(u, next, st.Id);
            how = OwnShip;
        }
        else if (u.StationTypes.TryGetValue(st.TypeKey, out var type) && type.Services.Contains(RentalService.ServiceKey)
                 && u.ShipModels.ContainsKey(u.Rules.RentalModel) && Hangar.RentIssue(u, st, lost, lost.Id) is null)
        {
            next = Hangar.Rent(u, st, lost, out _, lost.Id);
            how = Rented;
        }
        else
        {
            next = EmergencyRental(u, st, lost);
            how = Emergency;
        }
        if (next is null) return null;
        string model = u.ShipModels.TryGetValue(next.ModelKey, out var m) ? m.Name : "Schiff";
        string msg = how switch
        {
            OwnShip => $"Dein Schiff ist verloren — samt Fracht. Du bist an {st.Name} gelandet und fliegst jetzt {model} #{next.Id.Value}.",
            Rented => $"Dein Schiff ist verloren — samt Fracht. An {st.Name} hast du ein Leihschiff ({model}) gemietet.",
            _ => $"Dein Schiff ist verloren — samt Fracht. {st.Name} stellt dir ein Notfall-Leihschiff ({model}); die Miete läuft je Tag.",
        };
        if (!landing.Reachable) msg += " Kein erreichbarer Hafen — du wurdest über gesperrte Tore hinweg dorthin gebracht.";
        if (forfeit > 0f || rentPaid > 0f) msg += $" Leihschiff verloren: Pfand {forfeit:0} Cr verfällt, Miete {rentPaid:0} Cr.";
        if (FineHint(u, st) is { } fine) msg += " " + fine;
        Departures.RemoveShip(u, lost, DepartureReason.Destroyed, detail);
        u.Events.Publish(new PlayerShipLost(lost.Id, st.Id, next.Id, how, msg));
        return next;
    }

    /// <summary>Notfall-Leihschiff: ohne Gebühr und Pfand, auch ohne Leihdienst und ohne Geld; die Miete je Tag läuft (Rückgabe wie jedes Leihschiff).</summary>
    static Ship? EmergencyRental(Universe u, Station st, Ship lost)
    {
        string key = u.Rules.EmergencyRentalModel.Length > 0 ? u.Rules.EmergencyRentalModel : u.Rules.RentalModel;
        if (!u.ShipModels.TryGetValue(key, out var model) || !u.ShipClasses.TryGetValue(model.ClassKey, out var cls))
        {
            model = u.ShipModels.Values.OrderBy(m => m.Key, StringComparer.Ordinal).FirstOrDefault(m => u.ShipClasses.ContainsKey(m.ClassKey))!;
            if (model is null) return null;
            cls = u.ShipClasses[model.ClassKey];
        }
        var ship = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Player, Controller.Player, lost.Owner, st, 0f);
        ship.RentedFrom = st.Id;
        ship.RentedSince = u.Tick;
        ship.RentalDeposit = 0f;
        ship.HomeBase = st.Id;
        u.AddShip(ship);
        u.Events.Publish(new ShipRented(ship.Id, st.Id, 0f, 0f));
        PlayerFleet.Activate(u, ship, st.Id);
        return ship;
    }
}
