namespace ProjectileSimulator.Models;

/// <summary>
/// One sample of the trajectory in physics space (Y up). Vx/Vy are the velocity relative to
/// the ground. Short property names keep the JSON payload compact.
/// </summary>
public readonly record struct TrajectoryPoint(double T, double X, double Y, double Vx, double Vy);
