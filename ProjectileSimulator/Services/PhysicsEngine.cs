using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using ProjectileSimulator.Models;
using ProjectileSimulator.Physics;

namespace ProjectileSimulator.Services;

/// <summary>
/// Fixed-time-step projectile simulator.
/// <para>Each step:</para>
/// <list type="number">
///   <item>compute the acceleration from all active models (gravity, wind, drag),</item>
///   <item>update the velocity,</item>
///   <item>update the position (integrator-dependent order),</item>
///   <item>sweep-test the segment travelled against the target and the ground,</item>
///   <item>store the point in the trajectory.</item>
/// </list>
/// The loop is bounded by MaxTime/DeltaTime (and <see cref="PhysicsLimits.MaxSteps"/>), by the
/// world bounds and by a NaN/∞ guard, so it can never run forever.
/// </summary>
public sealed class PhysicsEngine : IPhysicsEngine
{
    public SimulationResult Simulate(SimulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var sw = Stopwatch.StartNew();
        var setup = ShotSetup.From(request);
        var body = setup.Body;
        var integrator = IntegratorFactory.Get(request.Integrator);
        var dt = request.DeltaTime;
        var groundY = request.GroundLevel;

        var target = new Aabb(
            request.Distance - request.TargetWidth / 2, groundY,
            request.Distance + request.TargetWidth / 2, groundY + request.TargetHeight);

        var stepLimit = (int)Math.Min(PhysicsLimits.MaxSteps, Math.Ceiling(request.MaxTime / dt));
        var ceiling = groundY + request.WorldHalfWidth * 100;

        var recorder = new TrajectoryRecorder(request.MaxTrajectoryPoints);
        var state = new ProjectileState(0, setup.LaunchPosition, setup.MuzzleVelocity);
        recorder.Add(ToPoint(body, state), force: true);

        var maxY = state.Position.Y;
        var maxYx = state.Position.X;
        var steps = 0;
        SimulationStatus? status = null;

        while (steps < stepLimit)
        {
            var next = integrator.Step(body, state, dt);
            steps++;

            if (!next.Position.IsFinite || !next.Velocity.IsFinite)
            {
                status = SimulationStatus.OutOfBounds; // numerical blow-up: stop safely
                break;
            }

            // Continuous collision: test the whole segment state → next.
            var fGround = CollisionDetector.SegmentVsGround(state.Position, next.Position, groundY);
            var fTarget = request.TargetCollisionEnabled
                ? CollisionDetector.SegmentVsAabb(state.Position, next.Position, target)
                : null;

            if (fGround.HasValue || fTarget.HasValue)
            {
                var hitTarget = fTarget.HasValue && (!fGround.HasValue || fTarget.Value <= fGround.Value);
                var f = hitTarget ? fTarget!.Value : fGround!.Value;
                state = !hitTarget && request.Integrator == IntegratorType.RungeKutta4
                    ? HermiteGroundCrossing(body, state, next, groundY, f)
                    : Interpolate(state, next, f);
                if (hitTarget) state = state with { Position = ClampInto(state.Position, target) };
                else state = state with { Position = state.Position with { Y = groundY } };
                status = hitTarget ? SimulationStatus.HitTarget : SimulationStatus.HitGround;
                TrackMax(state.Position, ref maxY, ref maxYx);
                recorder.Add(ToPoint(body, state), force: true);
                break;
            }

            state = next;
            TrackMax(state.Position, ref maxY, ref maxYx);
            recorder.Add(ToPoint(body, state));

            if (Math.Abs(state.Position.X) > request.WorldHalfWidth || state.Position.Y > ceiling)
            {
                status = SimulationStatus.OutOfBounds;
                break;
            }
        }

        var finalStatus = status ?? SimulationStatus.Timeout;
        recorder.Add(ToPoint(body, state), force: true); // make sure the last point is present (no-op if duplicate)

        var groundVelocity = body.GroundVelocity(state);
        var (analyticRange, analyticTime) = setup.IsGravityOnly
            ? AnalyticFlatGround(setup.MuzzleVelocity, setup.LaunchPosition.Y - groundY, setup.EffectiveGravity)
            : (null, null);

        sw.Stop();
        return new SimulationResult
        {
            Status = finalStatus,
            CollisionStatus = Describe(finalStatus),
            Angle = request.Angle,
            EffectiveAngle = setup.EffectiveAngleDeg,
            Power = request.Power,
            Distance = request.Distance,
            Wind = request.Wind,
            InitialSpeed = setup.InitialSpeed,
            EffectiveGravity = setup.EffectiveGravity,
            LaunchX = setup.LaunchPosition.X,
            LaunchY = setup.LaunchPosition.Y,
            FlightTime = state.Time,
            MaxHeight = maxY - groundY,
            MaxHeightX = maxYx,
            LandingX = state.Position.X,
            LandingY = state.Position.Y,
            HorizontalRange = state.Position.X - setup.LaunchPosition.X,
            MissDistance = state.Position.X - request.Distance,
            ImpactSpeed = groundVelocity.Length,
            ImpactAngle = Math.Atan2(groundVelocity.Y, groundVelocity.X) * 180 / Math.PI,
            Steps = steps,
            TargetX = request.Distance,
            AnalyticRange = analyticRange,
            AnalyticFlightTime = analyticTime,
            Integrator = request.Integrator.ToString(),
            DeltaTime = dt,
            ActiveModels = setup.ActiveModelNames,
            Trajectory = recorder.Points,
            ComputeTimeMs = sw.Elapsed.TotalMilliseconds
        };
    }

    /// <summary>
    /// Closed-form solution over flat ground (gravity only) from height h above the ground:
    /// <code>
    ///   y(t) = h + Vy·t − ½·g·t² = 0  ⇒  T = (Vy + √(Vy² + 2·g·h)) / g
    ///   R    = Vx · T
    /// </code>
    /// </summary>
    internal static (double? range, double? time) AnalyticFlatGround(Vector2D v, double h, double g)
    {
        if (g <= 0) return (null, null);
        var disc = v.Y * v.Y + 2 * g * Math.Max(0, h);
        var t = (v.Y + Math.Sqrt(disc)) / g;
        return (v.X * t, t);
    }

    private static void Validate(SimulationRequest request)
    {
        var errors = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true);
        ok &= Validator.TryValidateObject(request.Calibration, new ValidationContext(request.Calibration), errors, validateAllProperties: true);
        if (!ok)
            throw new ArgumentException("Invalid simulation request: " + string.Join("; ", errors.Select(e => e.ErrorMessage)));
    }

    private static ProjectileState Interpolate(in ProjectileState a, in ProjectileState b, double f) =>
        new(a.Time + (b.Time - a.Time) * f,
            Vector2D.Lerp(a.Position, b.Position, f),
            Vector2D.Lerp(a.Velocity, b.Velocity, f));

    /// <summary>
    /// For RK4 the step end points are (near) exact samples of the continuous solution, so the
    /// impact point is refined with a cubic Hermite curve through (x0, v0) and (x1, v1) — exact
    /// for parabolic motion — instead of a straight chord. Root found by bisection.
    /// (Euler integrators keep the linear chord: their discrete path IS the simulated path.)
    /// </summary>
    private static ProjectileState HermiteGroundCrossing(Projectile body, ProjectileState a, ProjectileState b, double groundY, double fLinear)
    {
        var dt = b.Time - a.Time;
        var va = body.GroundVelocity(a);
        var vb = body.GroundVelocity(b);

        Vector2D H(double s)
        {
            double s2 = s * s, s3 = s2 * s;
            return a.Position * (2 * s3 - 3 * s2 + 1) + va * (dt * (s3 - 2 * s2 + s))
                 + b.Position * (-2 * s3 + 3 * s2) + vb * (dt * (s3 - s2));
        }

        if (a.Position.Y < groundY || b.Position.Y > groundY) return Interpolate(a, b, fLinear);
        double lo = 0, hi = 1;
        for (var i = 0; i < 60; i++)
        {
            var mid = 0.5 * (lo + hi);
            if (H(mid).Y > groundY) lo = mid; else hi = mid;
        }
        var f = 0.5 * (lo + hi);
        var p = H(f);
        return new ProjectileState(a.Time + dt * f, p, Vector2D.Lerp(a.Velocity, b.Velocity, f));
    }

    private static Vector2D ClampInto(Vector2D p, Aabb box) =>
        new(Math.Clamp(p.X, box.MinX, box.MaxX), Math.Clamp(p.Y, box.MinY, box.MaxY));

    private static void TrackMax(Vector2D p, ref double maxY, ref double maxYx)
    {
        if (p.Y > maxY) { maxY = p.Y; maxYx = p.X; }
    }

    private static TrajectoryPoint ToPoint(Projectile body, in ProjectileState s)
    {
        var v = body.GroundVelocity(s);
        return new TrajectoryPoint(s.Time, s.Position.X, s.Position.Y, v.X, v.Y);
    }

    private static string Describe(SimulationStatus s) => s switch
    {
        SimulationStatus.HitTarget => "Hit target",
        SimulationStatus.HitGround => "Missed – hit the ground",
        SimulationStatus.OutOfBounds => "Out of bounds",
        SimulationStatus.Timeout => "Timeout – no collision within MaxTime",
        _ => s.ToString()
    };

    /// <summary>
    /// Stores trajectory points with bounded memory: when the buffer exceeds 2·max points,
    /// every other point is dropped and the sampling stride doubles. The first and last points
    /// are always kept, so the shape and the end state are exact.
    /// </summary>
    private sealed class TrajectoryRecorder
    {
        private readonly int _max;
        private readonly List<TrajectoryPoint> _points = new();
        private int _stride = 1;
        private int _counter;

        public TrajectoryRecorder(int maxPoints) => _max = maxPoints;

        public List<TrajectoryPoint> Points => _points;

        public void Add(TrajectoryPoint p, bool force = false)
        {
            if (_points.Count > 0 && _points[^1].T == p.T) { _points[^1] = p; return; }
            _counter++;
            if (!force && _max > 0 && _counter % _stride != 0) return;
            _points.Add(p);
            if (_max > 0 && _points.Count > 2 * _max) Compact();
        }

        private void Compact()
        {
            var last = _points[^1];
            var kept = new List<TrajectoryPoint>(_points.Count / 2 + 2);
            for (var i = 0; i < _points.Count; i += 2) kept.Add(_points[i]);
            if (kept[^1].T != last.T) kept.Add(last);
            _points.Clear();
            _points.AddRange(kept);
            _stride *= 2;
        }
    }
}
