using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

/// <summary>Wurmlöcher (Balance-Zeile): Reisen von NPC-Schiffen durch stabile Wurmlöcher (Stichprobe der laufenden Reisen) und instabile Wurmlöcher.</summary>
sealed class WormholeStats
{
    readonly HashSet<(int Ship, long Start)> _seen = new();
    int _transits, _through, _opened, _collapsed, _traversed;
    readonly SortedDictionary<string, int> _byPair = new(StringComparer.Ordinal);

    public void On(SimEvent e)
    {
        switch (e)
        {
            case WormholeOpened: _opened++; break;
            case WormholeCollapsed: _collapsed++; break;
            case WormholeTraversed: _traversed++; break;
        }
    }

    /// <summary>Neue Reisen seit der letzten Stichprobe: führt ein Sektorwechsel über ein Wurmloch?</summary>
    public void Sample(Universe u)
    {
        if (!u.Rules.Wormholes) return;
        foreach (var s in u.Ships.Values)
        {
            if (s.Transit is not { } t || s.IsPlayer || !_seen.Add((s.Id.Value, t.StartTick))) continue;
            _transits++;
            bool via = false;
            for (int i = 0; i + 1 < t.Legs.Count; i++)
            {
                var a = t.Legs[i]; var b = t.Legs[i + 1];
                if (a.Sector == b.Sector) continue;
                foreach (var gid in u.Sectors[a.Sector].Gates)
                {
                    var g = u.Gates[gid];
                    if (!g.Wormhole || u.Gates[g.Target].Sector != b.Sector || Vec2.Distance(g.Position, a.To) > 1f) continue;
                    via = true;
                    string key = SpaceSim.Core.Simulation.Wormholes.NameOf(u, g.Id.Value < g.Target.Value ? g : u.Gates[g.Target]);
                    _byPair[key] = _byPair.GetValueOrDefault(key) + 1;
                }
            }
            if (via) _through++;
        }
    }

    public string Line(Universe u) =>
        $"Wurmlöcher ({(u.Rules.Wormholes ? "an" : "aus")}): NPC-Reisen {_transits}, davon durch ein Wurmloch {_through} ({(_transits == 0 ? 0 : 100.0 * _through / _transits):0.0} %)"
        + (_byPair.Count == 0 ? "" : " [" + string.Join(", ", _byPair.Select(kv => $"{kv.Key} {kv.Value}")) + "]")
        + $"; instabile geöffnet {_opened}, zusammengefallen {_collapsed}, durchflogen {_traversed}, offen {u.UnstableWormholes.Count}";
}
