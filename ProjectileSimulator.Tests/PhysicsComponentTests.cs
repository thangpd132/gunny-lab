using ProjectileSimulator.Physics;

namespace ProjectileSimulator.Tests;

public class PhysicsComponentTests
{
    [Fact]
    public void Vector2D_Arithmetic()
    {
        var a = new Vector2D(3, 4);
        Assert.Equal(5, a.Length);
        Assert.Equal(new Vector2D(6, 8), a * 2);
        Assert.Equal(new Vector2D(1, 2), new Vector2D(4, 6) - a);
        Assert.Equal(11, Vector2D.Dot(a, new Vector2D(1, 2)));
        Assert.Equal(new Vector2D(1.5, 2), Vector2D.Lerp(Vector2D.Zero, a, 0.5));
    }

    [Fact]
    public void QuadraticDrag_OpposesVelocity_AndScalesWithSpeedSquared()
    {
        var drag = new QuadraticDragModel(1.2, 0.5, 0.1); // k = 0.03
        var s1 = new ProjectileState(0, Vector2D.Zero, new Vector2D(10, 0));
        var s2 = new ProjectileState(0, Vector2D.Zero, new Vector2D(20, 0));
        var a1 = drag.GetAcceleration(s1, 1);
        var a2 = drag.GetAcceleration(s2, 1);
        Assert.Equal(-0.03 * 100, a1.X, 12);
        Assert.Equal(4, a2.X / a1.X, 12);
        Assert.Equal(0, a1.Y);
    }

    [Fact]
    public void LinearDrag_IsProportionalToVelocity_AndInverseToMass()
    {
        var drag = new LinearDragModel(0.4);
        var s = new ProjectileState(0, Vector2D.Zero, new Vector2D(5, -5));
        Assert.Equal(new Vector2D(-2, 2), drag.GetAcceleration(s, 1));
        Assert.Equal(new Vector2D(-1, 1), drag.GetAcceleration(s, 2));
    }

    [Fact]
    public void Gravity_PointsDown()
    {
        Assert.Equal(new Vector2D(0, -9.81), new GravityModel(9.81).GetAcceleration(default, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GravityModel(-1));
    }

    [Fact]
    public void SegmentVsAabb_FindsEntryFraction()
    {
        var box = new Aabb(4, 0, 6, 2);
        Assert.Equal(0.4, CollisionDetector.SegmentVsAabb(new(0, 1), new(10, 1), box)!.Value, 12);
        Assert.Null(CollisionDetector.SegmentVsAabb(new(0, 3), new(10, 3), box));
        Assert.Null(CollisionDetector.SegmentVsAabb(new(0, 1), new(3, 1), box));
    }

    [Fact]
    public void SegmentVsGround_InterpolatesCrossing()
    {
        Assert.Equal(0.25, CollisionDetector.SegmentVsGround(new(0, 1), new(1, -3), 0)!.Value, 12);
        Assert.Null(CollisionDetector.SegmentVsGround(new(0, 1), new(1, 0.5), 0));
    }

    [Fact]
    public void Rk4_IsExactForConstantAcceleration()
    {
        var body = new Projectile(1, new IAccelerationModel[] { new GravityModel(10) }, Vector2D.Zero);
        var s = new ProjectileState(0, Vector2D.Zero, new Vector2D(1, 10));
        var rk = new RungeKutta4Integrator();
        for (var i = 0; i < 10; i++) s = rk.Step(body, s, 0.1);
        Assert.Equal(5, s.Position.Y, 10);  // y(1) = 10·1 − ½·10·1² = 5
        Assert.Equal(1, s.Position.X, 10);  // x(1) = 1·1
        Assert.Equal(0, s.Velocity.Y, 10);  // vy(1) = 10 − 10·1
    }
}
