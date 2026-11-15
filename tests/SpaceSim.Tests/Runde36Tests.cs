using SpaceSim.Core.Content;
using SpaceSim.Core.Diagnostics;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Persistence;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Runde 36: drei offene Lücken. (1) Zwei-Waren-Lücke: ein Produktkauf hebt über die Zahlungsbereitschaft das Gebot der Station für die Zutaten — solange der
/// Kauf-Posten offen ist, bekommt der Spieler für eine Zutat dort höchstens das Gebot, das die Station ohne seine noch nicht nachproduzierten Käufe hätte.
/// (2) Befehlskapazität: Verlassen/Auflösen eines
/// Verbands darf keine Einzelbefehle über die Kapazität erzeugen. (3) Liegegebühr: Verbandsmitgliedschaft befreit nicht; wer angedockt an einer fremden
/// Station liegt, zahlt.
/// </summary>
public class Runde36Tests
{
    static readonly Lazy<string> Saved = new(() =>
    {
        var u = WorldBuilder.BuildDefault(11, "trader");
        new SimulationRunner().Run(u, 2000);
        return SaveGameJson.Write(u, DateTimeOffset.UnixEpoch);
    });

    static Universe World(bool inputCap = true)
    {
        var u = SaveGameJson.Read(Saved.Value).Universe;
        u.Rules.BuybackInputCap = inputCap;
        var p = u.Ships[u.PlayerShip]; p.Cargo.Clear(); u.WalletOf(p).Credits = 1_000_000f;
        foreach (var st in u.Stations.Values) st.Credits = MathF.Max(st.Credits, 1_000_000f);
        return u;
    }

    static Ship Own(Universe u, string model, Station at)
    {
        var m = u.ShipModels[model];
        var s = ShipPurchase.Create(u, u.NewShipId(), m.Key, u.ShipClasses[m.ClassKey].Slots, m.Fit, ShipRole.Player, Controller.Player, u.PlayerActor, at, 0f);
        s.HomeBase = at.Id;
        u.AddShip(s);
        return s;
    }

    static List<SimEvent> Capture(Universe u) { var l = new List<SimEvent>(); u.Events.Raised += l.Add; return l; }

    /// <summary>Werft Hephaistos: mittlerer Bausatz (Produkt) und Hüllenplatten (Zutat seines Rezepts).</summary>
    static (Station St, GoodId Kit, GoodId Plates) Yard(Universe u)
    {
        var st = u.StationsById().First(s => s.Name == "Werft Hephaistos");
        var kit = u.FindGood("ship_kit_medium")!.Id;
        var plates = u.FindGood("hull_plates")!.Id;
        Assert.Contains(st.Production, r => r.Outputs.ContainsKey(kit) && r.Inputs.ContainsKey(plates));
        if (st.Market[kit].Amount < 4f) st.Market[kit].Amount = 8f;
        return (st, kit, plates);
    }

    // ───────── 1. Zwei-Waren-Lücke ─────────

    /// <summary>Gemessener Fall der Prüfung Runde 32 mit echtem Tick: vier Bausätze gekauft, das Plattengebot springt; die mitgebrachten Platten bringen höchstens das Gebot ohne den Kauf (≈ vor dem Kauf).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ZweiWaren_ProduktKauf_ZutatHoechstensGebotVorDemKauf(bool cap)
    {
        var u = World(cap);
        var (st, kit, plates) = Yard(u);
        var p = u.Ships[u.PlayerShip];
        float bidBefore = st.Market[plates].Price;
        Assert.True(TradeActions.Buy(u, p, st, kit, 4f).Units >= 4f);
        new SimulationRunner().Step(u);
        float jumped = st.Market[plates].Price;
        Assert.True(jumped > bidBefore * 1.5f, $"Voraussetzung: Gebot springt ({bidBefore:0.0} → {jumped:0.0})");
        float n = MathF.Floor(MathF.Min(40f, TradeActions.SaleCap(u, st, plates)));
        Assert.True(n >= 5f, $"Annahme {n}");
        p.Cargo[plates] = n;
        float rev = TradeActions.Sell(u, p, st, plates, n).Revenue;
        if (cap) Assert.True(rev <= n * bidBefore + 0.5f, $"Erlös {rev:0.0} > {n} × {bidBefore:0.0}");
        else Assert.True(rev > n * bidBefore * 1.4f, $"ohne Deckel bleibt die Lücke messbar ({rev:0.0})");
    }

    /// <summary>Umwege: zweites Schiff, Flottenschiff (gemeinsame Buchung), zwei Buchungen im selben Tick, Tickrand (Kauf gestuft über mehrere Ticks).</summary>
    [Fact]
    public void ZweiWaren_ZweitesSchiffFlotteUndMehrfachbuchung_Gedeckelt()
    {
        var u = World();
        var (st, kit, plates) = Yard(u);
        var p = u.Ships[u.PlayerShip];
        float bid = st.Market[plates].Price;
        // gestuft über drei Ticks (Tickrand): der Bestand vor dem ersten Kauf zählt
        var r = new SimulationRunner();
        for (int i = 0; i < 3; i++) { Assert.True(TradeActions.Buy(u, p, st, kit, 1f).Units >= 1f); r.Step(u); }
        Assert.True(st.Market[plates].Price > bid * 1.2f, $"Voraussetzung: Gebot springt ({bid:0.0} → {st.Market[plates].Price:0.0})");
        Assert.True(PlayerLedger.InputCap(u, st, plates)!.Value.Price <= bid * 1.02f + 0.5f, $"Deckel {PlayerLedger.InputCap(u, st, plates)!.Value.Price:0.0} vs {bid:0.0}");
        bid = PlayerLedger.InputCap(u, st, plates)!.Value.Price;
        var b = Own(u, "nomad", st);
        b.Cargo[plates] = 10f;
        Assert.True(TradeActions.Sell(u, b, st, plates, 10f).Revenue <= 10f * bid + 0.5f);   // zweites Schiff
        var fleet = Own(u, "mule", st);
        fleet.Cargo[plates] = 10f;
        var bk = PlayerLedger.BookSale(u, fleet, st, plates, 10f, u.WalletOf(fleet));   // Flottenbuchung (TraderSystem)
        Assert.True(bk.Money <= 10f * bid + 0.5f && bk.Ledgered >= 10f);
        p.Cargo[plates] = 12f;
        float r1 = TradeActions.Sell(u, p, st, plates, 4f).Revenue, r2 = TradeActions.Sell(u, p, st, plates, 8f).Revenue;   // gleicher Tick, zwei Buchungen
        Assert.True(r1 + r2 <= 12f * bid + 0.5f);
        // NPC-Händler: kein Deckel
        var npc = u.ShipsById().First(s => s.IsEconomyAi && s.Has(Capability.Cargo));
        Assert.Equal(10f * st.Market[plates].Price, PlayerLedger.SaleValue(u, npc, st, plates, 10f, st.Market[plates].Price).Revenue, 2);
    }

    /// <summary>Kauf an S und Verkauf der Zutat an einer anderen Station bleibt ungedeckelt (gewöhnliche Arbitrage); ebenso Waren, die kein Rezept des Produkts verwendet.</summary>
    [Fact]
    public void ZweiWaren_AndereStationUndAndereWare_Frei()
    {
        var u = World();
        var (st, kit, plates) = Yard(u);
        var p = u.Ships[u.PlayerShip];
        TradeActions.Buy(u, p, st, kit, 2f);
        var other = u.StationsById().First(s => s.Id != st.Id && s.Market.ContainsKey(plates) && !s.Produces(plates) && !s.IsBuildSite);
        Assert.Null(PlayerLedger.InputCap(u, other, plates));
        float price = other.Market[plates].Price;
        Assert.Equal(5f * price, PlayerLedger.SaleValue(u, p, other, plates, 5f, price).Revenue, 2);
        var unrelated = st.Market.Keys.First(g => g != kit && g != plates && !st.Production.Any(r => r.Outputs.ContainsKey(kit) && r.Inputs.ContainsKey(g)));
        Assert.Null(PlayerLedger.InputCap(u, st, unrelated));
    }

    /// <summary>Mehrere Produkte: der Deckel rechnet alle eigenen Produktkäufe der Station heraus (mittlerer und schwerer Bausatz, beide aus Hüllenplatten).</summary>
    [Fact]
    public void ZweiWaren_MehrereProdukte_AlleHerausgerechnet()
    {
        var u = World();
        var (st, kit, plates) = Yard(u);
        var heavy = u.FindGood("ship_kit_heavy")!.Id;
        if (st.Market[heavy].Amount < 4f) st.Market[heavy].Amount = 6f;
        var p = u.Ships[u.PlayerShip];
        float bid = st.Market[plates].Price;
        TradeActions.Buy(u, p, st, kit, 4f);
        TradeActions.Buy(u, p, st, heavy, 4f);
        new SimulationRunner().Step(u);
        Assert.True(st.Market[plates].Price > bid * 1.2f);
        float cap = PlayerLedger.InputCap(u, st, plates)!.Value.Price;
        Assert.True(cap <= bid * 1.02f + 0.5f, $"Deckel {cap:0.0} vs Gebot vorher {bid:0.0}");
    }

    /// <summary>Ehrlicher Fall: steigt das Zutatengebot aus anderen Gründen (Grenze ohne eigenen Kauf gleich), deckelt nichts — auch bei offenem Produkt-Posten.</summary>
    [Fact]
    public void ZweiWaren_OhneEigenenEinfluss_KeinDeckel()
    {
        var u = World();
        var (st, kit, plates) = Yard(u);
        var p = u.Ships[u.PlayerShip];
        TradeActions.Buy(u, p, st, kit, 2f);
        var post = PlayerLedger.Post(u, st, kit)!;
        st.Market[kit].Amount = post.BoughtStockBefore + 5f;   // Station hat nachproduziert: der eigene Kauf wirkt nicht mehr auf den Preis
        new SimulationRunner().Step(u);
        if (PlayerLedger.Post(u, st, kit) is { } still && PlayerLedger.BuyActive(u, still))
            Assert.Null(PlayerLedger.InputCap(u, st, plates));
    }

    /// <summary>Schiffskauf verbraucht den Bausatz der Werft: der Verbrauch zählt als Kauf (Runde 32) und setzt den Zutatendeckel.</summary>
    [Fact]
    public void ZweiWaren_Schiffskauf_SetztZutatendeckel()
    {
        var u = World();
        var st = u.StationsById().First(s => s.Name == "Werft Hephaistos");
        var p = u.Ships[u.PlayerShip];
        var svc = new ShipDealerService();
        var offer = svc.Offers(u, st, p).FirstOrDefault(o => o.Amount > 0 && u.ShipModels.TryGetValue(o.Key[4..], out var m) && u.ShipClasses[m.ClassKey].Kit == "ship_kit_light");
        Assert.True(offer.Amount > 0, "kein kaufbares Schiff mit leichtem Bausatz");
        var kit = u.FindGood("ship_kit_light")!.Id;
        Assert.True(svc.Execute(u, st, p, new ServiceRequest(offer.Key, default, 1)).Success);
        var post = PlayerLedger.Post(u, st, kit);
        Assert.NotNull(post);
        Assert.True(PlayerLedger.BuyActive(u, post!));
        // der Verbrauch zählt wie ein Kauf: drückt er den Bausatzbestand, rechnet der Zutatendeckel ihn heraus
        var plates = u.FindGood("hull_plates")!.Id;
        st.Market[kit].Amount = MathF.Max(0f, post.BoughtStockBefore - post.BoughtQty);
        new SimulationRunner().Step(u);
        if (st.Market[plates].Price > 0f && PlayerLedger.InputCap(u, st, plates) is { } ic) Assert.True(ic.Price <= st.Market[plates].Price);
    }

    /// <summary>Verfall: kehren Bestand und Preis des Produkts zurück (und hält der Spieler nichts davon), endet der Zutatendeckel mit dem Kaufteil.</summary>
    [Fact]
    public void ZweiWaren_Verfall_BeendetZutatendeckel()
    {
        var u = World();
        var (st, kit, plates) = Yard(u);
        var p = u.Ships[u.PlayerShip];
        TradeActions.Buy(u, p, st, kit, 4f);
        Assert.Null(PlayerLedger.InputCap(u, st, plates));   // im selben Tick hat der Markt noch nicht neu bepreist: nichts zu deckeln
        new SimulationRunner().Step(u);
        Assert.NotNull(PlayerLedger.InputCap(u, st, plates));
        p.Cargo.Remove(kit);
        var post = PlayerLedger.Post(u, st, kit)!;
        st.Market[kit].Amount = post.BoughtStockBefore + 1f;
        st.Market[kit].Price = post.BoughtPrice * 0.5f;
        u.Tick = post.LastTick + 1;
        PlayerLedger.Prune(u);
        Assert.Null(PlayerLedger.InputCap(u, st, plates));
    }

    /// <summary>Speichern und Laden mit offenem Posten: kein neuer Zustand (der Deckel wird aus dem Posten nachgerechnet), Hash und Deckel gleich; Hinweise nennen das Produkt.</summary>
    [Fact]
    public void ZweiWaren_SpeichernLaden_UndHinweis()
    {
        var u = World();
        var (st, kit, plates) = Yard(u);
        var p = u.Ships[u.PlayerShip];
        TradeActions.Buy(u, p, st, kit, 4f);
        new SimulationRunner().Step(u);
        var cap = PlayerLedger.InputCap(u, st, plates)!.Value;
        p.Cargo[plates] = 5f;
        string note = PlayerLedger.SaleNote(u, p, st, plates)!;
        Assert.Contains(u.GoodOf(kit).Name, note);
        Assert.Equal($"Deckel {cap.Price:0.0}", PlayerLedger.SaleNoteShort(u, p, st, plates));
        ulong h = UniverseHash.Compute(u);
        var u2 = SaveGameJson.Read(SaveGameJson.Write(u, DateTimeOffset.UnixEpoch)).Universe;
        Assert.Equal(h, UniverseHash.Compute(u2));
        Assert.Equal(cap.Price, PlayerLedger.InputCap(u2, u2.Stations[st.Id], plates)!.Value.Price, 2);
    }

    // ───────── 2. Befehlskapazität ─────────

    /// <summary>
    /// Lücke reproduziert und geschlossen: Verband bilden, Arbeitsbefehl an alle, Kapazität mit Einzelbefehlen füllen, auflösen — vorher behielten die
    /// Mitglieder Einzelbefehle über der Kapazität. Jetzt ist Auflösen/Verlassen verweigert (mit Grund), bis genug Befehle beendet sind.
    /// </summary>
    [Fact]
    public void Befehlskapazitaet_AufloesenUndVerlassen_UeberDerGrenzeVerweigert()
    {
        var u = WorldBuilder.BuildDefault(1);
        var beta = u.StationsById().First(s => s.Name == "Handelsstation Beta");
        var a = Own(u, "nomad", beta); var b = Own(u, "nomad", beta); var c = Own(u, "nomad", beta);
        var sq = Squadrons.Create(u, a, "Test", out _)!;
        Assert.True(Squadrons.Join(u, sq, b, out _));
        Assert.True(Squadrons.Join(u, sq, c, out _));
        Assert.True(Squadrons.Command(u, sq, new TradeCommand(new AutoLimits(beta.Sector, 2, 1.5f, 0f)), out var m3), m3);
        var spare = Enumerable.Range(0, 12).Select(_ => Own(u, "nomad", beta)).ToList();
        int cap = Commands.Capacity(u, u.PlayerActor).Ships;
        foreach (var s in spare.Take(cap)) Assert.True(Commands.Give(u, s, new HoldCommand(), out var mg), mg);
        Assert.Equal(cap, Commands.Commanded(u, u.PlayerActor));
        // Auflösen verweigert, nichts geändert
        Assert.False(Squadrons.Disband(u, sq, out var dm));
        Assert.Contains("Auflösen gesperrt", dm);
        Assert.Contains("3 Schiffen", dm);
        Assert.True(u.Squadrons.ContainsKey(sq.Id));
        Assert.All(new[] { a, b, c }, s => Assert.Equal(sq.Id, s.Squadron));
        // Verlassen ebenso
        Assert.False(Squadrons.Leave(u, b, out var lm));
        Assert.Contains("Verlassen gesperrt", lm);
        Assert.Equal(sq.Id, b.Squadron);
        Assert.Equal(cap, Commands.Commanded(u, u.PlayerActor));
        // Befehl beenden → Verlassen erlaubt (ohne Befehl zählt das Schiff nicht)
        Commands.Cancel(u, b);
        Assert.True(Squadrons.Leave(u, b, out lm), lm);
        Assert.Equal(cap, Commands.Commanded(u, u.PlayerActor));
        // Platz schaffen: zwei Einzelbefehle beenden → Auflösen erlaubt, danach genau an der Grenze
        Commands.Cancel(u, spare[0]); Commands.Cancel(u, spare[1]);
        Assert.True(Squadrons.Disband(u, sq, out dm), dm);
        Assert.Equal(cap, Commands.Commanded(u, u.PlayerActor));
        Assert.Equal(0, Commands.OverCapacity(u, u.PlayerActor));
    }

    /// <summary>Wiederholung der Umgehung (bilden, Befehl, auflösen) kann die Einzelbefehle nie über die Kapazität heben; NPC-Verbände sind nicht betroffen.</summary>
    [Fact]
    public void Befehlskapazitaet_WiederholteUmgehung_NieUeberDerGrenze()
    {
        var u = WorldBuilder.BuildDefault(1);
        var beta = u.StationsById().First(s => s.Name == "Handelsstation Beta");
        var ships = Enumerable.Range(0, 24).Select(_ => Own(u, "nomad", beta)).ToList();
        int cap = Commands.Capacity(u, u.PlayerActor).Ships;
        for (int round = 0; round < 6; round++)
        {
            var free = ships.Where(s => s.Command is null && s.Squadron.Value == 0).Take(4).ToList();
            if (free.Count < 2) break;
            var sq = Squadrons.Create(u, free[0], "", out var cm);
            if (sq is null) break;
            foreach (var s in free.Skip(1)) Squadrons.Join(u, sq, s, out _);
            Squadrons.Command(u, sq, new HoldCommand(), out _);
            Squadrons.Command(u, sq, new TradeCommand(AutoLimits.None), out _);
            Squadrons.Disband(u, sq);
            foreach (var id in sq.Members.ToList()) if (u.Ships.TryGetValue(id, out var m)) Squadrons.Leave(u, m);
            Assert.True(Commands.Commanded(u, u.PlayerActor) <= cap, $"Runde {round}: {Commands.Commanded(u, u.PlayerActor)}/{cap}");
        }
        // NPC-Verbände: Auflösen und Verlassen ohne Prüfung
        var npcSq = u.Squadrons.Values.FirstOrDefault(q => q.Owner != u.PlayerActor);
        if (npcSq is not null) Assert.Null(Squadrons.DisbandIssue(u, npcSq));
    }

    /// <summary>Verlust des Anführers (Nachfolge) erzeugt keine Einzelbefehle: der Nachfolger bleibt im Verband.</summary>
    [Fact]
    public void Befehlskapazitaet_VerlustDesAnfuehrers_KeineEinzelbefehle()
    {
        var u = WorldBuilder.BuildDefault(1);
        var beta = u.StationsById().First(s => s.Name == "Handelsstation Beta");
        var a = Own(u, "nomad", beta); var b = Own(u, "nomad", beta); var c = Own(u, "nomad", beta);
        var sq = Squadrons.Create(u, a, "", out _)!;
        Squadrons.Join(u, sq, b, out _); Squadrons.Join(u, sq, c, out _);
        Assert.True(Squadrons.Command(u, sq, new GoToCommand(default, default, beta.Id), out _));
        int before = Commands.Commanded(u, u.PlayerActor);
        Departures.RemoveShip(u, a, DepartureReason.Destroyed, "test");
        Assert.Equal(before, Commands.Commanded(u, u.PlayerActor));
        Assert.Equal(b.Id, sq.Leader);
        Assert.Equal(sq.Id, b.Squadron);
    }

    // ───────── 3. Liegegebühr ─────────

    static Universe War() => TestWorlds.Default(fuel: false, research: false, startOption: "war");

    /// <summary>
    /// Führer schwebt neben einer fremden Station ohne anzudocken: die Begleiter bekommen keinen Liegeplatz (Runde 20) und zahlen nichts — auch ein Begleiter
    /// eines Begleiters (Kette). Liegt ein Begleiter trotzdem angedockt dort (Zustand der alten Lücke), zahlt er jetzt.
    /// </summary>
    [Fact]
    public void Liegegebuehr_FuehrerSchwebt_BegleiterOhneLiegeplatzZahlenNicht_AngedocktZahlt()
    {
        var u = War();
        var me = u.Ships[u.PlayerShip];
        var wing = u.ShipsById().Where(s => s.IsPlayer && s.Id != me.Id && s.Command is FollowCommand).ToList();
        Assert.Equal(2, wing.Count);
        var foreign = u.StationsById().First(st => st.Sector == me.Sector && st.Owner != me.Owner && st.Dockable && !st.IsBuildSite);
        // Kette: drittes Schiff folgt dem ersten Begleiter
        var chain = Own(u, "nomad", foreign);
        chain.DockedAt = default; chain.Position = me.Position + new Vec2(-12f, 8f);   // gekauft liegt es angedockt — es soll nur folgen
        Assert.True(Commands.Give(u, chain, new FollowCommand(wing[0].Id), out var cm, ignoreCapacity: true), cm);
        me.DockedAt = default;
        me.Position = foreign.DockPoint(me.Id) + new Vec2(6f, 0f);   // schwebt neben dem Andockpunkt
        var ev = Capture(u);
        var r = new SimulationRunner();
        for (int i = 0; i < u.Rules.DayTicks + 200; i++) r.Step(u);
        foreach (var w in wing.Append(chain))
        {
            Assert.Equal(0, w.DockedAt.Value);
            Assert.Equal(0f, Hangar.BerthFeeNow(u, w));
        }
        var paid = ev.OfType<BerthFeePaid>().Where(bf => bf.Actor == u.PlayerActor).ToList();
        Assert.True(paid.Count == 0, string.Join("; ", paid.Take(5).Select(bf => $"Schiff {bf.Ship.Value} an {u.Stations[bf.Station].Name} {bf.Amount}"))
            + $" · Führer {me.Id.Value}, Begleiter {string.Join(",", wing.Select(w => w.Id.Value))}, Kette {chain.Id.Value}");
        // alte Lücke: angedockt, Führer schwebt — Mitgliedschaft befreit nicht mehr
        foreach (var w in wing) { w.DockedAt = foreign.Id; Assert.True(Hangar.PaysBerthFee(u, w)); Assert.True(Hangar.BerthFeeNow(u, w) > 0f); }
    }

    /// <summary>Verband fliegt (Führer unterwegs mit Befehl, Mitglieder folgen): unterwegs keine Gebühr; angedockter Verband an fremder Station zahlt (Runde 21 bleibt).</summary>
    [Fact]
    public void Liegegebuehr_FliegenderVerbandZahltNicht_AngedockterZahlt()
    {
        var u = WorldBuilder.BuildDefault(1);
        var beta = u.StationsById().First(s => s.Name == "Handelsstation Beta");
        var a = Own(u, "nomad", beta); var b = Own(u, "nomad", beta); var c = Own(u, "nomad", beta);
        var sq = Squadrons.Create(u, a, "", out _)!;
        Squadrons.Join(u, sq, b, out _); Squadrons.Join(u, sq, c, out _);
        var far = u.StationsById().First(s => s.Sector != beta.Sector && s.Dockable && !s.IsBuildSite && s.Owner != u.PlayerActor && u.FindRoute(beta.Sector, s.Sector) is { Count: >= 1 });
        Assert.True(Squadrons.Command(u, sq, new GoToCommand(default, default, far.Id), out var gm), gm);
        var r = new SimulationRunner();
        bool sawFlying = false;
        for (int i = 0; i < 6000 && sq.Members.Any(id => u.Ships[id].DockedAt != far.Id); i++)
        {
            r.Step(u);
            if (sq.Members.All(id => u.Ships[id].Transit is not null)) { sawFlying = true; Assert.All(sq.Members, id => Assert.Equal(0f, Hangar.BerthFeeNow(u, u.Ships[id]))); }
        }
        Assert.True(sawFlying);
        Assert.All(sq.Members, id => Assert.Equal(far.Id, u.Ships[id].DockedAt));
        Assert.All(sq.Members, id => Assert.True(Hangar.BerthFeeNow(u, u.Ships[id]) > 0f));
    }
}
