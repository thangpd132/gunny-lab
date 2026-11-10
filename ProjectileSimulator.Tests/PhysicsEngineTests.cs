using ProjectileSimulator.Models;
using ProjectileSimulator.Physics;
using ProjectileSimulator.Services;

namespace ProjectileSimulator.Tests;

public class PhysicsEngineTests
{
    private const double G = 9.81;
    private readonly PhysicsEngine _engine = new();

    /// <summary>Gravity only, launched from (0,0), no barrel, no target — matches the textbook formulas.</summary>
    private static SimulationRequest Basic(double angle = 45, double power = 50,
        IntegratorType integrator = IntegratorType.RungeKutta4, double dt = 0.001) => new()
    {
        Angle = angle,
        Power = power,
        Gravity = G,
        WindEnabled = false,
        DragType = DragType.None,
        BarrelLength = 0,
        TargetCollisionEnabled = false,
        Integrator = integrator,
        DeltaTime = dt,
        MaxTrajectoryPoints = 0,
        Calibration = new CalibrationParameters { LaunchHeight = 0, PowerToVelocity = 0.14 }
    };

    private static double V0(double power) => 0.14 * power;

    // ---------------------------------------------------------------- 1. 45°

    [Fact]
    public void Angle45_NoWindNoDrag_MatchesAnalyticRange_RK4()
    {
        var r = _engine.Simulate(Basic(45, 50));
        var v = V0(50);

        Assert.Equal(SimulationStatus.HitGround, r.Status);
        Assert.Equal(v * v / G, r.HorizontalRange, 6);                      // R = v²·sin(2θ)/g
        Assert.Equal(2 * v * Math.Sin(Math.PI / 4) / G, r.FlightTime, 6);   // T = 2v·sinθ/g
        Assert.Equal(v * v * 0.5 / (2 * G), r.MaxHeight, 4);                 // H = v²·sin²θ/(2g)
        Assert.Equal(r.AnalyticRange!.Value, r.HorizontalRange, 6);
    }

    [Theory]
    [InlineData(0.001, 0.005)]  // 0.5 % error with a 1 ms step
    [InlineData(0.016, 0.03)]   // ≤ 3 % with the default 60 Hz step
    public void Angle45_SemiImplicitEuler_ConvergesToAnalytic(double dt, double relTolerance)
    {
        var r = _engine.Simulate(Basic(45, 50, IntegratorType.SemiImplicitEuler, dt));
        var expected = V0(50) * V0(50) / G;
        Assert.InRange(r.HorizontalRange, expected * (1 - relTolerance), expected * (1 + relTolerance));
    }

    [Fact]
    public void SmallerTimeStep_ReducesSemiImplicitEulerError()
    {
        var expected = V0(50) * V0(50) / G;
        var coarse = Math.Abs(_engine.Simulate(Basic(45, 50, IntegratorType.SemiImplicitEuler, 0.02)).HorizontalRange - expected);
        var fine = Math.Abs(_engine.Simulate(Basic(45, 50, IntegratorType.SemiImplicitEuler, 0.002)).HorizontalRange - expected);
        Assert.True(fine < coarse, $"fine={fine} coarse={coarse}");
    }

    // ---------------------------------------------------------------- 2. 90°

    [Fact]
    public void Angle90_GoesStraightUpAndFallsBackAtLaunchX()
    {
        var r = _engine.Simulate(Basic(90, 60));
        var v = V0(60);

        Assert.Equal(SimulationStatus.HitGround, r.Status);
        Assert.InRange(r.LandingX, -1e-9, 1e-9);
        Assert.Equal(v * v / (2 * G), r.MaxHeight, 4);     // H = v²/(2g)
        Assert.Equal(2 * v / G, r.FlightTime, 6);          // T = 2v/g
        Assert.All(r.Trajectory, p => Assert.InRange(p.X, -1e-9, 1e-9));
    }

    // ---------------------------------------------------------------- 3. 0°

    [Fact]
    public void Angle0_FromHeight_FallsAsHorizontalThrow()
    {
        var req = Basic(0, 50);
        req.Calibration.LaunchHeight = 2;
        var r = _engine.Simulate(req);
        var t = Math.Sqrt(2 * 2 / G);                      // h = ½·g·t²

        Assert.Equal(SimulationStatus.HitGround, r.Status);
        Assert.Equal(t, r.FlightTime, 6);
        Assert.Equal(V0(50) * t, r.LandingX, 6);
        Assert.Equal(2, r.MaxHeight, 9);                   // never rises above the launch height
        Assert.Equal(0, r.LandingY, 9);
    }

    [Fact]
    public void Angle0_FromGround_LandsImmediately()
    {
        var r = _engine.Simulate(Basic(0, 50, IntegratorType.SemiImplicitEuler, 0.016));
        Assert.Equal(SimulationStatus.HitGround, r.Status);
        Assert.InRange(r.FlightTime, 0, 0.016);
        Assert.Equal(1, r.Steps);
    }

    [Fact]
    public void Angle180_IsMirrorImageOfAngle0()
    {
        var a = Basic(30, 50); var b = Basic(150, 50);
        var ra = _engine.Simulate(a); var rb = _engine.Simulate(b);
        Assert.Equal(ra.LandingX, -rb.LandingX, 9);
        Assert.Equal(ra.FlightTime, rb.FlightTime, 9);
        Assert.True(rb.HorizontalRange < 0);
    }

    // ---------------------------------------------------------------- 4. Determinism

    [Fact]
    public void SameInput_GivesIdenticalResult()
    {
        var req = Basic(52, 73, IntegratorType.SemiImplicitEuler, 0.016);
        req.WindEnabled = true; req.Wind = -7.5; req.DragType = DragType.Quadratic;

        var r1 = _engine.Simulate(req);
        var r2 = _engine.Simulate(req.Clone());

        Assert.Equal(r1.Steps, r2.Steps);
        Assert.Equal(r1.LandingX, r2.LandingX);
        Assert.Equal(r1.FlightTime, r2.FlightTime);
        Assert.Equal(r1.Trajectory, r2.Trajectory);
    }

    // ---------------------------------------------------------------- 5. Power

    [Fact]
    public void IncreasingPower_IncreasesRange()
    {
        var previous = double.NegativeInfinity;
        for (var power = 10; power <= 100; power += 10)
        {
            var range = _engine.Simulate(Basic(45, power)).HorizontalRange;
            Assert.True(range > previous, $"power {power}: {range} <= {previous}");
            previous = range;
        }

        // Without drag the range scales with V0² — doubling power quadruples range.
        var r1 = _engine.Simulate(Basic(45, 30)).HorizontalRange;
        var r2 = _engine.Simulate(Basic(45, 60)).HorizontalRange;
        Assert.Equal(4, r2 / r1, 6);
    }

    // ---------------------------------------------------------------- 6. Gravity

    [Fact]
    public void IncreasingGravity_ReducesFlightTimeHeightAndRange()
    {
        var weak = Basic(60, 70); weak.Gravity = 5;
        var strong = Basic(60, 70); strong.Gravity = 20;
        var rw = _engine.Simulate(weak);
        var rs = _engine.Simulate(strong);

        Assert.True(rs.FlightTime < rw.FlightTime);
        Assert.True(rs.MaxHeight < rw.MaxHeight);
        Assert.True(rs.HorizontalRange < rw.HorizontalRange);
        // From ground level, T, H and R are all ∝ 1/g.
        Assert.Equal(4, rw.FlightTime / rs.FlightTime, 5);
        Assert.Equal(4, rw.HorizontalRange / rs.HorizontalRange, 5);
    }

    [Fact]
    public void GravityMultiplier_IsEquivalentToChangingGravity()
    {
        var a = Basic(45, 50); a.Gravity = 19.62;
        var b = Basic(45, 50); b.Calibration.GravityMultiplier = 2;
        Assert.Equal(_engine.Simulate(a).LandingX, _engine.Simulate(b).LandingX, 9);
    }

    // ---------------------------------------------------------------- 7. Wind direction

    [Theory]
    [InlineData(WindMode.WindAcceleration)]
    [InlineData(WindMode.ConstantWind)]
    public void Wind_PushesTrajectoryInItsDirection(WindMode mode)
    {
        SimulationRequest With(double wind)
        {
            var r = Basic(45, 50);
            r.WindEnabled = true; r.WindMode = mode; r.Wind = wind;
            return r;
        }

        var calm = _engine.Simulate(With(0)).LandingX;
        var tail = _engine.Simulate(With(+10)).LandingX;
        var head = _engine.Simulate(With(-10)).LandingX;

        Assert.True(tail > calm, $"tail {tail} <= calm {calm}");
        Assert.True(head < calm, $"head {head} >= calm {calm}");
        // Without drag the wind effect is symmetric.
        Assert.Equal(tail - calm, calm - head, 6);
    }

    [Fact]
    public void WindAcceleration_MatchesAnalyticDisplacement()
    {
        // Vertical shot: x(T) = ½·a·T², with a = wind · scale, T = 2v/g
        var r = Basic(90, 50);
        r.WindEnabled = true; r.WindMode = WindMode.WindAcceleration; r.Wind = 10;
        var res = _engine.Simulate(r);
        var a = 10 * r.Calibration.WindAccelerationScale;
        var t = 2 * V0(50) / G;
        Assert.Equal(0.5 * a * t * t, res.LandingX, 6);
    }

    [Fact]
    public void WindDisabled_IgnoresWindValue()
    {
        var a = Basic(); a.Wind = 15; a.WindEnabled = false;
        Assert.Equal(_engine.Simulate(Basic()).LandingX, _engine.Simulate(a).LandingX);
    }

    // ---------------------------------------------------------------- 8. Ground collision

    [Fact]
    public void GroundCollision_IsDetectedAndInterpolatedExactlyOnGround()
    {
        var r = _engine.Simulate(Basic(45, 50, IntegratorType.SemiImplicitEuler, 0.05));
        Assert.Equal(SimulationStatus.HitGround, r.Status);
        Assert.Equal(0, r.LandingY, 12);
        Assert.Equal(0, r.Trajectory[^1].Y, 12);
        Assert.All(r.Trajectory, p => Assert.True(p.Y >= -1e-12));
        Assert.True(r.Trajectory[^1].Vy < 0); // moving downward at impact
    }

    [Fact]
    public void GroundCollision_WorksWithCustomGroundLevel()
    {
        var req = Basic(45, 50); req.GroundLevel = -3; req.Calibration.LaunchHeight = 0;
        var r = _engine.Simulate(req);
        Assert.Equal(-3, r.LandingY, 9);
        Assert.True(r.HorizontalRange > V0(50) * V0(50) / G); // falling further ⇒ landing further
    }

    // ---------------------------------------------------------------- Target / miss handling

    [Fact]
    public void TargetAtLandingPoint_IsHit()
    {
        var req = Basic(45, 50);
        req.TargetCollisionEnabled = true;
        req.Distance = V0(50) * V0(50) / G; // exactly the range
        var r = _engine.Simulate(req);
        Assert.Equal(SimulationStatus.HitTarget, r.Status);
        Assert.True(r.HitTarget);
        Assert.True(r.FlightTime < 2 * V0(50) * Math.Sin(Math.PI / 4) / G); // hit before reaching the ground
    }

    [Fact]
    public void TargetTooFar_ReportsMiss()
    {
        var req = Basic(45, 50);
        req.TargetCollisionEnabled = true;
        req.Distance = 9;
        var r = _engine.Simulate(req);
        Assert.Equal(SimulationStatus.HitGround, r.Status);
        Assert.False(r.HitTarget);
        Assert.True(r.MissDistance < 0); // fell short
    }

    [Fact]
    public void SweptCollision_DoesNotTunnelThroughThinTargetWithLargeStep()
    {
        var req = Basic(10, 100, IntegratorType.SemiImplicitEuler, 0.1);
        req.Calibration.LaunchHeight = 0.2;
        req.TargetCollisionEnabled = true;
        req.Distance = 2;
        req.TargetWidth = 0.02;
        req.TargetHeight = 3;
        var r = _engine.Simulate(req);
        Assert.Equal(SimulationStatus.HitTarget, r.Status);
        Assert.InRange(r.LandingX, 2 - 0.011, 2 + 0.011);
    }

    // ---------------------------------------------------------------- Termination / safety

    [Fact]
    public void ZeroGravity_TerminatesWithTimeout()
    {
        var req = Basic(90, 50, IntegratorType.SemiImplicitEuler, 0.016);
        req.Gravity = 0; req.MaxTime = 5;
        var r = _engine.Simulate(req);
        Assert.Equal(SimulationStatus.Timeout, r.Status);
        Assert.Equal((int)Math.Ceiling(5 / 0.016), r.Steps);
    }

    [Fact]
    public void LeavingTheWorld_ReportsOutOfBounds()
    {
        var req = Basic(10, 100, IntegratorType.SemiImplicitEuler, 0.016);
        req.Gravity = 0.01; req.WorldHalfWidth = 20;
        var r = _engine.Simulate(req);
        Assert.Equal(SimulationStatus.OutOfBounds, r.Status);
    }

    [Fact]
    public void TrajectoryPointLimit_IsRespected_AndEndpointsKept()
    {
        var req = Basic(80, 100, IntegratorType.SemiImplicitEuler, 0.0005);
        req.MaxTrajectoryPoints = 300;
        var r = _engine.Simulate(req);
        Assert.InRange(r.Trajectory.Count, 2, 2 * 300 + 2);
        Assert.Equal(0, r.Trajectory[0].T);
        Assert.Equal(r.FlightTime, r.Trajectory[^1].T, 12);
        Assert.True(r.Steps > 300);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(-1)]
    public void InvalidAngle_Throws(double angle)
    {
        var req = Basic(); req.Angle = angle;
        Assert.Throws<ArgumentException>(() => _engine.Simulate(req));
    }

    [Fact]
    public void InvalidTimeStep_Throws()
    {
        var req = Basic(); req.DeltaTime = 0;
        Assert.Throws<ArgumentException>(() => _engine.Simulate(req));
    }

    // ---------------------------------------------------------------- Drag

    [Theory]
    [InlineData(DragType.Linear, 0.2)]
    [InlineData(DragType.Quadratic, 0.47)]
    public void Drag_ReducesRangeAndHeight(DragType type, double coefficient)
    {
        var free = _engine.Simulate(Basic(45, 80));
        var req = Basic(45, 80); req.DragType = type; req.DragCoefficient = coefficient;
        var dragged = _engine.Simulate(req);

        Assert.True(dragged.HorizontalRange < free.HorizontalRange);
        Assert.True(dragged.MaxHeight < free.MaxHeight);
        Assert.Null(dragged.AnalyticRange);
    }

    [Fact]
    public void LinearDrag_MatchesAnalyticSolution()
    {
        // With linear drag (k = b/m) and no gravity along x: x(t) = vx0/k · (1 − e^(−k t))
        var req = Basic(0, 50);
        req.Calibration.LaunchHeight = 3;
        req.DragType = DragType.Linear; req.DragCoefficient = 0.5;
        var r = _engine.Simulate(req);
        var k = 0.5;
        var expectedX = V0(50) / k * (1 - Math.Exp(-k * r.FlightTime));
        Assert.Equal(expectedX, r.LandingX, 5);
    }

    // ---------------------------------------------------------------- Calibration formula

    [Fact]
    public void AngleOffset_And_Multipliers_AreApplied()
    {
        var req = Basic(40, 50);
        req.Calibration.AngleOffset = 5;
        req.Calibration.VelocityMultiplier = 1.5;
        var r = _engine.Simulate(req);
        Assert.Equal(45, r.EffectiveAngle, 9);
        Assert.Equal(0.14 * 1.5 * 50, r.InitialSpeed, 9);
        var v = 0.14 * 1.5 * 50;
        Assert.Equal(v * v / G, r.HorizontalRange, 6);
    }

    [Fact]
    public void PowerExponent_ChangesVelocityCurve()
    {
        var req = Basic(45, 64);
        req.Calibration.PowerExponent = 0.5;
        Assert.Equal(0.14 * 8, _engine.Simulate(req).InitialSpeed, 9);
    }
}
