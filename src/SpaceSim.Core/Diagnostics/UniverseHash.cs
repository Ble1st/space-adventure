using SpaceSim.Core.Model;

namespace SpaceSim.Core.Diagnostics;

/// <summary>Stabiler Hash über den simulationsrelevanten Zustand. Für Determinismus- und Speichern/Laden-Tests.</summary>
public static class UniverseHash
{
    public static ulong Compute(Universe u)
    {
        ulong h = 14695981039346656037UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        void MixF(float f) => Mix((ulong)BitConverter.SingleToInt32Bits(MathF.Round(f, 3)));
        void MixString(string t) { Mix((ulong)t.Length); foreach (var c in t) Mix(c); }

        Mix((ulong)u.Tick); Mix(u.Rng.State);
        foreach (var st in u.Stations.Values.OrderBy(s => s.Id.Value))
        {
            Mix((ulong)st.Id.Value); MixF(st.Credits); Mix((ulong)st.Owner.Value);   // Phase 9: Besitzer (Akteur)
            foreach (var (g, e) in st.Market.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(e.Amount); MixF(e.Price); }
            // Phase 8: Schutz, Baustelle (Typ, Stückliste, Bauschiff, Zeiten, Zustand) und Spielerlager gehören zum Spielstand
            Mix(st.BankruptcyProtected ? 1UL : 0UL); MixString(st.TypeKey);
            if (st.Site is { } bs)
            {
                MixString(bs.TypeKey); Mix((ulong)bs.Builder.Value); Mix((ulong)bs.LastProgressTick); Mix((ulong)bs.BlockedSince);
                Mix((ulong)bs.AssemblyTicks); Mix((ulong)bs.InvestorActor); Mix(bs.Emergency ? 1UL : 0UL); Mix(bs.Abandoned ? 1UL : 0UL); Mix(bs.Panic ? 1UL : 0UL); MixF(bs.Budget);
                foreach (var (g, n) in bs.Required.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(n); }
            }
            foreach (var (g, n) in st.PlayerStorage.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(n); }
            // Phase 14: wartende Exportchargen (Reihenfolge des Ankaufs)
            foreach (var b in st.Exports) { Mix((ulong)b.Good.Value); MixF(b.Amount); MixF(b.Paid); Mix((ulong)b.DueTick); }
            foreach (var (g, n) in st.MaintenanceBacklog.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(n); }   // Phase 15
            Mix((ulong)st.BuiltTick); MixF(st.Activity); MixF(st.Starved); Mix(st.Panic ? 1UL : 0UL);   // Phase 19: Auslastung
            MixF(st.Profit.Rate); MixF(st.Profit.Worth); Mix((ulong)st.Profit.LossTicks); Mix((ulong)st.Profit.Since);   // Phase 22: Rentabilität
            if (st.Condition is { } sc)   // Phase 28: Kampfschaden (unbeschädigte Stationen mischen nichts)
            {
                Mix(0x2800u); MixF(sc.HullMissing); MixF(sc.ShieldMissing); Mix((ulong)sc.ShieldPauseUntil); Mix(sc.Disabled ? 1UL : 0UL);
                foreach (var t in sc.Timers) MixF(t);
            }
        }
        foreach (var f in u.Factions.Values.OrderBy(f => f.Id.Value))
        {
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
            MixString(s.ModelKey);                                   // Phase 3: Modell und Fit gehören zum Spielstand
            foreach (var f in s.Fit) { if (f is null) Mix(0UL); else MixString(f); }
            foreach (var (g, a) in s.Cargo.OrderBy(k => k.Key.Value)) { Mix((ulong)g.Value); MixF(a); }
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
            Mix((ulong)a.Id.Value); Mix((ulong)a.Kind); Mix((ulong)(a.Membership?.Value ?? -1)); MixF(a.Treasury.Credits); Mix(a.ShipsUseTreasury ? 1UL : 0UL);
            MixF(a.Personality.Aggression); MixF(a.Personality.Greed); MixF(a.Personality.Cooperation); MixF(a.Personality.Lawfulness);
            foreach (var (k, v) in a.Relations.OrderBy(k => k.Key.Value)) { Mix((ulong)k.Value); MixF(v); }
        }
        foreach (var f in u.Factions.Values.OrderBy(f => f.Id.Value))
            foreach (var (k, v) in f.Relations.OrderBy(k => k.Key.Value)) { Mix((ulong)k.Value); MixF(v); }
        Mix((ulong)u.PlayerActor.Value); Mix((ulong)u.FreeActor.Value); Mix((ulong)u.NextActorId);
        Mix((ulong)u.PlayerShip.Value);   // Phase 5c: aktives Schiff kann wechseln (Kauf, Hangar)
        // Phase 10: Gate-Zustände, Sektorbesitz, Sperren (Stufen, Zeiten, Durchsetzung), Einfluss, Kontrollstufe, umkämpft, unsterbliche Stationen
        foreach (var g in u.Gates.Values.OrderBy(g => g.Id.Value)) { Mix((ulong)g.Id.Value); Mix((ulong)g.State); Mix(g.MainRoute ? 1UL : 0UL); }
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
            MixString(a.Tag);   // Phase 29: Kürzel
            Mix((ulong)a.LastRestoreTick);   // Phase 22
            Mix((ulong)a.CreatedTick);       // Phase 27 (Prüfung Phase 26): Gründungszeit (Aufbauzeit der Rücklage hängt daran)
            if (a.ArrestCooldownUntil != 0) Mix((ulong)a.ArrestCooldownUntil + 0x2802u);   // Phase 28: Festnahme-Sperre
            foreach (var (f, w) in a.Wanted.OrderBy(k => k.Key.Value)) { Mix((ulong)f.Value); MixF(w.Heat); MixF(w.Fine); Mix((ulong)w.LastCrimeTick); }
            foreach (var (f, c) in a.BountyClaims.OrderBy(k => k.Key.Value)) { Mix((ulong)f.Value); MixF(c); }
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
            }
            if (e.Spatial) Mix(0x5A7u);
            if (e.Station.Value != 0) { Mix(0x2803u); Mix((ulong)e.Station.Value); MixF(e.StationX); }   // Phase 28: Belagerung
            foreach (var m in e.Missiles)
            {
                Mix(0x2804u); Mix((ulong)m.Shooter.Value); Mix((ulong)m.Owner.Value); Mix((ulong)m.Side); MixString(m.Module); Mix((ulong)m.Target.Value); Mix((ulong)m.Station.Value);
                MixF(m.Left); MixF(m.Total); MixF(m.Speed);
                if (e.Spatial) { MixF(m.From.X); MixF(m.From.Y); }
            }
        }
        // Phase 28: Kriege und Friedensschlüsse (ohne Krieg mischt nichts)
        foreach (var w in u.Wars) { Mix(0x2805u); Mix((ulong)w.A.Value); Mix((ulong)w.B.Value); Mix((ulong)w.Since); MixF(w.Weariness); MixF(w.LossA); MixF(w.LossB); Mix((ulong)w.Sieges); }
        foreach (var (k, t) in u.PeaceSince.OrderBy(k => k.Key.A).ThenBy(k => k.Key.B)) { Mix(0x2806u); Mix((ulong)k.A); Mix((ulong)k.B); Mix((ulong)t); }
        foreach (var e in u.PendingEncounters) { Mix((ulong)e.Kind); Mix((ulong)e.Source.Value); Mix((ulong)e.Target.Value); Mix((ulong)e.Sector.Value); Mix((ulong)e.DueTick); }
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
                    case TradeCommand tc: Mix((ulong)tc.Limits.Center.Value); Mix((ulong)(uint)tc.Limits.RadiusJumps); MixF(tc.Limits.MaxBuyFactor); MixF(tc.Limits.Reserve); break;
                    case MineCommand mc: Mix((ulong)mc.Limits.Center.Value); Mix((ulong)(uint)mc.Limits.RadiusJumps); MixF(mc.Limits.MaxBuyFactor); MixF(mc.Limits.Reserve); break;
                    case BuildCommand bc: Mix((ulong)bc.Site.Value); break;
                    case LurkCommand lc: Mix((ulong)lc.Sector.Value); MixF(lc.Spot.X); MixF(lc.Spot.Y); Mix((ulong)lc.Until); break;   // Phase 27
                    case PursueCommand pc: Mix((ulong)pc.Target.Value); MixF(pc.Lead); Mix((ulong)pc.Until); break;
                    case AssaultCommand ac: Mix((ulong)ac.Sector.Value); Mix((ulong)ac.Station.Value); break;   // Phase 28
                }
            }
        }
        Mix((ulong)u.NextSquadronId);
        // Phase 14: Planeten der Sektoren
        foreach (var sec in u.Sectors.Values.OrderBy(s => s.Id.Value)) MixString(sec.Planet?.Name ?? "");
        foreach (var q in u.Squadrons.Values.OrderBy(q => q.Id.Value))
        {
            Mix((ulong)q.Id.Value); Mix((ulong)q.Owner.Value); Mix((ulong)q.Leader.Value); Mix((ulong)q.Formation); Mix((ulong)q.HomeBase.Value); Mix((ulong)q.Rules);
            foreach (var m in q.Members) Mix((ulong)m.Value);
        }
        return h;
    }
}
