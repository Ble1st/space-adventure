using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Fahndung (Phase 11, FLOTTEN.md „Verbrechen und Fahndung“): je Akteur und Hoheitsgebiet (Fraktion) ein <see cref="WantedRecord"/> mit Heat und Strafe.
/// Verbrechen (<see cref="Report"/>) heben die Heat nach <see cref="EconomyRules.CrimeHeat"/> (Obergrenze <see cref="EconomyRules.WantedMaxHeat"/>) und
/// die Strafe nach <see cref="EconomyRules.CrimeFine"/>; nach <see cref="EconomyRules.WantedMinTicks"/> ohne neues Verbrechen verfällt die Heat
/// (<see cref="WantedSystem"/>), bei 0 fällt der Eintrag weg (Strafe erlassen). Stufen 0–4 aus <see cref="EconomyRules.WantedLevels"/>.
/// </summary>
public static class Wanted
{
    /// <summary>Stufe zu einer Heat (Anzahl erreichter Schwellen, 0–4).</summary>
    public static int Level(EconomyRules r, float heat)
    {
        int level = 0;
        for (int i = 0; i < r.WantedLevels.Count && i < 4; i++) if (heat >= r.WantedLevels[i] - 1e-4f) level = i + 1;
        return level;
    }

    /// <summary>Stufe eines Akteurs bei einer Fraktion (0 = sauber).</summary>
    public static int LevelOf(Universe u, ActorId actor, FactionId faction) =>
        u.Actors.TryGetValue(actor, out var a) && a.Wanted.TryGetValue(faction, out var w) ? Level(u.Rules, w.Heat) : 0;

    /// <summary>Höchste Stufe eines Akteurs über alle Fraktionen.</summary>
    public static int MaxLevel(Universe u, ActorId actor) =>
        u.Actors.TryGetValue(actor, out var a) && a.Wanted.Count > 0 ? a.Wanted.Values.Max(w => Level(u.Rules, w.Heat)) : 0;

    /// <summary>Was das Gericht für die Löschung des Eintrags verlangt: aufgelaufene Strafe + Heat × <see cref="EconomyRules.FinePerHeat"/>.</summary>
    public static float FineOf(Universe u, WantedRecord w) => MathF.Ceiling(w.Fine + w.Heat * u.Rules.FinePerHeat);

    /// <summary>Kopfgeld auf ein Schiff dieses Akteurs bei dieser Fraktion: Stufe (mindestens 1, nur mit Eintrag) × <see cref="EconomyRules.BountyPerLevel"/>.</summary>
    public static float BountyOn(Universe u, ActorId actor, FactionId faction)
    {
        if (!u.Actors.TryGetValue(actor, out var a) || !a.Wanted.TryGetValue(faction, out var w)) return 0f;
        return Math.Max(1, Level(u.Rules, w.Heat)) * u.Rules.BountyPerLevel;
    }

    /// <summary>
    /// Meldet ein Verbrechen des Akteurs im Hoheitsgebiet der Fraktion: Heat und Strafe steigen (zusätzlich <paramref name="extraFine"/>), Zeitpunkt
    /// gemerkt, Ereignisse <see cref="CrimeReported"/> und bei Stufenwechsel <see cref="WantedChanged"/>. Ohne Akteur/Fraktion passiert nichts.
    /// </summary>
    public static void Report(Universe u, ActorId actor, FactionId faction, string crime, float extraFine = 0f)
    {
        // Runde 38: nur KI-Fraktionen führen Fahndung (die Spielerfraktion hat weder Polizei noch Gericht)
        if (!u.Actors.TryGetValue(actor, out var a) || !u.IsNpcFaction(faction)) return;
        // Ausbau Punkt 17: eine etablierte Banditenfraktion fahndet nicht nach ihren eigenen Leuten (Rundengefecht-Überfall in eigenem Gebiet
        // meldet sonst Piraterie bei sich selbst — Engage.StartRaid wie PirateSystem)
        if (a.Membership == faction && Bandits.IsEstablished(u, faction)) return;
        var r = u.Rules;
        if (!a.Wanted.TryGetValue(faction, out var w)) a.Wanted[faction] = w = new WantedRecord();
        int before = Level(r, w.Heat);
        w.Heat = MathF.Min(r.WantedMaxHeat, w.Heat + r.CrimeHeat.GetValueOrDefault(crime, 1f));
        w.Fine += r.CrimeFine.GetValueOrDefault(crime, 0f) + MathF.Max(0f, extraFine);
        w.LastCrimeTick = u.Tick;
        w.LastCrime = crime;
        int after = Level(r, w.Heat);
        float standing = CrimeStanding.Apply(u, actor, faction, crime);   // Spielersperre: Verbrechen des Spielers senken auch sein Ansehen
        u.Events.Publish(new CrimeReported(actor, faction, crime, w.Heat, after, standing));
        if (after != before) u.Events.Publish(new WantedChanged(actor, faction, before, after));
    }

    /// <summary>
    /// Phase 25 (Korrektur): Wer verfolgt einen Angriff des Spielers? Immer die Fraktion des Opfers (Besitzer des Schiffs) und der Besitzer des Sektors; sind
    /// beide leer (fraktionsloses Opfer im herrenlosen Sektor), die Fraktionen der angrenzenden Sektoren (über Tore), und gibt es dort keine, die nächste
    /// Fraktion nach Sprüngen. Feste Reihenfolge (Fraktions-Id), ohne Doppelte. Rein.
    /// </summary>
    public static List<FactionId> AssaultAuthorities(Universe u, SectorId sector, ActorId victimOwner)
    {
        var set = new SortedSet<int>();
        if (u.Sectors.TryGetValue(sector, out var sec) && u.IsNpcFaction(sec.Owner)) set.Add(sec.Owner.Value);
        if (u.Actors.TryGetValue(victimOwner, out var va) && va.Membership is { } m && u.IsNpcFaction(m)) set.Add(m.Value);
        if (set.Count == 0 && sec is not null)
        {
            // Nachbarn über Tore, dann Breitensuche bis zur ersten Ebene mit einem Sektorbesitzer
            var seen = new HashSet<int> { sector.Value };
            var ring = new List<SectorId> { sector };
            while (set.Count == 0 && ring.Count > 0)
            {
                var next = new List<SectorId>();
                foreach (var sid in ring)
                    foreach (var gid in u.Sectors[sid].Gates)
                    {
                        var far = u.Gates[u.Gates[gid].Target].Sector;
                        if (seen.Add(far.Value)) next.Add(far);
                    }
                foreach (var n in next.OrderBy(x => x.Value))
                    if (u.IsNpcFaction(u.Sectors[n].Owner)) set.Add(u.Sectors[n].Owner.Value);
                ring = next;
            }
        }
        return set.Select(v => new FactionId(v)).ToList();
    }

    /// <summary>Phase 25 (Korrektur): meldet einen Angriff bei allen zuständigen Fraktionen (<see cref="AssaultAuthorities"/>). Liefert die Fraktionen.</summary>
    /// Spielersperre: <paramref name="kill"/> = das Opfer wurde zerstört — zusätzlicher Ansehensverlust „kill“ (<see cref="CrimeStanding"/>).
    public static List<FactionId> ReportAssault(Universe u, ActorId actor, SectorId sector, ActorId victimOwner, string crime = "assault", bool kill = false)
    {
        // Ausbau Punkt 6: ein Angriff des Spielers auf Schiffe seines Fehdegegners ist kein Verbrechen (wie im Krieg); Stationen bleiben geschützt
        if (crime == "assault" && u.Feuds.Count > 0 && u.Actors.TryGetValue(actor, out var ac) && ac.Kind == ActorKind.Player && Feuds.PlayerFeudWith(u, victimOwner)) return new List<FactionId>();
        var list = AssaultAuthorities(u, sector, victimOwner);
        foreach (var f in list)
        {
            Report(u, actor, f, crime);
            if (kill) CrimeStanding.Apply(u, actor, f, "kill");
        }
        return list;
    }

    /// <summary>Setzt die Heat eines Eintrags (Strafe teilweise eingezogen) bzw. löscht ihn (bezahlt); meldet den Stufenwechsel.</summary>
    public static void SetHeat(Universe u, Actor a, FactionId faction, float heat, float fine)
    {
        if (!a.Wanted.TryGetValue(faction, out var w)) return;
        int before = Level(u.Rules, w.Heat);
        if (heat <= 0f) a.Wanted.Remove(faction);
        else { w.Heat = heat; w.Fine = MathF.Max(0f, fine); }
        int after = heat <= 0f ? 0 : Level(u.Rules, heat);
        if (after != before) u.Events.Publish(new WantedChanged(a.Id, faction, before, after));
    }

    /// <summary>Verfall aller Einträge um <paramref name="dt"/> Ticks (feste Reihenfolge).</summary>
    public static void Decay(Universe u, int dt)
    {
        var r = u.Rules;
        foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value))
        {
            if (a.Wanted.Count == 0) continue;
            foreach (var f in a.Wanted.Keys.OrderBy(k => k.Value).ToList())
            {
                var w = a.Wanted[f];
                if (u.Tick - w.LastCrimeTick < r.WantedMinTicks) continue;
                int before = Level(r, w.Heat);
                w.Heat -= r.WantedDecayPerTick * dt;
                if (w.Heat <= 0f) { a.Wanted.Remove(f); if (before != 0) u.Events.Publish(new WantedChanged(a.Id, f, before, 0)); continue; }
                int after = Level(r, w.Heat);
                if (after != before) u.Events.Publish(new WantedChanged(a.Id, f, before, after));
            }
        }
    }
}

/// <summary>Verfall der Fahndung (Phase 11; selten, Intervall).</summary>
public sealed class WantedSystem : ISimSystem
{
    public void Step(Universe u, int dt) { Wanted.Decay(u, dt); CrimeStanding.Recover(u, dt); }   // Spielersperre: Erholung des Ansehens
}
