using System.ComponentModel.DataAnnotations;

namespace ProjectileSimulator.Models;

public enum OptimizationMethod
{
    /// <summary>Exhaustive search on a regular grid over the enabled parameters.</summary>
    GridSearch,

    /// <summary>Levenberg–Marquardt non-linear least squares (numerical Jacobian) from the current values.</summary>
    LeastSquares,

    /// <summary>Coarse grid search to find the basin, then Levenberg–Marquardt refinement.</summary>
    GridThenLeastSquares
}

/// <summary>Names of the parameters the optimizer may tune.</summary>
public enum TunableParameter
{
    VelocityMultiplier,
    PowerMultiplier,
    PowerExponent,
    GravityMultiplier,
    WindMultiplier,
    DragCoefficient,
    AngleOffset,
    LaunchHeight
}

public sealed class ParameterRange
{
    public TunableParameter Name { get; set; }
    public bool Enabled { get; set; } = true;
    public double Min { get; set; }
    public double Max { get; set; }

    /// <summary>Number of grid values (≥ 2) for grid search.</summary>
    [Range(2, 1000)] public int Steps { get; set; } = 11;
}

public sealed class OptimizationRequest : IValidatableObject
{
    [Required] public SimulationRequest Base { get; set; } = new();
    [Required, MinLength(1), MaxLength(500)] public List<ExperimentData> Experiments { get; set; } = new();
    public OptimizationMethod Method { get; set; } = OptimizationMethod.GridThenLeastSquares;

    /// <summary>Weight w of flight-time residuals (u per second), see <see cref="EvaluationRequest.FlightTimeWeight"/>.</summary>
    [Range(0, 1000)] public double FlightTimeWeight { get; set; } = 5;
    [Required, MinLength(1)] public List<ParameterRange> Parameters { get; set; } = new();
    [Range(1, 1000)] public int MaxIterations { get; set; } = 100;

    /// <summary>Upper bound of grid evaluations (each evaluation = one simulation per experiment).</summary>
    [Range(1, 200_000)] public int MaxGridEvaluations { get; set; } = 20_000;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var enabled = Parameters.Where(p => p.Enabled).ToList();
        if (enabled.Count == 0)
            yield return new ValidationResult("Enable at least one parameter to optimise.", new[] { nameof(Parameters) });
        if (enabled.GroupBy(p => p.Name).Any(g => g.Count() > 1))
            yield return new ValidationResult("Each parameter may appear only once.", new[] { nameof(Parameters) });
        foreach (var p in enabled)
        {
            if (!double.IsFinite(p.Min) || !double.IsFinite(p.Max) || p.Min > p.Max)
                yield return new ValidationResult($"Invalid range for {p.Name}: Min must be ≤ Max.", new[] { nameof(Parameters) });
            if (p.Name is TunableParameter.VelocityMultiplier or TunableParameter.PowerMultiplier && p.Min <= 0)
                yield return new ValidationResult($"{p.Name} must have Min > 0.", new[] { nameof(Parameters) });
            if (p.Name == TunableParameter.PowerExponent && p.Min < 0.05)
                yield return new ValidationResult("PowerExponent must have Min ≥ 0.05.", new[] { nameof(Parameters) });
            if (p.Name is TunableParameter.GravityMultiplier or TunableParameter.DragCoefficient && p.Min < 0)
                yield return new ValidationResult($"{p.Name} must have Min ≥ 0.", new[] { nameof(Parameters) });
        }
    }
}

public sealed class OptimizationResult
{
    public OptimizationMethod Method { get; init; }
    public Dictionary<string, double> InitialParameters { get; init; } = new();
    public Dictionary<string, double> BestParameters { get; init; } = new();

    /// <summary>The base request with the best parameters applied (ready to use).</summary>
    public SimulationRequest BestRequest { get; init; } = new();

    public ErrorMetrics InitialMetrics { get; init; } = new();
    public ErrorMetrics BestMetrics { get; init; } = new();
    public List<ExperimentPrediction> Predictions { get; init; } = new();
    public int Evaluations { get; init; }
    public int Iterations { get; init; }

    /// <summary>√(objective / N) after each stage / iteration (equals landing RMSE when no flight times are given).</summary>
    public List<double> RmseHistory { get; init; } = new();
    public List<string> Notes { get; init; } = new();
    public double ElapsedMs { get; set; }
}
