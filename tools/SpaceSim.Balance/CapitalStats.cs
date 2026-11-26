using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

/// <summary>Welle E (Balance-Zeile): Großkampfschiff-Aufträge (erteilt, fertig, abgebrochen) und Einsatz der fertigen Schiffe in Gefechten.</summary>
sealed class CapitalStats
{
    readonly List<string> _log = new();
    readonly HashSet<int> _capital = new();
    readonly Dictionary<int, int> _encounterCause = new();
    int _placed, _done, _ended, _shots, _stationShots, _lost;
    float _escrow, _refund;
    readonly SortedSet<int> _sieges = new(), _naval = new(), _other = new();

    public void On(Universe u, SimEvent e)
    {
        switch (e)
        {
            case CapitalOrderPlaced p:
                _placed++; _escrow += p.Escrow;
                _log.Add($"{u.Tick} Auftrag {p.Model} für {u.Factions[p.Faction].Name} an {u.Stations[p.Yard].Name} ({p.Reason}, {p.Escrow:0} Cr)");
                break;
            case CapitalShipCompleted c:
                _done++; _capital.Add(c.Ship.Value);
                _log.Add($"{u.Tick} fertig #{c.Ship.Value} {c.Model} für {u.Factions[c.Faction].Name}");
                break;
            case CapitalOrderEnded x:
                _ended++; _refund += x.Refund;
                _log.Add($"{u.Tick} abgebrochen {x.Model} für {u.Factions[x.Faction].Name} ({x.Reason}, Rückerstattung {x.Refund:0} Cr)");
                break;
            case ShotFired sf when _capital.Contains(sf.Shooter.Value):
                _shots++; Note(u, sf.Encounter);
                break;
            case ShotAtStation sa when _capital.Contains(sa.Shooter.Value):
                _stationShots++; Note(u, sa.Encounter);
                break;
            case ShipDeparted d when _capital.Contains(d.Ship.Value):
                _lost++; _log.Add($"{u.Tick} #{d.Ship.Value} abgegangen ({d.Reason}/{d.Detail})");
                break;
        }
    }

    void Note(Universe u, int encounter)
    {
        var enc = u.Fights.Encounters.FirstOrDefault(x => x.Id == encounter);
        if (enc is null) return;
        (enc.Cause switch { EncounterCause.Siege => _sieges, EncounterCause.Naval => _naval, _ => _other }).Add(encounter);
    }

    public string Line(Universe u) =>
        $"Großkampfschiffe (Welle E, {(u.Rules.CapitalOrders ? "an" : "aus")}): Aufträge {_placed} (Treuhand {_escrow:0} Cr), fertig {_done}, abgebrochen {_ended} (Rückerstattung {_refund:0} Cr), "
        + $"laufend {u.CapitalOrders?.Orders.Count ?? 0}, im Dienst {u.Ships.Values.Count(s => CapitalOrders.IsCapital(u, s))}, abgegangen {_lost}; "
        + $"Schüsse {_shots} auf Schiffe, {_stationShots} auf Stationen; Gefechte: Belagerung {_sieges.Count}, Seeschlacht {_naval.Count}, andere {_other.Count}"
        + (_log.Count > 0 ? " — " + string.Join("; ", _log.Take(30)) : "");
}
