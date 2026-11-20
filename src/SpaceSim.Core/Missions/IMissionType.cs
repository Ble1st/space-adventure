using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Runde 41: eigene Zufallsquelle der Aufträge — abgeleitet aus Welt-Seed, Station und Angebots-Takt (Bucket), nie
/// <see cref="Universe.Rng"/> (der Welt-RNG bleibt unberührt, Angebote ändern keinen Hash). Muster wie
/// <see cref="Content.FactionNames.WorldRng"/>.
/// </summary>
public static class MissionRng
{
    /// <summary>Eigener RNG für (Station, Bucket); derselbe Seed + dasselbe Bucket ⇒ dieselben Angebote.</summary>
    public static DeterministicRng For(Universe u, int stationId, long bucket) =>
        new(u.Seed ^ (0x9E3779B97F4A7C15UL * (ulong)(uint)stationId) ^ (0x4D414E45UL * (ulong)bucket));

    /// <summary>Nächste Gleitkommazahl 0..1 aus dem Missions-RNG.</summary>
    public static float NextFloat(DeterministicRng rng) => (float)rng.NextDouble();

    /// <summary>Ganze Zahl [0, n) aus dem Missions-RNG.</summary>
    public static int NextInt(DeterministicRng rng, int n) => rng.NextInt(n);
}

/// <summary>
/// Runde 41: eine Auftragsart (Registry wie <see cref="Services.StationServices"/>; neue Arten — Kopfgeldjagd, Eskorte,
/// Patrouille in Runde 42 — kommen als neue Klasse dazu). <see cref="Generate"/> ist rein (liefert das Angebot, ändert nichts),
/// <see cref="AcceptIssue"/> prüft streng, <see cref="Accept"/> bucht die Annahme (nur nach bestandenem <see cref="AcceptIssue"/>).
/// </summary>
public interface IMissionType
{
    /// <summary>Eindeutiger Schlüssel der Art, z. B. „delivery“.</summary>
    string Key { get; }

    /// <summary>Erzeugt ein Angebot für diese Station (null = keins); <paramref name="rng"/> ist der Missions-RNG, <paramref name="bucket"/> der Angebots-Takt.</summary>
    MissionOffer? Generate(Universe u, Station st, DeterministicRng rng, long bucket);

    /// <summary>Sperrgrund der Annahme (null = möglich). Rein.</summary>
    string? AcceptIssue(Universe u, MissionOffer o, Ship ship, bool remote);

    /// <summary>Bucht die Annahme (baut die <see cref="Mission"/> und bucht typspezifischen Zustand wie Pfand-Fracht oder Kassen-Reservierung).</summary>
    Mission Accept(Universe u, MissionOffer o, Ship ship, bool remote);

    /// <summary>Fortschritt aus einem Simulationsereignis (Verkauf/Kauf des Spielers); nur das eigene Schiff zählt.</summary>
    void OnEvent(Universe u, Mission m, SimEvent e);

    /// <summary>Ist das Ziel des Auftrags weg (Station entfernt)? Dann scheitert er.</summary>
    bool TargetGone(Universe u, Mission m);

    /// <summary>Marker-Ziel (Sektor, Position, Anzeige, Art des Ziels — Leuchtfeuer/Randpfeil/Karten) oder null, wenn es keines gibt.</summary>
    (SectorId Sector, Vec2 Pos, string Label, MissionMarkerKind Kind)? Marker(Universe u, Mission m);

    /// <summary>Anzeige des Angebots (Vorschau: Belohnung, Pfand, Frist, Ziel).</summary>
    string DescribeOffer(Universe u, MissionOffer o);

    /// <summary>Ziel des Angebots als Kurztext für die Vorschauzeile (Station, Schiff, Sektor …), leer = keins.</summary>
    string TargetText(Universe u, MissionOffer o);

    /// <summary>Frist des Angebots bei Annahme ab jetzt (Ticks), so wie <see cref="Accept"/> sie setzen würde — für die Vorschau.</summary>
    long DeadlineOf(Universe u, MissionOffer o, Ship ship);

    /// <summary>Anzeige des laufenden Auftrags (HUD-Zeile, Listen).</summary>
    string Describe(Universe u, Mission m);

    /// <summary>Prüft Frist und Erfüllung im Tick (bucht Scheitern/Erfüllung über <see cref="MissionRules.Fail"/>/<see cref="MissionRules.Complete"/>).</summary>
    void Check(Universe u, Mission m);

    /// <summary>Sperrgrund der Ablieferung (Transport) an dieser Station; null = möglich.</summary>
    string? DeliverIssue(Universe u, Mission m, Ship ship, Station st);

    /// <summary>Liefert die Fracht ab (bucht Erfüllung).</summary>
    void Deliver(Universe u, Mission m, Ship ship, Station st);
}

/// <summary>
/// Runde 41: Registry der Auftragsarten (Registrierungsreihenfolge, doppelter Schlüssel wirft). Offen für Runde 42
/// (Kopfgeldjagd, Eskorte, Patrouille registrieren sich hier einfach dazu).
/// </summary>
public sealed class MissionTypes
{
    readonly List<IMissionType> _types = new();
    readonly Dictionary<string, IMissionType> _byKey = new();

    public void Register(IMissionType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_byKey.ContainsKey(type.Key)) throw new ArgumentException($"Auftragsart '{type.Key}' ist bereits registriert.", nameof(type));
        _byKey.Add(type.Key, type);
        _types.Add(type);
    }

    public IMissionType? Get(string key) => _byKey.TryGetValue(key, out var t) ? t : null;

    public IReadOnlyList<IMissionType> All => _types;

    /// <summary>Standard-Registry: Lieferung und Transport (Runde 41) sowie Kopfgeldjagd, Eskorte und Patrouille (Runde 42).</summary>
    public static MissionTypes Default()
    {
        var r = new MissionTypes();
        r.Register(new DeliveryMission());
        r.Register(new TransportMission());
        r.Register(new BountyMission());
        r.Register(new EscortMission());
        r.Register(new PatrolMission());
        // Ausbau Punkte 10/14 (angehängt: die Würfe der älteren Arten im Missions-RNG bleiben gleich)
        r.Register(new SmuggleMission());
        r.Register(new ResearchMission());
        r.Register(new PassengerMission());
        return r;
    }
}
