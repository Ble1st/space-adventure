namespace SpaceSim.Core.Model;

public readonly record struct SectorId(int Value);
public readonly record struct GateId(int Value);
public readonly record struct StationId(int Value);
public readonly record struct ShipId(int Value);
public readonly record struct FactionId(int Value);
public readonly record struct GoodId(int Value);

public readonly record struct Vec2(float X, float Y)
{
    public static float Distance(Vec2 a, Vec2 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
