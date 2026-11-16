using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Comms;

/// <summary>
/// Runde 40: Posteingang des Spielers (<see cref="Universe.Inbox"/>). Additiv gespeichert, <b>nicht gehasht</b>, verbraucht keine Zufallszahl —
/// reine Anzeige. Höchstes <see cref="EconomyRules.MaxInbox"/> Einträge: älteste gelesene fallen zuerst weg, dann älteste; abgelaufene sofort.
/// </summary>
public static class Comms
{
    /// <summary>Ungelesene Nachrichten (Zähler am Menü/Kommando).</summary>
    public static int Unread(Universe u) => u.Inbox.Count(m => !m.Read);

    /// <summary>
    /// Neue Nachricht einstellen (Reihenfolge = Id; ohne Zufall). <paramref name="ttl"/> &gt; 0 = Verfall nach dieser vielen Ticks ab jetzt.
    /// Hält die Obergrenze ein und entfernt abgelaufene.
    /// </summary>
    public static CommsMessage Add(Universe u, ActorId from, string kind, Dictionary<string, string>? args = null, long ttl = 0)
    {
        var m = new CommsMessage { Id = u.NextMessageId++, From = from, Kind = kind, Args = args ?? new(), Created = u.Tick, Expires = ttl > 0 ? u.Tick + ttl : 0 };
        u.Inbox.Add(m);
        Prune(u);
        return m;
    }

    /// <summary>Obergrenze und abgelaufene Nachrichten entfernen (älteste gelesene zuerst, dann älteste insgesamt).</summary>
    public static void Prune(Universe u)
    {
        u.Inbox.RemoveAll(m => m.Expires > 0 && u.Tick >= m.Expires);
        int max = Math.Max(1, u.Rules.MaxInbox);
        while (u.Inbox.Count > max)
        {
            int idx = u.Inbox.FindIndex(m => m.Read);
            if (idx < 0) idx = 0;
            u.Inbox.RemoveAt(idx);
        }
    }

    /// <summary>Als gelesen markieren (Zähler). Unbekannte Id: nichts.</summary>
    public static void MarkRead(Universe u, int id)
    {
        var m = u.Inbox.FirstOrDefault(x => x.Id == id);
        if (m is not null) m.Read = true;
    }

    /// <summary>Runde 42b: Alter eines Ticks als „vor X min“ (minutengenau; unter einer Minute „&lt;1 min“) — statt roher Tick-Zahlen.</summary>
    public static string Ago(Universe u, long tick) => MinText(Math.Max(0L, u.Tick - tick));

    /// <summary>Runde 42b: Restzeit bis zu einem Tick als „in X min“.</summary>
    public static string In(Universe u, long tick) => MinText(Math.Max(0L, tick - u.Tick));

    static string MinText(long ticks)
    {
        long m = ticks / (SimClock.TicksPerSecond * 60);
        return m > 0 ? $"{m} min" : "<1 min";
    }
}

/// <summary>
/// Runde 40: schreibt Nachrichten aus Simulationsereignissen in den Posteingang (<see cref="Universe.Inbox"/>) — nur Nachrichten an den Spieler
/// (Krieg/Frieden/Waffenstillstand mit seiner Fraktion, Fahndungsstufen, Kopfgeld, Angebote der KI). Verbraucht kein <see cref="Universe.Rng"/> und
/// ändert sonst nichts. Läuft im Politics-Modul (Intervall 20); hängt sich an den Ereignisbus, sobald er ein anderes Universum sieht
/// (neues Spiel/Laden: die Systeme der Sitzung bleiben, das Universum wechselt — wie <see cref="Diagnostics.RouteRecorder"/> in der Ansicht).
/// </summary>
public sealed class CommsSystem : ISimSystem
{
    public const int Interval = 20;

    Universe? _sub;
    Action<SimEvent>? _handler;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.CommsActive) return;
        if (!ReferenceEquals(_sub, u))
        {
            if (_sub is not null && _handler is not null) _sub.Events.Raised -= _handler;
            _handler = e => OnEvent(u, e);
            u.Events.Raised += _handler;
            _sub = u;
        }
        Comms.Prune(u);
        SyncContractOffers(u);
    }

    /// <summary>
    /// Runde 41: Liefervertrags-Angebote (Comms-Kanal) der Station, an der der Spieler gerade angedockt ist, als Posteingang-Nachricht —
    /// gezogen beim Andocken und bei jedem neuen Angebot. Ohne Andocken erzeugt die NPC-Welt keine Post für den Spieler (Inbox bleibt leer,
    /// wenn der Spieler nicht handelt). Verbraucht keine Zufallszahl.
    /// </summary>
    static void SyncContractOffers(Universe u)
    {
        if (u.PlayerFaction is null || !u.Ships.TryGetValue(u.PlayerShip, out var ps) || ps.DockedAt == default) return;
        var dock = ps.DockedAt;
        foreach (var o in u.MissionOffers)
        {
            if (o.At != dock || !o.Args.TryGetValue("channel", out var ch) || ch != "comms") continue;
            string id = o.Id.ToString();
            string kind = IsContract(o) ? "contract_offer" : "mission_offer";   // Runde 42: auch Kopfgeld/Eskorte/Patrouille
            if (u.Inbox.Any(m => m.Kind == kind && m.Args.TryGetValue("offer", out var os) && os == id)) continue;
            Comms.Add(u, o.Client, kind, ContractArgs(u, o), u.Rules.OfferMessageTicks);
        }
    }

    static bool IsContract(MissionOffer o) => o.Args.TryGetValue("contract", out var c) && c == "1";

    static Dictionary<string, string> ContractArgs(Universe u, MissionOffer o) => new()
    {
        ["offer"] = o.Id.ToString(),
        ["faction"] = (u.FactionOf(o.Client) is { } f ? f.Value : 0).ToString(),
        ["message"] = Missions.MissionRules.DescribeOfferText(u, o),
    };

    /// <summary>Ereignis → Nachricht(en). Rein bis auf den Inbox-Eintrag; ohne Zufallszahl. Auch direkt aufrufbar (Tests).</summary>
    public static void OnEvent(Universe u, SimEvent e)
    {
        if (!u.Rules.CommsActive || u.PlayerFaction is not { } me) return;
        switch (e)
        {
            case WarDeclared wd when wd.A == me || wd.B == me:
            {
                var other = wd.A == me ? wd.B : wd.A;
                Comms.Add(u, other, "war_declared", new() { ["faction"] = other.Value.ToString(), ["message"] = wd.Message }, u.Rules.InboxMessageTicks);
                break;
            }
            case PeaceConcluded pc when pc.A == me || pc.B == me:
            {
                var other = pc.A == me ? pc.B : pc.A;
                Comms.Add(u, other, "peace_concluded", new() { ["faction"] = other.Value.ToString(), ["reason"] = pc.Reason, ["message"] = pc.Message }, u.Rules.InboxMessageTicks);
                break;
            }
            case CeasefireConcluded cc when cc.A == me || cc.B == me:
            {
                var other = cc.A == me ? cc.B : cc.A;
                Comms.Add(u, other, "ceasefire_concluded", new() { ["faction"] = other.Value.ToString(), ["until"] = cc.Until.ToString(), ["tribute"] = cc.Tribute.ToString(System.Globalization.CultureInfo.InvariantCulture), ["message"] = cc.Message }, u.Rules.InboxMessageTicks);
                break;
            }
            case CeasefireOffered co when co.To == me:
                Comms.Add(u, co.From, "ceasefire_offer", new() { ["faction"] = co.From.Value.ToString(), ["message"] = co.Message }, u.Rules.OfferMessageTicks);
                break;
            case PeaceOffered po when po.To == me:
                Comms.Add(u, po.From, "peace_offer", new() { ["faction"] = po.From.Value.ToString(), ["message"] = po.Message }, u.Rules.OfferMessageTicks);
                break;
            case WantedChanged wc when wc.Actor == u.PlayerActor && wc.To > wc.From:
            {
                var w = u.Actors[u.PlayerActor].Wanted.GetValueOrDefault(wc.Faction);
                Comms.Add(u, wc.Faction, "wanted_up", new()
                {
                    ["faction"] = wc.Faction.Value.ToString(),
                    ["level"] = wc.To.ToString(),
                    ["fine"] = w is null ? "0" : Wanted.FineOf(u, w).ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                }, u.Rules.InboxMessageTicks);
                break;
            }
            case BountyEarned be when be.Hunter == u.PlayerActor:
                Comms.Add(u, be.Faction, "bounty_earned", new()
                {
                    ["faction"] = be.Faction.Value.ToString(),
                    ["amount"] = be.Amount.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                }, u.Rules.InboxMessageTicks);
                break;
            // Runde 41: Aufträge — Liefervertrags-Angebot (Comms-Kanal) und Annahme/Erfüllung/Scheitern; Runde 42: alle Comms-Angebote (auch Kopfgeld/Eskorte/Patrouille)
            case MissionOffered mo:
            {
                var offer = u.MissionOffers.FirstOrDefault(o => o.Id == mo.Offer);
                if (offer is null) break;
                // Runde 42b: MissionOffered feuert für alle Angebotsarten; Posteingang nur für den Comms-Kanal (Dock-Angebote erscheinen im Dock-Bereich)
                if (!offer.Args.TryGetValue("channel", out var ch) || ch != "comms") break;
                // nur wenn der Spieler an der anbietenden Station angedockt ist (sonst wäre die NPC-Welt voller Post für ihn)
                if (!u.Ships.TryGetValue(u.PlayerShip, out var ps) || ps.DockedAt == default || ps.DockedAt != offer.At) break;
                Comms.Add(u, mo.Client, IsContract(offer) ? "contract_offer" : "mission_offer", ContractArgs(u, offer), u.Rules.OfferMessageTicks);
                break;
            }
            case MissionAccepted ma:
                Comms.Add(u, ma.Client, "mission_accepted", MissionArgs(u, ma.Mission, ma.Message), u.Rules.InboxMessageTicks);
                break;
            case MissionCompleted mc:
                Comms.Add(u, mc.Client, "mission_completed", new() { ["mission"] = mc.Mission.ToString(), ["amount"] = mc.Paid.ToString("0", System.Globalization.CultureInfo.InvariantCulture), ["message"] = mc.Message, ["deadline"] = DeadlineArg(u, mc.Mission) }, u.Rules.InboxMessageTicks);
                break;
            case MissionFailed mf:
                Comms.Add(u, mf.Client, "mission_failed", new() { ["mission"] = mf.Mission.ToString(), ["reason"] = mf.Reason, ["message"] = mf.Message, ["deadline"] = DeadlineArg(u, mf.Mission) }, u.Rules.InboxMessageTicks);
                break;
        }
    }

    /// <summary>Runde 42c: Args einer Auftrags-Nachricht inkl. Frist-Tick („deadline“) — die Anzeige rechnet „noch …“ daraus zur Anzeigezeit.</summary>
    static Dictionary<string, string> MissionArgs(Universe u, int missionId, string message) =>
        new() { ["mission"] = missionId.ToString(), ["message"] = message, ["deadline"] = DeadlineArg(u, missionId) };

    /// <summary>Runde 42c: Frist-Tick des Auftrags (beim Veröffentlichen von Erfüllen/Scheitern noch in der Liste); „0“ = unbekannt.</summary>
    static string DeadlineArg(Universe u, int missionId) =>
        u.Missions.FirstOrDefault(x => x.Id == missionId)?.Deadline.ToString() ?? "0";
}

/// <summary>
/// Runde 40: reine Anzeige-Texte einer Posteingang-Nachricht (Betreff, Text, Antwort-Angebote als Comms-Schlüssel). Die Antworten werden über
/// die <see cref="CommsActions"/>-Registry vorgeschaut und ausgeführt; „Ignorieren“ kostet nichts und ist keine Registry-Aktion.
/// Runde 42c: Zeiten werden BEIM ANZEIGEN gerechnet, nicht beim Erzeugen (<see cref="RenderTimes"/>): Restzeiten/„vor …“ statt roher Ticks;
/// Angebots-Nachrichten werden aus dem lebenden Angebot neu aufgebaut, abgelaufene/unbekannte klar als „abgelaufen“ markiert.
/// </summary>
public static class CommsText
{
    static readonly System.Text.RegularExpressions.Regex BisTick = new(@"bis Tick (\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);
    static readonly System.Text.RegularExpressions.Regex SeitTick = new(@"seit Tick (\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);
    static readonly System.Text.RegularExpressions.Regex Noch = new(@"noch \d+:\d\d", System.Text.RegularExpressions.RegexOptions.Compiled);
    /// <summary>Runde 42d: „ · noch m:ss“ ganz streichen (erledigte/angenommene/gescheiterte Meldungen).</summary>
    static readonly System.Text.RegularExpressions.Regex NochLeer = new(@" · noch \d+:\d\d", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Zeiten im Text zur Anzeigezeit rendern — „bis Tick N“ → „bis in X min“ (bzw. „abgelaufen vor X min“),
    /// „seit Tick N“ → „seit X min“. Runde 42d (Handytest): nur offene Angebote tragen eine Restzeit — bei allen anderen
    /// Meldungen (erledigt/angenommen/gescheitert …) wird die weiterlaufende Restzeit („ · noch m:ss“) gestrichen statt
    /// neu gerechnet. Reiner Textumbau, keine Zustandsänderung.
    /// </summary>
    public static string RenderTimes(Universe u, CommsMessage m, string body)
    {
        body = BisTick.Replace(body, mm =>
        {
            long t = long.Parse(mm.Groups[1].Value);
            return t > u.Tick ? $"bis in {Comms.In(u, t)}" : $"abgelaufen vor {Comms.Ago(u, t)}";
        });
        body = SeitTick.Replace(body, mm => $"seit {Comms.Ago(u, long.Parse(mm.Groups[1].Value))}");
        if (m.Kind is "contract_offer" or "mission_offer")
        {
            if (m.Args.TryGetValue("deadline", out var ds) && long.TryParse(ds, out var dl) && dl > 0)
                body = Noch.Replace(body, _ => dl > u.Tick ? $"noch {Missions.MissionRules.Rest(dl - u.Tick)}" : $"abgelaufen vor {Comms.Ago(u, dl)}");
        }
        else
        {
            body = NochLeer.Replace(body, "");
        }
        return body;
    }

    /// <summary>
    /// Runde 42d (Handytest): Meta-Zeile einer Posteingangszeile („vor X min“, bei <b>offenen Angeboten</b> zusätzlich
    /// „ · verfällt in Y min“ — nur die haben eine Restzeit; erledigte/angenommene/gescheiterte Meldungen nicht).
    /// </summary>
    public static string RowMeta(Universe u, CommsMessage m) =>
        $"vor {Comms.Ago(u, m.Created)}" + (LiveOffer(u, m) is not null && m.Expires > 0 ? $" · verfällt in {Comms.In(u, m.Expires)}" : "");

    /// <summary>Lebendes Angebot zur Nachricht (null = unbekannt oder abgelaufen).</summary>
    public static MissionOffer? LiveOffer(Universe u, CommsMessage m) =>
        m.Args.TryGetValue("offer", out var os) && int.TryParse(os, out var ov)
            ? u.MissionOffers.FirstOrDefault(o => o.Id == ov && (o.Expires <= 0 || u.Tick < o.Expires))
            : null;

    /// <summary>Betreff und Text der Nachricht (aus <see cref="CommsMessage.Kind"/> und <see cref="CommsMessage.Args"/>; Zeiten zur Anzeigezeit).</summary>
    public static (string Subject, string Body) Describe(Universe u, CommsMessage m)
    {
        string F() => m.Args.TryGetValue("faction", out var fs) && int.TryParse(fs, out var fv) && u.Factions.TryGetValue(new FactionId(fv), out var fac) ? fac.Name : "?";
        string Msg() => m.Args.TryGetValue("message", out var s) ? s : "";
        var (subj, body) = m.Kind switch
        {
            "war_declared" => ($"Kriegserklärung – {F()}", Msg().Length > 0 ? Msg() : $"{F()} erklärt dir den Krieg."),
            "peace_concluded" => ($"Frieden – {F()}", Msg()),
            "ceasefire_concluded" => ($"Waffenstillstand – {F()}", Msg()),
            "ceasefire_offer" => ($"Angebot: Waffenstillstand – {F()}", Msg()),
            "peace_offer" => ($"Angebot: Frieden – {F()}", Msg()),
            "wanted_up" => ($"Fahndung gestiegen – {F()}",
                m.Args.TryGetValue("level", out var ls) && m.Args.TryGetValue("fine", out var fs)
                    ? $"Stufe {ls} ({SecurityNames.WantedLevel(int.Parse(ls))}) bei {F()} — offene Strafe {fs} Cr (bezahlen per Diplomatie oder am Gericht)."
                    : $"Deine Fahndung bei {F()} ist gestiegen."),
            "bounty_earned" => ($"Kopfgeld verdient – {F()}",
                m.Args.TryGetValue("amount", out var as_) ? $"Kopfgeld {as_} Cr bei {F()} — abholen im Kopfgeldbüro." : $"Kopfgeld bei {F()} verdient."),
            // Runde 41: Aufträge; Runde 42: allgemeine Auftragsangebote (Kopfgeldjagd, Eskorte, Patrouille)
            // Runde 42c: Angebots-Nachrichten aus dem lebenden Angebot neu aufbauen (Restzeiten stimmen), sonst „abgelaufen“
            "contract_offer" => OfferText(u, m, "Liefervertrag", F(), Msg()),
            "mission_offer" => OfferText(u, m, "Auftrag angeboten", F(), Msg()),
            "mission_accepted" => ("Auftrag angenommen", Msg()),
            "mission_completed" => ("Auftrag erfüllt", m.Args.TryGetValue("amount", out var am) ? $"{Msg()} Belohnung {am} Cr." : Msg()),
            "mission_failed" => ($"Auftrag gescheitert{(m.Args.TryGetValue("reason", out var rs) ? $" ({rs})" : "")}", Msg()),
            _ => (m.Kind, Msg()),
        };
        return (subj, RenderTimes(u, m, body));
    }

    /// <summary>
    /// Runde 42c: Text einer Angebots-Nachricht zur Anzeigezeit: existiert das Angebot noch, wird die Beschreibung frisch
    /// gerechnet (Restzeiten ab jetzt); sonst klar als abgelaufen markiert (mit dem ursprünglichen Text als Verweis).
    /// </summary>
    static (string, string) OfferText(Universe u, CommsMessage m, string subject, string from, string msg)
    {
        string head = $"{subject} – {from}";
        if (LiveOffer(u, m) is { } o)
            return (head, Missions.MissionRules.DescribeOfferText(u, o));
        string body = msg.Length > 0 ? msg : "Angebot einer Station.";
        return (head, RenderTimes(u, m, body) + " — dieses Angebot ist abgelaufen.");
    }

    /// <summary>
    /// Antwort-Angebote der Nachricht als Comms-Schlüssel (werden über die Registry vorgeschaut/ausgeführt). Angebote der KI (Waffenstillstand/
    /// Frieden) können angenommen werden; Runde 41: Liefervertrag annehmen/nachverhandeln; „Ignorieren“ ist implizit und kostet nichts.
    /// Runde 42c: abgelaufene/unbekannte Auftragsangebote bekommen keine Antworten mehr (statt grauer „Angebot unbekannt“-Knöpfe).
    /// </summary>
    public static IReadOnlyList<string> Answers(Universe u, CommsMessage m)
    {
        if (m.Args.TryGetValue("offer", out var os_) && int.TryParse(os_, out var ov))
        {
            if (m.Kind == "contract_offer")
                return LiveOffer(u, m) is not null ? new[] { $"mission:accept:{ov}", $"contract:haggle:{ov}" } : Array.Empty<string>();
            if (m.Kind == "mission_offer")   // Runde 42: Kopfgeld/Eskorte/Patrouille annehmen
                return LiveOffer(u, m) is not null ? new[] { $"mission:accept:{ov}" } : Array.Empty<string>();
        }
        if (m.Args.TryGetValue("faction", out var fs) && int.TryParse(fs, out var fv))
        {
            return m.Kind switch
            {
                "ceasefire_offer" => new[] { $"ceasefire:{fv}" },
                "peace_offer" => new[] { $"peace:{fv}" },
                _ => Array.Empty<string>(),
            };
        }
        return Array.Empty<string>();
    }
}
