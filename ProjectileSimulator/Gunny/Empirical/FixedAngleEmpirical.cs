namespace ProjectileSimulator.Gunny.Empirical;

/// <summary>
/// Bộ học dữ liệu cho MỘT kỹ thuật góc cố định (ví dụ 65° hoặc 20°): điểm rơi = f(lực, gió).
/// Dữ liệu của mỗi kỹ thuật được khớp riêng — không dùng chung quy tắc chỉnh gió giữa các kỹ thuật.
/// Không có công thức đặt sẵn: nếu chưa đủ dữ liệu thì trạng thái là "chưa xác định".
/// </summary>
public sealed class FixedAngleEmpirical
{
    public double Angle { get; }
    public double Tolerance { get; }
    public IReadOnlyList<GunnyExperiment> Data { get; }
    public FitReport Report { get; }

    /// <summary>True nếu dữ liệu có nhiều mức gió → hồi quy có thêm biến gió.</summary>
    public bool WindVaries { get; }

    /// <summary>Giá trị gió duy nhất khi <see cref="WindVaries"/> = false.</summary>
    public double ConstantWind { get; }

    public EmpiricalFit? Fit => Report.Best;

    public FixedAngleEmpirical(IEnumerable<GunnyExperiment> experiments, double angle, double tolerance)
    {
        Angle = angle;
        Tolerance = tolerance;
        Data = experiments.Where(e => e.EffectiveLanding.HasValue && Math.Abs(e.Angle - angle) <= tolerance).ToList();
        var samples = Data.Select(e => new EmpiricalSample(new[] { e.Power, e.SignedWind }, e.EffectiveLanding!.Value, e.Id)).ToList();
        WindVaries = samples.Count > 0 && samples.Max(s => s.X[1]) - samples.Min(s => s.X[1]) > 1e-9;
        ConstantWind = samples.Count > 0 ? samples[0].X[1] : 0;

        var specs = new List<RegressionSpec>();
        if (WindVaries)
        {
            specs.Add(new(EmpiricalMethod.LinearRegression, "Hồi quy tuyến tính D = a + b·P + c·W", x => new[] { 1, x[0], x[1] }, new[] { "1", "P", "W" }));
            specs.Add(new(EmpiricalMethod.QuadraticRegression, "Hồi quy bậc 2 D = a + b·P + c·P² + d·W", x => new[] { 1, x[0], x[0] * x[0], x[1] }, new[] { "1", "P", "P²", "W" }));
        }
        else
        {
            specs.Add(new(EmpiricalMethod.LinearRegression, "Hồi quy tuyến tính D = a + b·P", x => new[] { 1, x[0] }, new[] { "1", "P" }));
            specs.Add(new(EmpiricalMethod.QuadraticRegression, "Hồi quy bậc 2 D = a + b·P + c·P²", x => new[] { 1, x[0], x[0] * x[0] }, new[] { "1", "P", "P²" }));
        }
        Report = EmpiricalFitter.FitBest(samples, specs, allowPiecewise: !WindVaries, allowLookup: true, lookupScale: new[] { 5.0, 1.0 });
    }

    public ModelStatus Status => Fit != null ? ModelStatus.Fitted : ModelStatus.InsufficientData;

    public string StatusMessage =>
        Fit == null
            ? $"Chưa xác định được công thức: {Report.Message} (đang có {Data.Count} lần bắn ở góc {Angle:0.#}° ± {Tolerance:0.#})."
            : $"{Report.Message} {(WindVaries ? "" : $"Dữ liệu chỉ có gió = {ConstantWind:0.##} → chưa xác định ảnh hưởng của gió.")}";

    /// <summary>Dự đoán điểm rơi; null + lý do nếu không đủ cơ sở.</summary>
    public (double? landing, bool extrapolated, string? reason) Predict(double power, double signedWind)
    {
        if (Fit == null) return (null, false, StatusMessage);
        if (!WindVaries && Math.Abs(signedWind - ConstantWind) > 1e-6)
            return (null, false, $"Dữ liệu góc {Angle:0.#}° chỉ có gió = {ConstantWind:0.##}; chưa đủ dữ liệu để dự đoán với gió {signedWind:0.##}.");
        var x = new[] { power, signedWind };
        return (Fit.Predict(x), Fit.IsExtrapolation(x), null);
    }

    /// <summary>Tìm các mức lực (0–100) cho điểm rơi = mục tiêu: quét bước 0.25 rồi chia đôi tại chỗ đổi dấu.</summary>
    public List<(double power, double landing, bool extrapolated)> SolvePower(double target, double signedWind)
    {
        var res = new List<(double, double, bool)>();
        if (Predict(50, signedWind).landing == null) return res;
        double F(double p) => Predict(p, signedWind).landing!.Value - target;
        double prevP = 0, prevF = F(0);
        (double p, double f) best = (0, prevF);
        for (var p = 0.25; p <= 100 + 1e-9; p += 0.25)
        {
            var f = F(p);
            if (Math.Abs(f) < Math.Abs(best.f)) best = (p, f);
            if (Math.Sign(f) != Math.Sign(prevF) || f == 0)
            {
                double lo = prevP, hi = p, flo = prevF;
                for (var i = 0; i < 40; i++)
                {
                    var mid = 0.5 * (lo + hi);
                    var fm = F(mid);
                    if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; } else hi = mid;
                }
                var root = Math.Round(0.5 * (lo + hi), 1);
                if (!res.Any(r => Math.Abs(r.Item1 - root) < 0.5))
                {
                    var pr = Predict(root, signedWind);
                    res.Add((root, pr.landing!.Value, pr.extrapolated));
                }
            }
            prevP = p; prevF = f;
        }
        if (res.Count == 0)
        {
            // Không có nghiệm chính xác (ví dụ bảng tra có bậc): trả về mức lực gần nhất kèm sai số.
            var pr = Predict(best.p, signedWind);
            res.Add((best.p, pr.landing!.Value, pr.extrapolated));
        }
        return res;
    }
}
