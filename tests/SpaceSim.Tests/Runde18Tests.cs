using SpaceSim.Core.Flight;
using SpaceSim.Core.Mining;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;
using Xunit;

namespace SpaceSim.Tests;

/// <summary>Runde 18 (Handy-Test Phase 32): Wenden als eigener Rissgrund, kurze Schachttexte, Mindestgröße in der Warnphase, Liegegebühr eines geparkten Verbands.</summary>
public class Runde18Tests
{
    [Fact]
    public void Abriss_WendenHatEigenenGrund_SchubDanach()
    {
        var r = new EconomyRules();
        Assert.Null(MiningAnchor.SteeringBreak(r, MiningAutopilot.ContactPush, 0f));
        Assert.Equal("turn", MiningAnchor.SteeringBreak(r, 0f, -1f));
        Assert.Equal("turn", MiningAnchor.SteeringBreak(r, 0.8f, 1f));   // Wenden zählt zuerst
        Assert.Equal("thrust", MiningAnchor.SteeringBreak(r, 0.5f, 0f));
        Assert.Equal("Riss: Wenden", MiningAnchor.ShortReason("turn"));
        Assert.Equal("Anker gerissen: Wenden", MiningAnchor.ReasonText("turn"));
    }

    [Fact]
    public void Schachttexte_Kurz()
    {
        Assert.Equal("verankert", WeaponBays.ContactText(ContactState.Mining, true));
        Assert.Equal("Anker fliegt", WeaponBays.ContactText(ContactState.Anchoring, true));
        Assert.Equal("Bug an Fels", WeaponBays.ContactText(ContactState.NoContact, true));
        Assert.Equal("Bug an Fels", WeaponBays.ContactText(ContactState.NoContact, false));
        Assert.Equal("Riss: Schub", WeaponBays.ContactText(ContactState.NoContact, true, "Riss: Schub"));
        // die Texte, die die Taste zeigen kann, bleiben kurz (die Schriftmessung prüft der Selbsttest)
        foreach (var s in Enum.GetValues<ContactState>()) Assert.True(WeaponBays.ContactText(s, true).Length <= 14, s.ToString());
        Assert.Equal(0.6f, ContactMining.Spin(new ShipStats { ContactMining = 3f }, ContactState.Anchoring));
    }

    [Fact]
    public void Warnphase_MindestgroesseKapptDenAbstand()
    {
        // Schiff S (Länge 4,8), Sichtfeld 50°: höchstens ≈ 86 m, damit es 6 % der Bildhöhe einnimmt
        float d = CombatFraming.MaxDistanceForSize(4.8f, 50f, CombatFraming.MinShipScreenShare);
        Assert.InRange(d, 84f, 88f);
        float share = 4.8f / (2f * d * MathF.Tan(25f * MathF.PI / 180f));
        Assert.Equal(CombatFraming.MinShipScreenShare, share, 3);
        Assert.True(CombatFraming.MaxDistanceForSize(4.8f * 1.5f, 50f, 0.06f) > d);   // größere Schiffe dürfen weiter weg
    }

    [Fact]
    public void Liegegebuehr_GeparkterVerbandZahlt_UnterwegsNicht()
    {
        var u = TestWorlds.Default(fuel: false, research: false, startOption: "war");
        var me = u.Ships[u.PlayerShip];
        var wing = u.ShipsById().Where(s => s.IsPlayer && s.Id != me.Id && s.Command is FollowCommand).ToList();
        Assert.Equal(2, wing.Count);
        var foreign = u.StationsById().First(st => st.Sector == me.Sector && st.Owner != me.Owner && st.Dockable && !st.IsBuildSite);
        foreach (var w in wing) w.DockedAt = foreign.Id;
        // Führer fliegt: Gefolge in Formation, keine Liegegebühr
        me.DockedAt = default;
        Assert.All(wing, w => Assert.Equal(0f, Hangar.BerthFeeNow(u, w)));
        // Führer liegt selbst an der fremden Station: der Verband ist geparkt und zahlt
        me.DockedAt = foreign.Id;
        Assert.All(wing, w => Assert.True(Hangar.BerthFeeNow(u, w) > 0f));
        Assert.Contains("Liegegebühr", Hangar.RunningCostsText(u, u.PlayerActor));
        // Führer fehlt: zählt als geparkt
        foreach (var w in wing) w.Command = new FollowCommand(new ShipId(999999));
        me.DockedAt = default;
        Assert.All(wing, w => Assert.True(Hangar.BerthFeeNow(u, w) > 0f));
    }
}
