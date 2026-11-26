using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.PlayerBot;

/// <summary>
/// Test 7 (--austerity N): Endspiel-Kasse. Ab N eigenen Sektoren lässt der Bot Behörden ruhen, die der Sektor nicht braucht (Comms rest:on, Rang >= 2 laut
/// PlayerUpkeepOrder.Rank), und schaltet sie wieder an, wenn dadurch ein Sektor verloren ginge. Nur Spielerwege (Comms-Aktion), kein Debug.
/// </summary>
public sealed partial class Empire
{
    public static bool Aus, NoDonate; public static int AusMin = 8;
    long _ausNext; int _ausSectors; public int AusRests, AusWakes; readonly List<(StationId id, SectorId sec, long tick)> _ausRested = new();
    public readonly List<string> AusLog = new();
    void AusStep()
    {
        if (!Aus || U.Tick < _ausNext || U.PlayerFaction is not { } me) return;
        _ausNext = U.Tick + 1200;
        int own = U.Sectors.Values.Count(s => s.Owner == me);
        // Rückgängig: ein Sektor ging seit dem Ruhenlassen verloren
        if (own < _ausSectors)
        {
            foreach (var (id, sec, t) in _ausRested.ToList())
                if (U.Stations.TryGetValue(id, out var st) && st.Dormant && U.Sectors[sec].Owner != me && U.Tick - t < 20000)
                {
                    var r = C.Execute(U, $"rest:off:{id.Value}"); AusWakes++; _ausRested.RemoveAll(x => x.id == id);
                    AusLog.Add($"[{U.Tick}] wieder an {st.Name} ({U.Sectors[sec].Name}): Sektor verloren; {r.Message}");
                }
        }
        _ausSectors = own;
        if (own < AusMin) return;
        foreach (var st in PlayerUpkeepOrder.Governments(U).Where(x => !x.Dormant).ToList())
        {
            if (PlayerUpkeepOrder.Rank(U, st) < 2) continue;
            if (PlayerUpkeepOrder.RestIssue(U, st.Id, true) is not null) continue;
            var r = C.Execute(U, $"rest:on:{st.Id.Value}");
            if (r.Success) { AusRests++; _ausRested.Add((st.Id, st.Sector, U.Tick)); AusLog.Add($"[{U.Tick}] ruht {st.Name} [{st.TypeKey}] in {U.Sectors[st.Sector].Name}"); Note($"Sparkurs: {st.Name} ruht ({own} Sektoren, Kasse {Wallet:0})"); }
            break;   // eine pro Takt: Ränge ändern sich mit jeder Ruhe
        }
    }
}
