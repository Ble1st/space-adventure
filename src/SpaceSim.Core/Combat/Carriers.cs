using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Träger und Bordjäger (KAMPF.md „Träger und Bordjäger“; Lücke gegenüber Interstellar Pilot 2, eigener Entwurf). Ein Träger (Schiff mit
/// <see cref="ShipStats.HangarBays"/> &gt; 0) nimmt bewaffnete Schiffe der Klasse S desselben Besitzers auf. Eingeschleust liegen sie in
/// <see cref="Universe.Hangars"/> — außerhalb von <see cref="Universe.Ships"/>: nicht im Raum, nicht gezeichnet, kein System bewegt sie, keine Liegegebühr.
/// Gestartet sind es gewöhnliche Schiffe im Sektor des Trägers mit <see cref="Ship.CarrierHome"/> und einer Eskorte zum Träger
/// (<see cref="EscortCommand"/>): über die vorhandene Naht (<see cref="FleetGuards.Escorts"/>, <see cref="Reinforcements.JoinSide"/>) treten sie den
/// Gefechten des Trägers auf seiner Seite bei. Regeln:
/// <list type="bullet">
/// <item>Bordjäger reisen nie allein zwischen Sektoren: verlässt der Träger den Sektor, legt er an oder reist ein Jäger weg, landet der Jäger sofort.</item>
/// <item>Nach einem Gefecht, im Notfall (Hülle unter der Rückzugsschwelle) oder auf „Jäger zurück“ fliegt er zum Träger und landet (spätestens nach
/// <see cref="EconomyRules.CarrierReturnTicks"/>).</item>
/// <item>Eingeschleust werden sie langsam repariert (<see cref="EconomyRules.CarrierRepairPerSecond"/>), Schild, Munition, Raketen, Speicherbank und
/// Täuschkörper sofort aufgefüllt, Brände gelöscht — kostenlos (Bordversorgung des Trägers).</item>
/// <item>Geht der Träger verloren, gehen die eingeschleusten Jäger mit (<see cref="Universe.RemoveShip"/>); gestartete kämpfen weiter, landen danach auf einem
/// anderen eigenen Träger im Sektor mit freiem Platz — sonst gehen sie verloren (kein Sprungantrieb, keine Versorgung: Grund a, „carrier_lost“).</item>
/// <item>KI: Polizei und Piraten kaufen nie Träger, die Marine nur im Krieg (<see cref="CombatModels.AtYard"/>); ein KI-Träger startet seine Jäger bei
/// Gefechtsbeginn von selbst. Der Spieler startet und ruft mit „Jäger starten“/„Jäger zurück“; seine nicht aktiven Träger handeln wie KI-Träger.</item>
/// </list>
/// Keine Zufallszahl (außer dem Gefechtsbeitritt selbst, den <see cref="Engage.Join"/> zieht).
/// </summary>
public static class Carriers
{
    /// <summary>Schalter an?</summary>
    public static bool Active(Universe u) => u.Rules.Carriers;

    /// <summary>Ist das ein Träger (Hangarplätze &gt; 0)?</summary>
    public static bool IsCarrier(Ship s) => s.Stats.HangarBays > 0;

    /// <summary>Hangarplätze des Trägers.</summary>
    public static int Bays(Ship carrier) => Math.Max(0, carrier.Stats.HangarBays);

    /// <summary>Eingeschleuste Jäger (Id-Reihenfolge; leer = keine).</summary>
    public static IReadOnlyList<Ship> Stowed(Universe u, ShipId carrier) => u.Hangars.TryGetValue(carrier, out var l) ? l : Array.Empty<Ship>();

    /// <summary>Gestartete Jäger dieses Trägers im Raum (Id-Reihenfolge).</summary>
    public static List<Ship> Launched(Universe u, ShipId carrier)
    {
        var list = new List<Ship>();
        if (carrier.Value == 0) return list;
        foreach (var s in u.ShipsById()) if (s.CarrierHome == carrier) list.Add(s);
        return list;
    }

    /// <summary>Freie Hangarplätze (Plätze − eingeschleust − gestartet).</summary>
    public static int FreeBays(Universe u, Ship carrier) => Bays(carrier) - Stowed(u, carrier.Id).Count - Launched(u, carrier.Id).Count;

    /// <summary>Taugt dieses Schiff als Bordjäger (Klasse S, bewaffnet)? null = ja.</summary>
    public static string? FighterIssue(Universe u, Ship s)
    {
        if (!u.ShipModels.TryGetValue(s.ModelKey, out var m) || !u.ShipClasses.TryGetValue(m.ClassKey, out var cls) || cls.Size != SlotSize.S)
            return "Nur Schiffe der Klasse S passen in den Hangar.";
        if (!s.Has(Capability.Combat)) return "Nur bewaffnete Schiffe (Jäger).";
        if (IsCarrier(s)) return "Ein Träger passt in keinen Hangar.";
        return null;
    }

    /// <summary>Kann <paramref name="fighter"/> jetzt in <paramref name="carrier"/> einschleusen (Spielerknopf)? null = ja.</summary>
    public static string? StowIssue(Universe u, Ship carrier, Ship fighter)
    {
        if (!Active(u)) return "Träger sind abgeschaltet.";
        if (!IsCarrier(carrier)) return "Kein Träger.";
        if (fighter.Id == carrier.Id) return "Der Träger kann sich nicht selbst aufnehmen.";
        if (fighter.Owner != carrier.Owner) return "Gehört nicht dem Besitzer des Trägers.";
        if (fighter.Id == u.PlayerShip) return "Das aktive Schiff kann nicht einschleusen.";
        if (fighter.CarrierHome.Value != 0 && fighter.CarrierHome != carrier.Id) return "Gehört zu einem anderen Träger.";
        if (FighterIssue(u, fighter) is { } fi) return fi;
        if (fighter.IsRental) return "Leihschiffe nicht.";
        if (fighter.IsDerelict) return "Herrenlose Schiffe nicht.";
        if (fighter.MissionCargo > 0f || InMission(u, fighter)) return "Das Schiff hat einen Auftrag.";
        if (u.Fights.IsEngaged(fighter.Id)) return "Das Schiff kämpft gerade.";
        if (fighter.Transit is not null || carrier.Transit is not null) return "Unterwegs nicht möglich.";
        if (fighter.Sector != carrier.Sector) return "Nicht im Sektor des Trägers.";
        bool bothDocked = carrier.DockedAt.Value != 0 && fighter.DockedAt == carrier.DockedAt;
        if (!bothDocked && Vec2.Distance(fighter.Position, carrier.Position) > u.Rules.CarrierDockRange)
            return $"Zu weit vom Träger (höchstens {u.Rules.CarrierDockRange:0} m).";
        if (fighter.CarrierHome != carrier.Id && FreeBays(u, carrier) <= 0) return "Hangar voll.";
        return null;
    }

    static bool InMission(Universe u, Ship s)
    {
        foreach (var m in u.Missions)
            if (m.State == MissionState.Active && m.Args.TryGetValue("ship", out var v) && v == s.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) return true;
        return false;
    }

    /// <summary>Schleust ein (Spielerknopf); false mit Grund = nichts geändert.</summary>
    public static bool TryStow(Universe u, Ship carrier, Ship fighter, out string message)
    {
        if (StowIssue(u, carrier, fighter) is { } issue) { message = issue; return false; }
        Stow(u, carrier, fighter, "manual");
        message = $"{ShipNames.Display(u, fighter)} im Hangar ({Bays(carrier) - FreeBays(u, carrier)} von {Bays(carrier)} Plätzen belegt).";
        return true;
    }

    /// <summary>Schleust ohne Prüfung ein (Regeln oben): Befehl endet, das Schiff verlässt das Universum und liegt im Hangar.</summary>
    public static void Stow(Universe u, Ship carrier, Ship fighter, string reason)
    {
        if (fighter.Command is not null) Commands.Cancel(u, fighter, "eingeschleust");
        u.RemoveShip(fighter.Id, "stowed");   // verlässt Gefecht, Verband, Tracker (kein Abgang: das Schiff besteht weiter)
        fighter.Transit = null; fighter.Order = null; fighter.Phase = TradePhase.Idle; fighter.MinePhase = MinePhase.Idle;
        fighter.DockedAt = default; fighter.DockedSince = -1; fighter.Squadron = default; fighter.Emergency = false;
        fighter.Sector = carrier.Sector; fighter.Position = carrier.Position;
        fighter.CarrierHome = carrier.Id; fighter.CarrierReturnSince = -1; fighter.CarrierFought = false;
        fighter.Cloaked = false; fighter.JammerOn = false; fighter.AnchorRock = 0; fighter.AnchorProgress = 0f;
        if (!u.Hangars.TryGetValue(carrier.Id, out var list)) u.Hangars[carrier.Id] = list = new List<Ship>();
        list.Add(fighter);
        list.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        u.Events.Publish(new CarrierFighterDocked(carrier.Id, fighter.Id, reason));
    }

    /// <summary>Kann der Träger jetzt Jäger starten? null = ja.</summary>
    public static string? LaunchIssue(Universe u, Ship carrier)
    {
        if (!Active(u)) return "Träger sind abgeschaltet.";
        if (!IsCarrier(carrier)) return "Kein Träger.";
        if (Stowed(u, carrier.Id).Count == 0) return "Keine Jäger an Bord.";
        if (carrier.Transit is not null) return "Unterwegs zwischen Sektoren nicht möglich.";
        if (carrier.DockedAt.Value != 0) return "Angedockt — erst abdocken.";
        return null;
    }

    /// <summary>Startet alle eingeschleusten Jäger; false mit Grund = nichts geändert.</summary>
    public static bool TryLaunch(Universe u, Ship carrier, out string message)
    {
        if (LaunchIssue(u, carrier) is { } issue) { message = issue; return false; }
        int n = Launch(u, carrier, auto: false);
        message = $"{n} Jäger gestartet — sie begleiten den Träger und greifen in seine Gefechte ein.";
        return true;
    }

    /// <summary>Startet ohne Prüfung (Regeln oben). Steht der Träger im Gefecht, treten die Jäger auf seiner Seite bei. Anzahl gestartet.</summary>
    public static int Launch(Universe u, Ship carrier, bool auto)
    {
        if (!u.Hangars.Remove(carrier.Id, out var list) || list.Count == 0) return 0;
        var e = u.Fights.Of(carrier.Id);
        var cp = e?.Of(carrier.Id) is { Active: true } p ? p : null;
        for (int i = 0; i < list.Count; i++)
        {
            var f = list[i];
            float a = MathF.PI / 2f + i * MathF.Tau / list.Count;
            var pos = carrier.Position + new Vec2(MathF.Cos(a), MathF.Sin(a)) * u.Rules.CarrierLaunchRadius;
            f.Sector = carrier.Sector;
            f.Position = CombatSpace.Clear(u, carrier.Sector, pos);
            f.Transit = null; f.DockedAt = default; f.DockedSince = -1;
            f.CarrierHome = carrier.Id; f.CarrierReturnSince = -1; f.CarrierFought = cp is not null;
            f.NextThinkTick = u.Tick;
            u.AddShip(f);
            Escort(u, f, carrier);
            if (e is not null && cp is not null && !u.Fights.IsEngaged(f.Id)) Reinforcements.JoinSide(u, e, f, cp.Side, "carrier", cp);
        }
        u.Events.Publish(new CarrierLaunched(carrier.Id, list.Count, auto));
        return list.Count;
    }

    /// <summary>Gestarteter Jäger begleitet seinen Träger (Eskorte; ohne Befehlskapazität — der Träger führt ihn).</summary>
    static void Escort(Universe u, Ship f, Ship carrier)
    {
        if (f.Command is EscortCommand ec && ec.Target == carrier.Id) return;
        if (!Commands.Give(u, f, new EscortCommand(carrier.Id), out _, ignoreCapacity: true))
        {
            f.Command = new EscortCommand(carrier.Id); f.CommandSince = u.Tick; f.CommandStep = 0;
        }
    }

    /// <summary>Kann der Träger seine gestarteten Jäger zurückrufen? null = ja.</summary>
    public static string? RecallIssue(Universe u, Ship carrier)
    {
        if (!Active(u)) return "Träger sind abgeschaltet.";
        if (!IsCarrier(carrier)) return "Kein Träger.";
        if (Launched(u, carrier.Id).Count == 0) return "Keine Jäger draußen.";
        return null;
    }

    /// <summary>„Jäger zurück“: alle gestarteten Jäger brechen ihre Gefechte ab und fliegen zum Träger. false mit Grund = nichts geändert.</summary>
    public static bool TryRecall(Universe u, Ship carrier, out string message)
    {
        if (RecallIssue(u, carrier) is { } issue) { message = issue; return false; }
        var list = Launched(u, carrier.Id);
        foreach (var f in list)
        {
            if (u.Fights.Of(f.Id) is { } e && e.Of(f.Id) is { Active: true } p)
            {
                Engage.Leave(u, e, p, CombatExit.Fled);
                Engage.Resume(u, f);
            }
            f.CarrierReturnSince = u.Tick;
            Escort(u, f, carrier);
        }
        message = $"{list.Count} Jäger kehren zum Träger zurück.";
        return true;
    }

    /// <summary>Steht der Träger in einem aktiven Gefecht?</summary>
    public static bool Engaged(Universe u, Ship s) => u.Fights.Of(s.Id)?.Of(s.Id) is { Active: true };

    /// <summary>Kurzzeile für Anzeigen: „4 an Bord · 0 draußen (4 Plätze)“.</summary>
    public static string Line(Universe u, Ship carrier)
    {
        int at = Stowed(u, carrier.Id).Count, out_ = Launched(u, carrier.Id).Count;
        return $"Jäger: {at} an Bord · {out_} draußen ({Bays(carrier)} Plätze)";
    }

    /// <summary>Eigene Schiffe, die gerade in diesen Träger einschleusen könnten (Id-Reihenfolge).</summary>
    public static List<Ship> Candidates(Universe u, Ship carrier)
    {
        var list = new List<Ship>();
        foreach (var s in u.ShipsById())
            if (s.Owner == carrier.Owner && s.Id != carrier.Id && s.CarrierHome.Value == 0 && StowIssue(u, carrier, s) is null) list.Add(s);
        return list;
    }

    /// <summary>
    /// KI-Marine: ein neu gekaufter Träger wird mit Bordjägern (<see cref="EconomyRules.CarrierNpcFighterModel"/>) gefüllt — so viele, wie die Kasse über der
    /// Rücklage zum Materialwert (Basispreise) bezahlt (Fraktionskasse → Senke, „carrier_fighters“). Anzahl.
    /// </summary>
    public static int EquipNpc(Universe u, Ship carrier, Actor payer)
    {
        if (!Active(u) || !IsCarrier(carrier) || !u.ShipModels.TryGetValue(u.Rules.CarrierNpcFighterModel, out var model)
            || !u.ShipClasses.TryGetValue(model.ClassKey, out var cls)) return 0;
        float price = CombatModels.MaterialValue(u, model);
        int n = 0;
        while (FreeBays(u, carrier) > 0 && FactionViability.Spendable(u, payer) >= price)
        {
            payer.Treasury.Credits -= price;
            var f = new Ship
            {
                Id = u.NewShipId(), Role = ShipRole.Fighter, Controller = Controller.Ai, Owner = carrier.Owner, Sector = carrier.Sector, Position = carrier.Position,
                ModelKey = model.Key, Slots = cls.Slots.Select(s => new ClassSlot { Type = s.Type, Size = s.Size, Facing = s.Facing, Arc = s.Arc }).ToList(),
                Fit = new List<string?>(model.Fit), HomeBase = carrier.HomeBase,
            };
            if (f.RecomputeStats(u)) { f.CargoCapacity = f.Stats.Cargo; f.Hull = f.MaxHull; f.FillFuel(); }
            AddToHangar(u, carrier, f);
            u.Events.Publish(new TreasuryUpkeep(payer.Id, "carrier_fighters", price, 0f));
            n++;
        }
        return n;
    }

    /// <summary>Legt ein neues (noch nicht im Universum befindliches) Schiff in den Hangar des Trägers (Kauf der KI, Startpaket). Ohne Prüfung.</summary>
    public static void AddToHangar(Universe u, Ship carrier, Ship f)
    {
        f.CarrierHome = carrier.Id; f.CarrierReturnSince = -1; f.CarrierFought = false;
        f.DockedAt = default; f.Sector = carrier.Sector; f.Position = carrier.Position;
        if (!u.Hangars.TryGetValue(carrier.Id, out var list)) u.Hangars[carrier.Id] = list = new List<Ship>();
        list.Add(f);
        list.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
    }

    /// <summary>Stärke eines Trägers mit vollen Hangars (KI-Modellwahl): Rumpf + Plätze × Stärke des KI-Bordjägers.</summary>
    public static float FighterStrength(Universe u)
    {
        if (!u.ShipModels.TryGetValue(u.Rules.CarrierNpcFighterModel, out var m) || !u.ShipClasses.TryGetValue(m.ClassKey, out var c)) return 0f;
        return CombatModels.Strength(ShipStats.Compute(c, m.Fit.Select(k => k is not null && u.Modules.TryGetValue(k, out var mod) ? mod : null).ToList()));
    }
}
