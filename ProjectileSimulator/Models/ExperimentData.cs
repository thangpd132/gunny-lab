using System.ComponentModel.DataAnnotations;

namespace ProjectileSimulator.Models;

/// <summary>
/// One shot observed in the real game, entered by the user. Coordinates are in distance units,
/// X measured from the shooter (x = 0), Y measured from the shooter's ground level.
/// The application never invents these values.
/// </summary>
public sealed class ExperimentData
{
    public string? Label { get; set; }
    [Range(0.1, 100)] public double Distance { get; set; } = 5;
    [Range(0, 180)] public double Angle { get; set; }
    [Range(0, 1000)] public double Power { get; set; }
    [Range(-100, 100)] public double Wind { get; set; }
    [Range(-10_000, 10_000)] public double ObservedLandingX { get; set; }
    [Range(-1000, 1000)] public double ObservedLandingY { get; set; }

    /// <summary>
    /// Optional measured flight time (s), e.g. by counting video frames. Landing position alone
    /// only identifies combinations like V0²/g; adding the flight time lets the optimizer separate
    /// velocity from gravity.
    /// </summary>
    [Range(0, 300)] public double? ObservedFlightTime { get; set; }
}

/// <summary>Prediction of the model for one experiment.</summary>
public sealed class ExperimentPrediction
{
    public int Index { get; init; }
    public string? Label { get; init; }
    public double Distance { get; init; }
    public double Angle { get; init; }
    public double Power { get; init; }
    public double Wind { get; init; }
    public double PredictedLandingX { get; init; }
    public double PredictedLandingY { get; init; }
    public double ActualLandingX { get; init; }
    public double ActualLandingY { get; init; }

    /// <summary>Signed horizontal error (predicted − actual).</summary>
    public double ErrorX { get; init; }
    public double ErrorY { get; init; }

    /// <summary>Euclidean distance between predicted and actual landing points.</summary>
    public double ErrorDistance { get; init; }

    /// <summary>
    /// True if the predicted trajectory crosses the observed landing height while descending.
    /// When false the "landing" is the closest approach of the trajectory to the observed point.
    /// </summary>
    public bool ReachedObservedHeight { get; init; }

    /// <summary>Predicted time at the predicted landing point.</summary>
    public double FlightTime { get; init; }
    public double? ActualFlightTime { get; init; }

    /// <summary>Predicted − observed flight time (0 when no time was observed).</summary>
    public double ErrorTime { get; init; }
}

public sealed class ErrorMetrics
{
    public int Count { get; init; }
    public double MeanAbsoluteError { get; init; }
    public double RootMeanSquareError { get; init; }
    public double MaxError { get; init; }
    public double MeanSignedErrorX { get; init; }

    /// <summary>RMSE of flight time over experiments that have an observed time (null if none).</summary>
    public double? FlightTimeRmse { get; init; }

    /// <summary>Σ ErrorDistance² + Σ (w·ErrorTime)² — the objective minimised by the optimizer.</summary>
    public double SumSquaredError { get; init; }
}

public sealed class EvaluationRequest
{
    [Required] public SimulationRequest Base { get; set; } = new();
    [Required, MinLength(1), MaxLength(500)] public List<ExperimentData> Experiments { get; set; } = new();

    /// <summary>Weight w converting a flight-time error (s) into distance units in the objective.</summary>
    [Range(0, 1000)] public double FlightTimeWeight { get; set; } = 5;
}

public sealed class EvaluationResult
{
    public List<ExperimentPrediction> Predictions { get; init; } = new();
    public ErrorMetrics Metrics { get; init; } = new();
}
