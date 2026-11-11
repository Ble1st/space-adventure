using System.Text.Json.Nodes;
using SpaceSim.Core.Content;
using SpaceSim.Core.Demo;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;

namespace SpaceSim.Tests;

public class StationServiceConfigTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Standardwelt, bei der ein Stationstyp eine explizite services-Liste bekommt.</summary>
    static string WorldWithServices(string typeKey, string servicesJson) =>
        WorldBuilder.DefaultWorldJson().Replace($"\"key\": \"{typeKey}\"", $"\"key\": \"{typeKey}\", \"services\": {servicesJson}");

    sealed class Stub : IStationService
    {
        public Stub(string key, bool available) { Key = key; Available = available; }
        public string Key { get; }
        public bool Available { get; }
        public bool IsAvailable(Universe u, Station station) => Available;
        public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship) => Array.Empty<ServiceOffer>();
        public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request) => new(false, 0f, 0f, "");
    }

    [Fact]
    public void DefaultWorld_WithoutServicesField_GetsTradeAsStandard()   // Phase 4c: Raffinerie/Panzerwerk/Werft führen explizite Listen
    {
        var u = WorldBuilder.BuildDefault();
        Assert.NotEmpty(u.StationTypes);
        foreach (var t in u.StationTypes.Values)
        {
            if (t.Key is "fuel_refinery") Assert.Equal(new[] { "trade", "refuel" }, t.Services);
            else if (t.Key is "plating_works") Assert.Equal(new[] { "trade", "repair" }, t.Services);
            else if (t.Key is "shipyard") Assert.Equal(new[] { "trade", "repair", "outfitter", "shipdealer", "upgrade" }, t.Services);   // Phase 5c, Phase 7
            else if (t.Key is "trade_station") Assert.Equal(new[] { "trade", "outfitter", "shipdealer" }, t.Services);       // Phase 5c
            else if (t.Key is "warehouse") Assert.Equal(new[] { "trade", "storage" }, t.Services);                            // Phase 8
            else Assert.Equal(new[] { TradeService.ServiceKey }, t.Services);
        }
    }

    [Fact]
    public void NewServiceKeys_RefuelAndRepair_AreAccepted()
    {
        var u = WorldBuilder.Build(WorldBuilder.Parse(WorldWithServices("solar_plant", "[\"trade\", \"refuel\", \"repair\"]")));
        Assert.Equal(new[] { "trade", "refuel", "repair" }, u.StationTypes["solar_plant"].Services);
    }

    [Fact]
    public void ExplicitServices_AreTakenOver_AndEmptyListStaysEmpty()
    {
        var u = WorldBuilder.Build(WorldBuilder.Parse(WorldWithServices("solar_plant", "[]")));
        Assert.Empty(u.StationTypes["solar_plant"].Services);
        Assert.Equal(new[] { TradeService.ServiceKey }, u.StationTypes["steelworks"].Services);
    }

    [Fact]
    public void UnknownServiceKey_ThrowsWorldFormatException_WithTypeAndKey()
    {
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(WorldBuilder.Parse(WorldWithServices("solar_plant", "[\"trade\", \"hyperdrive\"]"))));
        Assert.Contains("solar_plant", ex.Message);
        Assert.Contains("hyperdrive", ex.Message);
    }

    [Fact]
    public void DuplicateServiceKey_ThrowsWorldFormatException_WithTypeAndKey()
    {
        var ex = Assert.Throws<WorldFormatException>(() => WorldBuilder.Build(WorldBuilder.Parse(WorldWithServices("solar_plant", "[\"trade\", \"trade\"]"))));
        Assert.Contains("solar_plant", ex.Message);
        Assert.Contains("trade", ex.Message);
    }

    [Fact]
    public void SaveLoad_KeepsTheServiceList()
    {
        var u = WorldBuilder.Build(WorldBuilder.Parse(WorldWithServices("solar_plant", "[]")));
        var json = SaveGameJson.Write(u, Now);
        Assert.Contains("\"Services\":[]", json);

        var (loaded, _) = SaveGameJson.Read(json);

        Assert.Empty(loaded.StationTypes["solar_plant"].Services);
        Assert.Equal(new[] { TradeService.ServiceKey }, loaded.StationTypes["steelworks"].Services);
    }

    [Fact]
    public void OldSave_WithoutServicesField_LoadsWithTradeStandard()
    {
        var u = WorldBuilder.BuildDefault();
        var json = SaveGameJson.Write(u, Now);
        var root = JsonNode.Parse(json)!;
        var types = root["StationTypes"]!.AsArray();
        Assert.All(types, t => Assert.True(t!.AsObject().ContainsKey("Services")));   // neuer Stand schreibt das Feld
        foreach (var t in types) t!.AsObject().Remove("Services");                    // alter Stand: Feld fehlt

        var (loaded, _) = SaveGameJson.Read(root.ToJsonString());

        Assert.NotEmpty(loaded.StationTypes);
        foreach (var t in loaded.StationTypes.Values)
            Assert.Equal(new[] { TradeService.ServiceKey }, t.Services);
    }

    [Fact]
    public void ForStation_DefaultWorld_ReturnsTradeForMarketStations()
    {
        var u = DemoUniverse.Create();
        var registry = StationServices.Default();
        var st = u.Stations.Values.First(s => s.Market.Count > 0);

        var services = registry.ForStation(u, st);

        Assert.Equal(new[] { TradeService.ServiceKey }, services.Select(s => s.Key));
    }

    [Fact]
    public void ForStation_FollowsTypeOrder_AndSkipsUnavailableAndUnknownKeys()
    {
        var u = DemoUniverse.Create();
        var st = u.Stations.Values.First(s => s.Market.Count > 0);
        u.StationTypes[st.TypeKey].Services = new List<string> { "beta", "ghost", "alpha", "trade" };
        var registry = new StationServices();
        registry.Register(new Stub("alpha", true));
        registry.Register(new Stub("beta", false));   // registriert, aber an dieser Station nicht verfügbar
        registry.Register(new TradeService());          // "trade" ist an einer Station mit Markt verfügbar

        var services = registry.ForStation(u, st);

        Assert.Equal(new[] { "alpha", "trade" }, services.Select(s => s.Key));
    }

    [Fact]
    public void ForStation_StationWithoutMarket_OmitsTrade()
    {
        var u = DemoUniverse.Create();
        var registry = StationServices.Default();
        var st = u.Stations.Values.First(s => s.Market.Count > 0);
        st.Market.Clear();

        Assert.Empty(registry.ForStation(u, st));
    }
}
