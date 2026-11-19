using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Ein Sammelpunkt hat einen Verband gebildet (bzw. ist gescheitert). Nur Anzeige; nicht gespeichert.</summary>
public sealed record RallyFormed(ShipId Leader, SquadronId Squadron, int Members, string Text) : SimEvent;

/// <summary>
/// Ausbau Punkt 1b: Sammelpunkt und Konvoi — Gruppenbefehle der Spielerflotte, die einen Verband bilden. Beide prüfen vorab die Verbandsgrenzen (Mitglieder
/// <see cref="EconomyRules.SquadronMaxMembers"/>, Verbandskapazität der Befehlsstufe); der Verband zählt danach als ein Verband, nicht als Einzelbefehle.
/// </summary>
public static class FleetFormations
{
    /// <summary>Wer darf in eine neue Gruppe: eigene, nicht aktive Schiffe ohne Verband, kein Leihschiff, nicht im Gefecht.</summary>
    public static string? MemberIssue(Universe u, Ship s)
    {
        if (!s.IsPlayer) return "Fremdes Schiff.";
        if (s.Id == u.PlayerShip) return "Das aktive Schiff steuerst du selbst.";
        if (s.Squadron.Value != 0) return $"{OrderMove.ShipName(u, s.Id)} gehört schon zu einem Verband.";
        if (u.Fights.IsEngaged(s.Id)) return $"{OrderMove.ShipName(u, s.Id)} ist im Gefecht.";
        return null;
    }

    /// <summary>Gemeinsame Gruppenprüfung (Mitgliederzahl, Verbandskapazität, je Schiff <see cref="MemberIssue"/>). null = in Ordnung.</summary>
    public static string? GroupIssue(Universe u, IReadOnlyList<Ship> ships)
    {
        if (ships.Count < 2) return "Mindestens zwei Schiffe wählen.";
        if (ships.Count > u.Rules.SquadronMaxMembers) return $"Höchstens {u.Rules.SquadronMaxMembers} Schiffe je Verband.";
        foreach (var s in ships) if (MemberIssue(u, s) is { } m) return m;
        var (_, cap) = Commands.Capacity(u, u.PlayerActor);
        if (Squadrons.Of(u, u.PlayerActor).Count >= cap) return $"Befehlskapazität erreicht ({Commands.Count(cap, "Verband", "Verbände")}) – erst einen Verband auflösen.";
        return null;
    }

    /// <summary>
    /// Sammelpunkt: alle Schiffe fliegen zur Station <paramref name="at"/>; der erste ist der Anführer, er bildet dort den Verband, sobald alle da sind.
    /// false (mit Grund) = nichts geändert.
    /// </summary>
    public static bool StartRally(Universe u, Ship leader, IEnumerable<Ship> others, Station at, out string message)
    {
        var group = new List<Ship> { leader };
        group.AddRange(others.Where(s => s.Id != leader.Id).DistinctBy(s => s.Id.Value));
        if (GroupIssue(u, group) is { } issue) { message = issue; return false; }
        var cmd = new RallyCommand(at.Id, leader.Id);
        foreach (var s in group) if (Commands.Check(u, s, cmd) is { } why) { message = $"{OrderMove.ShipName(u, s.Id)}: {why}"; return false; }
        foreach (var s in group) Commands.Give(u, s, cmd, out _, ignoreCapacity: true);   // vorübergehend; danach ein Verband
        message = $"Sammelpunkt {at.Name}: {group.Count} Schiffe fliegen hin und bilden dort einen Verband.";
        return true;
    }

    /// <summary>Am Sammelpunkt: der Anführer bildet den Verband, die übrigen Schiffe der Gruppe treten bei (wer nicht beitreten kann, behält keinen Befehl).</summary>
    public static void FormAtRally(Universe u, Ship leader, IReadOnlyList<Ship> group)
    {
        var sq = Squadrons.Create(u, leader, "", out var msg);
        if (sq is null)
        {
            foreach (var s in group) if (s.Id != leader.Id && s.Command is RallyCommand) Commands.Cancel(u, s, "Sammelpunkt gescheitert");
            u.Events.Publish(new RallyFormed(leader.Id, default, 0, "Sammelpunkt: kein Verband — " + msg));
            return;
        }
        int n = 1;
        foreach (var s in group)
        {
            if (s.Id == leader.Id || !u.Ships.ContainsKey(s.Id)) continue;
            if (Squadrons.Join(u, sq, s, out _)) n++;
            else if (s.Command is RallyCommand) Commands.Cancel(u, s, "Verband voll");
        }
        u.Events.Publish(new RallyFormed(leader.Id, sq.Id, n, $"{sq.Name} am Sammelpunkt gebildet ({n} Schiffe)."));
    }

    /// <summary>Langsamster Händler (Tempo, dann Id) — der Anführer eines Konvois.</summary>
    public static Ship SlowestTrader(IReadOnlyList<Ship> traders) =>
        traders.OrderBy(s => Travel.SpeedOf(s)).ThenBy(s => s.Id.Value).First();

    /// <summary>
    /// Konvoi: aus den gewählten Schiffen werden Händler (Frachtraum) und Geleitschutz (bewaffnet ohne Frachtraum). Verband mit dem langsamsten Händler als
    /// Anführer; alle Händler bekommen den Konvoibefehl (in den Grenzen), der Geleitschutz begleitet den Anführer. false (mit Grund) = nichts geändert.
    /// </summary>
    public static bool StartConvoy(Universe u, IReadOnlyList<Ship> ships, AutoLimits limits, out string message)
    {
        var all = ships.DistinctBy(s => s.Id.Value).ToList();
        if (GroupIssue(u, all) is { } issue) { message = issue; return false; }
        var traders = all.Where(s => s.Has(Capability.Cargo)).ToList();
        var escorts = all.Where(s => !s.Has(Capability.Cargo) && s.Has(Capability.Combat)).ToList();
        var rest = all.Where(s => !traders.Contains(s) && !escorts.Contains(s)).ToList();
        if (traders.Count == 0) { message = "Ein Konvoi braucht mindestens einen Händler (Frachtraum)."; return false; }
        if (rest.Count > 0) { message = $"{OrderMove.ShipName(u, rest[0].Id)} hat weder Frachtraum noch Waffen."; return false; }
        var leader = SlowestTrader(traders);
        var sq = Squadrons.Create(u, leader, $"Konvoi {u.NextSquadronId}", out var cm);
        if (sq is null) { message = cm; return false; }
        foreach (var s in all) if (s.Id != leader.Id) Squadrons.Join(u, sq, s, out _);
        var cmd = new ConvoyCommand(limits);
        foreach (var t in traders) Commands.Give(u, t, cmd, out _, ignoreCapacity: true);
        foreach (var e in escorts) Commands.Give(u, e, new EscortCommand(leader.Id), out _, ignoreCapacity: true);
        message = $"{sq.Name}: {traders.Count} Händler (führt {OrderMove.ShipName(u, leader.Id)}), {escorts.Count} Geleitschutz.";
        return true;
    }
}
