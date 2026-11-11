using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Tests;

/// <summary>
/// Phase 11: Lagekarte (<see cref="ThreatMap"/>), Polizei (<see cref="SecuritySystem"/>), Piraten (<see cref="PirateSystem"/>), abstrakte Begegnungen
/// (<see cref="IEncounterResolver"/>), Fahndung (<see cref="Wanted"/>), Gericht und Kopfgeldbüro (<see cref="CourtService"/>, <see cref="BountyService"/>),
/// Risikoabschlag der Händler/Miner, Save v14 mit Migration.
/// </summary>
public class SecurityTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    static readonly FactionId Union = new(1), Guild = new(2);
    static readonly SectorId Alpha = new(1), Beta = new(2), Gamma = new(3), Delta = new(4);

    internal static readonly string[] Phase11Rules =
    {
        "SecurityActive", "PlayerRaids", "ThreatDecayPerTick", "PatrolSuppressionPerTick", "RaidThreat", "RepelledThreat", "SmugglingThreat", "SecuritySmoothing",
        "PoliceStrengthUnit", "CoverageSecurity", "RiskPerThreat", "MaxSectorRisk", "PoliceModel", "PolicePerSector", "PolicePerThreat", "PoliceMinPerFaction",
        "PoliceMaxPerFaction", "PoliceUpkeepPerTick", "PoliceReserve", "PoliceChangeCooldown", "PoliceDecommissionTicks", "PatrolDwellTicks", "RetreatHull",
        "BaseRepairPerHull", "InterceptRatePerTick", "PoliceResponseChance", "PoliceResponseMax", "PirateModel", "MaxRaidersPerActor", "RaiderCost", "RaiderRebuildTicks",
        "RaiderUpkeepPerTick", "RaidRatePerTick", "RaidSecurityDeterrence", "PirateLurkTicks", "PirateRestTicks", "MinLootValue", "FenceShare",
        "PirateSecurityAversion", "PlayerWarningTicks", "PlayerRaidWeight", "StationSafeRadius", "PlayerMinHull", "EncounterExponent", "LootMin", "LootMax",
        "LoserDamage", "WinnerDamage", "DestroyChance", "RaiderDestroyChance", "NpcHullRegenPerTick", "CrimeHeat", "CrimeFine", "WantedLevels", "WantedMaxHeat",
        "WantedDecayPerTick", "WantedMinTicks", "FinePerHeat", "ExtraditionRelation", "BountyPerLevel",
    };

    internal static readonly string[] Phase11Modules = { "weapon_laser_s" };
    internal static readonly string[] Phase11Classes = { "patrol_craft", "raider" };
    internal static readonly string[] Phase11Models = { "warden", "corsair" };

    /// <summary>
    /// Demotet einen aktuellen Save (v14) auf v13: Polizei- und Kaperschiffe, gesetzlose Akteure samt Versteck (Typ und Stationen), Fahndung, Kopfgeld,
    /// Streitkräfte-Takt, angekündigte Begegnungen, Heimatbasis/Einsatz, Grundschutz und Versteck-Kennung der Typen, Dienste „court“/„bounty“,
    /// <c>restricted</c>, die Klassen/Modelle von Polizei und Piraten und die Phase-11-Regeln (außer dem Schalter <c>SecurityActive</c>) entfallen.
    /// Die Laserkanone (Modul, Ware, Rezept, Lager) und die Werte der Lagekarte bleiben stehen, damit die übrigen Migrationstests hash-gleich zur frischen
    /// Welt laden (die Migration ergänzt nur, was fehlt). Mit <paramref name="full"/> entfallen auch sie — ein echter v13-Stand.
    /// Ältere Stände bleiben unverändert.
    /// </summary>
    internal static string ToV13Json(string json, bool full = false)
    {
        json = FleetCommandTests.ToV14Json(json);   // Phase 12: erst Befehle, Verbände, Hangar/Taxi/Leihschiff zurückbauen
        var node = JsonNode.Parse(json)!.AsObject();
        if ((int)node["Version"]! < 14) return json;
        node["Version"] = 13;
        var types = node["StationTypes"]!.AsArray();
        var hideoutTypes = new HashSet<string>();
        foreach (var t in types.ToList())
        {
            var o = t!.AsObject();
            if (o["Hideout"] is JsonNode h && (bool)h) { hideoutTypes.Add((string)o["Key"]!); types.Remove(t); continue; }
            o.Remove("Security"); o.Remove("Hideout");
            if (o["Services"] is JsonArray sv) foreach (var x in sv.ToList()) if ((string)x! is "court" or "bounty") sv.Remove(x);
        }
        var stations = node["Stations"]!.AsArray();
        var removedStations = new HashSet<int>();
        foreach (var s in stations.ToList())
            if (hideoutTypes.Contains((string)s!["Type"]!)) { removedStations.Add((int)s["Id"]!); stations.Remove(s); }
        foreach (var sec in node["Sectors"]!.AsArray())
        {
            var o = sec!.AsObject();
            var list = o["Stations"]!.AsArray();
            foreach (var x in list.ToList()) if (removedStations.Contains((int)x!)) list.Remove(x);
            if (full) { o.Remove("Threat"); o.Remove("Security"); o.Remove("Traffic"); }
        }
        var ships = node["Ships"]!.AsArray();
        foreach (var sh in ships.ToList())
        {
            if ((string)sh!["Role"]! is "Police" or "Pirate") { ships.Remove(sh); continue; }
            sh.AsObject().Remove("HomeBase"); sh.AsObject().Remove("DutySector");
        }
        var actors = node["Actors"]!.AsArray();
        bool outlawRemoved = false;
        foreach (var a in actors.ToList())
        {
            if ((string)a!["Kind"]! == "Outlaw") { actors.Remove(a); outlawRemoved = true; continue; }
            a.AsObject().Remove("Wanted"); a.AsObject().Remove("BountyClaims"); a.AsObject().Remove("LastForceTick");
        }
        int minNext = Math.Max((int)node["FreeActor"]!, (int)node["PlayerActor"]!) + 1;
        if (outlawRemoved) node["NextActorId"] = Math.Max(minNext, actors.Select(a => (int)a!["Id"]!).DefaultIfEmpty(0).Max() + 1);
        node.Remove("Encounters");
        foreach (var key in Phase11Rules) if (full || key != "SecurityActive") node["Rules"]!.AsObject().Remove(key);
        var classes = node["ShipClasses"]!.AsArray();
        foreach (var c in classes.ToList()) if (Phase11Classes.Contains((string)c!["Key"]!)) classes.Remove(c); else c.AsObject().Remove("Restricted");
        var models = node["ShipModels"]!.AsArray();
        foreach (var m in models.ToList()) if (Phase11Models.Contains((string)m!["Key"]!)) models.Remove(m);
        if (full) QualityUpgradeTests.RemoveModules(node, Phase11Modules);
        return node.ToJsonString();
    }

    /// <summary>
    /// Schaltet Polizei und Piraten in einer Welt ab und entfernt sie (Testhilfe für ältere Migrationstests, die eine laufende Welt verlustfrei demotieren):
    /// Regel <c>SecurityActive</c> aus, Polizei- und Kaperschiffe, Verstecke und gesetzlose Akteure weg (Akteur-Zähler zurück).
    /// </summary>
    internal static Universe WithoutSecurity(Universe u)
    {
        u.Rules.SecurityActive = false;
        foreach (var s in u.Ships.Values.Where(s => s.Role is ShipRole.Police or ShipRole.Pirate).ToList()) u.RemoveShip(s.Id, "test");
        foreach (var st in u.Stations.Values.Where(st => StationProtection.IsHideout(u, st)).ToList()) u.RemoveStation(st.Id, "test");
        foreach (var a in u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw).OrderByDescending(a => a.Id.Value).ToList())
        {
            u.Actors.Remove(a.Id);
            if (u.NextActorId == a.Id.Value + 1) u.NextActorId = a.Id.Value;
        }
        return u;
    }

    // ───────── Helfer ─────────

    static Universe Fresh() => DemoUniverse.Create();

    /// <summary>Kaperschiff der Startwelt, an einen Ort im Sektor gesetzt (geparkt, lauernd).</summary>
    static Ship Raider(Universe u, SectorId sector, Vec2 pos)
    {
        var r = u.Ships.Values.Where(ThreatMap.IsRaider).OrderBy(s => s.Id.Value).First();
        if (r.Transit is not null) u.FinishTransit(r);
        if (r.Sector != sector) u.MoveShip(r, sector);
        r.Position = pos;
        r.Order = new PatrolOrder(sector);
        r.DutySector = sector;
        r.NextThinkTick = u.Tick + 10_000;
        return r;
    }

    /// <summary>Händler mit Fracht, geparkt abseits der Stationen im Sektor.</summary>
    static Ship LadenTrader(Universe u, SectorId sector, Vec2 pos, float units = 80f)
    {
        var t = u.Ships.Values.Where(s => s.Role == ShipRole.Trader).OrderBy(s => s.Id.Value).First();
        if (t.Transit is not null) u.FinishTransit(t);
        if (t.Sector != sector) u.MoveShip(t, sector);
        t.Position = pos;
        t.Order = null; t.Phase = TradePhase.Idle; t.NextThinkTick = u.Tick + 10_000;
        t.Cargo.Clear();
        t.Cargo[DemoUniverse.Food] = units;
        return t;
    }

    /// <summary>Auflöser mit festem Ausgang (für Tests der Folgen).</summary>
    sealed class FixedResolver(bool attackerWins, bool destroyed = false, float loot = 0.5f, float dmg = 0.3f) : IEncounterResolver
    {
        public int Calls;
        public EncounterOutcome Resolve(Universe u, IReadOnlyList<Ship> attackers, IReadOnlyList<Ship> defenders, float destroyBase)
        {
            Calls++;
            return new EncounterOutcome(attackerWins, CombatRules.Strength(attackers), CombatRules.Strength(defenders), attackerWins ? 1f : 0f,
                attackerWins ? 0.05f : dmg, attackerWins ? dmg : 0.05f, destroyed, attackerWins ? loot : 0f);
        }
    }

    /// <summary>Ein Punkt im Sektor, weit weg von Stationen (außerhalb jedes Schutzradius) — nahe dem ersten Gate.</summary>
    static Vec2 FarFromStations(Universe u, SectorId sector)
    {
        var gate = u.Gates[u.Sectors[sector].Gates[0]].Position;
        for (int ring = 1; ring < 10; ring++)
            for (int k = 0; k < 12; k++)
            {
                var p = gate + Vec2.FromAngle(k * MathF.PI / 6f) * (20f * ring);
                if (u.Sectors[sector].Stations.All(id => Vec2.Distance(u.Stations[id].Position, p) > 60f)) return p;
            }
        throw new InvalidOperationException("kein freier Punkt");
    }

    // ───────── Content und Startwelt ─────────

    [Fact]
    public void Content_PolizeiPiratenVersteckDienste_UndStartwelt()
    {
        var u = Fresh();
        var laser = u.Modules["weapon_laser_s"];
        Assert.Contains(Capability.Combat, laser.Grants);
        Assert.NotNull(u.FindGood("weapon_laser_s"));
        // das Waffenwerk stellt die Laserkanone her (Modulfabrik-Rezept aus ihrer cost)
        Assert.Contains(u.StationTypes["weapons_works"].Recipes, r => r.Outputs.ContainsKey(u.FindGood("weapon_laser_s")!.Id));
        foreach (var key in new[] { "warden", "corsair" })
        {
            var m = u.ShipModels[key];
            var cls = u.ShipClasses[m.ClassKey];
            Assert.Null(FitRules.Check(u.SlotTypes, u.Modules, cls, cls.Slots, m.Fit));
            Assert.Empty(m.Jobs);   // keine Aufgabe des Flottenwachstums
        }
        Assert.True(u.ShipClasses["raider"].Restricted);
        Assert.False(u.ShipClasses["patrol_craft"].Restricted);
        var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
        Assert.DoesNotContain(ShipDealerService.Models(u, yard), m => m.Key == "corsair");
        Assert.Contains(ShipDealerService.Models(u, yard), m => m.Key == "warden");
        // Regierungsstationen: Gericht und Kopfgeldbüro, Grundschutz
        foreach (var t in new[] { "headquarters", "sector_hq", "police_station" })
        {
            // Phase 12: dazu Hangar (alle) und Taxi (HQ, Sektor-HQ)
            Assert.Equal(t == "police_station" ? new[] { "court", "bounty", "hangar" } : new[] { "court", "bounty", "hangar", "taxi" }, u.StationTypes[t].Services);
            Assert.True(u.StationTypes[t].Security > 0f);
        }
        // Piraten: gesetzloser Akteur ohne Fraktion, Versteck in Delta, drei Kaperschiffe dort; Versteck zählt nicht gegen die Bau-Obergrenzen
        var pirates = Assert.Single(u.Actors.Values, a => a.Kind == ActorKind.Outlaw);
        Assert.Null(pirates.Membership);
        var hideout = Assert.Single(u.Stations.Values, st => StationProtection.IsHideout(u, st));
        Assert.Equal(pirates.Id, hideout.Owner);
        Assert.Equal(Delta, hideout.Sector);
        Assert.True(hideout.BankruptcyProtected);
        Assert.False(StationProtection.IsEconomic(u, hideout));
        Assert.Empty(hideout.Market);
        var raiders = u.Ships.Values.Where(ThreatMap.IsRaider).ToList();
        Assert.Equal(3, raiders.Count);
        Assert.All(raiders, r => { Assert.Equal(pirates.Id, r.Owner); Assert.Equal(hideout.Id, r.HomeBase); Assert.True(r.Has(Capability.Combat)); Assert.False(r.IsEconomyAi); });
        // Polizei: je Fraktion zwei Wächter an ihrem Hauptquartier
        foreach (var f in new[] { Union, Guild })
        {
            var police = SecuritySystem.PoliceOf(u, f);
            Assert.Equal(2, police.Count);
            Assert.All(police, p => { Assert.Equal(GovernmentKind.Headquarters, StationProtection.KindOf(u, u.Stations[p.HomeBase])); Assert.False(p.IsEconomyAi); Assert.Equal(p.MaxHull, p.Hull); });
        }
        // Piraten investieren nicht in Stationen (keine Kasse als Geldgeber)
        pirates.Treasury.Credits = 1_000_000f;
        Assert.DoesNotContain(Investors.ForStation(u, 1000f), i => i.Holder == pirates.Id);
    }

    [Fact]
    public void Content_UngueltigeAkteureUndBesitzerWerfen()
    {
        WorldDef Def() => WorldBuilder.Parse(WorldBuilder.DefaultWorldJson());
        var d1 = Def(); d1.Actors[0].Kind = "state";
        Assert.Contains("unbekannte Art", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(d1)).Message);
        var d2 = Def(); d2.Actors.Add(new ActorDef { Key = "pirates", Name = "Doppelt" });
        Assert.Contains("doppelt", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(d2)).Message);
        var d3 = Def(); d3.Sectors[0].Stations[0].Owner = "nobody";
        Assert.Contains("unbekannter Besitzer", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(d3)).Message);
        var d4 = Def(); d4.StationTypes.First(t => t.Key == "headquarters").Security = -1f;
        Assert.Contains("security", Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(d4)).Message);
    }

    // ───────── Lagekarte ─────────

    [Fact]
    public void Lagekarte_GrundschutzStreifenBedrohungVerfall_UndAbdeckung()
    {
        var u = Fresh();
        // Start: Alpha/Gamma Hauptquartier (1,5) plus zwei Wächter am HQ; Beta/Delta nur Sektor-HQ (0,5) → Kontrollstufen wie in Phase 10
        float warden = CombatRules.Strength(SecuritySystem.PoliceOf(u, Union)[0]) / u.Rules.PoliceStrengthUnit;
        Assert.Equal(1.5f + 2 * warden, u.Sectors[Alpha].Security, 3);
        Assert.Equal(0.5f, u.Sectors[Beta].Security, 3);
        Assert.Equal(ControlStage.Control, u.Sectors[Alpha].Control);
        Assert.Equal(ControlStage.Dominance, u.Sectors[Beta].Control);
        Assert.True(ThreatMap.HasCoverage(u, u.Sectors[Alpha], Union));
        Assert.False(ThreatMap.HasCoverage(u, u.Sectors[Beta], Union));
        // eine Streife in Beta liefert die Abdeckung (echte Patrouille statt Annahme)
        var p = SecuritySystem.PoliceOf(u, Union)[0];
        u.MoveShip(p, Beta);
        ThreatMap.Initialize(u);
        Assert.True(ThreatMap.HasCoverage(u, u.Sectors[Beta], Union));
        // Bedrohung: Meldung, Routenrisiko, Verfall
        Assert.Equal(0f, ThreatMap.RiskOf(u, Gamma));
        ThreatMap.Report(u, Gamma, 4f);
        float risk = ThreatMap.RiskOf(u, Gamma);
        Assert.Equal(u.Rules.RiskPerThreat * 4f / (1f + u.Sectors[Gamma].Security), risk, 4);
        Assert.Equal(risk, ThreatMap.RouteRisk(u, Alpha, Gamma), 4);   // Alpha → Beta → Gamma, nur Gamma ist bedroht
        Assert.Equal(0f, ThreatMap.RouteRisk(u, Alpha, Beta));
        ThreatMap.Update(u, 1000);
        Assert.True(u.Sectors[Gamma].Threat < 4f && u.Sectors[Gamma].Threat > 0f);
        u.Rules.SecurityActive = false;
        Assert.Equal(0f, ThreatMap.RouteRisk(u, Alpha, Gamma));
    }

    // ───────── Begegnungen ─────────

    [Fact]
    public void Begegnung_Abstrakt_NachStaerke_Deterministisch_UndAustauschbar()
    {
        var u = Fresh();
        var police = SecuritySystem.PoliceOf(u, Union)[0];
        var raider = u.Ships.Values.First(ThreatMap.IsRaider);
        var trader = u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        Assert.True(CombatRules.Strength(raider) > 5f * CombatRules.Strength(trader));   // Waffen zählen, ein Frachter wehrt sich kaum
        Assert.True(CombatRules.Strength(police) > CombatRules.Strength(raider));
        Assert.True(CombatRules.WinChance(u.Rules, CombatRules.Strength(raider), CombatRules.Strength(trader)) > 0.9f);
        // gleicher Zustand → gleiche Ergebnisse; genau fünf Zufallszahlen je Begegnung
        var a = Fresh(); var b = Fresh();
        var ra = new AbstractEncounterResolver();
        var resA = Enumerable.Range(0, 50).Select(_ => ra.Resolve(a, new[] { a.Ships[raider.Id] }, new[] { a.Ships[trader.Id] }, 0.03f)).ToList();
        var resB = Enumerable.Range(0, 50).Select(_ => ra.Resolve(b, new[] { b.Ships[raider.Id] }, new[] { b.Ships[trader.Id] }, 0.03f)).ToList();
        Assert.Equal(resA, resB);
        var rng = new DeterministicRng(Fresh().Rng.State);
        for (int i = 0; i < 5 * 50; i++) rng.NextUInt64();
        Assert.Equal(rng.State, a.Rng.State);
        Assert.True(resA.Count(r => r.AttackerWon) > 40);
        Assert.All(resA.Where(r => r.AttackerWon), r => Assert.InRange(r.LootFraction, u.Rules.LootMin, u.Rules.LootMax));
        // der Auflöser ist austauschbar (später echter Kampf): Piraten nutzen den gesetzten
        var fixedR = new FixedResolver(attackerWins: false);
        u.Encounters = fixedR;
        var victim = LadenTrader(u, Gamma, FarFromStations(u, Gamma));
        var rd = Raider(u, Gamma, victim.Position + new Vec2(5, 0));
        var ev = PirateSystem.ResolveRaid(u, rd, victim, u.Sectors[Gamma]);
        Assert.Equal(1, fixedR.Calls);
        Assert.False(ev.AttackerWon);
        Assert.Equal(80f, victim.Cargo[DemoUniverse.Food]);   // abgewehrt: Fracht bleibt
    }

    // ───────── Piraten ─────────

    [Fact]
    public void Ueberfall_AufHaendler_FrachtVerlust_Hehlerei_BedrohungUndFahndung()
    {
        var u = Fresh();
        u.Encounters = new FixedResolver(attackerWins: true, loot: 0.5f);
        var pirates = u.Actors.Values.Single(a => a.Kind == ActorKind.Outlaw);
        float treasury = pirates.Treasury.Credits;
        var victim = LadenTrader(u, Gamma, FarFromStations(u, Gamma));
        var raider = Raider(u, Gamma, victim.Position + new Vec2(5, 0));
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        // nur Opfer abseits der Stationen mit lohnender Fracht
        Assert.Contains(PirateSystem.Victims(u, Gamma), v => v.Ship == victim);
        var ev = PirateSystem.ResolveRaid(u, raider, victim, u.Sectors[Gamma]);
        Assert.True(ev.AttackerWon);
        Assert.Equal(40f, victim.Cargo[DemoUniverse.Food], 3);
        float loot = 40f * u.Goods[DemoUniverse.Food].BasePrice;
        Assert.Equal(loot, ev.LootValue, 2);
        Assert.Equal(loot * u.Rules.FenceShare, pirates.Treasury.Credits - treasury, 2);
        Assert.Contains(events, e => e is TreasuryIncome { Purpose: "fence" } ti && ti.Actor == pirates.Id);
        Assert.True(victim.Hull < victim.MaxHull);
        Assert.Equal(u.Rules.RaidThreat, u.Sectors[Gamma].Threat, 3);
        Assert.Equal(1, Wanted.LevelOf(u, pirates.Id, Guild));   // Piraterie im Gebiet der Gilde
        Assert.Contains(events, e => e is CrimeReported { Crime: "piracy" });
        Assert.IsType<GoToOrder>(raider.Order);   // mit Beute heim zur Hehlerei
        // Opfer im Schutz einer Station werden nicht überfallen
        victim.Position = u.Stations[u.Sectors[Gamma].Stations[0]].DockPoint(victim.Id);
        Assert.DoesNotContain(PirateSystem.Victims(u, Gamma), v => v.Ship == victim);
    }

    [Fact]
    public void Ueberfall_MitStreife_PolizeiGreiftEin_UndAbfangenStelltKaperschiff()
    {
        var u = Fresh();
        var victim = LadenTrader(u, Gamma, FarFromStations(u, Gamma));
        var police = SecuritySystem.PoliceOf(u, Guild)[0];
        u.MoveShip(police, Gamma);
        police.Position = victim.Position + new Vec2(-5, 0);
        u.Rules.PoliceResponseChance = 1f; u.Rules.PoliceResponseMax = 1f;
        u.Encounters = new FixedResolver(attackerWins: false);
        var raider = Raider(u, Gamma, victim.Position + new Vec2(5, 0));
        var ev = PirateSystem.ResolveRaid(u, raider, victim, u.Sectors[Gamma]);
        Assert.Equal(police.Id, ev.Police);
        Assert.False(ev.AttackerWon);
        Assert.Equal(u.Rules.RepelledThreat, u.Sectors[Gamma].Threat, 3);
        // Abfangen: Streife gewinnt, Kaperschiff zerstört
        var u2 = Fresh();
        var p2 = SecuritySystem.PoliceOf(u2, Guild)[0];
        var r2 = Raider(u2, Gamma, FarFromStations(u2, Gamma));
        u2.Encounters = new FixedResolver(attackerWins: true, destroyed: true);
        var removed = new List<ShipRemoved>();
        u2.Events.Raised += e => { if (e is ShipRemoved sr) removed.Add(sr); };
        var ic = PirateSystem.Intercept(u2, p2, r2);
        Assert.True(ic.PoliceWon && ic.RaiderDestroyed);
        Assert.False(u2.Ships.ContainsKey(r2.Id));
        Assert.Contains(removed, x => x.Ship == r2.Id && x.Reason == "destroyed");
        // die Piraten bauen aus ihrer Kasse nach (nach der Abkühlzeit)
        var pirates = u2.Actors.Values.Single(a => a.Kind == ActorKind.Outlaw);
        pirates.LastForceTick = -1_000_000;
        float before = pirates.Treasury.Credits;
        var built = new List<RaiderBuilt>();
        u2.Events.Raised += e => { if (e is RaiderBuilt rb) built.Add(rb); };
        new SimulationRunner().Run(u2, 60);
        Assert.Single(built);
        Assert.Equal(3, u2.Ships.Values.Count(ThreatMap.IsRaider));
        Assert.True(pirates.Treasury.Credits <= before - u2.Rules.RaiderCost + 1f);
    }

    [Fact]
    public void Ueberfall_AufSpieler_Vorwarnung_Entkommen_UndAufloesungOhneRuin()
    {
        var u = Fresh();
        var player = u.Ships[u.PlayerShip];
        u.MoveShip(player, Gamma);
        u.PlayerSector = Gamma;
        player.Position = FarFromStations(u, Gamma);
        player.Cargo[DemoUniverse.Food] = 50f;
        var raider = Raider(u, Gamma, player.Position + new Vec2(8, 0));
        u.Rules.PlayerRaidWeight = 1e6f;   // der Spieler ist das Ziel (NPCs mit Fracht im Sektor nicht vorziehen)
        u.Rules.RaidRatePerTick = 1f;   // sofort zuschlagen
        u.Rules.InterceptRatePerTick = 0f;
        var warnings = new List<EncounterWarning>();
        var escaped = new List<EncounterEscaped>();
        var raids = new List<RaidResolved>();
        u.Events.Raised += e => { if (e is EncounterWarning w) warnings.Add(w); else if (e is EncounterEscaped x) escaped.Add(x); else if (e is RaidResolved { Player: true } r) raids.Add(r); };
        var runner = new SimulationRunner();
        runner.Run(u, 5);
        var w1 = Assert.Single(warnings);
        Assert.Equal(EncounterKind.Raid, w1.Kind);
        Assert.Single(u.PendingEncounters);
        // der Spieler dockt an (Schutz der Station): entkommen, nichts verloren
        player.Position = u.Stations[u.Sectors[Gamma].Stations[0]].Position;
        runner.Run(u, u.Rules.PlayerWarningTicks + 5);
        Assert.Single(escaped);
        Assert.Empty(raids);
        Assert.Equal(50f, player.Cargo[DemoUniverse.Food]);
        // zweiter Versuch, der Spieler bleibt draußen: Überfall mit Verlust, aber Hülle nie unter dem Mindestanteil, Schiff bleibt
        raider = Raider(u, Gamma, FarFromStations(u, Gamma) + new Vec2(8, 0));
        player.Position = FarFromStations(u, Gamma);
        u.Encounters = new FixedResolver(attackerWins: true, destroyed: true, loot: 0.5f, dmg: 5f);
        warnings.Clear();
        u.Rules.PlayerRaidWeight = 1e6f;   // NPCs mit Fracht im Sektor nicht vorziehen
        for (int i = 0; i < 400 && raids.Count == 0; i++) runner.Step(u);
        var raid = Assert.Single(raids);
        Assert.True(raid.Player && raid.AttackerWon && !raid.VictimDestroyed);
        Assert.Single(warnings);
        Assert.True(u.Ships.ContainsKey(player.Id));
        Assert.Equal(25f, player.Cargo[DemoUniverse.Food], 3);
        Assert.Equal(u.Rules.PlayerMinHull * player.MaxHull, player.Hull, 2);
        Assert.Contains("Überfall", raid.Message);
        // ohne Spielerüberfälle keine Warnung
        var u3 = Fresh();
        u3.Rules.PlayerRaids = false;
        var p3 = u3.Ships[u3.PlayerShip];
        u3.MoveShip(p3, Gamma); u3.PlayerSector = Gamma; p3.Position = FarFromStations(u3, Gamma); p3.Cargo[DemoUniverse.Food] = 50f;
        Assert.DoesNotContain(PirateSystem.Victims(u3, Gamma), v => v.Ship == p3);
    }

    [Fact]
    public void Kopfgeld_SpielerBesiegtKaperschiff_KopfgeldbueroZahltAusDerKasse()
    {
        var u = Fresh();
        var player = u.Ships[u.PlayerShip];
        u.MoveShip(player, Gamma); u.PlayerSector = Gamma;
        player.Position = FarFromStations(u, Gamma);
        player.Cargo[DemoUniverse.Food] = 50f;
        var pirates = u.Actors.Values.Single(a => a.Kind == ActorKind.Outlaw);
        // die Piraten sind bei der Gilde schon Staatsfeind (Stufe 4)
        for (int i = 0; i < 4; i++) Wanted.Report(u, pirates.Id, Guild, "piracy");
        Assert.Equal(4, Wanted.LevelOf(u, pirates.Id, Guild));
        var raider = Raider(u, Gamma, player.Position + new Vec2(8, 0));
        u.Encounters = new FixedResolver(attackerWins: false);
        u.Rules.RaiderDestroyChance = 1f;   // unterlegen → zerstört
        var ev = PirateSystem.ResolveRaid(u, raider, player, u.Sectors[Gamma]);
        Assert.True(ev.RaiderDestroyed);
        float bounty = 4 * u.Rules.BountyPerLevel + 0f;
        Assert.Equal(Wanted.Level(u.Rules, pirates.Wanted[Guild].Heat) * u.Rules.BountyPerLevel, u.Player.BountyClaims[Guild], 2);
        Assert.Contains("Kopfgeld", ev.Message);
        // Kopfgeldbüro am Gildenhaus (Hauptquartier Gamma): Auszahlung aus der Gildenkasse; strenge Schlüssel
        var hq = u.Stations[new StationId(34)];
        var office = new BountyService();
        Assert.True(office.IsAvailable(u, hq));
        var offers = office.Offers(u, hq, player);
        Assert.Contains(offers, o => o.Key == $"claim:{Guild.Value}" && o.Amount == 0f);   // Kasse leer: nichts auszuzahlen
        Assert.Contains(offers, o => o.Key == $"wanted:{pirates.Id.Value}" && o.Amount == 0f && o.Price > 0f);   // Fahndungsliste (Anzeige)
        Assert.False(office.Execute(u, hq, player, new ServiceRequest($"wanted:{pirates.Id.Value}", default, 0f)).Success);
        Assert.False(office.Execute(u, hq, player, new ServiceRequest($"claim:{Union.Value}", default, 0f)).Success);   // falsche Fraktion
        u.Actors[Guild].Treasury.Credits = 10_000f;
        float claim = u.Player.BountyClaims[Guild], wallet = u.PlayerWallet.Credits;
        var r = office.Execute(u, hq, player, new ServiceRequest($"claim:{Guild.Value}", default, 0f));
        Assert.True(r.Success);
        Assert.Equal(wallet + claim, u.PlayerWallet.Credits, 2);
        Assert.Equal(10_000f - claim, u.Actors[Guild].Treasury.Credits, 2);
        Assert.False(u.Player.BountyClaims.ContainsKey(Guild));
        Assert.True(bounty >= claim);
    }

    // ───────── Fahndung, Gericht, Verbrechen ─────────

    [Fact]
    public void Fahndung_Stufen_Verfall_GerichtLoeschtGegenStrafe()
    {
        var u = Fresh();
        var player = u.Ships[u.PlayerShip];
        var changes = new List<WantedChanged>();
        u.Events.Raised += e => { if (e is WantedChanged w) changes.Add(w); };
        Wanted.Report(u, u.PlayerActor, Guild, "toll_evasion");
        Assert.Equal(1, Wanted.LevelOf(u, u.PlayerActor, Guild));
        Wanted.Report(u, u.PlayerActor, Guild, "smuggling");
        Assert.Equal(4, Wanted.LevelOf(u, u.PlayerActor, Guild));   // 1,5 + 2,5
        Assert.Equal(new[] { (0, 1), (1, 4) }, changes.Select(c => (c.From, c.To)));
        for (int i = 0; i < 10; i++) Wanted.Report(u, u.PlayerActor, Guild, "smuggling");
        Assert.Equal(u.Rules.WantedMaxHeat, u.Player.Wanted[Guild].Heat, 3);   // Obergrenze
        // Verfall erst nach der Mindestdauer
        float heat = u.Player.Wanted[Guild].Heat;
        Wanted.Decay(u, 500);
        Assert.Equal(heat, u.Player.Wanted[Guild].Heat);
        u.Tick += u.Rules.WantedMinTicks;
        Wanted.Decay(u, 1000);
        Assert.Equal(heat - 1000 * u.Rules.WantedDecayPerTick, u.Player.Wanted[Guild].Heat, 3);
        // Gericht: am Gildenhaus (eigene Fraktion) und — Rechtshilfe — an Unionsstationen (Beziehung ≥ 0); strenge Schlüssel
        var court = new CourtService();
        var guildHq = u.Stations[new StationId(34)];
        var unionHq = u.Stations[new StationId(32)];
        Assert.True(CourtService.Accepts(u, guildHq, Guild));
        Assert.True(CourtService.Accepts(u, unionHq, Guild));
        float fine = Wanted.FineOf(u, u.Player.Wanted[Guild]);
        u.PlayerWallet.Credits = fine - 1f;
        var offer = Assert.Single(court.Offers(u, guildHq, player));
        Assert.Equal($"pay:{Guild.Value}", offer.Key);
        Assert.Equal(0f, offer.Amount);
        Assert.False(court.Execute(u, guildHq, player, new ServiceRequest($"pay:{Guild.Value}", default, 0f)).Success);
        Assert.False(court.Execute(u, guildHq, player, new ServiceRequest("pay:x", default, 0f)).Success);
        Assert.False(court.Execute(u, guildHq, player, new ServiceRequest($"pay:{Union.Value}", default, 0f)).Success);   // keine Strafe offen
        Assert.True(u.Player.Wanted.ContainsKey(Guild));
        u.PlayerWallet.Credits = fine + 100f;
        float treasury = u.Actors[Guild].Treasury.Credits;
        var paid = new List<SimEvent>();
        u.Events.Raised += paid.Add;
        var r = court.Execute(u, unionHq, player, new ServiceRequest($"pay:{Guild.Value}", default, 0f));
        Assert.True(r.Success);
        Assert.Equal(100f, u.PlayerWallet.Credits, 2);
        Assert.Equal(treasury + fine, u.Actors[Guild].Treasury.Credits, 2);
        Assert.False(u.Player.Wanted.ContainsKey(Guild));
        Assert.Contains(paid, e => e is FinePaid);
        Assert.Contains(paid, e => e is ServicePerformed { Service: "court" });
        // feindliche Fraktionen leisten keine Rechtshilfe
        Relations.Set(u, Guild, Union, -0.5f);
        Assert.False(CourtService.Accepts(u, unionHq, Guild));
    }

    [Fact]
    public void Verbrechen_SchmuggelAmKontrollpunkt_ZollfluchtUndPolizeiStelltGesuchte()
    {
        var u = Fresh();
        var player = u.Ships[u.PlayerShip];
        var into = u.Gates.Values.First(g => g.Sector == Beta && u.Gates[g.Target].Sector == Gamma);
        // Schmuggel: Gamma auf Stufe 1, Wasser ist verboten — Sprung abgewiesen, Verbrechen gemeldet, Bedrohung in Gamma
        u.Rules.Contraband.Add("water");
        FactionPolicySystem.SetLevel(u, u.Sectors[Gamma], 1);
        u.MoveShip(player, Beta); u.PlayerSector = Beta;
        player.Cargo[DemoUniverse.Water] = 10f;
        var j = GateAccess.TryPlayerJump(u, player, into);
        Assert.False(j.Allowed);
        Assert.Contains("Schmuggel", j.Message);
        Assert.Equal(2, Wanted.LevelOf(u, u.PlayerActor, Guild));
        Assert.Equal(u.Rules.SmugglingThreat, u.Sectors[Gamma].Threat, 3);
        // Polizei stellt den Gesuchten (Stufe ≥ 3) in Gamma: Vorwarnung, dann Strafe eingezogen und Schmuggelware beschlagnahmt
        Wanted.Report(u, u.PlayerActor, Guild, "toll_evasion");
        Assert.True(Wanted.LevelOf(u, u.PlayerActor, Guild) >= 3);
        u.MoveShip(player, Gamma); u.PlayerSector = Gamma;
        var police = SecuritySystem.PoliceOf(u, Guild)[0];
        Assert.Equal(Gamma, police.Sector);   // Wächter liegen am Gildenhaus in Gamma
        u.PlayerWallet.Credits = 50_000f;
        float fine = Wanted.FineOf(u, u.Player.Wanted[Guild]);
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        var runner = new SimulationRunner();
        runner.Run(u, 5);
        Assert.Contains(events, e => e is EncounterWarning { Kind: EncounterKind.Intercept });
        runner.Run(u, u.Rules.PlayerWarningTicks + 5);
        var ic = Assert.Single(events.OfType<PlayerIntercepted>());
        Assert.Equal(fine, ic.FineCollected, 1);
        Assert.Equal(10f, ic.SeizedUnits, 3);
        Assert.False(player.Cargo.ContainsKey(DemoUniverse.Water));
        Assert.False(u.Player.Wanted.ContainsKey(Guild));
        Assert.Equal(50_000f - fine, u.PlayerWallet.Credits, 1);
    }

    // ───────── Polizei ─────────

    [Fact]
    public void Polizei_UnterhaltAusDerKasse_SollNachBedrohung_KaufUeberKaufregel_UndZuweisung()
    {
        var u = Fresh();
        u.Actors[Union].Treasury.Credits = 100_000f;
        var events = new List<SimEvent>();
        u.Events.Raised += events.Add;
        Assert.Equal(2, SecuritySystem.Desired(u, Union));   // zwei Sektoren, keine Bedrohung
        new SimulationRunner().Run(u, 1);
        Assert.Empty(events.OfType<PoliceCommissioned>());   // Soll erfüllt: kein Kauf
        ThreatMap.Report(u, Beta, 4f);
        Assert.Equal(4, SecuritySystem.Desired(u, Union));   // 2 + 4 × 0,5, gedeckelt bei 4
        // Kauf aus der Kasse über die Kaufregel an der Werft (Bausatz + Module aus dem Lager, Preis an die Werft)
        var yard = u.Stations.Values.First(s => s.TypeKey == "shipyard");
        var model = u.ShipModels[u.Rules.PoliceModel];
        foreach (var n in ShipPurchase.Materials(u, model)!) yard.Stock(n.Good).Amount = 10f;
        float yardCredits = yard.Credits, treasury = u.Actors[Union].Treasury.Credits;
        u.Actors[Union].LastForceTick = -1_000_000;
        var ship = SecuritySystem.Commission(u, Union);
        Assert.NotNull(ship);
        var bought = Assert.Single(events.OfType<PoliceCommissioned>());
        Assert.Equal(yardCredits + bought.Price, yard.Credits, 1);
        Assert.Equal(treasury - bought.Price, u.Actors[Union].Treasury.Credits, 1);
        Assert.Equal("warden", ship!.ModelKey);
        Assert.Equal(ShipRole.Police, ship.Role);
        Assert.Equal(GovernmentKind.Headquarters, StationProtection.KindOf(u, u.Stations[ship.HomeBase]));
        foreach (var n in ShipPurchase.Materials(u, model)!) Assert.Equal(10f - n.Units, yard.Stock(n.Good).Amount, 3);
        // Zuweisung: Beta (bedroht, Verkehr) bekommt mehr Streifen als Alpha
        var police = SecuritySystem.PoliceOf(u, Union);
        SecuritySystem.Assign(u, Union, police);
        var quotas = SecuritySystem.Quotas(u, SecuritySystem.Territory(u, Union), police.Count);
        Assert.Equal(police.Count, quotas.Values.Sum());
        Assert.True(quotas[Beta] > quotas[Alpha]);
        Assert.Equal(quotas[Beta], police.Count(p => p.DutySector == Beta));
        // Unterhalt aus der Kasse (Takt 100 Ticks)
        events.Clear();
        new SimulationRunner().Run(u, 200);
        var upkeep = events.OfType<TreasuryUpkeep>().Where(t => t.Purpose == "police" && t.Actor == Union).ToList();
        Assert.NotEmpty(upkeep);
        Assert.All(upkeep, t => Assert.Equal(u.Rules.PoliceUpkeepPerTick * 3 * SecuritySystem.ManageTicks, t.Amount + t.Unpaid, 1));
        // die Streifen fliegen in ihre Sektoren (nur eigenes Gebiet)
        new SimulationRunner().Run(u, 2000);
        Assert.All(SecuritySystem.PoliceOf(u, Union), p => Assert.Equal(Union, u.Sectors[p.DutySector].Owner));
        Assert.Contains(SecuritySystem.PoliceOf(u, Union), p => p.Sector == Beta || p.Transit is not null);
    }

    [Fact]
    public void Polizei_Abbau_NachDauerhafterUeberzahl()
    {
        var u = Fresh();
        var f = Guild;
        u.Actors[f].Treasury.Credits = 100_000f;
        u.Rules.PoliceMinPerFaction = 1;
        u.Rules.PolicePerSector = 0.5f;   // Soll 1 statt 2
        u.Actors[f].LastForceTick = -1_000_000;
        var gone = new List<PoliceDecommissioned>();
        u.Events.Raised += e => { if (e is PoliceDecommissioned d && d.Faction == f) gone.Add(d); };
        new SimulationRunner().Run(u, 200);
        Assert.Single(gone);   // einmal abgebaut, danach Abkühlzeit
        Assert.Single(SecuritySystem.PoliceOf(u, f));
    }

    // ───────── Risikoabschlag ─────────

    [Fact]
    public void Haendler_UndMiner_RechnenDasRoutenrisikoEin()
    {
        var u = Fresh();
        new SimulationRunner().Run(u, 50);
        var trader = u.Ships.Values.Where(s => s.Role == ShipRole.Trader && s.Transit is null).OrderBy(s => s.Id.Value).FirstOrDefault()
                     ?? u.Ships.Values.First(s => s.Role == ShipRole.Trader);
        var (order, score) = TraderSystem.BestTrade(u, trader);
        Assert.NotNull(order);
        var buy = u.Stations[order!.Buy]; var sell = u.Stations[order.Sell];
        // Bedrohung auf dem Weg der Ladung senkt die Bewertung genau dieser Route
        foreach (var sec in u.Sectors.Values) ThreatMap.Report(u, sec.Id, 20f);
        float risk = ThreatMap.RouteRisk(u, buy.Sector, sell.Sector);
        Assert.True(risk > 0f);
        var (_, riskyScore) = TraderSystem.BestTrade(u, trader);
        Assert.True(riskyScore < score);
        // Miner ebenso
        var miner = u.Ships.Values.First(s => s.Role == ShipRole.Miner);
        foreach (var sec in u.Sectors.Values) sec.Threat = 0f;
        var (_, _, mineScore) = MinerSystem.BestMine(u, miner);
        foreach (var sec in u.Sectors.Values) sec.Threat = 20f;
        var (_, _, riskyMine) = MinerSystem.BestMine(u, miner);
        Assert.True(riskyMine < mineScore);
    }

    // ───────── Determinismus, Save, Migration ─────────

    [Fact]
    public void Determinismus_UndSaveRoundtrip_V14_MitFahndungLagekarteUndAnkuendigung_BitIdentisch()
    {
        var a = Fresh(); var b = Fresh();
        var runner = new SimulationRunner();
        runner.Run(a, 4000); runner.Run(b, 4000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(b));
        // Zustand anreichern: Fahndung, Kopfgeld, Bedrohung, angekündigte Begegnung
        Wanted.Report(a, a.PlayerActor, Guild, "smuggling");
        a.Player.BountyClaims[Union] = 300f;
        ThreatMap.Report(a, Delta, 2.5f);
        var raider = a.Ships.Values.First(ThreatMap.IsRaider);
        a.PendingEncounters.Add(new PendingEncounter { Kind = EncounterKind.Raid, Source = raider.Id, Target = a.PlayerShip, Sector = raider.Sector, DueTick = a.Tick + 40 });
        var json = SaveGameJson.Write(a, Now);
        Assert.Contains("\"Version\":17", json);   // Phase 14: v17
        var (loaded, _) = SaveGameJson.Read(json);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
        Assert.Equal(a.Player.Wanted[Guild].Heat, loaded.Player.Wanted[Guild].Heat);
        Assert.Equal("smuggling", loaded.Player.Wanted[Guild].LastCrime);
        Assert.Equal(300f, loaded.Player.BountyClaims[Union]);
        Assert.Equal(a.Sectors[Delta].Threat, loaded.Sectors[Delta].Threat);
        Assert.Single(loaded.PendingEncounters);
        Assert.All(a.Ships.Values.Where(s => s.HomeBase.Value != 0), s => Assert.Equal(s.HomeBase, loaded.Ships[s.Id].HomeBase));
        Assert.True(loaded.ShipClasses["raider"].Restricted);
        Assert.True(loaded.StationTypes["pirate_hideout"].Hideout);
        Assert.Equal(1.5f, loaded.StationTypes["headquarters"].Security);
        runner.Run(a, 3000);
        new SimulationRunner().Run(loaded, 3000);
        Assert.Equal(UniverseHash.Compute(a), UniverseHash.Compute(loaded));
    }

    [Fact]
    public void Migration_V13ToV14_PolizeiPiratenContentDienste()
    {
        var u = Fresh();
        new SimulationRunner().Run(u, 500);
        var v13 = ToV13Json(SaveGameJson.Write(u, Now), full: true);
        Assert.Contains("\"Version\":13", v13);
        Assert.DoesNotContain("weapon_laser_s", v13);
        Assert.DoesNotContain("pirate_hideout", v13);
        Assert.DoesNotContain("warden", v13);
        Assert.DoesNotContain("\"court\"", v13);
        Assert.DoesNotContain("\"Kind\":\"Outlaw\"", v13);
        var (loaded, _) = SaveGameJson.Read(v13);
        // Content
        Assert.True(loaded.Modules.ContainsKey("weapon_laser_s"));
        Assert.NotNull(loaded.FindGood("weapon_laser_s"));
        Assert.True(loaded.ShipClasses["raider"].Restricted);
        Assert.Contains(loaded.StationTypes["weapons_works"].Recipes, r => r.Outputs.ContainsKey(loaded.FindGood("weapon_laser_s")!.Id));
        Assert.Equal(new[] { "court", "bounty", "hangar", "taxi" }, loaded.StationTypes["headquarters"].Services);   // Phase 12: Hangar, Taxi
        Assert.Equal(0.5f, loaded.StationTypes["sector_hq"].Security);
        // Polizei je Fraktion, Piraten mit Versteck und Kaperschiffen
        foreach (var f in new[] { Union, Guild })
        {
            var police = SecuritySystem.PoliceOf(loaded, f);
            Assert.Equal(2, police.Count);
            Assert.All(police, p => { Assert.True(loaded.Stations.ContainsKey(p.HomeBase)); Assert.Equal(p.Stats.FuelCapacity, p.TotalFuel); Assert.Equal(p.MaxHull, p.Hull); });
        }
        var pirates = Assert.Single(loaded.Actors.Values, a => a.Kind == ActorKind.Outlaw);
        var hideout = Assert.Single(loaded.Stations.Values, st => StationProtection.IsHideout(loaded, st));
        Assert.Equal(pirates.Id, hideout.Owner);
        Assert.Equal(3, loaded.Ships.Values.Count(s => ThreatMap.IsRaider(s) && s.Owner == pirates.Id && s.HomeBase == hideout.Id));
        Assert.All(loaded.Sectors.Values, s => Assert.True(s.Security > 0f));   // Lagekarte beim Laden gemessen
        // eine frische Welt (Tick 0) lädt nach dem Demotieren hash-gleich: Ids in der Gründerlücke, Akteur-Id, Versteck-Id wie beim Weltaufbau
        var fresh = Fresh();
        var (again, _) = SaveGameJson.Read(ToV13Json(SaveGameJson.Write(fresh, Now)));
        Assert.Equal(UniverseHash.Compute(fresh), UniverseHash.Compute(again));
        new SimulationRunner().Run(loaded, 3000);
        Assert.True(loaded.Ships.Values.Count(s => s.IsEconomyAi) >= 10);
    }

    // ───────── Langlauf ─────────

    [Fact]
    public void Langlauf_20000Ticks_PiratenUeberfallen_PolizeiReagiert_FlotteHaelt()
    {
        var u = Fresh();
        int raids = 0, policeActs = 0, lost = 0;
        double loot = 0, trade = 0;
        u.Events.Raised += e =>
        {
            switch (e)
            {
                case RaidResolved r: raids++; loot += r.LootValue; if (r.Police.Value != 0) policeActs++; if (r.VictimDestroyed) lost++; break;
                case RaiderIntercepted: policeActs++; break;
                case TradeCompleted t: trade += t.Revenue; break;
            }
        };
        var runner = new SimulationRunner();
        int raidersLate = 0;   // Phase 13: höchste Zahl Kaperschiffe im letzten Viertel (eine ausgelöschte Flotte fängt neu an)
        for (int t = 0; t < 20_000; t += 500)
        {
            runner.Run(u, 500);
            if (u.Tick > 15_000) raidersLate = Math.Max(raidersLate, u.Ships.Values.Count(ThreatMap.IsRaider));
            // keine Geisteraufträge: Polizei und Piraten haben nur Patrouillen- oder Heimflüge, nie Handels-/Abbauaufträge
            Assert.All(u.Ships.Values.Where(s => s.Role is ShipRole.Police or ShipRole.Pirate), s => Assert.True(s.Order is null or PatrolOrder or GoToOrder));
            Assert.All(u.Ships.Values, s => Assert.InRange(s.Hull, 0f, s.MaxHull + 0.01f));
            Assert.All(u.Sectors.Values, s => Assert.True(s.Threat >= 0f && float.IsFinite(s.Threat)));
        }
        Assert.True(raids > 0, "Piraten überfallen in der Startwelt");
        Assert.True(policeActs > 0, "Polizei reagiert (greift ein oder fängt ab)");
        Assert.True(loot < 0.05 * trade, $"Verlustquote moderat: {loot:0} von {trade:0}");
        Assert.True(lost <= 5);
        // die Flotte hält ihre Kapazität (Neubauten ersetzen Verluste)
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.ByKey("trade")!) >= 11f);
        Assert.True(ShipJobs.UsedUnits(u, ShipJobs.ByKey("mine")!) >= 6f);
        // Phase 13: Piraten bleiben im Spiel — im letzten Viertel gab es Kaperschiffe; ist die Flotte gerade ausgelöscht (Standard-Seed ab Tick 18000),
        // baut der Neuanfang (RaiderRestartCostFactor, PirateIdleIncomePerTick) sie wieder auf, statt sie für immer zu verlieren
        Assert.True(raidersLate >= 1);
        Assert.True(u.Ships.Values.Count(ThreatMap.IsRaider) >= 1 || u.Actors.Values.Any(a => a.Kind == ActorKind.Outlaw && a.Treasury.Credits > 0f));
        Assert.All(u.Factions.Keys, f => Assert.True(SecuritySystem.PoliceOf(u, f).Count >= u.Rules.PoliceMinPerFaction));
    }

    [Fact]
    public void Piraten_Neuanfang_NachAusloeschung_HalberPreis_EinnahmeNurOhneFlotte()   // Phase 13 (Korrektur an Phase 11)
    {
        var u = Fresh();
        var outlaw = u.Actors.Values.Single(a => a.Kind == ActorKind.Outlaw);
        foreach (var s in u.Ships.Values.Where(ThreatMap.IsRaider).ToList()) u.RemoveShip(s.Id, "test");
        outlaw.Treasury.Credits = 0f;
        var built = new List<RaiderBuilt>();
        int raidersAtBuild = 0;
        double income = 0;
        u.Events.Raised += e =>
        {
            if (e is RaiderBuilt b) { built.Add(b); raidersAtBuild = Math.Max(raidersAtBuild, u.Ships.Values.Count(ThreatMap.IsRaider)); }
            else if (e is TreasuryIncome { Purpose: "hideout" } t) income += t.Amount;
        };
        new SimulationRunner().Run(u, 12_000);
        var first = Assert.Single(built.Take(1));
        Assert.Equal(u.Rules.RaiderCost * u.Rules.RaiderRestartCostFactor, first.Cost, 3);
        Assert.InRange(income, 0.9 * first.Cost, 1.2 * first.Cost);   // die Einnahme floss nur, solange kein Kaperschiff da war
        // Phase 14: geprüft wird, dass der Neuanfang ein Kaperschiff in die Welt bringt — ob es bis Tick 12000 überlebt, hängt vom Kampfglück ab
        // (Standard-Seed: am Ende abgewehrt und zerstört)
        Assert.True(raidersAtBuild >= 1);
    }
}
