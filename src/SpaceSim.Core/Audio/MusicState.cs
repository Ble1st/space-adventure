using SpaceSim.Core.Model;

namespace SpaceSim.Core.Audio;

/// <summary>Musikzustand (Runde 12): Stimmung bzw. Schichtmischung, die der Musikregler der Ansicht spielt.</summary>
public enum MusicMood { Menu, Docked, SafeFlight, Frontier, Mining, Warning, Combat, Siege }

/// <summary>Was nur die Ansicht weiß (offenes Menü, angedockt, Abbau läuft) — der Rest kommt aus dem Universum.</summary>
public readonly record struct MusicContext(bool Menu, bool Docked, bool Mining);

/// <summary>
/// Runde 12: Musikzustand als reine Funktion über Universum und Spielerlage (keine Wiedergabe, keine Zeit außer der übergebenen) — Priorität: Menü, Gefecht
/// (Belagerung/Großkampf vor Gefecht), angekündigter Überfall/Festnahme (Warnung), angedockt, Bergbau, Grenzgebiet (herrenlos, wenig Sicherheit, hohe
/// Bedrohung), sicherer Flug. <see cref="Hysteresis"/> verhindert Flattern: Steigerungen (und Menü/Andocken) sofort, Beruhigung erst nach einer Mindestdauer.
/// </summary>
public static class MusicState
{
    /// <summary>Ab so vielen aktiven Teilnehmern gilt ein Gefecht als Großkampf (Schicht „epic“).</summary>
    public const int BigBattleParticipants = 6;
    /// <summary>Grenzgebiet: Bedrohung ab diesem Wert …</summary>
    public const float FrontierThreat = 1.5f;
    /// <summary>… oder Sicherheit unter diesem Wert (oder herrenlos).</summary>
    public const float FrontierSecurity = 0.5f;

    /// <summary>Schlüssel der Zustandstabelle in <c>music.json</c>.</summary>
    public static string Key(MusicMood m) => m switch
    {
        MusicMood.Menu => "menue",
        MusicMood.Docked => "angedockt",
        MusicMood.SafeFlight => "flug_sicher",
        MusicMood.Frontier => "grenzgebiet",
        MusicMood.Mining => "bergbau",
        MusicMood.Warning => "warnung",
        MusicMood.Combat => "gefecht",
        _ => "belagerung",
    };

    /// <summary>Spannungsstufe (für die Hysterese): Steigerungen wechseln sofort.</summary>
    public static int Intensity(MusicMood m) => m switch
    {
        MusicMood.Menu => 0, MusicMood.Docked => 1, MusicMood.SafeFlight => 2, MusicMood.Mining => 2, MusicMood.Frontier => 3,
        MusicMood.Warning => 4, MusicMood.Combat => 5, _ => 6,
    };

    /// <summary>Roher Zustand ohne Hysterese.</summary>
    public static MusicMood Raw(Universe u, MusicContext ctx)
    {
        if (ctx.Menu) return MusicMood.Menu;
        var me = u.PlayerShip;
        if (u.Fights.Of(me) is { } e && e.Of(me) is { Active: true })
        {
            int active = 0;
            foreach (var p in e.Participants) if (p.Active) active++;
            return e.Cause == EncounterCause.Siege || active >= BigBattleParticipants ? MusicMood.Siege : MusicMood.Combat;
        }
        foreach (var pe in u.PendingEncounters) if (pe.Target == me) return MusicMood.Warning;
        if (ctx.Docked) return MusicMood.Docked;
        if (ctx.Mining) return MusicMood.Mining;
        if (u.Sectors.TryGetValue(u.PlayerSector, out var s) && (s.Owner.Value == 0 || s.Threat >= FrontierThreat || s.Security < FrontierSecurity)) return MusicMood.Frontier;
        return MusicMood.SafeFlight;
    }

    /// <summary>Gehaltener Zustand mit Hysterese (je Ansicht eine Instanz; Zeit in Sekunden).</summary>
    public sealed class Hysteresis
    {
        /// <summary>Ein Zustand bleibt mindestens so lange, bevor er sich beruhigt.</summary>
        public float MinHoldSeconds = 6f;
        /// <summary>Der ruhigere Rohzustand muss so lange anliegen, bevor gewechselt wird.</summary>
        public float CalmSeconds = 5f;

        public MusicMood Current { get; private set; } = MusicMood.Menu;
        double _since = double.NegativeInfinity, _rawSince;
        MusicMood _raw = MusicMood.Menu;
        bool _started;

        /// <summary>Neuen Rohzustand einspeisen; liefert den gehaltenen Zustand.</summary>
        public MusicMood Update(MusicMood raw, double now)
        {
            if (!_started) { _started = true; Current = raw; _raw = raw; _since = now; _rawSince = now; return Current; }
            if (raw != _raw) { _raw = raw; _rawSince = now; }
            if (raw == Current) return Current;
            bool immediate = raw is MusicMood.Menu or MusicMood.Docked || Current is MusicMood.Menu or MusicMood.Docked
                || Intensity(raw) > Intensity(Current);
            if (immediate || (now - _since >= MinHoldSeconds && now - _rawSince >= CalmSeconds))
            {
                Current = raw;
                _since = now;
            }
            return Current;
        }
    }
}
