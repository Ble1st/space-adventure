# Zwei Banditenarten (Ausbau Punkt 17) — Umsetzungskonzept

Stand: 2026-11-20. Grundlage: `CLAUDE.md` (Phase 9 Akteure, Phase 10 Sektorkontrolle/Sperren, Phase 11 Piraten/Versteck/Fahndung,
Phase 22 Handlungsfähigkeit, Phase 28 Krieg/Belagerung, „Black market", „Station capture", Punkt 9 Firmensitz), `docs/POLITIK.md`,
`docs/KAMPF.md`, `docs/SCHWARZMARKT.md`, `docs/TODO-AUSBAU.md` Punkt 17.

Der Text ist so geschrieben, dass ihn ein anderer Programmierer ohne Rückfrage umsetzen kann: jede Änderung nennt Datei, Klasse,
Stelle und Prüfung.

---

## 1. Ziel und Abgrenzung

Punkt 17 verlangt **zwei** Arten von Banditen, die nebeneinander existieren:

| | (a) Sektorbanditen | (b) Etablierte Banditen |
|---|---|---|
| Was | die heutigen Piratengruppen („Rote Korsaren", „Schwarze Klingen") | **eine** große gesetzlose Fraktion |
| Modell | `actors[]`-Eintrag, `ActorKind.Outlaw`, **kein** `Faction`-Eintrag | `factions[]`-Eintrag mit `"kind": "outlaw"` (Akteur **und** Fraktion) |
| Besitz | ein Versteck, 2–3 Kaperschiffe, keine Wirtschaft | Sitz (Versteck-Bauart), 3 Wirtschaftsstationen, Schutzflotte **und** Kaperflotte |
| Gebiet | kein Einfluss (`influence: 0`), Sektor bleibt herrenlos | eigener Sektor über Einfluss, Stufe **Dominanz**, Dauerzoll |
| Geld | Hehlerei (gedeckelt), Unterhalt/Neubau | Hehlerei **plus** Zoll, Abgaben der Schiffe im Sektor, Verkäufe der eigenen Werke |
| Verhalten | überfallen, fliehen, neu bauen | überfallen **und** verteidigen **und** kassieren; können Sektoren gewinnen und verlieren |
| Beziehung | Vorgabe `RelationOutlaw` (−0,6) | unverändert feindlich, **aber** aus der Politik der anderen Fraktionen herausgenommen (siehe 3.2) |

**Nicht Teil dieses Punktes:** Spieler-Diplomatie mit Banditen, Tribut, Bündnisse, Fehden (Punkt 6), Schmuggler-KI (Punkt 5),
Missionen (Punkt 10), Regierungskrise. Die Banditenfraktion erklärt **keinen** Krieg (`Wars.CanWage` = false) und erobert **keine**
Stationen (`Capitulation.TakerFor` verlangt für KI `Kind != Outlaw`); sie verteidigt nur, was sie hat, und verliert, was zerstört wird.

---

## 2. Entscheidungen (mit Begründung)

### 2.1 Faction-Eintrag: ja

Die etablierten Banditen bekommen einen echten `factions[]`-Eintrag mit `"kind": "outlaw"`. `WorldBuilder` akzeptiert das bereits
(`WorldBuilder.cs:206`: `state`, `guild`, `outlaw`), der Akteur bekommt `Membership = id` und ist damit eine Fraktion im Sinne von
`Universe.Factions` und `Actor.IsFaction(u)`.

**Warum nicht nur `actors[]` (wie die heutigen Gruppen):** Ohne Fraktionseintrag zählt `SectorControl.Compute` ihre Stationen nur in
den Anteil „ohne Fraktion" (`Politics.cs:72`), `Sector.Owner` kann nie ihre Id annehmen, `GateAccess.PayToll` findet keine Kasse
(`to` = Sektorbesitzer), `AccessInfo`, `ThreatMap.BaseSecurity`, `Wanted`, `Faction.Territory` und die Kartenanzeige kennen sie nicht.
Ein zweites Besitzkonzept („Sektor.BanditActor") wäre in Zugangsprüfung, Anzeige und Karte tiefer verzahnt als die paar Ausnahmen,
die ein Fraktionseintrag nötig macht.

**Der Diskriminator ist damit eindeutig und braucht kein neues Flag:**

```csharp
// Simulation/Bandits.cs
/// <summary>Etablierte Banditen: gesetzlos UND Fraktion (die kleinen Gruppen haben keinen Faction-Eintrag).</summary>
public static bool IsEstablished(Universe u, ActorId id) =>
    u.Actors.TryGetValue(id, out var a) && a.Kind == ActorKind.Outlaw && u.Factions.ContainsKey(new FactionId(id.Value));
public static bool IsEstablished(Universe u, FactionId f) => IsEstablished(u, new ActorId(f.Value));
```

Damit gilt: heutige Gruppen = `IsEstablished` **false** → alle neuen Pfade sind für sie tot, ihr Verhalten bleibt bitgleich.

### 2.2 Ort: Abgrund als Heimat, Nebelmark als Ausbauziel

- **Heimat Abgrund (Sektor 11).** Abgrund ist herrenlos (`owner: 0`), hat einen Metallgürtel, ein Gasfeld, Nebel und bereits ein
  Versteck (Id 74, „Schwarze Klingen"). Nebelmark (9) bleibt **unbebaut und herrenlos**: dort gründen `SpielerHqTests`,
  `FirmensitzTests` und die Runde-45-Tests ihre Spieler-Sitze; würde Nebelmark einem Banditen gehören, wäre
  `PlayerSeats.FoundIssue` („nur herrenlose Sektoren") und `PlayerBuilding.SectorIssue` dort tot und die Spielerfraktion hätte keinen
  Gründungsort mehr.
- **Ausbauziel Nebelmark (9)** über die Regel `banditExpansionSectors: [9]` (§ 5/§ 6.4): Ist die Kasse groß genug und Nebelmark
  weiterhin herrenlos (kein Spielersitz, keine Spielerstation), setzen sie dort einen Vorposten. Das ist der Weg „Sektoren erobern";
  der Korridor Alpha↔Delta bekommt damit im Langlauf einen Wegzoll — messbar, aber erst nach dem Aufbau.
- Zwei Gruppen in einem Sektor sind gewollt: die Klingen bleiben die kleinen Sektorbanditen (Versteck, 2 Korsaren), die neuen
  Banditen sind die etablierten Herren desselben Sektors.

### 2.3 Sitz = eigener Stationstyp `bandit_seat`

Ein `pirate_hideout` als Sitz scheidet aus: der Typ hat `security` ungesetzt (0). Für **Dominanz** braucht `SectorControl.StageOf`
(`Politics.cs:94`) entweder eine Behörde (`HasAuthority`, Regierungsform HQ/Sektor-HQ) oder Patrouillenabdeckung
(`ThreatMap.HasCoverage` = Sicherheit ≥ `CoverageSecurity` 1). Ohne Abdeckung bleibt der Sektor auf **Einfluss**, `MaxLevel` = 0,
und es gäbe keinen Zoll.

Deshalb ein eigener Typ `bandit_seat` („Freistatt", Größe L): `hideout: true` (Heimat der Kaperer, kein Unterhalt, keine Pleite,
kein Kapitulieren, zählt nicht gegen die Bau-Obergrenzen — `StationProtection.IsHideout`), dazu `security: 1.2`, `influence: 3`,
Dienste `trade`, `blackmarket`, `hangar`. `security` wird **nicht** am geteilten `pirate_hideout` hochgezogen — das würde Delta
(Lagekarte, Bedrohungsabwehr, `HuntWeight`) und damit den Referenz-Hash verändern.

### 2.4 Zoll statt Steuern — über die vorhandene Sperrstufe 1

„Zoll/Schutzgeld statt Steuern" wird **nicht** neu erfunden, sondern benutzt, was Phase 10 schon hat:

- `PolicyCheck` (`GateAccess.cs:47`) verlangt bei Stufe 1 `level × GateTollPerLevel` = 8 Cr von jedem Nicht-Mitglied; eigene Schiffe
  (`IsMember`) fahren frei; das Geld geht über `Travel.Start`/`PayToll` in die Kasse des **Sektor**besitzers — also in die der
  Banditen. Genau Schutzgeld.
- **Stufe 1, nicht 2:** ab Stufe 2 verweigert `CargoCheck` beladenen Gegnern die Einreise (`embargo`) — der Spieler ist mit −0,6
  „Feind" und käme mit Fracht nicht mehr in den Sektor, obwohl er dort handeln können soll. Stufe 1 ist „Kontrolle/Scan und Zoll".
- **Dauerzoll statt Ablauf:** `FactionPolicySystem.Decide` lässt Sperren nach `LockMaxTicks` ablaufen und dann `LockCooldownTicks`
  abkühlen. Für Banditensektoren gilt das nicht (Regel `banditTollLevel`, Dauerzoll, Begründung in § 7.2).
- Die Abgaben der **freien Schiffe** im Sektor (`UpkeepSystem.TaxAuthority` → Sektorherr) fließen ohnehin schon an den Besitzer; für
  einen Banditensektor ist das dasselbe Schutzgeld. Der Steuerdeckel (`FiscalFactors`) lässt sie aus (§ 7.5), damit der Zufall der
  drei Staaten unverändert bleibt und die Banditenkasse nicht vom Deckel gebremst wird.

### 2.5 Schutzflotte = `ShipRole.Warship`, Kaperflotte = `ShipRole.Pirate`

Keine neuen Rollen (die Rolle steht als String im Spielstand, in `CapabilityDefaults`, in der Anzeige und in
`ThreatMap.IsPolice/IsRaider`):

- **Kaperflotte:** `ShipRole.Pirate` — `PirateSystem`/`PirateCommands` übernehmen sie **von selbst**, weil sie über
  `u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw)` laufen (`PirateSystem.cs:490`, `ForceCommands.cs:188`). Sie jagen, lauern,
  fliehen, flicken und bauen im Sitz nach. Nur die Kapazitätsgrenze wird für sie angehoben (§ 7.3 Nr. 1: 3 statt 2 Einheiten).
- **Schutzflotte:** `ShipRole.Warship` — damit gelten ohne Zutun: `Siege.Reinforce` nimmt sie als Verteidiger eigener Stationen
  (`u.FactionOf(s) in defenders`, `Siege.cs:131`), `StationCombat` behandelt sie als freundlich, `UnitSupport.Serve` versorgt sie an
  der Heimatbasis aus der Kasse, `Wars.IsWarship` lässt sie Sperren physisch durchbrechen. Sie kaufen/kommandieren muss ein eigenes
  System (`NavySystem` überspringt sie, weil `Wars.CanWage` für Gesetzlose false ist).

### 2.6 Wirtschaft: drei vorhandene Typen, keine neuen Rezepte

`steelworks` (Stahl), `plating_works` (Hüllenplatten), `electronics_fab` (Elektronik) — Ausgänge sind **keine** Modulwaren, also
greift keine Forschungs-Sperre (`ProductionSystem` überspringt Rezepte mit unerforschten Modul-Ausgängen; die Banditenfraktion forscht
nie). Eingänge: Eisenerz/Titan/Kupfer aus dem eigenen Gürtel, Energie und Wafer von außen (Import über freie Händler) — das ist die
gewollte Verflechtung: die Banditen **kaufen** bei der ehrlichen Wirtschaft und **verkaufen** in sie hinein. Kein neuer
Transport-Code: `TraderSystem` prüft keine Beziehungen, Händler beliefern und leeren jede Station mit Markteintrag.

Bau/Neubau ohne Baustelle und ohne Bauschiff (Muster `SeatRelocation.Ensure`/`Outposts.Build`): fertig gekauft aus der eigenen Kasse,
`TreasuryUpkeep` „bandit_build". Begründung: `FactionBuilderSystem` überspringt Gesetzlose, `Investors.ForStation` ebenfalls — ein
eigener Baustellenweg wäre ein zweites Bausystem.

### 2.7 Was ausdrücklich **nicht** passiert

- Die beiden heutigen Gruppen ändern sich nicht (kein Fraktionseintrag → alle neuen Pfade greifen nicht).
- Kein Krieg, keine Bündnisse, keine Fehden mit oder gegen die Banditen (`Wars.CanWage`/`Alliances.IsParty` bleiben, wie sie sind).
- Keine Kapitulation, keine Eroberung durch Banditen (`Capitulation.IsPirate` deckt „Besitzer gesetzlos" schon ab).
- Keine Forschung, keine Baustellen, keine Wirtschaftsschiffe, keine Polizei, kein Gericht, kein Kopfgeldbüro am Sitz.
- Kein neues Speicherformat (§ 9).

**Was der Spieler darf (und was daraus folgt):** Er kann die Werke und den Sitz angreifen wie jede Station
(`PlayerGunnery.StationIssue` verbietet nur eigene, Baustellen, ausgeschaltete und Verteidiger; Gesetzlose sind kein Sonderfall).
Das ist gewollt: der Weg, die Banditen zu schwächen, ist ihre Wirtschaft. Ein zerstörtes Werk fehlt in der Bilanz, `BanditSystem`
baut es nach (Geld, Zeit) — der Spieler erzwingt damit laufende Ausgaben. Der **Sitz** ist unzerstörbar (`FallToZero` schaltet
Verstecke nur aus), also verliert die Fraktion nie ihre Handlungsfähigkeit; ein ausgeschalteter Sitz heilt über
`StationRepairSystem` wie jede Station. Zerstört der Spieler alle drei Werke, fällt der Sektor nach dem nächsten
`SectorControlSystem`-Schritt an ihn bzw. wird herrenlos (§ 7.2 Nr. 5) — die Bande baut dann von vorn (Vorposten/Ausbau), was
Geld kostet und sie schwächt. Der Angriff außerhalb eines Krieges ist ein schweres Verbrechen bei der Banditenfraktion
(`assault_station`); da diese kein Gericht und kein Kopfgeldbüro hat, bleibt davon nur die Fahndung stehen, die nirgends zahlbar ist
— bewusst: gegen Banditen kämpft man, man verhandelt nicht.

---

## 3. Modell (was wo steht)

### 3.1 Vorhandene Felder, die mitbenutzt werden

| Feld | Nutzung |
|---|---|
| `Faction` (Id 4) + `Actor` (Id 4) | Fraktion und Wirtschaftssubjekt; `Membership = 4`, `Treasury` = Banditenkasse, `Personality` (aggressiv, gierig, unkooperativ, gesetzlos) |
| `Sector.Owner = 4` (Sektor 11) | aus Einfluss abgeleitet (`SectorControl`), Startwert aus der Weltdatei |
| `Sector.Access.EntryLevel = 1` | Dauerzoll, gesetzt vom `FactionPolicySystem` (§ 7.2), gespeichert/gehasht wie jede Sperre |
| `Station.Owner = 4` | Sitz + drei Wirtschaftsstationen (in der Weltdatei ohne `owner`, weil der Sektor ihnen gehört) |
| `Ship.Owner = 4`, `Ship.HomeBase = Sitz`, `Ship.DutySector` | Kaperer und Schutzflotte |
| `Ship.Role` | `Pirate` bzw. `Warship` |
| `Faction.Territory` | {11}, nach Ausbau zusätzlich {9} — pflegt `SectorControl.Update` |
| `Actor.Wanted`, `Actor.BountyClaims` | Fahndung der Banditen gegen andere (Piraterie), Kopfgeld auf ihre Kaperer |
| `Station.BlackStock` (Sitz) | Hehlerware; `BlackMarket.Stash` findet „erstes Versteck mit Schwarzmarkt des Besitzers" |

### 3.2 Ein **einziges** neues persistentes Feld

```csharp
// Model/Actor.cs
/// <summary>Ausbau Punkt 17: Tick des letzten Banditenbaus (Wirtschaftsstation oder Vorposten), 0 = nie.
/// Abklingzeit <see cref="EconomyRules.BanditRebuildCooldownTicks"/>. Gespeichert, gehasht nur wenn ≠ 0.</summary>
public long LastBanditTick;
```

Mehr braucht es nicht: Sektor, Sperre, Kasse, Stationen, Schiffe, Lager sind bestehender Zustand. Damit bleibt der Spielstand
**v17** und der Hash einer Welt ohne Banditen unverändert (`LastBanditTick == 0` mischt nichts).

---

## 4. Regeln: `src/SpaceSim.Core/Model/EconomyRules.Bandits.cs`

Neue partielle Datei nach dem Muster `EconomyRules.Salvage.cs` / `EconomyRules.Seats.cs`. **Code-Standard = aus/0, Standardwelt =
an** — ohne die Fraktion in der Welt ist alles wirkungslos, deshalb genügt ein Hauptschalter.

```csharp
namespace SpaceSim.Core.Model;

/// <summary>
/// Ausbau Punkt 17 — zwei Banditenarten (docs/BANDITEN.md). Etablierte Banditen = gesetzlose Fraktion (ActorKind.Outlaw **mit**
/// Faction-Eintrag): eigener Sitz, eigene Wirtschaft, Schutz- und Kaperflotte, Zoll statt Steuern, eigenes Gebiet.
/// Code-Vorgabe aus; die Standardwelt schaltet an und stellt die Fraktion. Aus (oder ohne solche Fraktion) rechnet die Welt wie vorher.
/// </summary>
public sealed partial class EconomyRules
{
    /// <summary>Hauptschalter: gesetzlose Fraktionen mit Sitz, Wirtschaft, Flotten, Zoll. Aus = WorldBuilder lässt solche Fraktionen
    /// und alles, was ihnen gehört, aus der Welt (die Bau-Obergrenze sinkt entsprechend).</summary>
    public bool BanditFactions;

    /// <summary>Dauerzoll: Sperrstufe, die eine Banditenfraktion in ihren Sektoren hält (0 = kein Zoll, höchstens 2 — Stufe 2 wäre ein
    /// Embargo und sperrte beladene Spieler aus).</summary>
    public int BanditTollLevel = 1;
    /// <summary>Wie oft der Dauerzoll nachgeführt wird (Ticks).</summary>
    public int BanditTollRefreshTicks = 600;
    /// <summary>
    /// Durchsetzungskosten des Dauerzolls je Stufe und Tick. 0 = die Bande setzt ihn mit ihren eigenen Schiffen durch (die zahlen
    /// ohnehin Unterhalt) und die Kasse wird nicht zusätzlich belastet. Wichtig: mit den normalen 0,5 Cr/Stufe/Tick fraß die
    /// Durchsetzung (10 000 Cr in 20 000 Ticks) jede Zolleinnahme auf, die Sperre fiel auf „Papier" und der Zoll verschwand — eine
    /// Sackgasse wie Abgrund trägt keine Verwaltungskosten.
    /// </summary>
    public float BanditTollUpkeepPerLevel;

    /// <summary>Kapazitätseinheiten der Kaperflotte einer Banditenfraktion (S 1, M 1,5, L 2; ersetzt für sie maxRaiderUnitsPerActor).</summary>
    public float BanditMaxRaiderUnits = 3f;

    /// <summary>Schutzflotte: höchstens so viele Kriegsschiffe.</summary>
    public int BanditWarships = 2;
    /// <summary>Unterhalt je Schutzschiff und Tick aus der Banditenkasse.</summary>
    public float BanditWarshipUpkeepPerTick = 0.8f;
    /// <summary>Rücklage, die die Kasse für den Schutzflottenkauf behalten muss.</summary>
    public float BanditWarshipReserve = 3000f;
    /// <summary>Modell der Schutzflotte (muss ein Kampfmodell ohne Fracht/Bergbau/Bau sein).</summary>
    public string BanditWarshipModel = "warden";
    /// <summary>Abklingzeit zwischen zwei Streitkraft-Käufen (Schutzschiff) — gemeinsam mit dem Kaperneubau über Actor.LastForceTick.</summary>
    public int BanditFleetCooldownTicks = 3000;

    /// <summary>Neubau einer verlorenen Wirtschaftsstation: Kosten = Baukosten × Faktor (Geldsenke, ohne Baustelle und Material).</summary>
    public float BanditRebuildMoneyFactor = 1.5f;
    /// <summary>Abklingzeit zwischen zwei Banditenbauten (Wirtschaftsstation oder Vorposten) in Ticks.</summary>
    public int BanditRebuildCooldownTicks = 3000;
    /// <summary>
    /// Sollbestand der Wirtschaft: Typ-Schlüssel → Anzahl, die die Fraktion in ihrem Gebiet halten will (Neubau bei Verlust,
    /// Muster <see cref="Simulation.Outposts.Build"/>). In der Standardwelt `steelworks: 1`, `plating_works: 1`,
    /// `electronics_fab: 1`. Leer = kein Nachbau (dann bleibt eine zerstörte Station weg).
    /// </summary>
    public Dictionary<string, int> BanditEconomyTypes = new();

    /// <summary>Vorposten in fremden, herrenlosen Sektoren (Sektor erobern).</summary>
    public bool BanditExpansion;
    /// <summary>Sektoren, in denen ein Vorposten entstehen darf — nur solange herrenlos und ohne Spielerbesitz.</summary>
    public List<int> BanditExpansionSectors = new();
    /// <summary>Stationstyp des Vorpostens (muss einen Markt haben, damit er Einfluss zählt).</summary>
    public string BanditExpansionType = "warehouse";
    /// <summary>Kasse muss Baukosten × Faktor erreichen, bevor ein Vorposten gebaut wird.</summary>
    public float BanditExpansionMoneyFactor = 4f;

    /// <summary>Kassenziel: darüber zahlt die Fraktion je Tick einen Beuteanteil an die Mannschaft (Geldsenke, kein Dauerhorten).</summary>
    public float BanditTreasuryTarget = 40000f;
    /// <summary>Anteil des Überschusses über dem Ziel, der je Tick als Beuteanteil abgeht (0 = keine Senke).</summary>
    public float BanditHoardShare = 0.0002f;

    /// <summary>Deckel der Hehlerei für Banditenfraktionen (0 = ungedeckelt; die kleinen Gruppen bleiben bei pirateTreasuryCap).</summary>
    public float BanditFenceCap;
}
```

Alle Felder sind additiv in der gespeicherten `EconomyRules` — nichts weiter zu tun (§ 9).

---

## 5. Systeme: `BanditModule`

Neue Datei `src/SpaceSim.Core/Simulation/Bandits.cs` (reine Regeln) und `src/SpaceSim.Core/Simulation/BanditSystem.cs` (`ISimSystem`).

```csharp
// Modules/DefaultModules.cs — zwischen SecurityModule und CombatModule registrieren
public sealed class BanditModule : ISimModule
{
    public string Name => "Bandits";
    public void Register(SimulationBuilder b) => b.Add(new BanditSystem(), BanditSystem.Interval);
}
// Simulation.cs, DefaultModules(): ... new ConstructionModule(), new SecurityModule(), new BanditModule(), new CombatModule(), ...
```

`Interval = 50` (wie Wirtschafts-/Lebenszyklus-Takte, skaliert mit `dt`, kein `Universe.Rng` — der Takt ist reine Verwaltung).

### 5.1 `Bandits` (reine Funktionen, testbar)

```csharp
public static class Bandits
{
    public static bool IsEstablished(Universe u, ActorId id);          // § 2.1
    public static bool IsEstablished(Universe u, FactionId f);
    public static ActorId? Seat(Universe u, ActorId id);               // erstes eigenes Versteck (Id-Reihenfolge)
    public static List<Station> Economic(Universe u, ActorId id);      // eigene fertige Stationen mit Rezepten, ohne Versteck
    public static bool HoldsSector(Universe u, Sector s);              // Besitzer ist etabliert
    public static float RaiderUnitsCap(Universe u, Actor a);           // BanditMaxRaiderUnits bzw. MaxRaiderUnitsPerActor
    public static float FenceCap(Universe u, Actor a);                 // BanditFenceCap bzw. PirateTreasuryCap (0 = unendlich)
    public static string? BuildIssue(Universe u, Actor a, StationType t);  // Kasse, Platz (Outposts.FreeSpot), Cooldown
    public static Vec2? Spot(Universe u, Sector s, float radius);      // Outposts.FreeSpot um die Sitzposition, sonst Sektormitte
    public static string Text(Universe u, ActorId id);                 // Anzeige „Sitz, 3 Werke, Zoll Stufe 1, Kasse …"
}
```

### 5.2 `BanditSystem.Step(u, dt)`

Nur für `u.Actors.Values.Where(a => a.Kind == Outlaw).OrderBy(a => a.Id.Value)` **mit** `IsEstablished`. Kein Zufall, alles in
Id-Reihenfolge; ein `continue`, wenn `!u.Rules.BanditFactions`.

1. **Sitz prüfen.** `Seat(u, a)` fehlt (Sitz zerstört/ausgeschaltet)? Nichts tun — der Sitz ist unzerstörbar
   (`Capitulation.IsPirate` + `FallToZero` schalten Verstecke nur aus), `FactionViability` meldet für Gesetzlose ohnehin nur
   `NoHideout`/`CannotHunt`. Kein Not-Sitz (das macht `SeatRelocation` bewusst nur für Staaten/Gilden).
2. **Schutzflotte.** `ships = u.ShipsById().Where(s => Wars.IsWarship(s) && s.Owner == a.Id)`.
   - Unterhalt: `paid = min(BanditWarshipUpkeepPerTick × count × dt, max(0, Kasse))`, buchen und
     `TreasuryUpkeep(a.Id, "bandit_fleet", paid, rest)`.
   - Kauf: `count < BanditWarships` **und** `Tick - LastForceTick >= BanditFleetCooldownTicks` **und**
     `Kasse - BanditWarshipReserve >= Materialwert des Modells × StationBuildCostFactor` → `ShipPurchase.Create` am Sitz
     (Modell `BanditWarshipModel`, Rolle `Warship`, `Controller.Ai`, Owner `a.Id`, `HomeBase = Sitz`), Kasse belasten,
     `TreasuryUpkeep(a.Id, "bandit_build", cost, 0)`, `LastForceTick = Tick`. Kein Materialverbrauch (Muster
     `PirateSystem.Book`). **`LastForceTick` statt eines neuen Feldes:** es ist gespeichert, gehasht und schon die Bremse für
     Polizei-, Kriegsschiff- und Kaperneubauten; die Bande kauft dadurch höchstens eine Einheit je Abklingzeit, und Kaperer und
     Schutzschiff teilen sich die Bremse (eine Kasse, ein Einkauf).
   - Befehl (`Commands.Give(..., ignoreCapacity: true)`): im Gefecht/Notfall/`ReturnCommand` nichts; sonst
     `Combat.EconomicHoldout.AlarmSectorFor(u, s)` → `PatrolCommand([alarm])`; sonst eigener Sektor mit der höchsten Bedrohung
     (`SecuritySystem.Territory`-Analogon: `u.Sectors.Values.Where(s => s.Owner == a.Id)`), sonst Sitzsektor; Sitz anfliegen, wenn
     Hülle < `RetreatHull`, Munition/Raketen leer oder `NpcRepair.Crippled`.
3. **Wirtschaftsstationen nachbauen.** Sollbestand = `u.Rules.BanditEconomyTypes` (Typ-Schlüssel → Anzahl, in der Weltdatei
   gesetzt, siehe § 4/§ 6.5): für jeden Typ, von dem weniger fertige Stationen der Fraktion stehen als gefordert, und wenn
   `Tick - LastBanditTick >= BanditRebuildCooldownTicks`, `Kasse >= Baukosten × BanditRebuildMoneyFactor` und
   `Bandits.Spot` einen Platz liefert → `StationFactory.Create` + `AddStation`, `StationBuilt`, Kasse belasten,
   `TreasuryUpkeep "bandit_build"`, `LastBanditTick = Tick`. Höchstens **ein** Bau je Fraktion und Schritt (Reihenfolge der
   Typenliste, damit es deterministisch bleibt). Der Typ muss im Sektor einen Platz finden, sonst rückt der nächste Typ nach.
   Kein Materialverbrauch (die Bande kauft fertig, Muster `Outposts.Build`).
4. **Vorposten (Sektor erobern).** `BanditExpansion` und `BanditExpansionSectors` nicht leer: erster Sektor der Liste, der
   (a) herrenlos ist (`Owner == 0`), (b) weder eine Station des Spielers noch einen Spielersitz enthält
   (`st.Owner != u.PlayerActor`, `PlayerSeats.Seats(u)` leer für den Sektor) und (c) freien Platz bietet; Kasse ≥ Baukosten ×
   `BanditExpansionMoneyFactor` und Cooldown vorbei → eine Station vom Typ `BanditExpansionType` (Standard `warehouse`,
   „Umschlagplatz") dort bauen, buchen wie in Nr. 3. `SectorControl.Update` läuft im nächsten `SectorControlSystem`-Schritt und setzt
   den Besitzer (Einfluss > 0, bisheriger Besitzer 0 → sofortiger Wechsel, `SectorOwnerChanged`). Nur ein Vorposten je Sektor, nie
   in einem Sektor mit Spielerbesitz — der Spieler soll Nebelmark nicht verlieren, ohne es zu merken.
5. **Beuteanteil (Senke).** `Kasse > BanditTreasuryTarget`: `share = (Kasse − Ziel) × BanditHoardShare × dt`, abziehen,
   `TreasuryUpkeep(a.Id, "bandit_share", share, 0)`. Verhindert das lineare Anwachsen der Kasse in Langläufen (dieselbe Rolle wie
   `PirateTreasuryCap` für die kleinen Gruppen, nur ohne Obergrenze auf das Wirtschaften).

Reihenfolge ist wichtig: erst Unterhalt, dann Kauf/Nachbau (Geld ist danach weg), zuletzt die Senke.

---

## 6. Datenänderungen `src/SpaceSim.Core/Content/default-world.json`

### 6.1 Fraktion

Auszug aus `factions` — die drei bestehenden Einträge bleiben unverändert, der vierte kommt ans Ende:

```
"factions": [
  { "id": 1, ... }, { "id": 2, ... }, { "id": 3, ... },
  {
    "id": 4,
    "name": "Herren der Nebelmark",
    "kind": "outlaw",
    "personality": { "aggression": 0.85, "greed": 0.9, "cooperation": 0.1, "lawfulness": 0.0 },
    "treasury": 22000
  }
]
```

- **Keine** `relations`: es gilt die Vorgabe `RelationOutlaw` (−0,6) in beide Richtungen. Ein Eintrag wäre eine gespeicherte
  Abweichung und würde `ActorTests.Beziehungen_Vorgaben_Abweichungen_Sparsam` zusätzlich belasten (§ 11.3).
- `randomNames: true` in der Weltdatei würfelt auch diesen Namen (wie heute bei „Rote Korsaren"); in Tests mit
  `TestWorlds.Def` (`RandomNames = false`) steht der Name aus der Datei. Das ist gewollt und ändert nichts an der Umsetzung.
- `treasury: 22000` = Startkapital für Sitzausbau, erste Schutzschiffe und Reparaturen; kein Geld aus dem Nichts, es ist
  Startkapital wie bei den anderen Gruppen (6000/4000).

### 6.2 Stationstyp `bandit_seat`

Am Ende von `stationTypes`:

```json
{
  "key": "bandit_seat",
  "name": "Freistatt",
  "size": "L",
  "hideout": true,
  "influence": 3,
  "security": 1.2,
  "buildCost": 18000,
  "upkeep": 0,
  "income": 0,
  "services": ["trade", "blackmarket", "hangar"],
  "recipes": [],
  "capturable": false,
  "defense": { "hull": 1800, "shield": 240, "shieldRegen": 2.5, "armor": 60, "pointDefense": 0,
               "guns": ["weapon_laser_s", "weapon_massdriver_s", "weapon_laser_s", "weapon_massdriver_s"] }
}
```

Prüfungen, die das erfüllt: `hideout` + keine Regierungsform (sonst wirft `WorldBuilder`), `influence` 3 (Dominanz im eigenen
Sektor), `security` 1.2 ≥ `CoverageSecurity` 1 (Patrouillenabdeckung → Dominanz), Größe L erlaubt 4 Geschütze
(`StationSizes.MaxGuns`), `capturable: false` (Verstecke kapitulieren ohnehin nie), `blackmarket` liefert den zweiten Hehler.

### 6.3 Sektor 11: Besitzer und vier Stationen (Ids 91–94, am Ende)

`"owner": 4` im Sektorobjekt; die Stationen **ohne** `"owner"` (sie erben den Sektorherrn — `WorldBuilder.cs:350`), Koordinaten im
Grundmaßstab (der Aufbau multipliziert mit `scale` 7.9). Auszug aus `sectors` — der Sektor ist der elfte und letzte:

```
"sectors": [ ..., {
  "id": 11, "name": "Sektor Abgrund", "nebula": true,
  "gasFields": [{ "pos": [-150, 150], "radius": 40 }],
  "skybox": "red_local_star_no_nebulae", "skyboxYaw": 200, "scale": 7.9, "owner": 4, "map": [1, 2],
  "asteroids": 35,
  "fields": [{ "class": "metal_rock", "pos": [-50, 50], "belt": 12, "respawn": 90 }],
  "stations": [
    { "id": 74, "type": "pirate_hideout", "name": "Schlupfwinkel – {owner}", "owner": "blades", "pos": [210, 190], "essential": true, "credits": 0 },
    { "id": 91, "type": "bandit_seat",     "name": "Freistatt – {owner}",   "pos": [-115, -75], "essential": true, "credits": 8000 },
    { "id": 92, "type": "steelworks",      "name": "Beutewerk – {owner}",   "pos": [45, -85],   "essential": true, "credits": 4000 },
    { "id": 93, "type": "plating_works",   "name": "Panzerwerk – {owner}",  "pos": [75, 25],    "essential": true, "credits": 4000 },
    { "id": 94, "type": "electronics_fab", "name": "Elektronikwerk – {owner}", "pos": [-150, -140], "essential": true, "credits": 4000 }
  ]
} ]
```

- `essential: true` auf allen vier: die Wirtschaftsstationen sollen nicht in der Stilllegungsprüfung (`StationReview`) hängen; der
  Sitz ist als Versteck ohnehin geschützt. `credits` = Betriebskapital für den ersten Einkauf.
- **Abstände geprüft** (Grundmaßstab; der Aufbau multipliziert Positionen und Radien mit `scale` 7.9, die Abstände also mit):
  - Station ↔ Station braucht `r_a + r_b + 20` (`StationPlacement.Check`) = 22+15+20 = **57** für Sitz↔Werk, 15+15+20 = **50** für
    Werk↔Werk. Ist: Sitz↔Stahl 160,3 · Sitz↔Panzer 214,7 · Sitz↔Elektronik 73,8 · Stahl↔Panzer 114,0 · Stahl↔Elektronik 202,6 ·
    Panzer↔Elektronik 279,0.
  - Station ↔ Tor braucht `r + 35` = **57**. Nächstes Tor ist Tor 1 (0, −150) mit 79,1 (Stahlwerk); Tor 2 (0, 160) ist überall
    > 154 entfernt.
  - Station ↔ Gürtelmittelpunkt braucht `r + 30` = **52**. Ist: 127,5 (Panzerwerk) … 214,7 (Elektronikwerk).
  - **Gürtel bleibt voll:** `BeltPlacement.Fits` verlangt für jeden Felsen `> 50 − 15 + r_Station + r_Fels` = **57** Abstand zur
    Station und legt Felsen 22–45 um den Feldmittelpunkt (Felsradius ≤ 6). Nötig sind damit **≥ 45 + 6 + 57 = 108** vom
    Mittelpunkt; der kleinste Ist-Abstand ist 127,5 — also ist auch der ungünstigste Felsen erlaubt und der Gürtel behält seine 12
    Asteroiden. Test 11.1 Nr. 3 prüft genau diese Zahl.
  - Zum Klingen-Versteck (210, 190) mindestens 213,2 (Panzerwerk) — zwei Verstecke im Sektor sind erlaubt und gewollt (§ 2.2).
  - Alle vier liegen innerhalb der Platzierungsfläche (±150 × Maßstab) und über das ganze Sektorviertel verteilt, damit die
    Kaperer und die drei Werke nicht in einem Pulk stehen; der Sitz liegt bewusst nahe am Elektronikwerk (73,8), weil dort die
    teuerste Ware entsteht.

### 6.4 Schiffsgruppen (am Ende von `ships`)

```json
{ "role": "pirate",  "faction": 4, "sector": 11, "credits": 0, "count": 2, "model": "corsair" },
{ "role": "warship", "faction": 4, "sector": 11, "credits": 0, "count": 2, "model": "warden" }
```

`"faction": 4` und kein `owner` → `owner = new FactionId(4)` (`WorldBuilder.cs:381`), `PlaceAtHomeBase` setzt für Piraten die
Heimatbasis auf das Versteck des Besitzers, für die Rolle `Warship` greift der `else`-Zweig
(`SecuritySystem.HomeBaseFor`) — **das findet für eine Gesetzlosenfraktion keine Regierungsstation** und lässt `HomeBase` leer.
Deshalb: in `WorldBuilder.PlaceAtHomeBase` ergänzen, dass auch `ShipRole.Warship` bei einem gesetzlosen Besitzer dessen Versteck
nimmt:

```csharp
Station? home = ship.Role is ShipRole.Pirate or ShipRole.Warship && u.Actors.TryGetValue(ship.Owner, out var oa) && oa.Kind == ActorKind.Outlaw
    ? u.Stations.Values.Where(st => st.Owner == ship.Owner && Simulation.StationProtection.IsHideout(u, st))
        .OrderBy(st => st.Sector == ship.Sector ? 0 : 1).ThenBy(st => st.Id.Value).FirstOrDefault()
    : u.FactionOf(ship.Owner) is { } f ? Simulation.SecuritySystem.HomeBaseFor(u, f, ship.Sector) : null;
```

(Ohne diese Zeile fliegen die beiden Schutzschiffe ohne Heimatbasis und werden nie versorgt.)

### 6.5 Regeln in der Weltdatei

Auszug aus dem `rules`-Abschnitt (alle übrigen Regeln bleiben, wie sie sind):

```
"rules": {
  "banditFactions": true,
  "banditTollLevel": 1,
  "banditTollUpkeepPerLevel": 0,
  "banditMaxRaiderUnits": 3,
  "banditWarships": 2,
  "banditWarshipModel": "warden",
  "banditFleetCooldownTicks": 3000,
  "banditRebuildMoneyFactor": 1.5,
  "banditRebuildCooldownTicks": 3000,
  "banditEconomyTypes": { "steelworks": 1, "plating_works": 1, "electronics_fab": 1 },
  "banditExpansion": true,
  "banditExpansionSectors": [9],
  "banditExpansionType": "warehouse",
  "banditTreasuryTarget": 40000,
  "banditHoardShare": 0.0002,
  "banditFenceCap": 0,
  "maxStations": 78
}
```

**`maxStations` 75 → 78** (+3 zählende Wirtschaftsstationen; der Sitz zählt als Versteck nicht). Der Spielraum bleibt damit bei 6
(78 − 72 wirtschaftliche Stationen; vorher 75 − 69 = 6). Wird der Schalter beim Aufbau ausgeschaltet, senkt `WorldBuilder` die
Obergrenze um die weggelassenen Wirtschaftsstationen (`omittedEconomic`, `WorldBuilder.cs:351`) — 78 → 75, ohne dass der
Rücknahme-Helfer nachfassen muss.

`banditExpansionType` und `banditExpansionSectors` sind Daten, damit man den Ausbau in Tests und Szenarien abschalten bzw. auf einen
anderen Sektor richten kann, ohne Code zu ändern.

---

## 7. Eingriffe in bestehende Systeme (vollständige Liste)

Jede Änderung ist mit `Bandits.IsEstablished` bedingt; **ohne** Fraktionseintrag (also in jeder Welt ohne den Schalter) ist sie
wirkungslos. Das ist die Hash-Garantie: nicht „Regel aus", sondern „kein solcher Akteur vorhanden".

### 7.1 `Content/WorldBuilder.cs` — Fraktion, Inhalt, Besitzer

1. **Vor** dem Fraktions-Loop (Zeile ~199): weggefallene Fraktionen bestimmen
   ```csharp
   var dropped = u.Rules.BanditFactions ? new HashSet<int>()
       : def.Factions.Where(f => string.Equals(f.Kind, "outlaw", StringComparison.OrdinalIgnoreCase)).Select(f => f.Id).ToHashSet();
   ```
   (Nur `outlaw` — eine Welt mit einer Gesetzlosenfraktion ist per Definition eine Banditenwelt; heute gibt es keine.)
2. Fraktions-Loop: `if (dropped.Contains(f.Id)) continue;`
3. Sektor-Loop: `int sectorOwner = dropped.Contains(sd.Owner) ? 0 : sd.Owner;` statt direkt `sd.Owner`.
4. Station-Loop: `if (st.Owner is { Length: > 0 } ok2 && !actorByKey.ContainsKey(ok2) && …) continue;` — konkreter: eine Station
   weglassen, wenn sie einem weggefallenen Sektorherrn erbt (`st.Owner` leer **und** `dropped.Contains(sd.Owner)`) oder wenn ihr
   `owner`-Schlüssel zu einer weggefallenen Fraktion gehört. `omittedEconomic++`, wenn der Typ wirtschaftlich ist — das senkt
   `MaxStations` automatisch.
5. Schiffs-Loop: `if (dropped.Contains(group.Faction)) continue;`
6. `PlaceAtHomeBase`: siehe § 6.4.
7. `FactionDef` braucht **kein** neues Feld.

> **Achtung:** Punkt 3 und 4 dürfen `def` nicht mutieren (Tests bauen dieselbe `WorldDef` mehrfach). Alle Ersetzungen in lokalen
> Variablen bzw. beim Anlegen der `Sector`/`Station`-Objekte.

### 7.2 `Simulation/Politics.cs` — Dauerzoll, keine Fremdsperren, Sektor verlieren

1. **`FactionPolicySystem.Decide`**, direkt nach dem Spielerfraktions-Block (Zeile ~250):
   ```csharp
   if (Bandits.HoldsSector(u, sector))
   {
       int want = Math.Clamp(u.Rules.BanditTollLevel, 0, Math.Min(2, SectorControl.MaxLevel(u, sector)));
       if (acc.EntryLevel == want && acc.ExpiresTick < 0) return false;      // steht schon, kein Ablauf
       Apply(u, sector, want, want > 0 ? Desired(u, sector).Against : null, "bandit");
       acc.ExpiresTick = -1;                                                 // Dauerzoll
       acc.CooldownUntil = -1_000_000;
       return true;
   }
   ```
   Wirkung: Stufe 1 dauerhaft, solange die Kontrollstufe sie deckt (Dominanz = 2). Fällt die Kontrolle (Stationen zerstört), sinkt
   `MaxLevel` auf 0 und der Zoll fällt mit — „wer nicht durchsetzen kann, kassiert nicht". Die Durchsetzungskosten regelt § 7.2a.

   **Warum dieser Zweig zwingend ist:** ohne ihn liefe der Sektor durch `Desired`, und die schlechteste Beziehung der Banditen ist
   die Vorgabe `RelationOutlaw` = −0,6 gegen **jede** Fraktion. `LevelFor` (Schwellen −0,15/−0,3/−0,45/−0,65/−0,85) ergibt daraus
   **Stufe 3**, und `PolicyCheck` weist bei Stufe 3 jeden mit Beziehung ≤ `HostileRelation` (−0,15) ab — auch die **freien Händler**,
   die gegen Gesetzlose ebenfalls −0,6 haben. Abgrund wäre damit für den gesamten Nicht-Banditenverkehr dicht, der Korridor
   Alpha↔Delta zerfiele für die Wirtschaft, und `MapConnectivity` schlüge an. Stufe 1 („Kontrolle und Zoll") lässt jeden herein und
   nimmt 8 Cr.
2. **`WorstRelation`** (Zeile ~192): `if (Bandits.IsEstablished(u, other)) continue;` — die Banditen sind kein politischer Partner;
   ohne diese Zeile stünde jede KI-Fraktion wegen −0,6 auf Stufe 3 (`LockThresholds[2]` = −0,45) und würde **alle** ihre Sektoren
   gegen „Feinde" abschotten. Das ist die wichtigste Zeile des ganzen Punktes.
3. **`StepRelations`** (Zeile ~320): Paare überspringen, wenn eine Seite etabliert ist (Beziehung bleibt auf der Vorgabe −0,6;
   kein Hochdriften auf den Persönlichkeits-Ruhewert).
4. **`LocksAgainst`** (Zeile ~407): Sektoren überspringen, deren Besitzer etabliert ist (der Zoll der Banditen erzeugt keinen Groll
   bei Dritten — dieselbe Idee wie Regel `playerLocksSourOnlyTarget`).
5. **`SectorControl.Update`** (Zeile ~119), im `else`-Zweig zu `inf.Shares.Count > 0`:
   ```csharp
   else if (Bandits.IsEstablished(u, sector.Owner))
   {
       var old = sector.Owner;
       sector.Owner = default;                                  // herrenlos
       if (u.Factions.TryGetValue(old, out var fo)) fo.Territory.Remove(sector.Id);
       u.Events.Publish(new SectorOwnerChanged(sector.Id, old, default));
       changed = true;
   }
   ```
   Das ist der Weg „Sektor verlieren": keine eigene (aktive) Station mehr im Sektor → Besitz fällt. (Ohne diese Zeile behielte
   ein Sektor ohne Stationen seinen Besitzer — `Politics.cs:121` — und die Banditen könnten nie verlieren.)
6. **`SectorControl.Compute`** (Zeile ~66), in der Stationsschleife:
   ```csharp
   // Ausbau Punkt 17: ein ausgeschalteter Sitz zählt keinen Einfluss — nur so können Banditen einen Sektor verlieren
   // (Verstecke werden bei Hülle 0 ausgeschaltet statt zerstört) und nach der Reparatur zurückgewinnen.
   if (st.Condition is { Disabled: true } && Bandits.IsEstablished(u, st.Owner)) continue;
   ```
   **Warum das nötig ist:** `Compute` überspringt heute nur Baustellen und unbezahlte Regierungsstationen; eine ausgeschaltete
   Station zählt ihren Einfluss weiter. Für Staaten ist das richtig (ein HQ im Notbetrieb hält den Anspruch), für eine Bande, die
   gerade ihren Sitz verloren hat, wäre es der ewige Besitzanspruch: der Sitz ist unzerstörbar (Versteck → nur „ausgeschaltet") und
   hätte mit Einfluss 3 immer 100 % — der Sektor könnte nie wechseln. Mit der Zeile gilt: Sitz ausgeschaltet → kein Einfluss →
   Sektor fällt (an wen auch immer, sonst herrenlos); `StationRepairSystem` setzt den Sitz nach der Belagerung auf 25 % Hülle zurück,
   der Einfluss kehrt zurück, der nächste Kontrollschritt holt den Sektor (`OwnerSwitchMargin` greift nicht, weil der bisherige
   Besitzer 0 ist). Nur so ist „kann Sektoren erobern **und** verlieren" eine echte Regel statt einer Behauptung — und weil sie an
   `IsEstablished` hängt, ändert sich für alle anderen Stationen nichts (kein Kampf in der Standardwelt → ohnehin keine
   ausgeschaltete Station).

### 7.2a `Simulation/Politics.cs` — Durchsetzung ohne Verwaltungskosten

`FactionPolicySystem.PayEnforcement` (Zeile ~294) rechnet heute `LockUpkeepPerLevel × Stufe × dt` aus der Kasse des Besitzers und
setzt `Enforced` falsch, wenn sie nicht reicht. Ergänzen:

```csharp
float perLevel = Bandits.IsEstablished(u, sector.Owner) ? u.Rules.BanditTollUpkeepPerLevel : u.Rules.LockUpkeepPerLevel;
float cost = perLevel * acc.EntryLevel * dt;
```

und bei `cost <= 0` **nicht** buchen, aber `Enforced = true` setzen (die Bande setzt den Zoll mit ihren eigenen Schiffen durch).
Zusätzlich in `Desired`/`Decide` nichts ändern — der Banditenzweig (7.2.1) entscheidet allein.

**Zahlenbeleg:** Stufe 1, `LockUpkeepPerLevel` 0,5 → 0,5 Cr/Tick = 10 000 Cr je 20 000 Ticks. Der Zoll bringt bei einer Sackgasse
wie Abgrund (wenige Durchreisende, je 8 Cr) realistisch 300–3000 Cr je 20 000 Ticks. Mit den normalen Kosten wäre die Sperre also
nach 600 Ticks „auf dem Papier", `PolicyCheck` ließe alle durch und der Zoll wäre weg — genau das Gegenteil des Ziels.

### 7.3 `Simulation/PirateSystem.cs` — Grenzen und Selbstanzeige

1. **`Book`** (Zeile ~518): `float capUnits = Bandits.IsEstablished(u, a) ? u.Rules.BanditMaxRaiderUnits : r.MaxRaiderUnitsPerActor;`
   und `capUnits` statt `r.MaxRaiderUnitsPerActor` verwenden. Alles andere (Bau im Versteck, `CombatModels.ForPirates`,
   `RaiderRebuildTicks`, `LastForceTick`, `RaiderBuilt`) bleibt.
2. **`ResolveRaid`** (Zeile ~306): Hehlerei-Deckel
   `float cap = Bandits.FenceCap(u, pirates-actor);` statt `r.PirateTreasuryCap` (0 = ungedeckelt).
3. **`ResolveRaid`** (Zeile ~327): `if (faction != raider.Owner) Wanted.Report(u, raider.Owner, faction, "piracy");` —
   sonst meldet eine Bande Piraterie gegen sich selbst, sobald sie den Sektor besitzt (Fahndung bei sich selbst, Kopfgeld auf die
   eigenen Kaperer). In der heutigen Welt ist der Kaperer nie Sektorherr → verhaltensgleich.

### 7.4 `Simulation/UpkeepSystem.cs` — Steuerdeckel

`FiscalFactors` (Zeile ~135): `var npc = u.NpcFactionIds().Where(f => !Bandits.IsEstablished(u, f)).ToList();`. Damit bleibt der
Deckel für Union/Gilde/Montanbund **rechnerisch unverändert** (gleiche Fraktionszahl, gleiche Zielsumme), und die Banditen nehmen
ihre Abgaben voll ein (`FactorOf` liefert für sie 1).

### 7.5 `Simulation/FactionBudget.cs` — keine Subventionen an Banditen

`SubsidySystem.Step` (Zeile ~211): `foreach (var f in u.NpcFactionIds()) { if (Bandits.IsEstablished(u, f)) continue; … }` — die
Banditen stützen ihre Stationen selbst (oder lassen sie verfallen, § 13).

### 7.6 `Simulation/ForceCommands.cs` — nichts

`PirateCommands.Step` läuft schon über alle Gesetzlosen; die höhere Grenze kommt aus `Book` (§ 7.3 Nr. 1). Die Schutzflotte läuft
nicht über `NavalBattleSystem`/`NavySystem` (beide verlangen `Wars.CanWage`/Krieg), sondern ausschließlich über den `BanditSystem`-
Befehl aus § 5.2 Nr. 2. Keine Änderung nötig.

### 7.7 `Comms/CommsActions.cs`, `Comms/DiplomacyActions.cs`, `Comms/Alliances`-Aktionen

Angebotslisten, die `NpcFactionIds()` durchlaufen und nicht schon `Wars.CanWage`/`Wars.IsParty` prüfen (Bündnis, Waffenstillstand,
Tribut, Vermittlung): `if (Bandits.IsEstablished(u, f)) continue;` — der Spieler soll keine unerfüllbaren Diplomatie-Zeilen für eine
Bande sehen. `FineAction`/`IntelMarket` sind unkritisch (Fahndung gibt es nur nach Verbrechen; Intel verweigert bei feindlicher
Beziehung ohnehin).

### 7.8 `Simulation/StationReview.cs`, `Departures`, `Capitulation`, `Wars`, `NavySystem`, `SecuritySystem`

**Keine Änderung.** Zur Sicherheit geprüft:
- `StationReview.Reviewable` schließt geschützte Stationen aus — die Banditenstationen sind `essential`, also nicht stilllegbar;
  sollte man das `essential` weglassen, greift die normale Prüfung (Grund c) und die Station verfällt, was zulässig ist.
- `Capitulation.IsPirate` deckt „Besitzer gesetzlos" → nie Kapitulation, bei Hülle 0 zerstört (`FallToZero`), Verstecke nur
  ausgeschaltet.
- `SecuritySystem.Manage`, `NavySystem`, `FactionViabilitySystem`, `SeatRelocation`, `FactionBuilderSystem`,
  `Investors.ForStation`, `Investors.ForShip`: überspringen Gesetzlose bereits (Zeilen in `SecuritySystem.cs:129`,
  `Wars.CanWage`, `SeatRelocation.cs:26`, `FactionBuilderSystem.cs:33`, `Actors.cs:173`, `FleetSystem.cs:182`).
- `Maintenance.Applies` verlangt `u.FactionOf(st) is not null` — mit Fraktionseintrag gilt das für die drei Werke (Unterhalt,
  Instandhaltung, Verschleiß); der Sitz ist ein Versteck und damit ausgenommen. Das ist gewollt: die Werke müssen gepflegt werden.

### 7.9 `Combat/StationCombat.cs` — Geschütze schießen nicht auf Unbeteiligte

`StationCombat.Hostile` (Zeile ~86) erklärt jeden zum Feind, dessen Beziehung zum Besitzer ≤ `HostileRelation` (−0,15) ist. Für
eine Banditenstation trifft das **jeden** (Vorgabe −0,6): sobald in Abgrund irgendein Gefecht läuft — ein Überfall der Klingen auf
einen Händler, eine Polizeiverfolgung —, feuern die Geschütze des Sitzes und der Werke auf **alle** Teilnehmer außer den eigenen.
Das ist kein Banditenverhalten, sondern ein Flächenbrand. Ergänzen, direkt nach der Besitzer-/Fraktionszeile:

```csharp
// Ausbau Punkt 17: die Geschütze einer Banditenstation treffen nur, wer sie oder ihre Schiffe angreift —
// nicht jeden Unbeteiligten (die Vorgabe −0,6 macht sonst jeden zum Feind).
if (Bandits.IsEstablished(u, st.Owner) && e.Station != st.Id && !Wars.AtWar(u, st.Owner, ship.Owner)) return false;
```

Ausnahmen bleiben: die eigene Belagerung (`e.Station == st.Id`) und ein laufender Krieg (Gesetzlose führen keinen, aber der Zweig
soll für spätere Welten korrekt sein). Damit greifen die Geschütze im Normalfall nur, wenn der Sitz selbst belagert wird — genau
wie ein Versteck.

### 7.10 `Combat/Capitulation.cs` und `Combat/EconomicHoldout.cs` — nichts

`Capitulation.TakerFor` verlangt für KI-Angreifer `Kind != Outlaw` und `Wars.Hostile` — Banditen erobern also nie, und ihre
Stationen kapitulieren nie (`IsPirate`). `EconomicHoldout` (Alarm, Notschild, mehr Geschütze) gilt für **Wirtschaftsstationen ohne
eigenen `defense`-Abschnitt** — die drei Werke der Banditen sind genau das und bekommen damit Notschild und Alarm. Der Alarm ruft
`EconomicHoldout.AlliedDefenders` (Verbündete) und `PoliceCommands`/`NavySystem` der Besitzerfraktion; für Gesetzlose läuft beides
ins Leere, **die Schutzflotte** verteidigt sie über `Siege.Reinforce` (Rolle `Warship`, `u.FactionOf(s) in defenders`). Genau
deshalb ist die Schutzflotte eine Pflicht und keine Kür. Keine Änderung nötig.

---

## 8. Determinismus und Rechenzeit

- `BanditSystem` zieht **keinen** Zufall: feste Id-Reihenfolge, feste Typenliste, erster freier Platz über
  `Outposts.FreeSpot` (deterministisch, kein `Rng`). Damit ist Speichern/Laden/Weiterlaufen bitgleich, ohne neue Regeln.
- Kein Einfluss auf die heißen Schleifen (Markt, Händler): der Takt 50 berührt höchstens eine Handvoll Stationen; die Lagekarte
  ändert sich nur über die vorhandenen Stationen.
- Die Zollprüfung ist Teil des bestehenden `GateAccess`-Pfads und kostet nichts zusätzlich (Stufe 1 ist eine Zahl, kein neuer Check).

---

## 9. Speichern, Laden, Hash

| Was | Wo | Zustand |
|---|---|---|
| `Actor.LastBanditTick` | `ActorSave.LastBanditTick` (`[JsonIgnore(WhenWritingDefault)]`) | additiv, v17 bleibt |
| Sektor, Sperre, Kasse, Stationen, Schiffe, Lager | bestehende Felder | unverändert |
| Regeln | `SaveGame.Rules` (ganze `EconomyRules`) | additiv, keine Migration |

- `UniverseSerializer`: `LastBanditTick` in `ToSave`/`FromSave` durchreichen (Muster `LastFoundTick`).
- `UniverseHash`: in der Akteursschleife (Zeile ~156, neben `LastFoundTick`) nur wenn gesetzt:
  `if (a.LastBanditTick != 0) Mix((ulong)a.LastBanditTick + 0x1701u);`
- **Keine** Versionserhöhung, **keine** Migration: alte Stände kennen keine Banditen (Feld 0), neue laden ohne Sonderfall.
- `Persistence/SaveGameJson.cs`: `AfterV17Keys` um `"bandit_seat"` ergänzen, damit die eingefrorene Migrationskette den Typ nicht
  in alte Stände trägt. Die Fraktion und ihre Stationen tauchen in keiner Migration auf (Migrationen fügen nur Typen aus festen
  Listen hinzu; `bandit_seat` ist ein Versteck und fällt aus dem `MigrateV7ToV8`-Filter).

---

## 10. Rücknahme-Helfer (Referenz-Hash-Tests)

`tests/SpaceSim.Tests/BanditenTests.cs`:

```csharp
/// <summary>
/// Ausbau Punkt 17 zurücknehmen (Referenz-Hash-Tests; Teil der Rücknahmekette über <see cref="BalanceFixTests.RevertBalanceFix"/>
/// und <see cref="GasNebulaTests.RevertGas"/>): Fraktion 4, ihre vier Stationen und Schiffsgruppen entfernen, Sektor 11 wieder
/// herrenlos, Obergrenze zurück, Schalter aus. Der Weltaufbau liesse den Inhalt sonst selbst weg (WorldBuilder) — hier wird die
/// WorldDef zusätzlich bereinigt, weil Tests sie direkt untersuchen.
/// </summary>
internal static void RevertBandits(WorldDef def)
{
    foreach (var sd in def.Sectors)
    {
        sd.Stations.RemoveAll(s => s.Id is >= 91 and <= 94 || s.Owner == "bandits");
        if (sd.Id == 11) sd.Owner = 0;
    }
    def.Ships.RemoveAll(g => g.Faction == 4);
    def.Factions.RemoveAll(f => f.Id == 4);
    var r = def.Rules ??= new EconomyRules();
    r.BanditFactions = false;
    if (r.MaxStations == 78) r.MaxStations = 75;
}
```

- **Einhängen:** als **erste** Zeile in `BalanceFixTests.RevertBalanceFix` und in `GasNebulaTests.RevertGas` (beide sind
  Sammel-Rücknahmen; die neueste Runde ruft die vorherigen auf, deshalb an beiden Stellen — Muster `RevertSalvage`).
- Reihenfolge: `RevertBandits` **vor** `RevertGas`, weil es nur eigene Inhalte entfernt und die Obergrenze zuerst auf 75 bringt.
- `MaxStations` **hier** zurücknehmen, nicht im `WorldBuilder`: sonst zöge die Builder-Regel (fehlende Wirtschaftsstationen) ein
  zweites Mal ab. Beide Wege sind so gebaut, dass immer nur einer greift: entweder fehlt der Inhalt (Helfer hat ihn entfernt) oder
  der Schalter ist aus (Builder zählt `omittedEconomic`).

---

## 11. Tests

### 11.1 Neue Datei `tests/SpaceSim.Tests/BanditenTests.cs`

1. **`Schalter_CodeAus_StandardweltAn_RuecknahmeAus`** — `new EconomyRules()`: `BanditFactions` false, `BanditTollLevel` 1,
   `BanditWarships` 2, keine Ausbausektoren. Standardwelt: an, Liste `[9]`. `RevertBandits` → gebaute Welt hat keine Fraktion 4,
   keine Station 91–94, `MaxStations` 75, Sektor 11 herrenlos, 6000 Ticks ohne `SectorOwnerChanged` und ohne `TollPaid` an 4.
2. **`Inhalt_Fraktion_Sitz_Werke_Flotten`** — Fraktion 4 existiert, `Actor.Kind == Outlaw`, `Membership == 4`,
   `IsEstablished` true; `bandit_seat` ist `Hideout`, `Security` 1.2, `Influence` 3, Dienste `trade/blackmarket/hangar`;
   drei Werke mit Rezepten und ohne Modul-Ausgang; 2 Kaperer (`Role Pirate`) und 2 Schutzschiffe (`Role Warship`) mit
   `HomeBase` = Sitz; `IsEstablished` false für „Rote Korsaren", „Schwarze Klingen" und ein aufgestiegener Einzelakteur.
3. **`Platzierung_…`** — `StationPlacement.Check` für alle vier Stationen frei (Sitz 22, Werke 15 Radius; Mindestabstand
   Sitz↔Werk 57, Werk↔Werk 50 — Ist ≥ 73,8); jede > 108 vom Gürtelmittelpunkt (damit `BeltPlacement.Fits` jeden Felsen zulässt)
   und > 57 von beiden Toren; `AsteroidGenerator` erreicht im Gürtel weiter 12 Asteroiden; `StationProtection.CountEconomic(u) == 72`,
   `MaxStations == 78` (Spielraum 6 wie vorher 75 − 69).
4. **`SektorAbgrund_GehoertDenBanditen_DominanzUndZoll`** — nach dem Aufbau `Sectors[11].Owner == 4`, `Control == Dominance`,
   `SectorControl.MaxLevel == 2`, `Territory` enthält 11; Nebelmark bleibt `Owner == 0`; nach 40 Ticks `Access.EntryLevel == 1`,
   `ExpiresTick == -1` (Dauerzoll), `Enforced` true.
5. **`Zoll_WirdErhoben_UndFliesstInDieKasse`** — freier Händler reist durch Abgrund: `TollPaid` mit `To == 4`, Kasse + Betrag,
   `Level 1 × GateTollPerLevel`; eigenes Kaperschiff passiert ohne Zoll (`IsMember`); Spieler mit Fracht wird **nicht** abgewiesen
   (`CargoCheck` greift erst ab Stufe 2); Betrag ist kleiner als ein Ladungswert.
6. **`KeineFremdsperren_UndKeineBeziehungsdrift`** — 20 000 Ticks: `FactionPolicySystem.WorstRelation(union)` liefert **nicht** 4;
   `Relations.StoredEntries` unverändert gegenüber einer Welt ohne Banditen; `SectorAccessChanged` ausschließlich für Sektor 11 (und
   9 nach Ausbau); `Relations.Effective(4, X) == RelationOutlaw` für alle X, auch nach 20 000 Ticks. Zusätzlich die Gegenprobe zur
   Wirkung: mit **ausgebautem** § 7.2 Nr. 2 (Test-Hilfsweg: Ausnahme temporär aus) steigt `WorstRelation(Union)` auf −0,6 und
   `Desired(Union-Sektor)` auf Stufe 3 — der Test belegt so, dass die Ausnahme wirklich das ist, was die Sperre verhindert.
7. **`KeineDiplomatie_KeinKrieg_KeineBuendnisse`** — `Wars.CanWage(4)` false, `DeclareIssue` verweigert, `Alliances.FormIssue`
   verweigert, `AllianceAction`/`WarAction` führen keine Zeile für 4, `Wars.WarshipsOf` enthält die Schutzflotte nur über die
   Rolle (nicht als Kriegspartei).
8. **`Schutzflotte_Kauf_Unterhalt_UndRuecklage`** — leere Flotte, reiche Kasse → nach `Interval` zwei Schiffe am Sitz, Kasse um
   Materialwert gesunken, `TreasuryUpkeep "bandit_fleet"` gebucht; Kasse unter `BanditWarshipReserve` → kein Kauf; Unterhalt
   mindert die Kasse und wird als unbezahlt gemeldet, wenn sie leer ist.
9. **`Schutzflotte_VerteidigtEigenesWerk`** — `Siege.Start` eines fremden Angreifers auf Station 92, zwei Schutzschiffe im Sektor →
   `Siege.Reinforce` nimmt sie auf Seite 1 (`e.Of(id).Side == 1`), das Gefecht läuft (Hüllenschaden auf beiden Seiten).
10. **`Kaperflotte_NutztDieHoehereGrenze_UndBautImSitzNach`** — `Book`-Schritt mit leerer Flotte und Kasse: Grenze
    `BanditMaxRaiderUnits` (3 Einheiten) statt `MaxRaiderUnitsPerActor` (2); zwei Korsaren = 2 Einheiten, ein dritter erst bei
    Platz; nach Verlust eines Kaperschiffs entsteht nach `RaiderRebuildTicks` Ersatz am Sitz; an der kleinen Gruppe ändert sich
    nichts (Grenze 2).
11. **`Geschuetze_TreffenKeineUnbeteiligten`** — Gefecht zwischen einem Klingenschiff und einem freien Händler in Abgrund
    (`Siege`-frei, `EncounterCause.Raid`): die Geschütze von Sitz und Werken feuern **nicht** (`StationShot`-Ereignisse leer);
    Belagerung des Sitzes durch einen fremden Verband → sie feuern auf die Angreifer. Ohne die Zeile aus § 7.9 schlagen beide
    Prüfungen fehl (Gegenprobe im Test dokumentiert).
12. **`Wirtschaft_WirdNachgebaut_KostetGeld_UndBrauchtZeit`** — Station 92 per `Departures.RemoveStation` entfernt → vor Ablauf des
    Cooldowns nichts, danach genau ein Neubau vom Typ `steelworks` im Sektor 11, Kasse − Baukosten × `BanditRebuildMoneyFactor`,
    `TreasuryUpkeep "bandit_build"`, `LastBanditTick` gesetzt; ohne Geld kein Bau.
13. **`Vorposten_NimmtNebelmark_NurEinmal_UndNieMitSpielerbesitz`** — reiche Kasse, `BanditExpansion` an → nach Cooldown ein
    `warehouse` in Sektor 9, `SectorOwnerChanged(9, 0, 4)`, `Territory` enthält 9; zweiter Lauf baut keinen zweiten; mit einer
    Spielerstation (oder einem Spielersitz) in 9 bleibt 9 herrenlos.
14. **`SektorVerlieren_OhneEigeneStation`** — die drei Werke in 11 entfernt (Sitz bleibt) → `SectorControlSystem`-Schritt setzt
    `Sectors[11].Owner == 0`, `Territory` ohne 11, `SectorOwnerChanged`; die Fraktion bleibt handlungsfähig
    (`FactionViability.Check` leer, weil der Sitz als Versteck zählt). Zusatz: nach dem Neubau der Werke (Cooldown, Geld) und dem
    nächsten Kontrollschritt gehört 11 wieder der Fraktion (`SectorOwnerChanged(11, 0, 4)`) — der Beweis für „kann Sektoren
    gewinnen **und** verlieren".
15. **`Kasse_BleibtErklaerbar_20000Ticks`** — Kasse = Start + `TollPaid` + `TaxCollected` + `TreasuryIncome` („fence", „blackmarket")
    + Stationserlöse − `TreasuryUpkeep` („bandit_fleet", „bandit_build", „bandit_share", „maintenance", „station_repair") −
    Stationskäufe, Toleranz 1 Cr; kein `TreasuryIncome`/`Upkeep` mit unbekanntem Zweck.
16. **`SpeichernLaden_BitIdentisch`** — mitten im Lauf speichern/laden: `UniverseHash` gleich, `LastBanditTick` erhalten, 600 Ticks
    Weiterlauf bitgleich (beide Welten).
17. **`Schwarzmarkt_UndFahndung_AmSitz`** — Sitz hat `blackmarket`: Spieler verkauft Beute/Kauft Hehlerware (Kasse,
    `BlackMarketReserve`), Kaperer-Beute landet im `BlackStock` des Sitzes; ein Überfall der Banditen in einem fremden Sektor erzeugt
    Fahndung **bei der dortigen Fraktion** und ein Kopfgeld, das am dortigen Büro abholbar ist — **nicht** bei der Banditenfraktion
    (Selbstanzeige, § 7.3.3).
18. **`NpcWeltOhneBanditen_Bitgleich`** — `RevertBandits` → 20 000 Ticks `UniverseHash` gleich dem Referenzwert des Stands davor
    (Wert beim Umsetzen eintragen).
19. **`NurDieKleinenGruppen_VerhaltenUnveraendert`** — Welt ohne Fraktion 4: Korsaren/Klingen mit Grenze 2, kein Zoll in
    Delta/Abgrund (`Access.EntryLevel == 0`), `WorstRelation` unverändert, 6000 Ticks Hash gleich einer Welt, in der
    `BanditFactions` an ist (der Schalter allein darf nichts tun).

### 11.2 Selbsttest (Godot, `godot/scripts/SelfTest.Bandits.cs`, Stufe 151, `LastStage` 150 → 151)

- Sektorkarte: Abgrund zeigt den Banditenbesitz (Name/Kürzel), Detailkarte nennt „Zoll Stufe 1" und „Besitzer".
- Sprung nach Abgrund mit dem Spielerschiff: Zoll wird abgebucht (Toast), Kassenanzeige der Fraktion im Fraktionen-Detail steigt.
- Andocken am Sitz: Kacheln Handel, Schwarzmarkt, Hangar; Schwarzmarkt verkauft/kauft (echte Taps).
- Stationsinfo der Werke: Besitzer = Banditenfraktion, Zustand/Rentabilität wie bei jeder Station.
- `--ui-shots`-Zustand `kz-banditen` (Sektoransicht Abgrund + Stationsinfo Sitz).

### 11.3 Anpassungen bestehender Tests (mit Grund)

| Test | Änderung | Grund |
|---|---|---|
| `Phase21Tests.Welt_DritteFraktion_HerrenloseSektoren_ZweitePiratengruppe` | `Factions.Count` 4 → 5, `NpcFactionIds` 3 → 4; Abgrund (11) ist nicht mehr herrenlos, nur Nebelmark (9) | neue Fraktion 4 besitzt 11 |
| `ActorTests.Beziehungen_Vorgaben_Abweichungen_Sparsam` | `stored == Factions.Count * (Factions.Count - 1)` → Anzahl der **politischen** Fraktionspaare (Gesetzlose speichern keine Beziehungen) | Fraktion 4 speichert keine Beziehungen (§ 6.1) |
| `ActorTests` Geld-/Kassentests, die über `IsFaction` filtern | Banditenfraktion einschließen **oder** ausschließen und die neuen Zwecke (`bandit_*`) in die Erklärformel aufnehmen | neue Kassenbewegungen |
| `EigeneStationenTests.Bauen_…`: `Assert.Null(PlayerBuilding.SectorIssue(u, 11))` | erwartet jetzt keinen Freibrief: mit `PlayerBuildForeignSectors` an ist Bau in Alpha erlaubt, in Abgrund greift die **feindliche Beziehung** (`ForeignIssue`, −0,6 ≤ `HostileRelation`) — Assertion auf „feindlich" umstellen | Abgrund hat einen Besitzer |
| `FirmensitzTests`: `SeatIssue(Abgrund)` enthält „Piratenversteck" | erwartet „gehört … — nur herrenlose Sektoren" (die Besitzerprüfung steht vor der Versteckprüfung) | dito |
| `Phase32Tests`, `SpielerHqTests`, `Runde45`-Sitztests | **keine** Änderung — sie nutzen Nebelmark, das herrenlos bleibt | bewusste Ortswahl (§ 2.2) |
| `CargoWaterOreTests`, `FuelResearchDefaultTests`, `ExportPlanetTests` | Zahlen bleiben gültig (78 bzw. 78 − 9 = 69), nur die Vergleiche gegen `FileMaxStations()` prüfen | `maxStations` 78 |
| `PoliticsTests` 20000-Ticks-Spielwelt | muss weiter „0 Sperren außerhalb Abgrund, Karte zusammenhängend" liefern; **wenn nicht**, ist § 7.2.2 falsch umgesetzt | die kritische Invariante |
| `ContentTests` Stations-/Typenzahlen | +1 Typ (`bandit_seat`), +4 Stationen | neuer Inhalt |

---

## 12. Balance-Erwartungen und Messverfahren

Messung: `dotnet build -c Release` (Pflicht, sonst läuft ein alter Stand), dann je Lauf

```
dotnet run --project tools/SpaceSim.Balance -- --ticks 20000 --every 2000
dotnet run --project tools/SpaceSim.Balance -- --ticks 60000 --bandits off   # Vergleich
```

Neue Ausgabezeile **„Banditen (Punkt 17)"**: Besitzer von 11/9, Zollstufe, eingenommene Zölle (Anzahl, Cr), Kasse/Verlauf,
Wirtschaftsstationen (Typ, Bestand, Ø Preis/Basis letzte 25 %), Kaperer/Schutzschiffe (Anzahl, Verluste, Neubauten), Nachbauten,
Vorposten, Beuteanteil, `LastBanditTick`. Option `--bandits on|off` (setzt `Rules.BanditFactions` vor dem Aufbau).

**Erwartung (Abnahme):**

| Kennzahl | Erwartung |
|---|---|
| Urteil Preise | „Preise im Zielbereich" bei 20 000 und 60 000, an und aus |
| Sperren | 0 Sperren außerhalb der Banditensektoren; Abgrund dauerhaft Stufe 1, `Enforced` true |
| Karte | zusammenhängend, `iso` 0 (Zoll ist keine Sperre); Ausbau nach Nebelmark erzeugt `SectorOwnerChanged`, keine Teilung |
| Verlustquote | +0 bis +0,15 pp gegenüber `--bandits off` (eine zusätzliche Kaperflotte von 3 Einheiten) |
| Händlerpleiten | 0 zusätzliche (die Banditen kaufen, sie verkaufen nicht unter Preis) |
| Stahl/Hüllenplatten/Elektronik | Ø Preis/Basis 0,6–1,6 in der letzten 25 %; kein `HINWEIS` für diese drei Waren (sonst produzieren die Werke zu viel: `BanditRebuildMoneyFactor`/Startbestände senken) |
| Energie/Wafer/Titan | nicht über 2,0 (neue Nachfrage); wenn doch, Werke auf zwei reduzieren |
| Banditenkasse | 20 000–150 000 bei 60 000 Ticks; **kein** lineares Wachstum über 300 000 (Beuteanteil greift) |
| Zölle | 500–20 000 Cr bei 60 000 Ticks (Abgrund ist Sackgasse); nach Ausbau Nebelmark deutlich mehr, dann Verlustquote erneut prüfen |
| Rechenzeit | +≤ 2 % ms/Tick |
| Handlungsfähigkeit | Banditenfraktion in jeder Probe handlungsfähig (Sitz unzerstörbar); `CannotHunt` 0 % |

**Wenn eine Erwartung reißt, in dieser Reihenfolge drehen:** (1) `banditRebuildMoneyFactor`/Startkasse (Geld), (2)
`banditMaxRaiderUnits` (Verlustquote), (3) `banditTollLevel` (nur 0/1), (4) `banditExpansion` aus (Karte/Verkehr), (5) Werke-Typen
tauschen (Preise). **Nicht** drehen: `maxStations`, die Sitz-Sicherheit 1.2 (Dominanz) und die Ausnahmen in § 7.2.

---

## 13. Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| **Fremdsperren** durch −0,6 (jede KI-Fraktion sperrt Stufe 3, auch gegen freie Händler) | Karte zersplittert, Händler stehen, Preise kippen | § 7.2.2 (`WorstRelation` überspringt Etablierte) **und** § 7.2.1 (Dauerzoll im eigenen Sektor) + Test 11.1 Nr. 6; `PoliticsTests`-Langlauf als Wächter |
| **Handlungsfähigkeit** | Sitz zerstörbar? Nein (`IsPirate`/`FallToZero`). Kasse leer? `FactionViability.Check` fordert für Gesetzlose nur Versteck + Jagdfähigkeit; `PatchAtBase` hält Kaperer flugfähig, `Spendable` = ganze Kasse | Test 11.1 Nr. 13; Balance-Zeile „Handlungsfähigkeit" muss 0 % Verstöße zeigen |
| **Wirtschaftsgleichgewicht** | drei neue Produzenten senken Stahl-/Platten-/Elektronikpreise, neue Nachfrage nach Energie/Wafer/Titan hebt diese | kleine Startbestände, `essential` verhindert Stilllegung, Messung § 12; im Zweifel nur zwei Werke |
| **Stationszahl** | `maxStations` 78; je Sektor 10 (Abgrund: 3 wirtschaftliche + Sitz + Klingen-Versteck) | Test 11.1 Nr. 3; `omittedEconomic` im Builder hält den Spielraum beim Ausschalten |
| **Zwei Gruppen in Abgrund** | Klingen und Banditen teilen sich einen Sektor; Fahndung der Klingen bei den Banditen | bewusst (§ 2.2), Selbstanzeige unterdrückt (§ 7.3.3) |
| **Spieler** | −0,6 = feindlich, aber Stufe 1 lässt ihn mit Fracht herein; Handel am Sitz möglich; ab Fahndungsstufe 3 verweigert `TradeActions.Refusal` | gewollt („Geschäft mit Banditen ist möglich, aber unangenehm"); Test 11.1 Nr. 5/16 |
| **Nebelmark** | Ausbau nimmt der Spielerfraktion den letzten Gründungsort | Regel prüft „herrenlos **und** ohne Spielerbesitz" (§ 5.2 Nr. 4); Test 11.1 Nr. 12 |
| **Determinismus** | neuer Takt 50 mit eigenem Zustand | kein `Rng`, feste Reihenfolgen; Test 11.1 Nr. 15 |
| **Rechenzeit** | vernachlässigbar (≤ 10 Stationen/Schiffe je Fraktion) | Messzeile `--systemtime`; Abnahme +≤ 2 % |
| **Namenswürfel** | `randomNames: true` benennt die Fraktion im Spiel neu | akzeptiert (wie bei den heutigen Gruppen); Tests mit `TestWorlds.Def` |
| **Hash-Referenz** | jede Änderung an `PirateSystem`/`Politics` könnte den Referenzlauf verschieben | alle Änderungen sind an `IsEstablished` gebunden; `RevertBandits` + Test 11.1 Nr. 17/18 belegen Bitgleichheit |

---

## 14. Schritt-für-Schritt-Plan

1. **Regeln.** `Model/EconomyRules.Bandits.cs` anlegen (§ 4). Prüfen: `dotnet build` 0 Warnungen; `new EconomyRules()` hat
   `BanditFactions == false`.
2. **Reine Regeln.** `Simulation/Bandits.cs` mit `IsEstablished`, `Seat`, `Economic`, `HoldsSector`, `RaiderUnitsCap`, `FenceCap`,
   `Spot`, `BuildIssue`, `Text` (§ 5.1).
3. **Inhalt.** `bandit_seat` in `stationTypes`; Fraktion 4; Sektor 11 `owner: 4` und Stationen 91–94; Schiffsgruppen; Regeln;
   `maxStations` 78 (§ 6). Prüfen: Welt baut, `SectorControl` liefert Dominanz, `StationPlacement`-Test (11.1 Nr. 3) grün.
4. **WorldBuilder.** Ausschlussweggefallener Fraktionen/Sektoren/Stationen/Schiffe, `omittedEconomic`, `PlaceAtHomeBase` für
   `Warship` bei Gesetzlosen (§ 7.1, § 6.4). Prüfen: `BanditFactions = false` → Welt wie vorher (Zahlen: 69 wirtschaftliche
   Stationen, `MaxStations` 75).
5. **Politik.** `Decide`-Dauerzoll, `WorstRelation`/`StepRelations`/`LocksAgainst`-Ausnahmen, `SectorControl`-Verlust (§ 7.2), und
   in `PayEnforcement` die Kostenzeile aus § 7.2a. Prüfen: `PoliticsTests` komplett grün, 20 000-Ticks-Spielwelt 0 Sperren außer
   Abgrund, `Enforced` dort true.
6. **Piraten.** Grenze, Hehlerei-Deckel, Selbstanzeige (§ 7.3). Prüfen: `SecurityTests`, `SchwarzmarktTests`, `SchwarzmarktNpcTests`
   grün; 20 000-Ticks-Hash der Welt ohne Banditen unverändert.
7. **Kassen.** Steuerdeckel-Ausnahme, Subventions-Ausnahme (§ 7.4, § 7.5); `LastBanditTick` in `Actor`, `ActorSave`,
   `UniverseSerializer`, `UniverseHash` (§ 9).
8. **System.** `BanditSystem` + `BanditModule` (§ 5.2), Registrierung nach `SecurityModule`.
9. **Comms.** Ausnahmen in den Angebotslisten (§ 7.7).
10. **Rücknahme.** `BanditenTests.RevertBandits`; in `BalanceFixTests.RevertBalanceFix` und `GasNebulaTests.RevertGas` einhängen
    (§ 10). Prüfen: die bestehenden Referenz-Hash-Tests laufen unverändert.
11. **Tests.** `BanditenTests` (11.1 Nr. 1–18), Anpassungen nach 11.3. Prüfen: `dotnet test --filter "FullyQualifiedName~Banditen"`
    und danach die volle Suite.
12. **Anzeige.** `FactionsScreen` (Banditen als Gesetzlose mit Sitz/Werken/Zoll — `Bandits.Text`), Stationsinfo-Zeile am Sitz,
    Sektor-Detail „Zoll Stufe 1 · Besitzer", Sektorkarte Abgrund. Optional Rotton in `MapOverlay.FactionColor` für Id 4.
13. **Selbsttest.** Stufe 151 (§ 11.2), `LastStage` 151; `--ui-shots`-Zustand.
14. **Balance.** Release bauen, 20 000/60 000 an/aus, Saaten 1–4 an/aus, Zahlen in § 12 eintragen; bei Abweichung nach der
    Reihenfolge in § 12 drehen.
15. **Dokumentation.** `CLAUDE.md` (neuer Abschnitt „Zwei Banditenarten (Punkt 17)"), `docs/TODO-AUSBAU.md` Punkt 17 abhaken,
    `docs/POLITIK.md` (Fraktion mit Gesetzlosen-Art), `docs/KAMPF.md` (Schutzflotte/Kaperflotte) je ein kurzer Verweis auf diese
    Datei.

**Definition of Done:** Volle Testsuite grün (keine gelöschten Tests, jede Anpassung mit Grund in 11.3), Selbsttest OK, UI-Check OK,
Balance 20 000/60 000 „Preise im Zielbereich" an und aus, Banditenkasse ohne lineares Wachstum, 0 Sperren außerhalb der
Banditensektoren, Referenz-Hash mit `RevertBandits` bitgleich.

---

## 15. Offen (bewusst nicht in diesem Punkt)

- Schutzgeld **verhandeln** (Spieler zahlt regelmäßig und wird nicht überfallen) — gehört zu Punkt 3 (Schutzverträge).
- Banditen, die selbst Schmuggelware in Sperrsektoren bringen (Punkt 5, Schmuggler-KI).
- Missionen am Sitz (Punkt 10) und ein Kopfgeldbüro der Banditen (rechtlich unsinnig, aber denkbar als „Auftragsmord").
- Übernahme einer Banditenstation durch den Spieler (Kapitulation ist für Gesetzlose abgeschaltet).
- Banditen, die eine *andere* Fraktion aus einem Sektor verdrängen (dafür bräuchten sie Baustellen und Influenzwachstum gegen
  etablierte Staaten — das ist Punkt 6/Fehden, nicht Punkt 17).

---

## 16. Umgesetzt (Stand: Branch `banditen`)

Vollständig umgesetzt nach §§ 1–11 mit folgenden Dateien: Core-Code in eigenen Dateien
(`Model/EconomyRules.Bandits.cs` partial, `Simulation/Bandits.cs`, `Simulation/BanditSystem.cs` + `BanditModule` in
`Modules/DefaultModules.cs` und `Simulation.cs`), Daten angehängt in `src/SpaceSim.Core/Content/default-world.json`
(Regeln vor `maxStations` 78, Typ `bandit_seat` am Ende der `stationTypes`, Fraktion 4 „Herren der Nebelmark",
Sektor 11 `"owner": 4` mit den Stationen 91–94, zwei Schiffsgruppen; keine bestehenden Ids geändert),
`Actor.LastBanditTick` additiv in `Actor`/`ActorSave`/`UniverseSerializer`/`UniverseHash` (Save bleibt v17, Schlüssel
`bandit_seat` in `AfterV17Keys`), WorldBuilder-Ausschlussweg bei `BanditFactions = false` (Fraktion/Stationen/Schiffe
weg, `omittedEconomic`, Sektor 11 herrenlos, `PlaceAtHomeBase` nur für `Warship` bei Gesetzlosen), Politik-Ausnahmen
in `Politics.cs` (Dauerzoll in `Decide`, `WorstRelation`/`StepRelations`/`LocksAgainst`, Sektorverlust in
`SectorControl.Update/Compute`, kostenlose Durchsetzung in `PayEnforcement`), Piraten-Ausnahmen in
`PirateSystem.cs` (Einheitengrenze, Hehlerei-Deckel, keine Selbstanzeige), Kassen-Ausnahmen in `UpkeepSystem.cs`
und `FactionBudget.cs`, Gefechts-Ausnahme in `StationCombat.cs` (keine Unbeteiligten im Banditengebiet ohne Krieg),
Comms-Ausnahme in `CommsActions.cs` (`FineAction` gegen etablierte Banditen), Tests in
`tests/SpaceSim.Tests/BanditenTests.cs` (19 Tests inkl. Referenz-Hash `0x8EEEBEA81B038A74UL` mit voller Rücknahme
über `RevertBandits`, eingehängt als erste Zeile in `BalanceFixTests.RevertBalanceFix` und
`GasNebulaTests.RevertGas`), UI-Zeilen in `godot/scripts/Ui/Screens/FactionsScreen.cs` (Banditen-Zeile in der
Detailansicht über `Bandits.Text`) und `StationInfoScreen.cs` (Zeile „Banditen“ an Stationen der Fraktion).

### Abweichungen vom Konzept (mit Grund)

1. **`banditTollRefreshTicks` entfällt.** `FactionPolicySystem.Decide` ist zustandslos pro Aufruf; der Dauerzoll
   bleibt mit `ExpiresTick = -1` dauerhaft, ohne Nachstell-Tick. Die Regel wäre tote Daten gewesen.
2. **Banditen-Zweig in `Decide` steht VOR der Ablaufprüfung** (nicht nach dem Spieler-Block wie im Konzept):
   Andernfalls würde `cur > 0 && Tick >= ExpiresTick(-1)` den Dauerzoll sofort wieder ablaufen lassen.
3. **§ 7.7 Comms braucht fast keine neuen Filter:** `WarAction`/`AllianceAction` filtern bereits über
   `Wars.CanWage` bzw. `Alliances.FormIssue` (Gesetzlose ausgeschlossen), Intel-Markt verweigert Feindliche. Neu ist
   nur: `FineAction.Issue`/`Offers` springen etablierte Banditen („Gesetzlose nehmen keine Strafen“), passend zu
   § 2.7 „nirgends zahlbar“.
4. **Nachbau-Stationen bekommen `Credits = 4000`** (Betriebskapital wie die Werke der Weltdatei), damit der erste
   Einkauf nicht an leerer Kasse scheitert; nur die Baukosten gehen aus der Fraktionskasse.
5. **„Wegzoll“ im Nebelmark bleibt wirkungslos** (Konzept § 6.3 optimistisch): Der Vorposten-Typ `warehouse` hat
   `security` 0 → nur Einfluss-Stufe → `MaxLevel` 0 → kein Zoll in 9. Besitz und Gebiet wechseln trotzdem
   (§ 7.2 greift). Um das zu ändern, bräuchte der Typ `security > 0` — bewusst so gelassen, weil ein zweiter
   Zollsektor die Balance verschiebt (siehe § 12).
6. **`PlaceAtHomeBase`-Aufruf nur für `Warship` erweitert, wenn der Besitzer Gesetzlos ist** — staatliche
   Kriegsschiffe sollen weiterhin die Gründer-Güteklasse würfeln.

### Test-Anpassungen über § 11.3 hinaus

- `Runde38Tests`: Startbeziehungs- und Fiskal-Zählungen schließen etablierte Banditen aus (Vorgabe `RelationOutlaw`
  statt 0,1/0,2; `FiscalFactors` ohne sie; `StoredPlayerEntries >= 2 × Nicht-Banditen`).
- `Runde38Tests` Sperr-Invariante: Paare mit Banditen-Beteiligung werden nicht mehr geprüft (−0,6 wäre Sperre 3).
- `Phase22Tests`/`Phase28Tests` Langläufe: Polizei-/Bauschiff-Mindestbestand und Kassen-Prüfung nur für
  Nicht-Banditen; `KeineKriegsschiffe` prüft nur Nicht-Banditen (die Bande startet mit 2 Wächtern).
- `ConstructionTests`, `ResearchTests`, `Phase27Tests`, `Phase29Tests`, `FlottenbefehleTests`, `Phase28Tests`,
  `SpielersperreTests`: Outlaw-Picks mit `!Bandits.IsEstablished` — die Tests meinen die kleinen Gruppen.
- `BalanceFixTests.Piratensektor_BauErlaubt…` baut jetzt in Delta (Sektor 4, Korsaren-Versteck, Gilde freundlich)
  statt im Abgrund; `EigeneStationenTests` prüft die Versteck-Regel ebenfalls an Delta, am Abgrund die
  Feindlich-Regel. `FirmensitzTests`/`Runde45Tests`: `SeatIssue`/`FoundIssue` am Abgrund erwarten die
  Besitzer-Meldung (steht vor der Versteck-Prüfung).
- `Phase21Tests` wie in § 11.3 beschrieben (inkl. 20 000-Tick- und Determinismus-Test).
- `BanditenTests` selbst: 19 Tests (Schalter, Inhalt, Platzierung, Besitz/Dominanz/Dauerzoll, Zoll-Erhebung,
  keine Fremdsperren nach 20 000 Ticks, keine Diplomatie, Schutzflotte Kauf/Unterhalt/Rücklage, Verteidigung,
  Kaperflotte, Geschütze, Nachbau, Vorposten, Sektor verlieren/zurückgewinnen, Kasse erklärbar, Save-Roundtrip,
  Schwarzmarkt/Selbstanzeige, NPC-Welt bitgleich, kleine Gruppen unverändert).

### Offen (in diesem Schritt bewusst nicht gemacht)

- **§ 11.2 Selbsttest Stufe 151** (`SelfTest.Bandits.cs`, `LastStage` 151) und der `--ui-shots`-Zustand
  `kz-banditen` — Godot-Lauf steht aus (bauen/testen übernimmt jemand anderes).
- **§ 12 Balance-Tool:** Flag `--bandits on|off` und Ausgabezeile „Banditen (Punkt 17)" in
  `tools/SpaceSim.Balance` — nicht implementiert; die 20 000/60 000-Läufe an/aus müssen manuell über die
  Rücknahme (`RevertBandits`) oder den Regelschalter verglichen werden.
- **§ 14 Schritt 15 Verweise** in `docs/TODO-AUSBAU.md` (Punkt 17 abhaken), `docs/POLITIK.md` und `docs/KAMPF.md`.
- **Optionaler Rotton** für Fraktion 4 in `MapOverlay.FactionColor` (Konzept § 14 Schritt 12 „optional") —
  unverändert (Id 4 fällt auf den vorhandenen Lila-Ton).
