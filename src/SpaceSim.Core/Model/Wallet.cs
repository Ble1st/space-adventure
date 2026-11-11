namespace SpaceSim.Core.Model;

/// <summary>
/// Eine Kasse: alles, worauf Handel, Dienste und Gebühren Credits buchen. NPC-Schiffe, deren Besitzer nicht über eine gemeinsame Kasse
/// wirtschaftet, sind ihre eigene (Betriebs-)Kasse (<see cref="Ship.Credits"/>); Schiffe des Spielers buchen auf die <see cref="Treasury"/>
/// ihres Besitzers. Aufrufer holen die Kasse immer über <see cref="Universe.WalletOf(Ship)"/> und nie direkt über das Schiff.
/// </summary>
public interface ICreditAccount
{
    /// <summary>Kontostand; Buchungen sind einfache Zu-/Abschläge (<c>account.Credits -= cost</c>), dieselbe Gleitkomma-Arithmetik wie bisher.</summary>
    float Credits { get; set; }
}

/// <summary>
/// Kasse eines Akteurs (Phase 9, POLITIK.md „Fraktionskasse“): beim Spieler sein ganzes Geld (Phase 5b „Spielerkasse“, Geld hängt an der Person),
/// bei Fraktionen die Staatskasse, gespeist aus den Vermögensabgaben ihrer Mitglieder (<see cref="Simulation.UpkeepSystem"/>) und ausgegeben für
/// Stationsbau (Zukauf, <see cref="Simulation.Investors"/>).
/// </summary>
public sealed class Treasury : ICreditAccount
{
    public float Credits { get; set; }
}
