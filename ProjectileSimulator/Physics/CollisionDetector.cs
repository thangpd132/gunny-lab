namespace ProjectileSimulator.Physics;

/// <summary>Axis-aligned rectangle in physics space (Y up).</summary>
public readonly record struct Aabb(double MinX, double MinY, double MaxX, double MaxY)
{
    public bool Contains(Vector2D p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
}

/// <summary>
/// Swept (continuous) collision tests between the segment travelled during one time step and
/// the world. Testing the segment instead of only the end point prevents "tunnelling" through a
/// thin target when the projectile is fast or the time step is large.
/// All functions return the fraction f ∈ [0, 1] along the segment p0 → p1, or null.
/// </summary>
public static class CollisionDetector
{
    /// <summary>
    /// First crossing of the horizontal ground line y = groundY while moving downward.
    /// Linear interpolation: f = (y0 − g) / (y0 − y1).
    /// </summary>
    public static double? SegmentVsGround(Vector2D p0, Vector2D p1, double groundY)
    {
        if (p1.Y > groundY) return null;     // still above ground
        if (p0.Y < groundY) return 0;        // already below (should not happen, be safe)
        var dy = p0.Y - p1.Y;
        if (dy <= 0) return 0;               // lying exactly on the ground
        return Math.Clamp((p0.Y - groundY) / dy, 0, 1);
    }

    /// <summary>Liang–Barsky segment/AABB clipping; returns entry fraction.</summary>
    public static double? SegmentVsAabb(Vector2D p0, Vector2D p1, Aabb box)
    {
        var d = p1 - p0;
        double tMin = 0, tMax = 1;

        if (!Clip(-d.X, p0.X - box.MinX, ref tMin, ref tMax)) return null;
        if (!Clip(d.X, box.MaxX - p0.X, ref tMin, ref tMax)) return null;
        if (!Clip(-d.Y, p0.Y - box.MinY, ref tMin, ref tMax)) return null;
        if (!Clip(d.Y, box.MaxY - p0.Y, ref tMin, ref tMax)) return null;
        return tMin;
    }

    private static bool Clip(double p, double q, ref double tMin, ref double tMax)
    {
        if (Math.Abs(p) < 1e-15) return q >= 0; // parallel: inside iff q ≥ 0
        var r = q / p;
        if (p < 0)
        {
            if (r > tMax) return false;
            if (r > tMin) tMin = r;
        }
        else
        {
            if (r < tMin) return false;
            if (r < tMax) tMax = r;
        }
        return true;
    }
}
