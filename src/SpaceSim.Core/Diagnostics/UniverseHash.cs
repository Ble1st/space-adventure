using SpaceSim.Core.Model;

namespace SpaceSim.Core.Diagnostics;

/// <summary>Stabiler Hash über den simulationsrelevanten Zustand. Für Determinismus- und Speichern/Laden-Tests.</summary>
public static class UniverseHash
{
    public static ulong Compute(Universe u) => Compute(u, includeTags: true);

    /// <summary>
    /// Runde 12: Hash wahlweise ohne die Akteurskürzel (<see cref="Actor.Tag"/>) — die Kürzel hängen von der Namensliste ab; der Vergleichswert ohne sie zeigt, dass
    /// eine geänderte Referenzkonstante nur von Namen/Kürzeln kommt und das Verhalten gleich blieb.
    /// </summary>
    public static ulong Compute(Universe u, bool includeTags) => Compute(u, includeTags, includePlayerPolitics: true);

    /// <summary>
    /// Runde 38: Hash wahlweise ohne die Spielerpolitik — Spielerfraktion (Eintrag und Forschungsstand), Mitgliedschaft des Spielerakteurs und alle
    /// Beziehungseinträge von und zu ihr. Der Vergleichswert mit Regel <see cref="EconomyRules.PlayerOwnFaction"/> an und aus muss gleich sein: Beweis, dass die
    /// KI-Welt von der Spielerfraktion nichts merkt (wie Runde 12 für die Kürzel).
    /// </summary>
    public static ulong Compute(Universe u, bool includeTags, bool includePlayerPolitics)
    {
        bool Skip(FactionId f) => !includePlayerPolitics && f.Value == u.PlayerActor.Value;
        ulong h = 14695981039346656037UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        void MixF(float f) => Mix((ulong)BitConverter.SingleToInt32Bits(MathF.Round(f, 3)));
        void MixString(string t) { Mix((ulong)t.Length); foreach (var c in t) Mix(c); }

        Mix((ulong)u.Tick); Mix(u.Rng.State);
        foreach (var st in u.Stations.Values.OrderBy(s => s.Id.Value))
        {
            Mix((ulong)st.Id.Value); MixF(st.Credits); Mix((ulong)st.Owner.Value);   // Phase 9: Besitzer (Akteur)
            if (st.CalmSince >= 0) Mix((ulong)st.CalmSince);   // Phase 30
            if (st.Integrity != 1f) { Mix(0x0801UL); MixF(st.Integrity); }   // Ausbau Punkt 8: Zustand (nur wenn ≠ 1)
            if (st.Rotation != 0f) { Mix(0x2074UL); MixF(st.Rotation); }   // eigene Stationen: Drehung (nur wenn gesetzt)
            if (st.CapitulationLockUntil != 0) { Mix(0x2076UL); Mix((ulong)st.CapitulationLockUntil); }   // Ausgleich: Kapitulationssperre
            if (st.Unpaid) Mix(0x2077UL);   // Ausgleich: Regierungsunterhalt unbezahlt
            foreach (var (g, e) in st.Market.OrderBy(k => k.Key.Value))
            {
                Mix((ulong)g.Value); MixF(e.Amount); MixF(e.Price);
                if (e.LastMove is { } lm) { Mix(0x1D1EUL); Mix((ulong)lm); }   // Ausbau Punkt 12: letzte Regal-Bewegung (nur wenn gesetzt)
                if (e.SeenAmount is { } sa) { Mix(0x1D1FUL); MixF(sa); }       // Ausbau Punkt 12: zuletzt beobachteter Regalbestand (nur wenn gesetzt)
            }
            // Phase 8: Schutz, Baustelle (Typ, Stückliste, Bauschiff, Zeiten, Zustand) und Spielerlager gehören zum Spielstand
            Mix(st.BankruptcyProtected ? 1UL : 0UL); MixString(st.TypeKey);
            if (st.Site is { } bs)
            {
                MixString(bs.TypeKey); Mix((ulong)bs.Builder.Value); Mix((ulong)bs.LastProgressTick); Mix((ulong)bs.BlockedSince);
                Mix((ulong)bs.AssemblyTicks); Mix((ulong)bs.InvestorActor); Mix(bs.Emergency ? 1UL : 0UL); Mix(bs.Abandoned ? 1UL : 0UL); Mix(bs.Panic ? 1UL : 0UL); MixF(bs.Budget);
                foreach (var (g, n) in bs.Required.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(n); }
            }
            foreach (var (g, n) in st.PlayerStorage.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(n); }
            if (st.BlackStock.Count > 0) { Mix(0x5C4DUL); foreach (var (g, n) in st.BlackStock.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(n); } }   // Schwarzmarkt
            // Phase 14: wartende Exportchargen (Reihenfolge des Ankaufs)
            foreach (var b in st.Exports) { Mix((ulong)b.Good.Value); MixF(b.Amount); MixF(b.Paid); Mix((ulong)b.DueTick); }
            foreach (var (g, n) in st.MaintenanceBacklog.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(n); }   // Phase 15
            Mix((ulong)st.BuiltTick); MixF(st.Activity); MixF(st.Starved); Mix(st.Panic ? 1UL : 0UL);   // Phase 19: Auslastung
            MixF(st.Profit.Rate); MixF(st.Profit.Worth); Mix((ulong)st.Profit.LossTicks); Mix((ulong)st.Profit.Since);   // Phase 22: Rentabilität
            if (st.Condition is { } sc)   // Phase 28: Kampfschaden (unbeschädigte Stationen mischen nichts)
            {
                Mix(0x2800u); MixF(sc.HullMissing); MixF(sc.ShieldMissing); Mix((ulong)sc.ShieldPauseUntil); Mix(sc.Disabled ? 1UL : 0UL);
                foreach (var t in sc.Timers) MixF(t);
                // Wirtschaftsstationen halten stand: Alarm/Notschild nur, wenn gesetzt (sonst unverändert)
                if (sc.AlarmUntil != 0 || sc.EmergencyUntil != 0 || sc.EmergencyCooldownUntil != 0)
                { Mix(0x2810u); Mix((ulong)sc.AlarmUntil); Mix((ulong)sc.EmergencyUntil); Mix((ulong)sc.EmergencyCooldownUntil); }
            }
        }
        foreach (var f in u.Factions.Values.OrderBy(f => f.Id.Value))
        {
            if (Skip(f.Id)) continue;
            Mix((ulong)f.LastEmergencyBuildTick);
            // Phase 13: Forschungsstand (laufendes Projekt, FP je Projekt, Abschlüsse, Summen)
            var r = f.Research;
            MixString(r.Current ?? ""); Mix(r.Commissioned ? 1UL : 0UL); MixF(r.PointsTotal); MixF(r.Spent); MixF(r.PlayerFunded);
            foreach (var (k, v) in r.Progress.OrderBy(kv => kv.Key, StringComparer.Ordinal)) { MixString(k); MixF(v); }
            foreach (var (k, t) in r.Completed.OrderBy(kv => kv.Key, StringComparer.Ordinal)) { MixString(k); Mix((ulong)t); }
        }
        foreach (var s in u.Ships.Values.OrderBy(s => s.Id.Value))
        {
            Mix((ulong)s.Id.Value); Mix((ulong)s.Owner.Value); Mix((ulong)s.Sector.Value); MixF(s.Position.X); MixF(s.Position.Y); MixF(s.Credits);
            Mix(s.Transit is null ? 0UL : (ulong)s.Transit.ArrivalTick); Mix((ulong)s.Phase); Mix((ulong)s.MinePhase); MixF(s.ShotTimer); Mix((ulong)s.MineAsteroid); Mix((ulong)s.Capabilities); Mix((ulong)s.Controller);
            if (s.AnchorRock != 0) { Mix(0xA2C4UL); Mix((ulong)s.AnchorRock); MixF(s.AnchorProgress); }   // Phase 32: Verankerung (nur wenn gesetzt)
            if (s.JumpTarget.Value != 0) { Mix(0x5A01UL); Mix((ulong)s.JumpTarget.Value); MixF(s.JumpCharge); MixF(s.JumpFrom.X); MixF(s.JumpFrom.Y); }   // Sprungantrieb lädt (nur wenn gesetzt)
            if (s.JumpCooldownUntil != 0) { Mix(0x5A02UL); Mix((ulong)s.JumpCooldownUntil); }   // Sprungantrieb kühlt ab
            if (s.Cloaked) Mix(0x5A03UL);   // Tarnfeld an
            if (s.FlaresMissing != 0f) { Mix(0x6E01UL); MixF(s.FlaresMissing); }   // Raketenlenkung: Täuschkörpermagazin
            if (s.FlaresUntil != 0) { Mix(0x6E02UL); Mix((ulong)s.FlaresUntil); }
            if (s.JammerOn) Mix(0x6E03UL);   // Störsender an
            MixString(s.ModelKey);                                   // Phase 3: Modell und Fit gehören zum Spielstand
            foreach (var f in s.Fit) { if (f is null) Mix(0UL); else MixString(f); }
            foreach (var (g, a) in s.Cargo.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(a); }
            if (s.MissionCargo > 0f) { Mix(0x4101u); MixF(s.MissionCargo); }
            if (s.DerelictUntil != 0) { Mix(0xDE1Eu); Mix((ulong)s.DerelictUntil); }   // herrenloses Schiff verfällt (nur wenn gesetzt)
            if (s.DockedSince >= 0) { Mix(0xD0C5u); Mix((ulong)s.DockedSince); }   // Liegezeit (nur wenn gesetzt)   // Runde 41: versiegelte Fracht (nur wenn gesetzt)
            foreach (var (g, a) in s.Fuel.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(a); }   // Phase 4: Tank gehört zum Spielstand
            MixF(s.Profit.Rate); MixF(s.Profit.Worth); Mix((ulong)s.Profit.LossTicks); Mix((ulong)s.Profit.Since);   // Phase 22: Rentabilität
            Mix((ulong)s.Role); Mix((ulong)s.JobSince); Mix((ulong)s.StrandedSince);   // Phase 19: unverkäufliche Fracht seit   // Phase 6: Aufgabe (Etikett) und Beginn der Aufgabe wechseln bei Multirole-Schiffen
            Mix((ulong)s.Quality);                          // Phase 7: Güteklasse, Slotliste (Bonusslots, vergrößerte Slots) und Mitleidszähler
            foreach (var sl in s.Slots) { MixString(sl.Type); Mix((ulong)sl.Size); }
            foreach (var (k, v) in s.UpgradePity.OrderBy(k => k.Key)) { Mix((ulong)(long)k); Mix((ulong)v); }
            // Phase 23: Bordsysteme (Fehlbeträge, Zustand je Slot, Leistungsverteilung, Schildpause)
            var sys = s.Systems;
            MixF(sys.ShieldMissing); MixF(sys.ChargeMissing); MixF(sys.AmmoMissing); Mix((ulong)sys.Mode); Mix((ulong)sys.ShieldPauseUntil);
            foreach (var c in sys.Condition) MixF(c);
            foreach (var b in sys.Burning) Mix((ulong)(b + 1000));   // Phase 26: Brand (Slots); unbeschädigte Schiffe mischen nichts
            if (sys.MissilesMissing != 0f) { Mix(0x2801u); MixF(sys.MissilesMissing); }   // Phase 28: Raketenmagazin
            if (sys.Heat != 0f || sys.HeatLocked) { Mix(0x4E47u); MixF(sys.Heat); Mix(sys.HeatLocked ? 1UL : 0UL); }   // Hitze: nur wenn gesetzt
        }
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value))
            foreach (var a in sec.Asteroids.OrderBy(a => a.Id)) { Mix((ulong)a.Id); MixF(a.Ore); MixF(a.Position.X); MixF(a.Position.Y); MixF(a.BaseRadius); }
        Mix((ulong)u.NextAsteroidId);   // Phase 8: Fragment-Ids sind fortlaufend
        // Phase 19: Stilllegung (Abklingzeit, anhaltende Knappheit)
        Mix((ulong)u.LastStationRetireTick);
        foreach (var (g, t) in u.ScarceSince.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); Mix((ulong)t); }
        // Phase 20: Versorgungspanik und Kaufsperre nach Ausmusterung
        foreach (var (k, t) in u.PanicSince.OrderBy(k => k.Key.Part).ThenBy(k => k.Key.Good.Value)) { Mix((ulong)k.Part); Mix((ulong)k.Good.Value); Mix((ulong)t); }
        foreach (var (k, t) in u.IdleRetiredTick.OrderBy(k => k.Key.Job, StringComparer.Ordinal).ThenBy(k => k.Key.Part)) { MixString(k.Job); Mix((ulong)k.Part); Mix((ulong)t); }
        // Phase 9: Akteure (Art, Mitgliedschaft, Kasse — darunter die Spielerkasse aus Phase 5b —, Persönlichkeit, Beziehungsabweichungen)
        foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value))
        {
            Mix((ulong)a.Id.Value); Mix((ulong)a.Kind);
            if (includePlayerPolitics || a.Id != u.PlayerActor) Mix((ulong)(a.Membership?.Value ?? -1)); MixF(a.Treasury.Credits); Mix(a.ShipsUseTreasury ? 1UL : 0UL);
            MixF(a.Personality.Aggression); MixF(a.Personality.Greed); MixF(a.Personality.Cooperation); MixF(a.Personality.Lawfulness);
            foreach (var (k, v) in a.Relations.OrderBy(k => k.Key.Value)) { Mix((ulong)k.Value); MixF(v); }
        }
        foreach (var f in u.Factions.Values.OrderBy(f => f.Id.Value))
        {
            if (Skip(f.Id)) continue;
            foreach (var (k, v) in f.Relations.OrderBy(k => k.Key.Value)) { if (Skip(k)) continue; Mix((ulong)k.Value); MixF(v); }
        }
        Mix((ulong)u.PlayerActor.Value); Mix((ulong)u.FreeActor.Value); Mix((ulong)u.NextActorId);
        Mix((ulong)u.PlayerShip.Value);   // Phase 5c: aktives Schiff kann wechseln (Kauf, Hangar)
        // Zu-Fuß-Station der Person: nur gemischt, wenn gesetzt — ohne den Zustand ältere Referenz-Hashes bitgleich (an Bord ist der Normalfall; das Feld
        // wirkt über Liegegebühr/Flottenunterhalt des geparkten aktiven Schiffs in die Wirtschaft und muss in den Hash).
        if (u.PlayerAtStation.Value != 0) { Mix(0x50415453UL); Mix((ulong)u.PlayerAtStation.Value); }
        // Phase 10: Gate-Zustände, Sektorbesitz, Sperren (Stufen, Zeiten, Durchsetzung), Einfluss, Kontrollstufe, umkämpft, unsterbliche Stationen
        foreach (var g in u.Gates.Values.OrderBy(g => g.Id.Value))
        {
            Mix((ulong)g.Id.Value); Mix((ulong)g.State); Mix(g.MainRoute ? 1UL : 0UL);
            if (g.Condition is { } gc)   // Phase 31: Torkampf — nur wenn gesetzt (Referenz-Hashes bleiben)
            {
                Mix(0x3101u); MixF(gc.HullMissing); MixF(gc.ShieldMissing); Mix((ulong)gc.ShieldPauseUntil); foreach (var t in gc.Timers) MixF(t);
                Mix(gc.Disabled ? 1UL : 0UL); Mix((ulong)gc.DownSince); Mix((ulong)gc.LastAttacker.Value);
                if (gc.GraceUntil != 0) Mix((ulong)gc.GraceUntil + 0x3106u);   // Runde 14b
                if (gc.PrevState is { } ps) Mix((ulong)ps + 0x3107u);
            }
        }
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value))
        {
            var a = sec.Access;
            Mix((ulong)sec.Id.Value); Mix((ulong)sec.Owner.Value); Mix((ulong)a.EntryLevel); Mix((ulong)a.ExitLevel); Mix((ulong)(a.Against?.Value ?? -1));
            Mix((ulong)a.SinceTick); Mix((ulong)a.ExpiresTick); Mix((ulong)a.CooldownUntil); Mix(a.Enforced ? 1UL : 0UL);
            Mix((ulong)sec.Control); Mix(sec.Contested ? 1UL : 0UL);
            foreach (var (f, v) in sec.Influence.OrderBy(k => k.Key.Value)) { Mix((ulong)f.Value); MixF(v); }
        }
        foreach (var st in u.Stations.Values.Where(s => s.Immortal).OrderBy(s => s.Id.Value)) Mix((ulong)st.Id.Value);
        // Phase 11: Lagekarte, Fahndung, Kopfgeld, Streitkräfte-Takt, Heimatbasis/Einsatz, angekündigte Begegnungen
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value)) { MixF(sec.Threat); MixF(sec.Security); MixF(sec.Traffic); }
        foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value))
        {
            Mix((ulong)a.LastForceTick); MixF(a.PoliceNeed); Mix((ulong)a.PoliceSurplusSince);   // Phase 20
            if (includeTags) MixString(a.Tag);   // Phase 29: Kürzel (Runde 12: abschaltbar für den Vergleichswert)
            Mix((ulong)a.LastRestoreTick);   // Phase 22
            if (a.LastOutpostTick != 0) Mix((ulong)a.LastOutpostTick);   // Phase 30
            if (a.LastFoundTick != 0) Mix((ulong)a.LastFoundTick + 0x9001u);   // Ausbau Punkt 9: letzte Gründung (Sitz/Büro)
            if (a.LastSmugglerTick != 0) Mix((ulong)a.LastSmugglerTick + 0x5001u);   // Ausbau Punkt 5: letzter Schmugglerkauf
            if (a.LastSmugglerRetireTick != 0) Mix((ulong)a.LastSmugglerRetireTick + 0x5002u);   // Ausbau Punkt 5: letzter Schmugglerverkauf
            if (a.LastBanditTick != 0) Mix((ulong)a.LastBanditTick + 0x1701u);   // Ausbau Punkt 17: letzter Banditenbau
            if (a.SeatArrearsSince >= 0) Mix((ulong)a.SeatArrearsSince + 0x9002u);   // Ausbau Punkt 9: Unterhaltsrückstand des Firmensitzes
            Mix((ulong)a.CreatedTick);       // Phase 27 (Prüfung Phase 26): Gründungszeit (Aufbauzeit der Rücklage hängt daran)
            if (a.ArrestCooldownUntil != 0) Mix((ulong)a.ArrestCooldownUntil + 0x2802u);   // Phase 28: Festnahme-Sperre
            foreach (var (f, w) in a.Wanted.OrderBy(k => k.Key.Value)) { Mix((ulong)f.Value); MixF(w.Heat); MixF(w.Fine); Mix((ulong)w.LastCrimeTick); }
            foreach (var (f, c) in a.BountyClaims.OrderBy(k => k.Key.Value)) { Mix((ulong)f.Value); MixF(c); }
            if (includePlayerPolitics)   // Spielersperre: Ansehensschuld des Spielers (Spielerpolitik; nur wenn vorhanden)
                foreach (var (f, c) in a.CrimeStanding.OrderBy(k => k.Key.Value))
                { Mix((ulong)f.Value + 0x5e11u); MixF(c.Debt); MixF(c.Base); Mix((ulong)c.WindowStart); MixF(c.WindowLoss); }
            if (includePlayerPolitics && a.Intel is { } intel) Persistence.IntelCodec.Mix(intel, Mix);   // Ausbau Punkt 16: Spielerwissen (nur wenn beschränkt)
            if (includePlayerPolitics && a.Stats is { } pst) Persistence.PlayerStatsCodec.Mix(pst, Mix);   // Spielerstatistik (nur wenn gezählt)
            if (includePlayerPolitics && a.Route is { } route) Persistence.RouteCodec.Mix(route, Mix);   // Mehrpunkt-Route des Spielers (nur wenn vorhanden)
        }
        foreach (var sh in u.Ships.Values.OrderBy(x => x.Id.Value))
        { Mix((ulong)sh.Id.Value); Mix((ulong)sh.HomeBase.Value); Mix((ulong)sh.DutySector.Value); MixF(sh.Hull); }   // Hülle: Überfälle, Rückzug
        // Phase 26: Wracks und Frachtbehälter (ohne Wrack mischt nichts — Welten ohne Phase 26 hashen wie vorher)
        if (u.NextWreckId != 1 || u.Wrecks.Count > 0)
        {
            Mix((ulong)u.NextWreckId);
            foreach (var w in u.Wrecks)
            {
                Mix((ulong)w.Id); Mix((ulong)w.Kind); Mix((ulong)w.Sector.Value); MixF(w.Position.X); MixF(w.Position.Y); Mix((ulong)w.ExpiresTick);
                foreach (var (g, a) in w.Cargo.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(a); }
                foreach (var m in w.Modules) { MixString(m.Key); MixF(m.Condition); }
            }
        }
        Persistence.UniverseEventCodec.Mix(u, Mix, MixF, MixString);   // Universumsereignisse (nur wenn je eines lief)
        // Phase 24: laufende Gefechte
        Mix((ulong)u.Fights.NextId);
        foreach (var e in u.Fights.Encounters)
        {
            Mix((ulong)e.Id); Mix((ulong)e.Sector.Value); Mix((ulong)e.StartTick); Mix((ulong)e.Cause); Mix((ulong)e.Raider.Value); Mix((ulong)e.RaiderOwner.Value);
            Mix((ulong)e.Victim.Value); Mix((ulong)e.Police.Value); MixF(e.Loot); MixF(e.Fenced); Mix((ulong)e.Shots); Mix((ulong)e.Hits);
            foreach (var p in e.Participants)
            {
                Mix((ulong)p.Ship.Value); Mix((ulong)p.Side); MixString(p.Tactic); MixF(p.X); Mix((ulong)p.Target.Value); Mix((ulong)p.Move); MixF(p.Speed);
                foreach (var t in p.Timers) MixF(t);
                MixF(p.Courage); MixF(p.DropShare); Mix((ulong)p.PrevMode); Mix((ulong)p.Exit);
                // Phase 25: nur, wo es gilt (räumliches Gefecht, Angriff des Spielers, gemerkter Liegeplatz) — reine NPC-Linien-Gefechte hashen wie in Phase 24
                if (e.Spatial) { MixF(p.Pos.X); MixF(p.Pos.Y); MixF(p.Dir.X); MixF(p.Dir.Y); }
                if (e.Spatial || e.Cause == EncounterCause.Assault) Mix((ulong)p.Owner.Value);
                if (p.WasDockedAt.Value != 0) Mix((ulong)p.WasDockedAt.Value);
                if (p.RefugeId != 0) { Mix((ulong)(uint)p.RefugeId + 0x2700u); MixF(p.Refuge); MixF(p.RefugePos.X); MixF(p.RefugePos.Y); }   // Phase 27: Zuflucht
                if (p.LastShotTick != 0) Mix((ulong)p.LastShotTick + 0x2806u);   // Runde 6: letzter Schuss des Spielers
                if (p.CastOffUntil != 0) Mix((ulong)p.CastOffUntil + 0x3201u);   // Phase 32: Trossen kappen
            }
            if (e.Spatial) Mix(0x5A7u);
            if (e.Station.Value != 0) { Mix(0x2803u); Mix((ulong)e.Station.Value); MixF(e.StationX); }   // Phase 28: Belagerung
            if (e.Gate.Value != 0) { Mix(0x3102u); Mix((ulong)e.Gate.Value); MixF(e.StationX); }   // Phase 31: Torbelagerung
            foreach (var m in e.Missiles)
            {
                Mix(0x2804u); Mix((ulong)m.Shooter.Value); Mix((ulong)m.Owner.Value); Mix((ulong)m.Side); MixString(m.Module); Mix((ulong)m.Target.Value); Mix((ulong)m.Station.Value);
                if (m.Gate.Value != 0) Mix((ulong)m.Gate.Value + 0x3103u);
                MixF(m.Left); MixF(m.Total); MixF(m.Speed);
                if (e.Spatial) { MixF(m.From.X); MixF(m.From.Y); }
            }
        }
        // Phase 28: Kriege und Friedensschlüsse (ohne Krieg mischt nichts). Runde 40: Waffenstillstand/Angebots-Tick/Tribut nur, wenn gesetzt.
        foreach (var w in u.Wars) { Mix(0x2805u); Mix((ulong)w.A.Value); Mix((ulong)w.B.Value); Mix((ulong)w.Since); MixF(w.Weariness); MixF(w.LossA); MixF(w.LossB); Mix((ulong)w.Sieges); if (w.Blockades != 0) Mix((ulong)w.Blockades + 0x3104u); if (w.CeasefireUntil != 0) Mix((ulong)w.CeasefireUntil + 0x4001u); if (w.LastOfferTick != 0) Mix((ulong)w.LastOfferTick + 0x4002u); if (w.TributePaid != 0f) MixF(w.TributePaid + 4000f); if (w.Join) Mix(0x4503u); if (w.ShipsLostA != 0 || w.ShipsLostB != 0) { Mix(0x7701u); Mix((ulong)w.ShipsLostA); Mix((ulong)w.ShipsLostB); } if (w.MediationBonus != 0f) MixF(w.MediationBonus + 6000f); if (w.LastMediationTick != 0) Mix((ulong)w.LastMediationTick + 0x6001u); }
        foreach (var (k, t) in u.PeaceSince.OrderBy(k => k.Key.A).ThenBy(k => k.Key.B)) { Mix(0x2806u); Mix((ulong)k.A); Mix((ulong)k.B); Mix((ulong)t); }
        // Nachtrag 2b: Ende der letzten Seeschlacht je Sektor und Paar (ohne Seeschlacht mischt nichts)
        foreach (var (k, t) in u.NavalEnded.OrderBy(k => k.Key.Sector).ThenBy(k => k.Key.A).ThenBy(k => k.Key.B)) { Mix(0x2B01u); Mix((ulong)k.Sector); Mix((ulong)k.A); Mix((ulong)k.B); Mix((ulong)t); }
        // Runde 45: Bündnisse und Bündnisenden (ohne Bündnis mischt nichts; ohne Spielerpolitik ohne die Paare mit der Spielerfraktion)
        foreach (var al in u.Alliances) { if (Skip(al.A) || Skip(al.B)) continue; Mix(0x4501u); Mix((ulong)al.A.Value); Mix((ulong)al.B.Value); Mix((ulong)al.Since); }
        foreach (var (k, t) in u.AllianceEnded.OrderBy(k => k.Key.A).ThenBy(k => k.Key.B)) { if (Skip(new FactionId(k.A)) || Skip(new FactionId(k.B))) continue; Mix(0x4502u); Mix((ulong)k.A); Mix((ulong)k.B); Mix((ulong)t); }
        // Ausbau Punkt 6: Fehden, Fehdenenden, Regierungskrisen (ohne mischt nichts; ohne Spielerpolitik ohne die Paare mit der Spielerfraktion)
        foreach (var fd in u.Feuds) { if (Skip(fd.A) || Skip(fd.B)) continue; Mix(0x6002u); Mix((ulong)fd.A.Value); Mix((ulong)fd.B.Value); Mix((ulong)fd.Since); Mix((ulong)fd.Until); Mix(fd.ByPlayer ? 1u : 0u); Mix((ulong)fd.LastRaidTick); Mix((ulong)fd.Raids); }
        foreach (var (k, t) in u.FeudEnded.OrderBy(k => k.Key.A).ThenBy(k => k.Key.B)) { if (Skip(new FactionId(k.A)) || Skip(new FactionId(k.B))) continue; Mix(0x6003u); Mix((ulong)k.A); Mix((ulong)k.B); Mix((ulong)t); }
        foreach (var (f, t) in u.Crises.OrderBy(k => k.Key)) { Mix(0x6004u); Mix((ulong)f); Mix((ulong)t); }
        // Ausbau Punkt 3: Schutzverträge (Spielerpolitik), Konvois (ohne Konvoi mischt nichts)
        Persistence.BlueprintMercCodec.Mix(u, includePlayerPolitics, Mix, MixF, MixString);   // Baupläne (Spielerpolitik), Söldner — nur wenn vorhanden
        if (includePlayerPolitics) foreach (var c in u.Protections) { Mix(0x0301u); Mix((ulong)c.Faction.Value); Mix((ulong)c.Since); Mix((ulong)c.PaidUntil); }
        foreach (var c in u.Convoys)
        {
            Mix(0x0302u); Mix((ulong)c.Id); Mix((ulong)c.Faction.Value); Mix((ulong)c.From.Value); Mix((ulong)c.To.Value); Mix((ulong)c.Formed); Mix((ulong)c.Departed);
            foreach (var t in c.Traders) Mix((ulong)t.Value);
            Mix(0x0303u);
            foreach (var e in c.Escorts) Mix((ulong)e.Value);
        }
        if (u.NextConvoyId != 1) Mix((ulong)u.NextConvoyId + 0x0304u);
        // Runde 30: Rückkauf-Posten (ohne Spielerhandel leer — mischt nichts)
        foreach (var (k, l) in u.Ledger.OrderBy(kv => kv.Key.Actor).ThenBy(kv => kv.Key.Station).ThenBy(kv => kv.Key.Good))
        { Mix(0x3001u); Mix((ulong)k.Actor); Mix((ulong)k.Station); Mix((ulong)k.Good); MixF(l.BoughtQty); MixF(l.BoughtPrice); MixF(l.BoughtStockBefore); MixF(l.SoldQty); MixF(l.SoldPrice); MixF(l.SoldStockBefore); Mix(l.BoughtOpen ? 1u : 0u); Mix(l.SoldOpen ? 1u : 0u); Mix((ulong)l.LastTick);
          if (l.MissingQty > 0f) { Mix(0x3601u); MixF(l.MissingQty); MixF(l.MissingSeen); } }   // Runde 36: nur wenn gesetzt
        foreach (var e in u.PendingEncounters) { Mix((ulong)e.Kind); Mix((ulong)e.Source.Value); Mix((ulong)e.Target.Value); Mix((ulong)e.Sector.Value); Mix((ulong)e.DueTick); if (e.Station.Value != 0) Mix((ulong)e.Station.Value); if (e.Stalled) Mix(0x4A11UL); }
        // Phase 12: Befehle (Art, Parameter, Fortschritt, Notfall), Verhaltensregel, Verband, Liegeplatz, Leihschiff; Verbände
        foreach (var sh in u.Ships.Values.OrderBy(x => x.Id.Value))
        {
            Mix((ulong)sh.Id.Value); Mix((ulong)sh.DockedAt.Value); Mix((ulong)sh.Squadron.Value); Mix((ulong)sh.Engagement);
            Mix((ulong)sh.RentedFrom.Value); Mix((ulong)sh.RentedSince); MixF(sh.RentalDeposit);
            if (sh.RentPaidDays != 0) Mix((ulong)sh.RentPaidDays + 0x2600u);   // Phase 26: laufende Leihmiete
            if (sh.Command is { } c)
            {
                MixString(c.Kind); Mix((ulong)sh.CommandSince); Mix((ulong)(uint)sh.CommandStep); Mix(sh.Emergency ? 1UL : 0UL);
                switch (c)
                {
                    case GoToCommand g: Mix((ulong)g.Sector.Value); Mix((ulong)g.Station.Value); MixF(g.Position.X); MixF(g.Position.Y); break;
                    case PatrolCommand p: foreach (var x in p.Sectors) Mix((ulong)x.Value); break;
                    case GuardCommand gd: Mix((ulong)gd.Station.Value); break;
                    case EscortCommand ec: Mix((ulong)ec.Target.Value); break;
                    case FollowCommand fc: Mix((ulong)fc.Target.Value); break;
                    case TradeCommand tc: Mix((ulong)tc.Limits.Center.Value); Mix((ulong)(uint)tc.Limits.RadiusJumps); MixF(tc.Limits.MaxBuyFactor); MixF(tc.Limits.Reserve); if (tc.Limits.AllowLoss) Mix(0xA110u); break;
                    case MineCommand mc: Mix((ulong)mc.Limits.Center.Value); Mix((ulong)(uint)mc.Limits.RadiusJumps); MixF(mc.Limits.MaxBuyFactor); MixF(mc.Limits.Reserve); if (mc.Limits.AllowLoss) Mix(0xA110u); break;
                    case BuildCommand bc: Mix((ulong)bc.Site.Value); break;
                    case LurkCommand lc: Mix((ulong)lc.Sector.Value); MixF(lc.Spot.X); MixF(lc.Spot.Y); Mix((ulong)lc.Until); break;   // Phase 27
                    case PursueCommand pc: Mix((ulong)pc.Target.Value); MixF(pc.Lead); Mix((ulong)pc.Until); break;
                    case AssaultCommand ac: Mix((ulong)ac.Sector.Value); Mix((ulong)ac.Station.Value); if (ac.Gate.Value != 0) Mix((ulong)ac.Gate.Value + 0x3105u); break;   // Phase 28
                    default: FleetOpsHash.Mix(c, Mix, MixF); break;   // Flottenbefehle (nur Spielerschiffe tragen sie)
                }
            }
        }
        Mix((ulong)u.NextSquadronId);
        // Runde 41: angenommene Aufträge und Budget-/Sperrbuchaltung (ohne Aufträge mischt nichts — Angebote werden NICHT gehasht)
        if (u.Missions.Count > 0)
        {
            Mix((ulong)u.Missions.Count + 0x4100u);
            foreach (var m in u.Missions.OrderBy(m => m.Id))
            {
                Mix((ulong)m.Id); MixString(m.Type); Mix((ulong)m.Client.Value); Mix((ulong)m.At.Value);
                foreach (var (k, v) in m.Args.OrderBy(kv => kv.Key, StringComparer.Ordinal)) { MixString(k); MixString(v); }
                MixF(m.Reserved); MixF(m.Deposit); MixF(m.ReservedCash); MixF(m.CargoVolume); MixF(m.Sold); MixF(m.Bought);
                Mix((ulong)m.Deadline); Mix((ulong)m.AcceptedTick); Mix((ulong)m.State); Mix(m.Haggled ? 1u : 0u);
            }
        }
        if (u.MissionFactions.Count > 0)
            foreach (var (k, v) in u.MissionFactions.OrderBy(kv => kv.Key))
            { Mix(0x4102u); Mix((ulong)k); Mix((ulong)v.Day); MixF(v.Spent); Mix((ulong)v.BlockedUntil); }
        // Phase 14: Planeten der Sektoren
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value)) MixString(sec.Planet?.Name ?? "");
        // Runde 44: Maßstab nur, wenn ≠ 1 (Welten im Grundmaßstab hashen wie zuvor)
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value)) if (sec.Scale != 1f) { Mix(0x5CA1Eu); Mix((ulong)sec.Id.Value); MixF(sec.Scale); }
        // Gasernte: Nebel und Gasfelder nur, wenn gesetzt (Welten ohne Gas hashen wie zuvor)
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value))
        {
            if (sec.Nebula) { Mix(0x6E6Bu); Mix((ulong)sec.Id.Value); }
            foreach (var f in sec.GasFields) { Mix(0x6A5Fu); Mix((ulong)sec.Id.Value); MixF(f.Position.X); MixF(f.Position.Y); MixF(f.Radius); MixF(f.Density); }
        }
        foreach (var q in u.Squadrons.Values.OrderBy(q => q.Id.Value))
        {
            Mix((ulong)q.Id.Value); Mix((ulong)q.Owner.Value); Mix((ulong)q.Leader.Value); Mix((ulong)q.Formation); Mix((ulong)q.HomeBase.Value); Mix((ulong)q.Rules);
            foreach (var m in q.Members) Mix((ulong)m.Value);
        }
        return h;
    }
}
