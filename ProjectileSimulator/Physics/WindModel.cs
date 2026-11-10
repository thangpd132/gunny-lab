namespace ProjectileSimulator.Physics;

/// <summary>How the wind value is turned into physics.</summary>
public enum WindMode
{
    /// <summary>
    /// The air mass moves with a constant horizontal velocity w = W·k_drift.
    /// The projectile is carried along: dX/dt = v + w. If drag is enabled it acts on the
    /// velocity RELATIVE to the moving air, so a long flight in a strong headwind is slowed
    /// (this is how real wind works).
    /// </summary>
    ConstantWind,

    /// <summary>
    /// The wind is a constant horizontal force: a_x = W·k_accel. Displacement grows with t²
    /// (Δx = ½·a_x·t²). Many artillery games use this simplification.
    /// </summary>
    WindAcceleration
}

/// <summary>
/// Wind as a constant horizontal acceleration: a = (W · scale, 0).
/// Positive wind pushes towards +X (to the right on screen).
/// </summary>
public sealed class WindAccelerationModel : IAccelerationModel
{
    public WindAccelerationModel(double accelerationX)
    {
        if (!double.IsFinite(accelerationX))
            throw new ArgumentOutOfRangeException(nameof(accelerationX));
        AccelerationX = accelerationX;
    }

    public double AccelerationX { get; }

    public string Name => "WindAcceleration";

    public Vector2D GetAcceleration(in ProjectileState state, double mass) => new(AccelerationX, 0);
}

/// <summary>Factory helpers that convert a UI wind value into physical quantities.</summary>
public static class WindModel
{
    /// <summary>Horizontal drift velocity of the air mass for <see cref="WindMode.ConstantWind"/>.</summary>
    public static Vector2D DriftVelocity(double wind, double multiplier, double driftScale)
        => new(wind * multiplier * driftScale, 0);

    /// <summary>Horizontal acceleration for <see cref="WindMode.WindAcceleration"/>.</summary>
    public static WindAccelerationModel Acceleration(double wind, double multiplier, double accelerationScale)
        => new(wind * multiplier * accelerationScale);
}
