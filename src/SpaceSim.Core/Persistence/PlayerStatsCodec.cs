using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

/// <summary>Lücken gegenüber IP2: Spielerstatistik ↔ <see cref="PlayerStatsSave"/> (additiv, Save bleibt v17) und ihr Hashbeitrag.</summary>
public static class PlayerStatsCodec
{
    public static PlayerStatsSave? ToSave(PlayerStats? s) => s is null ? null : new PlayerStatsSave
    {
        Earned = s.Earned, Spent = s.Spent, MaxWallet = s.MaxWallet, LastWallet = s.LastWallet, LastSector = s.LastSector,
        TradeRevenue = s.TradeRevenue, TradeSpent = s.TradeSpent, TradeUnits = s.TradeUnits, Sales = s.Sales, Purchases = s.Purchases,
        OreMined = s.OreMined, Kills = s.Kills, ShipsLost = s.ShipsLost, MissionsDone = s.MissionsDone, MissionsFailed = s.MissionsFailed,
        Jumps = s.Jumps, Bounties = s.Bounties, BountyCredits = s.BountyCredits, Transferred = s.Transferred,
        KillPoints = s.KillPoints, Crimes = s.Crimes, RankSeen = s.RankSeen, PilotName = s.PilotName,
    };

    public static PlayerStats? FromSave(PlayerStatsSave? s) => s is null ? null : new PlayerStats
    {
        Earned = s.Earned, Spent = s.Spent, MaxWallet = s.MaxWallet, LastWallet = s.LastWallet, LastSector = s.LastSector,
        TradeRevenue = s.TradeRevenue, TradeSpent = s.TradeSpent, TradeUnits = s.TradeUnits, Sales = s.Sales, Purchases = s.Purchases,
        OreMined = s.OreMined, Kills = s.Kills, ShipsLost = s.ShipsLost, MissionsDone = s.MissionsDone, MissionsFailed = s.MissionsFailed,
        Jumps = s.Jumps, Bounties = s.Bounties, BountyCredits = s.BountyCredits, Transferred = s.Transferred,
        KillPoints = s.KillPoints, Crimes = s.Crimes, RankSeen = s.RankSeen, PilotName = s.PilotName,
    };

    /// <summary>Hashbeitrag (nur mit Spielerpolitik und nur, wenn es eine Statistik gibt).</summary>
    public static void Mix(PlayerStats s, Action<ulong> mix)
    {
        mix(0x5157u);
        foreach (var d in new[] { s.Earned, s.Spent, s.MaxWallet, s.LastWallet, s.TradeRevenue, s.TradeSpent, s.TradeUnits, s.OreMined, s.BountyCredits, s.Transferred })
            mix((ulong)BitConverter.DoubleToInt64Bits(d));
        foreach (var i in new[] { s.LastSector, s.Sales, s.Purchases, s.Kills, s.ShipsLost, s.MissionsDone, s.MissionsFailed, s.Jumps, s.Bounties })
            mix((ulong)(long)i);
        // Piloten: nur wenn gesetzt (ältere Statistiken behalten ihren Hashbeitrag)
        if (s.KillPoints != 0 || s.Crimes != 0 || s.RankSeen != 0 || s.PilotName is not null)
        {
            mix(0x9117u);
            mix((ulong)BitConverter.DoubleToInt64Bits(s.KillPoints)); mix((ulong)(long)s.Crimes); mix((ulong)(long)s.RankSeen);
            foreach (char c in s.PilotName ?? "") mix(c);
        }
    }
}
