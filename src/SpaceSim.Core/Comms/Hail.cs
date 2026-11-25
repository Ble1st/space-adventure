using SpaceSim.Core.Combat;
using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Comms;

/// <summary>Funk: wen der Spieler anruft (ein Schiff oder eine Station im Sektor).</summary>
public readonly record struct HailTarget(ShipId Ship, StationId Station)
{
    public static HailTarget OfShip(ShipId s) => new(s, default);
    public static HailTarget OfStation(StationId s) => new(default, s);
}

/// <summary>Funk: ein Gesprächsthema (Taste). <see cref="Possible"/> false = gesperrt mit <see cref="Reason"/>.</summary>
public readonly record struct HailTopic(string Key, string Text, string Detail, bool Possible, string? Reason);

/// <summary>Funk: Gesprächsansicht — wer spricht, sein Gruß und die Themen.</summary>
/// <param name="Captain">Piloten: Kapitänszeile in kürzer werdenden Formen (null = kein benannter Kapitän).</param>
public sealed record HailView(string Who, string Greeting, IReadOnlyList<HailTopic> Topics, IReadOnlyList<string>? Captain = null);

/// <summary>Funk: Ergebnis eines Themas. <see cref="OpenShipMenu"/> = die Ansicht soll das Schiffsmenü des angerufenen eigenen Schiffs öffnen.</summary>
public readonly record struct HailResult(bool Success, string Message, bool OpenShipMenu = false);

/// <summary>Funk: der Spieler hat ein Thema ausgeführt (Anzeige/Statistik; nicht gespeichert).</summary>
public sealed record HailPerformed(HailTarget Target, string Topic, bool Success, string Message) : SimEvent;

/// <summary>
/// Funk Stufe 1 (UI-Umbau Abschnitt 7: „direkt mit dem gewählten Ziel sprechen“). Reine Themenliste <see cref="View"/> und eine Ausführung
/// <see cref="Execute"/> mit strengen Schlüsseln — wie die Stationsdienste: ein unbekanntes oder gesperrtes Thema ändert nichts und veröffentlicht nichts.
/// <list type="bullet">
/// <item>Kaperer, die dich gerade ankündigen oder bekämpfen: <c>bribe</c> (Schutzgeld aus der Kasse an die Piratenkasse, sie drehen ab),
/// <c>threaten</c> (nur vor dem Angriff; eine Zufallszahl: Erfolg nach Kampfstärke, sonst greifen sie sofort an), <c>stall</c> (einmal je Warnung:
/// die Frist wächst um die halbe Warnzeit), <c>drop</c> (= Ergeben, <see cref="PlayerSurrender"/>).</item>
/// <item>Polizei, die dich kontrollieren will: <c>submit</c> (= Ergeben bei der Polizei); jede Polizei: <c>news</c> (Lage im Sektor).</item>
/// <item>Andere fremde Schiffe: <c>news</c>.</item>
/// <item>Stationen: <c>dock</c> (Andockerlaubnis = Andockregel), <c>fine</c> (offene Strafe bei der Stationsfraktion per Funk zahlen, <see cref="FineAction"/>), <c>news</c>.</item>
/// <item>Herrenlose Schiffe: <c>claim</c> (übernehmen, <see cref="Derelicts.Claim"/>).</item>
/// <item>Eigene Schiffe: <c>menu</c> (Schiffsmenü), <c>follow</c> (folge mir), <c>hold</c> (Position halten), <c>home</c> (zur Heimatbasis) — über <see cref="Commands.Give"/>.</item>
/// </list>
/// </summary>
public static class Hail
{
    /// <summary>Grundbetrag des Schutzgelds; dazu <see cref="BribeCargoShare"/> des Frachtwerts (Basispreis).</summary>
    public const float BribeBase = 200f;
    public const float BribeCargoShare = 0.3f;

    public static Ship? Me(Universe u) => u.Ships.TryGetValue(u.PlayerShip, out var s) ? s : null;

    /// <summary>Schutzgeld für dieses Gespräch (aufgerundet auf 10 Cr).</summary>
    public static float Bribe(Universe u, Ship me) => MathF.Ceiling((BribeBase + BribeCargoShare * ThreatMap.CargoValue(u, me)) / 10f) * 10f;

    /// <summary>Erfolgschance der Drohung: eigene Stärke ÷ (eigene + Kaperstärke), begrenzt auf 0,1–0,8.</summary>
    public static float ThreatChance(Ship me, Ship raider)
    {
        float a = CombatRules.Strength(me), b = CombatRules.Strength(raider);
        return Math.Clamp(a / MathF.Max(0.01f, a + b), 0.1f, 0.8f);
    }

    /// <summary>Angekündigter Überfall dieses Kaperers auf den Spieler (null = keiner).</summary>
    static PendingEncounter? PendingRaid(Universe u, Ship me, ShipId raider) =>
        u.PendingEncounters.FirstOrDefault(x => x.Target == me.Id && x.Source == raider && x.Kind == EncounterKind.Raid);

    /// <summary>Laufendes Gefecht des Spielers, in dem <paramref name="raider"/> aktiv gegen ihn kämpft (Überfall), sonst null.</summary>
    static Encounter? RaidFight(Universe u, Ship me, ShipId raider)
    {
        if (u.Fights.Of(me.Id) is not { } e || e.Cause == EncounterCause.War) return null;
        if (e.Of(me.Id) is not { Active: true } p || e.Of(raider) is not { Active: true } q || q.Side == p.Side) return null;
        return q.Tactic == CombatTactics.Raider ? e : null;
    }

    static string Name(Universe u, Ship s) =>
        ActorNames.Tagged(u, s.Owner, $"{(u.ShipModels.TryGetValue(s.ModelKey, out var m) ? m.Name : "Schiff")} #{s.Id.Value}");

    /// <summary>Gesprächsansicht für ein Ziel (rein). Unbekanntes oder nicht erreichbares Ziel = Ansicht ohne Themen.</summary>
    public static HailView View(Universe u, HailTarget t)
    {
        var v = ViewCore(u, t);
        // Piloten: benannte Kapitäne melden sich mit Rang und Namen (nur Anzeige)
        if (t.Station.Value == 0 && u.Ships.TryGetValue(t.Ship, out var cs) && Pilots.PilotBook.CaptainLineOptions(u, cs) is { Count: > 0 } cap && v.Topics.Count > 0)
            return v with { Captain = cap };
        return v;
    }

    static HailView ViewCore(Universe u, HailTarget t)
    {
        var me = Me(u);
        if (me is null) return new HailView("—", "Kein Schiff.", Array.Empty<HailTopic>());
        if (t.Station.Value != 0)
        {
            if (!u.Stations.TryGetValue(t.Station, out var st) || st.Sector != me.Sector)
                return new HailView("—", "Keine Verbindung.", Array.Empty<HailTopic>());
            var list = new List<HailTopic>();
            string? dock = Docking.Issue(u, st, me);
            list.Add(new HailTopic("dock", "Andockerlaubnis", dock is null ? "Andocken erlaubt." : dock, true, null));
            if (u.FactionOf(st) is { } f && u.Player.Wanted.ContainsKey(f) && u.IsNpcFaction(f))
            {
                var fine = new FineAction().Preview(u, $"{FineAction.KeyId}:{f.Value}");
                list.Add(new HailTopic("fine", "Strafe per Funk zahlen", fine.Preview, fine.Possible, fine.Reason));
            }
            list.Add(new HailTopic("news", "Lage erfragen", "Was gibt es Neues im Sektor?", true, null));
            string owner = ActorNames.Tagged(u, st.Owner, st.Name);
            return new HailView(owner, $"Hier {st.Name}. Was können wir für Sie tun?", list);
        }
        if (!u.Ships.TryGetValue(t.Ship, out var s) || s.Id == me.Id || s.Sector != me.Sector)
            return new HailView("—", "Keine Verbindung.", Array.Empty<HailTopic>());
        var topics = new List<HailTopic>();
        if (s.IsDerelict)   // herrenloses Schiff: niemand antwortet — nah genug heran und übernehmen
        {
            string? claim = Derelicts.ClaimIssue(u, me, s);
            topics.Add(new HailTopic("claim", "Übernehmen", claim ?? $"Hülle {s.Hull / MathF.Max(1f, s.MaxHull) * 100f:0} % — das Schiff gehört danach dir.", claim is null, claim));
            string who = s.Owner == u.FreeActor ? $"{Derelicts.Name(u, s)} (herrenlos)" : Name(u, s);
            return new HailView(who, "Keine Antwort — an Bord ist niemand.", topics);
        }
        if (s.Owner == u.PlayerActor)
        {
            topics.Add(new HailTopic("menu", "Schiffsmenü", "Werte, Module, Fracht, Befehl.", true, null));
            foreach (var (key, text, cmd) in OwnCommands(u, me))
            {
                string? issue = Commands.Check(u, s, cmd);
                topics.Add(new HailTopic(key, text, issue ?? CmdText(key), issue is null, issue));
            }
            return new HailView(Name(u, s), "Eigenes Schiff meldet sich. Befehle?", topics);
        }
        if (ThreatMap.IsRaider(s))
        {
            var pending = PendingRaid(u, me, s.Id);
            var fight = RaidFight(u, me, s.Id);
            if (pending is null && fight is null)
            {
                topics.Add(new HailTopic("news", "Lage erfragen", "Vielleicht verraten sie etwas.", true, null));
                return new HailView(Name(u, s), "Was willst du? Verschwinde.", topics);
            }
            float bribe = Bribe(u, me);
            string? payIssue = u.Player.Treasury.Credits < bribe ? $"Kasse reicht nicht ({bribe:0} Cr nötig)." : null;
            topics.Add(new HailTopic("bribe", $"Schutzgeld zahlen ({bribe:0} Cr)", "Sie nehmen das Geld und drehen ab.", payIssue is null, payIssue));
            if (pending is not null)
            {
                float ch = ThreatChance(me, s);
                topics.Add(new HailTopic("threaten", $"Drohen ({ch * 100f:0} %)", "Gelingt es, drehen sie ab — sonst greifen sie sofort an.", true, null));
                topics.Add(pending.Stalled
                    ? new HailTopic("stall", "Hinhalten", "", false, "Sie lassen sich nicht noch einmal hinhalten.")
                    : new HailTopic("stall", "Hinhalten", $"Verschafft dir etwa {u.Rules.PlayerWarningTicks / 2 / SimClock.TicksPerSecond:0} s.", true, null));
            }
            string? dropIssue = PlayerSurrender.Issue(u);
            topics.Add(new HailTopic("drop", "Fracht abwerfen", $"Du wirfst {u.Rules.PlayerSurrenderDropShare * 100f:0} % jeder Ware ab, sie drehen ab.", dropIssue is null, dropIssue));
            return new HailView(Name(u, s), fight is not null ? "Gib auf, dann lassen wir dich leben!" : "Halt! Fracht her oder wir holen sie uns.", topics);
        }
        if (ThreatMap.IsPolice(s))
        {
            bool control = u.PendingEncounters.Any(x => x.Target == me.Id && x.Kind == EncounterKind.Intercept && x.Source == s.Id)
                || u.Fights.Of(me.Id) is { } e && e.Of(s.Id) is { Active: true } && e.Of(me.Id) is { Active: true } pm && e.Of(s.Id)!.Side != pm.Side;
            if (control)
            {
                string? issue = PlayerSurrender.Issue(u);
                topics.Add(new HailTopic("submit", "Kontrolle akzeptieren", "Schmuggelware wird beschlagnahmt, die Strafe eingezogen.", issue is null, issue));
            }
            topics.Add(new HailTopic("news", "Lage erfragen", "Was meldet die Streife?", true, null));
            return new HailView(Name(u, s), control ? "Polizei! Halten Sie an und lassen Sie sich kontrollieren." : "Polizei. Alles ruhig?", topics);
        }
        topics.Add(new HailTopic("news", "Lage erfragen", "Was gibt es Neues?", true, null));
        return new HailView(Name(u, s), "Ja? Wir hören.", topics);
    }

    static string CmdText(string key) => key switch { "follow" => "folgt dir", "hold" => "hält Position", _ => "fliegt zur Heimatbasis" };

    static IEnumerable<(string Key, string Text, ShipCommand Cmd)> OwnCommands(Universe u, Ship me)
    {
        yield return ("follow", "Folge mir", new FollowCommand(me.Id));
        yield return ("hold", "Position halten", new HoldCommand());
        yield return ("home", "Zur Heimatbasis", new ReturnCommand());
    }

    /// <summary>Führt ein Thema aus. Unbekanntes/gesperrtes Thema: kein Zustand, kein Ereignis.</summary>
    public static HailResult Execute(Universe u, HailTarget t, string key)
    {
        var view = View(u, t);
        var topic = view.Topics.FirstOrDefault(x => x.Key == key);
        if (topic.Key is null) return new HailResult(false, $"Kein Thema „{key}“.");
        if (!topic.Possible) return new HailResult(false, topic.Reason ?? "Nicht möglich.");
        var me = Me(u)!;
        HailResult r = key switch
        {
            "news" => new HailResult(true, News(u, me)),
            "dock" => new HailResult(true, topic.Detail),
            "fine" => Fine(u, t),
            "menu" => new HailResult(true, "Schiffsmenü geöffnet.", true),
            "claim" => Claim(u, t, me),
            "follow" or "hold" or "home" => Own(u, t, key, me),
            "bribe" => Bribe(u, t, me),
            "threaten" => Threaten(u, t, me),
            "stall" => Stall(u, t, me),
            "drop" or "submit" => Surrender(u),
            _ => new HailResult(false, $"Kein Thema „{key}“."),
        };
        if (r.Success) u.Events.Publish(new HailPerformed(t, key, true, r.Message));
        return r;
    }

    static string News(Universe u, Ship me)
    {
        var view = SecurityInfo.ForSector(u, me.Sector, me);
        var sec = u.Sectors[me.Sector];
        string owner = u.Factions.TryGetValue(sec.Owner, out var f) ? f.Name : "niemand";
        string pirates = u.PendingEncounters.Any(x => x.Sector == me.Sector) ? " Kaperer sind unterwegs!" : "";
        return $"{sec.Name}: {view.Summary}, {view.Patrols} Streife(n), Besitzer {owner}.{pirates}";
    }

    static HailResult Fine(Universe u, HailTarget t)
    {
        var st = u.Stations[t.Station];
        var f = u.FactionOf(st)!.Value;
        var res = new FineAction().Execute(u, $"{FineAction.KeyId}:{f.Value}");
        return new HailResult(res.Success, res.Message);
    }

    static HailResult Claim(Universe u, HailTarget t, Ship me)
    {
        bool ok = Derelicts.Claim(u, me, u.Ships[t.Ship], out var msg);
        return new HailResult(ok, msg);
    }

    static HailResult Own(Universe u, HailTarget t, string key, Ship me)
    {
        var s = u.Ships[t.Ship];
        ShipCommand cmd = key switch { "follow" => new FollowCommand(me.Id), "hold" => new HoldCommand(), _ => new ReturnCommand() };
        bool ok = Commands.Give(u, s, cmd, out var msg);
        return new HailResult(ok, ok ? $"Verstanden: {CmdText(key)}." : msg);
    }

    /// <summary>Lässt die Kaperer abdrehen: Warnung weg bzw. Gefecht beendet, der Kaperer fliegt heim.</summary>
    static void Release(Universe u, Ship me, Ship raider)
    {
        var pending = PendingRaid(u, me, raider.Id);
        if (pending is not null) u.PendingEncounters.Remove(pending);
        if (RaidFight(u, me, raider.Id) is { } e)
        {
            if (e.Of(me.Id) is { Active: true } p) Engage.Leave(u, e, p, CombatExit.Surrendered);
            CombatSystem.Finish(u, e, "bribed");
        }
        PirateCommands.AfterFight(u, raider);
    }

    static HailResult Bribe(Universe u, HailTarget t, Ship me)
    {
        var raider = u.Ships[t.Ship];
        float amount = Bribe(u, me);
        u.Player.Treasury.Credits -= amount;
        if (u.Actors.TryGetValue(raider.Owner, out var pirates))
        {
            float cap = Bandits.FenceCap(u, pirates);
            float into = cap > 0f ? MathF.Min(amount, MathF.Max(0f, cap - pirates.Treasury.Credits)) : amount;
            if (into > 0f) { pirates.Treasury.Credits += into; u.Events.Publish(new TreasuryIncome(pirates.Id, "bribe", into)); }
        }
        Release(u, me, raider);
        return new HailResult(true, $"{amount:0} Cr Schutzgeld gezahlt — die Kaperer drehen ab.");
    }

    static HailResult Threaten(Universe u, HailTarget t, Ship me)
    {
        var raider = u.Ships[t.Ship];
        float chance = ThreatChance(me, raider);
        bool ok = u.Rng.NextDouble() < chance;
        if (ok)
        {
            Release(u, me, raider);
            return new HailResult(true, "Die Drohung wirkt — die Kaperer drehen ab.");
        }
        var pending = PendingRaid(u, me, raider.Id)!;
        pending.DueTick = u.Tick;   // greifen im nächsten Pirate-Schritt an
        return new HailResult(true, "Sie lachen nur — Angriff!");
    }

    static HailResult Stall(Universe u, HailTarget t, Ship me)
    {
        var pending = PendingRaid(u, me, t.Ship)!;
        pending.Stalled = true;
        long add = u.Rules.PlayerWarningTicks / 2;
        pending.DueTick += add;
        return new HailResult(true, $"Du hältst sie hin — etwa {add / SimClock.TicksPerSecond:0} s mehr Zeit.");
    }

    static HailResult Surrender(Universe u)
    {
        var ev = PlayerSurrender.Surrender(u);
        return ev is null ? new HailResult(false, PlayerSurrender.Issue(u) ?? "Nicht möglich.") : new HailResult(true, ev.Message);
    }
}
