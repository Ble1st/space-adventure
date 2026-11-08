namespace SpaceSim.Core.Model;

public readonly record struct SectorId(int Value);
public readonly record struct GateId(int Value);
public readonly record struct StationId(int Value);
public readonly record struct ShipId(int Value);
public readonly record struct FactionId(int Value);
public readonly record struct GoodId(int Value);

public readonly record struct Vec2(float X, float Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);
    public float Length => MathF.Sqrt(X * X + Y * Y);
    public float Dot(Vec2 o) => X * o.X + Y * o.Y;
    public Vec2 Normalized() { float l = Length; return l > 1e-6f ? new Vec2(X / l, Y / l) : new Vec2(0, -1); }
    public static Vec2 FromAngle(float a) => new(MathF.Cos(a), MathF.Sin(a));

    public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public static float Distance(Vec2 a, Vec2 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
