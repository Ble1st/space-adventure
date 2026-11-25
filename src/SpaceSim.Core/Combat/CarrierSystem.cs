using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Träger (<see cref="Carriers"/>): Bordversorgung der eingeschleusten Jäger, Selbststart bei Gefechtsbeginn (KI-Träger und nicht aktive Spielerträger),
/// Rückkehr und Landung der gestarteten Jäger, Verlust ohne Träger. Läuft jeden Tick nach dem Rundengefecht (schaltet mit <see cref="EconomyRules.Carriers"/>);
/// ohne Träger und Bordjäger ändert es nichts. Keine Zufallszahl.
/// </summary>
public sealed class CarrierSystem : ISimSystem
{
    public void Step(Universe u, int dt)
    {
        if (!u.Rules.Carriers) return;
        // Bordversorgung: alle eingeschleusten Jäger (Träger in Id-Reihenfolge)
        if (u.Hangars.Count > 0)
        {
            float sec = dt / (float)SimClock.TicksPerSecond;
            foreach (var (_, list) in u.Hangars.OrderBy(kv => kv.Key.Value))
                foreach (var f in list) Service(u, f, sec);
        }
        var ships = u.ShipsById();
        for (int i = 0; i < ships.Count; i++)
        {
            var s = ships[i];
            if (!u.Ships.ContainsKey(s.Id)) continue;   // in diesem Takt eingeschleust oder verloren
            if (s.CarrierHome.Value != 0) StepFighter(u, s);
            else if (Carriers.IsCarrier(s) && u.Hangars.ContainsKey(s.Id)) StepCarrier(u, s);
        }
    }

    /// <summary>Bordversorgung eines eingeschleusten Jägers: Hülle langsam, Schild/Munition/Raketen/Bank/Täuschkörper sofort, Brände aus, Hitze ab.</summary>
    static void Service(Universe u, Ship f, float sec)
    {
        if (f.Hull < f.MaxHull) f.Hull = MathF.Min(f.MaxHull, f.Hull + f.MaxHull * MathF.Max(0f, u.Rules.CarrierRepairPerSecond) * sec);
        var sy = f.Systems;
        sy.ShieldMissing = 0f; sy.ChargeMissing = 0f; sy.AmmoMissing = 0f; sy.MissilesMissing = 0f; sy.Heat = 0f; sy.HeatLocked = false;
        sy.Burning.Clear();
        f.FlaresMissing = 0f; f.FlaresUntil = 0;
    }

    /// <summary>Träger mit Jägern an Bord: bei Gefechtsbeginn selbst starten (nicht der Träger, den die Person gerade fliegt — dort entscheidet sie).</summary>
    static void StepCarrier(Universe u, Ship c)
    {
        bool manual = c.Id == u.PlayerShip && !u.PlayerOnFoot;
        if (manual || !Carriers.Engaged(u, c) || Carriers.LaunchIssue(u, c) is not null) return;
        Carriers.Launch(u, c, auto: true);
    }

    static void StepFighter(Universe u, Ship f)
    {
        var r = u.Rules;
        bool engaged = Carriers.Engaged(u, f);
        if (!u.Ships.TryGetValue(f.CarrierHome, out var c) || !Carriers.IsCarrier(c))
        {
            if (engaged) return;   // kämpft weiter; danach Ersatzträger oder Verlust
            if (Rehome(u, f) is { } nc) { c = nc; }
            else { Departures.RemoveShip(u, f, DepartureReason.Destroyed, "carrier_lost"); return; }
        }
        // nie allein zwischen Sektoren: Träger reist, liegt woanders, legt an — oder der Jäger will weg → sofort landen (verlässt dabei ein Gefecht)
        bool carrierAway = c.Transit is not null || c.Sector != f.Sector || c.DockedAt.Value != 0;
        bool fighterAway = f.Transit is { } t && t.ToSector != c.Sector;
        if (carrierAway || fighterAway) { Carriers.Stow(u, c, f, c.DockedAt.Value != 0 ? "docked" : "left"); return; }
        if (engaged) { f.CarrierFought = true; return; }
        // Rückkehr: nach einem Gefecht (Träger kämpft nicht mehr), im Notfall oder angeschlagen
        if (f.CarrierReturnSince < 0 && ((f.CarrierFought && !Carriers.Engaged(u, c)) || f.Emergency || f.Hull < r.EscortRetreatHull * f.MaxHull))
            f.CarrierReturnSince = u.Tick;
        if (f.CarrierReturnSince >= 0)
        {
            var here = ThreatMap.Where(u, f).Position;
            if (Vec2.Distance(here, c.Position) <= r.CarrierDockRange || u.Tick - f.CarrierReturnSince >= r.CarrierReturnTicks)
                Carriers.Stow(u, c, f, "return");
            return;
        }
        // draußen: begleitet den Träger (z. B. nach einem Notfall oder nach dem Laden ohne Befehl)
        bool escorting = f.Command is EscortCommand e2 && e2.Target == c.Id;
        if (!f.Emergency && !escorting) Commands.Give(u, f, new EscortCommand(c.Id), out _, ignoreCapacity: true);
    }

    /// <summary>Ersatzträger im Sektor: eigener Träger mit freiem Platz, nicht unterwegs (kleinste Id).</summary>
    static Ship? Rehome(Universe u, Ship f)
    {
        foreach (var s in u.ShipsById())
            if (s.Owner == f.Owner && Carriers.IsCarrier(s) && s.Transit is null && s.Sector == f.Sector && s.DockedAt.Value == 0 && Carriers.FreeBays(u, s) > 0)
            {
                f.CarrierHome = s.Id;
                return s;
            }
        return null;
    }
}
