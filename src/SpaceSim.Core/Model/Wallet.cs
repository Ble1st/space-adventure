namespace SpaceSim.Core.Model;

/// <summary>
/// Eine Kasse: alles, worauf Handel, Dienste und Gebühren Credits buchen. Phase 5b: NPC-Einzelschiffe sind ihre eigene
/// Kasse (<see cref="Ship.Credits"/>, implizite Akteure), Spielerschiffe buchen auf die <see cref="PlayerWallet"/> der Person.
/// Ab Phase 9 ist das die Kasse des Akteurs, dem ein Schiff gehört; Aufrufer holen sie immer über
/// <see cref="Universe.WalletOf(Ship)"/> und nie direkt über das Schiff.
/// </summary>
public interface ICreditAccount
{
    /// <summary>Kontostand; Buchungen sind einfache Zu-/Abschläge (<c>account.Credits -= cost</c>), dieselbe Gleitkomma-Arithmetik wie bisher.</summary>
    float Credits { get; set; }
}

/// <summary>
/// Kasse des Spielers (der Person, nicht des Schiffs; POLITIK.md, „Spielerkasse“). Zwischenstufe bis zum Akteurmodell
/// (Phase 9): dann wird daraus die Kasse des Spielerakteurs. Geld hängt an der Person, Fracht und Treibstoff am Schiff.
/// </summary>
public sealed class PlayerWallet : ICreditAccount
{
    public float Credits { get; set; }
    /// <summary>Fraktion, der die Person angehört (Besitzer-Fraktion; heute die des Startschiffs).</summary>
    public FactionId Faction;
}
