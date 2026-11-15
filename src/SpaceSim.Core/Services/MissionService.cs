using SpaceSim.Core.Events;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Services;

/// <summary>
/// Runde 41: der Dienst „missions“ einer Station — der Dock-Bereich „Aufträge“. Angebote: <c>accept:&lt;id&gt;</c> (annehmen),
/// <c>abandon:&lt;id&gt;</c> (aktiven Auftrag abbrechen), <c>deliver:&lt;id&gt;</c> (Transport-Fracht an dieser Station abliefern).
/// Schlüssel streng; Ausführung nur über <see cref="MissionRules"/>; die Anzeige (Vorschau: Belohnung, Pfand, Frist, Ziel) ist
/// rein. Fehlschlag ändert nichts und veröffentlicht nichts (außer <see cref="ServicePerformed"/> bei Erfolg).
/// </summary>
public sealed class MissionService : IStationService
{
    public const string ServiceKey = "missions";
    public const string Accept = "accept";
    public const string Abandon = "abandon";
    public const string Deliver = "deliver";

    public string Key => ServiceKey;

    /// <summary>Verfügbar, wenn Aufträge aktiv sind (die Station zeigt dann auch „keine Angebote“ ehrlich an).</summary>
    public bool IsAvailable(Universe u, Station station) => u.Rules.MissionsActive;

    /// <summary>Angebote der Station: jedes hängende Angebot als „accept:&lt;id&gt;“ plus aktive Aufträge hier als „abandon:&lt;id&gt;“ / „deliver:&lt;id&gt;“.</summary>
    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var list = new List<ServiceOffer>();
        foreach (var o in u.MissionOffers.Where(o => o.At == station.Id).OrderBy(o => o.Id))
        {
            if (MissionRules.Types.Get(o.Type) is not { } type) continue;
            string? issue = MissionRules.AcceptIssue(u, o.Id, ship, remote: false);
            list.Add(new ServiceOffer($"{Accept}:{o.Id}", $"Annehmen: {type.DescribeOffer(u, o)}", o.Reward, issue is null ? 1f : 0f, default, 0f, 0f,
                $"Pfand {o.Deposit:0} Cr · Belohnung {o.Reward:0} Cr" + (issue is not null ? $" · {issue}" : "")));
        }
        foreach (var m in u.Missions.Where(m => m.State == MissionState.Active && m.At == station.Id).OrderBy(m => m.Id))
        {
            if (MissionRules.Types.Get(m.Type) is not { } type) continue;
            string label = type.Describe(u, m);
            if (m.Type == TransportMission.KeyId && u.Stations.TryGetValue(new StationId(int.Parse(m.Args["target"])), out var tgt) && tgt.Id == station.Id)
            {
                string? issue = type.DeliverIssue(u, m, ship, station);
                list.Add(new ServiceOffer($"{Deliver}:{m.Id}", $"Abliefern: {label}", m.Reserved, 1f, default, 0f, 0f, issue ?? "Fracht abliefern"));
            }
            else
            {
                list.Add(new ServiceOffer($"{Abandon}:{m.Id}", $"Abbrechen: {label}", 0f, 1f, default, 0f, 0f,
                    $"Pfand {m.Deposit:0} Cr verfällt · Beziehung −{u.Rules.MissionRelationLoss:0.00}"));
            }
        }
        return list;
    }

    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        if (!TryParse(request.Offer, out var action, out var id))
            return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station)) return new ServiceResult(false, 0f, 0f, "Aufträge sind abgeschaltet.");
        switch (action)
        {
            case Accept:
            {
                var (m, issue) = MissionRules.Accept(u, id, ship, remote: false);
                if (m is null) return new ServiceResult(false, 0f, 0f, issue ?? "Annahme nicht möglich.");
                var type = MissionRules.Types.Get(m.Type)!;
                u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Accept, default, 1f, m.Deposit > 0f ? -m.Deposit : 0f));
                return new ServiceResult(true, 1f, m.Deposit > 0f ? -m.Deposit : 0f, $"Auftrag angenommen: {type.Describe(u, m)}");
            }
            case Abandon:
            {
                var (ok, issue) = MissionRules.Abandon(u, id, ship);
                if (!ok) return new ServiceResult(false, 0f, 0f, issue ?? "Abbruch nicht möglich.");
                u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Abandon, default, 1f, 0f));
                return new ServiceResult(true, 1f, 0f, "Auftrag abgebrochen. Pfand verfällt, die Kasse des Auftraggebers gibt die Reservierung zurück.");
            }
            case Deliver:
            {
                var m = MissionRules.Active(u, id);
                if (m is null) return new ServiceResult(false, 0f, 0f, "Kein laufender Auftrag mit dieser Id.");
                var type = MissionRules.Types.Get(m.Type);
                if (type is null || m.Type != TransportMission.KeyId) return new ServiceResult(false, 0f, 0f, "Nur Transportaufträge werden abgeliefert.");
                if (type.DeliverIssue(u, m, ship, station) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
                type.Deliver(u, m, ship, station);
                u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, Deliver, default, 1f, m.Reserved + m.Deposit));
                return new ServiceResult(true, 1f, m.Reserved + m.Deposit, $"Fracht abgeliefert: {type.Describe(u, m)} Belohnung {m.Reserved:0} Cr, Pfand {m.Deposit:0} Cr zurück.");
            }
            default:
                return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        }
    }

    /// <summary>Zerlegt streng „accept:&lt;id&gt;“, „abandon:&lt;id&gt;“, „deliver:&lt;id&gt;“ (nur Ziffern, Id &gt; 0).</summary>
    static bool TryParse(string offer, out string action, out int id)
    {
        action = "";
        id = 0;
        int sep = offer.IndexOf(':');
        if (sep <= 0) return false;
        string head = offer[..sep];
        if (head != Accept && head != Abandon && head != Deliver) return false;
        string tail = offer[(sep + 1)..];
        if (tail.Length == 0 || tail.Any(c => c is < '0' or > '9') || !int.TryParse(tail, out id) || id <= 0) return false;
        action = head;
        return true;
    }
}
