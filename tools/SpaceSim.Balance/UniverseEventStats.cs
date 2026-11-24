using SpaceSim.Core.Events;
using SpaceSim.Core.Model;


/// <summary>Universumsereignisse in der Balance: begonnene je Art, Enden je Grund, Nutzung (geborgene Einheiten/Module durch NPC und Spieler, abgebaute Felsen).</summary>
sealed class UniverseEventStats
{
    readonly SortedDictionary<string, int> _started = new(StringComparer.Ordinal), _ended = new(StringComparer.Ordinal);
    readonly HashSet<int> _wrecks = new(), _rocks = new();
    float _npcUnits, _playerUnits; int _npcModules, _rocksDepleted, _rocksMade, _wrecksMade;

    public void On(Universe u, SimEvent e)
    {
        switch (e)
        {
            case UniverseEventStarted s:
                _started[s.Kind] = _started.GetValueOrDefault(s.Kind) + 1;
                if (u.UniverseEvents.Find(x => x.Id == s.Event) is { } ev)
                {
                    foreach (var w in ev.Wrecks) { _wrecks.Add(w); _wrecksMade++; }
                    foreach (var a in ev.Asteroids) { _rocks.Add(a); _rocksMade++; }
                }
                break;
            case UniverseEventEnded x: _ended[x.Reason] = _ended.GetValueOrDefault(x.Reason) + 1; break;
            case WreckSalvaged ws when _wrecks.Contains(ws.Wreck):
                if (ws.ByNpc) { _npcUnits += ws.Units; if (ws.Module is not null) _npcModules++; } else _playerUnits += ws.Units;
                break;
            case AsteroidDepleted ad when _rocks.Remove(ad.Asteroid): _rocksDepleted++; break;
        }
    }

    public string Line(Universe u) =>
        $"Universumsereignisse ({(u.Rules.UniverseEvents ? "an" : "aus")}): begonnen {Join(_started)}; beendet {Join(_ended)}; laufend {u.UniverseEvents.Count}; "
        + $"Felsen erzeugt {_rocksMade}, davon abgebaut {_rocksDepleted}; Wracks/Behälter erzeugt {_wrecksMade}, geborgen NPC {_npcUnits:0.#} E. ({_npcModules} Module), Spieler {_playerUnits:0.#} E.";

    static string Join(SortedDictionary<string, int> d) => d.Count == 0 ? "–" : string.Join(", ", d.Select(kv => $"{kv.Key} {kv.Value}"));
}
