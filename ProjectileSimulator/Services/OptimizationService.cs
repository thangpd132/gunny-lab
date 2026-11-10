using System.Diagnostics;
using ProjectileSimulator.Models;

namespace ProjectileSimulator.Services;

/// <summary>
/// Fits the tunable parameters of the model to observed shots by minimising
/// Σ ErrorDistance² (non-linear least squares).
/// <list type="bullet">
///   <item><b>Grid search</b>: evaluates every combination on a regular grid (parallel). Robust, finds the basin.</item>
///   <item><b>Levenberg–Marquardt</b>: Gauss–Newton with adaptive damping and a forward-difference Jacobian,
///         working in normalised [0,1] coordinates, clamped to the given bounds.</item>
/// </list>
/// </summary>
public sealed class OptimizationService
{
    private readonly ReverseEngineeringService _re;

    public OptimizationService(ReverseEngineeringService re) => _re = re;

    public static double Get(SimulationRequest r, TunableParameter p) => p switch
    {
        TunableParameter.VelocityMultiplier => r.Calibration.VelocityMultiplier,
        TunableParameter.PowerMultiplier => r.Calibration.PowerMultiplier,
        TunableParameter.PowerExponent => r.Calibration.PowerExponent,
        TunableParameter.GravityMultiplier => r.Calibration.GravityMultiplier,
        TunableParameter.WindMultiplier => r.Calibration.WindMultiplier,
        TunableParameter.DragCoefficient => r.DragCoefficient,
        TunableParameter.AngleOffset => r.Calibration.AngleOffset,
        TunableParameter.LaunchHeight => r.Calibration.LaunchHeight,
        _ => throw new ArgumentOutOfRangeException(nameof(p))
    };

    public static void Set(SimulationRequest r, TunableParameter p, double v)
    {
        switch (p)
        {
            case TunableParameter.VelocityMultiplier: r.Calibration.VelocityMultiplier = v; break;
            case TunableParameter.PowerMultiplier: r.Calibration.PowerMultiplier = v; break;
            case TunableParameter.PowerExponent: r.Calibration.PowerExponent = v; break;
            case TunableParameter.GravityMultiplier: r.Calibration.GravityMultiplier = v; break;
            case TunableParameter.WindMultiplier: r.Calibration.WindMultiplier = v; break;
            case TunableParameter.DragCoefficient: r.DragCoefficient = v; break;
            case TunableParameter.AngleOffset: r.Calibration.AngleOffset = v; break;
            case TunableParameter.LaunchHeight: r.Calibration.LaunchHeight = v; break;
            default: throw new ArgumentOutOfRangeException(nameof(p));
        }
    }

    /// <summary>Default search ranges shown in the UI.</summary>
    public static List<ParameterRange> DefaultRanges() => new()
    {
        new() { Name = TunableParameter.VelocityMultiplier, Enabled = true, Min = 0.5, Max = 2.0, Steps = 31 },
        new() { Name = TunableParameter.PowerMultiplier, Enabled = false, Min = 0.5, Max = 2.0, Steps = 16 },
        new() { Name = TunableParameter.PowerExponent, Enabled = false, Min = 0.5, Max = 1.5, Steps = 11 },
        new() { Name = TunableParameter.GravityMultiplier, Enabled = true, Min = 0.5, Max = 2.0, Steps = 31 },
        new() { Name = TunableParameter.WindMultiplier, Enabled = false, Min = 0, Max = 3, Steps = 13 },
        new() { Name = TunableParameter.DragCoefficient, Enabled = false, Min = 0, Max = 2, Steps = 11 },
        new() { Name = TunableParameter.AngleOffset, Enabled = false, Min = -5, Max = 5, Steps = 11 },
        new() { Name = TunableParameter.LaunchHeight, Enabled = false, Min = 0, Max = 2, Steps = 11 },
    };

    public OptimizationResult Optimize(OptimizationRequest req, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var specs = req.Parameters.Where(p => p.Enabled).ToArray();
        var notes = new List<string>();
        var history = new List<double>();
        var evaluations = 0;
        var iterations = 0;

        var baseReq = req.Base.Clone();
        var experiments = req.Experiments;
        var n = specs.Length;

        // If the user turned drag off but wants to fit its coefficient, the parameter has no effect.
        if (specs.Any(s => s.Name == TunableParameter.DragCoefficient) && baseReq.DragType == Physics.DragType.None)
            notes.Add("DragCoefficient is being optimised but DragType = None, so it has no effect.");
        if (specs.Any(s => s.Name == TunableParameter.VelocityMultiplier) && specs.Any(s => s.Name == TunableParameter.PowerMultiplier))
            notes.Add("VelocityMultiplier and PowerMultiplier are (almost) collinear — only their combined effect can be identified.");
        if (specs.Any(s => s.Name == TunableParameter.VelocityMultiplier) && specs.Any(s => s.Name == TunableParameter.GravityMultiplier)
            && experiments.All(e => e.ObservedFlightTime is null))
            notes.Add("Velocity and Gravity multipliers are fitted from landing points only: landing X mostly depends on V0²/g, so the pair is weakly identifiable. Enter observed flight times to separate them.");
        if (experiments.Count * 2 < n)
            notes.Add($"Only {experiments.Count} experiments for {n} parameters — the problem is under-determined; add more shots.");

        var initial = specs.Select(s => Math.Clamp(Get(baseReq, s.Name), s.Min, s.Max)).ToArray();
        var initialParams = specs.ToDictionary(s => s.Name.ToString(), s => Get(baseReq, s.Name));
        var w = req.FlightTimeWeight;
        var initialEval = _re.Evaluate(baseReq, experiments, w);
        evaluations++;
        history.Add(initialEval.Metrics.RootMeanSquareError);

        double Cost(double[] x)
        {
            var r = Build(baseReq, specs, x);
            Interlocked.Increment(ref evaluations);
            return _re.Residuals(r, experiments, w).Sum(v => v * v);
        }

        var best = initial;
        var bestCost = Cost(best);

        // Keep the starting point if it is outside the grid; otherwise the grid result is used.
        if (req.Method is OptimizationMethod.GridSearch or OptimizationMethod.GridThenLeastSquares)
        {
            var (gx, gc) = GridSearch(specs, Cost, req.MaxGridEvaluations, notes, ct);
            if (gc < bestCost) { best = gx; bestCost = gc; }
            history.Add(Math.Sqrt(bestCost / experiments.Count));
        }

        if (req.Method is OptimizationMethod.LeastSquares or OptimizationMethod.GridThenLeastSquares)
        {
            var (lx, lc, it) = LevenbergMarquardt(specs, best, x => _re.Residuals(Build(baseReq, specs, x), experiments, w),
                req.MaxIterations, history, experiments.Count, () => Interlocked.Increment(ref evaluations), ct);
            iterations = it;
            if (lc <= bestCost) { best = lx; bestCost = lc; }
        }

        var bestReq = Build(baseReq, specs, best);
        var bestEval = _re.Evaluate(bestReq, experiments, w);
        evaluations++;

        sw.Stop();
        return new OptimizationResult
        {
            Method = req.Method,
            InitialParameters = initialParams,
            BestParameters = specs.Select((s, i) => (s, i)).ToDictionary(t => t.s.Name.ToString(), t => best[t.i]),
            BestRequest = bestReq,
            InitialMetrics = initialEval.Metrics,
            BestMetrics = bestEval.Metrics,
            Predictions = bestEval.Predictions,
            Evaluations = evaluations,
            Iterations = iterations,
            RmseHistory = history,
            Notes = notes,
            ElapsedMs = sw.Elapsed.TotalMilliseconds
        };
    }

    private static SimulationRequest Build(SimulationRequest baseReq, ParameterRange[] specs, double[] x)
    {
        var r = baseReq.Clone();
        for (var i = 0; i < specs.Length; i++) Set(r, specs[i].Name, x[i]);
        return r;
    }

    // ------------------------------------------------------------------ Grid search

    private static (double[] x, double cost) GridSearch(ParameterRange[] specs, Func<double[], double> cost,
        int maxEvaluations, List<string> notes, CancellationToken ct)
    {
        var n = specs.Length;
        var steps = specs.Select(s => s.Min == s.Max ? 1 : Math.Max(2, s.Steps)).ToArray();
        double total = steps.Aggregate(1.0, (a, b) => a * b);
        if (total > maxEvaluations)
        {
            // Shrink every axis proportionally so that Π steps ≤ maxEvaluations.
            var factor = Math.Pow(maxEvaluations / total, 1.0 / n);
            for (var i = 0; i < n; i++)
                if (steps[i] > 1) steps[i] = Math.Max(2, (int)Math.Floor(steps[i] * factor));
            total = steps.Aggregate(1.0, (a, b) => a * b);
            notes.Add($"Grid reduced to {string.Join("×", steps)} = {total:N0} points to respect MaxGridEvaluations.");
        }

        var count = (long)total;
        var lockObj = new object();
        double[] bestX = specs.Select(s => s.Min).ToArray();
        var bestCost = double.MaxValue;

        Parallel.For(0L, count, new ParallelOptions { CancellationToken = ct },
            () => (x: (double[]?)null, c: double.MaxValue),
            (idx, _, local) =>
            {
                var x = new double[n];
                var rem = idx;
                for (var i = 0; i < n; i++)
                {
                    var k = rem % steps[i];
                    rem /= steps[i];
                    x[i] = steps[i] == 1 ? specs[i].Min : specs[i].Min + (specs[i].Max - specs[i].Min) * k / (steps[i] - 1);
                }
                var c = cost(x);
                return c < local.c ? (x, c) : local;
            },
            local =>
            {
                if (local.x == null) return;
                lock (lockObj)
                {
                    if (local.c < bestCost) { bestCost = local.c; bestX = local.x; }
                }
            });

        return (bestX, bestCost);
    }

    // ------------------------------------------------------------------ Levenberg–Marquardt

    private static (double[] x, double cost, int iterations) LevenbergMarquardt(
        ParameterRange[] specs, double[] start, Func<double[], double[]> residualFn, int maxIterations,
        List<double> history, int experimentCount, Action countEval, CancellationToken ct)
    {
        var n = specs.Length;
        var span = specs.Select(s => Math.Max(s.Max - s.Min, 0)).ToArray();

        // Work in normalised coordinates u ∈ [0,1]: x = min + u·span. Fixed params (span 0) are skipped.
        double[] ToX(double[] u) => u.Select((v, i) => specs[i].Min + v * span[i]).ToArray();
        double[] Res(double[] u) { countEval(); return residualFn(ToX(u)); }
        static double Sse(double[] r) => r.Sum(v => v * v);

        var u = start.Select((v, i) => span[i] > 0 ? (v - specs[i].Min) / span[i] : 0).ToArray();
        var r = Res(u);
        var cost = Sse(r);
        var lambda = 1e-2;
        var it = 0;

        for (; it < maxIterations && cost > 1e-14; it++)
        {
            ct.ThrowIfCancellationRequested();
            var m = r.Length;

            // Forward-difference Jacobian J[m×n]
            var J = new double[m, n];
            for (var j = 0; j < n; j++)
            {
                if (span[j] <= 0) continue;
                var h = 1e-5;
                var uj = (double[])u.Clone();
                if (uj[j] + h > 1) h = -h; // step inward at the upper bound
                uj[j] += h;
                var rj = Res(uj);
                for (var i = 0; i < m; i++) J[i, j] = (rj[i] - r[i]) / h;
            }

            // Normal equations: A = JᵀJ, g = Jᵀr
            var A = new double[n, n];
            var g = new double[n];
            for (var a = 0; a < n; a++)
            {
                for (var i = 0; i < m; i++) g[a] += J[i, a] * r[i];
                for (var b = 0; b < n; b++)
                {
                    double s = 0;
                    for (var i = 0; i < m; i++) s += J[i, a] * J[i, b];
                    A[a, b] = s;
                }
            }

            var improved = false;
            for (var attempt = 0; attempt < 12 && !improved; attempt++)
            {
                // (JᵀJ + λ·diag(JᵀJ) + ε·I)·δ = −Jᵀr
                var M = (double[,])A.Clone();
                for (var d = 0; d < n; d++) M[d, d] += lambda * (A[d, d] + 1e-9) + 1e-12;
                var delta = Solve(M, g.Select(v => -v).ToArray());
                if (delta == null) { lambda *= 10; continue; }

                var uNew = u.Select((v, i) => span[i] > 0 ? Math.Clamp(v + delta[i], 0, 1) : v).ToArray();
                var rNew = Res(uNew);
                var cNew = Sse(rNew);
                if (cNew < cost)
                {
                    var relGain = (cost - cNew) / Math.Max(cost, 1e-300);
                    u = uNew; r = rNew; cost = cNew;
                    lambda = Math.Max(lambda / 3, 1e-9);
                    improved = true;
                    history.Add(Math.Sqrt(cost / experimentCount));
                    if (relGain < 1e-10) return (ToX(u), cost, it + 1);
                }
                else lambda *= 4;
            }
            if (!improved) break; // converged (no descent direction left)
        }

        return (ToX(u), cost, it);
    }

    /// <summary>Gaussian elimination with partial pivoting; null if singular.</summary>
    internal static double[]? Solve(double[,] a, double[] b)
    {
        var n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (var col = 0; col < n; col++)
        {
            var piv = col;
            for (var row = col + 1; row < n; row++)
                if (Math.Abs(m[row, col]) > Math.Abs(m[piv, col])) piv = row;
            if (Math.Abs(m[piv, col]) < 1e-300) return null;
            if (piv != col)
            {
                for (var k = 0; k < n; k++) (m[col, k], m[piv, k]) = (m[piv, k], m[col, k]);
                (x[col], x[piv]) = (x[piv], x[col]);
            }
            for (var row = col + 1; row < n; row++)
            {
                var f = m[row, col] / m[col, col];
                if (f == 0) continue;
                for (var k = col; k < n; k++) m[row, k] -= f * m[col, k];
                x[row] -= f * x[col];
            }
        }
        for (var row = n - 1; row >= 0; row--)
        {
            var s = x[row];
            for (var k = row + 1; k < n; k++) s -= m[row, k] * x[k];
            x[row] = s / m[row, row];
        }
        return x.All(double.IsFinite) ? x : null;
    }
}
