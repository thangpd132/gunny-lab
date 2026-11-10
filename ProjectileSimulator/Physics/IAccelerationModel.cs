namespace ProjectileSimulator.Physics;

/// <summary>
/// One independent physical effect that contributes an acceleration to the projectile.
/// Gravity, wind and drag are each implemented as a separate model so that formulas can be
/// swapped, disabled or extended without touching the engine or the integrators.
/// The total acceleration is the sum of all active models (superposition of forces / m).
/// </summary>
public interface IAccelerationModel
{
    /// <summary>Short name shown in diagnostics (e.g. "Gravity", "QuadraticDrag").</summary>
    string Name { get; }

    /// <summary>Acceleration (units/s²) contributed by this model for the given state.</summary>
    Vector2D GetAcceleration(in ProjectileState state, double mass);
}
