namespace SpaceSim.Core.Model;

/// <summary>
/// Kleiner, speicherbarer Zufallsgenerator (xorshift64*). System.Random ist weder serialisierbar noch
/// versionsstabil; die Simulation muss aber aus (Seed, Zustand) reproduzierbar sein.
/// </summary>
public sealed class DeterministicRng
{
    public ulong State;

    public DeterministicRng(ulong seed) => State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public ulong NextUInt64()
    {
        State ^= State >> 12; State ^= State << 25; State ^= State >> 27;
        return State * 0x2545F4914F6CDD1DUL;
    }

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    public int NextInt(int maxExclusive) => maxExclusive <= 1 ? 0 : (int)(NextUInt64() % (ulong)maxExclusive);
    public float Range(float lo, float hi) => lo + (float)NextDouble() * (hi - lo);
}
