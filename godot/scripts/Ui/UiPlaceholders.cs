using System.Linq;

namespace SpaceAdventure.Ui;

/// <summary>
/// Sammelstelle für ALLE erfundenen Anzeigewerte der Oberfläche (Platzhalter). Nichts davon stammt aus der
/// Simulation und nichts wird gespeichert: noch nicht gebaute Bereiche fehlen im Core und werden hier als gedämpfte
/// Platzhalter geführt. Schild und Leistung sind seit Phase 23 echt (Core: <c>Ship.ShieldNow</c>, <c>ShipStats</c>, <c>Combat.ShipSystems</c>).
/// Echte Werte (Credits, Hülle, Fracht, Sektoren, Stationen …) lesen die Bildschirme immer aus dem Core.
/// </summary>
public static class UiPlaceholders
{
	/// <summary>Beschriftung für noch nicht echte Anzeigen.</summary>
	public const string Tag = "Platzhalter";
	/// <summary>Beschriftung für noch nicht gebaute Bereiche.</summary>
	public const string Soon = "Bald";

	// ── Dock-Bereiche (UI.md Abschnitt 3): Texte für Bereiche, deren Dienst der Core noch nicht kennt ──

	/// <summary>Erklärtext je Platzhalter-Bereich des Andock-Menüs (Schlüssel = Bereichs-Id, siehe DockAreas).</summary>
	public static string DockAreaText(string areaId) => areaId switch
	{
		"repair" => "Reparatur und Tanken gibt es nur an Stationen mit dem passenden Dienst: Treibstoff an Treibstoffraffinerien, Reparatur an Panzerwerken, Werften, Hauptquartieren, Sektor-HQs und Polizeiwachen (verbraucht Hüllenplatten und Elektronik aus dem Stationslager; Notreparatur auf Schuld, wenn das Schiff nicht mehr funktioniert). Diese Station bietet es nicht an oder ihr Lager ist gerade leer.",
		"outfitter" => "Ausrüster gibt es an Werften und Handelsstationen: Module aus dem Stationslager ein- und ausbauen. Diese Station führt keine Module.",
		"shipyard" => "Schiffe kaufen kannst du an Werften (alle Klassen) und an Handelsstationen (kleine Schiffe der Skiff-Klasse). Diese Station verkauft keine Schiffe.",
		"taxi" => "Taxi gibt es an Verkehrsknoten (Werft, Handelsstation, Raffinerie, Lagerhaus, Hauptquartier, Sektor-HQ): Du reist allein zu einer Station, an der ein eigenes Schiff wartet oder ein Leihschiff zu haben ist. Diese Station hat keinen Taxistand.",
		_ => "Dieser Bereich ist noch nicht gebaut.",
	};

	/// <summary>Platzhalter für Preisverlauf-Trends im Handel (der Core speichert keine Preis-Historie).</summary>
	public const string TrendSoon = "Preisverlauf: " + Soon;   // Runde 39: „Rückgängig“ im Handel ist echt (TradeJournal im Core), dieser Platzhalter bleibt
	// ── Kommandozentrale (UI.md Abschnitt 4) ──
	// Runde 40: Posteingang und Diplomatie sind echt (Core: CommsSystem, CommsActions) — die Platzhalter
	// CommsMessages/CommsUnread/FactionEvents sind entfallen; der Zähler liest SpaceSim.Core.Comms.Comms.Unread(u),
	// „Letzte Ereignisse“ einer Fraktion liest deren Nachrichten aus dem Posteingang.

	// ── Meine Fraktion ──
	// Phase 9: Größe, Fortschritt, Mitgliedschaft, Beziehungen und Persönlichkeit sind echt (Akteure im Core); hier bleibt der Rest.

	// Runde 38: „Einnahmen/Tag“ ist echt (PlayerIncome aus Ereignissen), Beitreten/Gründen entfallen (der Spieler ist von Anbeginn eine eigene Fraktion).
	// Ausbau Punkt 9: Büro, Fehde und Firmensitz sind echt (MyFactionScreen, Fähigkeiten und Karte „Sektorpolitik“) — der Platzhalter entfällt.
}
