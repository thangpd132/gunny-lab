namespace ProjectileSimulator.Models;

public enum SimulationStatus
{
    /// <summary>The projectile entered the target's hit box.</summary>
    HitTarget,

    /// <summary>The projectile reached the ground without touching the target.</summary>
    HitGround,

    /// <summary>The projectile left the world horizontally (|x| &gt; WorldHalfWidth) or flew too high.</summary>
    OutOfBounds,

    /// <summary>MaxTime was reached before any collision (e.g. gravity = 0).</summary>
    Timeout
}

public sealed class SimulationResult
{
    public SimulationStatus Status { get; init; }

    /// <summary>Human-readable description of <see cref="Status"/>.</summary>
    public string CollisionStatus { get; init; } = "";

    public bool HitTarget => Status == SimulationStatus.HitTarget;

    // Echo of the inputs
    public double Angle { get; init; }
    public double EffectiveAngle { get; init; }
    public double Power { get; init; }
    public double Distance { get; init; }
    public double Wind { get; init; }

    // Derived launch values
    public double InitialSpeed { get; init; }
    public double EffectiveGravity { get; init; }
    public double LaunchX { get; init; }
    public double LaunchY { get; init; }

    // Outcome
    public double FlightTime { get; init; }
    public double MaxHeight { get; init; }
    public double MaxHeightX { get; init; }
    public double LandingX { get; init; }
    public double LandingY { get; init; }

    /// <summary>LandingX − LaunchX (signed: negative when the shot travels to the left).</summary>
    public double HorizontalRange { get; init; }

    /// <summary>LandingX − target centre X (signed; 0 = dead centre).</summary>
    public double MissDistance { get; init; }

    public double ImpactSpeed { get; init; }
    public double ImpactAngle { get; init; }
    public int Steps { get; init; }
    public double TargetX { get; init; }

    /// <summary>
    /// Closed-form range over flat ground for gravity-only motion (no wind, no drag), from the
    /// launch height: R = Vx·(Vy + √(Vy² + 2·g·h)) / g. Null if the model is not gravity-only.
    /// Useful to see the integrator error.
    /// </summary>
    public double? AnalyticRange { get; init; }
    public double? AnalyticFlightTime { get; init; }

    public string Integrator { get; init; } = "";
    public double DeltaTime { get; init; }
    public IReadOnlyList<string> ActiveModels { get; init; } = Array.Empty<string>();
    public double ComputeTimeMs { get; set; }

    public IReadOnlyList<TrajectoryPoint> Trajectory { get; init; } = Array.Empty<TrajectoryPoint>();
}
