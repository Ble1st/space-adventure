using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Verbände (Phase 12, FLOTTEN.md Abschnitte 3/3a): frei anlegen, beitreten, verlassen, auflösen — begrenzt durch die Mitgliederzahl und die
/// Befehlskapazität des Besitzers. Der Befehl des Verbands geht an den Anführer: Bewegungsbefehle plant nur er (mit dem Tempo des langsamsten
/// Mitglieds, <see cref="Speed"/>), die übrigen folgen ihm; Arbeitsbefehle (Handel, Abbau, Bau) führt jedes fähige Mitglied selbst aus, die übrigen
/// folgen dem Anführer. Fällt der Anführer aus, übernimmt das nächste Mitglied samt Befehl. Die Formation ist nur Darstellung (<see cref="Offset"/>).
/// </summary>
public static class Squadrons
{
    /// <summary>Befehle, die jedes fähige Mitglied selbst ausführt (die übrigen folgen dem Anführer).</summary>
    public static bool IsWorkCommand(ShipCommand c) => c is TradeCommand or MineCommand or BuildCommand;

    /// <summary>Verbände eines Akteurs (nach Id).</summary>
    public static IReadOnlyList<Squadron> Of(Universe u, ActorId owner) =>
        u.Squadrons.Values.Where(s => s.Owner == owner).OrderBy(s => s.Id.Value).ToList();

    /// <summary>Verband des Schiffs (null = keiner).</summary>
    public static Squadron? Get(Universe u, Ship ship) => u.Squadrons.TryGetValue(ship.Squadron, out var sq) ? sq : null;

    /// <summary>Legt einen Verband mit diesem Schiff als Anführer an. false (mit Grund) = nichts geändert.</summary>
    public static Squadron? Create(Universe u, Ship leader, string name, out string message)
    {
        if (leader.Squadron.Value != 0) { message = "Das Schiff gehört schon zu einem Verband."; return null; }
        var (_, cap) = Commands.Capacity(u, leader.Owner);
        if (Of(u, leader.Owner).Count >= cap) { message = $"Befehlskapazität erreicht ({Commands.Count(cap, "Verband", "Verbände")}) – einem bestehenden Verband beitreten oder mehr Besitz (Stationen, Geld) hebt die Befehlsstufe."; return null; }
        var sq = new Squadron
        {
            Id = u.NewSquadronId(), Owner = leader.Owner, Leader = leader.Id, Members = { leader.Id },
            Name = name.Length > 0 ? name : $"Verband {u.NextSquadronId - 1}", HomeBase = leader.HomeBase.Value != 0 ? leader.HomeBase : leader.DockedAt,
            Rules = leader.Engagement,
        };
        u.Squadrons[sq.Id] = sq;
        leader.Squadron = sq.Id;
        u.Events.Publish(new SquadronChanged(sq.Id, "created", leader.Id));
        message = $"{sq.Name} gebildet, Anführer {OrderMove.ShipName(u, leader.Id)}.";
        return sq;
    }

    /// <summary>Schiff tritt dem Verband bei und folgt dem Anführer. false (mit Grund) = nichts geändert.</summary>
    public static bool Join(Universe u, Squadron sq, Ship ship, out string message)
    {
        if (ship.Squadron == sq.Id) { message = "Schon Mitglied."; return false; }
        if (ship.Squadron.Value != 0) { message = "Das Schiff gehört schon zu einem anderen Verband."; return false; }
        if (ship.Owner != sq.Owner) { message = "Das Schiff gehört nicht dem Besitzer des Verbands."; return false; }
        if (ship.Id == u.PlayerShip) { message = "Das aktive Schiff kann nur Anführer sein."; return false; }
        if (sq.Members.Count >= u.Rules.SquadronMaxMembers) { message = $"Der Verband ist voll ({u.Rules.SquadronMaxMembers} Schiffe)."; return false; }
        if (ship.Command is not null) Commands.Cancel(u, ship, "Verband beigetreten");
        sq.Members.Add(ship.Id);
        ship.Squadron = sq.Id;
        ship.Engagement = sq.Rules;
        AssignMember(u, sq, ship);
        u.Events.Publish(new SquadronChanged(sq.Id, "joined", ship.Id));
        message = $"{OrderMove.ShipName(u, ship.Id)} ist {sq.Name} beigetreten.";
        return true;
    }

    /// <summary>Schiff verlässt den Verband (behält einen eigenen Arbeitsbefehl, ein Folgebefehl entfällt); der Anführer folgt nach.</summary>
    public static void Leave(Universe u, Ship ship)
    {
        if (Get(u, ship) is not { } sq) return;
        if (ship.Command is FollowCommand f && f.Target == sq.Leader) Commands.Cancel(u, ship, "Verband verlassen");
        Detach(u, sq, ship, inheritCommand: false);
        u.Events.Publish(new SquadronChanged(sq.Id, "left", ship.Id));
    }

    /// <summary>Löst den Verband auf: Folgebefehle entfallen, Arbeitsbefehle bleiben.</summary>
    public static void Disband(Universe u, Squadron sq)
    {
        foreach (var id in sq.Members.ToList())
            if (u.Ships.TryGetValue(id, out var s))
            {
                if (s.Command is FollowCommand f && f.Target == sq.Leader) Commands.Cancel(u, s, "Verband aufgelöst");
                s.Squadron = default;
            }
        u.Squadrons.Remove(sq.Id);
        u.Events.Publish(new SquadronChanged(sq.Id, "disbanded", sq.Leader));
    }

    /// <summary>Ein Schiff ist weg (zerstört, verkauft): aus dem Verband nehmen; fiel der Anführer aus, übernimmt das nächste Mitglied seinen Befehl.</summary>
    public static void OnShipRemoved(Universe u, Ship ship)
    {
        if (Get(u, ship) is not { } sq) return;
        Detach(u, sq, ship, inheritCommand: true);
    }

    static void Detach(Universe u, Squadron sq, Ship ship, bool inheritCommand)
    {
        sq.Members.Remove(ship.Id);
        ship.Squadron = default;
        if (sq.Members.Count == 0) { u.Squadrons.Remove(sq.Id); u.Events.Publish(new SquadronChanged(sq.Id, "disbanded", ship.Id)); return; }
        if (sq.Leader != ship.Id) return;
        var next = sq.Members.Select(id => u.Ships.GetValueOrDefault(id)).FirstOrDefault(s => s is not null);
        if (next is null) { u.Squadrons.Remove(sq.Id); return; }
        sq.Leader = next.Id;
        // der neue Anführer übernimmt einen Bewegungsbefehl des alten (nur bei Ausfall), alle Folger hängen sich an ihn
        var inherited = inheritCommand ? ship.Command : null;
        if (next.Command is FollowCommand nf && nf.Target == ship.Id) Commands.Cancel(u, next, "neuer Anführer");
        if (inherited is not null && !IsWorkCommand(inherited) && Commands.Check(u, next, inherited) is null)
            Commands.Give(u, next, inherited, out _, ignoreCapacity: true);
        foreach (var id in sq.Members.Skip(1))
            if (u.Ships.TryGetValue(id, out var m) && m.Command is FollowCommand f && f.Target == ship.Id)
                Commands.Give(u, m, new FollowCommand(next.Id), out _, ignoreCapacity: true);
        u.Events.Publish(new SquadronChanged(sq.Id, "leader", next.Id));
    }

    /// <summary>
    /// Befehl an den Verband: Bewegungsbefehle an den Anführer (die übrigen folgen ihm), Arbeitsbefehle an jedes fähige Mitglied (die übrigen folgen).
    /// Ist der Anführer das aktive Schiff, folgen alle ihm (Bewegungsbefehle gehen dann nicht). false = der Anführer konnte den Befehl nicht annehmen.
    /// </summary>
    public static bool Command(Universe u, Squadron sq, ShipCommand command, out string message)
    {
        if (!u.Ships.TryGetValue(sq.Leader, out var leader)) { message = "Kein Anführer."; return false; }
        bool work = IsWorkCommand(command);
        if (!work)
        {
            if (!Commands.Give(u, leader, command, out message, ignoreCapacity: true)) return false;
            foreach (var id in sq.Members.Skip(1))
                if (u.Ships.TryGetValue(id, out var m)) FollowLeader(u, sq, m);
        }
        else
        {
            int doing = 0;
            foreach (var id in sq.Members)
            {
                if (!u.Ships.TryGetValue(id, out var m) || m.Id == u.PlayerShip) continue;
                if (Commands.Check(u, m, command) is null) { Commands.Give(u, m, command, out _, ignoreCapacity: true); doing++; }
                else if (m.Id != leader.Id) FollowLeader(u, sq, m);
                else Commands.Give(u, m, new HoldCommand(), out _, ignoreCapacity: true);
            }
            if (doing == 0) { message = "Kein Mitglied kann diesen Befehl ausführen."; return false; }
            message = $"{sq.Name}: {doing} Schiff{(doing == 1 ? "" : "e")} — {CommandNames.Kind(command.Kind)}, die übrigen folgen dem Anführer.";
        }
        u.Events.Publish(new SquadronChanged(sq.Id, "command", leader.Id));
        if (!work) message = $"{sq.Name}: {message}";
        return true;
    }

    /// <summary>Neues Mitglied: folgt dem Anführer (führt der Verband einen Arbeitsbefehl aus, den das Mitglied kann, übernimmt es diesen).</summary>
    static void AssignMember(Universe u, Squadron sq, Ship m)
    {
        if (!u.Ships.TryGetValue(sq.Leader, out var leader)) return;
        if (leader.Command is { } lc && IsWorkCommand(lc) && Commands.Check(u, m, lc) is null) { Commands.Give(u, m, lc, out _, ignoreCapacity: true); return; }
        FollowLeader(u, sq, m);
    }

    static void FollowLeader(Universe u, Squadron sq, Ship m)
    {
        if (m.Id == sq.Leader || m.Id == u.PlayerShip) return;
        if (m.Command is FollowCommand f && f.Target == sq.Leader) return;
        Commands.Give(u, m, new FollowCommand(sq.Leader), out _, ignoreCapacity: true);
    }

    /// <summary>Tempo des Verbands: das langsamste Mitglied (null = kein Verband oder allein).</summary>
    public static float? Speed(Universe u, Ship ship)
    {
        if (Get(u, ship) is not { } sq || sq.Members.Count < 2) return null;
        float v = float.MaxValue;
        foreach (var id in sq.Members) if (u.Ships.TryGetValue(id, out var m)) v = MathF.Min(v, Travel.SpeedOf(m));
        return v < float.MaxValue ? v : null;
    }

    /// <summary>Verhaltensregel, die für das Schiff gilt (im Verband die des Verbands).</summary>
    public static Engagement RulesOf(Universe u, Ship ship) => Get(u, ship)?.Rules ?? ship.Engagement;

    /// <summary>Setzt die Verhaltensregel des Verbands (und seiner Mitglieder).</summary>
    public static void SetRules(Universe u, Squadron sq, Engagement rules)
    {
        sq.Rules = rules;
        foreach (var id in sq.Members) if (u.Ships.TryGetValue(id, out var m)) m.Engagement = rules;
    }

    /// <summary>
    /// Versatz eines Mitglieds in der Formation relativ zum Anführer (nur Darstellung; Index 0 = Anführer, Richtung = Flugrichtung des Anführers).
    /// </summary>
    public static Vec2 Offset(Formation f, int index, Vec2 dir) => Offset(f, index, dir, 7f);

    /// <summary>
    /// Runde 12 (Handy-Test: zwei Fregatten im Kriegstest standen übereinander): Abstand der Formationsplätze nach der größten Klasse im Verband
    /// (S 7 · M 10 · L 16 · XL 24 Einheiten) — nur Darstellung.
    /// </summary>
    public static float FormationGap(Universe u, Squadron sq)
    {
        float gap = 7f;
        foreach (var id in sq.Members)
            if (u.Ships.TryGetValue(id, out var s) && u.ShipModels.TryGetValue(s.ModelKey, out var m) && u.ShipClasses.TryGetValue(m.ClassKey, out var c))
                gap = MathF.Max(gap, c.Size switch { SlotSize.S => 7f, SlotSize.M => 10f, SlotSize.L => 16f, _ => 24f });
        return gap;
    }

    /// <summary>Formationsplatz <paramref name="index"/> hinter/neben dem Anführer mit Abstand <paramref name="gap"/>.</summary>
    public static Vec2 Offset(Formation f, int index, Vec2 dir, float gap)
    {
        if (index <= 0) return default;
        var back = dir.Normalized() * -1f;
        var side = new Vec2(-back.Y, back.X);
        int k = (index + 1) / 2, sign = index % 2 == 1 ? 1 : -1;
        return f switch
        {
            Formation.Wedge => back * (gap * k) + side * (gap * k * sign),
            Formation.Star => Vec2.FromAngle(index * MathF.PI * 2f / 7f) * (gap * 1.4f),
            Formation.Box => back * (gap * ((index - 1) / 2 + 1)) + side * (gap * 0.5f * sign),
            _ => back * (gap * index),
        };
    }
}

/// <summary>
/// Phase 12: Schutzbefehle in Begegnungen (FLOTTEN.md Verhaltensregeln): Eskorten verteidigen ihr Ziel, Wachen die Schiffe im Sektor ihrer Station —
/// nur mit Regel „Verteidigen“ oder „Angriff“. Streifen und Wachen mit Regel „Angriff“ stellen lauernde Kaperschiffe in ihrem Sektor wie die Polizei.
/// Feste Reihenfolge (Id); ohne solche Befehle leer (NPC-Welten unverändert).
/// </summary>
public static class FleetGuards
{
    static bool Fights(Universe u, Ship s) => s.Has(Capability.Combat) && Squadrons.RulesOf(u, s) is Engagement.Defend or Engagement.Attack;

    static bool InSector(Universe u, Ship s, SectorId sector) => !s.Emergency && ThreatMap.Where(u, s).Sector == sector;

    /// <summary>Verteidiger eines Opfers im Sektor: Eskorten des Opfers und Wachen einer Station dieses Sektors (ohne das Opfer).</summary>
    public static List<Ship> Escorts(Universe u, Ship victim, SectorId sector)
    {
        var list = new List<Ship>();
        foreach (var s in u.Ships.Values)
        {
            if (s.Id == victim.Id || s.Command is null || !Fights(u, s)) continue;
            bool guards = s.Command switch
            {
                EscortCommand e => e.Target == victim.Id,
                GuardCommand g => u.Stations.TryGetValue(g.Station, out var st) && st.Sector == sector,
                _ => false,
            };
            if (guards && InSector(u, s, sector)) list.Add(s);
        }
        return list.OrderBy(s => s.Id.Value).ToList();
    }

    /// <summary>Jäger im Sektor: Streifen (Sektor in der Runde) und Wachen mit Regel „Angriff“, die gerade dort sind.</summary>
    public static List<Ship> Hunters(Universe u, SectorId sector)
    {
        var list = new List<Ship>();
        foreach (var s in u.Ships.Values)
        {
            if (s.Command is null || !s.Has(Capability.Combat) || Squadrons.RulesOf(u, s) != Engagement.Attack) continue;
            bool duty = s.Command switch
            {
                PatrolCommand p => p.Sectors.Contains(sector),
                GuardCommand g => u.Stations.TryGetValue(g.Station, out var st) && st.Sector == sector,
                _ => false,
            };
            if (duty && InSector(u, s, sector)) list.Add(s);
        }
        return list.OrderBy(s => s.Id.Value).ToList();
    }
}
