using SpaceSim.Core.Combat;
using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Ausbau Punkt 17 — die Verwaltung der etablierten Banditenfraktion (docs/BANDITEN.md): Schutzflotte (Unterhalt aus der Kasse,
/// Kauf am Sitz, Befehle), Nachbau verlorener Wirtschaftsstationen, Vorposten in herrenlosen Sektoren („Sektor erobern“) und der
/// Beuteanteil als Geldsenke. Läuft nur für Gesetzlose MIT Faction-Eintrag (<see cref="Bandits.IsEstablished"/>) und nur mit Regel
/// <see cref="EconomyRules.BanditFactions"/>; sonst tut es nichts. Kein Zufall, feste Id-Reihenfolge — deterministisch.
/// Reihenfolge je Fraktion und Schritt: erst Unterhalt, dann Kauf/Nachbau (das Geld ist danach weg), zuletzt die Senke.
/// </summary>
public sealed class BanditSystem : ISimSystem
{
    /// <summary>Takt der Verwaltung (wie Wirtschafts-/Lebenszyklus-Takte); skaliert mit dt.</summary>
    public const int Interval = 50;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.BanditFactions) return;
        foreach (var a in u.Actors.Values.Where(a => a.Kind == ActorKind.Outlaw).OrderBy(a => a.Id.Value).ToList())
        {
            if (!Bandits.IsEstablished(u, a.Id)) continue;
            StepActor(u, a, dt);
        }
    }

    static void StepActor(Universe u, Actor a, int dt)
    {
        var r = u.Rules;
        var id = a.Id;
        var fid = new FactionId(id.Value);
        var seat = Bandits.Seat(u, id);
        if (seat is null) return;   // der Sitz ist unzerstörbar (Versteck: nur ausgeschaltet); FactionViability meldet nur NoHideout/CannotHunt

        // ── 1. Schutzflotte: Unterhalt, Kauf, Befehl ──
        var warships = u.ShipsById().Where(s => Wars.IsWarship(s) && s.Owner == id).ToList();
        float upkeep = r.BanditWarshipUpkeepPerTick * warships.Count * dt;
        if (upkeep > 0f)
        {
            float paid = MathF.Min(upkeep, MathF.Max(0f, a.Treasury.Credits));
            a.Treasury.Credits -= paid;
            u.Events.Publish(new TreasuryUpkeep(id, "bandit_fleet", paid, upkeep - paid));
        }
        // Kauf am Sitz (kein Materialverbrauch — die Bande kauft fertig, Muster PirateSystem.Book); teilt sich die Bremse
        // LastForceTick mit dem Kaperneubau (eine Kasse, ein Einkauf je Abklingzeit)
        if (warships.Count < r.BanditWarships && u.Tick - a.LastForceTick >= r.BanditFleetCooldownTicks
            && u.ShipModels.TryGetValue(r.BanditWarshipModel, out var model) && u.ShipClasses.TryGetValue(model.ClassKey, out var cls))
        {
            float cost = CombatModels.MaterialValue(u, model) * r.StationBuildCostFactor;
            if (a.Treasury.Credits - r.BanditWarshipReserve >= cost)
            {
                var ship = ShipPurchase.Create(u, u.NewShipId(), model.Key, cls.Slots, model.Fit, ShipRole.Warship, Controller.Ai, id, seat, 0f);
                ship.HomeBase = seat.Id;
                ship.DutySector = seat.Sector;
                a.Treasury.Credits -= cost;
                a.LastForceTick = u.Tick;
                u.AddShip(ship);
                u.Events.Publish(new TreasuryUpkeep(id, "bandit_build", cost, 0f));
                warships.Add(ship);
            }
        }
        // Befehle nur mit Flottenkampf (Phase 27): ohne ihn führen auch Polizei und Kaperer keine Befehle; die Schutzflotte liegt dann am Sitz
        // und verteidigt über Siege.Reinforce, was in ihrem Sektor angegriffen wird
        foreach (var s in r.FleetCombat ? warships : new List<Ship>())
        {
            if (!u.Ships.ContainsKey(s.Id) || u.Fights.IsEngaged(s.Id) || s.Emergency || s.Command is ReturnCommand) continue;
            // Alarm einer Wirtschaftsstation geht vor (wie die Polizei)
            if (EconomicHoldout.AlarmSectorFor(u, s) is { } alarm)
            {
                if (s.Command is not PatrolCommand ap || ap.Sectors.Count != 1 || ap.Sectors[0] != alarm)
                    Commands.Give(u, s, new PatrolCommand(new[] { alarm }), out _, ignoreCapacity: true);
                continue;
            }
            // Sitz anfliegen bei Schaden/leerer Munition/Modulschaden
            bool needsBase = s.Hull < r.RetreatHull * s.MaxHull
                             || (r.AmmoActive && s.Stats.AmmoCapacity > 0f && s.AmmoLoaded <= 0f)
                             || (r.DamageActive && NpcRepair.Crippled(u, s));
            if (needsBase) { Commands.Give(u, s, new ReturnCommand(), out _, ignoreCapacity: true); continue; }
            // sonst eigener Sektor mit der höchsten Bedrohung, sonst der Sitzsektor
            var target = u.Sectors.Values.Where(x => x.Owner == fid).OrderByDescending(x => x.Threat).ThenBy(x => x.Id.Value).FirstOrDefault()
                         ?? u.Sectors[seat.Sector];
            if (s.Command is PatrolCommand pc && pc.Sectors.Count == 1 && pc.Sectors[0] == target.Id) continue;
            Commands.Give(u, s, new PatrolCommand(new[] { target.Id }), out _, ignoreCapacity: true);
        }

        // ── 2. Wirtschaftsstationen nachbauen (höchstens ein Bau je Fraktion und Schritt, Reihenfolge der Typenliste) ──
        if (u.Tick - a.LastBanditTick >= r.BanditRebuildCooldownTicks && r.BanditEconomyTypes.Count > 0)
        {
            var have = new Dictionary<string, int>();
            foreach (var st in Bandits.Economic(u, id)) have[st.TypeKey] = have.GetValueOrDefault(st.TypeKey) + 1;
            foreach (var (typeKey, want) in r.BanditEconomyTypes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!u.StationTypes.TryGetValue(typeKey, out var type)) continue;
                if (have.GetValueOrDefault(typeKey) >= want) continue;
                if (Bandits.BuildIssue(u, a, type) is not null) continue;
                var sec = u.Sectors[seat.Sector];
                if (Bandits.Spot(u, sec, type.Radius, seat.Position) is not { } spot) continue;
                float cost = type.BuildCost * r.BanditRebuildMoneyFactor;
                a.Treasury.Credits -= cost;
                a.LastBanditTick = u.Tick;
                var stId = u.NewStationId();
                var st = StationFactory.Create(u, stId, type, StationFactory.BuiltName(u, type, sec.Id), sec.Id, spot, id);
                st.BankruptcyProtected = true;   // wie die Start-Werke (essential)
                // Betriebskapital für den ersten Einkauf (wie die Werke der Weltdatei) — Nachtrag 17b: aus der Bandenkasse, keine neue Geldquelle
                float seed = MathF.Min(4000f, MathF.Max(0f, a.Treasury.Credits));
                a.Treasury.Credits -= seed;
                st.Credits = seed;
                u.AddStation(st);
                u.Events.Publish(new StationBuilt(stId, sec.Id, type.Key));
                u.Events.Publish(new TreasuryUpkeep(id, "bandit_build", cost + seed, 0f));
                break;
            }
        }

        // ── 3. Vorposten in fremden, herrenlosen Sektoren (Sektor erobern; nie mit Spielerbesitz) ──
        if (r.BanditExpansion && r.BanditExpansionSectors.Count > 0 && u.Tick >= r.BanditExpansionAfterTicks && u.Tick - a.LastBanditTick >= r.BanditRebuildCooldownTicks
            && u.StationTypes.TryGetValue(r.BanditExpansionType, out var expType))
        {
            foreach (var sid in r.BanditExpansionSectors)
            {
                if (!u.Sectors.TryGetValue(new SectorId(sid), out var sec) || sec.Owner.Value != 0) continue;
                bool playerOwned = sec.Stations.Any(sid2 => u.Stations.TryGetValue(sid2, out var st2) && st2.Owner == u.PlayerActor)
                                   || PlayerSeats.Seats(u).Any(s2 => s2.Sector == sec.Id);
                if (playerOwned) continue;
                if (sec.Stations.Any(sid2 => u.Stations.TryGetValue(sid2, out var st2) && st2.Owner == id)) continue;   // nur ein Vorposten je Sektor
                float cost = expType.BuildCost * r.BanditExpansionMoneyFactor;
                if (a.Treasury.Credits < cost) continue;
                if (Bandits.Spot(u, sec, expType.Radius) is not { } spot) continue;
                a.Treasury.Credits -= cost;
                a.LastBanditTick = u.Tick;
                var stId = u.NewStationId();
                var st = StationFactory.Create(u, stId, expType, StationFactory.BuiltName(u, expType, sec.Id), sec.Id, spot, id);
                st.Credits = 0f;
                u.AddStation(st);
                u.Events.Publish(new StationBuilt(stId, sec.Id, expType.Key));
                u.Events.Publish(new TreasuryUpkeep(id, "bandit_build", cost, 0f));
                if (SectorControl.Update(u, sec)) u.TouchAccess();   // Einfluss > 0, bisheriger Besitzer 0 → sofortiger Wechsel
                break;
            }
        }

        // ── 4. Beuteanteil (Senke): über dem Kassenziel geht je Tick ein Anteil des Überschusses ab (kein Dauerhorten) ──
        if (r.BanditHoardShare > 0f && a.Treasury.Credits > r.BanditTreasuryTarget)
        {
            float share = (a.Treasury.Credits - r.BanditTreasuryTarget) * r.BanditHoardShare * dt;
            if (share > 0f)
            {
                a.Treasury.Credits -= share;
                u.Events.Publish(new TreasuryUpkeep(id, "bandit_share", share, 0f));
            }
        }
    }
}
