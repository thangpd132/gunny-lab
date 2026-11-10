using ProjectileSimulator.Models;
using ProjectileSimulator.Physics;

namespace ProjectileSimulator.Services;

/// <summary>
/// Converts a <see cref="SimulationRequest"/> (game-level inputs + calibration knobs) into the
/// physical initial conditions and the list of active force models.
/// This is THE place where the hypothesised "game formula" lives — change it here.
/// </summary>
public sealed class ShotSetup
{
    public required double EffectiveAngleDeg { get; init; }
    public required double InitialSpeed { get; init; }
    public required double EffectiveGravity { get; init; }
    public required Vector2D LaunchPosition { get; init; }
    public required Vector2D MuzzleVelocity { get; init; }
    public required Projectile Body { get; init; }
    public required bool IsGravityOnly { get; init; }
    public required IReadOnlyList<string> ActiveModelNames { get; init; }

    public static ShotSetup From(SimulationRequest r)
    {
        var c = r.Calibration;

        // 1) Angle: user angle + calibration offset, in radians. 0° → +X, 90° → +Y, 180° → −X.
        var angleDeg = r.Angle + c.AngleOffset;
        var theta = angleDeg * Math.PI / 180.0;
        var direction = Vector2D.FromAngle(theta);

        // 2) Initial speed: V0 = k · VelMul · (Power · PowMul)^PowExp
        var effectivePower = Math.Max(0, r.Power * c.PowerMultiplier);
        var v0 = c.PowerToVelocity * c.VelocityMultiplier * Math.Pow(effectivePower, c.PowerExponent);

        // 3) Vx = V0·cos(θ), Vy = V0·sin(θ)
        var muzzleVelocity = direction * v0;

        // 4) Launch point = pivot (0, LaunchHeight) + barrel tip offset.
        var launch = new Vector2D(0, c.LaunchHeight) + direction * r.BarrelLength;
        if (launch.Y < r.GroundLevel) launch = launch with { Y = r.GroundLevel }; // barrel pointing into the ground

        // 5) Force models (each independent, summed by the Projectile).
        var g = r.Gravity * c.GravityMultiplier;
        var models = new List<IAccelerationModel>(3) { new GravityModel(g) };
        var names = new List<string> { $"Gravity(g={g:0.###})" };

        var drift = Vector2D.Zero;
        var windActive = r.WindEnabled && r.Wind != 0 && c.WindMultiplier != 0;
        if (windActive)
        {
            if (r.WindMode == WindMode.WindAcceleration)
            {
                var wind = WindModel.Acceleration(r.Wind, c.WindMultiplier, c.WindAccelerationScale);
                if (wind.AccelerationX != 0)
                {
                    models.Add(wind);
                    names.Add($"WindAcceleration(ax={wind.AccelerationX:0.###})");
                }
            }
            else
            {
                drift = WindModel.DriftVelocity(r.Wind, c.WindMultiplier, c.WindDriftScale);
                if (drift.X != 0) names.Add($"ConstantWind(drift={drift.X:0.###} u/s)");
            }
        }

        var drag = DragModel.Create(r.DragType, r.DragCoefficient, r.AirDensity, r.CrossSectionArea);
        if (drag != null)
        {
            models.Add(drag);
            names.Add(drag switch
            {
                QuadraticDragModel q => $"QuadraticDrag(k={q.K:0.#####})",
                LinearDragModel l => $"LinearDrag(b={l.B:0.###})",
                _ => drag.Name
            });
        }

        return new ShotSetup
        {
            EffectiveAngleDeg = angleDeg,
            InitialSpeed = v0,
            EffectiveGravity = g,
            LaunchPosition = launch,
            MuzzleVelocity = muzzleVelocity,
            // Simplification for ConstantWind: the bullet is entrained by the moving air from the
            // first instant, so its ground velocity is muzzle velocity + wind drift.
            Body = new Projectile(r.Mass, models, drift),
            IsGravityOnly = models.Count == 1 && drift == Vector2D.Zero,
            ActiveModelNames = names
        };
    }
}
