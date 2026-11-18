using SpaceSim.Core.Content;
using SpaceSim.Core.Events;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>
/// Startpaket „Sprung- und Tarntest (Test)“ (Schlüssel <c>jumpcloak</c>): Nomade mit Sprungantrieb und Tarnfeld Mk2 vor dem Sektor-HQ Beta, ein lauernder
/// Kaperer in Sichtweite, eine Streife daneben, voller Tank. Geprüft: Aufbau, Fit und Leistung (Tarnung an während des Aufladens), Sprung zwei Hops weit,
/// Kaperer wählen den getarnten Spieler nicht als Opfer (die Polizei sieht ihn mit Mk2 auch nicht).
/// </summary>
public class SprungTarnStartpaketTests
{
    static readonly SectorId Beta = new(2), Borealis = new(5);

    static (Universe u, Ship me) Start(ulong seed = 7)
    {
        var u = WorldBuilder.BuildDefault(seed, "jumpcloak");
        return (u, u.Ships[u.PlayerShip]);
    }

    [Fact]
    public void Aufbau_FitTankKapererStreife()
    {
        var (u, me) = Start();
        Assert.Equal("nomad", me.ModelKey);
        Assert.Contains("jump_drive_s", me.Fit);
        Assert.Contains("cloak_s_mk2", me.Fit);
        Assert.True(JumpDrive.Fitted(u, me));
        Assert.True(Cloak.Fitted(u, me));
        Assert.Equal(2, me.Stats.JumpRange);
        Assert.Equal(2.5f, me.Stats.Cloak);
        Assert.Equal(Beta, me.Sector);
        Assert.Equal(Beta, u.PlayerSector);
        Assert.True(u.Rules.PlayerRaids);   // nicht „ruhig“: Kaperer zählen
        Assert.Equal(5000f, u.WalletOf(me).Credits, 1);
        Assert.True(u.Rules.FuelActive);
        Assert.Equal(me.Stats.FuelCapacity, me.TotalFuel, 1);   // voller Tank
        Assert.Null(JumpDrive.ShadowAt(u, Beta, me.Position));   // Start außerhalb des Massenschattens: sofort sprungbereit
        Assert.Null(JumpDrive.ReadyIssue(u, me));
        Assert.Contains(u.ShipsById(), r => ThreatMap.IsRaider(r) && r.Sector == Beta && Vec2.Distance(r.Position, me.Position) < 100f);
        Assert.Contains(SecuritySystem.PoliceOf(u, u.Sectors[Beta].Owner), p => p.Sector == Beta && Vec2.Distance(p.Position, me.Position) < 100f);
        // zwei Hops weit erreichbar (Beta → Alpha/Vesta → Borealis)
        Assert.Contains(JumpDrive.Targets(u, me), t => t.Sector == Borealis && t.Hops == 2);
    }

    [Fact]
    public void Leistung_TarnungAnBeimAufladen_Reicht_SprungZweiHops()
    {
        var (u, me) = Start();
        Assert.Null(Cloak.TurnOn(u, me));
        Assert.True(me.Cloaked);
        Assert.Null(JumpDrive.StartIssue(u, me, Borealis));
        Assert.Null(JumpDrive.Start(u, me, Borealis));
        Assert.True(JumpDrive.Charging(me));
        // Leistungsbilanz mit Tarnfeld und Sprungladung gleichzeitig: Grundlast + beide Zusatzlasten ≤ Reaktorleistung
        Assert.True(me.Stats.PowerDraw <= me.Stats.PowerOutput, $"Last {me.Stats.PowerDraw} > Leistung {me.Stats.PowerOutput}");
        var sys = new JumpCloakSystem();
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        for (int i = 0; i < 80 && me.Sector == Beta; i++) { sys.Step(u, 1); u.Tick++; }
        Assert.Equal(Borealis, me.Sector);
        Assert.Equal(Borealis, u.PlayerSector);
        Assert.Empty(ev.OfType<JumpChargeAborted>());
        Assert.Single(ev.OfType<ShipDriveJumped>());
        Assert.Null(JumpDrive.ShadowAt(u, Borealis, me.Position));
        Assert.True(JumpDrive.CooldownLeft(u, me) > 0);
        Assert.True(me.TotalFuel < me.Stats.FuelCapacity);   // Treibstoff an: der Sprung kostet Tank
    }

    [Fact]
    public void Kaperer_WaehlenGetarntenSpielerNicht_AuchPolizeiSiehtMk2Nicht()
    {
        var (u, me) = Start();
        Assert.False(ThreatMap.NearStation(u, Beta, me.Position));
        Assert.Contains(PirateSystem.Victims(u, Beta), v => v.Ship.Id == me.Id);   // ungetarnt ein Opfer (Fracht ≥ Mindestbeute)
        Assert.Null(Cloak.TurnOn(u, me));
        Assert.DoesNotContain(PirateSystem.Victims(u, Beta), v => v.Ship.Id == me.Id);
        Assert.True(Cloak.HiddenFrom(me, u.Rules.PoliceSensorStrength));
        Assert.True(Cloak.HiddenFrom(me, u.Rules.JumpSensorStrength));
        // getarnt eine Weile laufen lassen (Schiff steht still): kein Überfall auf den Spieler
        var ev = new List<SimEvent>(); u.Events.Raised += ev.Add;
        new SimulationRunner().Run(u, 600);
        Assert.True(me.Cloaked);
        Assert.DoesNotContain(u.PendingEncounters, p => p.Target == me.Id);
        Assert.DoesNotContain(ev.OfType<EncounterStarted>(), e => e.Defender == me.Id || e.Attacker == me.Id);
    }
}
