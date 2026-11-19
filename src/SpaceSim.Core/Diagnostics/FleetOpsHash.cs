using SpaceSim.Core.Model;

namespace SpaceSim.Core.Diagnostics;

/// <summary>Flottenbefehle (Punkt 1/1b) im <see cref="UniverseHash"/>: nur Spielerschiffe tragen sie, die NPC-Welt mischt nichts davon.</summary>
public static class FleetOpsHash
{
    public static void Mix(ShipCommand c, Action<ulong> mix, Action<float> mixF)
    {
        switch (c)
        {
            case AttackCommand a: mix(0xF101u); mix((ulong)a.Ship.Value); mix((ulong)a.Station.Value); mix((ulong)a.Gate.Value); mix((ulong)a.Sector.Value); break;
            case SalvageCommand s: mix(0xF102u); mix((ulong)s.Sector.Value); break;
            case BlockadeCommand b: mix(0xF103u); mix((ulong)b.Gate.Value); mix((ulong)b.Faction.Value); break;
            case DeliverCommand d: mix(0xF104u); mix((ulong)d.From.Value); mix((ulong)d.To.Value); mix((ulong)d.Good.Value); mixF(d.Amount); mixF(d.MaxBuyFactor); break;
            case RallyCommand r: mix(0xF105u); mix((ulong)r.At.Value); mix((ulong)r.Leader.Value); break;
            case ConvoyCommand v:
                mix(0xF106u); mix((ulong)v.Limits.Center.Value); mix((ulong)(uint)v.Limits.RadiusJumps); mixF(v.Limits.MaxBuyFactor); mixF(v.Limits.Reserve);
                if (v.Limits.AllowLoss) mix(0xA110u);
                break;
        }
    }
}
