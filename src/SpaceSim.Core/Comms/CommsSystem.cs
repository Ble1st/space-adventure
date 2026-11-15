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
    }

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
        }
    }
}

/// <summary>
/// Runde 40: reine Anzeige-Texte einer Posteingang-Nachricht (Betreff, Text, Antwort-Angebote als Comms-Schlüssel). Die Antworten werden über
/// die <see cref="CommsActions"/>-Registry vorgeschaut und ausgeführt; „Ignorieren“ kostet nichts und ist keine Registry-Aktion.
/// </summary>
public static class CommsText
{
    /// <summary>Betreff und Text der Nachricht (aus <see cref="CommsMessage.Kind"/> und <see cref="CommsMessage.Args"/>).</summary>
    public static (string Subject, string Body) Describe(Universe u, CommsMessage m)
    {
        string F() => m.Args.TryGetValue("faction", out var fs) && int.TryParse(fs, out var fv) && u.Factions.TryGetValue(new FactionId(fv), out var fac) ? fac.Name : "?";
        string Msg() => m.Args.TryGetValue("message", out var s) ? s : "";
        return m.Kind switch
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
            _ => (m.Kind, Msg()),
        };
    }

    /// <summary>
    /// Antwort-Angebote der Nachricht als Comms-Schlüssel (werden über die Registry vorgeschaut/ausgeführt). Angebote der KI (Waffenstillstand/
    /// Frieden) können angenommen werden; „Ignorieren“ ist implizit und kostet nichts.
    /// </summary>
    public static IReadOnlyList<string> Answers(Universe u, CommsMessage m)
    {
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
