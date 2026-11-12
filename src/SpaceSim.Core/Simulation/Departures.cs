using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Phase 22: Der einzige Weg, auf dem die Simulation ein Schiff oder eine Station aus der Welt nimmt. Jeder Abgang trägt einen der drei Verfallsgründe
/// (<see cref="DepartureReason.Destroyed"/>, <see cref="DepartureReason.Bankrupt"/>, <see cref="DepartureReason.Unprofitable"/>) oder ist eine Entscheidung der
/// Spielerperson (<see cref="DepartureReason.Sold"/>, <see cref="DepartureReason.Returned"/>). Veröffentlicht zuerst <see cref="ShipDeparted"/> bzw.
/// <see cref="StationDeparted"/> (Objekt existiert noch), dann entfernt <see cref="Universe.RemoveShip"/>/<see cref="Universe.RemoveStation"/> mit dem
/// Grund-Schlüssel (<see cref="DepartureNames.Key"/>). Verfallsregeln berühren nie Spielerbesitz.
/// </summary>
public static class Departures
{
    public static void RemoveShip(Universe u, Ship ship, DepartureReason reason, string detail)
    {
        if (!u.Ships.ContainsKey(ship.Id)) return;
        u.Events.Publish(new ShipDeparted(ship.Id, ship.Role, ship.Owner, ship.Transit?.ToSector ?? ship.Sector, reason, detail));
        u.RemoveShip(ship.Id, DepartureNames.Key(reason));
    }

    public static void RemoveStation(Universe u, Station st, DepartureReason reason, string detail)
    {
        if (!u.Stations.ContainsKey(st.Id)) return;
        u.Events.Publish(new StationDeparted(st.Id, st.TypeKey, st.Name, st.Owner, st.Sector, reason, detail));
        u.RemoveStation(st.Id, DepartureNames.Key(reason));
    }

    /// <summary>
    /// Pleite eines KI-Schiffs (Grund b): wirtschaftliches KI-Schiff (kein Bauschiff einer Fraktion), ohne Reise und Auftrag, seit
    /// <see cref="EconomyRules.ShipBankruptTicks"/> untätig, Betriebsgeld unter <see cref="EconomyRules.ShipBankruptCredits"/> und — mit
    /// <see cref="EconomyRules.BankruptNeedsEmptyHold"/> — keine Fracht an Bord. Rein.
    /// </summary>
    public static bool ShipBankrupt(Universe u, Ship ship)
    {
        if (!ship.IsEconomyAi || ship.Transit is not null || ship.Order is not null || ship.IdleSince < 0 || u.Fights.IsEngaged(ship.Id)) return false;
        if (FactionBuilderSystem.IsFactionBuilder(u, ship)) return false;
        if (u.Tick - ship.IdleSince <= u.Rules.ShipBankruptTicks || ship.Credits >= u.Rules.ShipBankruptCredits) return false;
        bool hasCargo = ship.Cargo.Values.Any(v => v > 0.001f);
        return !(u.Rules.BankruptNeedsEmptyHold && hasCargo);
    }

    /// <summary>
    /// Länger nicht rentabel (Grund c) für ein wirtschaftliches KI-Schiff: Händler oder Schürfer, gerade ohne Reise, Auftrag und Fracht (zwischen zwei Runden),
    /// Messung nach <see cref="Profitability.Unprofitable"/>. Rein.
    /// </summary>
    public static bool ShipUnprofitable(Universe u, Ship ship) =>
        ship.IsEconomyAi && ship.Role is ShipRole.Trader or ShipRole.Miner && !FactionBuilderSystem.IsFactionBuilder(u, ship) && !u.Fights.IsEngaged(ship.Id)
        && ship.Transit is null && ship.Order is null && !ship.Cargo.Values.Any(v => v > 0.001f)
        && Profitability.Unprofitable(u, ship.Profit);

    /// <summary>
    /// Toast-Text, wenn eine Station wegen (b) oder (c) aufgegeben wird: „Station aufgegeben: Name — pleite“ bzw. „— länger nicht rentabel“ (Baustelle:
    /// „Baustelle aufgegeben …“); null für andere Gründe. Rein.
    /// </summary>
    public static string? StationToast(StationDeparted e)
    {
        if (e.Reason is not (DepartureReason.Bankrupt or DepartureReason.Unprofitable)) return null;
        bool site = e.Detail.StartsWith("site", StringComparison.Ordinal);
        string why = e.Reason == DepartureReason.Bankrupt ? "pleite" : site ? "ohne Fortschritt" : "länger nicht rentabel";
        return $"{(site ? "Baustelle" : "Station")} aufgegeben: {e.Name} — {why}";
    }
}
