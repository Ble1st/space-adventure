using System.Linq;

namespace SpaceAdventure.Ui;

/// <summary>
/// Sammelstelle für ALLE erfundenen Anzeigewerte der Oberfläche (Platzhalter). Nichts davon stammt aus der
/// Simulation und nichts wird gespeichert: noch nicht gebaute Bereiche (Comms-Postfach, Aufträge …) fehlen im Core und werden hier als gedämpfte
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
		"missions" => "Aufträge: Offene Missionen der Station und Fraktionen. Kommt mit Phase 9+ (Akteure, Missionen).",
		_ => "Dieser Bereich ist noch nicht gebaut.",
	};

	/// <summary>Platzhalter für Preisverlauf-Trends im Handel (der Core speichert keine Preis-Historie).</summary>
	public const string TrendSoon = "Preisverlauf: " + Soon;   // Runde 39: „Rückgängig“ im Handel ist echt (TradeJournal im Core), dieser Platzhalter bleibt
	// ── Kommandozentrale (UI.md Abschnitt 4) ──

	/// <summary>Eine Platzhalter-Nachricht im Comms-Postfach (Texte und Zahlen zentral hier, nie im Code verstreut).</summary>
	public sealed record CommsMessage(string From, string Subject, string Body, bool IsNew, CommsAnswer[] Answers);
	/// <summary>Antwortmöglichkeit mit Folgenvorschau (Ruf/Kosten/Chance) – Platzhalter.</summary>
	public sealed record CommsAnswer(string Text, string EffectPreview);

	/// <summary>Alle Platzhalter-Nachrichten des Comms-Postfachs (Vorschau, nichts davon ist echt oder gespeichert).</summary>
	public static CommsMessage[] CommsMessages => new[]
	{
		new CommsMessage("Terranische Union · Zollbehörde", "Zollbescheid Delta-Route",
			"Pilot, deine Lieferungen nach Delta werden künftig mit 4 % Zoll belegt. Die Behörde erwartet eine Antwort.",
			true,
			new[]
			{
				new CommsAnswer("Zoll akzeptieren", "Kosten: 4 % Zoll · Ruf −1"),
				new CommsAnswer("Einspruch einlegen", "Chance 40 % · bei Misserfolg Ruf −3"),
				new CommsAnswer("Ignorieren", "Angebot verfällt in 2 Tagen · Ruf −2"),
			}),
		new CommsMessage("Freie Händler · Marktbüro", "Angebot: Handelsvertrag",
			"Die Freien Händler bieten dir feste Ankaufmengen zu garantierten Preisen – gegen eine kleine Gebühr.",
			true,
			new[]
			{
				new CommsAnswer("Vertrag annehmen", "Gebühr 200 Cr · Ruf +4 · Pflicht: 40 Einheiten/Woche"),
				new CommsAnswer("Tarif verhandeln", "Chance 55 % · bei Misserfolg Ruf −2"),
				new CommsAnswer("Ablehnen", "kein Ruf-Verlust · Angebot verfällt"),
			}),
		new CommsMessage("Werft Delta · Kundendienst", "Wartungsangebot",
			"Ihre Techniker bieten einen Wartungsvertrag für deine Flotte an – inklusive kostenloser Reparaturen an der Werft.",
			false,
			new[]
			{
				new CommsAnswer("Schutzvertrag anfragen", "120 Cr/Tag · Patrouille deiner Stationen"),
				new CommsAnswer("Später", "kein Effekt"),
			}),
	};

	/// <summary>Zähler ungelesener Platzhalter-Nachrichten am Hamburger-Menü und im Comms-Bereich.</summary>
	public static int CommsUnread => CommsMessages.Count(m => m.IsNew);

	/// <summary>Ereignisliste einer Fraktion (Platzhalter; Politik und Ereignisse kommen ab Phase 10).</summary>
	public static string[] FactionEvents => new[] { "Keine Ereignisse aufgezeichnet – " + Tag };

	// ── Meine Fraktion ──
	// Phase 9: Größe, Fortschritt, Mitgliedschaft, Beziehungen und Persönlichkeit sind echt (Akteure im Core); hier bleibt der Rest.

	// Runde 38: „Einnahmen/Tag“ ist echt (PlayerIncome aus Ereignissen), Beitreten/Gründen entfallen (der Spieler ist von Anbeginn eine eigene Fraktion).
	/// <summary>
	/// Fähigkeiten mit Schloss nach Größenstufe (Platzhalter). Runde 38: Büro, Fehde und Firmensitz erscheinen erst, wenn der Spieler eine Station besitzt
	/// (<paramref name="hasStations"/>) — vorher kein „Bald“ für Dinge ohne Grundlage; „Zoll und Sperren“ bleibt mit Schloss, bis der Spieler einen Sektor dominiert.
	/// </summary>
	public static (string Name, bool Unlocked, string Note)[] MyFactionAbilities(bool hasStations) => hasStations
		? new[]
		{
			("Büro an eigener Station", false, "ab Kleinfirma – " + Soon),
			("Fehde erklären", false, "ab Kleinfirma – " + Soon),
			("Firmensitz", false, "ab Firma – " + Soon),
			("Zoll und Sperren", false, "ab Sektor-Dominanz – " + Soon),
		}
		: new[] { ("Zoll und Sperren", false, "ab Sektor-Dominanz – " + Soon) };   // Runde 26 (Gerätetest c05): passt in die Spalte
}
