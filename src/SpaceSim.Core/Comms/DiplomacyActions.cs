using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Comms;

/// <summary>
/// Runde 45: Comms-Aktion „demand“ — der Spieler fordert von einer Kriegsgegnerin Tribut. Dieselbe Regel wie der KI-Tributfrieden
/// (<see cref="Wars.DemandIssue"/>: Mindestdauer, Müdigkeit, klare Überlegenheit, Zahlungsfähigkeit aus dem Tributbudget der KI); die KI zahlt in die
/// Spielerkasse, es ist Frieden, alle anderen KI-Fraktionen verlieren Achtung. Schlüssel streng: <c>demand:&lt;FactionId&gt;</c>.
/// </summary>
public sealed class DemandAction : ICommsAction
{
    public const string KeyId = "demand";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    static string PreviewText(Universe u, FactionId f, War w) =>
        $"{CommsHelp.FactionName(u, f)} zahlt {Wars.DemandAmount(u, w, f):0} Cr in deine Kasse — dann Frieden; alle anderen KI-Fraktionen −{u.Rules.PlayerDemandRelationLoss:0.00}.";

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (u.PlayerFaction is not { } me) return list;
        foreach (var w in u.Wars)
            if (w.Involves(me) && u.IsNpcFaction(w.Other(me))) list.Add(Preview(u, $"{KeyId}:{w.Other(me).Value}"));
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!CommsActions.TryFaction(offer, KeyId, out var f)) return new(offer, "Tribut fordern", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        if (Wars.PlayerWar(u, f) is not { } w) return new(offer, $"Tribut fordern – {CommsHelp.FactionName(u, f)}", "Kein Krieg mit dieser Fraktion.", 0f, false, "Kein Krieg mit dieser Fraktion.");
        string? issue = Wars.DemandIssue(u, f);
        return new(offer, $"Tribut fordern – {CommsHelp.FactionName(u, f)}", issue ?? PreviewText(u, f, w), issue is null ? Wars.DemandAmount(u, w, f) : 0f, issue is null, issue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!CommsActions.TryFaction(offer, KeyId, out var f)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (Wars.DemandIssue(u, f) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
        float paid = Wars.Demand(u, f) ?? 0f;
        string msg = $"{CommsHelp.FactionName(u, f)} zahlt {paid:0} Cr Tribut — Frieden.";
        u.Events.Publish(new CommsActionPerformed(KeyId, f.Value, paid, msg));
        return new ServiceResult(true, 1f, paid, msg);
    }
}

/// <summary>
/// Runde 45: Comms-Aktion „alliance“ — Bündnisse des Spielers. Schlüssel streng: <c>alliance:propose:&lt;F&gt;</c> (die KI nimmt deterministisch an, wenn
/// <see cref="Alliances.FormIssue"/> nichts einwendet), <c>alliance:leave:&lt;F&gt;</c> (sofort, Beziehung −<see cref="EconomyRules.AllianceLeaveLoss"/>, Abkühlzeit),
/// <c>alliance:aid:&lt;F&gt;</c> (Beistand: Krieg gegen den Angreifer eines Verbündeten, ohne Kriegskasse; nur solange der Ruf offen ist).
/// </summary>
public sealed class AllianceAction : ICommsAction
{
    public const string KeyId = "alliance";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    static bool Parse(string offer, out string verb, out FactionId f)
    {
        verb = ""; f = default;
        foreach (var v in new[] { "propose", "leave", "aid" })
            if (CommsActions.TryFaction(offer, $"{KeyId}:{v}", out f)) { verb = v; return true; }
        return false;
    }

    static string Name(Universe u, FactionId f) => CommsHelp.FactionName(u, f);

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (u.PlayerFaction is not { } me || !u.Rules.AlliancesActive) return list;
        foreach (var f in u.NpcFactionIds())
            if (Wars.CanWage(u, f)) list.Add(Preview(u, Alliances.Allied(u, me, f) ? $"{KeyId}:leave:{f.Value}" : $"{KeyId}:propose:{f.Value}"));
        foreach (var c in Alliances.Calls(u).ToList()) list.Add(Preview(u, $"{KeyId}:aid:{c.Attacker.Value}"));
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!Parse(offer, out var verb, out var f)) return new(offer, "Bündnis", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        var r = u.Rules;
        switch (verb)
        {
            case "propose":
            {
                string? issue = Alliances.ProposeIssue(u, f);
                return new(offer, $"Bündnis vorschlagen – {Name(u, f)}",
                    issue ?? $"{Name(u, f)} nimmt an — Beistandspakt: wer einen von euch angreift, bekommt es mit beiden zu tun; an Kontrollpunkten wie Verbündete (kein Zoll, kein Embargo). Endet unter Beziehung {r.AllianceBreakRelation:0.00} oder bei Verrat.",
                    0f, issue is null, issue);
            }
            case "leave":
            {
                string? issue = Alliances.LeaveIssue(u, f);
                return new(offer, $"Bündnis verlassen – {Name(u, f)}",
                    issue ?? $"Bündnis mit {Name(u, f)} endet sofort — Beziehung −{r.AllianceLeaveLoss:0.00}, {r.AllianceCooldownTicks / 240} min kein neues Bündnis.",
                    0f, issue is null, issue);
            }
            default:
            {
                string? issue = Alliances.AidIssue(u, f);
                var call = Alliances.Calls(u).FirstOrDefault(c => c.Attacker == f);
                string ally = call.War is null ? "dem Verbündeten" : Name(u, call.Ally);
                long left = call.War is null ? 0 : Math.Max(0L, r.AllianceCallTicks - (u.Tick - call.War.Since));
                return new(offer, $"Beistand leisten – Krieg gegen {Name(u, f)}",
                    issue ?? $"Du erklärst {Name(u, f)} den Krieg, um {ally} beizustehen (ohne Kriegskasse). Ohne Beistand endet das Bündnis in {Missions.MissionRules.Rest(left)}.",
                    0f, issue is null, issue);
            }
        }
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!Parse(offer, out var verb, out var f)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        string msg;
        switch (verb)
        {
            case "propose":
                if (Alliances.ProposeIssue(u, f) is { } i1) return new ServiceResult(false, 0f, 0f, i1);
                Alliances.Form(u, u.PlayerFaction!.Value, f);
                msg = $"Bündnis mit {Name(u, f)} geschlossen.";
                break;
            case "leave":
                if (Alliances.LeaveIssue(u, f) is { } i2) return new ServiceResult(false, 0f, 0f, i2);
                Alliances.Leave(u, f);
                msg = $"Bündnis mit {Name(u, f)} verlassen.";
                break;
            default:
                if (Alliances.AidIssue(u, f) is { } i3) return new ServiceResult(false, 0f, 0f, i3);
                Alliances.Aid(u, f);
                msg = $"Beistand: Krieg gegen {Name(u, f)}.";
                break;
        }
        u.Events.Publish(new CommsActionPerformed(KeyId, f.Value, 0f, msg));
        return new ServiceResult(true, 1f, 0f, msg);
    }
}

/// <summary>
/// Runde 45: Comms-Aktion „seat“ — Sektorbehörde der Spielerfraktion gründen oder schließen (<see cref="PlayerSeats"/>). Schlüssel streng:
/// <c>seat:found:&lt;SectorId&gt;</c>, <c>seat:close:&lt;StationId&gt;</c>.
/// </summary>
public sealed class SeatAction : ICommsAction
{
    public const string KeyId = "seat";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (u.PlayerFaction is null || !u.Rules.PlayerSeats) return list;
        if (u.Ships.TryGetValue(u.PlayerShip, out var ship)) list.Add(Preview(u, $"{KeyId}:found:{ship.Sector.Value}"));
        foreach (var st in PlayerSeats.Seats(u)) list.Add(Preview(u, $"{KeyId}:close:{st.Id.Value}"));
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (CommsActions.TryId(offer, $"{KeyId}:found", out var sid))
        {
            var sector = new SectorId(sid);
            string name = u.Sectors.TryGetValue(sector, out var sec) ? sec.Name : "?";
            string? issue = PlayerSeats.FoundIssue(u, sector);
            var t = PlayerSeats.SeatType(u);
            float cost = t is null ? 0f : PlayerSeats.Cost(u, t);
            float upkeep = t is null ? 0f : t.UpkeepPerTick * SimClock.TicksPerSecond * 60f;
            return new(offer, $"Sektorbehörde gründen – {name}",
                issue ?? $"{cost:0} Cr einmalig, Unterhalt {upkeep:0} Cr/min aus deiner Kasse — der Sektor wird deiner (Dominanz): Zoll und Sperrstufe 1–2.",
                -cost, issue is null, issue);
        }
        if (CommsActions.TryId(offer, $"{KeyId}:close", out var stid))
        {
            string? issue = PlayerSeats.CloseIssue(u, new StationId(stid));
            string name = u.Stations.TryGetValue(new StationId(stid), out var st) ? st.Name : "?";
            return new(offer, $"Behörde schließen – {name}", issue ?? "Kein Erlös; der Unterhalt endet, Sperren fallen mit der Kontrolle.", 0f, issue is null, issue);
        }
        return new(offer, "Sektorbehörde", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (CommsActions.TryId(offer, $"{KeyId}:found", out var sid))
        {
            var sector = new SectorId(sid);
            if (PlayerSeats.FoundIssue(u, sector) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
            float cost = PlayerSeats.Cost(u, PlayerSeats.SeatType(u)!);
            var st = PlayerSeats.Found(u, sector);
            string msg = $"{st.Name} gegründet ({cost:0} Cr) — {u.Sectors[sector].Name} gehört jetzt deiner Fraktion.";
            u.Events.Publish(new CommsActionPerformed(KeyId, sid, -cost, msg));
            return new ServiceResult(true, 1f, -cost, msg);
        }
        if (CommsActions.TryId(offer, $"{KeyId}:close", out var stid))
        {
            if (PlayerSeats.CloseIssue(u, new StationId(stid)) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
            string name = u.Stations[new StationId(stid)].Name;
            PlayerSeats.Close(u, new StationId(stid));
            string msg = $"{name} geschlossen.";
            u.Events.Publish(new CommsActionPerformed(KeyId, stid, 0f, msg));
            return new ServiceResult(true, 1f, 0f, msg);
        }
        return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
    }
}

/// <summary>
/// Runde 45: Comms-Aktion „lock“ — Sperrstufe in einem eigenen Sektor setzen (<see cref="PlayerSeats.LockIssue"/>, dieselben Regeln wie die KI).
/// Schlüssel streng: <c>lock:&lt;SectorId&gt;:&lt;Stufe 0–5&gt;</c>.
/// </summary>
public sealed class LockAction : ICommsAction
{
    public const string KeyId = "lock";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    static bool Digits(string s) => s.Length > 0 && s.All(c => c is >= '0' and <= '9');

    static bool Parse(string offer, out SectorId sector, out int level)
    {
        sector = default; level = -1;
        if (!offer.StartsWith(KeyId + ":", StringComparison.Ordinal)) return false;
        var parts = offer[(KeyId.Length + 1)..].Split(':');
        if (parts.Length != 2 || !Digits(parts[0]) || parts[1].Length != 1 || !Digits(parts[1])) return false;
        if (!int.TryParse(parts[0], out var sv) || sv <= 0) return false;
        level = parts[1][0] - '0';
        if (level > 5) return false;
        sector = new SectorId(sv);
        return true;
    }

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (u.PlayerFaction is null || !u.Rules.PlayerSeats) return list;
        foreach (var sec in PlayerSeats.OwnSectors(u))
            for (int lv = 0; lv <= 5; lv++) list.Add(Preview(u, $"{KeyId}:{sec.Id.Value}:{lv}"));
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!Parse(offer, out var sector, out var level)) return new(offer, "Sperrstufe", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        string name = u.Sectors.TryGetValue(sector, out var sec) ? sec.Name : "?";
        string? issue = PlayerSeats.LockIssue(u, sector, level);
        string text = level == 0
            ? $"Sperre in {name} aufheben — danach {u.Rules.LockCooldownTicks / 240} min Abkühlzeit."
            : $"{name}: {AccessNames.Level(level)} — {PlayerSeats.UpkeepPerMinute(u, level):0} Cr/min aus deiner Kasse, höchstens {u.Rules.LockMaxTicks / 240} min; Zoll {level * u.Rules.GateTollPerLevel:0} Cr je Einreise in deine Kasse. KI-Fraktionen grollen wie bei jeder Sperre.";
        return new(offer, $"Sperrstufe {level} – {name}", issue ?? text, 0f, issue is null, issue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!Parse(offer, out var sector, out var level)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (PlayerSeats.LockIssue(u, sector, level) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
        int set = PlayerSeats.SetLock(u, sector, level);
        string msg = set == 0 ? $"Sperre in {u.Sectors[sector].Name} aufgehoben." : $"{u.Sectors[sector].Name}: Sperrstufe {set} ({AccessNames.Level(set)}).";
        u.Events.Publish(new CommsActionPerformed(KeyId, sector.Value, 0f, msg));
        return new ServiceResult(true, 1f, 0f, msg);
    }
}

/// <summary>
/// Eigenes Hauptquartier (docs/POLITIK.md „Eigenes Hauptquartier“): Comms-Aktion „hq“ — gründen oder aufgeben (<see cref="PlayerHq"/>). Schlüssel streng:
/// <c>hq:found:&lt;SectorId&gt;</c>, <c>hq:close:&lt;StationId&gt;</c>.
/// </summary>
public sealed class HqAction : ICommsAction
{
    public const string KeyId = "hq";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (u.PlayerFaction is null || !u.Rules.PlayerHq) return list;
        var all = PlayerHq.All(u);
        if (all.Count == 0 && u.Ships.TryGetValue(u.PlayerShip, out var ship)) list.Add(Preview(u, $"{KeyId}:found:{ship.Sector.Value}"));
        foreach (var st in all) list.Add(Preview(u, $"{KeyId}:close:{st.Id.Value}"));
        return list;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (CommsActions.TryId(offer, $"{KeyId}:found", out var sid))
        {
            var sector = new SectorId(sid);
            string name = u.Sectors.TryGetValue(sector, out var sec) ? sec.Name : "?";
            string? issue = PlayerHq.FoundIssue(u, sector);
            var t = PlayerHq.HqType(u);
            float cost = t is null ? 0f : PlayerHq.Cost(u, t);
            float upkeep = t is null ? 0f : PlayerHq.UpkeepPerMinute(t);
            return new(offer, $"Hauptquartier gründen – {name}",
                issue ?? $"{cost:0} Cr einmalig, Unterhalt {upkeep:0} Cr/min aus deiner Kasse (notfalls Schulden) — Einfluss 6, Gericht, Reparatur, Geschütze; der Sektor erreicht Kontrolle: Sperrstufe bis 5.",
                -cost, issue is null, issue);
        }
        if (CommsActions.TryId(offer, $"{KeyId}:close", out var stid))
        {
            string? issue = PlayerHq.CloseIssue(u, new StationId(stid));
            string name = u.Stations.TryGetValue(new StationId(stid), out var st) ? st.Name : "?";
            return new(offer, $"Hauptquartier aufgeben – {name}", issue ?? "Kein Erlös; der Unterhalt endet, Einfluss und Sperrstufe fallen mit der Kontrolle.", 0f, issue is null, issue);
        }
        return new(offer, "Hauptquartier", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (CommsActions.TryId(offer, $"{KeyId}:found", out var sid))
        {
            var sector = new SectorId(sid);
            if (PlayerHq.FoundIssue(u, sector) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
            float cost = PlayerHq.Cost(u, PlayerHq.HqType(u)!);
            var st = PlayerHq.Found(u, sector);
            string msg = $"{st.Name} gegründet ({cost:0} Cr) — Sitz deiner Fraktion in {u.Sectors[sector].Name}.";
            u.Events.Publish(new CommsActionPerformed(KeyId, sid, -cost, msg));
            return new ServiceResult(true, 1f, -cost, msg);
        }
        if (CommsActions.TryId(offer, $"{KeyId}:close", out var stid))
        {
            if (PlayerHq.CloseIssue(u, new StationId(stid)) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
            string name = u.Stations[new StationId(stid)].Name;
            PlayerHq.Close(u, new StationId(stid));
            string msg = $"{name} aufgegeben.";
            u.Events.Publish(new CommsActionPerformed(KeyId, stid, 0f, msg));
            return new ServiceResult(true, 1f, 0f, msg);
        }
        return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
    }
}
