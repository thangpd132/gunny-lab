using ProjectileSimulator.Services;

namespace ProjectileSimulator.Gunny.Empirical;

/// <summary>Các phương pháp học từ dữ liệu thực nghiệm.</summary>
public enum EmpiricalMethod
{
    /// <summary>Nội suy tuyến tính từng đoạn theo một biến (lực). Chỉ dùng khi gió không đổi.</summary>
    PiecewiseLinear,

    /// <summary>Hồi quy tuyến tính (bình phương tối thiểu).</summary>
    LinearRegression,

    /// <summary>Hồi quy đa thức bậc 2 (bình phương tối thiểu).</summary>
    QuadraticRegression,

    /// <summary>Hồi quy dạng vật lý: D = a + b·P²·sin(2θ) (+ c·W).</summary>
    PhysicsShapeRegression,

    /// <summary>Bảng tra: lấy kết quả của lần bắn gần nhất (láng giềng gần nhất).</summary>
    LookupTable
}

/// <summary>Một mẫu dữ liệu: biến đầu vào X (thứ tự do mô hình quy định) → điểm rơi Y.</summary>
public sealed record EmpiricalSample(double[] X, double Y, Guid? SourceId = null);

/// <summary>Một hàm đã khớp xong, dự đoán được Y từ X.</summary>
public sealed class EmpiricalFit
{
    public EmpiricalMethod Method { get; init; }
    public string Label { get; init; } = "";
    public string Formula { get; init; } = "";
    public double[] Coefficients { get; init; } = Array.Empty<double>();
    public required Func<double[], double> Predict { get; init; }

    /// <summary>Miền dữ liệu đã có của từng biến (để đánh dấu ngoại suy).</summary>
    public double[] Min { get; init; } = Array.Empty<double>();
    public double[] Max { get; init; } = Array.Empty<double>();

    public bool IsExtrapolation(double[] x)
    {
        for (var i = 0; i < Min.Length; i++)
            if (x[i] < Min[i] - 1e-9 || x[i] > Max[i] + 1e-9) return true;
        return false;
    }
}

/// <summary>Đánh giá một phương pháp ứng viên trên dữ liệu.</summary>
public sealed class CandidateReport
{
    public EmpiricalMethod Method { get; init; }
    public string Label { get; init; } = "";
    public bool Available { get; init; }
    public string? Reason { get; init; }
    public double? InSampleMae { get; init; }
    public double? InSampleRmse { get; init; }

    /// <summary>RMSE kiểm định chéo bỏ-một (leave-one-out): tiêu chí chọn mô hình.</summary>
    public double? LoocvRmse { get; init; }
    public string? Formula { get; init; }
}

public sealed class FitReport
{
    public EmpiricalFit? Best { get; init; }
    public List<CandidateReport> Candidates { get; init; } = new();
    public int Samples { get; init; }
    public string? Message { get; init; }
    public CandidateReport? BestReport => Best == null ? null : Candidates.First(c => c.Method == Best.Method);
}

/// <summary>
/// Định nghĩa một ứng viên hồi quy: hàm cơ sở φ(x) và nhãn từng hệ số. Y ≈ Σ βᵢ·φᵢ(x).
/// </summary>
public sealed record RegressionSpec(EmpiricalMethod Method, string Label, Func<double[], double[]> Basis, string[] Terms);

/// <summary>
/// Khớp dữ liệu thực nghiệm bằng nhiều phương pháp đơn giản, dễ giải thích, và chọn phương pháp
/// có sai số kiểm định chéo bỏ-một (LOOCV RMSE) nhỏ nhất. Không chọn theo sai số trên chính tập
/// huấn luyện (vì đa thức nhiều hệ số luôn "khớp" tốt hơn trên dữ liệu đã thấy).
/// </summary>
public static class EmpiricalFitter
{
    public static FitReport FitBest(IReadOnlyList<EmpiricalSample> samples, IEnumerable<RegressionSpec> regressions,
        bool allowPiecewise, bool allowLookup, double[] lookupScale, IReadOnlyCollection<EmpiricalMethod>? allowed = null)
    {
        bool Allowed(EmpiricalMethod m) => allowed == null || allowed.Count == 0 || allowed.Contains(m);
        var reports = new List<CandidateReport>();
        var fits = new List<(EmpiricalFit fit, CandidateReport rep)>();

        void Consider(EmpiricalMethod m, string label, Func<IReadOnlyList<EmpiricalSample>, (EmpiricalFit? fit, string? reason)> trainer, int minSamples)
        {
            if (!Allowed(m)) return;
            if (samples.Count < minSamples)
            {
                reports.Add(new CandidateReport { Method = m, Label = label, Available = false, Reason = $"Cần ≥ {minSamples} lần bắn, hiện có {samples.Count}." });
                return;
            }
            var (fit, reason) = trainer(samples);
            if (fit == null)
            {
                reports.Add(new CandidateReport { Method = m, Label = label, Available = false, Reason = reason });
                return;
            }
            var (mae, rmse) = Errors(samples, fit.Predict);
            var loo = Loocv(samples, trainer);
            var rep = new CandidateReport
            {
                Method = m, Label = label, Available = loo.HasValue, InSampleMae = mae, InSampleRmse = rmse, LoocvRmse = loo,
                Formula = fit.Formula, Reason = loo.HasValue ? null : "Không kiểm định chéo được (dữ liệu suy biến)."
            };
            reports.Add(rep);
            if (loo.HasValue) fits.Add((fit, rep));
        }

        foreach (var spec in regressions)
            Consider(spec.Method, spec.Label, s => TrainRegression(s, spec), spec.Terms.Length + 2);
        if (allowPiecewise)
            Consider(EmpiricalMethod.PiecewiseLinear, "Nội suy tuyến tính từng đoạn theo lực", TrainPiecewise, 3);
        if (allowLookup)
            Consider(EmpiricalMethod.LookupTable, "Bảng tra (lần bắn gần nhất)", s => (TrainLookup(s, lookupScale), null), 2);

        var best = fits.OrderBy(f => f.rep.LoocvRmse).ThenBy(f => f.fit.Coefficients.Length).FirstOrDefault();
        return new FitReport
        {
            Best = best.fit,
            Candidates = reports,
            Samples = samples.Count,
            Message = best.fit == null
                ? (samples.Count == 0 ? "Chưa có dữ liệu thực nghiệm phù hợp." : "Chưa đủ dữ liệu để khớp bất kỳ phương pháp nào.")
                : $"Chọn \"{best.rep.Label}\" (LOOCV RMSE nhỏ nhất = {best.rep.LoocvRmse:0.###})."
        };
    }

    public static (double mae, double rmse) Errors(IReadOnlyList<EmpiricalSample> s, Func<double[], double> f)
    {
        if (s.Count == 0) return (0, 0);
        double sa = 0, ss = 0;
        foreach (var x in s)
        {
            var e = f(x.X) - x.Y;
            sa += Math.Abs(e);
            ss += e * e;
        }
        return (sa / s.Count, Math.Sqrt(ss / s.Count));
    }

    /// <summary>Leave-one-out: khớp trên n−1 mẫu, dự đoán mẫu còn lại; RMSE trên n lần.</summary>
    public static double? Loocv(IReadOnlyList<EmpiricalSample> s, Func<IReadOnlyList<EmpiricalSample>, (EmpiricalFit? fit, string? reason)> trainer)
    {
        if (s.Count < 2) return null;
        double ss = 0;
        for (var i = 0; i < s.Count; i++)
        {
            var rest = s.Where((_, j) => j != i).ToList();
            var (fit, _) = trainer(rest);
            if (fit == null) return null;
            var e = fit.Predict(s[i].X) - s[i].Y;
            if (!double.IsFinite(e)) return null;
            ss += e * e;
        }
        return Math.Sqrt(ss / s.Count);
    }

    // ------------------------------------------------------------------ trainers

    public static (EmpiricalFit? fit, string? reason) TrainRegression(IReadOnlyList<EmpiricalSample> s, RegressionSpec spec)
    {
        var p = spec.Terms.Length;
        if (s.Count < p) return (null, $"Cần ≥ {p} mẫu.");
        var rows = s.Select(x => spec.Basis(x.X)).ToArray();

        // Chuẩn hoá cột để hệ phương trình chuẩn không bị điều kiện xấu (P² ~ 10⁴).
        var scale = new double[p];
        for (var j = 0; j < p; j++) scale[j] = Math.Max(1e-12, rows.Max(r => Math.Abs(r[j])));

        var a = new double[p, p];
        var b = new double[p];
        for (var k = 0; k < rows.Length; k++)
            for (var i = 0; i < p; i++)
            {
                var xi = rows[k][i] / scale[i];
                b[i] += xi * s[k].Y;
                for (var j = 0; j < p; j++) a[i, j] += xi * rows[k][j] / scale[j];
            }
        // Kiểm tra suy biến (ví dụ mọi lần bắn cùng một lực → không xác định được hệ số của lực).
        if (Rank(rows.Select(r => r.Select((v, j) => v / scale[j]).ToArray()).ToArray()) < p)
            return (null, "Dữ liệu không đủ đa dạng để xác định mọi hệ số (ví dụ mọi lần bắn cùng một lực).");
        var maxDiag = Enumerable.Range(0, p).Max(i => a[i, i]);
        for (var i = 0; i < p; i++) a[i, i] += 1e-10 * maxDiag;
        var beta = OptimizationService.Solve(a, b);
        if (beta == null) return (null, "Hệ phương trình suy biến (dữ liệu không đủ đa dạng).");
        for (var j = 0; j < p; j++) beta[j] /= scale[j];

        var coeffs = beta;
        return (new EmpiricalFit
        {
            Method = spec.Method,
            Label = spec.Label,
            Coefficients = coeffs,
            Formula = FormatFormula(coeffs, spec.Terms),
            Predict = x => { var r = spec.Basis(x); double y = 0; for (var i = 0; i < p; i++) y += coeffs[i] * r[i]; return y; },
            Min = Enumerable.Range(0, s[0].X.Length).Select(i => s.Min(v => v.X[i])).ToArray(),
            Max = Enumerable.Range(0, s[0].X.Length).Select(i => s.Max(v => v.X[i])).ToArray(),
        }, null);
    }

    /// <summary>
    /// Nội suy tuyến tính theo X[0] (lực). Các lần bắn cùng lực được lấy trung bình.
    /// Ngoài phạm vi dữ liệu: ngoại suy theo đoạn đầu/cuối (được đánh dấu là ngoại suy).
    /// </summary>
    public static (EmpiricalFit? fit, string? reason) TrainPiecewise(IReadOnlyList<EmpiricalSample> s)
    {
        var pts = s.GroupBy(x => Math.Round(x.X[0], 6)).Select(g => (x: g.Key, y: g.Average(v => v.Y))).OrderBy(t => t.x).ToArray();
        if (pts.Length < 2) return (null, "Cần ít nhất 2 mức lực khác nhau.");
        double F(double[] v)
        {
            var x = v[0];
            var i = 1;
            while (i < pts.Length - 1 && pts[i].x < x) i++;
            var (x0, y0) = pts[i - 1];
            var (x1, y1) = pts[i];
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }
        return (new EmpiricalFit
        {
            Method = EmpiricalMethod.PiecewiseLinear,
            Label = "Nội suy tuyến tính từng đoạn theo lực",
            Formula = $"Nội suy giữa {pts.Length} mốc lực: " + string.Join(", ", pts.Select(p => $"P{p.x:0.#}→{p.y:0.###}")),
            Predict = F,
            Min = new[] { pts[0].x }.Concat(Enumerable.Range(1, s[0].X.Length - 1).Select(i => s.Min(v => v.X[i]))).ToArray(),
            Max = new[] { pts[^1].x }.Concat(Enumerable.Range(1, s[0].X.Length - 1).Select(i => s.Max(v => v.X[i]))).ToArray(),
        }, null);
    }

    /// <summary>Láng giềng gần nhất trong không gian đã chuẩn hoá bởi <paramref name="scale"/>.</summary>
    public static EmpiricalFit TrainLookup(IReadOnlyList<EmpiricalSample> s, double[] scale)
    {
        var data = s.ToArray();
        return new EmpiricalFit
        {
            Method = EmpiricalMethod.LookupTable,
            Label = "Bảng tra (lần bắn gần nhất)",
            Formula = $"Lấy điểm rơi của lần bắn gần nhất trong {data.Length} lần đã ghi",
            Predict = x =>
            {
                var best = data[0]; var bd = double.MaxValue;
                foreach (var d in data)
                {
                    double dist = 0;
                    for (var i = 0; i < x.Length; i++) { var t = (x[i] - d.X[i]) / scale[i]; dist += t * t; }
                    if (dist < bd) { bd = dist; best = d; }
                }
                return best.Y;
            },
            Min = Enumerable.Range(0, data[0].X.Length).Select(i => data.Min(v => v.X[i])).ToArray(),
            Max = Enumerable.Range(0, data[0].X.Length).Select(i => data.Max(v => v.X[i])).ToArray(),
        };
    }

    /// <summary>"D = 1.2 + 0.05·P − 0.3·W"</summary>
    public static string FormatFormula(double[] c, string[] terms)
    {
        var sb = new System.Text.StringBuilder("D = ");
        for (var i = 0; i < c.Length; i++)
        {
            var v = c[i].ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture).TrimStart('-');
            if (i == 0) sb.Append(c[i] < 0 ? "−" : "");
            else sb.Append(c[i] < 0 ? " − " : " + ");
            sb.Append(v);
            if (terms[i] != "1") sb.Append('·').Append(terms[i]);
        }
        return sb.ToString();
    }

    /// <summary>Hạng của ma trận (khử Gauss có chọn trụ) — kiểm tra dữ liệu có đủ đa dạng.</summary>
    internal static int Rank(double[][] rows)
    {
        if (rows.Length == 0) return 0;
        var m = rows.Select(r => (double[])r.Clone()).ToArray();
        int n = m.Length, p = m[0].Length, rank = 0;
        for (var col = 0; col < p && rank < n; col++)
        {
            var scale = m.Max(r => Math.Abs(r[col]));
            if (scale < 1e-12) continue;
            var piv = rank;
            for (var r = rank + 1; r < n; r++) if (Math.Abs(m[r][col]) > Math.Abs(m[piv][col])) piv = r;
            if (Math.Abs(m[piv][col]) < 1e-9 * scale) continue;
            (m[rank], m[piv]) = (m[piv], m[rank]);
            for (var r = rank + 1; r < n; r++)
            {
                var f = m[r][col] / m[rank][col];
                for (var c = col; c < p; c++) m[r][c] -= f * m[rank][c];
            }
            rank++;
        }
        return rank;
    }
}
