namespace ProjectileSimulator.Physics;

/// <summary>Numerical method used to advance the state by one time step.</summary>
public enum IntegratorType
{
    /// <summary>
    /// Semi-implicit (symplectic) Euler — the classic game-physics integrator:
    /// <code>
    ///   a     = a(x_n, v_n)
    ///   v_n+1 = v_n + a·dt          (velocity first)
    ///   x_n+1 = x_n + v_n+1·dt      (position uses the NEW velocity)
    /// </code>
    /// First order accurate but energy-stable. Most games (likely including Gunny) use this.
    /// </summary>
    SemiImplicitEuler,

    /// <summary>
    /// Explicit (forward) Euler: x_n+1 = x_n + v_n·dt, v_n+1 = v_n + a·dt.
    /// Included only for comparison; it drifts and gains energy.
    /// </summary>
    ExplicitEuler,

    /// <summary>
    /// Classic 4th-order Runge–Kutta. Exact for constant acceleration (up to rounding) and very
    /// accurate with drag; use it as the "ground truth" reference.
    /// </summary>
    RungeKutta4
}

public interface IIntegrator
{
    ProjectileState Step(Projectile body, in ProjectileState state, double dt);
}

public sealed class SemiImplicitEulerIntegrator : IIntegrator
{
    public ProjectileState Step(Projectile body, in ProjectileState s, double dt)
    {
        var d = body.Evaluate(s);                                  // 1. acceleration
        var v = s.Velocity + d.Acceleration * dt;                  // 2. update velocity
        var x = s.Position + (v + body.DriftVelocity) * dt;        // 3. update position with new velocity
        return new ProjectileState(s.Time + dt, x, v);
    }
}

public sealed class ExplicitEulerIntegrator : IIntegrator
{
    public ProjectileState Step(Projectile body, in ProjectileState s, double dt)
    {
        var d = body.Evaluate(s);
        return new ProjectileState(s.Time + dt, s.Position + d.PositionRate * dt, s.Velocity + d.Acceleration * dt);
    }
}

public sealed class RungeKutta4Integrator : IIntegrator
{
    public ProjectileState Step(Projectile body, in ProjectileState s, double dt)
    {
        var h = dt * 0.5;
        var k1 = body.Evaluate(s);
        var k2 = body.Evaluate(new ProjectileState(s.Time + h, s.Position + k1.PositionRate * h, s.Velocity + k1.Acceleration * h));
        var k3 = body.Evaluate(new ProjectileState(s.Time + h, s.Position + k2.PositionRate * h, s.Velocity + k2.Acceleration * h));
        var k4 = body.Evaluate(new ProjectileState(s.Time + dt, s.Position + k3.PositionRate * dt, s.Velocity + k3.Acceleration * dt));

        var dx = (k1.PositionRate + 2 * k2.PositionRate + 2 * k3.PositionRate + k4.PositionRate) * (dt / 6.0);
        var dv = (k1.Acceleration + 2 * k2.Acceleration + 2 * k3.Acceleration + k4.Acceleration) * (dt / 6.0);
        return new ProjectileState(s.Time + dt, s.Position + dx, s.Velocity + dv);
    }
}

public static class IntegratorFactory
{
    private static readonly IIntegrator SemiImplicit = new SemiImplicitEulerIntegrator();
    private static readonly IIntegrator Explicit = new ExplicitEulerIntegrator();
    private static readonly IIntegrator Rk4 = new RungeKutta4Integrator();

    /// <summary>Integrators are stateless, so shared singletons are thread-safe.</summary>
    public static IIntegrator Get(IntegratorType type) => type switch
    {
        IntegratorType.SemiImplicitEuler => SemiImplicit,
        IntegratorType.ExplicitEuler => Explicit,
        IntegratorType.RungeKutta4 => Rk4,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown integrator.")
    };
}
