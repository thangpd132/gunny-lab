using ProjectileSimulator.Models;

namespace ProjectileSimulator.Services;

/// <summary>
/// Compares the model against shots observed in the real game.
/// <para>
/// Definition of the predicted landing point for an experiment with observed point (Xo, Yo):
/// the first point where the predicted trajectory crosses the horizontal line y = Yo while
/// descending (linear interpolation between steps). If the trajectory never gets that high, the
/// closest point of the trajectory to (Xo, Yo) is used instead. With Yo = 0 this is simply the
/// landing point on flat ground. The target hit box is ignored during calibration.
/// </para>
/// </summary>
public sealed class ReverseEngineeringService
{
    private readonly IPhysicsEngine _engine;

    public ReverseEngineeringService(IPhysicsEngine engine) => _engine = engine;

    public const double DefaultFlightTimeWeight = 5;

    public EvaluationResult Evaluate(SimulationRequest baseRequest, IReadOnlyList<ExperimentData> experiments,
        double flightTimeWeight = DefaultFlightTimeWeight)
    {
        var predictions = new ExperimentPrediction[experiments.Count];
        for (var i = 0; i < experiments.Count; i++)
            predictions[i] = Predict(baseRequest, experiments[i], i);
        return new EvaluationResult { Predictions = predictions.ToList(), Metrics = ComputeMetrics(predictions, flightTimeWeight) };
    }

    /// <summary>
    /// Residual vector used by least squares: three components (ex, ey, w·et) per experiment, so
    /// that Σ r² = Σ ErrorDistance² + Σ (w·ErrorTime)². et = 0 when no flight time was observed.
    /// </summary>
    public double[] Residuals(SimulationRequest baseRequest, IReadOnlyList<ExperimentData> experiments,
        double flightTimeWeight = DefaultFlightTimeWeight)
    {
        var r = new double[experiments.Count * 3];
        for (var i = 0; i < experiments.Count; i++)
        {
            var p = Predict(baseRequest, experiments[i], i);
            r[3 * i] = p.ErrorX;
            r[3 * i + 1] = p.ErrorY;
            r[3 * i + 2] = flightTimeWeight * p.ErrorTime;
        }
        return r;
    }

    public ExperimentPrediction Predict(SimulationRequest baseRequest, ExperimentData e, int index)
    {
        var req = baseRequest.Clone();
        req.Distance = e.Distance;
        req.Angle = e.Angle;
        req.Power = e.Power;
        req.Wind = e.Wind;
        req.TargetCollisionEnabled = false;
        req.MaxTrajectoryPoints = 0; // keep every step for accurate crossing detection
        req.GroundLevel = Math.Min(Math.Min(baseRequest.GroundLevel, e.ObservedLandingY), req.Calibration.LaunchHeight);

        var result = _engine.Simulate(req);
        var (x, y, t, reached) = FindLanding(result.Trajectory, e.ObservedLandingX, e.ObservedLandingY);

        var ex = x - e.ObservedLandingX;
        var ey = y - e.ObservedLandingY;
        var et = e.ObservedFlightTime.HasValue ? t - e.ObservedFlightTime.Value : 0;
        return new ExperimentPrediction
        {
            Index = index,
            Label = e.Label,
            Distance = e.Distance,
            Angle = e.Angle,
            Power = e.Power,
            Wind = e.Wind,
            PredictedLandingX = x,
            PredictedLandingY = y,
            ActualLandingX = e.ObservedLandingX,
            ActualLandingY = e.ObservedLandingY,
            ErrorX = ex,
            ErrorY = ey,
            ErrorDistance = Math.Sqrt(ex * ex + ey * ey),
            ReachedObservedHeight = reached,
            FlightTime = t,
            ActualFlightTime = e.ObservedFlightTime,
            ErrorTime = et
        };
    }

    internal static (double x, double y, double t, bool reached) FindLanding(IReadOnlyList<TrajectoryPoint> pts, double xo, double yo)
    {
        // 1) first descending crossing of y = yo
        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            if (a.Y >= yo && b.Y <= yo && b.Y < a.Y)
            {
                var f = (a.Y - yo) / (a.Y - b.Y);
                return (a.X + (b.X - a.X) * f, yo, a.T + (b.T - a.T) * f, true);
            }
        }

        // 2) fallback: closest approach of the polyline to (xo, yo)
        var best = (x: pts[0].X, y: pts[0].Y, t: pts[0].T);
        var bestD = double.MaxValue;
        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            var len2 = dx * dx + dy * dy;
            var t = len2 > 0 ? Math.Clamp(((xo - a.X) * dx + (yo - a.Y) * dy) / len2, 0, 1) : 0;
            double px = a.X + dx * t, py = a.Y + dy * t;
            var d = (px - xo) * (px - xo) + (py - yo) * (py - yo);
            if (d < bestD) { bestD = d; best = (px, py, a.T + (b.T - a.T) * t); }
        }
        return (best.x, best.y, best.t, false);
    }

    /// <summary>MAE = mean |e|, RMSE = √(mean e²), with e = Euclidean landing error.</summary>
    public static ErrorMetrics ComputeMetrics(IReadOnlyList<ExperimentPrediction> p, double flightTimeWeight = DefaultFlightTimeWeight)
    {
        if (p.Count == 0) return new ErrorMetrics();
        var sse = p.Sum(x => x.ErrorDistance * x.ErrorDistance);
        var timed = p.Where(x => x.ActualFlightTime.HasValue).ToList();
        return new ErrorMetrics
        {
            Count = p.Count,
            MeanAbsoluteError = p.Average(x => x.ErrorDistance),
            RootMeanSquareError = Math.Sqrt(sse / p.Count),
            MaxError = p.Max(x => x.ErrorDistance),
            MeanSignedErrorX = p.Average(x => x.ErrorX),
            FlightTimeRmse = timed.Count > 0 ? Math.Sqrt(timed.Average(x => x.ErrorTime * x.ErrorTime)) : null,
            SumSquaredError = sse + timed.Sum(x => Math.Pow(flightTimeWeight * x.ErrorTime, 2))
        };
    }
}
