namespace SpaceSim.Core.Model;

/// <summary>
/// Phase 22: Warum ein Schiff oder eine Station die Welt verlässt. Die KI-Wirtschaft kennt genau drei Verfallsgründe — zerstört, pleite, länger nicht
/// rentabel; Verkauf und Rückgabe sind Entscheidungen der Spielerperson (keine Verfallswege).
/// </summary>
public enum DepartureReason
{
    /// <summary>(a) Zerstört: Überfall, Gefecht, Abfangen.</summary>
    Destroyed,
    /// <summary>(b) Pleite: kein Geld, keine Fracht, kein Auftrag und nach einer Frist keine Besserung (Station: Kasse dauerhaft unter der Pleitegrenze).</summary>
    Bankrupt,
    /// <summary>(c) Länger nicht rentabel: über ein langes Fenster dauerhaft negativer Netto-Geldfluss (Dienst der Fraktion: lange ohne Nutzen und überzählig).</summary>
    Unprofitable,
    /// <summary>Spielerperson hat ihr Schiff verkauft (Hangar).</summary>
    Sold,
    /// <summary>Spielerperson hat ein Mietschiff zurückgegeben.</summary>
    Returned,
}

/// <summary>Phase 22: Schlüssel und deutsche Namen der Abgangsgründe (Ereignis-Grund, Balance-Werkzeug, Anzeige).</summary>
public static class DepartureNames
{
    /// <summary>Schlüssel für <c>ShipRemoved/StationRemoved.Reason</c>: destroyed, bankrupt, unprofitable, sold, returned.</summary>
    public static string Key(DepartureReason r) => r switch
    {
        DepartureReason.Destroyed => "destroyed",
        DepartureReason.Bankrupt => "bankrupt",
        DepartureReason.Unprofitable => "unprofitable",
        DepartureReason.Sold => "sold",
        _ => "returned",
    };

    public static string German(DepartureReason r) => r switch
    {
        DepartureReason.Destroyed => "zerstört",
        DepartureReason.Bankrupt => "pleite",
        DepartureReason.Unprofitable => "nicht rentabel",
        DepartureReason.Sold => "verkauft",
        _ => "zurückgegeben",
    };
}

/// <summary>
/// Phase 22: Dauerhafte Rentabilitätsmessung eines KI-Schiffs oder einer Station (Grund c). Gemessen wird der Netto-Geldfluss je Tick als gleitender
/// Mittelwert über ein langes Fenster (<see cref="EconomyRules.ProfitWindowTicks"/>): Vermögen = Kasse + Fracht/Lager zum Basispreis; durchgereichte
/// Beträge, die kein Geschäftsergebnis sind (Investitionen eines Schiffs in neue Schiffe/Stationen/Upgrades, Subvention und Grundeinkommen einer Station,
/// Kapitalausgleich innerhalb eines Akteurs), verschieben die Bezugsgröße <see cref="Worth"/> (<see cref="Simulation.Profitability.Transfer(ProfitMeter, float)"/>).
/// <see cref="LossTicks"/> zählt die Ticks, in denen der Mittelwert unter null lag, und wird erst über der Hysterese-Schwelle zurückgesetzt; die Uhr ruht,
/// solange eine physische Torsperre das Schiff/die Station vom Hauptteil der Karte trennt.
/// </summary>
public sealed class ProfitMeter
{
    /// <summary>Gleitender Mittelwert des Netto-Geldflusses (Credits je Tick).</summary>
    public float Rate;
    /// <summary>Vermögen beim letzten Messpunkt (Bezug der nächsten Messung).</summary>
    public float Worth;
    /// <summary>Ticks mit Mittelwert unter null (seit dem letzten Überschreiten der Hysterese-Schwelle).</summary>
    public int LossTicks;
    /// <summary>Beginn der Messung (Kauf/Bau); −1 = noch nicht gemessen. Die Schonfrist zählt ab hier.</summary>
    public long Since = -1;

    public ProfitMeter Clone() => new() { Rate = Rate, Worth = Worth, LossTicks = LossTicks, Since = Since };
}
