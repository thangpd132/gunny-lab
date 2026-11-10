namespace ProjectileSimulator.Physics;

/// <summary>
/// Immutable 2D vector in PHYSICS space (X to the right, Y pointing UP).
/// The front-end converts to canvas space (Y pointing down); the engine never does.
/// </summary>
public readonly record struct Vector2D(double X, double Y)
{
    public static readonly Vector2D Zero = new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2D operator -(Vector2D a, Vector2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2D operator -(Vector2D a) => new(-a.X, -a.Y);
    public static Vector2D operator *(Vector2D a, double s) => new(a.X * s, a.Y * s);
    public static Vector2D operator *(double s, Vector2D a) => new(a.X * s, a.Y * s);
    public static Vector2D operator /(Vector2D a, double s) => new(a.X / s, a.Y / s);

    public static double Dot(Vector2D a, Vector2D b) => a.X * b.X + a.Y * b.Y;

    public static double Distance(Vector2D a, Vector2D b) => (a - b).Length;

    /// <summary>Linear interpolation a + (b - a) * t.</summary>
    public static Vector2D Lerp(Vector2D a, Vector2D b, double t) => a + (b - a) * t;

    /// <summary>Unit vector from an angle in radians measured counter-clockwise from +X.</summary>
    public static Vector2D FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));

    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    public override string ToString() => $"({X:0.####}, {Y:0.####})";
}
