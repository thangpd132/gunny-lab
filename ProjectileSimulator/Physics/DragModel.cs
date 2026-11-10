namespace ProjectileSimulator.Physics;

/// <summary>Air-resistance law.</summary>
public enum DragType
{
    /// <summary>No air resistance.</summary>
    None,

    /// <summary>
    /// Stokes / viscous drag, proportional to velocity: F = −b·v  ⇒  a = −(b/m)·v.
    /// Good for slow, small objects; has an analytic solution.
    /// </summary>
    Linear,

    /// <summary>
    /// Newtonian drag, proportional to the square of the speed:
    /// F = −½·ρ·Cd·A·|v|·v  ⇒  a = −(½·ρ·Cd·A / m)·|v|·v.
    /// Realistic for fast projectiles; no closed-form solution, must be integrated numerically.
    /// </summary>
    Quadratic
}

/// <summary>Linear drag: a = −(b / m)·v, with v the velocity relative to the air.</summary>
public sealed class LinearDragModel : IAccelerationModel
{
    public LinearDragModel(double coefficientB)
    {
        if (!double.IsFinite(coefficientB) || coefficientB < 0)
            throw new ArgumentOutOfRangeException(nameof(coefficientB), "Drag coefficient must be ≥ 0.");
        B = coefficientB;
    }

    /// <summary>Linear damping coefficient b (mass/s).</summary>
    public double B { get; }

    public string Name => "LinearDrag";

    public Vector2D GetAcceleration(in ProjectileState state, double mass) => state.Velocity * (-B / mass);
}

/// <summary>Quadratic drag: a = −(½·ρ·Cd·A / m)·|v|·v, with v the velocity relative to the air.</summary>
public sealed class QuadraticDragModel : IAccelerationModel
{
    public QuadraticDragModel(double airDensity, double dragCoefficient, double crossSectionArea)
    {
        if (!double.IsFinite(airDensity) || airDensity < 0) throw new ArgumentOutOfRangeException(nameof(airDensity));
        if (!double.IsFinite(dragCoefficient) || dragCoefficient < 0) throw new ArgumentOutOfRangeException(nameof(dragCoefficient));
        if (!double.IsFinite(crossSectionArea) || crossSectionArea < 0) throw new ArgumentOutOfRangeException(nameof(crossSectionArea));
        AirDensity = airDensity;
        DragCoefficient = dragCoefficient;
        CrossSectionArea = crossSectionArea;
    }

    public double AirDensity { get; }
    public double DragCoefficient { get; }
    public double CrossSectionArea { get; }

    /// <summary>Lumped constant k = ½·ρ·Cd·A (force = −k·|v|·v).</summary>
    public double K => 0.5 * AirDensity * DragCoefficient * CrossSectionArea;

    public string Name => "QuadraticDrag";

    public Vector2D GetAcceleration(in ProjectileState state, double mass)
    {
        var v = state.Velocity;
        var speed = v.Length;
        return v * (-K * speed / mass);
    }
}

/// <summary>Builds the configured drag model (or null when drag is off).</summary>
public static class DragModel
{
    public static IAccelerationModel? Create(DragType type, double coefficient, double airDensity, double area)
        => type switch
        {
            DragType.None => null,
            DragType.Linear => coefficient > 0 ? new LinearDragModel(coefficient) : null,
            DragType.Quadratic => coefficient > 0 ? new QuadraticDragModel(airDensity, coefficient, area) : null,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown drag type.")
        };
}
