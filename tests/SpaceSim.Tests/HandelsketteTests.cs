using System.Text;
using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;
using Xunit.Abstractions;

namespace SpaceSim.Tests;

/// <summary>
/// Auftrag des Nutzers „Einmal Test der gesamten Handelskette mit Selberhandel des Spielers“ (Standardwelt, Schalter wie Standard): (1) die NPC-Wirtschaft
/// fördert, handelt und verbraucht bzw. exportiert jede Kettenware; (2) der Spieler handelt die ganze Kette selbst — ausschließlich über den Dienst „trade“,
/// den auch die Handelsansicht benutzt (Angebote aus <see cref="IStationService.Offers"/>, Ausführung über <see cref="IStationService.Execute"/>); Erz baut er mit
/// der Kontaktabbau-Regel des Spielerpfads (<see cref="OreChunks.Contact"/>) ab; (3) seine Flottenautomatik handelt und schürft auf die Kasse; (4) gleicher Seed →
/// gleiche Tabelle. Die Tabellen stehen im Testprotokoll; der Test schlägt nur bei echten Regelverletzungen fehl (Kettenware ohne Hersteller oder Abnehmer,
/// Bilanzfehler, Angebot nicht ausführbar, Laderaum über Kapazität, Spielerschiff mit eigenem Geld).
/// </summary>
public class HandelsketteTests
{
    readonly ITestOutputHelper _o;
    public HandelsketteTests(ITestOutputHelper o) => _o = o;

    static readonly IStationService Trade = new TradeService();

    /// <summary>Kettenwaren = alle Waren der Weltdatei außer Modulgütern und dem (abgeschalteten) Treibstoff.</summary>
    static List<Good> Chain(Universe u) =>
        u.Goods.Values.Where(g => g.Category != "module" && !(g.Category == "fuel" && !u.Rules.FuelActive)).OrderBy(g => g.Id.Value).ToList();

    static List<Good> Modules(Universe u) => u.Goods.Values.Where(g => g.Category == "module").OrderBy(g => g.Id.Value).ToList();

    static bool HasProducer(Universe u, GoodId g) => u.Stations.Values.Any(s => !s.IsBuildSite && s.Produces(g));
    static bool HasTaker(Universe u, GoodId g) => u.Stations.Values.Any(s => !s.IsBuildSite && s.Market.ContainsKey(g) && !(s.Produces(g) && !s.Consumes(g)));

    // ───────── 1. NPC-Seite ─────────

    sealed class NpcRow
    {
        public float Mined, FromProducer, Units, ToConsumer, ToShelf, Exported; public int Trips;
    }

    [Fact]
    public void Kette_NpcSeite_FoerdertHandeltVerbrauchtExportiert()
    {
        var u = WorldBuilder.BuildDefault();
        var rows = Chain(u).Concat(Modules(u)).ToDictionary(g => g.Id, _ => new NpcRow());
        int fleetBuilt = 0, purchased = 0; float ammoRearmed = 0f; int missiles = 0;
        var exportedAt = new Dictionary<StationId, float>();
        u.Events.Raised += e =>
        {
            switch (e)
            {
                case TradeCompleted t when rows.TryGetValue(t.Good, out var r):
                    r.Trips++; r.Units += t.Amount;
                    if (u.Stations.TryGetValue(t.From, out var from) && from.Produces(t.Good)) r.FromProducer += t.Amount;
                    if (u.Stations.TryGetValue(t.To, out var to)) { if (to.Consumes(t.Good)) r.ToConsumer += t.Amount; else if (to.IsStockOnly(t.Good)) r.ToShelf += t.Amount; }
                    break;
                case MineDelivered m when rows.TryGetValue(m.Good, out var r):
                    r.Trips++; r.Units += m.Amount; r.Mined += m.Amount;
                    if (u.Stations.TryGetValue(m.To, out var mto) && mto.Consumes(m.Good)) r.ToConsumer += m.Amount;
                    break;
                case GoodsExported x when rows.TryGetValue(x.Good, out var r):
                    r.Exported += x.Amount; exportedAt[x.Station] = exportedAt.GetValueOrDefault(x.Station) + x.Amount; break;
                case FleetShipBuilt: fleetBuilt++; break;
                case ShipPurchased: purchased++; break;
                case AmmoRearmed a: ammoRearmed += a.Units; break;
                case SpaceSim.Core.Events.MissileLaunched: missiles++; break;
            }
        };
        new SimulationRunner().Run(u, 20000);

        var sb = new StringBuilder();
        sb.AppendLine("NPC-Kette (Standardwelt, Standard-Seed, 20000 Ticks)");
        sb.AppendLine($"{"Ware",-26}{"Kat.",-10}{"gefördert",10}{"ab Herst.",10}{"Fahrten",8}{"Einheiten",10}{"→Verbr.",9}{"→Regal",8}{"Export",8}");
        var findings = new List<string>();
        foreach (var g in Chain(u).Concat(Modules(u)))
        {
            var r = rows[g.Id];
            sb.AppendLine($"{g.Name,-26}{g.Category,-10}{r.Mined,10:0}{r.FromProducer,10:0}{r.Trips,8}{r.Units,10:0}{r.ToConsumer,9:0}{r.ToShelf,8:0}{r.Exported,8:0}");
            // echte Regelverletzungen: Kettenware ohne Hersteller (Erze: ohne Felsklasse) oder ohne jeden Abnehmer
            bool ore = g.Category == "ore";
            if (ore) Assert.True(u.AsteroidClasses.Values.Any(c => c.Yields.Any(y => y.Good == g.Id)), $"{g.Name}: keine Felsklasse fördert es");
            else if (g.Category != "module") Assert.True(HasProducer(u, g.Id), $"{g.Name}: kein Hersteller in der Welt");
            // Bausätze werden an der Werft selbst verbaut (Schiffskauf aus dem Werftlager) — ein fremder Abnehmer ist kein Muss
            if (g.Category is not "module" and not "kit") Assert.True(HasTaker(u, g.Id), $"{g.Name}: kein Abnehmer in der Welt");
            if (g.Category == "kit" && !HasTaker(u, g.Id)) findings.Add($"{g.Name}: kein Abnehmer außer der Werft selbst (wird dort verbaut)");
            // Befunde (kein Fehlschlag): Ware ohne Fahrten / ohne Verbrauch
            if (g.Category is "ore" or "refined" or "component" or "end")
            {
                Assert.True(r.Trips > 0, $"{g.Name}: in 20000 Ticks keine einzige Fahrt");
                Assert.True(r.ToConsumer + r.Exported > 0f, $"{g.Name}: weder verbraucht noch exportiert");
            }
            else if (r.Trips == 0 && g.Category != "kit") findings.Add($"{g.Name} ({g.Category}): keine NPC-Fahrt in 20000 Ticks");
        }
        sb.AppendLine($"Bausätze: KI-Neubauten {fleetBuilt}, Käufe {purchased} (Bausätze werden an der Werft verbaut, nicht gehandelt); Munition nachgeladen {ammoRearmed:0}; Raketen gestartet {missiles}");
        var sinks = u.StationsById().Where(s => ExportRules.ConfigOf(u, s) is not null).ToList();
        foreach (var s in sinks) sb.AppendLine($"Exportsenke {s.Name}: exportiert {exportedAt.GetValueOrDefault(s.Id):0} Einheiten");
        Assert.Equal(4, sinks.Count);
        Assert.All(sinks, s => Assert.True(exportedAt.GetValueOrDefault(s.Id) > 0f, $"{s.Name} hat nichts exportiert"));
        foreach (var f in findings) sb.AppendLine("BEFUND: " + f);
        _o.WriteLine(sb.ToString());
    }

    // ───────── 2. Spieler handelt die Kette selbst ─────────

    sealed class Trip
    {
        public string Good = "", BuyAt = "", SellAt = "", Note = "";
        public float BuyPrice, SellPrice, Units, Profit; public bool Bought, Sold;
    }

    sealed class Run
    {
        public readonly List<Trip> Trips = new();
        public readonly List<string> Findings = new();
        public readonly List<string> Violations = new();
        public string Table = "";
    }

    static ServiceOffer? Find(Universe u, Station st, Ship ship, string key)
    {
        foreach (var o in Trade.Offers(u, st, ship)) if (o.Key == key) return o;
        return null;
    }

    static void DockAt(Universe u, Ship s, Station st)
    {
        if (s.AnchorRock != 0) MiningAnchor.Release(u, s, s.Sector, "released");
        if (s.Transit is not null) u.FinishTransit(s);
        if (s.Sector != st.Sector) u.MoveShip(s, st.Sector);
        u.PlayerSector = st.Sector;
        s.Position = st.DockPoint(s.Id);
        s.DockedAt = st.Id;
    }

    /// <summary>Ein Angebot mit Menge &gt; 0 ausführen und die Regeln prüfen: genau diese Menge bewegt, Geld erhalten, Spielerschiff ohne Geld, Laderaum.</summary>
    static ServiceResult Exec(Universe u, Station st, Ship ship, ServiceOffer o, float units, Run run)
    {
        u.PlayerWallet.Credits = 100_000f;   // Kasse vor jedem Handel auffüllen (außerhalb der Bilanz; float-Genauigkeit bei kleiner Summe)
        double before = (double)u.PlayerWallet.Credits + st.Credits;
        float heldBefore = ship.Cargo.GetValueOrDefault(o.Good);
        var r = Trade.Execute(u, st, ship, new ServiceRequest(o.Key, o.Good, units));
        double after = (double)u.PlayerWallet.Credits + st.Credits;
        if (!r.Success) run.Violations.Add($"Angebot {o.Key} bei {st.Name} (Menge {units:0.##} ≤ {o.Amount:0.##}) nicht ausführbar: {r.Message}");
        else
        {
            if (MathF.Abs(r.Units - units) > 0.001f) run.Violations.Add($"{o.Key} bei {st.Name}: {r.Units:0.###} statt {units:0.###} bewegt");
            if (MathF.Abs(MathF.Abs(ship.Cargo.GetValueOrDefault(o.Good) - heldBefore) - units) > 0.001f) run.Violations.Add($"{o.Key} bei {st.Name}: Laderaum änderte sich nicht um {units:0.###}");
        }
        if (Math.Abs(after - before) > 0.02) run.Violations.Add($"{o.Key} bei {st.Name}: Kasse + Stationskasse {before:0.##} → {after:0.##}");
        if (ship.Credits != 0f) run.Violations.Add($"Spielerschiff hat eigenes Geld ({ship.Credits:0.##})");
        if (ship.UsedVolume(u) > ship.CargoCapacity + 0.001f) run.Violations.Add($"Laderaum über Kapazität ({ship.UsedVolume(u):0.##} > {ship.CargoCapacity:0})");
        return r;
    }

    /// <summary>Alles an Bord loswerden: beim besten Abnehmer verkaufen, Rest abwerfen (Abwurf-Angebot des Handelsdienstes).</summary>
    static void Unload(Universe u, Ship ship, Run run)
    {
        foreach (var good in ship.Cargo.Where(kv => kv.Value > 0f).Select(kv => kv.Key).OrderBy(g => g.Value).ToList())
        {
            if (MarketInfo.BestBuyerForGood(u, good) is { } b)
            {
                DockAt(u, ship, b);
                if (Find(u, b, ship, $"sell:{good.Value}") is { Amount: > 0f } so) Exec(u, b, ship, so, so.Amount, run);
            }
            if (ship.Cargo.GetValueOrDefault(good) > 0f && ship.DockedAt.Value != 0 && u.Stations.TryGetValue(ship.DockedAt, out var here)
                && Find(u, here, ship, $"drop:{good.Value}") is { Amount: > 0f } d)
                Trade.Execute(u, here, ship, new ServiceRequest(d.Key, good, 0f));
        }
    }

    Run PlayerChain(ulong seed)
    {
        var run = new Run();
        var u = WorldBuilder.BuildDefault(seed, "anchor");   // Goldsucher mit Bohrkopf, Laderaum 500 (universal), ruhig
        var me = u.Ships[u.PlayerShip];
        var runner = new SimulationRunner();
        runner.Run(u, 200);   // Preise stehen erst nach dem ersten Takt
        var ev = new List<SimEvent>();
        u.Events.Raised += ev.Add;

        // ── Erz: jede Felsklasse mit der Kontaktabbau-Regel des Spielers, jedes Erz an eine annehmende Hütte ──
        var chunks = new OreChunks(seed: seed + 7);
        var oreSold = new HashSet<GoodId>();
        foreach (var cls in u.AsteroidClasses.Values.Where(c => c.Yields.Count > 0).OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            var rock = u.Sectors.Values.OrderBy(s => s.Id.Value).SelectMany(s => s.Asteroids.Where(a => a.Minable && a.ClassKey == cls.Key).Select(a => (s, a)))
                .OrderByDescending(x => x.a.Ore).FirstOrDefault();
            if (rock.a is null) { run.Findings.Add($"Felsklasse {cls.Name}: kein abbaubarer Fels in der Welt"); continue; }
            var (sector, a) = rock;
            if (me.Sector != sector.Id) u.MoveShip(me, sector.Id);
            u.PlayerSector = sector.Id; me.DockedAt = default;
            var dir = new Vec2(1f, 0f);
            var pos = a.Position - dir * (a.Radius + ContactMining.BowOffset + 1f);
            me.Position = pos;
            var want = cls.Yields.Where(y => y.Weight > 0f).Select(y => y.Good).ToHashSet();
            for (int i = 0; i < 20 * 600 && !want.All(g => me.Cargo.GetValueOrDefault(g) >= 1f) && a.Minable; i++)
            {
                chunks.Contact(u, me, sector, pos, dir, 0.05f);
                if (me.AnchorRock != 0) { pos = MiningAnchor.HoldPosition(pos, a, Collision.ShipRadius); dir = (a.Position - pos).Normalized(); }
                if (me.UsedVolume(u) > me.CargoCapacity + 0.001f) run.Violations.Add("Laderaum beim Abbau über Kapazität");
            }
            foreach (var g in want.Where(g => me.Cargo.GetValueOrDefault(g) < 1f)) run.Findings.Add($"Felsklasse {cls.Name}: {u.Goods[g].Name} nicht gefördert");
            foreach (var good in me.Cargo.Where(kv => kv.Value > 0f).Select(kv => kv.Key).OrderBy(g => g.Value).ToList())
            {
                var t = new Trip { Good = u.Goods[good].Name, BuyAt = $"Abbau {cls.Name}", Bought = true, Units = me.Cargo[good] };
                var b = MarketInfo.BestBuyerForGood(u, good);
                if (b is null) { t.Note = "kein annehmender Abnehmer"; run.Findings.Add($"{t.Good}: niemand nimmt es an"); run.Trips.Add(t); continue; }
                DockAt(u, me, b);
                if (Find(u, b, me, $"sell:{good.Value}") is not { Amount: > 0f } so) { t.Note = Find(u, b, me, $"sell:{good.Value}")?.Note ?? "kein Verkaufsangebot"; run.Trips.Add(t); continue; }
                var r = Exec(u, b, me, so, so.Amount, run);
                if (r.Success) { t.Sold = true; t.SellAt = b.Name; t.SellPrice = r.Credits / MathF.Max(r.Units, 0.001f); t.Profit = r.Credits; t.Units = r.Units; oreSold.Add(good); }
                run.Trips.Add(t);
            }
            Unload(u, me, run);
        }

        // ── jede Kettenstufe: beim Hersteller kaufen, beim besten Abnehmer verkaufen ──
        foreach (var g in Chain(u).Where(g => g.Category != "ore"))
        {
            var t = new Trip { Good = g.Name };
            var producers = u.StationsById().Where(s => !s.IsBuildSite && s.Produces(g.Id)).ToList();
            Station? at = null; ServiceOffer? bo = null;
            foreach (var p in producers.OrderByDescending(p => p.Market.TryGetValue(g.Id, out var e) ? e.Amount : 0f).ThenBy(p => p.Id.Value))
                if (Find(u, p, me, $"buy:{g.Id.Value}") is { Amount: >= 1f } o) { at = p; bo = o; break; }
            if (at is null || bo is null)
            {
                string why = producers.Count == 0 ? "kein Hersteller" : producers.Select(p => Find(u, p, me, $"buy:{g.Id.Value}")).FirstOrDefault(o => o is not null)?.Note ?? "Hersteller ohne Bestand";
                t.Note = $"nicht kaufbar: {why}"; run.Findings.Add($"{g.Name}: nirgends kaufbar ({why})"); run.Trips.Add(t); continue;
            }
            DockAt(u, me, at);
            float units = MathF.Min(bo.Value.Amount, MathF.Max(1f, MathF.Floor(60f / MathF.Max(0.1f, g.VolumePerUnit))));
            var br = Exec(u, at, me, bo.Value, units, run);
            if (!br.Success) { t.Note = "Kauf fehlgeschlagen"; run.Trips.Add(t); continue; }
            t.Bought = true; t.BuyAt = at.Name; t.Units = br.Units; t.BuyPrice = MathF.Abs(br.Credits) / br.Units;
            var buyer = MarketInfo.BestBuyerForGood(u, g.Id, at.Id);
            if (buyer is null)
            {
                // Grund an einem Halter der Ware nennen (erwartete Verweigerung)
                var holder = u.StationsById().Where(s => s.Id != at.Id && s.Market.ContainsKey(g.Id)).OrderBy(s => s.Produces(g.Id) ? 1 : 0).FirstOrDefault();   // ein Abnehmer vor anderen Herstellern
                t.Note = holder is null ? "niemand sonst handelt die Ware" : $"{holder.Name}: {MarketInfo.Acceptance(u, holder, g.Id).Reason}";
                run.Findings.Add($"{g.Name}: nirgends absetzbar ({t.Note})");
                Unload(u, me, run); run.Trips.Add(t); continue;
            }
            DockAt(u, me, buyer);
            if (Find(u, buyer, me, $"sell:{g.Id.Value}") is { Amount: > 0f } so)
            {
                var sr = Exec(u, buyer, me, so, so.Amount, run);
                if (sr.Success) { t.Sold = true; t.SellAt = buyer.Name; t.SellPrice = sr.Credits / sr.Units; t.Profit = sr.Credits - t.BuyPrice * sr.Units; if (sr.Units < t.Units - 0.001f) t.Note = $"nimmt nur {sr.Units:0.##}"; }
            }
            else t.Note = Find(u, buyer, me, $"sell:{g.Id.Value}")?.Note ?? "kein Verkaufsangebot";
            // Preisfaktoren am Deckel/Boden
            foreach (var (st, price) in new[] { (at, t.BuyPrice), (buyer, t.SellPrice) })
                if (price > 0f && (price >= g.BasePrice * 3.9f || price <= g.BasePrice * 0.27f)) run.Findings.Add($"{g.Name}: Preis {price:0.#} bei {st.Name} = {price / g.BasePrice:0.00}× Basis (Deckel/Boden)");
            Unload(u, me, run);
            run.Trips.Add(t);
        }

        // ── Endwaren an alle Planetensenken: Exportcharge entsteht und wird nach der Frist mit Aufschlag exportiert ──
        var queued = new List<ExportQueued>();
        bool collect = true;
        u.Events.Raised += e => { if (collect && e is ExportQueued q && q.Station == me.DockedAt) queued.Add(q); };
        var sinks = u.StationsById().Where(s => ExportRules.ConfigOf(u, s) is not null).ToList();
        foreach (var sink in sinks)
            foreach (var good in ExportRules.ConfigOf(u, sink)!.Goods.OrderBy(x => x.Value))
            {
                var t = new Trip { Good = $"{u.Goods[good].Name} → Export" };
                var p = u.StationsById().Where(s => !s.IsBuildSite && s.Produces(good)).FirstOrDefault(s => Find(u, s, me, $"buy:{good.Value}") is { Amount: >= 1f });
                if (p is null) { t.Note = "nicht kaufbar"; run.Trips.Add(t); continue; }
                DockAt(u, me, p);
                var bo2 = Find(u, p, me, $"buy:{good.Value}")!.Value;
                var br = Exec(u, p, me, bo2, MathF.Min(bo2.Amount, 3f), run);
                if (!br.Success) continue;
                t.Bought = true; t.BuyAt = p.Name; t.Units = br.Units; t.BuyPrice = MathF.Abs(br.Credits) / br.Units;
                DockAt(u, me, sink);
                // erwartete Verweigerung an der Senke: Kauf „Nur Ankauf (Export)“
                if (Find(u, sink, me, $"buy:{good.Value}") is { } nb && nb.Amount > 0f) run.Violations.Add($"Exportsenke {sink.Name} verkauft {u.Goods[good].Name}");
                if (Find(u, sink, me, $"sell:{good.Value}") is { Amount: > 0f } so)
                {
                    var sr = Exec(u, sink, me, so, so.Amount, run);
                    if (sr.Success) { t.Sold = true; t.SellAt = sink.Name; t.SellPrice = sr.Credits / sr.Units; t.Profit = sr.Credits - t.BuyPrice * sr.Units; }
                }
                else t.Note = Find(u, sink, me, $"sell:{good.Value}")?.Note ?? "kein Verkaufsangebot";
                Unload(u, me, run);
                run.Trips.Add(t);
            }
        collect = false;   // nur die Chargen dieses Abschnitts (ein späterer Restverkauf an eine Senke zählt nicht)
        int maxDelay = queued.Count == 0 ? 0 : (int)(queued.Max(q => q.DueTick) - u.Tick);   // Frist je Senke aus ihrem Typ
        var exported = new List<GoodsExported>();
        u.Events.Raised += e => { if (e is GoodsExported x) exported.Add(x); };
        runner.Run(u, maxDelay + 20);
        foreach (var q in queued)
        {
            float markup = ExportRules.ConfigOf(u, u.Stations[q.Station])!.Markup ?? u.Rules.ExportMarkup;
            var x = exported.FirstOrDefault(x => x.Station == q.Station && x.Good == q.Good && MathF.Abs(x.Paid - q.Paid) < 0.01f);
            if (x is null) run.Violations.Add($"Exportcharge {u.Goods[q.Good].Name} bei {u.Stations[q.Station].Name} ({q.Amount:0.##} Stück, bezahlt {q.Paid:0.##}, fällig {q.DueTick}) nicht exportiert; Exporte dort: "
                + string.Join(", ", exported.Where(e => e.Station == q.Station && e.Good == q.Good).Select(e => $"{e.Amount:0.##}/{e.Paid:0.##}")) + $"; Lager {u.Stations[q.Station].Market.GetValueOrDefault(q.Good)?.Amount:0.##}, Tick {u.Tick}");
            else if (MathF.Abs(x.Revenue - x.Paid * (1f + markup)) > 0.05f) run.Violations.Add($"Export {u.Goods[q.Good].Name}: Erlös {x.Revenue:0.##} ≠ {x.Paid:0.##} × {1f + markup:0.00}");
        }
        if (queued.Select(q => q.Station).Distinct().Count() != sinks.Count) run.Violations.Add($"nicht an allen {sinks.Count} Planetensenken exportiert ({queued.Select(q => q.Station).Distinct().Count()})");

        // ── ein Modulgut bei einer Modulfabrik kaufen und an eine Werft verkaufen ──
        bool moduleDone = false;
        foreach (var g in Modules(u))
        {
            var p = u.StationsById().FirstOrDefault(s => s.Produces(g.Id) && Find(u, s, me, $"buy:{g.Id.Value}") is { Amount: >= 1f });
            var yard = u.StationsById().FirstOrDefault(s => s.TypeKey == "shipyard" && MarketInfo.Acceptance(u, s, g.Id).Accepts);
            if (p is null || yard is null) continue;
            DockAt(u, me, p);
            var br = Exec(u, p, me, Find(u, p, me, $"buy:{g.Id.Value}")!.Value, 1f, run);
            DockAt(u, me, yard);
            if (Find(u, yard, me, $"sell:{g.Id.Value}") is { Amount: > 0f } so)
            {
                var sr = Exec(u, yard, me, so, so.Amount, run);
                run.Trips.Add(new Trip { Good = $"{g.Name} (Modul)", Bought = true, BuyAt = p.Name, BuyPrice = MathF.Abs(br.Credits), Units = 1f, Sold = sr.Success, SellAt = yard.Name, SellPrice = sr.Credits, Profit = sr.Credits - MathF.Abs(br.Credits) });
                moduleDone = sr.Success;
                Unload(u, me, run);
                break;
            }
            Unload(u, me, run);
        }
        if (!moduleDone) run.Findings.Add("kein Modulgut von einer Modulfabrik an eine Werft verkaufbar");

        // ── Tabelle ──
        var sb = new StringBuilder();
        sb.AppendLine($"Spieler-Rundreisen (Seed {seed}, nur Dienst „trade“)");
        sb.AppendLine($"{"Ware",-30}{"gekauft bei",-34}{"Preis",8}{"Menge",7}  {"verkauft an",-34}{"Preis",8}{"Gewinn",9}  Hinweis");
        foreach (var t in run.Trips)
            sb.AppendLine($"{t.Good,-30}{(t.Bought ? t.BuyAt : "—"),-34}{(t.Bought && t.BuyPrice > 0f ? t.BuyPrice.ToString("0.0") : ""),8}{t.Units,7:0.#}  {(t.Sold ? t.SellAt : "—"),-34}{(t.Sold ? t.SellPrice.ToString("0.0") : ""),8}{(t.Sold ? t.Profit.ToString("0") : ""),9}  {t.Note}");
        sb.AppendLine($"Erze verkauft: {oreSold.Count} von {Chain(u).Count(g => g.Category == "ore")}");
        foreach (var f in run.Findings) sb.AppendLine("BEFUND: " + f);
        foreach (var v in run.Violations) sb.AppendLine("VERLETZUNG: " + v);
        run.Table = sb.ToString();
        if (oreSold.Count != Chain(u).Count(g => g.Category == "ore")) run.Violations.Add($"nur {oreSold.Count} Erze verkauft");
        return run;
    }

    [Fact]
    public void Kette_SpielerHandeltSelbst_UeberDenHandelsdienst()
    {
        var run = PlayerChain(1);
        _o.WriteLine(run.Table);
        Assert.Empty(run.Violations);
        // jede Kettenware (ohne Erze, die gefördert werden) wurde mindestens einmal gekauft; verkauft, wo nicht erwartete Verweigerung
        Assert.All(run.Trips.Where(t => !t.Good.Contains("→") && !t.Good.Contains("(Modul)")), t => Assert.True(t.Bought || t.Note.Length > 0, $"{t.Good}: weder gekauft noch Grund"));
    }

    [Fact]
    public void Kette_GleicherSeed_GleicheTabelle()
    {
        Assert.Equal(PlayerChain(3).Table, PlayerChain(3).Table);
    }

    // ───────── 3. Flottenautomatik des Spielers ─────────

    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        return s;
    }

    [Fact]
    public void Kette_Flottenautomatik_HandeltUndSchuerftAufDieKasse()
    {
        var u = WorldBuilder.BuildDefault(5, "anchor");
        new SimulationRunner().Run(u, 50);
        var beta = u.StationsById().First(s => s.Name == "Handelsstation Beta");
        var alpha = u.StationsById().First(s => s.Sector == new SectorId(1) && s.Dockable && s.Market.Count > 0);
        var trader = Own(u, "mule", beta);
        var miner = Own(u, "prospector", alpha);
        Assert.True(Commands.Give(u, trader, new TradeCommand(AutoLimits.None), out var m1), m1);
        Assert.True(Commands.Give(u, miner, new MineCommand(new AutoLimits(new SectorId(1), 1, 0f, 0f)), out var m2), m2);
        float start = u.PlayerWallet.Credits;
        var ev = new List<SimEvent>();
        u.Events.Raised += ev.Add;
        new SimulationRunner().Run(u, 8000);
        int traderSales = ev.OfType<TradeCompleted>().Count(t => t.Ship == trader.Id) + ev.OfType<PlayerSold>().Count(t => t.Ship == trader.Id);
        int minerSales = ev.OfType<PlayerSold>().Count(t => t.Ship == miner.Id);
        float traderIn = ev.OfType<TradeCompleted>().Where(t => t.Ship == trader.Id).Sum(t => t.Revenue) + ev.OfType<PlayerSold>().Where(t => t.Ship == trader.Id).Sum(t => t.Revenue);
        float minerIn = ev.OfType<PlayerSold>().Where(t => t.Ship == miner.Id).Sum(t => t.Revenue);
        _o.WriteLine($"Flottenautomatik 8000 Ticks: Händler {traderSales} Verkäufe (Umsatz {traderIn:0}), Schürfer {minerSales} Verkäufe (Erlös {minerIn:0}); Kasse {start:0} → {u.PlayerWallet.Credits:0}");
        Assert.True(traderSales > 0 && minerSales > 0);
        Assert.Equal(0f, trader.Credits);
        Assert.Equal(0f, miner.Credits);
        Assert.True(u.PlayerWallet.Credits > start, $"Gewinn auf der Kasse ({start:0} → {u.PlayerWallet.Credits:0})");
    }
}
