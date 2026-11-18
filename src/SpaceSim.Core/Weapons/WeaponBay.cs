using SpaceSim.Core.Mining;

namespace SpaceSim.Core.Weapons;

/// <summary>Ausrichtung eines Waffenschachts am Schiff (siehe SCHIFFSSLOTS.md: Bug, Backbord, Steuerbord, Heck).</summary>
public enum Facing { Bow, Port, Starboard, Stern }

/// <summary>
/// Datensatz eines Waffenschachts: Ausrichtung <see cref="Facing"/>, halber Öffnungswinkel <see cref="ArcDegrees"/>
/// (>= 360 = Turm mit Vollkreis) und Reichweite. Reine Daten, die Winkelprüfung liegt in <see cref="FiringArc"/>.
/// </summary>
public sealed class WeaponBay
{
    public string Key = "";
    public string Name = "";
    public Facing Facing = Facing.Bow;
    /// <summary>Halber Öffnungswinkel in Grad; 360 oder mehr bedeutet Turm (jede Richtung liegt im Winkel).</summary>
    public float ArcDegrees = 360f;
    /// <summary>Reichweite in Einheiten (Abstand Schiff–Ziel minus Zielradius, siehe <see cref="FiringArc.State"/>).</summary>
    public float Range;

    public bool IsTurret => ArcDegrees >= 360f;
}

/// <summary>Anzeigbarer Zustand einer Waffenschacht-Taste (UI.md Abschnitt 2).</summary>
public enum BayState
{
    /// <summary>Ziel im Winkel und Waffe bereit – feuert sofort (grün, „FEUER“).</summary>
    Ready,
    /// <summary>Ziel in Reichweite, aber außerhalb des Winkels – gesperrt mit Drehhinweis (orange).</summary>
    OutsideArc,
    /// <summary>Lädt nach dem Schuss nach – grau mit Balken.</summary>
    Reloading,
    /// <summary>Kein Ziel in Reichweite (gedämpft).</summary>
    NoTarget,
    /// <summary>Phase 25: Waffe ohne Leistung (Bank leer und kein Überschuss für die Waffen) — gesperrt.</summary>
    NoPower,
    /// <summary>Phase 25: Magazin leer — gesperrt (nachladen über den Dienst „rearm“ oder aus der Fracht).</summary>
    NoAmmo,
}

/// <summary>Standard-Schächte des Spielerschiffs. Der Mining-Laser sitzt als 360°-Turm, damit sich das Spielverhalten nicht ändert.</summary>
public static class WeaponBays
{
    public static IReadOnlyList<WeaponBay> PlayerDefault { get; } = new[]
    {
        new WeaponBay { Key = "laser", Name = "Laser", Facing = Facing.Bow, ArcDegrees = 360f, Range = MiningRules.LaserRange },
        new WeaponBay { Key = "tractor", Name = "Traktor", Facing = Facing.Bow, ArcDegrees = 360f, Range = MiningRules.TractorRange },
    };

    /// <summary>
    /// Runde 34b (Prüfung B8): welche Schächte „Alle Feuer“ bzw. die Sammeltaste „+n weitere“ (ab <paramref name="from"/>) auslöst — genau die bereiten
    /// (<see cref="BayState.Ready"/>: geladen, im Feuerwinkel, mit Ziel, Energie und Munition) und nur Schächte, die feuern (nicht Traktor, Spalter, Bergen, Bohrer).
    /// Eine Regel für beide Tasten.
    /// </summary>
    public static List<int> ReadyToFire(IReadOnlyList<WeaponBay> bays, IReadOnlyList<BayState> states, int from = 0)
    {
        var list = new List<int>();
        for (int i = Math.Max(0, from); i < bays.Count && i < states.Count; i++)
            if (bays[i].Key is not ("tractor" or "split" or "salvage" or "contact" or "jump" or "cloak") && states[i] == BayState.Ready) list.Add(i);
        return list;
    }

    /// <summary>Phase 8: Spaltlaser (Asteroidenbrecher) als eigener Schacht, Reichweite wie der Mining-Laser.</summary>
    public static WeaponBay Splitter { get; } = new() { Key = "split", Name = "Spalter", Facing = Facing.Bow, ArcDegrees = 360f, Range = MiningRules.LaserRange };

    /// <summary>Nachladezeit des Spaltlasers in Sekunden (Spielerschacht).</summary>
    public const float SplitReloadSeconds = 2f;

    /// <summary>Phase 26: Bergungsgreifer als eigener Schacht (Module aus Wracks bergen; ein Griff je Auslösung).</summary>
    public static WeaponBay Salvager { get; } = new() { Key = "salvage", Name = "Bergen", Facing = Facing.Bow, ArcDegrees = 360f, Range = 30f };

    /// <summary>
    /// Phase 29: Kontaktwerkzeug (Bohrkopf, Schneidrad, Schneidwalze) als Anzeige-Schacht — feuert nicht, zeigt nur den Zustand (kein Kontakt, Abbau läuft,
    /// Laderaum voll); Abbau läuft von selbst, solange die Werkzeugfront den Fels berührt (<see cref="ContactMining"/>).
    /// </summary>
    public static WeaponBay Contact { get; } = new() { Key = "contact", Name = "Bohrer", Facing = Facing.Bow, ArcDegrees = ContactMining.FrontArcDeg, Range = ContactMining.ContactRange + ContactMining.BowOffset };

    /// <summary>Sprungantrieb als eigener Schacht (Taste „Sprung“: Ziel auf der Karte wählen, Laden, Abklingen; Ladeabbruch bei erneutem Tippen).</summary>
    public static WeaponBay Jump { get; } = new() { Key = "jump", Name = "Sprung", Facing = Facing.Bow, ArcDegrees = 360f, Range = 0f };

    /// <summary>Tarnfeld als eigener Schacht (Umschalter „Tarnung“).</summary>
    public static WeaponBay Cloak { get; } = new() { Key = "cloak", Name = "Tarnung", Facing = Facing.Bow, ArcDegrees = 360f, Range = 0f };

    /// <summary>Anzeigetext des Kontaktschachts.</summary>
    /// <summary>Phase 32: Text mit Ankerzustand — „verankert · Abbau läuft“ / „Anker fliegt“; kurz nach einem Abriss dessen Grund.</summary>
    public static string ContactText(ContactState state, bool anchors, string? broke = null) =>
        broke is { Length: > 0 } && state is ContactState.NoContact ? broke
        : anchors && state == ContactState.Mining ? "verankert"          // Runde 18: passt in die 176-px-Taste (vorher „verankert · Abb…“)
        : anchors && state == ContactState.Anchoring ? "Anker fliegt"
        : anchors && state == ContactState.NoContact ? "Bug an Fels"
        : ContactText(state);

    public static string ContactText(ContactState state) => state switch
    {
        ContactState.Mining => "Abbau läuft",
        ContactState.Anchoring => "Anker fliegt",
        ContactState.HoldFull => "Laderaum voll",
        ContactState.Released => "gelöst",
        ContactState.NoTool => "kein Werkzeug",
        _ => "Bug an Fels",   // Runde 18: passt in die Taste (vorher „kein Kontakt · Bug an Fels“)
    };

    /// <summary>Schächte eines Schiffs: Laser und Traktor wie bisher, dazu der Spalter (Spaltlaser im Fit) und — Phase 26 — „Bergen“ (Fähigkeit Bergung).
    /// Phase 29: ein Schiff mit Kontaktwerkzeug zeigt „Bohrer“ statt „Laser“, wenn es keinen Laser hat (mit beidem beide).</summary>
    public static IReadOnlyList<WeaponBay> For(SpaceSim.Core.Model.ShipStats stats)
    {
        if (stats.ContactMining > 0f)
        {
            var c = new List<WeaponBay>();
            if (stats.HasLaser) c.Add(PlayerDefault[0]);
            c.Add(Contact);
            c.Add(PlayerDefault[1]);
            if (stats.SplitPower > 0f) c.Add(Splitter);
            if ((stats.Capabilities & SpaceSim.Core.Model.Capability.Salvage) != 0) c.Add(Salvager);
            return c;
        }
        if (stats.SplitPower <= 0f && (stats.Capabilities & SpaceSim.Core.Model.Capability.Salvage) == 0) return PlayerDefault;
        var list = PlayerDefault.ToList();
        if (stats.SplitPower > 0f) list.Add(Splitter);
        if ((stats.Capabilities & SpaceSim.Core.Model.Capability.Salvage) != 0) list.Add(Salvager);
        return list;
    }

    /// <summary>
    /// Phase 25: Schächte eines Schiffs mit Kampfwaffen: zuerst die Bergbauschächte wie bisher (der Mining-Laser nur, wenn das Schiff abbauen kann oder keine
    /// Kampfwaffe hat), dann ein Schacht je Kampfwaffe (Schlüssel „w:&lt;slot&gt;“, Feuerwinkel und Reichweite aus Slot und Modul).
    /// </summary>
    public static IReadOnlyList<WeaponBay> For(SpaceSim.Core.Model.Universe u, SpaceSim.Core.Model.Ship ship)
    {
        var bays = ForWeapons(u, ship);
        // Sprungantrieb und Tarnfeld: eigene Schächte am Ende (eigene Gruppen im Daumenbogen)
        bool jump = SpaceSim.Core.Flight.JumpDrive.Fitted(u, ship), cloak = SpaceSim.Core.Flight.Cloak.Fitted(u, ship);
        if (!jump && !cloak) return bays;
        var list = bays.ToList();
        if (jump) list.Add(Jump);
        if (cloak) list.Add(Cloak);
        return list;
    }

    static IReadOnlyList<WeaponBay> ForWeapons(SpaceSim.Core.Model.Universe u, SpaceSim.Core.Model.Ship ship)
    {
        var weapons = SpaceSim.Core.Combat.PlayerGunnery.Bays(u, ship);
        // Zweiter Gerätetest Phase 28: ein Schiff ohne Bergbaulaser (Nomad) zeigt keinen „Laser“-Schacht mehr (Modell mit Fit ohne Mining)
        if (weapons.Count == 0 && (ship.Has(SpaceSim.Core.Model.Capability.Mining) || !ship.HasModel)) return For(ship.Stats);
        var list = new List<WeaponBay>();
        foreach (var b in For(ship.Stats))
            if (b.Key != "laser" || ship.Has(SpaceSim.Core.Model.Capability.Mining)) list.Add(b);
        list.AddRange(weapons);
        return list;
    }
}
