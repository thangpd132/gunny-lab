using ProjectileSimulator.Models;

namespace ProjectileSimulator.Services;

/// <summary>
/// Pure, deterministic, thread-safe projectile simulator. Same request ⇒ same result.
/// </summary>
public interface IPhysicsEngine
{
    /// <exception cref="ArgumentException">The request contains invalid values.</exception>
    SimulationResult Simulate(SimulationRequest request);
}
