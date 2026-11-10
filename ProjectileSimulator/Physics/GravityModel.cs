namespace ProjectileSimulator.Physics;

/// <summary>
/// Uniform gravitational field: a = (0, -g).
/// With only this model the analytic solution is the classic oblique throw:
/// <code>
///   X(t) = X0 + Vx·t
///   Y(t) = Y0 + Vy·t − ½·g·t²
/// </code>
/// </summary>
public sealed class GravityModel : IAccelerationModel
{
    public GravityModel(double g)
    {
        if (!double.IsFinite(g) || g < 0)
            throw new ArgumentOutOfRangeException(nameof(g), "Gravity must be a finite, non-negative number.");
        G = g;
    }

    public double G { get; }

    public string Name => "Gravity";

    public Vector2D GetAcceleration(in ProjectileState state, double mass) => new(0, -G);
}
