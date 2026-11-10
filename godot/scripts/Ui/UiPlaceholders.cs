using System.Linq;

namespace SpaceAdventure.Ui;

/// <summary>
/// Sammelstelle für ALLE erfundenen Anzeigewerte der Oberfläche (Platzhalter). Nichts davon stammt aus der
/// Simulation und nichts wird gespeichert: Schild, Leistung und noch nicht gebaute
/// Bereiche (Comms-Zähler, Sicherheit …) fehlen im Core und werden hier als gedämpfte Platzhalter geführt.
/// Echte Werte (Credits, Hülle, Fracht, Sektoren, Stationen …) lesen die Bildschirme immer aus dem Core.
/// </summary>
public static class UiPlaceholders
{
	/// <summary>Beschriftung für noch nicht echte Anzeigen.</summary>
	public const string Tag = "Platzhalter";
	/// <summary>Beschriftung für noch nicht gebaute Bereiche.</summary>
	public const string Soon = "Bald";

	/// <summary>Schild in Prozent (Platzhalter, das Simulationsmodell kennt noch keine Schilde).</summary>
	public static float ShieldPercent => 64f;
	/// <summary>Leistungsanzeige (Platzhalter, Leistungsbilanz der Module fehlt noch).</summary>
	public static string PowerText => "9/12";
	/// <summary>Sicherheitsanzeige des Sektors (Platzhalter).</summary>
	public static string SecurityText => "▮▮▮▯";

	// ── Dock-Bereiche (UI.md Abschnitt 3): Texte für Bereiche, deren Dienst der Core noch nicht kennt ──

	/// <summary>Erklärtext je Platzhalter-Bereich des Andock-Menüs (Schlüssel = Bereichs-Id, siehe DockAreas).</summary>
	public static string DockAreaText(string areaId) => areaId switch
	{
		"repair" => "Reparatur und Tanken gibt es nur an Stationen mit dem passenden Dienst: Treibstoff an Treibstoffraffinerien, Reparatur an Panzerwerken und Werften (verbraucht Hüllenplatten aus dem Stationslager). Diese Station bietet es nicht an oder ihr Lager ist gerade leer.",
		"outfitter" => "Ausrüster gibt es an Werften und Handelsstationen: Module aus dem Stationslager ein- und ausbauen. Diese Station führt keine Module.",
		"shipyard" => "Schiffe kaufen kannst du an Werften (alle Klassen) und an Handelsstationen (kleine Schiffe der Skiff-Klasse). Diese Station verkauft keine Schiffe.",
		"taxi" => "Taxi: Du reist allein zu einer anderen Station, dein Schiff bleibt im Hangar (Liegegebühr an fremden Stationen je Schiffsklasse). Kommt mit Phase 12 (Hangar, Taxi, Leihschiff).",
		"missions" => "Aufträge: Offene Missionen der Station und Fraktionen. Kommt mit Phase 9+ (Akteure, Missionen).",
		_ => "Dieser Bereich ist noch nicht gebaut.",
	};

	/// <summary>Platzhalter für „Rückgängig“ im Handel (der Core führt keine Transaktionshistorie).</summary>
	public const string UndoSoon = "Rückgängig kommt, sobald der Core eine Transaktionshistorie führt – " + Soon;
	/// <summary>Platzhalter für Preisverlauf-Trends im Handel (der Core speichert keine Preis-Historie).</summary>
	public const string TrendSoon = "Preisverlauf: " + Soon;
	/// <summary>Platzhalter für Ruf, Zoll, Sperrstufe und Hinweise der Station-Info (Politik fehlt noch, Phase 10/11).</summary>
	public const string PoliticsSoon = "Ruf, Zoll, Sperrstufe und Hinweise: " + Tag + " (" + Soon + ", Politik ab Phase 10)";
	/// <summary>Platzhalter für fremde/weitere eigene Schiffe und deren Aktionen im Hangar (kommt mit Phase 12).</summary>
	public const string HangarSoon = "Fremde Schiffe, Befehle, Verkaufen, Heim schicken: " + Tag + " (" + Soon + ", Phase 12)";

	// ── Kommandozentrale (UI.md Abschnitt 4) ──

	/// <summary>Taxi-Preis für den Platzhalter „Taxi buchen“ in der Universumskarte.</summary>
	public static int TaxiPrice => 120;

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

	/// <summary>Beziehung einer Fraktion zum Spieler (Platzhalter, Politik fehlt im Core noch).</summary>
	public const string FactionRelationToPlayer = "unbekannt – " + Tag;
	/// <summary>Art einer Fraktion (Platzhalter, Fraktionsarten gibt es im Core noch nicht).</summary>
	public const string FactionKind = "unbekannt – " + Tag;
	/// <summary>Ereignisliste einer Fraktion (Platzhalter).</summary>
	public static string[] FactionEvents => new[] { "Keine Ereignisse aufgezeichnet – " + Tag };

	/// <summary>
	/// Persönlichkeit einer Fraktion als Balkenwerte 0..1 (Platzhalter; deterministisch aus dem Namen,
	/// damit die Anzeige stabil bleibt, aber ohne Bezug zur Simulation).
	/// </summary>
	public static (float Aggression, float Greed, float Cooperation, float Lawfulness) FactionPersonality(string name)
	{
		int h = 7;
		foreach (var c in name) h = h * 31 + c;
		float F(int salt) => ((h + salt * 101) & 0xff) / 255f;
		return (F(1), F(2), F(3), F(4));
	}

	/// <summary>Beziehungen einer Fraktion zu anderen (Platzhalter, solange <see cref="SpaceSim.Core.Model.Faction.Relations"/> leer ist).</summary>
	public const string FactionRelationsSoon = "Beziehungen zu anderen Fraktionen: " + Tag + " (" + Soon + ", Politik ab Phase 10)";

	// ── Meine Fraktion ──

	/// <summary>Größenstufe des Spielers (Platzhalter, Akteursgrößen gibt es im Core noch nicht).</summary>
	public const string MyFactionSize = "Einzelakteur – " + Tag;
	/// <summary>Fortschritt zur nächsten Größenstufe (Platzhalter).</summary>
	public const string MyFactionProgress = "Fortschritt zur nächsten Stufe: " + Tag;
	/// <summary>Mitgliedschaft (Platzhalter).</summary>
	public const string MyFactionMembership = "keine eigene Fraktion – " + Tag;
	/// <summary>Unterhalt pro Tag (Platzhalter).</summary>
	public const string MyFactionUpkeep = "−42 Cr/Tag – " + Tag;
	/// <summary>Liegegebühr pro Tag (Platzhalter; echte Staffel je Schiffsklasse kommt mit Phase 12).</summary>
	public const string MyFactionBerthFee = "−12 Cr/Tag – " + Tag;
	/// <summary>Einnahmen pro Tag (Platzhalter).</summary>
	public const string MyFactionIncome = "+210 Cr/Tag – " + Tag;
	/// <summary>Ruf und Fahndung je Fraktion (Platzhalter, Politik fehlt noch).</summary>
	public const string MyFactionReputation = "Ruf/Fahndung: " + Tag + " (" + Soon + ", Politik ab Phase 10)";
	/// <summary>Fähigkeiten mit Schloss nach Größenstufe (Platzhalter).</summary>
	public static (string Name, bool Unlocked, string Note)[] MyFactionAbilities => new[]
	{
		("Verbände führen", false, "ab Kleinfirma – " + Soon),
		("Taxi, Hangar, Leihschiff", false, "ab Kleinfirma – " + Soon),
		("Büro an eigener Station", false, "ab Kleinfirma – " + Soon),
		("Fehde erklären", false, "ab Kleinfirma – " + Soon),
		("Firmensitz", false, "ab Firma – " + Soon),
		("Zoll und Sperren", false, "ab Dominanz im Sektor – " + Soon),
	};

	// ── Flotte ──

	/// <summary>Befehlsraster der Flotte (sichtbar, aber gesperrt – keine Wirkung außer Auto-Mining).</summary>
	public static string[] FleetCommands => new[] { "Fliegen", "Halten", "Handel", "Schürfen", "Patrouille", "Wache", "Eskorte", "Heimkehr" };
	/// <summary>Verhalten der Flotte (Platzhalter).</summary>
	public static string[] FleetBehaviors => new[] { "Passiv", "Verteidigen", "Angriff", "Feuer halten" };
	/// <summary>Befehlskapazität (Platzhalter).</summary>
	public const string FleetCommandCapacity = "Befehlskapazität: " + Tag + " (" + Soon + ")";

	// ── Besitz: Schiffsdetail ──

	/// <summary>Unterhalt eines Schiffs im Detailfenster (Platzhalter).</summary>
	public const string ShipUpkeepText = "Unterhalt 14 Cr/Tag – " + Tag;
	/// <summary>Wert eines Schiffs im Detailfenster (Platzhalter; der Gesamtwert in der Übersicht ist echt).</summary>
	public const string ShipValueText = "Wert ≈ 9.800 Cr – " + Tag;
	/// <summary>Hinweis, wo man in ein anderes eigenes Schiff wechselt (Phase 5c: im Hangar der Station, an der es liegt; Fernwechsel ab Phase 12).</summary>
	public const string ShipSwitchSoon = "Im Hangar der Station wechseln, an der es liegt";
}
