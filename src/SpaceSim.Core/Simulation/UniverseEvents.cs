using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Eine Art von Universumsereignis (registrierbar, <see cref="UniverseEventTypes.Default"/>). Die Arten erzeugen nur Dinge, die es schon gibt — Gürtelfelsen
/// (<see cref="Mining.BeltPlacement"/>), Wracks und Frachtbehälter (<see cref="Wrecks"/>) —, damit Spieler, NPC-Schürfer und NPC-Bergeschiffe sie mit ihren
/// gewohnten Regeln nutzen. Zufall nur aus dem übergebenen RNG (eigener RNG aus Welt-Seed und Takt).
/// </summary>
public interface IUniverseEventType
{
    /// <summary>Schlüssel der Art (gespeichert).</summary>
    string Key { get; }
    /// <summary>Anzeigename („Erzschwemme“).</summary>
    string Name { get; }
    /// <summary>Gewicht der Art bei der Auswahl (0 = aus).</summary>
    float Weight(EconomyRules r);
    /// <summary>Kann die Art in diesem Sektor stattfinden? Rein.</summary>
    bool Possible(Universe u, Sector s);
    /// <summary>
    /// Erzeugt die Dinge des Ereignisses und trägt Ort, Detail und Ids in <paramref name="e"/> ein (Sektor, Beginn und Ende sind gesetzt).
    /// <paramref name="at"/> = erzwungener Ort (Test), sonst wählt die Art. false = kein Platz gefunden (nichts erzeugt).
    /// </summary>
    bool Start(Universe u, UniverseEvent e, DeterministicRng rng, Vec2? at);
    /// <summary>Alles genutzt (Felsen abgebaut, Wracks geleert)?</summary>
    bool Exhausted(Universe u, UniverseEvent e);
    /// <summary>Aufräumen beim Ende: Ungenutztes verschwindet.</summary>
    void End(Universe u, UniverseEvent e);
    /// <summary>Kurztext („Erzschwemme: reiche Felsen im Metallgürtel“).</summary>
    string Title(Universe u, UniverseEvent e);
}

/// <summary>Registry der Ereignisarten (Reihenfolge = Auswahlreihenfolge, doppelter Schlüssel wirft).</summary>
public static class UniverseEventTypes
{
    public static readonly IReadOnlyList<IUniverseEventType> Default = Build(new OreSurgeEvent(), new CargoFindEvent(), new WreckFieldEvent());

    static IReadOnlyList<IUniverseEventType> Build(params IUniverseEventType[] types)
    {
        var seen = new HashSet<string>();
        foreach (var t in types) if (!seen.Add(t.Key)) throw new InvalidOperationException($"Ereignisart doppelt: {t.Key}");
        return types;
    }

    public static IUniverseEventType? Of(string key) => Default.FirstOrDefault(t => t.Key == key);
}

/// <summary>
/// Regeln der Universumsereignisse: alle <see cref="EconomyRules.UniverseEventCheckTicks"/> wird mit einem eigenen RNG (Welt-Seed ⊕ Takt, wie die
/// Auftrags-RNGs) gewürfelt, ob ein neues Ereignis beginnt (Art nach Gewicht, Sektor gleichverteilt unter den möglichen); laufende enden nach ihrer Frist
/// oder sobald alles genutzt ist. <see cref="Universe.Rng"/> wird nie gezogen — die Welt ändert sich trotzdem (neue Felsen, Wracks), also ändert sich der Hash.
/// </summary>
public static class UniverseEvents
{
    /// <summary>Eigener RNG für einen Takt (derselbe Seed + Takt ⇒ dieselben Ereignisse).</summary>
    public static DeterministicRng Rng(Universe u, long tick, int salt = 0) =>
        new(u.Seed ^ 0x55455645_4E545321UL ^ (0x9E3779B97F4A7C15UL * (ulong)(tick + 1)) ^ (0xC2B2AE3D27D4EB4FUL * (ulong)(uint)salt));

    /// <summary>Ein Schritt (aus <see cref="UniverseEventSystem"/>): Ablauf prüfen, zum Prüftakt ggf. ein neues Ereignis würfeln.</summary>
    public static void Step(Universe u)
    {
        var r = u.Rules;
        if (!r.UniverseEvents) return;
        Expire(u);
        int every = Math.Max(UniverseEventSystem.Interval, r.UniverseEventCheckTicks);
        if (u.Tick <= 0 || u.Tick % every != 0) return;
        if (u.UniverseEvents.Count >= r.UniverseEventMaxActive) return;
        var rng = Rng(u, u.Tick);
        if (rng.NextDouble() >= r.UniverseEventChance) return;
        var candidates = UniverseEventTypes.Default.Where(t => t.Weight(r) > 0f && Sectors(u, t).Count > 0).ToList();
        if (candidates.Count == 0) return;
        float total = candidates.Sum(t => t.Weight(r));
        double roll = rng.NextDouble() * total;
        var type = candidates[^1];
        foreach (var t in candidates) { roll -= t.Weight(r); if (roll < 0) { type = t; break; } }
        var sectors = Sectors(u, type);
        var sector = sectors[rng.NextInt(sectors.Count)];
        Begin(u, type, sector.Id, rng, null);
    }

    /// <summary>Sektoren, in denen die Art stattfinden kann (Id-Reihenfolge; ohne Sektoren, in denen schon ein Ereignis läuft).</summary>
    public static List<Sector> Sectors(Universe u, IUniverseEventType t) =>
        u.Sectors.Values.OrderBy(s => s.Id.Value).Where(s => u.UniverseEvents.All(e => e.Sector != s.Id) && t.Possible(u, s)).ToList();

    /// <summary>Erzwingt ein Ereignis (Test/Selbsttest): Art, Sektor, optional Ort. null = Art unbekannt/nicht möglich/kein Platz.</summary>
    public static UniverseEvent? Force(Universe u, string key, SectorId sector, Vec2? at = null)
    {
        if (UniverseEventTypes.Of(key) is not { } t || !u.Sectors.TryGetValue(sector, out var s) || !t.Possible(u, s)) return null;
        return Begin(u, t, sector, Rng(u, u.Tick, u.NextUniverseEventId), at);
    }

    static UniverseEvent? Begin(Universe u, IUniverseEventType type, SectorId sector, DeterministicRng rng, Vec2? at)
    {
        var r = u.Rules;
        int lo = Math.Max(1, r.UniverseEventMinTicks), hi = Math.Max(lo, r.UniverseEventMaxTicks);
        long duration = lo + rng.NextInt(hi - lo + 1);
        var e = new UniverseEvent { Kind = type.Key, Sector = sector, StartTick = u.Tick, EndTick = u.Tick + duration };
        if (!type.Start(u, e, rng, at)) return null;
        e.Id = u.NextUniverseEventId++;
        u.UniverseEvents.Add(e);
        u.Events.Publish(new UniverseEventStarted(e.Id, e.Kind, e.Sector, e.Position, type.Title(u, e)));
        return e;
    }

    /// <summary>Beendet abgelaufene oder genutzte Ereignisse (Reihenfolge des Beginns).</summary>
    public static void Expire(Universe u)
    {
        for (int i = 0; i < u.UniverseEvents.Count; i++)
        {
            var e = u.UniverseEvents[i];
            var t = UniverseEventTypes.Of(e.Kind);
            string? why = t is null ? "unknown" : u.Tick >= e.EndTick ? "expired" : t.Exhausted(u, e) ? "exhausted" : null;
            if (why is null) continue;
            t?.End(u, e);
            u.UniverseEvents.RemoveAt(i--);
            u.Events.Publish(new UniverseEventEnded(e.Id, e.Kind, e.Sector, why));
        }
    }

    /// <summary>Freier Platz im Sektor (fern von Stationen, Toren und Felsen); null = keiner gefunden.</summary>
    public static Vec2? FreeSpot(Universe u, Sector s, DeterministicRng rng, float clear)
    {
        // Reichweite live aus Stationen, Toren und Feldern (nicht Sector.Extent: abgeleitet und nur beim Bau/Laden berechnet — nach dem Laden anders)
        float reach = 120f;
        foreach (var id in s.Stations) if (u.Stations.TryGetValue(id, out var st)) reach = MathF.Max(reach, st.Position.Length);
        foreach (var g in s.Gates) reach = MathF.Max(reach, u.Gates[g].Position.Length);
        foreach (var f in s.Fields) reach = MathF.Max(reach, f.Position.Length);
        for (int attempt = 0; attempt < 32; attempt++)
        {
            var p = Vec2.FromAngle(rng.Range(0f, MathF.Tau)) * rng.Range(0.15f * reach, 0.7f * reach);
            if (Clear(u, s, p, clear)) return p;
        }
        return null;
    }

    /// <summary>Ist der Punkt frei (Stationen + 40, Tore + 50, Felsen + <paramref name="clear"/>)?</summary>
    public static bool Clear(Universe u, Sector s, Vec2 p, float clear) =>
        s.Stations.All(id => !u.Stations.TryGetValue(id, out var st) || Vec2.Distance(p, st.Position) > st.Radius + 40f + clear) &&
        s.Gates.All(g => Vec2.Distance(p, u.Gates[g].Position) > 50f + clear) &&
        s.Asteroids.All(a => Vec2.Distance(p, a.Position) > a.BaseRadius + 4f + clear);
}

/// <summary>Universumsereignisse: Ablauf und neue Ereignisse (eigenes Modul, am Ende; ohne Schalter nichts).</summary>
public sealed class UniverseEventSystem : ISimSystem
{
    public const int Interval = 50;
    public void Step(Universe u, int dt) => UniverseEvents.Step(u);
}
