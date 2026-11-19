namespace SpaceSim.Core.Model;

public sealed class Sector
{
    public SectorId Id;
    /// <summary>Lage auf der Sternenkarte (nur Darstellung).</summary>
    public Vec2 MapPosition;
    public string Name = "";
    /// <summary>
    /// Besitzerfraktion. Phase 10: abgeleitet aus dem Einfluss (<see cref="Simulation.SectorControl"/>, mit Hysterese), Startwert aus der Weltdatei;
    /// ein Sektor ohne Stationen behält seinen Besitzer.
    /// </summary>
    public FactionId Owner;
    /// <summary>Phase 10: Sperre des Sektors (Stufen 0–5 für Ein- und Ausreise), gesetzt vom <see cref="Simulation.FactionPolicySystem"/>.</summary>
    public SectorAccess Access = new();
    /// <summary>Phase 10: Einflussanteile je Fraktion (0..1, nur Fraktionen mit Anteil &gt; 0), zuletzt berechnet von <see cref="Simulation.SectorControl"/>.</summary>
    public Dictionary<FactionId, float> Influence = new();
    /// <summary>Phase 10: Kontrollstufe des Besitzers (Präsenz … Kontrolle).</summary>
    public ControlStage Control;
    /// <summary>Phase 10: Umkämpft — zwei Fraktionen über dem Schwellenanteil (<see cref="EconomyRules.ContestedShare"/>).</summary>
    public bool Contested;
    /// <summary>
    /// Phase 11 (Lagekarte, FLOTTEN.md Abschnitt 6): Bedrohung des Sektors — steigt durch Vorfälle (Überfälle, abgewehrte Überfälle, Schmuggel),
    /// verfällt mit der Zeit und sinkt durch Patrouillen (<see cref="Simulation.ThreatMap"/>). Gespeichert.
    /// </summary>
    public float Threat;
    /// <summary>
    /// Phase 11: Sicherheitsniveau (geglättet): Grundschutz der Regierungsstationen des Besitzers (<see cref="StationType.Security"/>) plus Stärke der
    /// Polizeischiffe des Besitzers im Sektor ÷ <see cref="EconomyRules.PoliceStrengthUnit"/>. Gespeichert.
    /// </summary>
    public float Security;
    /// <summary>Phase 11: Verkehrswert (geglättet): Wert der Fracht an Bord der KI-Schiffe im Sektor zum Basispreis — der „Wert des Sektors“ für Polizei und Piraten. Gespeichert.</summary>
    public float Traffic;
    public float Radius = 10_000f;
    /// <summary>
    /// Runde 44: Maßstab des Sektors (1 = Grundmaßstab). Gespeichert, gehasht nur ≠ 1. Die Positionen sind bereits vergrößert; der Faktor skaliert nur
    /// Regeln, die die Sektorgröße stillschweigend voraussetzen (Platzierungsfläche, Gefechtsfläche, Hintergrundfelsen).
    /// </summary>
    public float Scale = 1f;
    /// <summary>
    /// Runde 44: Ausdehnung — größter Abstand einer Station, eines Tors oder einer Feldmitte zum Ursprung plus 60. Abgeleitet (nach Aufbau und Laden,
    /// <see cref="SectorExtent.Recompute"/>), nicht gespeichert.
    /// </summary>
    public float Extent = 400f;
    /// <summary>
    /// Runde 44: Gefechtsfläche um den Ursprung — bei Maßstab 1 die Regel (<see cref="EconomyRules.CombatArenaRadius"/>, unverändert), sonst
    /// max(Regel × Maßstab, Ausdehnung + 100). Ohne das klemmte <c>CombatSpace.Clear</c> Schiffe jenseits von 400 m auf 400 zurück (Teleport).
    /// </summary>
    public float ArenaRadius(EconomyRules rules) => Scale <= 1f ? rules.CombatArenaRadius : MathF.Max(rules.CombatArenaRadius * Scale, Extent + 100f);
    /// <summary>Phase 14: Planet des Sektors (Kulisse, Abnehmer der Exportsenke); null = keiner.</summary>
    public Planet? Planet;
    /// <summary>Himmelskugel des Sektors (Schlüssel der Darstellung); "" = keine. Reine Darstellung, gespeichert, nicht im Hash.</summary>
    public string Skybox = "";
    /// <summary>Drehung des Himmels um die Hochachse in Grad.</summary>
    public float SkyboxYaw;
    public List<GateId> Gates = new();
    public List<StationId> Stations = new();
    public List<ResourceField> Fields = new();
    public List<Asteroid> Asteroids = new();
    /// <summary>Gasernte: der ganze Sektor liegt in einem Nebel (Wirkungen nur mit Regel <see cref="EconomyRules.NebulaEffects"/>). Aus der Weltdatei, gespeichert, gehasht nur wenn gesetzt.</summary>
    public bool Nebula;
    /// <summary>Gasernte: Gasfelder des Sektors (leer = keine). Gespeichert, gehasht nur wenn vorhanden.</summary>
    public List<GasField> GasFields = new();

    /// <summary>Gasfeld an einer Position (das erste, das sie enthält), sonst null.</summary>
    public GasField? GasFieldAt(Vec2 p)
    {
        foreach (var f in GasFields) if (f.Contains(p)) return f;
        return null;
    }

    /// <summary>Rohstofffeld, zu dessen Gürtel eine Position gehört (nächstes Feld im <see cref="Mining.MiningRules.BeltRadius"/>).</summary>
    public ResourceField? FieldFor(Vec2 p)
    {
        ResourceField? best = null;
        float bestD = Mining.MiningRules.BeltRadius;
        foreach (var f in Fields)
        {
            float d = Vec2.Distance(p, f.Position);
            if (d <= bestD) { bestD = d; best = f; }
        }
        return best;
    }
}

public sealed class Gate
{
    public GateId Id;
    public SectorId Sector;
    public Vec2 Position;
    public GateId Target;
    /// <summary>
    /// Phase 10: Zustand des Gates (offen, eingeschränkt, geschlossen, zerstört). Geschlossen/zerstört sperrt physisch für alle; eingeschränkt
    /// setzt das Zugangsmodell, solange an einem Ende eine Sperrstufe gilt. Änderungen über <see cref="Simulation.GateAccess.SetState"/>.
    /// </summary>
    public GateState State = GateState.Open;
    /// <summary>
    /// Phase 10: Hauptroute (POLITIK.md „eine Hauptroute je Region bleibt offen“) — Ausnahme von der Sektorsperre: hier gilt höchstens
    /// <see cref="EconomyRules.MainRouteMaxLevel"/>. Aus der Weltdatei (<c>mainRoute</c> am Gate-Paar), gespeichert.
    /// </summary>
    public bool MainRoute;
    /// <summary>Phase 31: Torklasse (<see cref="Universe.GateTypes"/>; leer = ohne Kampfwerte). Aus der Weltdatei (<c>gates[].type</c>, sonst main/side nach
    /// <see cref="MainRoute"/>), gespeichert, nicht gehasht (Inhalt).</summary>
    public string TypeKey = "";
    /// <summary>Phase 31: Kampfzustand des Paars — nur am Tor mit der kleineren Id gesetzt (null = unbeschädigt). Gespeichert, gehasht nur wenn gesetzt.</summary>
    public GateCondition? Condition;
    /// <summary>Physisch passierbar (offen oder eingeschränkt)? Früher ein eigener Schalter; ein Setzen schließt bzw. öffnet das Gate.</summary>
    public bool Open
    {
        get => State is GateState.Open or GateState.Restricted;
        set => State = value ? (State is GateState.Closed or GateState.Destroyed ? GateState.Open : State) : GateState.Closed;
    }
}

public sealed class ResourceField
{
    /// <summary>Asteroidenklasse des Gürtels (bestimmt Erzsorten, Reichtum, Trefferchance).</summary>
    public string ClassKey = "";
    public Vec2 Position;
    /// <summary>Soll-Anzahl abbaubarer Asteroiden im Gürtel; fehlende wachsen nach.</summary>
    public int TargetAsteroids = 14;
    /// <summary>Mittlere Ticks, bis ein fehlender Asteroid nachwächst (Respawn).</summary>
    public int RespawnTicks = 120;

    /// <summary>Freie Zone um den Feldmittelpunkt, in der keine Asteroiden liegen.</summary>
    public const float ClearRadius = 18f;

    /// <summary>Arbeitsposition eines Miners (je Schiff leicht versetzt).</summary>
    public Vec2 WorkPoint(ShipId ship) => Position + Vec2.FromAngle(ship.Value * 2.4f) * 8f;
}
