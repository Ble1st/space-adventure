using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Weapons;

namespace SpaceSim.Core.Combat;

/// <summary>Phase 25: Ergebnis eines Spielerschusses (<see cref="PlayerGunnery.TryFire"/>).</summary>
public enum GunResult { Fired, NoWeapon, NoTarget, OutOfRange, Reloading, OutsideArc, NoPower, NoAmmo, NotAllowed }

/// <summary>Phase 25: ein Spielerschuss: Ergebnis, Treffer (nur bei <see cref="GunResult.Fired"/>), Gefecht und Text für die Anzeige.</summary>
public readonly record struct GunShot(GunResult Result, HitResult Hit, int Encounter, string Message);

/// <summary>
/// Phase 25 (KAMPF.md 2, 4.3): Waffen des aktiven Spielerschiffs in Echtzeit — Muster wie der Bergbau (<see cref="Mining.OreChunks.TryFire"/>): ein Schuss je
/// Auslösung, sofort über <see cref="Universe.Rng"/> gewürfelt (<see cref="Hit.Roll"/>, dieselbe Regel wie im NPC-Gefecht) und in der Simulation gebucht.
/// Nachladeuhren liegen im Gefecht (<see cref="CombatParticipant.Timers"/>, laufen im Tick-Takt ab und werden gespeichert), Spitzenlast und Überladung aus der
/// Bank, Munition aus dem Magazin (<see cref="Gunnery"/>). Der erste Schuss auf ein fremdes Schiff eröffnet ein Gefecht (<see cref="PlayerCombat.Engage"/>).
/// Zielen: Die Geschütze zielen selbst auf das gewählte Ziel; Bugwaffen brauchen das Ziel im Feuerwinkel (Peilung des Schiffs aus der Ansicht).
/// </summary>
public static class PlayerGunnery
{
    /// <summary>Schlüsselpräfix der Waffenschächte („w:&lt;slot&gt;“).</summary>
    public const string BayPrefix = "w:";

    /// <summary>Slots mit (funktionierender) Waffe, aufsteigend.</summary>
    public static List<int> WeaponSlots(Universe u, Ship ship)
    {
        var list = new List<int>();
        for (int i = 0; i < ship.Fit.Count; i++) if (Gunnery.WeaponAt(u, ship, i) is not null) list.Add(i);
        return list;
    }

    /// <summary>
    /// Runde 16 (Gerätetest: Haupttor dauerte ≈ 9 min, Munition und Raketen waren nach ≈ 90 s leer, ohne Hinweis): welche Waffen mangels Raketen/Munition
    /// schweigen und was noch feuert — „Raketen leer · Munition leer – nur noch Laser“. Leer = alle Waffen haben, was sie brauchen (oder keine Waffe braucht Vorrat).
    /// </summary>
    public static string DepletedText(Universe u, Ship ship)
    {
        bool rocketsOut = false, ammoOut = false;
        var still = new List<string>();
        foreach (int slot in WeaponSlots(u, ship))
        {
            var m = Gunnery.WeaponAt(u, ship, slot)!;
            if (Missiles.IsMissile(m)) { if (Missiles.Loaded(u, ship) < 1f) rocketsOut = true; else still.Add("Raketen"); continue; }
            var w = WeaponProfile.Of(m);
            if (!Gunnery.HasAmmo(u, ship, w)) { ammoOut = true; continue; }
            still.Add(w.AmmoPerShot > 0f ? "Massetr." : "Laser");
        }
        if (!rocketsOut && !ammoOut) return "";
        // Runde 17 (Handy-Test: „Raketen leer – nur noch Laser/Mas…“ abgeschnitten): Kurzform für die Zielkarte
        var parts = new List<string>();
        if (rocketsOut) parts.Add("Rak. leer");
        if (ammoOut) parts.Add("Mun. leer");
        var rest = still.Distinct().ToList();
        return string.Join(" · ", parts) + (rest.Count == 0 ? " · keine Waffe" : $" · nur {string.Join("/", rest)}");
    }

    /// <summary>Waffenschächte des Schiffs (einer je Waffe, Feuerwinkel aus dem Slot).</summary>
    public static List<WeaponBay> Bays(Universe u, Ship ship)
    {
        var list = new List<WeaponBay>();
        foreach (int slot in WeaponSlots(u, ship)) list.Add(Gunnery.BayOf(ship, slot, Gunnery.WeaponAt(u, ship, slot)!));
        return list;
    }

    /// <summary>Slot eines Schachtschlüssels (−1 = kein Waffenschacht).</summary>
    public static int SlotOf(string bayKey) =>
        bayKey.StartsWith(BayPrefix, StringComparison.Ordinal) && int.TryParse(bayKey.AsSpan(BayPrefix.Length), out int s) ? s : -1;

    /// <summary>Verbleibende Nachladezeit (Sekunden) einer Waffe des Schiffs (0 = bereit; außerhalb eines Gefechts immer bereit).</summary>
    public static float ReloadLeft(Universe u, Ship ship, int slot) => u.Fights.Of(ship.Id)?.Of(ship.Id) is { } p ? p.TimerOf(slot) : 0f;

    /// <summary>Nachlade-Fortschritt 0..1 (1 = bereit) für den Balken der Taste.</summary>
    public static float ReloadProgress(Universe u, Ship ship, int slot)
    {
        if (Gunnery.WeaponAt(u, ship, slot) is not { } m) return 1f;
        float left = ReloadLeft(u, ship, slot);
        return left <= 0f ? 1f : 1f - Math.Clamp(left / MathF.Max(0.05f, m.Stat("reload")), 0f, 1f);
    }

    /// <summary>
    /// Zustand der Schachttaste (rein): kein Ziel / außer Reichweite → <see cref="BayState.NoTarget"/>, keine Munition, lädt nach, keine Leistung, außerhalb des
    /// Winkels, sonst bereit.
    /// </summary>
    public static BayState State(Universe u, Ship ship, int slot, float headingDeg, ShipId target)
    {
        if (Gunnery.WeaponAt(u, ship, slot) is not { } m) return BayState.NoTarget;
        var w = WeaponProfile.Of(m);
        if (PlayerCombat.PositionOf(u, target) is not { } tp || tp.Sector != ship.Sector || target == ship.Id) return BayState.NoTarget;
        // Gerätetest Phase 27: ein Ziel, das der Spieler nicht angreifen darf (angedockt, eigenes Schiff, Verbündeter im Gefecht), ist kein Ziel — vorher „FEUER“ und dann verweigert
        if (u.Ships.TryGetValue(target, out var tShip) && PlayerCombat.TargetIssue(u, ship, tShip) is not null) return BayState.NoTarget;
        if (Vec2.Distance(ship.Position, tp.Pos) > w.Range) return BayState.NoTarget;
        if (!HasRound(u, ship, m, w)) return BayState.NoAmmo;
        if (ReloadLeft(u, ship, slot) > 0f) return BayState.Reloading;
        if (!Gunnery.HasPower(u, ship, w)) return BayState.NoPower;
        if (!FiringArc.Contains(Gunnery.BayOf(ship, slot, m), headingDeg, ship.Position, tp.Pos)) return BayState.OutsideArc;
        return BayState.Ready;
    }

    /// <summary>
    /// Feuert genau einen Schuss der Waffe in <paramref name="slot"/> auf <paramref name="target"/>, wenn alles passt (Reichweite, nachgeladen, Winkel, Munition,
    /// Leistung, erlaubtes Ziel). Sonst ändert sich nichts. <paramref name="headingDeg"/> = Peilung des Schiffs in der Ansicht (<see cref="FiringArc.Bearing"/>).
    /// </summary>
    public static GunShot TryFire(Universe u, Ship ship, int slot, ShipId target, float headingDeg)
    {
        if (ship.Id != u.PlayerShip || ship.DockedAt.Value != 0 || ship.Sector != u.PlayerSector) return Fail(GunResult.NotAllowed, "Angedockt — erst abdocken.");
        if (Gunnery.WeaponAt(u, ship, slot) is not { } m) return Fail(GunResult.NoWeapon, "Keine Waffe in diesem Slot.");
        if (!u.Ships.TryGetValue(target, out var victim) || PlayerCombat.PositionOf(u, target) is not { } tp || tp.Sector != ship.Sector || target == ship.Id)
            return Fail(GunResult.NoTarget, "Kein Ziel — ein Schiff antippen oder „Nächster Gegner“.");
        if (PlayerCombat.TargetIssue(u, ship, victim) is { } issue) return Fail(GunResult.NotAllowed, issue);
        var w = WeaponProfile.Of(m);
        float dist = Vec2.Distance(ship.Position, tp.Pos);
        if (dist > w.Range) return Fail(GunResult.OutOfRange, $"Ziel außer Reichweite ({dist:0} > {w.Range:0} m).");
        if (ReloadLeft(u, ship, slot) > 0f) return Fail(GunResult.Reloading, "lädt nach");
        if (!FiringArc.Contains(Gunnery.BayOf(ship, slot, m), headingDeg, ship.Position, tp.Pos)) return Fail(GunResult.OutsideArc, "Ziel außerhalb des Feuerwinkels — drehen.");
        if (!HasRound(u, ship, m, w)) return Fail(GunResult.NoAmmo, NoRoundText(u, m));
        if (!Gunnery.HasPower(u, ship, w)) return Fail(GunResult.NoPower, "Keine Leistung für die Waffe.");
        var (e, me, them) = PlayerCombat.Engage(u, ship, victim);
        if (e is null || me is null || them is null) return Fail(GunResult.NotAllowed, "Dieses Schiff kannst du nicht angreifen.");
        me.Pos = ship.Position;
        me.Dir = CombatAutopilot.FromBearing(headingDeg);
        if (!Gunnery.DrawPower(u, ship, w, out float reload)) return Fail(GunResult.NoPower, "Keine Leistung für die Waffe.");
        me.LastShotTick = u.Tick;   // Runde 6: hält das Gefecht (eigene Reichweite), solange der Spieler schießt
        if (u.Rules.Warfare && Missiles.IsMissile(m))
        {
            // Phase 28: Rakete — fliegt, Treffer erst am Ziel (Punktabwehr, Ausweichen)
            Missiles.Launch(u, e, ship, null, me.Side, m, w, victim.Id, default, dist, ship.Position, slot);
            while (me.Timers.Count <= slot) me.Timers.Add(0f);
            me.Timers[slot] = reload;
            return new GunShot(GunResult.Fired, default, e.Id, "Rakete unterwegs");
        }
        var hit = Gunnery.Shoot(u, e, me, ship, slot, m, w, them, victim, dist, 1f, reload);
        return new GunShot(GunResult.Fired, hit, e.Id, hit.Hit ? $"Treffer ({hit.ShieldDamage + hit.HullDamage:0})" : "Fehlschuss");
    }

    /// <summary>Phase 28: Schuss möglich aus Sicht der Munition (Raketenwerfer: Raketen im Magazin, sonst Munition).</summary>
    public static bool HasRound(Universe u, Ship ship, Module m, in WeaponProfile w) =>
        u.Rules.Warfare && Missiles.IsMissile(m) ? Missiles.HasMissile(u, ship) : Gunnery.HasAmmo(u, ship, w);

    static string NoRoundText(Universe u, Module m) => u.Rules.Warfare && Missiles.IsMissile(m)
        ? "Keine Raketen — an Werft oder Polizeiwache nachladen."
        : "Keine Munition — an Werft, Polizeiwache oder Handelsstation nachladen.";

    /// <summary>Phase 28: Zustand der Schachttaste mit einer Station als Ziel (rein).</summary>
    public static BayState StateAtStation(Universe u, Ship ship, int slot, float headingDeg, StationId station) =>
        StateAtTarget(u, ship, slot, headingDeg, SiegeRef.Of(station));

    /// <summary>Phase 31: Zustand der Schachttaste mit einem Belagerungsziel (Station oder Tor; rein).</summary>
    public static BayState StateAtTarget(Universe u, Ship ship, int slot, float headingDeg, SiegeRef target)
    {
        if (!u.Rules.Warfare || Gunnery.WeaponAt(u, ship, slot) is not { } m || SiegeTargets.Resolve(u, target) is not { } st || st.Sector != ship.Sector) return BayState.NoTarget;
        var w = WeaponProfile.Of(m);
        if (Vec2.Distance(ship.Position, st.Position) > w.Range + st.Radius) return BayState.NoTarget;
        if (!HasRound(u, ship, m, w)) return BayState.NoAmmo;
        if (ReloadLeft(u, ship, slot) > 0f) return BayState.Reloading;
        if (!Gunnery.HasPower(u, ship, w)) return BayState.NoPower;
        if (!FiringArc.Contains(Gunnery.BayOf(ship, slot, m), headingDeg, ship.Position, st.Position)) return BayState.OutsideArc;
        return BayState.Ready;
    }

    /// <summary>Phase 28: Warum der Spieler diese Station nicht angreifen kann (null = er kann).</summary>
    public static string? StationIssue(Universe u, Ship ship, Station st)
    {
        if (!u.Rules.Warfare) return "Stationsangriffe gibt es erst mit der Kriegsführung.";
        if (st.IsBuildSite) return "Baustellen greifst du nicht an.";
        if (st.Owner == ship.Owner) return "Eigene Stationen greifst du nicht an.";
        if (StationCombat.IsDown(u, st)) return "Die Station ist ausgeschaltet.";
        if (u.Fights.Of(ship.Id) is { } e && e.Station != st.Id) return "Du steckst in einem anderen Gefecht.";
        if (Siege.Of(u, st.Id) is { } s && s.Of(ship.Id) is { } me && me.Side != 0) return "Du verteidigst diese Station.";
        return null;
    }

    /// <summary>Phase 31: Warum der Spieler dieses Tor nicht angreifen kann (null = er kann; jedes Tor, auch Sackgassentore).</summary>
    public static string? GateIssue(Universe u, Ship ship, Gate g)
    {
        if (!u.Rules.Warfare || !u.Rules.GateCombat) return "Tore lassen sich nicht angreifen.";
        if (GateCombat.TypeOf(u, g) is null) return "Dieses Tor hat keine Kampfwerte.";
        if (GateCombat.IsDown(u, g)) return "Das Tor ist außer Betrieb.";
        if (GateCombat.InGrace(u, g)) return $"Notbetrieb – nicht angreifbar für {GateCombat.Clock(GateCombat.GraceLeft(u, g))}";   // Runde 14b
        if (u.Fights.Of(ship.Id) is { } e && e.Gate != g.Id) return "Du steckst in einem anderen Gefecht.";
        if (Siege.OfGate(u, g.Id) is { } s && s.Of(ship.Id) is { } me && me.Side != 0) return "Du verteidigst dieses Tor.";
        return null;
    }

    /// <summary>
    /// Phase 28: Feuert genau einen Schuss auf eine Station (Belagerung durch den Spieler): Reichweite bis zur Außenhaut, Nachladen, Winkel, Munition/Raketen,
    /// Leistung. Der erste Schuss eröffnet die Belagerung (Fahndung „assault_station“ bei der Stationsfraktion); die Station und ihre Polizei wehren sich.
    /// </summary>
    public static GunShot TryFireAtStation(Universe u, Ship ship, int slot, StationId station, float headingDeg)
    {
        if (ship.Id != u.PlayerShip || ship.DockedAt.Value != 0 || ship.Sector != u.PlayerSector) return Fail(GunResult.NotAllowed, "Angedockt — erst abdocken.");
        if (Gunnery.WeaponAt(u, ship, slot) is null) return Fail(GunResult.NoWeapon, "Keine Waffe in diesem Slot.");
        if (!u.Stations.TryGetValue(station, out var st) || st.Sector != ship.Sector) return Fail(GunResult.NoTarget, "Keine Station als Ziel.");
        if (StationIssue(u, ship, st) is { } issue) return Fail(GunResult.NotAllowed, issue);
        return FireAt(u, ship, slot, new StationTarget(u, st), headingDeg, "Station");
    }

    /// <summary>
    /// Phase 31: Feuert genau einen Schuss auf ein Tor (dieselben Regeln wie bei Stationen). Der erste Schuss eröffnet die Belagerung — schweres Verbrechen
    /// („assault_gate“) bei beiden Endfraktionen; Polizei/Marine/Stellungen der Verteidiger im Sektor wehren sich.
    /// </summary>
    public static GunShot TryFireAtGate(Universe u, Ship ship, int slot, GateId gate, float headingDeg)
    {
        if (ship.Id != u.PlayerShip || ship.DockedAt.Value != 0 || ship.Sector != u.PlayerSector) return Fail(GunResult.NotAllowed, "Angedockt — erst abdocken.");
        if (Gunnery.WeaponAt(u, ship, slot) is null) return Fail(GunResult.NoWeapon, "Keine Waffe in diesem Slot.");
        if (!u.Gates.TryGetValue(gate, out var g) || g.Sector != ship.Sector) return Fail(GunResult.NoTarget, "Kein Tor als Ziel.");
        if (GateIssue(u, ship, g) is { } issue) return Fail(GunResult.NotAllowed, issue);
        return FireAt(u, ship, slot, new GateTarget(u, g), headingDeg, "Tor");
    }

    static GunShot FireAt(Universe u, Ship ship, int slot, ISiegeTarget st, float headingDeg, string what)
    {
        var m = Gunnery.WeaponAt(u, ship, slot)!;
        var w = WeaponProfile.Of(m);
        float dist = MathF.Max(0f, Vec2.Distance(ship.Position, st.Position) - st.Radius);
        if (dist > w.Range) return Fail(GunResult.OutOfRange, $"{what} außer Reichweite ({dist:0} > {w.Range:0} m).");
        if (ReloadLeft(u, ship, slot) > 0f) return Fail(GunResult.Reloading, "lädt nach");
        if (!FiringArc.Contains(Gunnery.BayOf(ship, slot, m), headingDeg, ship.Position, st.Position)) return Fail(GunResult.OutsideArc, $"{what} außerhalb des Feuerwinkels — drehen.");
        if (!HasRound(u, ship, m, w)) return Fail(GunResult.NoAmmo, NoRoundText(u, m));
        if (!Gunnery.HasPower(u, ship, w)) return Fail(GunResult.NoPower, "Keine Leistung für die Waffe.");
        var e = SiegeTargets.SiegeOf(u, st.Ref) ?? Siege.Start(u, ship, st);
        // Gerätetest Phase 28 (Runde 6): wer die Belagerung verlassen hat (geflohen), tritt wieder bei — vorher galt der alte, abgelaufene Eintrag weiter: keine
        // Nachladeuhr (Dauerfeuer schoss jedes Bild, ≈ 70 Schüsse je Sekunde) und kein Ziel für Geschütze und Verteidiger
        var me = e.Of(ship.Id) is { } old ? (old.Active ? old : Engage.Rejoin(u, e, old, ship)) : Engage.Join(u, e, ship, 0, CombatTactics.Player, 0f, ship.Position);
        me.Pos = ship.Position;
        me.Dir = CombatAutopilot.FromBearing(headingDeg);
        if (!Gunnery.DrawPower(u, ship, w, out float reload)) return Fail(GunResult.NoPower, "Keine Leistung für die Waffe.");
        me.LastShotTick = u.Tick;
        if (Missiles.IsMissile(m))
        {
            Missiles.Launch(u, e, ship, null, me.Side, m, w, default, st.Ref, dist, ship.Position, slot);
            while (me.Timers.Count <= slot) me.Timers.Add(0f);
            me.Timers[slot] = reload;
            return new GunShot(GunResult.Fired, default, e.Id, "Rakete unterwegs");
        }
        var hit = SiegeShot.Shoot(u, e, me, ship, slot, m, w, st, dist, 1f, reload);
        return new GunShot(GunResult.Fired, hit, e.Id, hit.Hit ? $"Treffer ({hit.ShieldDamage + hit.HullDamage:0})" : "Fehlschuss");
    }

    static GunShot Fail(GunResult r, string m) => new(r, default, 0, m);
}

/// <summary>
/// Phase 25 (KAMPF.md 4.3): der Spieler als Gefechtspartei — Gefecht eröffnen oder einem laufenden beitreten (<see cref="Engage"/>), Angriff als Verbrechen
/// melden (Verbrechen „assault“ beim Sektorbesitzer; auf Gesetzlose keins), Polizei greift ein, Zielwahl „nächster Gegner“, Positionen von Schiffen im
/// Spielersektor (echte Gefechtsposition oder abgeleitete Reiseposition).
/// </summary>
public static class PlayerCombat
{
    /// <summary>Sektor, Position und Tempo eines Schiffs jetzt (Gefechtsteilnehmer im Raum: echte Position; sonst aus der Reise abgeleitet). null = unbekannt.</summary>
    public static (SectorId Sector, Vec2 Pos, float Speed)? PositionOf(Universe u, ShipId id)
    {
        if (!u.Ships.TryGetValue(id, out var s)) return null;
        if (u.Fights.Of(id) is { Spatial: true } e && e.Of(id) is { } p) return (e.Sector, p.Pos, p.Speed);
        var (sector, pos) = ThreatMap.Where(u, s);
        return (sector, pos, s.Transit is null ? 0f : Travel.SpeedOf(s));
    }

    /// <summary>Ist das Schiff im Besitz eines Gesetzlosen (Pirat)? Angriffe darauf sind kein Verbrechen.</summary>
    public static bool IsOutlaw(Universe u, ActorId owner) => u.Actors.TryGetValue(owner, out var a) && a.Kind == ActorKind.Outlaw;

    /// <summary>
    /// Runde 21 (Prüferhinweis: die Kurzform hing am Wortlaut der Langform): warum der Spieler ein Schiff nicht angreifen kann — ein Grund, aus dem Lang- und
    /// Kurztext kommen (<see cref="TargetIssueText"/>, <see cref="TargetIssueShortText"/>); <see cref="TargetBlock.None"/> = angreifbar.
    /// </summary>
    public static TargetBlock TargetBlockOf(Universe u, Ship player, Ship target)
    {
        if (target.IsPlayer) return TargetBlock.Own;
        if (target.DockedAt.Value != 0 && !u.Fights.IsEngaged(target.Id)) return TargetBlock.Docked;
        if (u.Fights.Of(player.Id) is { } mine && mine.Of(target.Id) is { Active: true } t && mine.Of(player.Id) is { } me && t.Side == me.Side)
            return TargetBlock.Ally;
        if (u.Fights.Of(target.Id) is { } theirs && u.Fights.Of(player.Id) is { } own && theirs.Id != own.Id) return TargetBlock.OtherFight;
        return TargetBlock.None;
    }

    /// <summary>Langtext eines Grundes (Meldung beim Schuss); null für <see cref="TargetBlock.None"/>.</summary>
    public static string? TargetIssueText(TargetBlock b) => b switch
    {
        TargetBlock.Own => "Eigene Schiffe greifst du nicht an.",
        TargetBlock.Docked => "Das Schiff liegt angedockt.",
        TargetBlock.Ally => "Das Schiff kämpft auf deiner Seite.",
        TargetBlock.OtherFight => "Das Schiff steckt in einem anderen Gefecht.",
        _ => null,
    };

    /// <summary>Kurztext eines Grundes für die Waffentaste; null für <see cref="TargetBlock.None"/>.</summary>
    public static string? TargetIssueShortText(TargetBlock b) => b switch
    {
        TargetBlock.Own => "eigenes Schiff",
        TargetBlock.Docked => "Ziel angedockt",
        TargetBlock.Ally => "kämpft mit dir",
        TargetBlock.OtherFight => "anderes Gefecht",
        _ => null,
    };

    /// <summary>Warum der Spieler dieses Schiff nicht angreifen kann (null = er kann).</summary>
    public static string? TargetIssue(Universe u, Ship player, Ship target) => TargetIssueText(TargetBlockOf(u, player, target));

    /// <summary>Runde 20: Kurzform des Grundes für die Waffentaste — „eigenes Schiff“, „Ziel angedockt“, „kämpft mit dir“, „anderes Gefecht“; null = angreifbar.</summary>
    public static string? TargetIssueShort(Universe u, Ship player, Ship target) => TargetIssueShortText(TargetBlockOf(u, player, target));

    /// <summary>Gefechtstaktik eines NPC-Schiffs, das der Spieler angreift.</summary>
    public static string TacticOf(Ship s) => ThreatMap.IsPolice(s) ? CombatTactics.Police : ThreatMap.IsRaider(s) ? CombatTactics.Raider : CombatTactics.Trader;

    /// <summary>
    /// Sorgt dafür, dass Spieler und Ziel im selben Gefecht auf verschiedenen Seiten stehen: im laufenden Gefecht des Spielers (das Ziel tritt der Gegenseite
    /// bei), im laufenden Gefecht des Ziels (der Spieler tritt der Gegenseite bei — z. B. gegen einen Kaperer, der ein anderes Schiff überfällt) oder in einem
    /// neuen Gefecht „Angriff“. Ein Angriff auf ein Schiff, das keinem Gesetzlosen gehört, ist ein Verbrechen beim Sektorbesitzer; eine Streife der
    /// Sektorfraktion greift mit der Antwortchance der Überfälle ein. Liefert Gefecht, eigenen Teilnehmer und Ziel; null = nicht erlaubt.
    /// </summary>
    public static (Encounter? E, CombatParticipant? Me, CombatParticipant? Them) Engage(Universe u, Ship player, Ship target)
    {
        if (TargetIssue(u, player, target) is not null) return (null, null, null);
        var r = u.Rules;
        var sector = u.Sectors[player.Sector];
        bool crime = !IsOutlaw(u, target.Owner) && !Wars.AtWar(u, u.PlayerActor, target.Owner);   // Runde 40: Angriffe auf Kriegsgegner sind kein Verbrechen
        var mine = u.Fights.Of(player.Id);
        var theirs = u.Fights.Of(target.Id);
        if (mine is not null && theirs is not null && mine.Id == theirs.Id) return (mine, mine.Of(player.Id), mine.Of(target.Id));
        Encounter e;
        CombatParticipant me, them;
        if (mine is not null)
        {
            e = mine;
            me = mine.Of(player.Id)!;
            them = Combat.Engage.Join(u, e, target, 1 - me.Side, TacticOf(target), me.X + r.CombatStartDistance);
        }
        else if (theirs is not null)
        {
            e = theirs;
            them = theirs.Of(target.Id)!;
            me = e.Of(player.Id) is { } old && old.Side == 1 - them.Side ? Combat.Engage.Rejoin(u, e, old, player)   // Runde 6: alter Eintrag wird wieder aktiv
                : Combat.Engage.Join(u, e, player, 1 - them.Side, CombatTactics.Player, them.X - r.CombatStartDistance, player.Position);
        }
        else
        {
            e = Combat.Engage.Begin(u, EncounterCause.Assault, sector.Id);
            e.Victim = target.Id;
            me = Combat.Engage.Join(u, e, player, 0, CombatTactics.Player, 0f, player.Position);
            them = Combat.Engage.Join(u, e, target, 1, TacticOf(target), Vec2.Distance(player.Position, target.Position));
            if (crime)
            {
                // Phase 25 (Korrektur): im Schutzradius einer Station antwortet deren Polizei im Sektor sicher (ohne Zufallszug); sonst die Streife der
                // Sektorfraktion mit der Antwortchance der Überfälle
                var guard = GuardOf(u, sector.Id, target.Position);
                var guards = guard is { } gf ? PoliceOf(u, sector.Id, gf).Where(p => p.Id != target.Id).ToList() : new List<Ship>();
                var police = guards.Count > 0 ? guards : PirateSystem.PoliceIn(u, sector.Id).Where(p => p.Id != target.Id).ToList();
                if (police.Count > 0 && (guards.Count > 0 || u.Rng.NextDouble() < MathF.Min(r.PoliceResponseMax, r.PoliceResponseChance * police.Count)))
                {
                    e.Police = police[0].Id;
                    Combat.Engage.Join(u, e, police[0], 1, CombatTactics.Police, them.X + r.CombatHelperDistance,
                        CombatSpace.Around(target.Position, police[0].Position, r.CombatHelperDistance));
                }
            }
            u.Events.Publish(new EncounterStarted(e.Id, sector.Id, EncounterCause.Assault, player.Id, target.Id,
                crime ? $"Du greifst {Describe(u, target)} an — Fahndung bei {string.Join(", ", Wanted.AssaultAuthorities(u, sector.Id, target.Owner).Select(f => FactionName(u, f)))}."
                      : $"Du greifst {Describe(u, target)} an."));
        }
        // Phase 25 (Korrektur): Fahndung bei Opferfraktion und Sektorbesitzer (im herrenlosen Sektor bei den Nachbarn); im Stationsschutz schwerer
        if (crime) Wanted.ReportAssault(u, u.PlayerActor, sector.Id, target.Owner, GuardOf(u, sector.Id, target.Position) is not null ? "assault_station" : "assault");
        u.Events.Publish(new PlayerAttacked(e.Id, target.Id, crime));
        return (e, me, them);
    }

    /// <summary>Phase 25 (Korrektur): Fraktion der Station, in deren Schutzradius die Position liegt (nächste Station; null = keine).</summary>
    public static FactionId? GuardOf(Universe u, SectorId sector, Vec2 pos)
    {
        if (!u.Sectors.TryGetValue(sector, out var sec)) return null;
        Station? best = null; float bd = float.MaxValue;
        foreach (var id in sec.Stations.OrderBy(i => i.Value))
            if (u.Stations.TryGetValue(id, out var st) && Vec2.Distance(st.Position, pos) <= st.SafeRadius(u.Rules) && Vec2.Distance(st.Position, pos) - st.Radius < bd) { bd = Vec2.Distance(st.Position, pos) - st.Radius; best = st; }
        return best is null ? null : u.FactionOf(best);
    }

    /// <summary>Polizeischiffe einer Fraktion, die gerade frei im Sektor sind (feste Reihenfolge).</summary>
    static List<Ship> PoliceOf(Universe u, SectorId sector, FactionId faction)
    {
        var list = new List<Ship>();
        foreach (var id in u.Tracker.ParkedIn(sector).Concat(u.Tracker.TransitIn(sector)).Distinct().OrderBy(i => i.Value))
            if (u.Ships.TryGetValue(id, out var s) && ThreatMap.IsPolice(s) && u.FactionOf(s) == faction && !u.Fights.IsEngaged(s.Id) && ThreatMap.Where(u, s).Sector == sector) list.Add(s);
        return list;
    }

    static string FactionName(Universe u, FactionId f) => u.Factions.TryGetValue(f, out var fa) ? fa.Name : "niemandem";

    /// <summary>
    /// Gerätetest Phase 28 (Runde 5): Fluchtlage des aktiven Schiffs im Gefecht — dieselbe Regel wie die Simulation (<see cref="CombatSense.EscapeBlock"/>):
    /// „Flucht: Polizei in Reichweite (52 von 60 m)“; leer außerhalb eines Gefechts.
    /// </summary>
    public static string EscapeStatus(Universe u)
    {
        if (u.Fights.Of(u.PlayerShip) is not { } e || e.Of(u.PlayerShip) is not { Active: true } p) return "";
        return CombatSense.EscapeBlock(u, e, p) is { } why ? $"Flucht: {why}" : "Flucht: außer Reichweite — gleich entkommen";
    }

    /// <summary>Kurzbeschreibung eines Schiffs (Modell und Besitzer).</summary>
    public static string Describe(Universe u, Ship s) =>
        $"{(u.ShipModels.TryGetValue(s.ModelKey, out var m) ? m.Name : "Schiff")} #{s.Id.Value} ({ActorNames.Tagged(u, s.Owner)})";   // Phase 29: mit Kürzel

    /// <summary>
    /// „Nächster Gegner“: Gegner im eigenen Gefecht, danach Kaperschiffe im Sektor, beide nach Abstand; liefert das Schiff nach <paramref name="current"/>
    /// (zyklisch), default = keiner. Rein.
    /// </summary>
    public static ShipId NextEnemy(Universe u, Ship player, ShipId current)
    {
        var list = Enemies(u, player);
        if (list.Count == 0) return default;
        // Runde 16 (Handy-Test: „Nächster Gegner“ sprang vom Angreifer in 30 m zum fernen Kaperer in 380 m und die Kamera verlor das Ziel): im Gefecht wird nur
        // unter den Gefechtsgegnern gewechselt — ist der gewählte der einzige, bleibt er; ohne Gefecht reihum unter den Kaperern im Sektor
        var fight = FightEnemies(u, player);
        // Runde 17 (Handy-Test: in der Warnphase sprang die Taste zum fernen Lauerer): angekündigte Angreifer (Warnung läuft) haben Vorrang vor allen anderen
        var announced = fight.Count > 0 ? fight : Announced(u, player).Where(list.Contains).ToList();
        var pool = fight.Count > 0 ? fight : announced.Count > 0 ? announced : list;
        int i = pool.IndexOf(current);
        return i < 0 ? pool[0] : pool[(i + 1) % pool.Count];
    }

    /// <summary>Runde 17: Kaperer, deren Überfall auf den Spieler angekündigt ist (Warnzeit läuft), nach Fälligkeit.</summary>
    public static List<ShipId> Announced(Universe u, Ship player) =>
        u.PendingEncounters.Where(p => p.Target == player.Id && p.Kind == EncounterKind.Raid && p.Source.Value != 0 && p.DueTick - u.Tick <= u.Rules.PlayerWarningTicks)
            .OrderBy(p => p.DueTick).ThenBy(p => p.Source.Value).Select(p => p.Source).ToList();

    /// <summary>Gegner im eigenen Gefecht nach Abstand (leer ohne Gefecht).</summary>
    static List<ShipId> FightEnemies(Universe u, Ship player)
    {
        var fight = new List<(ShipId, float)>();
        if (u.Fights.Of(player.Id) is { } e && e.Of(player.Id) is { } me)
            foreach (var p in e.Participants)
                if (p.Active && p.Side != me.Side && PositionOf(u, p.Ship) is { } pp) fight.Add((p.Ship, Vec2.Distance(pp.Pos, player.Position)));
        return fight.OrderBy(f => f.Item2).ThenBy(f => f.Item1.Value).Select(f => f.Item1).ToList();
    }

    /// <summary>Feindliche Schiffe für die Zielwahl (feste Reihenfolge: Gefechtsgegner nach Abstand, dann Kaperer im Sektor nach Abstand).</summary>
    public static List<ShipId> Enemies(Universe u, Ship player)
    {
        var fight = new List<(ShipId, float)>();
        var e = u.Fights.Of(player.Id);
        var me = e?.Of(player.Id);
        if (e is not null && me is not null)
            foreach (var p in e.Participants)
                if (p.Active && p.Side != me.Side && PositionOf(u, p.Ship) is { } pp) fight.Add((p.Ship, Vec2.Distance(pp.Pos, player.Position)));
        // Gerätetest Phase 27: angedockte Schiffe (und andere nicht angreifbare) nicht als Gegner anbieten
        var raiders = new List<(ShipId, float)>();
        foreach (var id in u.Tracker.ParkedIn(player.Sector).Concat(u.Tracker.TransitIn(player.Sector)).Distinct().OrderBy(i => i.Value))
        {
            if (!u.Ships.TryGetValue(id, out var s) || !ThreatMap.IsRaider(s) || fight.Any(f => f.Item1 == id) || TargetIssue(u, player, s) is not null) continue;
            if (PositionOf(u, id) is { } pp && pp.Sector == player.Sector) raiders.Add((id, Vec2.Distance(pp.Pos, player.Position)));
        }
        return fight.OrderBy(f => f.Item2).ThenBy(f => f.Item1.Value).Select(f => f.Item1)
            .Concat(raiders.OrderBy(f => f.Item2).ThenBy(f => f.Item1.Value).Select(f => f.Item1)).ToList();
    }

    /// <summary>Beschießt jemand gerade das Spielerschiff? Liefert den Schützen eines Schusses auf das Schiff (Anzeige der Warnrichtung).</summary>
    public static bool IsHostile(Universe u, Ship player, ShipId other) =>
        u.Fights.Of(player.Id) is { } e && e.Of(player.Id) is { } me && e.Of(other) is { Active: true } o && o.Side != me.Side
        || (u.Ships.TryGetValue(other, out var s) && ThreatMap.IsRaider(s));

    /// <summary>
    /// Der Spieler verlässt sein Gefecht (Offline-Aufholen: niemand kann reagieren). Die Gegner gelten als entkommen gelassen; das Gefecht endet im
    /// nächsten Schritt.
    /// </summary>
    public static void Withdraw(Universe u)
    {
        if (u.Fights.Of(u.PlayerShip) is { } e && e.Of(u.PlayerShip) is { Active: true } p) Combat.Engage.Leave(u, e, p, CombatExit.Fled);
    }
}

/// <summary>Runde 21: Grund, aus dem der Spieler ein Schiff nicht angreifen kann.</summary>
public enum TargetBlock { None, Own, Docked, Ally, OtherFight }
