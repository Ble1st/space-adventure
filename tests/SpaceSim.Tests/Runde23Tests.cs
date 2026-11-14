using SpaceSim.Core.Content;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 23 (Handy-Test: „bester Abnehmer“ Gamma mit 66,4 genannt, 49,8 gezahlt, nur 3 von 22 verkauft): die ehrliche Abnehmer-Auskunft — angenommene Menge nach
/// der Verkaufsregel (Annahme, Kasse ÷ Preis), abzüglich dessen, was andere Händler schon bringen, Rangfolge nach erwartetem Erlös für die Menge.
/// </summary>
public class Runde23Tests
{
    static (Universe u, GoodId wafer, List<Station> buyers, Ship me) World()
    {
        var u = WorldBuilder.BuildDefault();
        new SimulationRunner().Run(u, 5);
        var wafer = u.FindGood("wafers")!.Id;
        var buyers = u.StationsById().Where(s => !s.IsBuildSite && s.Consumes(wafer) && s.Market.ContainsKey(wafer)).ToList();
        return (u, wafer, buyers, u.Ships[u.PlayerShip]);
    }

    [Fact]
    public void Abnehmer_NimmtNurWasErAnnimmtUndBezahlenKann()
    {
        var (u, wafer, buyers, me) = World();
        var st = buyers[0];
        var e = st.Market[wafer];
        e.Amount = 0f;   // viel Annahme
        st.Credits = e.Price * 3.5f;   // zahlt nur 3
        var q = MarketInfo.QuoteBuyer(u, st, wafer, 22f, me.Sector, me.Position);
        Assert.NotNull(q);
        Assert.Equal(3f, q!.Value.Units);
        Assert.Equal(3f * e.Price, q.Value.Revenue, 2);
        // mit der Verkaufsregel übereinstimmend
        var (cap, allCap) = TradeActions.SaleCaps(u, st, wafer, st.Credits);
        Assert.Equal(TradeActions.SaleUnits(22f, 22f, cap, allCap), q.Value.Units);
        st.Credits = 0f;
        Assert.Null(MarketInfo.QuoteBuyer(u, st, wafer, 22f, me.Sector, me.Position));   // kann nicht zahlen → fällt raus
    }

    [Fact]
    public void Abnehmer_ZiehtAbWasAndereHaendlerBringen()
    {
        var (u, wafer, buyers, me) = World();
        var st = buyers[0];
        var e = st.Market[wafer];
        st.Credits = 1_000_000f;
        float free = st.AcceptableUnits(wafer, u.Rules);
        var before = MarketInfo.QuoteBuyer(u, st, wafer, 9999f, me.Sector, me.Position)!.Value;
        var other = u.ShipsById().First(s => s.Role == ShipRole.Trader && !s.IsPlayer);
        other.Order = new TradeOrder(default, st.Id, wafer);
        other.Phase = TradePhase.GoingToSell;
        other.Cargo.Clear();
        other.Cargo[wafer] = MathF.Floor(free / 2f);
        var after = MarketInfo.QuoteBuyer(u, st, wafer, 9999f, me.Sector, me.Position);
        Assert.Equal(MathF.Floor(free / 2f), MarketInfo.IncomingTo(u, st.Id, wafer));
        Assert.True(after is null || after.Value.Units <= before.Units - MathF.Floor(free / 2f) + 1f, $"{before.Units} → {after?.Units}");
    }

    [Fact]
    public void Abnehmer_RangfolgeNachErwartetemErloesNichtNachStueckpreis()
    {
        var (u, wafer, buyers, me) = World();
        Assert.True(buyers.Count >= 2);
        var hi = buyers[0]; var lo = buyers[1];
        foreach (var b in buyers.Skip(2)) b.Credits = 0f;   // nur die beiden
        foreach (var s in u.StationsTrading(wafer).Where(s => s != hi && s != lo)) s.Credits = 0f;
        hi.Market[wafer].Amount = 0f; lo.Market[wafer].Amount = 0f;
        hi.Market[wafer].Price = 70f; lo.Market[wafer].Price = 60f;
        hi.Credits = 70f * 3f;   // zahlt nur 3 zum hohen Preis
        lo.Credits = 1_000_000f;
        var list = MarketInfo.HonestBuyers(u, wafer, 22f, me.Sector, me.Position);
        Assert.Equal(lo.Id, list[0].Station.Id);   // 22 × 60 schlägt 3 × 70
        Assert.Equal(22f, list[0].Units);
        Assert.Contains(list, q => q.Station.Id == hi.Id && q.Units == 3f);
        // für eine Menge, die beide ganz nehmen, gewinnt der höhere Preis
        Assert.Equal(hi.Id, MarketInfo.BestHonestBuyer(u, wafer, 2f, me.Sector, me.Position)!.Value.Station.Id);
    }
}
