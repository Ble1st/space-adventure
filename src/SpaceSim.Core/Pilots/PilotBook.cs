using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Pilots;

/// <summary>Bilanz eines NPC-Kapitäns (nur Anzeige/Statistik: gespeichert, nicht gehasht, ohne Wirkung auf die Simulation).</summary>
public sealed class CaptainRecord
{
    public int Kills;
    public float Rating;
    /// <summary>Schiff im Gefecht zerstört (der Kapitän ist gefallen).</summary>
    public bool Fallen;
    /// <summary>Rolle beim letzten Abschuss (für Anzeige und Grundwertung, auch wenn das Schiff weg ist).</summary>
    public ShipRole Role;
    public ActorId Owner;
}

/// <summary>Merkmale des prozeduralen Porträts (rein; Godot zeichnet daraus Schichten). Indizes beziehen sich auf feste Paletten in der Ansicht.</summary>
public readonly record struct PortraitSpec(int Background, int Skin, int Hair, int HairColor, int Eyes, bool Helmet, bool Scar, int Insignia);

/// <summary>Ein Eintrag der Rangliste.</summary>
public sealed record PilotEntry(string Name, int Rank, float Rating, int Kills, int Deaths, bool IsPlayer, ShipId Ship, string Affiliation, string Status);

/// <summary>
/// Piloten und Ränge (Lücke gegenüber Interstellar Pilot 2; eigener Entwurf). Der Spielerpilot wertet <see cref="PlayerStats"/> aus (Abschüsse, Verluste,
/// Aufträge, Kopfgelder kommen von dort; Abschusspunkte und Verbrechen zählt <see cref="PilotSystem"/>). Der Rang folgt allein aus der Wertung.
/// NPC-Kapitäne: Polizei-, Kriegs-, Kaper- und Söldnerschiffe haben einen benannten Kapitän; Name, Porträt und Grundwertung folgen rein aus Welt-Seed und
/// Schiffs-Id (kein <see cref="Universe.Rng"/>), seine Abschüsse stehen in <see cref="Universe.Captains"/> (nur, wenn er einen hat).
/// </summary>
public static class PilotBook
{
    /// <summary>Rangstufen (Mindestwertung, Name, Kurzform).</summary>
    public static readonly (float Min, string Name, string Short)[] Ranks =
    {
        (0f, "Anwärter", "Anw."),
        (25f, "Kadett", "Kdt."),
        (75f, "Fähnrich", "Fhr."),
        (150f, "Leutnant", "Lt."),
        (300f, "Oberleutnant", "Olt."),
        (500f, "Hauptmann", "Hptm."),
        (800f, "Kommandant", "Kmdt."),
        (1200f, "Geschwaderführer", "GF"),
        (1800f, "Kommodore", "Kom."),
        (2600f, "Admiral", "Adm."),
    };

    public static int RankOf(float rating)
    {
        int r = 0;
        for (int i = 0; i < Ranks.Length; i++) if (rating >= Ranks[i].Min) r = i;
        return r;
    }

    public static string RankName(int rank) => Ranks[Math.Clamp(rank, 0, Ranks.Length - 1)].Name;

    /// <summary>Fortschritt zum nächsten Rang (0..1; höchster Rang = 1) und die nächste Schwelle (0 = keine).</summary>
    public static (float Share, float Next) Progress(float rating)
    {
        int r = RankOf(rating);
        if (r >= Ranks.Length - 1) return (1f, 0f);
        float a = Ranks[r].Min, b = Ranks[r + 1].Min;
        return (Math.Clamp((rating - a) / (b - a), 0f, 1f), b);
    }

    /// <summary>Hat dieses Schiff einen benannten Kapitän (KI-Polizei, Kriegsschiff, Kaperer, Söldner)?</summary>
    public static bool HasCaptain(Ship s) =>
        s.Controller == Controller.Ai && s.Role is ShipRole.Police or ShipRole.Warship or ShipRole.Pirate or ShipRole.Fighter;

    static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    static ulong Key(Universe u, long id, ulong salt) => Mix(u.Seed * 0x100000001B3UL ^ Mix((ulong)id ^ salt));

    static readonly string[] First =
    {
        "Arvo", "Brenna", "Cassik", "Dalia", "Eskel", "Fenna", "Garron", "Hela", "Ivar", "Jessa", "Korin", "Liska", "Maren", "Nilo", "Orla", "Perrin",
        "Quira", "Rada", "Soren", "Tamsin", "Ulla", "Vesko", "Wenna", "Yaro", "Zela", "Tibor", "Mira", "Osk", "Neve", "Calo",
    };
    static readonly string[] Last =
    {
        "Achterberg", "Brask", "Corvald", "Dahlen", "Eckhoff", "Falkner", "Grell", "Haskel", "Imhof", "Jarow", "Kestran", "Lohmar", "Morvik", "Norrin",
        "Ostrak", "Pellar", "Quandt", "Rask", "Sarnow", "Tavik", "Ulmer", "Varn", "Wendt", "Ystad", "Zorell", "Brandis", "Kolm", "Seiler", "Tremm", "Vossberg",
    };

    static string NameFrom(ulong k) => $"{First[(int)(k % (ulong)First.Length)]} {Last[(int)(k / 31 % (ulong)Last.Length)]}";

    /// <summary>Kapitänsname eines Schiffs (rein aus Welt-Seed und Id).</summary>
    public static string CaptainName(Universe u, ShipId ship) => NameFrom(Key(u, ship.Value, 0xC0FFEEUL));

    /// <summary>Name des Spielerpiloten: eigener Name aus der Statistik, sonst aus dem Welt-Seed.</summary>
    public static string PlayerName(Universe u) =>
        u.Actors.TryGetValue(u.PlayerActor, out var me) && me.Stats?.PilotName is { Length: > 0 } n ? n : NameFrom(Key(u, -1, 0x9117UL));

    public static PortraitSpec Portrait(Universe u, ShipId ship) => PortraitFrom(Key(u, ship.Value, 0x507UL), helmet: true);
    public static PortraitSpec PlayerPortrait(Universe u) => PortraitFrom(Key(u, -1, 0x507UL), helmet: false);

    static PortraitSpec PortraitFrom(ulong k, bool helmet) => new(
        Background: (int)(k % 6), Skin: (int)(k / 6 % 5), Hair: (int)(k / 30 % 5), HairColor: (int)(k / 150 % 6), Eyes: (int)(k / 900 % 4),
        Helmet: helmet && k / 3600 % 3 == 0, Scar: k / 10800 % 4 == 0, Insignia: (int)(k / 43200 % 4));

    /// <summary>Wertigkeit eines Abschusses: Stärke bei voller Hülle/vollem Schild × Punkte, Gesetzlose und Gesuchte × Faktor.</summary>
    public static float KillValue(Universe u, Ship target)
    {
        var r = u.Rules;
        float strength = target.Stats.WeaponPower + target.Stats.Shield / 20f + target.MaxHull / 50f + 0.5f;
        float v = strength * r.PilotKillPointsPerStrength;
        bool outlaw = u.Actors.TryGetValue(target.Owner, out var a) && (a.Kind == ActorKind.Outlaw || a.Wanted.Count > 0);
        if (outlaw || ThreatMap.IsRaider(target)) v *= r.PilotOutlawFactor;
        return MathF.Round(v, 1);
    }

    /// <summary>Kampfwertung des Spielers (≥ 0) aus der Statistik.</summary>
    public static float PlayerRating(Universe u)
    {
        if (!u.Actors.TryGetValue(u.PlayerActor, out var me) || me.Stats is not { } s) return 0f;
        var r = u.Rules;
        float v = (float)s.KillPoints + r.PilotMissionPoints * s.MissionsDone + r.PilotBountyPoints * s.Bounties
                  - r.PilotCrimePenalty * s.Crimes - r.PilotDeathPenalty * s.ShipsLost;
        return MathF.Max(0f, MathF.Round(v, 1));
    }

    /// <summary>Grundwertung eines Kapitäns nach Rolle plus fester Streuung aus der Id.</summary>
    public static float CaptainBase(Universe u, ShipId ship, ShipRole role)
    {
        float b = role switch { ShipRole.Warship => 160f, ShipRole.Fighter => 80f, ShipRole.Police => 40f, _ => 20f };
        return b + Key(u, ship.Value, 0xBA5EUL) % 60;
    }

    public static float CaptainRating(Universe u, ShipId ship, ShipRole role) =>
        CaptainBase(u, ship, role) + (u.Captains.TryGetValue(ship, out var rec) ? rec.Rating : 0f);

    /// <summary>Kurzzeile für Zielkarte/Funk: „Kpt. Leutnant Arvo Brask“ (null = kein Kapitän oder Ränge aus).</summary>
    public static string? CaptainLine(Universe u, Ship s)
    {
        if (!u.Rules.PilotRanks || !HasCaptain(s)) return null;
        return $"Kpt. {RankName(RankOf(CaptainRating(u, s.Id, s.Role)))} {CaptainName(u, s.Id)}";
    }

    /// <summary>Kürzere Formen derselben Zeile (für schmale Felder), längste zuerst.</summary>
    public static IReadOnlyList<string> CaptainLineOptions(Universe u, Ship s)
    {
        if (CaptainLine(u, s) is not { } full) return Array.Empty<string>();
        var rank = Ranks[RankOf(CaptainRating(u, s.Id, s.Role))];
        string name = CaptainName(u, s.Id);
        string last = name[(name.IndexOf(' ') + 1)..];
        return new[] { full, $"Kpt. {rank.Short} {name}", $"{rank.Short} {last}" };
    }

    static string Affiliation(Universe u, ActorId owner) => ActorNames.Tag(u, owner) is { Length: > 0 } t ? t : "—";

    /// <summary>Rangliste: Spieler, lebende Kapitäne und Kapitäne mit Abschüssen, die nicht mehr im Dienst sind; nach Wertung absteigend, dann Name.</summary>
    public static List<PilotEntry> Ranking(Universe u)
    {
        var list = new List<PilotEntry>();
        var me = u.Actors.TryGetValue(u.PlayerActor, out var pa) ? pa.Stats : null;
        float pr = PlayerRating(u);
        list.Add(new PilotEntry(PlayerName(u), RankOf(pr), pr, me?.Kills ?? 0, me?.ShipsLost ?? 0, true, u.PlayerShip, Affiliation(u, u.PlayerActor), "du"));
        if (!u.Rules.PilotRanks) return list;
        foreach (var s in u.ShipsById())
        {
            if (!HasCaptain(s)) continue;
            float r = CaptainRating(u, s.Id, s.Role);
            int k = u.Captains.TryGetValue(s.Id, out var rec) ? rec.Kills : 0;
            list.Add(new PilotEntry(CaptainName(u, s.Id), RankOf(r), r, k, 0, false, s.Id, Affiliation(u, s.Owner), RoleName(s.Role)));
        }
        foreach (var (id, rec) in u.Captains.OrderBy(x => x.Key.Value))
        {
            if (u.Ships.ContainsKey(id) || rec.Kills == 0) continue;
            float r = CaptainBase(u, id, rec.Role) + rec.Rating;
            list.Add(new PilotEntry(CaptainName(u, id), RankOf(r), r, rec.Kills, rec.Fallen ? 1 : 0, false, id, Affiliation(u, rec.Owner), rec.Fallen ? "gefallen" : "außer Dienst"));
        }
        list.Sort((a, b) => b.Rating != a.Rating ? b.Rating.CompareTo(a.Rating) : a.IsPlayer != b.IsPlayer ? (a.IsPlayer ? -1 : 1) : string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    public static string RoleName(ShipRole r) => r switch
    {
        ShipRole.Police => "Polizei",
        ShipRole.Warship => "Kriegsschiff",
        ShipRole.Pirate => "Kaperer",
        ShipRole.Fighter => "Söldner",
        _ => r.ToString(),
    };

    /// <summary>K:D als Text (ohne Verluste: Abschüsse, „—“ ohne beides).</summary>
    public static string KillDeath(int kills, int deaths) =>
        deaths == 0 ? (kills == 0 ? "—" : kills.ToString()) : (kills / (float)deaths).ToString("0.0#", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));

    /// <summary>Gefallene Kapitäne mit Abschüssen werden auf so viele begrenzt (die schwächsten fallen heraus).</summary>
    public const int MaxRetiredRecords = 60;
}
