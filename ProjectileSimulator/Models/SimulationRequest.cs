using System.ComponentModel.DataAnnotations;
using ProjectileSimulator.Physics;

namespace ProjectileSimulator.Models;

/// <summary>
/// Full input of one shot. All lengths are in "game units" (u): the Distance slider measures
/// in the same unit, so Distance = 5 means the target stands 5 u from the shooter.
/// Gravity is in u/s², velocities in u/s.
/// </summary>
public sealed class SimulationRequest : IValidatableObject
{
    // ---------------- Shot parameters (main control panel) ----------------

    /// <summary>Horizontal distance from shooter (x = 0) to the target centre.</summary>
    [Range(0.1, 100)] public double Distance { get; set; } = 5;

    /// <summary>Firing angle in degrees: 0 = right, 90 = straight up, 180 = left.</summary>
    [Range(0, 180)] public double Angle { get; set; } = 45;

    /// <summary>Game power 1..100, converted to an initial speed by <see cref="CalibrationParameters"/>.</summary>
    [Range(0, 1000)] public double Power { get; set; } = 50;

    /// <summary>Wind value, sign = direction (+ blows to the right).</summary>
    [Range(-100, 100)] public double Wind { get; set; } = 0;

    /// <summary>Gravitational acceleration g (u/s²) before the gravity multiplier.</summary>
    [Range(0, 1000)] public double Gravity { get; set; } = 9.81;

    // ---------------- Model switches ----------------

    public bool WindEnabled { get; set; } = true;
    public WindMode WindMode { get; set; } = WindMode.WindAcceleration;

    public DragType DragType { get; set; } = DragType.None;

    /// <summary>
    /// Air-resistance coefficient. Linear drag: b. Quadratic drag: Cd.
    /// (The calibration exposes this as "Air Resistance Coefficient".)
    /// </summary>
    [Range(0, 100)] public double DragCoefficient { get; set; } = 0.47;

    /// <summary>ρ, used by quadratic drag only.</summary>
    [Range(0, 100)] public double AirDensity { get; set; } = 1.225;

    /// <summary>Reference area A, used by quadratic drag only.</summary>
    [Range(0, 100)] public double CrossSectionArea { get; set; } = 0.07;

    [Range(0.001, 1000)] public double Mass { get; set; } = 1;

    // ---------------- Numerical settings ----------------

    public IntegratorType Integrator { get; set; } = IntegratorType.SemiImplicitEuler;

    /// <summary>Fixed time step in seconds.</summary>
    [Range(0.0001, 0.1)] public double DeltaTime { get; set; } = 0.016;

    /// <summary>Hard cap on simulated time — guarantees the loop terminates.</summary>
    [Range(0.1, 300)] public double MaxTime { get; set; } = 30;

    /// <summary>
    /// Maximum number of trajectory points returned to the client (the simulation itself always
    /// runs every step; points are evenly decimated). 0 = return every step.
    /// </summary>
    [Range(0, 200_000)] public int MaxTrajectoryPoints { get; set; } = 4000;

    // ---------------- World / geometry ----------------

    /// <summary>Length of the barrel; the bullet spawns at the muzzle tip.</summary>
    [Range(0, 5)] public double BarrelLength { get; set; } = 0.3;

    [Range(0.01, 10)] public double TargetWidth { get; set; } = 0.4;
    [Range(0.01, 10)] public double TargetHeight { get; set; } = 0.6;

    /// <summary>When false the target is ignored (used by the calibration to get ground landings).</summary>
    public bool TargetCollisionEnabled { get; set; } = true;

    /// <summary>Y of the flat ground.</summary>
    [Range(-1000, 1000)] public double GroundLevel { get; set; } = 0;

    /// <summary>|X| beyond which the shot is reported as out of bounds.</summary>
    [Range(1, 100_000)] public double WorldHalfWidth { get; set; } = 500;

    // ---------------- Reverse-engineering parameters ----------------

    public CalibrationParameters Calibration { get; set; } = new();

    public SimulationRequest Clone()
    {
        var c = (SimulationRequest)MemberwiseClone();
        c.Calibration = Calibration.Clone();
        return c;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var r in Calibration.Validate(validationContext)) yield return r;

        if (!Enum.IsDefined(WindMode)) yield return new ValidationResult("Unknown WindMode.", new[] { nameof(WindMode) });
        if (!Enum.IsDefined(DragType)) yield return new ValidationResult("Unknown DragType.", new[] { nameof(DragType) });
        if (!Enum.IsDefined(Integrator)) yield return new ValidationResult("Unknown Integrator.", new[] { nameof(Integrator) });
        if (MaxTime / DeltaTime > PhysicsLimits.MaxSteps)
            yield return new ValidationResult(
                $"MaxTime / DeltaTime exceeds the step limit ({PhysicsLimits.MaxSteps:N0}). Increase DeltaTime or reduce MaxTime.",
                new[] { nameof(MaxTime), nameof(DeltaTime) });
        if (Calibration.LaunchHeight < GroundLevel)
            yield return new ValidationResult("LaunchHeight must not be below GroundLevel.", new[] { "Calibration.LaunchHeight" });
    }
}

/// <summary>
/// Tunable parameters of the hypothesised game formula. Nothing here claims to be Gunny's real
/// algorithm — these are knobs to fit the model against observed shots.
/// <code>
///   effectiveAngle = Angle + AngleOffset
///   V0             = PowerToVelocity · VelocityMultiplier · (Power · PowerMultiplier) ^ PowerExponent
///   g_eff          = Gravity · GravityMultiplier
///   wind_eff       = Wind · WindMultiplier · (WindAccelerationScale | WindDriftScale)
///   launch point   = (0, LaunchHeight) + BarrelLength · (cos θ, sin θ)
/// </code>
/// Note: with PowerExponent = 1, VelocityMultiplier and PowerMultiplier are perfectly collinear
/// (only their product matters) — optimise only one of them at a time.
/// </summary>
public sealed class CalibrationParameters : IValidatableObject
{
    [Range(0.0001, 100)] public double VelocityMultiplier { get; set; } = 1;
    [Range(0.0001, 100)] public double PowerMultiplier { get; set; } = 1;
    [Range(0.05, 5)] public double PowerExponent { get; set; } = 1;
    [Range(0, 100)] public double GravityMultiplier { get; set; } = 1;
    [Range(-100, 100)] public double WindMultiplier { get; set; } = 1;
    [Range(-90, 90)] public double AngleOffset { get; set; } = 0;
    [Range(-1000, 1000)] public double LaunchHeight { get; set; } = 0.4;

    /// <summary>Base conversion from game power to speed (u/s per power point).</summary>
    [Range(0.0001, 100)] public double PowerToVelocity { get; set; } = 0.14;

    /// <summary>Wind → acceleration (u/s² per wind point), WindAcceleration mode.</summary>
    [Range(0, 100)] public double WindAccelerationScale { get; set; } = 0.1;

    /// <summary>Wind → air speed (u/s per wind point), ConstantWind mode.</summary>
    [Range(0, 100)] public double WindDriftScale { get; set; } = 0.05;

    public CalibrationParameters Clone() => (CalibrationParameters)MemberwiseClone();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var values = new[]
        {
            VelocityMultiplier, PowerMultiplier, PowerExponent, GravityMultiplier, WindMultiplier,
            AngleOffset, LaunchHeight, PowerToVelocity, WindAccelerationScale, WindDriftScale
        };
        if (values.Any(v => !double.IsFinite(v)))
            yield return new ValidationResult("Calibration parameters must be finite numbers.");
    }
}

/// <summary>Global safety limits for the simulation loop.</summary>
public static class PhysicsLimits
{
    /// <summary>Absolute maximum number of integration steps for one shot.</summary>
    public const int MaxSteps = 2_000_000;
}
