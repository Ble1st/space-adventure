using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.PlayerBot;

/// <summary>Test 8, Schwerpunkt 2: Warum Notbetankung? Läuft 6000 Ticks auf einem Bot-Stand und protokolliert jede Notbetankung/jeden Treibstoffkauf der Spielerflotte
/// mit Schiff, Sektor, Tank, Wallet, nächster Raffinerie (Weg in Sprüngen, Bestand, Preis) und fasst nach Modell/Sektor zusammen.</summary>
public static class ProbeFuel
{
    public static void Run(Universe u, Pilot p, Action<string> P, long ticks)
    {
        var emer = new List<string>(); var byKey = new Dictionary<string, (int n, float cost, float units)>(); var buy = new Dictionary<string, (int n, float cost, float units)>();
        float walletMin = float.MaxValue; int walletZero = 0;
        u.Events.Raised += e =>
        {
            if (e is EmergencyRefuel er && u.Ships.TryGetValue(er.Ship, out var s) && s.Owner == u.PlayerActor)
            {
                var k = $"{s.ModelKey} in {u.Sectors[s.Sector].Name} cmd={s.Command?.GetType().Name ?? "-"}";
                var o = byKey.GetValueOrDefault(k); byKey[k] = (o.n + 1, o.cost + er.Cost, o.units + er.Units);
                if (emer.Count < 40)
                {
                    var good = er.Good;
                    var refs = u.StationsById().Where(x => x.Market.ContainsKey(good) && x.Produces(good)).Select(x => (st: x, hops: u.Hops(s.Sector, x.Sector))).Where(x => x.hops >= 0).OrderBy(x => x.hops).Take(2).ToList();
                    emer.Add($"[{u.Tick}] #{s.Id.Value} {k} Tank {s.TotalFuel:0}/{s.Stats.FuelCapacity:0} kauft {er.Units:0} E für {er.Cost:0} Cr, Kasse {u.PlayerWallet.Credits:0}; nächste Raffinerien: {string.Join(" | ", refs.Select(r => $"{r.st.Name} {r.hops} Sprünge Bestand {r.st.Market[good].Amount:0} Preis {r.st.Market[good].Price:0.0}"))}");
                }
            }
            if (e is FuelBought fb && u.Ships.TryGetValue(fb.Ship, out var fs) && fs.Owner == u.PlayerActor)
            {
                var k = $"{fs.ModelKey}"; var o = buy.GetValueOrDefault(k); buy[k] = (o.n + 1, o.cost + fb.Cost, o.units + fb.Units);
            }
        };
        for (long i = 0; i < ticks; i++)
        {
            p.Step();
            float w = u.PlayerWallet.Credits; walletMin = Math.Min(walletMin, w); if (w < 100) walletZero++;
        }
        P($"== Treibstoff-Ursachen, {ticks} Ticks ab Tick {u.Tick - ticks}: Kasse min {walletMin:0}, Takte mit Kasse < 100: {walletZero}");
        P("Notbetankung nach Modell/Sektor/Befehl:");
        foreach (var kv in byKey.OrderByDescending(k => k.Value.cost).Take(15)) P($"  {kv.Key}: {kv.Value.n}x {kv.Value.units:0} E {kv.Value.cost:0} Cr (Ø {kv.Value.cost / Math.Max(1, kv.Value.units):0.0}/E)");
        P("Normale Käufe nach Modell:");
        foreach (var kv in buy.OrderByDescending(k => k.Value.cost).Take(12)) P($"  {kv.Key}: {kv.Value.n}x {kv.Value.units:0} E {kv.Value.cost:0} Cr (Ø {kv.Value.cost / Math.Max(1, kv.Value.units):0.0}/E)");
        P("Erste Notbetankungen:"); foreach (var l in emer.Take(25)) P("  " + l);
        P("Raffinerien (Treibstoff): " + string.Join("; ", u.StationsById().Where(x => x.TypeKey == "fuel_refinery").Select(x => $"{x.Name} [{u.Sectors[x.Sector].Name}, {(x.Owner == u.PlayerActor ? "eigen" : "fremd")}]")));
    }
}
