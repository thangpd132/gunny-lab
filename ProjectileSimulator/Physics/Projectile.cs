namespace ProjectileSimulator.Physics;

/// <summary>
/// Kinematic state of the projectile at one instant.
/// <para>
/// <see cref="Velocity"/> is the velocity RELATIVE TO THE AIR. When the wind is modelled as a
/// moving air mass (<see cref="WindMode.ConstantWind"/>), the velocity relative to the ground is
/// Velocity + windDrift. With no drift both frames coincide.
/// </para>
/// </summary>
public readonly record struct ProjectileState(double Time, Vector2D Position, Vector2D Velocity);

/// <summary>
/// Time derivative of a <see cref="ProjectileState"/>: d(position)/dt and d(velocity)/dt.
/// </summary>
public readonly record struct StateDerivative(Vector2D PositionRate, Vector2D Acceleration);

/// <summary>
/// The projectile body: physical properties plus the force models acting on it.
/// It knows how to evaluate its own equations of motion, but not how to integrate them
/// (that is the job of an <see cref="IIntegrator"/>).
/// </summary>
public sealed class Projectile
{
    private readonly IReadOnlyList<IAccelerationModel> _accelerationModels;

    public Projectile(double mass, IReadOnlyList<IAccelerationModel> accelerationModels, Vector2D driftVelocity)
    {
        if (!(mass > 0) || !double.IsFinite(mass))
            throw new ArgumentOutOfRangeException(nameof(mass), "Mass must be a positive finite number.");
        Mass = mass;
        _accelerationModels = accelerationModels;
        DriftVelocity = driftVelocity;
    }

    /// <summary>Mass used by force-based models (drag): a = F / m.</summary>
    public double Mass { get; }

    /// <summary>
    /// Constant velocity of the air mass (wind drift). It is added to the position rate:
    /// dX/dt = v_air_relative + w.
    /// </summary>
    public Vector2D DriftVelocity { get; }

    public IReadOnlyList<IAccelerationModel> AccelerationModels => _accelerationModels;

    /// <summary>
    /// Equations of motion:
    /// <code>
    ///   dX/dt = v + w_drift
    ///   dv/dt = Σ a_i(X, v)        (gravity + wind acceleration + drag/m ...)
    /// </code>
    /// </summary>
    public StateDerivative Evaluate(in ProjectileState state)
    {
        var acceleration = Vector2D.Zero;
        for (var i = 0; i < _accelerationModels.Count; i++)
            acceleration += _accelerationModels[i].GetAcceleration(state, Mass);

        return new StateDerivative(state.Velocity + DriftVelocity, acceleration);
    }

    /// <summary>Velocity relative to the ground (what an observer on the ground sees).</summary>
    public Vector2D GroundVelocity(in ProjectileState state) => state.Velocity + DriftVelocity;
}
