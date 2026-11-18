using System.Globalization;
using System.Text;
using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

// Headless-Balancing: lässt die Wirtschaft ohne Spieler laufen und gibt Kennzahlen als Tabelle/CSV aus.
//   dotnet run --project tools/SpaceSim.Balance -- [--ticks 40000] [--every 2000] [--seed N] [--world datei.json] [--csv out.csv] [--security off] [--research on|off] [--fuel on|off] [--retire on|off] [--strict on] [--gates "id:closed|open@tick,..."] [--gatelist on]
// Exit-Codes: 1 Preise instabil (Durchschnitt), 2 Karte zerfallen, 3 nur mit --strict on: HINWEIS-Zeilen (Deckel, Waren ohne Fahrten, leere Kassen)

var opts = ParseArgs(args);
int ticks = Int(opts, "ticks", 40_000), every = Int(opts, "every", 2_000);
ulong? seed = opts.TryGetValue("seed", out var s) ? ulong.Parse(s, CultureInfo.InvariantCulture) : null;

var worldDef = WorldBuilder.Parse(opts.TryGetValue("world", out var path) ? File.ReadAllText(path) : WorldBuilder.DefaultWorldJson());
worldDef.Rules ??= new EconomyRules();
// Phase 13/14: --research on|off und --fuel on|off überschreiben die Regel der Weltdatei — vor dem Weltaufbau, denn abgeschaltete Systeme lassen
// ihre Stationen (Forschungsstationen, Treibstoffraffinerien) in der Welt weg
if (opts.TryGetValue("research", out var resOpt)) worldDef.Rules.ResearchActive = resOpt == "on";
if (opts.TryGetValue("fuel", out var fuelOpt)) worldDef.Rules.FuelActive = fuelOpt == "on";
// Runde 38: --playerfaction on|off — Spieler als eigene Fraktion (Vergleichslauf: der Hash ohne Spielerpolitik muss gleich sein)
if (opts.TryGetValue("playerfaction", out var pfOpt)) worldDef.Rules.PlayerOwnFaction = pfOpt == "on";
if (opts.TryGetValue("ammo", out var ammoOpt)) worldDef.Rules.AmmoActive = ammoOpt == "on";   // Phase 23: Munition (Rezept, Lagerware) an/aus
// Phase 19: --retire off schaltet die Stilllegung/Umwidmung ab (Vergleichslauf mit der Regel bis Phase 18)
if (opts.TryGetValue("retire", out var retOpt)) worldDef.Rules.StationRetireActive = retOpt == "on";
// Runde 25: Varianten für die Handelsmargen ohne Neubau — Exportgebot der Senken für Konsum-/Luxusgüter, Betriebskapitalanteil der Stationen
if (opts.TryGetValue("exportbid", out var ebOpt))
{
    float eb = float.Parse(ebOpt, CultureInfo.InvariantCulture);
    foreach (var t in worldDef.StationTypes)
        if (t.Export?.Bids is { } bids)
            foreach (var k in bids.Keys.ToList()) if (k is "consumer_goods" or "luxury_goods") bids[k] = eb;
}
if (opts.TryGetValue("sellcap", out var scOpt)) worldDef.Rules.ProducerPriceCapShare = float.Parse(scOpt, CultureInfo.InvariantCulture);
if (opts.TryGetValue("wcshare", out var wcOpt)) worldDef.Rules.StationWorkingCapitalShare = float.Parse(wcOpt, CultureInfo.InvariantCulture);
if (opts.TryGetValue("buyback", out var bbOpt)) worldDef.Rules.BuybackCap = bbOpt == "on";
// Runde 44: --sectorscale 1|map|X — Maßstab aller Sektoren (1 = Grundmaßstab, map = wie in der Weltdatei, X = alle gleich X); --maxtraders/--maxminers N
// (Kapazitätseinheiten); --raidreach X / --policerange X (0 = ganzer Sektor)
if (opts.TryGetValue("sectorscale", out var ssOpt) && ssOpt != "map")
{
    float ss = float.Parse(ssOpt, CultureInfo.InvariantCulture);
    foreach (var sd in worldDef.Sectors) sd.Scale = ss;
}
if (opts.TryGetValue("maxtraders", out var mtOpt)) worldDef.Rules.MaxTraders = int.Parse(mtOpt, CultureInfo.InvariantCulture);
if (opts.TryGetValue("maxminers", out var mmOpt)) worldDef.Rules.MaxMiners = int.Parse(mmOpt, CultureInfo.InvariantCulture);
if (opts.TryGetValue("raidreach", out var rrOpt)) worldDef.Rules.RaidReach = float.Parse(rrOpt, CultureInfo.InvariantCulture);
if (opts.TryGetValue("policerange", out var prOpt)) worldDef.Rules.PoliceResponseRange = float.Parse(prOpt, CultureInfo.InvariantCulture);   // Runde 30: Rückkaufdeckel an/aus (Vergleich)
// Runde 29: --probe rueckkauf misst die Kauf-/Rückverkaufsschleife an einer Station (eigene Welten, eigener Bericht) und endet
if (opts.TryGetValue("probe", out var rkOpt) && rkOpt == "rueckkauf") { Console.WriteLine(ExploitProbe.Run(worldDef, seed ?? 1)); return 0; }
var u = WorldBuilder.Build(worldDef, seed, opts.TryGetValue("start", out var startOpt) ? startOpt : null);   // Runde 26: --start trader wie „Neues Spiel“
// Phase 21: Torliste (Id, von → nach, Zustand, Hauptroute) am Anfang, damit --gates nachvollziehbar ist; je Verbindung wird die kleinere Id genannt
// (--gates schließt immer beide Enden). --gatelist on: zusätzlich je Verbindung, ob ihre Sperrung die Karte zerteilt (und welcher Teil abfällt), dann Ende.
var gateLinks = u.Gates.Values.Where(g => g.Id.Value < g.Target.Value).OrderBy(g => g.Id.Value).ToList();
string SectorShort(SectorId id) => u.Sectors.TryGetValue(id, out var sec) ? sec.Name.Replace("Sektor ", "") : id.Value.ToString(CultureInfo.InvariantCulture);
Console.WriteLine("Tore: " + string.Join(", ", gateLinks.Select(g =>
    $"{g.Id.Value}/{g.Target.Value} {SectorShort(g.Sector)}→{SectorShort(u.Gates[g.Target].Sector)} {g.State}{(g.MainRoute ? " Hauptroute" : "")}")));
if (opts.TryGetValue("gatelist", out var glOpt) && glOpt == "on")
{
    int baseParts = MapParts.Of(u).Parts.Count;
    foreach (var g in gateLinks)
    {
        var parts = MapParts.IfClosed(u, g.Id);
        string verdict = parts.Parts.Count > baseParts
            ? "zerteilt: " + string.Join(" | ", parts.Parts.Select(p => string.Join("+", p.Select(SectorShort))))
            : "Karte bleibt verbunden";
        Console.WriteLine($"Tor {g.Id.Value}/{g.Target.Value} {SectorShort(g.Sector)} → {SectorShort(u.Gates[g.Target].Sector)}: {verdict}");
    }
    return 0;
}
// Phase 11: --security off schaltet Polizei und Piraten ab (Vergleichslauf ohne Phase-11-Wirkung)
if (opts.TryGetValue("security", out var secOpt) && secOpt == "off") u.Rules.SecurityActive = false;
if (opts.TryGetValue("blackmarket", out var bmOpt)) u.Rules.BlackMarket = bmOpt == "on";   // Schwarzmarkt: Beute beim Hehler ablegen (Vergleichsläufe)
// Phase 27/28: --fleetcombat off und --warfare off schalten den Flottenkampf bzw. die Kriegsführung ab (Vergleichsläufe; Inhalte bleiben in der Welt)
if (opts.TryGetValue("fleetcombat", out var fcOpt)) u.Rules.FleetCombat = fcOpt == "on";
if (opts.TryGetValue("warfare", out var wfOpt)) u.Rules.Warfare = wfOpt == "on";
// Phase 31: --gatecombat on|off schaltet den Torkampf (Vergleichslauf); --gatedown N legt alle N Ticks ein nicht kartenteilendes Tor still (Testtreiber,
// wie ein Kampfausfall: Reparatur/Notregel bringen es zurück), --gatedownsplit on erlaubt dabei auch kartenteilende Tore
if (opts.TryGetValue("gatecombat", out var gcOpt)) u.Rules.GateCombat = gcOpt == "on";
long gateDownEvery = opts.TryGetValue("gatedown", out var gdOpt) ? long.Parse(gdOpt, CultureInfo.InvariantCulture) : 0;
bool gateDownSplit = opts.TryGetValue("gatedownsplit", out var gdsOpt) && gdsOpt == "on";
if (gateDownEvery > 0) u.Rules.GateCombat = true;
int gateDownCursor = 0;
// Runde 38: Spielerkrieg als Testtreiber (--playerwar F@tick, Tick auf 20 genau); Kennzahlen am Ende
(int Faction, long Tick)? playerWar = null;
long playerWarStarted = -1;
string playerPeace = "";
int playerLocks = 0;
if (opts.TryGetValue("playerwar", out var pwOpt))
{
    var pwp = pwOpt.Split('@');
    playerWar = (int.Parse(pwp[0], CultureInfo.InvariantCulture), long.Parse(pwp[1], CultureInfo.InvariantCulture) / 20 * 20);
}
// Spielersperre: Testtreiber --playercrime F:art[*n]@tick,... — der Spieler begeht zum Tick n Verbrechen der Art bei Fraktion F
// (art = Schlüssel wie smuggling/toll_evasion/assault/assault_station/assault_gate; „kill“ = Angriff mit Abschuss). Kennzahlen am Ende.
var crimeDriver = new List<(int Faction, string Kind, int Count, long Tick)>();
if (opts.TryGetValue("playercrime", out var pcOpt))
    foreach (var part in pcOpt.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var at = part.Split('@'); var fk = at[0].Split(':'); var kn = fk[1].Split('*');
        crimeDriver.Add((int.Parse(fk[0], CultureInfo.InvariantCulture), kn[0], kn.Length > 1 ? int.Parse(kn[1], CultureInfo.InvariantCulture) : 1, long.Parse(at[1], CultureInfo.InvariantCulture) / 20 * 20));
    }
var crimeLog = new List<string>();
double crimeStandingLoss = 0, crimeStandingRecovered = 0, tollsAtFirstCrime = -1, tollsAtFirstLock = -1;
long firstCrimeTick = -1, firstPlayerLockTick = -1;
int maxPlayerLock = 0;
// Runde 45: Testtreiber --aiwar A:B@tick (KI A erklärt KI B den Krieg, ohne Kriegsgrund) und --alliance A:B@tick (Beziehung beider auf 0,7, Bündnis)
(int A, int B, long Tick)? aiWarDriver = null, allianceDriver = null;
static (int, int, long) ParsePairAt(string s) { var at = s.Split('@'); var ab = at[0].Split(':'); return (int.Parse(ab[0], CultureInfo.InvariantCulture), int.Parse(ab[1], CultureInfo.InvariantCulture), long.Parse(at[1], CultureInfo.InvariantCulture) / 20 * 20); }
if (opts.TryGetValue("aiwar", out var awOpt)) aiWarDriver = ParsePairAt(awOpt);
if (opts.TryGetValue("alliance", out var alOpt)) allianceDriver = ParsePairAt(alOpt);
// --treasury F:Betrag@tick,F:Betrag@tick — Kasse einer Fraktion setzen (Kriegskasse für die Tribut-Vorführung)
var treasuryDriver = opts.TryGetValue("treasury", out var trOpt) ? trOpt.Split(',').Select(ParsePairAt).ToList() : new List<(int, int, long)>();
var allianceLog = new List<string>();
int tributePeaces = 0; double tributeMoney = 0;
// Phase 31: Torkampf-Kennzahlen
int gatesDisabled = 0, gatesDisabledSplit = 0, gatesAttacked = 0, gateShots = 0, gateDownMax = 0, tradersBrokeWhileDown = 0;
var gateRestored = new SortedDictionary<string, int>(StringComparer.Ordinal);
var gateDownSince = new Dictionary<int, long>();
var gateOutages = new List<long>();
double gateRepairCost = 0;
u.Events.Raised += e =>
{
    switch (e)
    {
        case GateDisabled gd: gatesDisabled++; if (gd.Splits) gatesDisabledSplit++; gateDownSince[gd.Gate.Value] = u.Tick; break;
        case GateRestored gr:
            gateRestored[gr.Reason] = gateRestored.GetValueOrDefault(gr.Reason) + 1;
            if (gateDownSince.Remove(gr.Gate.Value, out var since)) gateOutages.Add(u.Tick - since);
            break;
        case GateAttacked: gatesAttacked++; break;
        case ShotAtGate: gateShots++; break;
        case GateRepaired grp: gateRepairCost += grp.Cost; break;
        case ShipDeparted { Reason: DepartureReason.Bankrupt } sd when gateDownSince.Count > 0 && sd.Role == ShipRole.Trader: tradersBrokeWhileDown++; break;
    }
};
// Prüfung Runde 2: --planguard off schaltet den Kaufschutz in der Händlerplanung ab (Vergleichslauf)
if (opts.TryGetValue("planguard", out var pgOpt)) u.Rules.TraderPlanGuard = pgOpt == "on";
// Runde 7: --rejoin off = Wiedereintritt mit neuem Gefechtseintrag (Verhalten bis Runde 6, Vergleichslauf)
if (opts.TryGetValue("rejoin", out var rjOpt)) u.Rules.CombatRejoinReuse = rjOpt == "on";
// Phase 29 Runde 10: --spareloot off = Kaperer feuern auch, wenn der Schuss die Beute vernichten kann (Vergleichslauf)
if (opts.TryGetValue("spareloot", out var slOpt)) u.Rules.RaidersSpareLoot = slOpt == "on";
// Phase 32: --anchors on|off schaltet die Verankerung beim Kontaktabbau (Vergleichslauf); Erzfluss und Ankerkennzahlen
if (opts.TryGetValue("anchors", out var anOpt)) u.Rules.MiningAnchors = anOpt == "on";
double oreContact = 0, oreLaser = 0, oreDelivered = 0;
// Runde 44: Fahrten (Dauer je Händler-/Schürferfahrt, Anteil in Fahrt, Laderaumfüllung) und Abgänge (pleite/nicht rentabel) je 5000-Tick-Fenster
var tripSeen = new HashSet<(int Ship, long Start)>();
var tripTicks = new Dictionary<ShipRole, List<long>> { [ShipRole.Trader] = new(), [ShipRole.Miner] = new() };
var tripTransit = new Dictionary<ShipRole, (long In, long All, double Fill, long FillN)> { [ShipRole.Trader] = default, [ShipRole.Miner] = default };
var leaveWindows = new SortedDictionary<long, int>();
u.Events.Raised += e =>
{
    if (e is ShipDeparted { Reason: DepartureReason.Bankrupt or DepartureReason.Unprofitable } lw && lw.Role is ShipRole.Trader or ShipRole.Miner)
        leaveWindows[u.Tick / 5000] = leaveWindows.GetValueOrDefault(u.Tick / 5000) + 1;
};
int anchorsFired = 0, anchorsHeld = 0;
var anchorReleases = new SortedDictionary<string, int>(StringComparer.Ordinal);
u.Events.Raised += e =>
{
    switch (e)
    {
        case ContactMined cm: oreContact += cm.Units; break;
        case MinerShot ms: oreLaser += ms.Units; break;
        case MineDelivered md: oreDelivered += md.Amount; break;
        case AnchorFired: anchorsFired++; break;
        case AnchorHeld: anchorsHeld++; break;
        case AnchorReleased ar: anchorReleases[ar.Reason] = anchorReleases.GetValueOrDefault(ar.Reason) + 1; break;
    }
};
// Phase 11: Rechenzeit der Sicherheits-Systeme (Lagekarte, Piraten, Polizei, Fahndung) messen — das Modul wird in eine Zeitmessung gehüllt
var securityWatch = new System.Diagnostics.Stopwatch();
// Phase 17: Rechenzeit je System (jedes System eines Moduls bekommt eine eigene Stoppuhr; die Sicherheits-Systeme laufen zusätzlich auf der Sammeluhr)
var systemWatches = new List<(string Name, System.Diagnostics.Stopwatch Watch)>();
// --systemtime off: ohne die Uhren je System (nur die Sammeluhr der Sicherheit wie vor Phase 17) — zum Prüfen des Messaufwands
bool perSystem = !(opts.TryGetValue("systemtime", out var stOpt) && stOpt == "off");
var runner = new SimulationRunner(SimulationRunner.DefaultModules().Select(m =>
    perSystem ? new TimedModule(m, m is SpaceSim.Core.Modules.SecurityModule ? securityWatch : null, systemWatches)
    : m is SpaceSim.Core.Modules.SecurityModule ? new TimedModule(m, securityWatch, null) : m));
var probeWatch = new System.Diagnostics.Stopwatch();   // Phase 17: Messaufwand des Tools selbst (MarketWatch-Probe, Kapazität, Exportkassen)
var routes = new RouteRecorder(u);
// Phase 15: Deckel-, Kassen- und Fahrtenmessung je Station/Ware (alle 20 Ticks, nach dem Einschwingen = erste 10 % der Laufzeit)
var watch = new MarketWatch(u);
int recoveringMax = 0;   // Phase 23: größter Erholungsindex (Schiffe mit nicht vollem Schild/Bank) an den Messpunkten
const int WatchEvery = 20;
int watchFrom = ticks / 10;
// Phase 15: Geldmenge im Verlauf (Anteil der Kassen) und Kassen der Exportsenken (Minimum nach dem Einschwingen)
var exportCashMin = new SortedDictionary<int, float>();
var capMax = new SortedDictionary<string, float>(StringComparer.Ordinal);   // Phase 15: höchste belegte Kapazität je Aufgabe (alle 200 Ticks gemessen)
var csv = new StringBuilder();

// Phase 4b: Treibstoff-Kennzahlen zählen (Tankkäufe an Stationen, Notkäufe zum Aufpreis)
int fuelBuys = 0, notk = 0;
float fuelBuyCost = 0f, notkCost = 0f;
double fuelBuyUnits = 0, notkUnits = 0;   // Treibstoff im Großmaßstab: getankte Einheiten (Stationen/Notkauf)
// Phase 5a: Modul-Waren — wie viel Händler an Lager (stocks) liefern
float moduleDelivered = 0f, moduleRevenue = 0f;
// Phase 5c: KI-Neubauten mit Modulen (volle Kaufregel) bzw. nur aus dem Bausatz
int buildsWithModules = 0, buildsKitOnly = 0;
// Phase 6: Aufgabenwechsel der Multirole-Schiffe und Modellwahl der KI-Flotte
int jobSwitches = 0;
var chosenModels = new SortedDictionary<string, int>(StringComparer.Ordinal);
// Runde 9: Abgänge der Handelsschiffe je Modell und Grund (Tanker, Kurier: Rentabilität)
var traderDepartures = new SortedDictionary<string, int>(StringComparer.Ordinal);
// Phase 29: welche Modelle Polizei, Marine und Piraten kaufen bzw. bauen
var forceModels = new SortedDictionary<string, int>(StringComparer.Ordinal);
u.Events.Raised += e =>
{
    (string who, ShipId id) = e switch
    {
        PoliceCommissioned pc => ("Polizei", pc.Ship),
        WarshipCommissioned wc => ("Marine", wc.Ship),
        RaiderBuilt rb => ("Piraten", rb.Ship),
        _ => ("", default),
    };
    if (who.Length > 0 && u.Ships.TryGetValue(id, out var fs))
    {
        string k = $"{who} {fs.ModelKey}";
        forceModels[k] = forceModels.GetValueOrDefault(k) + 1;
    }
};
u.Events.Raised += e =>
{
    if (e is ShipDeparted sd && sd.Role == ShipRole.Trader && u.Ships.TryGetValue(sd.Ship, out var dship))
    {
        string k = $"{(dship.ModelKey.Length > 0 ? dship.ModelKey : "?")} {DepartureNames.Key(sd.Reason)}";
        traderDepartures[k] = traderDepartures.GetValueOrDefault(k) + 1;
    }
};
// Phase 7: Güteklassen der Neubauten (gewürfelt/erreicht) und Upgrade-Versuche der NPCs
var gradeRolled = new int[4]; var gradeGot = new int[4];
int upgradeTries = 0, upgradeOk = 0; float upgradeCost = 0f;
// Phase 8: Bau über Baustellen — angelegt, fertig (davon Notbau), verfallen; Material nach Lieferweg; Notbau-Kosten
int sitesStarted = 0, sitesDone = 0, sitesEmergency = 0, sitesAbandoned = 0, directBuilds = 0, splits = 0;
var deliveredBy = new SortedDictionary<string, float>(StringComparer.Ordinal);
var deliveredGood = new SortedDictionary<string, float>(StringComparer.Ordinal);
float emergencyCost = 0f, emergencyWaived = 0f, emergencyUnits = 0f;
u.Events.Raised += e =>
{
    if (e is ConstructionStarted) sitesStarted++;
    else if (e is ConstructionCompleted cc) { sitesDone++; if (cc.Emergency) sitesEmergency++; }
    else if (e is ConstructionAbandoned) sitesAbandoned++;
    else if (e is EmergencyConstruction ec) { emergencyCost += ec.Cost; emergencyWaived += ec.Waived; emergencyUnits += ec.Units; }
    else if (e is ConstructionDelivered cd)
    {
        deliveredBy[cd.By] = deliveredBy.GetValueOrDefault(cd.By) + cd.Units;
        deliveredGood[u.Goods[cd.Good].Name] = deliveredGood.GetValueOrDefault(u.Goods[cd.Good].Name) + cd.Units;
    }
    else if (e is AsteroidSplit) splits++;
    else if (e is StationBuilt) directBuilds++;   // abzüglich der fertigen Baustellen = Direktbau
};
// Phase 9: Kassen und Zukauf — Abgaben in Kassen (und ohne Empfänger), Investitionen nach Quelle und Größenklasse, Auf-/Abstiege
double taxIn = 0, taxSunk = 0, treasuryInvested = 0;
var investBy = new SortedDictionary<string, int>(StringComparer.Ordinal);
int promoted = 0, removedActors = 0;
u.Events.Raised += e =>
{
    switch (e)
    {
        case TaxCollected tcol: taxIn += tcol.Amount; break;
        case TaxSunk ts: taxSunk += ts.Amount; break;
        case InvestorChosen ic:
            string key = $"{(ic.Purpose == "station" ? "Station" : "Schiff")} aus {ic.Source switch { InvestorSource.Treasury => "Kasse", InvestorSource.Station => "Stationskasse", _ => "Schiffskasse" }} ({ActorNames.Size(ic.Class)})";
            investBy[key] = investBy.GetValueOrDefault(key) + 1;
            if (ic.Source == InvestorSource.Treasury) treasuryInvested += ic.Amount;
            break;
        case ActorPromoted: promoted++; break;
        case ActorRemoved: removedActors++; break;
    }
};
// Phase 10: Politik — Sperränderungen, Zoll, Umwege/Abbrüche, Kassen-Unterhalt (Regierungsstationen, Sperren), Besitzerwechsel
int accessChanges = 0, rerouted = 0, blocked = 0, ownerChanges = 0, maxLevelSeen = 0;
double tolls = 0, govUpkeep = 0, govUnpaid = 0, lockUpkeep = 0;
u.Events.Raised += e =>
{
    switch (e)
    {
        case SectorAccessChanged sac: accessChanges++; maxLevelSeen = Math.Max(maxLevelSeen, sac.ToEntry); break;
        case TollPaid tp: tolls += tp.Amount; break;
        case ShipRerouted: rerouted++; break;
        case RouteBlocked: blocked++; break;
        case SectorOwnerChanged: ownerChanges++; break;
        case TreasuryUpkeep tu when tu.Purpose == "government": govUpkeep += tu.Amount; govUnpaid += tu.Unpaid; break;
        case TreasuryUpkeep tu when tu.Purpose == "lock": lockUpkeep += tu.Amount; break;
    }
};
// Phase 11: Polizei, Piraten, Fahndung — Überfälle, Verluste, Abfangen, Polizeikosten, Kasseneinnahmen
int raids = 0, raidsWon = 0, raidsOnPlayer = 0, victimsLost = 0, raidersLost = 0, intercepts = 0, interceptsWon = 0, raidersBuilt = 0, policeBought = 0, policeGone = 0, policeHelped = 0;
int crimes = 0, warnings = 0;
double lootValue = 0, fenced = 0, tradeValue = 0, policeCost = 0, policeUnpaid = 0, pirateCost = 0, fines = 0, bounties = 0;
// Runde 41: Aufträge — Angebote/Annahmen/Erfüllungen/Scheitern und der Geldfluss (Reservierung, Zahlung, Rückgabe, Pfand)
int mOffered = 0, mAccepted = 0, mCompleted = 0, mFailed = 0;
var mByType = new SortedDictionary<string, int>(StringComparer.Ordinal);   // Runde 42b: Zählung je Auftragsart
double mReserve = 0, mPaid = 0, mReturn = 0, mDepositForfeit = 0;
var victimsByRole = new SortedDictionary<string, int>(StringComparer.Ordinal);
u.Events.Raised += e =>
{
    switch (e)
    {
        case RaidResolved rr:
            raids++; if (rr.AttackerWon) raidsWon++; if (rr.Player) raidsOnPlayer++; if (rr.VictimDestroyed) victimsLost++; if (rr.RaiderDestroyed) raidersLost++;
            if (rr.Police.Value != 0) policeHelped++;
            lootValue += rr.LootValue; fenced += rr.Fenced;
            break;
        case RaiderIntercepted ri: intercepts++; if (ri.PoliceWon) interceptsWon++; if (ri.RaiderDestroyed) raidersLost++; break;
        case RaiderBuilt: raidersBuilt++; break;
        case PoliceCommissioned: policeBought++; break;
        case PoliceDecommissioned: policeGone++; break;
        case TreasuryUpkeep { Purpose: "police" } pu: policeCost += pu.Amount; policeUnpaid += pu.Unpaid; break;
        case TreasuryUpkeep { Purpose: "pirates" } pc: pirateCost += pc.Amount; break;
        case TreasuryUpkeep { Purpose: "bounty" } bo: bounties += bo.Amount; break;
        case TreasuryIncome { Purpose: "fine" } fi: fines += fi.Amount; break;
        case CrimeReported: crimes++; break;
        case EncounterWarning: warnings++; break;
        case TradeCompleted tcv: tradeValue += tcv.Revenue; break;
        case MineDelivered md: tradeValue += md.Revenue; break;
        // Runde 41: Aufträge (Geldfluss erklärbar: Reservierung bei Annahme, Zahlung bei Erfüllung, Rückgabe/Pfand bei Scheitern)
        case MissionOffered mo: mOffered++; mByType[mo.Type] = mByType.GetValueOrDefault(mo.Type) + 1; break;   // Runde 42b: je Art
        case MissionAccepted: mAccepted++; break;
        case MissionCompleted mc: mCompleted++; break;
        case MissionFailed: mFailed++; break;
        case MissionPaid mp: mPaid += mp.Amount; break;
        case TreasuryUpkeep { Purpose: "mission_reserve" } mr: mReserve += mr.Amount; break;
        case TreasuryIncome { Purpose: "mission_return" } mr2: mReturn += mr2.Amount; break;
        case TreasuryIncome { Purpose: "mission_deposit" } md2: mDepositForfeit += md2.Amount; break;
    }
};
// Phase 24: Rundengefecht — Gefechte, Dauer, Schüsse/Treffer, Ausgänge, Munition, bewaffnete Händler
int fights = 0, fightRaids = 0, fightIntercepts = 0, fightShots = 0, fightHits = 0, fightsMax = 0;
long fightTicks = 0;
var fightOutcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
var exitsByRole = new SortedDictionary<string, int>(StringComparer.Ordinal);
double ammoUnits = 0, ammoCost = 0, ammoTreasury = 0, ammoPolice = 0, shieldDmg = 0, hullDmg = 0, ammoUsed = 0;
int armedBought = 0, raidsAtLastMark = 0;
var pirateTimeline = new List<string>();
u.Events.Raised += e =>
{
    switch (e)
    {
        case EncounterStarted es: fights++; if (es.Cause == EncounterCause.Raid) fightRaids++; else fightIntercepts++; break;
        case EncounterEnded ee:
            fightShots += ee.Shots; fightHits += ee.Hits; fightTicks += ee.Ticks;
            fightOutcomes[ee.Outcome] = fightOutcomes.GetValueOrDefault(ee.Outcome) + 1;
            break;
        case CombatExited cx: { string k = $"{cx.Role}/{cx.Exit}"; exitsByRole[k] = exitsByRole.GetValueOrDefault(k) + 1; break; }
        case ShotFired sf:
            shieldDmg += sf.ShieldDamage; hullDmg += sf.HullDamage;
            if (u.Rules.AmmoActive && u.Modules.TryGetValue(sf.Weapon, out var wm)) ammoUsed += wm.Stat("ammoPerShot");
            break;
        case AmmoRearmed ar:
            ammoUnits += ar.Units; ammoCost += ar.Cost; if (ar.FromTreasury) ammoTreasury += ar.Cost;
            if (u.Ships.TryGetValue(ar.Ship, out var arShip) && ThreatMap.IsPolice(arShip)) ammoPolice += ar.Cost;
            break;
        case FleetModelChosen fmc when u.ShipModels.TryGetValue(fmc.ModelKey, out var fm) && fm.Fit.Any(k => k is not null && u.Modules.TryGetValue(k, out var mm) && mm.IsWeapon):
            armedBought++; break;
    }
};
// Phase 27: Flottenkampf — Verbände, Verstärkung, Zuflucht, Verfolgung, Geleit, Ergeben
var joinedBy = new SortedDictionary<string, int>(StringComparer.Ordinal);
var refugeBy = new SortedDictionary<string, int>(StringComparer.Ordinal);
var pursuitEnds = new SortedDictionary<string, int>(StringComparer.Ordinal);
int pursuits = 0, escortsAssigned = 0, squadsFormed = 0, packsFormed = 0, arrests = 0;
u.Events.Raised += e =>
{
    switch (e)
    {
        case CombatJoined cj: joinedBy[cj.Reason] = joinedBy.GetValueOrDefault(cj.Reason) + 1; break;
        case RefugeReached rr2: refugeBy[rr2.Kind] = refugeBy.GetValueOrDefault(rr2.Kind) + 1; break;
        case PursuitStarted: pursuits++; break;
        case PursuitEnded pe: pursuitEnds[pe.How] = pursuitEnds.GetValueOrDefault(pe.How) + 1; break;
        case EscortAssigned: escortsAssigned++; break;
        case ForceSquadron fs when fs.Formed: if (fs.Kind == "pack") packsFormed++; else squadsFormed++; break;
        case EncounterStarted es2 when es2.Cause == EncounterCause.Arrest: arrests++; break;
    }
};
// Phase 28 (Nacharbeit): --probe pleite — Verlauf jedes pleitegegangenen KI-Händlers (Bau, letzte Verkäufe mit Kasse danach, Abwürfe, Überfälle)
bool probeBroke = opts.TryGetValue("probe", out var probeOpt) && probeOpt == "pleite";
var tradeProbe = opts.TryGetValue("probe", out var tpOpt) && tpOpt == "spielerhandel" ? new PlayerTradeProbe() : null;
var cashProbe = opts.TryGetValue("probe", out var cpOpt) && cpOpt == "kassen" ? new CashProbe() : null;   // Runde 25
var hintProbe = opts.TryGetValue("probe", out var hpOpt) && hpOpt == "hinweis" ? new HintProbe(u) : null;   // Runde 25
var probeLog = new Dictionary<int, List<string>>();
var probeOut = new List<string>();
void PLog(ShipId id, string text)
{
    if (!probeLog.TryGetValue(id.Value, out var l)) probeLog[id.Value] = l = new List<string>();
    l.Add(text);
    if (l.Count > 24) l.RemoveAt(0);
}
if (probeBroke)
    u.Events.Raised += e =>
    {
        switch (e)
        {
            case FleetShipBuilt fb when u.Ships.TryGetValue(fb.Ship, out var bs):
                PLog(fb.Ship, $"t{u.Tick} gebaut {bs.ModelKey} {(fb.WithModules ? "mit Modulen" : "Bausatz")} {fb.Price:0} Cr, Kasse {bs.Credits:0}"); break;
            case TradeCompleted tc when u.Ships.TryGetValue(tc.Ship, out var ts) && ts.Controller == Controller.Ai:
                PLog(tc.Ship, $"t{u.Tick} {u.Goods[tc.Good].Key} {tc.Amount:0.#} {u.Stations.GetValueOrDefault(tc.From)?.TypeKey}→{u.Stations.GetValueOrDefault(tc.To)?.TypeKey} Erlös {tc.Revenue:0} Kasse {ts.Credits:0}"); break;
            case CargoDiscarded cd: PLog(cd.Ship, $"t{u.Tick} ABWURF {u.Goods[cd.Good].Key} {cd.Units:0.#}"); break;
            case RaidResolved rr: PLog(rr.Victim, $"t{u.Tick} ÜBERFALL Beute {rr.LootValue:0}"); break;
            case ShipRepaired rp when !rp.AtBase: PLog(rp.Ship, $"t{u.Tick} REPARATUR {rp.Cost:0} Cr"); break;
            case FirmCapitalMoved fm: PLog(fm.From, $"t{u.Tick} KAPITAL ab {fm.Amount:0}"); PLog(fm.To, $"t{u.Tick} KAPITAL zu {fm.Amount:0}"); break;
            case AmmoRearmed ar when !ar.FromTreasury: PLog(ar.Ship, $"t{u.Tick} MUNITION {ar.Cost:0}"); break;
            case TraderBought tb: PLog(tb.Ship, $"t{u.Tick} kauft {u.Goods[tb.Good].Key} {tb.Units:0.#} für {tb.Cost:0}"); break;
            case CargoDumped cdu: PLog(cdu.Ship, $"t{u.Tick} ABGELADEN {u.Goods[cdu.Good].Key} {cdu.Units:0.#}"); break;
            case ShipDeparted sd when sd.Role == ShipRole.Trader && sd.Reason is DepartureReason.Bankrupt or DepartureReason.Unprofitable:
                probeOut.Add($"--- Schiff {sd.Ship.Value} {sd.Reason} t{u.Tick} in {SectorShort(sd.Sector)}: " + string.Join(" | ", probeLog.GetValueOrDefault(sd.Ship.Value) ?? new List<string>()));
                break;
        }
    };
// Phase 28: Raketen, Stationsgeschütze, Belagerungen, Krieg, Kriegsschiffe; dazu die Piratenkassen nach Zweck (Prüfpunkt e)
var missileOutcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
var stationFalls = new SortedDictionary<string, int>(StringComparer.Ordinal);
var peaceReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
var pirateIn = new SortedDictionary<string, double>(StringComparer.Ordinal);
var pirateOut = new SortedDictionary<string, double>(StringComparer.Ordinal);
int buysRefused = 0;   // Nacharbeit Phase 28: Einkäufe mit sicherem Verlust verweigert
var refusedBy = new SortedDictionary<string, int>(StringComparer.Ordinal);   // Runde 25: nach Grund und KI/Spieler
// Phase 30: Kleinstationen — gebaut (je Typ), abgebaut (ruhig), Abwehrgefechte, Schüsse der Stellungen, Kaufpreis
var outpostsBuilt = new SortedDictionary<string, int>(StringComparer.Ordinal);
int outpostsCalm = 0, outpostFights = 0, outpostShots = 0;
double outpostCost = 0;
var warLog = new List<string>();   // Runde 38: Kriegserklärungen mit Tick (Kaskaden sichtbar machen)
var playerWarLog = new List<string>();   // Runde 40: Kriegserklärungen gegen den Spieler (AiWarOnPlayer)
int missilesLaunched = 0, stationShots = 0, stationHits = 0, shotsAtStations = 0, sieges = 0, warsDeclared = 0, warshipsBought = 0, warshipsGone = 0, stationRepairs = 0;
double stationHullDmg = 0, stationRepairHull = 0, stationRepairPlates = 0, stationRepairCost = 0, navyCost = 0, stationGunDmg = 0;
u.Events.Raised += e =>
{
    switch (e)
    {
        case MissileLaunched: missilesLaunched++; break;
        case MissileResolved mr: missileOutcomes[mr.Outcome] = missileOutcomes.GetValueOrDefault(mr.Outcome) + 1; if (mr.TargetStation.Value != 0) stationHullDmg += mr.HullDamage; break;
        case StationShot ss: stationShots++; if (ss.Hit) { stationHits++; stationGunDmg += ss.ShieldDamage + ss.HullDamage; } break;
        case ShotAtStation sa: shotsAtStations++; stationHullDmg += sa.HullDamage; break;
        case SiegeStarted: sieges++; break;
        case StationFell sf: stationFalls[sf.How] = stationFalls.GetValueOrDefault(sf.How) + 1; break;
        case StationRepaired sr: stationRepairs++; stationRepairHull += sr.Hull; stationRepairPlates += sr.Plates; stationRepairCost += sr.Cost; break;
        case WarDeclared wd:
            warsDeclared++;
            warLog.Add($"Tick {u.Tick}: {wd.Message} (Beziehung {wd.Relation:0.00})");
            if (u.IsPlayerFaction(wd.A) || u.IsPlayerFaction(wd.B)) playerWarLog.Add($"Tick {u.Tick}: {wd.Message}");
            if (crimeDriver.Count > 0 && (u.IsPlayerFaction(wd.A) || u.IsPlayerFaction(wd.B))) crimeLog.Add($"Tick {u.Tick}: {wd.Message}");   // Spielersperre
            break;
        case AllianceFormed af: allianceLog.Add($"Tick {u.Tick}: {af.Message}"); break;
        case AllianceDissolved ad: allianceLog.Add($"Tick {u.Tick}: {ad.Message}"); break;
        case AllyJoinedWar aj: allianceLog.Add($"Tick {u.Tick}: {aj.Message}"); break;
        case TributePeace tp: tributePeaces++; tributeMoney += tp.Amount; allianceLog.Add($"Tick {u.Tick}: {tp.Message} (Forderung {tp.Demanded:0})"); break;
        case PeaceConcluded pc:
            peaceReasons[pc.Reason] = peaceReasons.GetValueOrDefault(pc.Reason) + 1;
            if (allianceDriver is not null || aiWarDriver is not null) allianceLog.Add($"Tick {u.Tick}: {pc.Message}");   // Runde 45: Testtreiber — Friedensschlüsse mit Tick
            if (u.IsPlayerFaction(pc.A) || u.IsPlayerFaction(pc.B)) playerPeace = $"Frieden bei Tick {u.Tick} ({pc.Reason})";
            break;
        case SectorAccessChanged sac when sac.Against is { } sag && u.IsPlayerFaction(sag) && sac.ToEntry > 0:
            playerLocks++;
            if (crimeDriver.Count > 0)
            {
                if (firstPlayerLockTick < 0) { firstPlayerLockTick = u.Tick; tollsAtFirstLock = tolls; }
                maxPlayerLock = Math.Max(maxPlayerLock, sac.ToEntry);
                crimeLog.Add($"Tick {u.Tick}: {u.Sectors[sac.Sector].Name} Stufe {sac.ToEntry} gegen dich");
            }
            break;
        case SectorAccessChanged sacEnd when crimeDriver.Count > 0 && sacEnd.ToEntry == 0 && sacEnd.FromEntry > 0:
            crimeLog.Add($"Tick {u.Tick}: {u.Sectors[sacEnd.Sector].Name} wieder offen");
            break;
        case StandingChanged sc when sc.Reason.StartsWith(CrimeStanding.ReasonPrefix, StringComparison.Ordinal): crimeStandingLoss += sc.From - sc.To; break;
        case StandingChanged sc2 when sc2.Reason is CrimeStanding.RecoveryReason or CrimeStanding.FineReason: crimeStandingRecovered += sc2.To - sc2.From; break;
        case WarshipCommissioned: warshipsBought++; break;
        case WarshipDecommissioned: warshipsGone++; break;
        case TraderBuyRefused br:
            buysRefused++;
            { string who = u.Ships.TryGetValue(br.Ship, out var bs) && bs.IsPlayer ? "Spieler" : "KI"; refusedBy[$"{who} {br.Reason}"] = refusedBy.GetValueOrDefault($"{who} {br.Reason}") + 1; }
            break;
        case TreasuryUpkeep tu when tu.Purpose == "navy": navyCost += tu.Amount; break;
        case OutpostBuilt ob: outpostsBuilt[ob.TypeKey] = outpostsBuilt.GetValueOrDefault(ob.TypeKey) + 1; outpostCost += ob.Cost; break;
        case OutpostEngaged: outpostFights++; break;
        case StationDeparted { Detail: "calm" }: outpostsCalm++; break;
    }
    if (e is StationShot os && u.Stations.TryGetValue(os.Station, out var ost) && Outposts.IsOutpost(u, ost)) outpostShots++;
    if (e is TreasuryIncome ti && u.Actors.TryGetValue(ti.Actor, out var pa) && pa.Kind == ActorKind.Outlaw) pirateIn[ti.Purpose] = pirateIn.GetValueOrDefault(ti.Purpose) + ti.Amount;
    if (e is TreasuryUpkeep tu2 && u.Actors.TryGetValue(tu2.Actor, out var pb) && pb.Kind == ActorKind.Outlaw) pirateOut[tu2.Purpose] = pirateOut.GetValueOrDefault(tu2.Purpose) + tu2.Amount;
};
// Phase 26: Schaden und Bergung — Modulschäden, Brände, Reparaturen, Wracks, Bergung
int modDamaged = 0, modFailed = 0, firesStarted = 0, wrecksMade = 0, containersMade = 0, wrecksExpired = 0, repairsNpc = 0, repairsBase = 0, repairsPlayer = 0, repairModules = 0;
double repairCost = 0, repairBaseCost = 0, repairPlates = 0, repairElec = 0, salvagedCargoNpc = 0, salvagedCargoPlayer = 0, wreckCargoMade = 0, wreckCargoExpired = 0;
int salvagedModules = 0, wreckModulesMade = 0, wreckModulesExpired = 0;
var fireEnds = new SortedDictionary<string, int>(StringComparer.Ordinal);
var repairByShip = new Dictionary<ShipId, double>();
double hullFracSum = 0; long hullFracN = 0, hullSerious = 0; int repairTrips = 0;
int brokeTraders = 0, brokeWithRepair = 0, brokeFailed = 0, brokeDamaged = 0; double brokeRepairSum = 0;
u.Events.Raised += e =>
{
    switch (e)
    {
        case ModuleDamaged md2: modDamaged++; if (md2.Failed) modFailed++; break;
        case FireStarted: firesStarted++; break;
        case RepairTripStarted: repairTrips++; break;
        case FireEnded fe: fireEnds[fe.How] = fireEnds.GetValueOrDefault(fe.How) + 1; break;
        case ShipRepaired sr:
            if (sr.AtBase) { repairsBase++; repairBaseCost += sr.Cost; }
            else
            {
                if (u.Ships.TryGetValue(sr.Ship, out var rs) && rs.IsPlayer) repairsPlayer++; else repairsNpc++;
                repairByShip[sr.Ship] = repairByShip.GetValueOrDefault(sr.Ship) + sr.Cost;
                repairCost += sr.Cost; repairPlates += sr.Plates; repairElec += sr.Electronics;
            }
            repairModules += sr.Modules;
            break;
        case WreckCreated wc:
            if (wc.Kind == WreckKind.Wreck) wrecksMade++; else containersMade++;
            wreckCargoMade += wc.CargoUnits; wreckModulesMade += wc.Modules; break;
        case ShipDeparted { Reason: DepartureReason.Bankrupt, Role: ShipRole.Trader } sdb:
            brokeTraders++;
            if (repairByShip.TryGetValue(sdb.Ship, out var paid)) { brokeWithRepair++; brokeRepairSum += paid; }
            if (u.Ships.TryGetValue(sdb.Ship, out var bs))
            {
                if (bs.Fit.Select((k, i) => i).Any(i => SpaceSim.Core.Combat.ModuleDamage.Failed(u, bs, i))) brokeFailed++;
                if (bs.Hull < bs.MaxHull * 0.999f) brokeDamaged++;
            }
            break;
        case WreckExpired wx: wrecksExpired++; wreckCargoExpired += wx.CargoUnits; wreckModulesExpired += wx.Modules; break;
        case WreckSalvaged ws:
            if (ws.Module is not null) salvagedModules++;
            else if (ws.ByNpc) salvagedCargoNpc += ws.Units; else salvagedCargoPlayer += ws.Units;
            break;
    }
};
// Phase 13: Forschung — FP, Takte (gelaufen/gestockt nach Grund), Personalkosten, verbrauchte Güter (Basiswert), Budget aus den Kassen, Abschlüsse
double researchPoints = 0, researchSalary = 0, researchGoods = 0, researchFunding = 0, researchPlayer = 0;
int researchCycles = 0, researchStalled = 0;
var stallReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
var researchDone = new List<string>();
u.Events.Raised += e =>
{
    switch (e)
    {
        case ResearchCycle rc when rc.Stalled is null: researchCycles++; researchPoints += rc.Points; researchSalary += rc.Credits; researchGoods += rc.GoodsValue; break;
        case ResearchCycle rc: researchStalled++; stallReasons[rc.Stalled!] = stallReasons.GetValueOrDefault(rc.Stalled!) + 1; break;
        case ResearchCompleted done: researchDone.Add($"{u.Factions[done.Faction].Name}: {u.Research.Get(done.Project)?.Name ?? done.Project} (Tick {done.Tick})"); break;
        case TreasuryUpkeep { Purpose: "research" } rf: researchFunding += rf.Amount; break;
        case ResearchFunded fu: researchPlayer += fu.Credits; break;
    }
};
// Phase 14: Exportsenke — exportierte Mengen und Erlös je Ware, neu geschöpftes Geld (Erlös − bezahlter Ankauf)
var exportUnits = new SortedDictionary<int, float>(); var exportRevenue = new SortedDictionary<int, float>();
double exportPaid = 0, exportRev = 0; int exportBatches = 0, exportQueued = 0;
u.Events.Raised += e =>
{
    switch (e)
    {
        case ExportQueued: exportQueued++; break;
        case GoodsExported ge:
            exportBatches++; exportPaid += ge.Paid; exportRev += ge.Revenue;
            exportUnits[ge.Good.Value] = exportUnits.GetValueOrDefault(ge.Good.Value) + ge.Amount;
            exportRevenue[ge.Good.Value] = exportRevenue.GetValueOrDefault(ge.Good.Value) + ge.Revenue;
            break;
    }
};
// Phase 15: Ausgaben der Fraktionskassen (Instandhaltung, Subventionen)
double maintPaid = 0, maintUnpaid = 0, subsidyPaid = 0, retiredCredits = 0; int subsidies = 0, retired = 0;
var subsidized = new SortedDictionary<int, float>();
u.Events.Raised += e =>
{
    switch (e)
    {
        case TreasuryUpkeep { Purpose: "maintenance" } mu: maintPaid += mu.Amount; maintUnpaid += mu.Unpaid; break;
        case TreasuryUpkeep { Purpose: "subsidy" } su: subsidyPaid += su.Amount; break;
        case ShipRetired sr: retired++; retiredCredits += sr.Credits; break;
        case StationSubsidized ss: subsidies++; subsidized[ss.Station.Value] = subsidized.GetValueOrDefault(ss.Station.Value) + ss.Amount; break;
    }
};
// Phase 19: Stilllegungen/Umwidmungen, Auslastung je Station (alle 200 Ticks), Knappheit ohne freien Platz
var retiredStations = new List<string>();
int buildersCommissioned = 0, discardCount = 0;
var discarded = new SortedDictionary<string, float>(StringComparer.Ordinal);   // Phase 19: von KI-Händlern im All abgeworfen (kein Käufer nahm den Rest)
var dumped = new SortedDictionary<string, float>(StringComparer.Ordinal);   // Phase 19: über die Annahme abgeladen (Station: Ware → Einheiten)
var utilSum = new SortedDictionary<int, (string Name, string Type, bool Protected, double Act, double Starved, int N)>();
var unanswered = new SortedDictionary<string, int>(StringComparer.Ordinal);
int capSamples = 0, capFullSamples = 0;
u.Events.Raised += e =>
{
    if (e is CargoDumped cdu) { string dk = $"{u.Stations.GetValueOrDefault(cdu.Station)?.Name ?? "#" + cdu.Station.Value}: {u.Goods[cdu.Good].Name}"; dumped[dk] = dumped.GetValueOrDefault(dk) + cdu.Units; }
    if (e is BuilderCommissioned) buildersCommissioned++;
    if (e is CargoDiscarded cdi) { discardCount++; string gk = u.Goods[cdi.Good].Name; discarded[gk] = discarded.GetValueOrDefault(gk) + cdi.Units; }
    if (e is StationRetired sre)
        retiredStations.Add($"Tick {u.Tick}: {sre.Name} ({u.StationTypes.GetValueOrDefault(sre.TypeKey)?.Name ?? sre.TypeKey}, Auslastung {100 * sre.Activity:0}%) → "
            + (sre.ForGood.Value == 0 ? "Überzahl abgebaut (Phase 20)" : $"{u.StationTypes.GetValueOrDefault(sre.ForType)?.Name ?? sre.ForType} für {u.Goods[sre.ForGood].Name}")
            + $", Restwert {sre.Payout:0} Cr");
};
// Phase 20: Schiffsabgänge je Aufgabe und Grund, Versorgungspanik
var roleOf = u.Ships.Values.ToDictionary(x => x.Id.Value, x => x.Role);
var removedBy = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
var departureDetails = new SortedDictionary<string, int>(StringComparer.Ordinal);
var cutOffDepartures = new SortedDictionary<string, int>(StringComparer.Ordinal);   // Phase 22: Abgänge in abgeschnittenen Kartenteilen   // Phase 22: Anlass je Aufgabe (raid, intercept, broke, unprofitable, …)
var stationDepartures = new SortedDictionary<string, int>(StringComparer.Ordinal);
var departedShips = new HashSet<int>(); var departedStations = new HashSet<int>();
int removedWithoutReason = 0, firmMoves = 0; float firmMoved = 0f;
var restores = new List<string>();
// Phase 22: Handlungsfähigkeit je Fraktion/Piratengruppe (alle 200 Ticks gemessen): Messpunkte, Messpunkte mit Verstoß, Verstöße nach Art
var viaSamples = new SortedDictionary<int, (int N, int Bad, SortedDictionary<string, int> Kinds)>();
int panicsRaised = 0, panicBuilds = 0, panicDone = 0;
var panicList = new List<string>();
u.Events.Raised += e =>
{
    switch (e)
    {
        case ShipSpawned ssp when u.Ships.TryGetValue(ssp.Ship, out var nsh): roleOf[ssp.Ship.Value] = nsh.Role; break;
        case JobChanged jch: roleOf[jch.Ship.Value] = jch.To; break;
        // Phase 22: Abgänge tragen einen der drei Gründe (zerstört, pleite, nicht rentabel) oder sind Entscheidungen der Spielerperson (verkauft, zurückgegeben)
        case ShipDeparted sdp:
        {
            string role = sdp.Role.ToString();
            if (!removedBy.TryGetValue(role, out var reasons)) removedBy[role] = reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
            string rk = DepartureNames.German(sdp.Reason);
            reasons[rk] = reasons.GetValueOrDefault(rk) + 1;
            string dk = $"{role}/{sdp.Detail}";
            departureDetails[dk] = departureDetails.GetValueOrDefault(dk) + 1;
            departedShips.Add(sdp.Ship.Value);
            // Phase 22: Abgänge in einem abgeschnittenen Kartenteil (nicht im Hauptteil) gesondert zählen
            int mainPart = Profitability.MainPart(u);
            if (mainPart != 0 && MapParts.Of(u).KeyOf(sdp.Sector) != mainPart)
            {
                string ck = $"{role} {DepartureNames.German(sdp.Reason)} ({sdp.Detail}) in {u.Sectors[sdp.Sector].Name.Replace("Sektor ", "")}";
                cutOffDepartures[ck] = cutOffDepartures.GetValueOrDefault(ck) + 1;
            }
            break;
        }
        case ShipRemoved srm when !departedShips.Contains(srm.Ship.Value): removedWithoutReason++; break;
        case StationDeparted stdp:
        {
            string sk = $"{u.StationTypes.GetValueOrDefault(stdp.TypeKey)?.Name ?? stdp.TypeKey} {DepartureNames.German(stdp.Reason)} ({stdp.Detail})";
            stationDepartures[sk] = stationDepartures.GetValueOrDefault(sk) + 1;
            departedStations.Add(stdp.Station.Value);
            break;
        }
        case StationRemoved strm when !departedStations.Contains(strm.Station.Value): removedWithoutReason++; break;
        case FactionRestored frs: restores.Add($"Tick {u.Tick}: {u.Factions.GetValueOrDefault(frs.Faction)?.Name ?? "?"} {frs.Role} (bezahlt {frs.Paid:0}, erlassen {frs.Waived:0} Cr)"); break;
        case FirmCapitalMoved fcm: firmMoves++; firmMoved += fcm.Amount; break;
        case SupplyPanicRaised spr:
            panicsRaised++;
            if (panicList.Count < 12)
                panicList.Add($"Tick {u.Tick}: Teil ab {u.Sectors[spr.Part].Name} ohne {u.Goods[spr.Good].Name} (Tor {spr.Gate.Value}) → {(spr.TypeKey is { } tk ? u.StationTypes.GetValueOrDefault(tk)?.Name ?? tk : "im Teil nicht herstellbar")}");
            break;
        case SupplyPanicBuild: panicBuilds++; break;
        case ConstructionCompleted pcc when u.Stations.TryGetValue(pcc.Station, out var pst) && pst.Panic: panicDone++; break;
    }
};
// Phase 14: Preise der reinen Verbraucher (Kolonie) je Zutat über die Messpunkte (Preis/Basis)
var consumerPrices = new SortedDictionary<string, List<float>>(StringComparer.Ordinal);
u.Events.Raised += e =>
{
    if (e is EmergencyRefuel er) { notk++; notkCost += er.Cost; notkUnits += er.Units; }
    else if (e is FuelBought fb) { fuelBuys++; fuelBuyCost += fb.Cost; fuelBuyUnits += fb.Units; }
    else if (e is FleetShipBuilt fsb) { if (fsb.WithModules) buildsWithModules++; else buildsKitOnly++; }
    else if (e is JobChanged) jobSwitches++;
    else if (e is ShipQualityRolled sq) { gradeRolled[(int)sq.Rolled]++; gradeGot[(int)sq.Grade]++; }
    else if (e is UpgradeAttempted ua) { upgradeTries++; if (ua.Success) upgradeOk++; upgradeCost += ua.Price; }
    else if (e is FleetModelChosen fmc) chosenModels[fmc.ModelKey] = chosenModels.GetValueOrDefault(fmc.ModelKey) + 1;
    else if (e is TradeCompleted tc && u.Modules.ContainsKey(u.Goods[tc.Good].Key)) { moduleDelivered += tc.Amount; moduleRevenue += tc.Revenue; }
};

var header = new List<string> { "tick", "stations", "sites", "traders", "miners", "stationCredits", "shipCredits", "playerCredits", "treasuryCredits", "tank", "notk", "locks", "isolated", "researchPoints", "researched" };
header.AddRange(u.Goods.Values.Select(g => "price_" + g.Name));
csv.AppendLine(string.Join(",", header));
Console.WriteLine($"{"tick",7} {"stat",4} {"bau",3} {"trad",4} {"mine",4} {"stCredits",10} {"shCredits",10} {"plCredits",9} {"kassen",8} {"tank",5} {"notk",5} {"sperr",5} {"iso",3} {"fp",6} {"erf",3}  " + string.Join(" ", u.Goods.Values.Select(g => (g.Name.Length > 5 ? g.Name[..5] : g.Name).PadLeft(5))) + "   (Preis/Basis)");

// Tor-Szenario: --gates "1:closed@20000,1:open@60000" schließt/öffnet Tore (beide Enden) zu festen Ticks (auf 20 Ticks genau)
var gateEvents = new List<(long Tick, int Gate, GateState State)>();
if (opts.TryGetValue("gates", out var gateSpec))
    foreach (var part in gateSpec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var at = part.Split('@'); var gs = at[0].Split(':');
        gateEvents.Add((long.Parse(at[1], CultureInfo.InvariantCulture), int.Parse(gs[0], CultureInfo.InvariantCulture), Enum.Parse<GateState>(gs[1], true)));
    }
gateEvents.Sort();
int gateNext = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();
var history = new List<EconomySnapshot>();
MapSplit? worstSplit = null, finalSplit = null;
// Phase 16: der letzte Abschnitt läuft bis genau --ticks, auch wenn --every die Laufzeit nicht teilt (vorher endete der Lauf beim letzten Vielfachen)
for (int t = 0, chunk = 0; t <= ticks; chunk = Math.Min(every, ticks - t), t += t == ticks ? 1 : chunk)
{
    if (t > ticks) break;
    if (t > 0)
        for (int done = 0; done < chunk; )
        {
            // Phase 16: bis zum nächsten Vielfachen von WatchEvery laufen — die Messpunkte liegen so unabhängig von --every immer auf denselben Ticks
            int step = Math.Min(WatchEvery - (int)(u.Tick % WatchEvery), chunk - done);
            // Runde 38: --playerwar F@tick — der Spieler erklärt Fraktion F zum Tick den Krieg (Kriegskasse wird gestellt, falls sie fehlt)
            if (playerWar is { } pw && u.Tick == pw.Tick && u.PlayerFaction is not null)
            {
                if (u.Player.Treasury.Credits < u.Rules.WarChest) u.Player.Treasury.Credits = u.Rules.WarChest;
                var issue = Wars.PlayerDeclareIssue(u, new FactionId(pw.Faction));
                if (issue is null) { Wars.PlayerDeclare(u, new FactionId(pw.Faction)); playerWarStarted = u.Tick; }
                Console.WriteLine($"   Testtreiber: Spielerkrieg gegen {u.Factions.GetValueOrDefault(new FactionId(pw.Faction))?.Name ?? "?"} bei Tick {u.Tick}: {issue ?? "erklärt"}");
            }
            foreach (var cd in crimeDriver)
                if (u.Tick == cd.Tick && u.PlayerFaction is not null)
                {
                    var cf = new FactionId(cd.Faction);
                    if (firstCrimeTick < 0) { firstCrimeTick = u.Tick; tollsAtFirstCrime = tolls; }
                    for (int ci = 0; ci < cd.Count; ci++)
                    {
                        Wanted.Report(u, u.PlayerActor, cf, cd.Kind == "kill" ? "assault" : cd.Kind);
                        if (cd.Kind == "kill") CrimeStanding.Apply(u, u.PlayerActor, cf, "kill");
                    }
                    crimeLog.Add($"Tick {u.Tick}: {cd.Count}x {cd.Kind} bei {u.Factions[cf].Name} -> Beziehung {Relations.Effective(u, cf, u.PlayerActor):+0.00;-0.00}, Fahndung {Wanted.LevelOf(u, u.PlayerActor, cf)}");
                }
            if (allianceDriver is { } ald && u.Tick == ald.Item3)
            {
                FactionId fa = new(ald.Item1), fb = new(ald.Item2);
                Relations.Set(u, fa, fb, 0.7f); Relations.Set(u, fb, fa, 0.7f);
                var issue = Alliances.FormIssue(u, fa, fb);
                if (issue is null) Alliances.Form(u, fa, fb);
                Console.WriteLine($"   Testtreiber: Bündnis {ald.Item1}–{ald.Item2} bei Tick {u.Tick}: {issue ?? "geschlossen"}");
            }
            foreach (var td in treasuryDriver)
                if (u.Tick == td.Item3 && u.Actors.TryGetValue(new ActorId(td.Item1), out var tda))
                {
                    tda.Treasury.Credits = td.Item2;
                    Console.WriteLine($"   Testtreiber: Kasse von {tda.Name} bei Tick {u.Tick} auf {td.Item2} Cr");
                }
            if (aiWarDriver is { } awd && u.Tick == awd.Item3)
            {
                Wars.Declare(u, new FactionId(awd.Item1), new FactionId(awd.Item2));
                Console.WriteLine($"   Testtreiber: KI-Krieg {awd.Item1} → {awd.Item2} bei Tick {u.Tick}");
            }
            if (gateDownEvery > 0 && u.Tick > 0 && u.Tick % gateDownEvery == 0 && GateCombat.DownCount(u) == 0)
            {
                // Testtreiber: das nächste Tor-Paar (Id-Reihenfolge, reihum), das Kampfwerte hat und — ohne --gatedownsplit — die Karte nicht teilt
                var pairs = u.Gates.Values.Where(g => g.Id.Value < g.Target.Value && GateCombat.TypeOf(u, g) is not null && (gateDownSplit || !MapParts.SplitsWhenClosed(u, g.Id)))
                    .OrderBy(g => g.Id.Value).ToList();
                if (pairs.Count > 0)
                {
                    var pick = pairs[gateDownCursor++ % pairs.Count];
                    GateCombat.Disable(u, pick, default);
                    Console.WriteLine($"   Testtreiber: {GateCombat.NameOf(u, pick)} außer Betrieb bei Tick {u.Tick}");
                }
            }
            for (; gateNext < gateEvents.Count && gateEvents[gateNext].Tick <= u.Tick; gateNext++)
            {
                var ge = gateEvents[gateNext];
                GateAccess.SetState(u, u.Gates[new GateId(ge.Gate)], ge.State);
                Console.WriteLine($"   Tor {ge.Gate} → {ge.State} bei Tick {u.Tick}");
            }
            if (hintProbe is null) runner.Run(u, step);
            else for (int i = 0; i < step; i++) { runner.Run(u, 1); hintProbe.Observe(u); }   // Runde 28: Preisgedächtnis je Tick wie in der Sitzung
            done += step;
            if (tradeProbe is not null && u.Tick % PlayerTradeProbe.Every == 0) tradeProbe.Sample(u);   // Runde 23: --probe spielerhandel
            if (cashProbe is not null && u.Tick % CashProbe.Every == 0) cashProbe.Sample(u);
            if (hintProbe is not null && u.Tick % 20 == 0) hintProbe.Sample(u);
            if (u.Tick % 20000 == 0)   // Phase 24 (Korrektur): Piratenbestand und -kasse über die Zeit
            {
                pirateTimeline.Add($"t{u.Tick / 1000}k {raids - raidsAtLastMark} Üf. " + string.Join(" ", u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw).OrderBy(a => a.Id.Value)
                    .Select(a => $"{a.Name.Split(' ').Last()} {u.Ships.Values.Count(x => ThreatMap.IsRaider(x) && x.Owner == a.Id)}/{a.Treasury.Credits:0}")));
                raidsAtLastMark = raids;
                // Prüfung Phase 28: --probe piraten — Zustand jedes Kaperschiffs (Hülle, Notfall, Befehl, daheim, Munition, Denkzeit)
                if (opts.TryGetValue("probe", out var pp) && pp == "piraten")
                    foreach (var x in u.ShipsById().Where(ThreatMap.IsRaider))
                        Console.WriteLine($"   SONDE t{u.Tick} Kaperer {x.Id.Value} {u.ActorName(x.Owner)}: Hülle {x.Hull:0}/{x.MaxHull:0} Notfall {x.Emergency} Befehl {x.Command?.GetType().Name ?? "-"} Auftrag {x.Order?.GetType().Name ?? "-"} daheim {PirateSystem.AtHome(u, x)} im Gefecht {u.Fights.IsEngaged(x.Id)} Transit {x.Transit is not null} Denken {x.NextThinkTick - u.Tick} Munition {x.AmmoLoaded:0} Module aus {string.Join(",", Enumerable.Range(0, x.Fit.Count).Where(i => x.Fit[i] != null && x.ConditionOf(i) < u.Rules.ModuleFailCondition))} Brand {x.Systems.Burning.Count} Verband {x.Squadron.Value} Sektor {x.Sector.Value}");
            }
            if (u.Tick >= watchFrom && u.Tick % WatchEvery == 0)
            {
                probeWatch.Start();
                watch.Sample(u);
                foreach (var ts in u.Ships.Values)   // Runde 44: Fahrten
                {
                    if (!ts.IsEconomyAi || ts.Role is not (ShipRole.Trader or ShipRole.Miner)) continue;
                    var cur = tripTransit[ts.Role];
                    bool moving = ts.Transit is not null;
                    float cap = MathF.Max(1f, ts.CargoCapacity);
                    tripTransit[ts.Role] = (cur.In + (moving ? 1 : 0), cur.All + 1, cur.Fill + (moving ? ts.UsedVolume(u) / cap : 0), cur.FillN + (moving ? 1 : 0));
                    if (moving && tripSeen.Add((ts.Id.Value, ts.Transit!.StartTick))) tripTicks[ts.Role].Add(ts.Transit.ArrivalTick - ts.Transit.StartTick);
                }
                recoveringMax = Math.Max(recoveringMax, u.Recovering.Count);   // Phase 23
                fightsMax = Math.Max(fightsMax, u.Fights.Count);   // Phase 24
                gateDownMax = Math.Max(gateDownMax, GateCombat.DownCount(u));   // Phase 31
                if (u.Tick % 200 == 0)   // Phase 26 (Korrektur): mittlere Hüllenquote und Anteil schwer beschädigter Händler/Schürfer
                    foreach (var es in u.Ships.Values)
                        if (es.IsEconomyAi) { hullFracSum += es.Hull / MathF.Max(1f, es.MaxHull); hullFracN++; if (NpcRepair.Serious(u, es)) hullSerious++; }
                if (u.Tick % 200 == 0)
                {
                    foreach (var j in ShipJobs.Default) capMax[j.Key] = MathF.Max(capMax.GetValueOrDefault(j.Key), ShipJobs.UsedUnits(u, j));
                    // Phase 19: Auslastung je Station und Knappheit, auf die wegen voller Plätze niemand antwortet
                    foreach (var ust in u.Stations.Values)
                        if (!ust.IsBuildSite && ust.Production.Count > 0)
                        {
                            var cur = utilSum.GetValueOrDefault(ust.Id.Value, (ust.Name, ust.TypeKey, ust.BankruptcyProtected, 0, 0, 0));
                            utilSum[ust.Id.Value] = (ust.Name, ust.TypeKey, ust.BankruptcyProtected, cur.Act + ust.Activity, cur.Starved + ust.Starved, cur.N + 1);
                        }
                    capSamples++;
                    // Phase 22: Invarianten der Handlungsfähigkeit je Fraktion und Piratengruppe
                    foreach (var va in u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw || a.IsNpcFaction(u)).OrderBy(a => a.Id.Value))
                    {
                        var issues = FactionViability.Check(u, va.Id);
                        var cur = viaSamples.TryGetValue(va.Id.Value, out var vs) ? vs : (N: 0, Bad: 0, Kinds: new SortedDictionary<string, int>(StringComparer.Ordinal));
                        foreach (var iss in issues) cur.Kinds[iss.Kind.ToString()] = cur.Kinds.GetValueOrDefault(iss.Kind.ToString()) + 1;
                        // Gerätetest (Phase 26): Rücklage im Aufbau (kein Verstoß) getrennt mitzählen
                        if (va.IsNpcFaction(u) && FactionViability.ReserveBuilding(u, va) && va.Treasury.Credits < FactionViability.ReserveOf(u, va))
                            cur.Kinds["RücklageImAufbau(kein Verstoß)"] = cur.Kinds.GetValueOrDefault("RücklageImAufbau(kein Verstoß)") + 1;
                        viaSamples[va.Id.Value] = (cur.N + 1, cur.Bad + (issues.Count > 0 ? 1 : 0), cur.Kinds);
                    }
                    if (StationProtection.CountEconomic(u) >= u.Rules.MaxStations)
                    {
                        capFullSamples++;
                        var lsc = StationLifecycleSystem.Scan(u);
                        if (StationReview.PersistentScarcity(u, lsc) is { } pg && StationLifecycleSystem.ProducerFor(u, pg, lsc.Ratios) is { } pt)
                        {
                            string uk = $"{u.Goods[pg].Name} ({pt.Name})";
                            unanswered[uk] = unanswered.GetValueOrDefault(uk) + 1;
                        }
                    }
                }
                foreach (var xs in u.Stations.Values)
                    if (ExportRules.ConfigOf(u, xs) is not null)
                        exportCashMin[xs.Id.Value] = MathF.Min(exportCashMin.GetValueOrDefault(xs.Id.Value, float.MaxValue), xs.Credits);
                probeWatch.Stop();
            }
        }
    var snap = EconomyStats.Snapshot(u);
    history.Add(snap);
    // Phase 10: Sperren und Zerfall der Karte (physisch oder für die freien Händler = die Wirtschaft)
    int locks = u.Sectors.Values.Count(x => !x.Access.IsOpen);
    var phys = MapConnectivity.Physical(u);
    var econ = MapConnectivity.Economy(u);
    var split = phys.Isolated.Count >= econ.Isolated.Count ? phys : econ;
    if (split.IsSplit && (worstSplit is null || split.Isolated.Count > worstSplit.Isolated.Count)) worstSplit = split;
    if (t == ticks) finalSplit = split.IsSplit ? split : null;
    // Phase 14: Preise an den reinen Verbrauchern (Stationen mit Rezepten ohne Produkte, z. B. Kolonie) je Zutat
    foreach (var cst in u.Stations.Values.Where(x => !x.IsBuildSite && x.Production.Count > 0 && x.Production.All(r => r.Outputs.Count == 0)).OrderBy(x => x.Id.Value))
        foreach (var gid in cst.Production.SelectMany(r => r.Inputs.Keys).Distinct().OrderBy(g => g.Value))
            if (cst.Market.TryGetValue(gid, out var ce) && u.Goods[gid].BasePrice > 0f)
            {
                string ck = $"{cst.Name}: {u.Goods[gid].Name}";
                if (!consumerPrices.TryGetValue(ck, out var cl)) consumerPrices[ck] = cl = new List<float>();
                if (t > 0) cl.Add(ce.Price / u.Goods[gid].BasePrice);
            }
    // Phase 13: Forschungsstand aller Fraktionen (Summe der FP und der abgeschlossenen Projekte)
    float fp = u.Factions.Values.Sum(f => f.Research.PointsTotal);
    int researched = u.Factions.Values.Sum(f => f.Research.Completed.Count);
    Console.WriteLine($"{snap.Tick,7} {snap.Stations,4} {snap.Sites,3} {snap.Traders,4} {snap.Miners,4} {snap.StationCredits,10:0} {snap.ShipCredits,10:0} {snap.PlayerCredits,9:0} {snap.TreasuryCredits,8:0} {fuelBuys,5} {notk,5} {locks,5} {split.Isolated.Count,3} {fp,6:0} {researched,3}  " +
                      string.Join(" ", snap.Goods.Select(g => g.PriceRatio.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5))));
    var row = new List<string> { snap.Tick.ToString(), snap.Stations.ToString(), snap.Sites.ToString(), snap.Traders.ToString(), snap.Miners.ToString(),
        snap.StationCredits.ToString("0", CultureInfo.InvariantCulture), snap.ShipCredits.ToString("0", CultureInfo.InvariantCulture),
        snap.PlayerCredits.ToString("0", CultureInfo.InvariantCulture), snap.TreasuryCredits.ToString("0", CultureInfo.InvariantCulture), fuelBuys.ToString(), notk.ToString(),
        locks.ToString(), split.Isolated.Count.ToString(), fp.ToString("0", CultureInfo.InvariantCulture), researched.ToString() };
    row.AddRange(snap.Goods.Select(g => g.AvgPrice.ToString("0.00", CultureInfo.InvariantCulture)));
    csv.AppendLine(string.Join(",", row));
}
sw.Stop();

Console.WriteLine();
Console.WriteLine($"{ticks} Ticks in {sw.ElapsedMilliseconds} ms ({ticks / Math.Max(1.0, sw.ElapsedMilliseconds):0.0} Ticks/ms, {sw.Elapsed.TotalMilliseconds / Math.Max(1, ticks):0.000} ms/Tick), Hash {UniverseHash.Compute(u):X16}");
if (playerWar is not null)
    Console.WriteLine($"Spielerkrieg (Testtreiber): erklärt bei Tick {playerWarStarted}; {(playerPeace.Length > 0 ? playerPeace : "läuft noch")}; Sperränderungen gegen dich {playerLocks}; "
        + $"Sektoren jetzt gegen dich gesperrt: {u.Sectors.Values.Count(x => x.Access.EntryLevel > 0 && x.Access.Against is { } ag && u.IsPlayerFaction(ag))}");
if (crimeDriver.Count > 0)
{
    // Spielersperre: Verlauf, Sperren gegen den Spieler, Zoll der übrigen Welt vor/nach der ersten Sperre, Urteil der Tore für das Spielerschiff
    Console.WriteLine($"Spielersperre (Testtreiber): erstes Verbrechen Tick {firstCrimeTick}, erste Sperre gegen dich Tick {firstPlayerLockTick}, höchste Stufe {maxPlayerLock}; "
        + $"Ansehen verloren {crimeStandingLoss:0.00}, erholt {crimeStandingRecovered:0.00}; Zoll gesamt bis 1. Verbrechen {Math.Max(0, tollsAtFirstCrime):0} Cr, bis 1. Sperre {Math.Max(0, tollsAtFirstLock):0} Cr, am Ende {tolls:0} Cr");
    foreach (var f in crimeDriver.Select(c => c.Faction).Distinct())
    {
        var cf = new FactionId(f);
        var me = u.Ships.GetValueOrDefault(u.PlayerShip);
        var secs = u.Sectors.Values.Where(x => x.Owner == cf).OrderBy(x => x.Id.Value)
            .Select(x => $"{x.Name} Stufe {x.Access.Level(AccessDirection.Entry)}" + (me is not null ? $" ({AccessInfo.ForSector(u, x.Id, me).ForPlayer})" : "")).ToList();
        Console.WriteLine($"   {u.Factions[cf].Name}: Beziehung jetzt {Relations.Effective(u, cf, u.PlayerActor):+0.00;-0.00}, Fahndung {Wanted.LevelOf(u, u.PlayerActor, cf)}, offen {CrimeStanding.Text(u, cf) ?? "-"}; " + string.Join("; ", secs));
    }
    foreach (var line in crimeLog.Take(60)) Console.WriteLine("   " + line);
    if (crimeLog.Count > 60) Console.WriteLine($"   ... {crimeLog.Count - 60} weitere");
}
Console.WriteLine($"Hash ohne Spielerpolitik {UniverseHash.Compute(u, includeTags: true, includePlayerPolitics: false):X16} (Runde 38: gleich mit Spielerfraktion an/aus; Spielerbeziehungen {Relations.StoredPlayerEntries(u)})");
// Phase 17: Rechenzeit je System (Top 5 in % der Laufzeit; der Rest ist Ereignisauswertung, Messpunkte und Messaufwand des Tools)
{
    double total = Math.Max(1.0, sw.Elapsed.TotalMilliseconds);
    var bySystem = systemWatches.GroupBy(x => x.Name).Select(g => (Name: g.Key, Ms: g.Sum(x => x.Watch.Elapsed.TotalMilliseconds))).OrderByDescending(x => x.Ms).ToList();
    double simMs = bySystem.Sum(x => x.Ms);
    if (perSystem) Console.WriteLine("Rechenzeit je System (Top 5): " + string.Join(", ", bySystem.Take(5).Select(x => $"{x.Name} {100 * x.Ms / total:0.0}%"))
        + $"; alle Systeme {100 * simMs / total:0.0}%, Messprobe des Tools {100 * probeWatch.Elapsed.TotalMilliseconds / total:0.0}%");
    // Phase 23: Schiffssysteme — wie viele Schiffe sich erholen und was das kostet
    double sysMs = bySystem.Where(x => x.Name == "ShipSystemsSystem").Sum(x => x.Ms);
    Console.WriteLine($"Schiffssysteme: Erholungsindex max {recoveringMax}, jetzt {u.Recovering.Count} Schiffe; Schilde jetzt {u.Ships.Values.Count(x => x.Stats.Shield > 0f && x.ShieldNow < x.Stats.Shield)} von {u.Ships.Values.Count(x => x.Stats.Shield > 0f)} nicht voll"
        + (perSystem ? $"; Rechenzeit ShipSystemsSystem {100 * sysMs / total:0.00}%" : ""));
    // Phase 24: eigene Zeile für das Rundengefecht
    if (perSystem)
        Console.WriteLine($"Rechenzeit Gefecht: CombatSystem {100 * bySystem.Where(x => x.Name == "CombatSystem").Sum(x => x.Ms) / total:0.00}%, "
            + $"RearmSystem {100 * bySystem.Where(x => x.Name == "RearmSystem").Sum(x => x.Ms) / total:0.00}%");
}
var prop = u.FindGood("propellant");
if (prop is not null && u.Rules.FuelActive)
{
    float propAvg = watch.AverageRatio(prop.Id, ticks * 3 / 4) ?? 0f;   // Phase 16: dichte Probe
    Console.WriteLine($"Treibstoff (propellant): {fuelBuys} Tankkäufe an Stationen ({fuelBuyCost:0} Cr), {notk} Notkäufe ({notkCost:0} Cr), Ø Preis/Basis letzte 25%: {propAvg:0.00}");
}
if (u.Modules.Count > 0)
{
    var moduleGoods = u.Goods.Values.Where(g => u.Modules.ContainsKey(g.Key)).Select(g => g.Id).ToHashSet();
    float atFactories = u.Stations.Values.SelectMany(st => st.Market.Where(kv => moduleGoods.Contains(kv.Key) && !st.IsStockOnly(kv.Key))).Sum(kv => kv.Value.Amount);
    float inStocks = u.Stations.Values.SelectMany(st => st.Market.Where(kv => moduleGoods.Contains(kv.Key) && st.IsStockOnly(kv.Key))).Sum(kv => kv.Value.Amount);
    Console.WriteLine($"Modul-Waren: {moduleDelivered:0} Einheiten von Händlern geliefert ({moduleRevenue:0} Cr); Bestand in Fabriken {atFactories:0}, in Lagern ohne Rezept {inStocks:0}");
    Console.WriteLine($"KI-Neubauten: {buildsWithModules} mit Modulen aus dem Werftlager, {buildsKitOnly} nur aus dem Bausatz");
}
// Phase 6: Flotte nach Modell und Aufgabe, belegte Kapazität je Aufgabe, Aufgabenwechsel, Modellwahl, Engpass-Module
{
    var ai = u.Ships.Values.Where(x => x.IsEconomyAi).ToList();
    var byModel = ai.GroupBy(x => (x.ModelKey.Length > 0 ? x.ModelKey : "-") + "/" + x.Role).OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => $"{g.Key} {g.Count()}");
    Console.WriteLine("Flotte (Modell/Aufgabe): " + string.Join(", ", byModel));
    Console.WriteLine("Kapazität: " + string.Join(", ", ShipJobs.Default.Select(j => $"{j.Key} {ShipJobs.UsedUnits(u, j):0.0}/{j.Limit(u.Rules):0} (max {capMax.GetValueOrDefault(j.Key):0.0})"))
        + $"; Aufgabenwechsel: {jobSwitches}; Multirole-Schiffe: {ai.Count(x => ShipJobs.IsMultirole(u, x))}; unrentabel ausgemustert: {retired} ({retiredCredits:0} Cr)");
    // Phase 22: Abgänge je Aufgabe nur aus den drei Gründen (zerstört, pleite, nicht rentabel) plus Entscheidungen der Spielerperson (verkauft, zurückgegeben)
    Console.WriteLine("Abgänge je Aufgabe (Grund): " + (removedBy.Count == 0 ? "keine" : string.Join("; ", removedBy.Select(kv => $"{kv.Key} " + string.Join(", ", kv.Value.Select(r => $"{r.Key} {r.Value}")))))
        + (removedWithoutReason > 0 ? $"; WARNUNG: {removedWithoutReason} Abgänge ohne Grund" : ""));
    Console.WriteLine("  Anlässe: " + (departureDetails.Count == 0 ? "keine" : string.Join(", ", departureDetails.Select(kv => $"{kv.Key} {kv.Value}")))
        + $" | Stationen: {(stationDepartures.Count == 0 ? "keine" : string.Join(", ", stationDepartures.Select(kv => $"{kv.Key} {kv.Value}")))}"
        + $" | Kapitalausgleich in Firmen: {firmMoves}× {firmMoved:0} Cr");
    Console.WriteLine("  Abgänge in abgeschnittenen Kartenteilen: " + (cutOffDepartures.Count == 0 ? "keine" : string.Join(", ", cutOffDepartures.Select(kv => $"{kv.Key} {kv.Value}"))));
    // Phase 20: bei zerfallener Karte die Flottenkapazität je erreichbarem Kartenteil
    var mparts = MapParts.Of(u);
    if (mparts.IsSplit)
        foreach (var mp in mparts.Parts)
            Console.WriteLine($"  Kartenteil {string.Join("+", mp.Select(id => u.Sectors[id].Name))}: " + string.Join(", ", ShipJobs.Default.Select(j =>
                $"{j.Key} {FleetCapacity.Used(u, j, mp[0].Value):0.0}/{FleetCapacity.Limit(u, j, mp[0].Value):0.0}")));
    Console.WriteLine("Modellwahl der KI: " + (chosenModels.Count == 0 ? "keine" : string.Join(", ", chosenModels.Select(kv => $"{kv.Key} {kv.Value}"))));
    Console.WriteLine("Kampfschiffe gekauft/gebaut je Modell: " + (forceModels.Count == 0 ? "keine" : string.Join(", ", forceModels.Select(kv => $"{kv.Key} {kv.Value}"))));
    Console.WriteLine("Kampfschiffe jetzt: " + string.Join(", ", u.Ships.Values.Where(x => x.Role is ShipRole.Police or ShipRole.Pirate or ShipRole.Warship)
        .GroupBy(x => $"{x.Role}/{x.ModelKey}").OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}")));
    Console.WriteLine("Abgänge Handelsschiffe je Modell: " + (traderDepartures.Count == 0 ? "keine" : string.Join(", ", traderDepartures.Select(kv => $"{kv.Key} {kv.Value}"))));
    // Phase 16: aus der dichten Probe (alle 20 Ticks im letzten Viertel), nicht aus den --every-Messpunkten
    string Ratio(string key) => u.FindGood(key) is { } gg && watch.AverageRatio(gg.Id, ticks * 3 / 4) is { } rr ? rr.ToString("0.00", CultureInfo.InvariantCulture) : "-";
    string Grades(int[] g) => $"{g[0]}/{g[1]}/{g[2]}/{g[3]}";
    var founders = u.Ships.Values.Where(x => x.IsEconomyAi).GroupBy(x => x.Quality).OrderBy(g => g.Key).Select(g => $"{ShipQualityNames.Name(g.Key)} {g.Count()}");
    Console.WriteLine($"Güteklassen Neubauten (Standard/Verbessert/Elite/Meisterwerk): gewürfelt {Grades(gradeRolled)}, erreicht {Grades(gradeGot)}; Flotte jetzt: {string.Join(", ", founders)}");
    Console.WriteLine($"Upgrades (NPC): {upgradeTries} Versuche, {upgradeOk} gelungen, {upgradeCost:0} Cr");
    Console.WriteLine($"Engpass-Module (Ø Preis/Basis letzte 25%): Reaktor {Ratio("reactor_mk1")}, Triebwerk {Ratio("chem_engine_s")}, Laser {Ratio("mining_laser_1")}");
    // Phase 23: Stahl und Kupfer tragen die neue Munitionskette (Ø Preis/Basis letzte 25 %, dichte Probe)
    Console.WriteLine($"Stahl/Kupfer/Munition (Ø Preis/Basis letzte 25%): Stahl {Ratio("steel")}, Kupfer {Ratio("copper")}, Munition {Ratio("ammo")}");
    // Phase 26: Reparaturmaterial und neue Module
    Console.WriteLine($"Reparaturmaterial (Ø Preis/Basis letzte 25%): Hüllenplatten {Ratio("hull_plates")}, Elektronik {Ratio("electronics")}; Löschanlage {Ratio("wet_suppressor_s")}, Bergungsgreifer {Ratio("wreck_grapple_s")}, Laserkanone {Ratio("weapon_laser_s")}");
}
// Phase 8: Bau über Baustellen und Notbau-Anteil
{
    int direct = directBuilds - sitesDone;
    float share = sitesDone > 0 ? 100f * sitesEmergency / sitesDone : 0f;
    Console.WriteLine($"Bau: {sitesStarted} Baustellen angelegt, {sitesDone} fertig (davon Notbau {sitesEmergency} = {share:0}%), {sitesAbandoned} verfallen, "
        + $"{u.Stations.Values.Count(x => x.Site is { Abandoned: false })} laufen, {direct} Direktbau; Bauschiffe {u.Ships.Values.Count(x => x.IsAiBuilder)} (von Fraktionen nachgekauft {buildersCommissioned})");
    // Runde 44: Fahrten — mittlere und 90-%-Fahrdauer (Ticks), Anteil der Zeit in Fahrt, mittlere Laderaumfüllung während der Fahrt
    foreach (var role in new[] { ShipRole.Trader, ShipRole.Miner })
    {
        var l = tripTicks[role]; l.Sort();
        var tt = tripTransit[role];
        string name = role == ShipRole.Trader ? "Händler" : "Schürfer";
        Console.WriteLine($"Fahrten {name}: {l.Count} Fahrten, Ø {(l.Count > 0 ? l.Average() : 0):0} Ticks, 90 % {(l.Count > 0 ? l[(int)(0.9 * (l.Count - 1))] : 0)} Ticks, "
            + $"in Fahrt {(tt.All > 0 ? 100.0 * tt.In / tt.All : 0):0}% der Zeit, Laderaum in Fahrt {(tt.FillN > 0 ? 100.0 * tt.Fill / tt.FillN : 0):0}% gefüllt");
    }
    {
        double avg = leaveWindows.Count == 0 ? 0 : leaveWindows.Values.Sum() / (double)Math.Max(1, (ticks + 4999) / 5000);
        int peak = leaveWindows.Count == 0 ? 0 : leaveWindows.Values.Max();
        Console.WriteLine($"Abgänge Händler/Schürfer (pleite/nicht rentabel) je 5000 Ticks: Ø {avg:0.00}, höchstens {peak}"
            + (leaveWindows.Count == 0 ? "" : " — " + string.Join(", ", leaveWindows.Select(kv => $"t{kv.Key * 5}k {kv.Value}"))));
    }
    Console.WriteLine($"Baustellen verfallen: {sitesAbandoned}");
    Console.WriteLine($"Versorgungspanik (Phase 20): {panicsRaised} Panik-Ereignisse, {panicBuilds} Ersatz-Baustellen, {panicDone} fertig; Panik-Stationen jetzt {u.Stations.Values.Count(x => x.Panic)}"
        + (panicList.Count == 0 ? "" : " — " + string.Join("; ", panicList)));
    Console.WriteLine($"Stilllegungen/Umwidmungen: {retiredStations.Count}" + (retiredStations.Count == 0 ? "" : " — " + string.Join("; ", retiredStations)));
    Console.WriteLine($"Plätze voll: {(capSamples > 0 ? 100.0 * capFullSamples / capSamples : 0):0}% der Messzeit; anhaltende Knappheit ohne freien Platz: "
        + (unanswered.Count == 0 ? "keine" : string.Join(", ", unanswered.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {(capSamples > 0 ? 100.0 * kv.Value / capSamples : 0):0}%"))));
    // Auslastung je Stationstyp (Mittel über die Messpunkte): lief / fehlende Zutaten / pausiert ohne Abnehmer
    Console.WriteLine("Auslastung je Stationstyp (lief/Zutaten fehlten, Ø Messzeit): " + string.Join(", ", utilSum.Values.GroupBy(x => x.Type).OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => $"{u.StationTypes.GetValueOrDefault(g.Key)?.Name ?? g.Key} ×{g.Count()} {100 * g.Average(x => x.Act / x.N):0}/{100 * g.Average(x => x.Starved / x.N):0}%")));
    var builtUtil = utilSum.Where(kv => !kv.Value.Protected).ToList();
    if (builtUtil.Count > 0)
        Console.WriteLine("  Gebaute Stationen: " + string.Join(", ", builtUtil.Select(kv => $"{kv.Value.Name} {100 * kv.Value.Act / kv.Value.N:0}/{100 * kv.Value.Starved / kv.Value.N:0}%"
            + (u.Stations.ContainsKey(new StationId(kv.Key)) ? "" : " (stillgelegt)"))));
    Console.WriteLine("Baumaterial geliefert: " + (deliveredBy.Count == 0 ? "nichts" : string.Join(", ", deliveredBy.Select(kv => $"{kv.Key} {kv.Value:0}")))
        + (deliveredGood.Count == 0 ? "" : " | " + string.Join(", ", deliveredGood.Select(kv => $"{kv.Key} {kv.Value:0}")))
        + $"; Notbau {emergencyUnits:0} Einheiten für {emergencyCost:0} Cr (erlassen {emergencyWaived:0}); Spaltungen {splits}");
}
// Phase 9: Akteure, Kassen, Zukauf, Beziehungen
{
    var snap = history[^1];
    Console.WriteLine($"Geldmenge: Stationen {snap.StationCredits:0} + Schiffe {snap.ShipCredits:0} + Spieler {snap.PlayerCredits:0} + Kassen {snap.TreasuryCredits:0} = {snap.TotalCredits:0} Cr; "
        + $"Abgaben in Kassen {taxIn:0}, ohne Empfänger {taxSunk:0}, aus Kassen investiert {treasuryInvested:0}");
    Console.WriteLine($"Kassenausgaben (Phase 15): Instandhaltung erstattet {maintPaid:0} Cr (ungedeckt {maintUnpaid:0}), offen jetzt {u.Stations.Values.Sum(x => Maintenance.BacklogValue(u, x)):0} Cr; "
        + $"Subventionen {subsidyPaid:0} Cr in {subsidies} Zahlungen"
        + (subsidized.Count == 0 ? "" : " (" + string.Join(", ", subsidized.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{u.Stations.GetValueOrDefault(new StationId(kv.Key))?.Name ?? "#" + kv.Key} {kv.Value:0}")) + ")"));
    Console.WriteLine("Kassenanteil an der Geldmenge: " + string.Join(", ", history.Where(h => h.Tick > 0).Select(h => $"{h.Tick} {(h.TotalCredits > 0 ? 100.0 * h.TreasuryCredits / h.TotalCredits : 0):0}%")));
    var actors = snap.Actors ?? Array.Empty<ActorStat>();
    Console.WriteLine($"Akteure: {actors.Count} ({promoted} aufgestiegen, {removedActors} abgestiegen); Beziehungseinträge {Relations.StoredNpcEntries(u)}; "
        + "Größenklassen " + string.Join(", ", actors.GroupBy(a => a.Class).OrderBy(g => g.Key).Select(g => $"{ActorNames.Size(g.Key)} {g.Count()}")));
    foreach (var a in actors.Where(a => a.Kind != ActorKind.Independent || a.Ships + a.Stations > 1).Take(12))
        Console.WriteLine($"  {a.Name,-26} {ActorNames.Kind(a.Kind),-20} {ActorNames.Size(a.Class),-12} Kasse {a.Treasury,9:0}  {a.Ships,3} Schiffe {a.Stations,3} Stationen  Größe {a.Size:0.0}");
    Console.WriteLine("Zukauf: " + (investBy.Count == 0 ? "keiner" : string.Join(", ", investBy.Select(kv => $"{kv.Key} {kv.Value}"))));
    // Phase 22: Handlungsfähigkeit je Fraktion/Piratengruppe über die Messzeit (Anteil der Messpunkte mit Verstoß, welche Art), Notkäufe
    Console.WriteLine("Handlungsfähigkeit: " + (viaSamples.Count == 0 ? "keine Messung" : string.Join("; ", viaSamples.Select(kv =>
    {
        string name = u.ActorName(new ActorId(kv.Key));
        var (n, bad, kinds) = kv.Value;
        return bad == 0 ? $"{name} ja (0 % Verstoß{string.Concat(kinds.Select(k => $", {k.Key} {100.0 * k.Value / Math.Max(1, n):0.#} %"))})"
            : $"{name} {(bad == n ? "nein" : "zeitweise nicht")} ({100.0 * bad / Math.Max(1, n):0.#} % Verstoß: {string.Join(", ", kinds.Select(k => $"{k.Key} {100.0 * k.Value / Math.Max(1, n):0.#} %"))})";
    })) + $"; jetzt: {string.Join(", ", u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw || a.IsNpcFaction(u)).OrderBy(a => a.Id.Value).Select(a => $"{a.Name} {FactionViability.Text(u, a.Id)}"))}"));
    Console.WriteLine($"Notkäufe zur Wiederherstellung: {restores.Count}" + (restores.Count == 0 ? "" : " — " + string.Join("; ", restores.Take(8))));
}
// Phase 10: Gate-Zugang, Sektorkontrolle, Beziehungen, Regierungsstationen
{
    Console.WriteLine($"Politik: {accessChanges} Sperränderungen (höchste Stufe {maxLevelSeen}), {u.Sectors.Values.Count(x => !x.Access.IsOpen)} Sektoren gesperrt; "
        + $"Zoll {tolls:0} Cr; Reisen umgeplant {rerouted}, angehalten/nicht angetreten {blocked}; Besitzerwechsel {ownerChanges}");
    Console.WriteLine($"Kassen-Unterhalt: Regierungsstationen {govUpkeep:0} Cr (ungedeckt {govUnpaid:0}), Sperren {lockUpkeep:0} Cr — "
        + $"{(taxIn > 0 ? 100.0 * (govUpkeep + lockUpkeep) / taxIn : 0):0}% der Abgaben in Kassen");
    foreach (var sec in u.Sectors.Values.OrderBy(x => x.Id.Value))
    {
        string inf = string.Join(" ", sec.Influence.OrderByDescending(kv => kv.Value).Select(kv => $"{(u.Factions.TryGetValue(kv.Key, out var f) ? f.Name : "?")} {kv.Value:P0}"));
        Console.WriteLine($"  {sec.Name,-14} {(u.Factions.TryGetValue(sec.Owner, out var of) ? of.Name : "?"),-20} {AccessNames.Stage(sec.Control),-10}{(sec.Contested ? " umkämpft" : "")}  "
            + $"Sperre {sec.Access.EntryLevel}/{sec.Access.ExitLevel}{(sec.Access.Enforced ? "" : " (Papier)")}  Einfluss {inf}");
    }
    var fs = u.NpcFactionIds().ToList();
    Console.WriteLine("Beziehungen: " + string.Join(", ", fs.SelectMany(a => fs.Where(b => b != a).Select(b => $"{u.Factions[a].Name}→{u.Factions[b].Name} {Relations.Effective(u, a, b):0.00}"))));
    foreach (var fsplit in MapConnectivity.Factions(u).Where(x => x.IsSplit))
        Console.WriteLine($"  Für {fsplit.Profile} gesperrt: {string.Join(", ", fsplit.Isolated.Select(id => u.Sectors[id].Name))} (Politik)");
}
// Phase 11: Lagekarte, Polizei, Piraten — Verlustquote, Polizeianteil am Haushalt, Rechenzeit der Scans
{
    double income = taxIn + tolls + fines;
    Console.WriteLine($"Sicherheit: {raids} Überfälle ({raidsWon} erfolgreich, {raids - raidsWon} abgewehrt, Polizei griff {policeHelped}× ein, {raidsOnPlayer} auf den Spieler), "
        + $"{warnings} Vorwarnungen; Abfangen {intercepts} ({interceptsWon} von der Polizei gewonnen); Kaperschiffe verloren {raidersLost}, neu {raidersBuilt}; Opferschiffe verloren {victimsLost}");
    Console.WriteLine($"Verlustquote: Beute {lootValue:0} Cr (Basiswert) = {(tradeValue > 0 ? 100.0 * lootValue / tradeValue : 0):0.00}% des Handelsumsatzes ({tradeValue:0} Cr); "
        + $"Hehlerei {fenced:0} Cr; Schiffsverluste {victimsLost}");
    // Treibstoff im Großmaßstab: Gewicht des Treibstoffs (Kosten ÷ Handelsumsatz, Einheiten je Tick) und Reichweite eines Spielertanks
    if (u.Rules.FuelActive)
    {
        int refineries = u.Stations.Values.Count(st => st.TypeKey == "fuel_refinery" && st.Site is null);
        Console.WriteLine($"Treibstoffgewicht: Kosten {fuelBuyCost + notkCost:0} Cr = {(tradeValue > 0 ? 100.0 * (fuelBuyCost + notkCost) / tradeValue : 0):0.0}% des Handelsumsatzes; "
            + $"getankt {fuelBuyUnits:0} + Notkauf {notkUnits:0} Einheiten = {(fuelBuyUnits + notkUnits) / Math.Max(1, ticks):0.000} je Tick; Raffinerien {refineries}");
        Console.WriteLine("Treibstoff Spieler: " + FuelRange.Describe(u));
    }
    Console.WriteLine($"Polizei: {u.NpcFactionIds().Select(f => $"{u.Factions[f].Name} {SecuritySystem.PoliceOf(u, f).Count}/{SecuritySystem.Desired(u, f)}").Aggregate((a, b) => a + ", " + b)} (Ist/Soll); "
        + $"gekauft {policeBought}, abgebaut {policeGone}; Kosten {policeCost:0} Cr (ungedeckt {policeUnpaid:0}) = {(income > 0 ? 100.0 * policeCost / income : 0):0.0}% der Kasseneinnahmen "
        + $"(Abgaben {taxIn:0} + Zoll {tolls:0} + Strafen {fines:0}); Kopfgeld ausgezahlt {bounties:0}");
    foreach (var a in u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw).OrderBy(a => a.Id.Value))
        Console.WriteLine($"  Piraten {a.Name}: {u.Ships.Values.Count(x => ThreatMap.IsRaider(x) && x.Owner == a.Id)} Kaperschiffe, Kasse {a.Treasury.Credits:0} Cr, Kosten {pirateCost:0} Cr; Fahndung "
            + string.Join(", ", a.Wanted.OrderBy(kv => kv.Key.Value).Select(kv => $"{u.Factions[kv.Key].Name} {SecurityNames.WantedLevel(Wanted.Level(u.Rules, kv.Value.Heat))}")));
    // Runde 41: Aufträge — in der NPC-Welt ohne Spielerannahme nur Angebote (nicht gehasht), Geldfluss 0
    Console.WriteLine($"Aufträge: Angebote {mOffered} je Art [{string.Join(", ", mByType.Select(kv => $"{kv.Key} {kv.Value}"))}] (aktuell {u.MissionOffers.Count}), angenommen {mAccepted}, erfüllt {mCompleted}, gescheitert {mFailed}"
        + $" — Reserviert {mReserve:0} Cr, ausgezahlt {mPaid:0} Cr, zurück {mReturn:0} Cr, Pfand verfallen {mDepositForfeit:0} Cr; laufend {u.Missions.Count(m => m.State == SpaceSim.Core.Model.MissionState.Active)}");
    Console.WriteLine("Lagekarte: " + string.Join(", ", u.Sectors.Values.OrderBy(x => x.Id.Value)
        .Select(x => $"{x.Name} Bedrohung {x.Threat:0.00} Sicherheit {x.Security:0.0} Verkehr {x.Traffic:0}")) + $"; Verbrechen gemeldet {crimes}");
    // Phase 24: Rundengefecht
    Console.WriteLine($"Gefechte: {fights} ({fightRaids} Überfälle, {fightIntercepts} Abfangen), laufend jetzt {u.Fights.Count} (max gleichzeitig {fightsMax}); "
        + $"Dauer Ø {(fights > 0 ? (double)fightTicks / Math.Max(1, fightOutcomes.Values.Sum()) : 0):0} Ticks; Schüsse {fightShots}, Treffer {fightHits} "
        + $"({(fightShots > 0 ? 100.0 * fightHits / fightShots : 0):0}%); Schaden Schild {shieldDmg:0}, Hülle {hullDmg:0}");
    Console.WriteLine("  Ausgänge: " + (fightOutcomes.Count == 0 ? "keine" : string.Join(", ", fightOutcomes.Select(kv => $"{kv.Key} {kv.Value}")))
        + " | je Rolle: " + (exitsByRole.Count == 0 ? "keine" : string.Join(", ", exitsByRole.Select(kv => $"{kv.Key} {kv.Value}"))));
    Console.WriteLine($"Flottenkampf: {(u.Rules.FleetCombat ? "an" : "aus")}; Streifen-Verbände gebildet {squadsFormed}, Rudel {packsFormed}, jetzt Verbände {u.Squadrons.Count}; "
        + $"Verstärkung {(joinedBy.Count == 0 ? "keine" : string.Join(", ", joinedBy.Select(kv => $"{kv.Key} {kv.Value}")))}; "
        + $"Zuflucht {(refugeBy.Count == 0 ? "keine" : string.Join(", ", refugeBy.Select(kv => $"{kv.Key} {kv.Value}")))}; "
        + $"Verfolgungen {pursuits} ({(pursuitEnds.Count == 0 ? "–" : string.Join(", ", pursuitEnds.Select(kv => $"{kv.Key} {kv.Value}")))}); Geleit {escortsAssigned}; Polizeigefechte gegen den Spieler {arrests}");
    Console.WriteLine("Piraten über die Zeit (je 20000 Ticks: Überfälle, je Gruppe Kaperschiffe/Kasse): " + (pirateTimeline.Count == 0 ? "–" : string.Join(" | ", pirateTimeline)));
    // Phase 28 (Prüfpunkt e): Piratenkassen nach Zweck — Einnahmen gegen Kosten
    Console.WriteLine($"Piratenkassen: Einnahmen {pirateIn.Values.Sum():0} Cr ({(pirateIn.Count == 0 ? "–" : string.Join(", ", pirateIn.Select(kv => $"{kv.Key} {kv.Value:0}")))}), "
        + $"Kosten {pirateOut.Values.Sum():0} Cr ({(pirateOut.Count == 0 ? "–" : string.Join(", ", pirateOut.Select(kv => $"{kv.Key} {kv.Value:0}")))})");
    // Schwarzmarkt: Hehlerware je Versteck (Beute der Kaperer; nur Ablage, ändert kein NPC-Verhalten)
    Console.WriteLine($"Schwarzmarkt ({(u.Rules.BlackMarket ? "an" : "aus")}): " + string.Join(" | ", u.StationsById().Where(s => SpaceSim.Core.Trading.BlackMarket.IsAt(u, s))
        .Select(s => $"{s.Name}: {s.BlackStock.Count} Waren, {s.BlackStock.Values.Sum():0} E. (Basiswert {s.BlackStock.Sum(kv => kv.Value * u.GoodOf(kv.Key).BasePrice):0} Cr)")
        .DefaultIfEmpty("kein Versteck mit Schwarzmarkt")));
    // Phase 28: Raketen, Stationen, Krieg
    Console.WriteLine($"Kriegsführung (Phase 28: {(u.Rules.Warfare ? "an" : "aus")}): Raketen {missilesLaunched} ({(missileOutcomes.Count == 0 ? "–" : string.Join(", ", missileOutcomes.Select(kv => $"{kv.Key} {kv.Value}")))}); "
        + $"Stationsgeschütze {stationShots} Schüsse ({stationHits} Treffer, Schaden {stationGunDmg:0}); Schüsse auf Stationen {shotsAtStations}, Hüllenschaden an Stationen {stationHullDmg:0}; "
        + $"Belagerungen {sieges}; Stationen gefallen {(stationFalls.Count == 0 ? "keine" : string.Join(", ", stationFalls.Select(kv => $"{kv.Key} {kv.Value}")))}; "
        + $"beschädigt jetzt {u.Stations.Values.Count(x => x.Condition is not null)}; Reparaturen {stationRepairs} ({stationRepairHull:0} Hülle, {stationRepairPlates:0.#} Hüllenplatten, {stationRepairCost:0} Cr)");
    {
        // Phase 30: Kleinstationen
        var small = u.Stations.Values.Where(x => !x.IsBuildSite && Outposts.IsOutpost(u, x)).ToList();
        float smallUp = small.Sum(x => u.StationTypes[x.TypeKey].UpkeepPerTick);
        float offUp = u.Stations.Values.Where(x => !x.IsBuildSite && StationProtection.IsOfficial(u, x)).Sum(x => u.StationTypes[x.TypeKey].UpkeepPerTick);
        Console.WriteLine($"Kleinstationen: jetzt {small.Count} ({(small.Count == 0 ? "–" : string.Join(", ", small.GroupBy(x => x.TypeKey).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}")))}; "
            + $"Besitzer {(small.Count == 0 ? "–" : string.Join(", ", small.GroupBy(x => u.ActorName(x.Owner)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}")))}); "
            + $"gebaut {(outpostsBuilt.Count == 0 ? "keine" : string.Join(", ", outpostsBuilt.Select(kv => $"{kv.Key} {kv.Value}")))} für {outpostCost:0} Cr "
            + $"({(income > 0 ? 100.0 * outpostCost / income : 0):0.00}% der Kasseneinnahmen), abgebaut (ruhig) {outpostsCalm}; Abwehrgefechte {outpostFights}, Schüsse {outpostShots}; "
            + $"Unterhalt {smallUp:0.00} Cr/Tick = {(offUp > 0 ? 100.0 * smallUp / offUp : 0):0.0}% des Unterhalts der Amtsstationen");
    }
    // Phase 32: Erzfluss und Anker
    Console.WriteLine($"Erzabbau (Phase 32, Anker {(u.Rules.MiningAnchors ? "an" : "aus")}): Kontakt {oreContact:0}, Laser {oreLaser:0}, abgeliefert {oreDelivered:0} Einheiten; "
        + $"Anker geschossen {anchorsFired}, gehalten {anchorsHeld}, gelöst {(anchorReleases.Count == 0 ? "–" : string.Join(", ", anchorReleases.Select(kv => $"{kv.Key} {kv.Value}")))}");
    // Phase 31: Torkampf
    Console.WriteLine($"Torkampf (Phase 31: {(u.Rules.GateCombat ? "an" : "aus")}): Angriffe {gatesAttacked}, Schüsse auf Tore {gateShots}; außer Betrieb {gatesDisabled} (kartenteilend {gatesDisabledSplit}), "
        + $"gleichzeitig max {gateDownMax}, jetzt {GateCombat.DownCount(u)}; wieder in Betrieb {(gateRestored.Count == 0 ? "–" : string.Join(", ", gateRestored.Select(kv => $"{(kv.Key == "emergency" ? "Notregel" : "repariert")} {kv.Value}")))}; "
        + $"Ausfalldauer Ø {(gateOutages.Count == 0 ? 0 : gateOutages.Average()):0} / max {(gateOutages.Count == 0 ? 0 : gateOutages.Max())} Ticks; Reparatur {gateRepairCost:0} Cr; Händlerpleiten während eines Ausfalls {tradersBrokeWhileDown}; "
        + $"Blockaden {u.Wars.Sum(w => w.Blockades)}");
    Console.WriteLine($"Krieg: erklärt {warsDeclared}, Frieden {(peaceReasons.Count == 0 ? "–" : string.Join(", ", peaceReasons.Select(kv => $"{kv.Key} {kv.Value}")))}, laufend {u.Wars.Count}"
        + (u.Wars.Count == 0 ? "" : " (" + string.Join(", ", u.Wars.Select(w => $"{u.Factions[w.A].Name}–{u.Factions[w.B].Name} Müdigkeit {w.Weariness:0.00}")) + ")")
        + $"; Kriegsschiffe gekauft {warshipsBought}, abgebaut {warshipsGone}, jetzt {u.Ships.Values.Count(Wars.IsWarship)}, Unterhalt {navyCost:0} Cr; "
        + "Beziehungen " + string.Join(", ", u.NpcFactionIds().SelectMany(a => u.NpcFactionIds().Where(b => b.Value > a.Value)
            .Select(b => $"{u.Factions[a].Name}–{u.Factions[b].Name} {Relations.Effective(u, a, b):0.00}/{Relations.Effective(u, b, a):0.00}"))));
    if (warLog.Count > 0) Console.WriteLine("Kriegserklärungen: " + string.Join("; ", warLog));
    Console.WriteLine($"Bündnisse (Runde 45, {(u.Rules.AlliancesActive ? "an" : "aus")}): jetzt {u.Alliances.Count}"
        + (u.Alliances.Count > 0 ? " (" + string.Join(", ", u.Alliances.Select(al => $"{al.A.Value}–{al.B.Value} seit {al.Since}")) + ")" : "")
        + $"; Tributfrieden {tributePeaces} für {tributeMoney:0} Cr (Tributfrieden KI {(u.Rules.AiTributePeace ? "an" : "aus")})"
        + (allianceLog.Count > 0 ? " — " + string.Join("; ", allianceLog) : ""));
    Console.WriteLine($"Kriegserklärungen gegen den Spieler (AiWarOnPlayer {(u.Rules.AiWarOnPlayer ? "an" : "aus")}): {playerWarLog.Count}"
        + (playerWarLog.Count > 0 ? " — " + string.Join("; ", playerWarLog) : ""));
    Console.WriteLine($"Munition: {(u.Rules.AmmoActive ? "an" : "aus")}; verbraucht {ammoUsed:0}, nachgeladen {ammoUnits:0} Einheiten für {ammoCost:0} Cr (aus Kassen {ammoTreasury:0}, davon Polizei {ammoPolice:0} = "
        + $"{(income > 0 ? 100.0 * ammoPolice / income : 0):0.00}% der Kasseneinnahmen); fehlt jetzt {u.Ships.Values.Sum(x => x.Systems.AmmoMissing):0}");
    {
        var armedNow = u.Ships.Values.Where(x => x.IsEconomyAi && x.Stats.WeaponPower > 0f).ToList();
        Console.WriteLine($"Bewaffnete Händler/Schürfer: jetzt {armedNow.Count} ({string.Join(", ", armedNow.GroupBy(x => x.ModelKey).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}"))}); "
            + $"bewaffnete Modelle gekauft {armedBought}");
    }
    // Phase 26: Schaden und Bergung
    Console.WriteLine($"Schaden (Phase 26: {(u.Rules.DamageActive ? "an" : "aus")}): Modulschäden {modDamaged} (ausgefallen {modFailed}); Brände {firesStarted} ("
        + (fireEnds.Count == 0 ? "keine beendet" : string.Join(", ", fireEnds.Select(kv => $"{kv.Key switch { "extinguished" => "gelöscht", "burnt_out" => "ausgebrannt", "docked" => "angedockt", "repaired" => "repariert", _ => kv.Key }} {kv.Value}")))
        + $"), brennen jetzt {u.Ships.Values.Sum(x => x.Systems.Burning.Count)}; beschädigte Module jetzt {u.Ships.Values.Sum(x => x.Systems.Condition.Count(c => c < 0.999f))}");
    Console.WriteLine($"Reparaturen: Dienst {repairsNpc} NPC + {repairsPlayer} Spieler für {repairCost:0} Cr (Hüllenplatten {repairPlates:0.#}, Elektronik {repairElec:0.#}); an der Basis aus Kassen {repairsBase} für {repairBaseCost:0} Cr; Module instand gesetzt {repairModules}; "
        + $"Hülle fehlt jetzt (KI) {u.Ships.Values.Where(x => !x.IsPlayer).Sum(x => MathF.Max(0f, x.MaxHull - x.Hull)):0}");
    Console.WriteLine($"Hüllenzustand KI (Händler/Schürfer, Probe ab 10 %): mittlere Hüllenquote {(hullFracN > 0 ? hullFracSum / hullFracN : 1):0.000}, schwer beschädigt {(hullFracN > 0 ? 100.0 * hullSerious / hullFracN : 0):0.0} %, Reparaturflüge {repairTrips}");
    foreach (var line in probeOut) Console.WriteLine("SONDE " + line);
    Console.WriteLine($"Händler-Einkäufe verweigert: {buysRefused}" + (refusedBy.Count > 0 ? " (" + string.Join(", ", refusedBy.Select(kv => $"{kv.Key} {kv.Value}")) + ")" : ""));
    Console.WriteLine($"Händlerpleiten und Reparatur: {brokeTraders} pleite, davon {brokeWithRepair} hatten repariert (zusammen {brokeRepairSum:0} Cr), {brokeDamaged} mit Hüllenschaden, {brokeFailed} mit ausgefallenem Modul");
    Console.WriteLine($"Wracks: {wrecksMade} entstanden + {containersMade} Frachtbehälter (Fracht {wreckCargoMade:0.#}, Module {wreckModulesMade}); geborgen Fracht {salvagedCargoNpc:0.#} (Kaperer) + {salvagedCargoPlayer:0.#} (Spieler), Module {salvagedModules}; "
        + $"verfallen {wrecksExpired} (Fracht {wreckCargoExpired:0.#}, Module {wreckModulesExpired}); jetzt {u.Wrecks.Count}");
    Console.WriteLine($"Rechenzeit Sicherheit (Lagekarte, Piraten, Polizei, Fahndung): {securityWatch.Elapsed.TotalMilliseconds:0} ms = "
        + $"{(sw.ElapsedMilliseconds > 0 ? 100.0 * securityWatch.Elapsed.TotalMilliseconds / sw.ElapsedMilliseconds : 0):0.0}% der Laufzeit");
}
// Phase 13: Forschungsstand je Fraktion, Freischaltungen, Ausgaben (Phase 14: nur wenn die Forschung an ist)
if (u.Rules.ResearchActive)
{
    Console.WriteLine($"Forschung{(u.Rules.ResearchActive ? "" : " (abgeschaltet)")}: {researchCycles} Takte, {researchPoints:0} FP; gestockt {researchStalled}"
        + (stallReasons.Count == 0 ? "" : " (" + string.Join(", ", stallReasons.Select(kv => $"{kv.Key} {kv.Value}")) + ")")
        + $"; Ausgaben: Budget aus Kassen {researchFunding:0} Cr, Personal {researchSalary:0} Cr (Senke), Güter {researchGoods:0} Cr (Basiswert), Spieler {researchPlayer:0} Cr");
    Console.WriteLine("Freischaltungen: " + (researchDone.Count == 0 ? "keine" : string.Join("; ", researchDone)));
    foreach (var f in u.NpcFactionIds())
        if (ResearchInfo.ForFaction(u, f) is { } rv)
            Console.WriteLine($"  {rv.Name,-20} {rv.Summary}; {rv.PointsTotal:0} FP gesamt, Personal {rv.Spent:0} Cr"
                + string.Concat(rv.Stations.Select(st => $"; {st.Station.Name}: {(st.Stalled ?? "läuft")}, Kasse {st.Credits:0}")));
    double incomeR = taxIn + tolls + fines;
    if (incomeR > 0) Console.WriteLine($"  Forschungsbudget = {100.0 * researchFunding / incomeR:0.0}% der Kasseneinnahmen");
}
// Phase 14: Exportsenke, Endwaren-Fluss, Preise an den Verbrauchern
{
    var exporters = u.Stations.Values.Where(x => ExportRules.ConfigOf(u, x) is not null).OrderBy(x => x.Id.Value).ToList();
    if (exporters.Count > 0)
    {
        Console.WriteLine($"Export: {exportBatches} Chargen an Planeten ({exportQueued} angekauft), bezahlt {exportPaid:0} Cr, Erlös {exportRev:0} Cr, neu geschöpft {exportRev - exportPaid:0} Cr; "
            + (exportUnits.Count == 0 ? "nichts exportiert" : string.Join(", ", exportUnits.Select(kv => $"{u.Goods[new GoodId(kv.Key)].Name} {kv.Value:0} ({exportRevenue[kv.Key]:0} Cr)"))));
        foreach (var ex in exporters)
            Console.WriteLine($"  {ex.Name,-22} Kasse {ex.Credits,8:0} (min {exportCashMin.GetValueOrDefault(ex.Id.Value):0}), {ex.Exports.Count} Chargen unterwegs ({ex.Exports.Sum(b => b.Paid):0} Cr bezahlt); Lager "
                + string.Join(", ", ExportRules.ConfigOf(u, ex)!.Goods.Select(g => $"{u.Goods[g].Name} {ex.Market.GetValueOrDefault(g)?.Amount ?? 0:0} @ {(ex.Market.GetValueOrDefault(g)?.Price ?? 0) / u.Goods[g].BasePrice:0.00}×")));
    }
    var endGoods = u.Goods.Values.Where(g => g.Category == "end").OrderBy(g => g.Id.Value).ToList();
    if (endGoods.Count > 0)
        Console.WriteLine("Endwaren-Fahrten: " + string.Join(", ", endGoods.Select(g => { var (vol, trips) = routes.ByGood(g.Id); return $"{g.Name} {trips} Fahrten ({vol:0} Einh.)"; })));
    foreach (var (k, list) in consumerPrices)
        if (list.Count > 0)
        {
            var tl = list.Skip(list.Count * 3 / 4).ToList();
            Console.WriteLine($"  Verbraucherpreis {k}: Ø {list.Average():0.00}×, letzte 25% {tl.Average():0.00}×, am Deckel (≥3,9×) {list.Count(x => x >= 3.9f)}/{list.Count} Messpunkte");
        }
    // Erzeuger und Verbraucher der Endwaren zum Schluss: Bestand/Wunsch, Preis, Kasse
    foreach (var st in u.Stations.Values.Where(x => !x.IsBuildSite && ExportRules.ConfigOf(u, x) is null && x.Production.Any(r => r.Outputs.Keys.Concat(r.Inputs.Keys).Any(g => u.Goods[g].Category == "end"))).OrderBy(x => x.Id.Value))
        Console.WriteLine($"  {st.Name,-24} Kasse {st.Credits,8:0}; " + string.Join(", ", st.Market.Where(kv => st.Production.Any(r => r.Inputs.ContainsKey(kv.Key) || r.Outputs.ContainsKey(kv.Key))).OrderBy(kv => kv.Key.Value)
            .Select(kv => $"{u.Goods[kv.Key].Name} {kv.Value.Amount:0}/{kv.Value.Target:0} @ {kv.Value.Price / u.Goods[kv.Key].BasePrice:0.00}×")));
}
Console.WriteLine("Über die Annahme abgeladen (Händler, letzter Ausweg): " + (dumped.Count == 0 ? "nichts" : string.Join(", ", dumped.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value:0}"))));
Console.WriteLine($"Im All abgeworfen (KI-Händler, Rest ohne Käufer): {discardCount}×" + (discarded.Count == 0 ? "" : " — " + string.Join(", ", discarded.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value:0}"))));
Console.WriteLine("Top-Handelsrouten:");
foreach (var r in routes.Top(5))
{
    string from = r.Route.From.Value < 0 ? $"Miner {-r.Route.From.Value}" : u.Stations.GetValueOrDefault(r.Route.From)?.Name ?? $"#{r.Route.From.Value}";
    string to = u.Stations.GetValueOrDefault(r.Route.To)?.Name ?? $"#{r.Route.To.Value}";
    Console.WriteLine($"  {u.Goods[r.Route.Good].Name,-9} {from} → {to}: {r.Volume:0} in {r.Trips} Fahrten");
}

// --dump on: alle Stationen mit Kasse und Markt zum Schluss (Fehlersuche)
if (opts.TryGetValue("dump", out var dumpOpt) && dumpOpt == "on")
    foreach (var st in u.Stations.Values.OrderBy(x => x.Id.Value))
        Console.WriteLine($"  #{st.Id.Value,-4} {st.Name,-30} Kasse {st.Credits,9:0}: " + string.Join(", ", st.Market.OrderBy(kv => kv.Key.Value)
            .Select(kv => $"{u.Goods[kv.Key].Key} {kv.Value.Amount:0}/{kv.Value.Target:0}@{kv.Value.Price / MathF.Max(0.01f, u.Goods[kv.Key].BasePrice):0.00}")));

// Phase 15: HINWEIS-Block — Dauerzustände, die der Durchschnitt wegmittelt (Deckel je Station und Ware, gehandelte Waren ohne Fahrten, leere Kassen)
// Phase 17: Exportsenken mit leerem Lager am Gebotsdeckel sind unbefriedigte Exportnachfrage (kein Fehler) — eigene Rubrik, zählt nicht als Hinweis
var allCapHints = watch.CapHints(u);
var exportDemand = allCapHints.Where(h => MarketWatch.IsExportDemand(u, h)).ToList();
var capHints = allCapHints.Where(h => !MarketWatch.IsExportDemand(u, h)).ToList();
var idleGoods = watch.IdleGoods(u);
var cashHints = watch.CashHints(u);
Console.WriteLine($"Deckel/Kassen-Messung: {watch.Samples} Messpunkte ab Tick {watchFrom} (alle {WatchEvery} Ticks)");
foreach (var key in new[] { "reactor_mk1", "chem_engine_s" })
    if (u.FindGood(key) is { } mg)
        foreach (var yard in u.Stations.Values.Where(x => !x.IsBuildSite && u.StationTypes.TryGetValue(x.TypeKey, out var yt) && yt.ShipDealer is not null && x.Market.ContainsKey(mg.Id)).OrderBy(x => x.Id.Value))
        {
            var (up, low) = watch.Share(yard.Id, mg.Id);
            Console.WriteLine($"  {yard.Name}: {mg.Name} {100 * up:0}% am oberen Deckel, {100 * low:0}% am unteren");
        }
// Phase 17: Anteil der Messzeit, in dem jede Exportsenke je Exportware leer am Gebotsdeckel stand (Ziel: unter 50 %)
foreach (var ex in u.Stations.Values.Where(x => ExportRules.ConfigOf(u, x) is not null).OrderBy(x => x.Id.Value))
    Console.WriteLine($"  Exportsenke {ex.Name}: leer am Gebotsdeckel " + string.Join(", ", ExportRules.ConfigOf(u, ex)!.Goods.Select(g => $"{u.Goods[g].Name} {100 * watch.Share(ex.Id, g).Upper:0}%"))
        + " | Gebot " + string.Join(", ", ExportRules.ConfigOf(u, ex)!.Goods.Select(g => $"{u.Goods[g].Name} {ExportRules.ConfigOf(u, ex)!.BidOf(g, u.Rules):0.00}×")));
foreach (var h in exportDemand)
    Console.WriteLine($"EXPORTNACHFRAGE: {u.Stations[h.Station].Name}: {u.Goods[h.Good].Name} {100 * h.UpperShare:0}% der Messzeit leer am Gebotsdeckel — unbefriedigte Exportnachfrage, kein Fehler (Ø {h.AvgRatio:0.00}×)");
foreach (var h in capHints)
    Console.WriteLine($"HINWEIS: {u.Stations[h.Station].Name}: {u.Goods[h.Good].Name} {(h.UpperShare >= h.LowerShare ? $"{100 * h.UpperShare:0}% der Messzeit am oberen Deckel (Bestand leer)" : $"{100 * h.LowerShare:0}% der Messzeit am unteren Deckel")} (Ø {h.AvgRatio:0.00}×)");
foreach (var g in idleGoods)
    Console.WriteLine($"HINWEIS: {u.Goods[g].Name}: 0 Fahrten (Hersteller und Abnehmer vorhanden)");
foreach (var h in cashHints)
    Console.WriteLine($"HINWEIS: Kasse {u.Stations[h.Station].Name}: {100 * h.EmptyShare:0}% der Messzeit praktisch leer (< {MarketWatch.EmptyCash:0} Cr; min {h.MinCredits:0}, Ende {h.EndCredits:0})");
int hintCount = capHints.Count + idleGoods.Count + cashHints.Count;
Console.WriteLine(hintCount == 0 ? "Hinweise: keine." : $"Hinweise: {hintCount} ({capHints.Count} Deckel, {idleGoods.Count} Waren ohne Fahrten, {cashHints.Count} leere Kassen)");
bool strict = opts.TryGetValue("strict", out var strictOpt) && strictOpt == "on";

// Urteil: Preise im letzten Viertel der Laufzeit dauerhaft am Anschlag deuten auf eine kippende Kette hin.
// Phase 16: Der Durchschnitt kommt aus der dichten Probe von MarketWatch (alle 20 Ticks ab 3/4 der Laufzeit), nicht aus den --every-Messpunkten —
// vorher war Seed 2 mit --every 10000 rot (3,21×, zwei Messpunkte) und mit --every 5000 grün. Gleicher Lauf → gleiches Urteil.
long verdictFrom = ticks * 3L / 4;
Console.WriteLine($"Urteilsprobe: {watch.SamplesSince(verdictFrom)} Messpunkte ab Tick {verdictFrom} (alle {WatchEvery} Ticks, unabhängig von --every)");
bool bad = false;
foreach (var g in u.Goods.Values)
{
    // Waren ohne Markt (noch ohne Erzeuger, z. B. propellant bis Phase 4b) haben keinen Preis und keine Stabilität
    if (!u.Stations.Values.Any(st => st.Market.ContainsKey(g.Id))) continue;
    if (watch.AverageRatio(g.Id, verdictFrom) is not { } avgRatio) continue;
    if (avgRatio > 3.0f || avgRatio < 0.3f) { Console.WriteLine($"WARNUNG: {g.Name} im Schnitt bei {avgRatio:0.00}× Basispreis (instabil)"); bad = true; }
}
// Phase 16: Spanne der Urteilswerte (knappste und billigste Ware) — macht sichtbar, wie nah ein grüner Lauf an den Grenzen 3,0/0,3 liegt
{
    var judged = u.Goods.Values.Where(g => u.Stations.Values.Any(st => st.Market.ContainsKey(g.Id)))
        .Select(g => (g.Name, R: watch.AverageRatio(g.Id, verdictFrom))).Where(x => x.R is not null).Select(x => (x.Name, R: x.R!.Value)).ToList();
    if (judged.Count > 0)
    {
        var hi = judged.OrderByDescending(x => x.R).Take(3).Select(x => $"{x.Name} {x.R:0.00}×");
        var lo = judged.OrderBy(x => x.R).Take(3).Select(x => $"{x.Name} {x.R:0.00}×");
        Console.WriteLine($"Urteilswerte: höchste {string.Join(", ", hi)}; niedrigste {string.Join(", ", lo)}");
    }
}
if (!bad) Console.WriteLine("Urteil: Preise im Zielbereich.");
if (tradeProbe is not null) Console.WriteLine(tradeProbe.Report(seed, ticks));
if (cashProbe is not null) Console.WriteLine(cashProbe.Report());
if (hintProbe is not null) Console.WriteLine(hintProbe.Report(u, opts.GetValueOrDefault("hintcsv")));
// Phase 10: zerfallende Karte (physisch oder für die freien Händler) ist ein eigener Befund mit eigenem Exit-Code
bool splitBad = worstSplit is not null;
if (worstSplit is not null)
    Console.WriteLine($"WARNUNG: Karte zerfallen ({worstSplit.Profile}): isolierte Sektoren {string.Join(", ", worstSplit.Isolated.Select(id => u.Sectors[id].Name))} "
        + $"(größter Block {worstSplit.Largest}/{worstSplit.Sectors}){(finalSplit is null ? ", am Ende wieder verbunden" : ", am Ende noch zerfallen")}");
else Console.WriteLine("Karte: zusammenhängend (physisch und für freie Händler, an allen Messpunkten).");

if (opts.TryGetValue("csv", out var csvPath)) { File.WriteAllText(csvPath, csv.ToString()); Console.WriteLine("CSV: " + csvPath); }
return bad ? 1 : splitBad ? 2 : strict && hintCount > 0 ? 3 : 0;

static Dictionary<string, string> ParseArgs(string[] a)
{
    var d = new Dictionary<string, string>();
    for (int i = 0; i + 1 < a.Length; i += 2) d[a[i].TrimStart('-')] = a[i + 1];
    return d;
}
static int Int(Dictionary<string, string> d, string k, int def) => d.TryGetValue(k, out var v) ? int.Parse(v) : def;

/// <summary>
/// Hüllt die Systeme eines Moduls in Zeitmessungen: eine eigene Uhr je System (Phase 17: Rechenzeit je System) und optional eine gemeinsame
/// Sammeluhr (Phase 11: Rechenzeit der Sicherheits-Scans). Reihenfolge und Intervalle bleiben unverändert.
/// </summary>
sealed class TimedModule(ISimModule inner, System.Diagnostics.Stopwatch? group, List<(string Name, System.Diagnostics.Stopwatch Watch)>? perSystem) : ISimModule
{
    public string Name => inner.Name;
    public void Register(SimulationBuilder b)
    {
        var tmp = new SimulationBuilder();
        inner.Register(tmp);
        foreach (var (system, interval) in tmp.Registered)
        {
            System.Diagnostics.Stopwatch? own = null;
            if (perSystem is not null) { own = new System.Diagnostics.Stopwatch(); perSystem.Add((system.GetType().Name, own)); }
            b.Add(new Timed(system, own, group), interval);
        }
    }

    sealed class Timed(ISimSystem system, System.Diagnostics.Stopwatch? own, System.Diagnostics.Stopwatch? group) : ISimSystem
    {
        public void Step(Universe u, int dt)
        {
            own?.Start(); group?.Start();
            system.Step(u, dt);
            group?.Stop(); own?.Stop();
        }
    }
}
