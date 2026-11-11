using SpaceSim.Core.Model;

namespace SpaceSim.Core.Services;

/// <summary>Ein Angebot eines Stationsdienstes für ein Schiff.</summary>
/// <param name="Key">Eindeutiger Schlüssel des Angebots innerhalb des Dienstes, z.B. "buy:3"; identifiziert es in einer <see cref="ServiceRequest"/>.</param>
/// <param name="Text">Anzeige-Text für die Oberfläche.</param>
/// <param name="Price">Preis je Einheit (beim Angebot "alles verkaufen": erwarteter Erlös der gesamten Fracht).</param>
/// <param name="Amount">
/// Jetzt verfügbare Menge: Kauf = so viel das Schiff höchstens nehmen kann (<see cref="Trading.TradeActions.MaxBuyable"/>);
/// Verkauf = so viel die Station jetzt abnimmt (an Bord, begrenzt durch Stationskasse und <see cref="Station.AcceptableUnits"/>);
/// bei "alles verkaufen" die gesamte Fracht.
/// </param>
/// <param name="Good">Betroffene Ware; bei Angeboten ohne Ware (z.B. "alles verkaufen") <c>default</c>.</param>
/// <param name="Stock">Lagerbestand der Station für <paramref name="Good"/>; 0 bei Angeboten ohne Ware.</param>
/// <param name="Held">Menge von <paramref name="Good"/> an Bord des Schiffs; 0 bei Angeboten ohne Ware.</param>
/// <param name="Note">Phase 16: kurzer Hinweis zum Angebot, z. B. beim Verkauf der Grund, warum die Station nichts oder nur einen Teil annimmt
/// ("Stellt die Ware selbst her", "nimmt noch 12"); null = kein Hinweis.</param>
public readonly record struct ServiceOffer(string Key, string Text, float Price, float Amount, GoodId Good, float Stock, float Held, string? Note = null);

/// <summary>Anfrage an einen Stationsdienst.</summary>
/// <param name="Offer">Schlüssel des auszuführenden Angebots (<see cref="ServiceOffer.Key"/>), z.B. "buy:3" oder "sell_all"; der Dienst prüft ihn streng.</param>
/// <param name="Good">Nur für Dienste, die die Ware nicht im Schlüssel führen; der Handelsdienst liest sie aus dem Schlüssel und ignoriert dieses Feld.</param>
/// <param name="Units">Gewünschte Menge; ≤ 0 = so viel wie möglich.</param>
public readonly record struct ServiceRequest(string Offer, GoodId Good, float Units);

/// <summary>Ergebnis eines Dienst-Aufrufs.</summary>
/// <param name="Success">Angebot wurde ausgeführt; false = nichts bewegt, kein Event, kein Zustandswechsel.</param>
/// <param name="Units">Tatsächlich bewegte Einheiten.</param>
/// <param name="Credits">Credit-Veränderung des Schiffs (negativ = Zahlung des Schiffs).</param>
/// <param name="Message">Kurzer Statustext für die Oberfläche.</param>
public readonly record struct ServiceResult(bool Success, float Units, float Credits, string Message);

/// <summary>
/// Ein Dienst, den eine Station anbietet (Handel, später Reparatur, Tankstelle, Ausrüster usw.).
/// Registrierung über <see cref="StationServices"/>; die Dienste selbst ändern nichts an Persistenz oder Hash.
/// </summary>
public interface IStationService
{
    /// <summary>Eindeutiger Schlüssel des Dienstes, z.B. "trade".</summary>
    string Key { get; }

    /// <summary>Ist der Dienst an dieser Station verfügbar?</summary>
    bool IsAvailable(Universe u, Station station);

    /// <summary>Aktuelle Angebote des Dienstes für dieses Schiff.</summary>
    IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship);

    /// <summary>
    /// Führt ein Angebot aus. Veröffentlicht <see cref="Events.ServicePerformed"/> nur bei Erfolg (mindestens eine Einheit bewegt,
    /// bei "alles verkaufen" Erlös &gt; 0); ein fehlgeschlagenes Angebot ändert keinen Zustand und löst kein Event aus.
    /// </summary>
    ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request);
}
