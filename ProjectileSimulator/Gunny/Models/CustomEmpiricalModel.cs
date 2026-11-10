using ProjectileSimulator.Gunny.Empirical;

namespace ProjectileSimulator.Gunny.Models;

/// <summary>
/// Model E – mô hình thực nghiệm tuỳ chỉnh: điểm rơi = f(góc, lực, gió) học trên toàn bộ dữ liệu
/// (lọc theo khoảng góc). Các ứng viên: hồi quy tuyến tính, bậc 2, dạng vật lý P²·sin2θ, bảng tra.
/// Chọn ứng viên có LOOCV RMSE nhỏ nhất.
/// </summary>
public sealed class CustomEmpiricalModel : IProjectileModel
{
    public const string ModelId = "custom";

    public string Id => ModelId;
    public string Name => "E · Thực nghiệm tuỳ chỉnh";
    public string Description =>
        "Học điểm rơi từ góc, lực, gió trên mọi lần bắn trong khoảng góc đã chọn. Thử: hồi quy tuyến tính, hồi quy bậc 2, " +
        "hồi quy dạng vật lý D = a + b·P²·sin(2θ) (+ c·W), bảng tra lần bắn gần nhất; chọn theo sai số kiểm định chéo bỏ-một.";
    public string Applicability => "Các lần bắn có góc trong [MinAngle, MaxAngle]. Kết quả ngoài phạm vi dữ liệu là ngoại suy.";

    public IReadOnlyList<ModelSource> Sources { get; } = new[]
    {
        new ModelSource("Dữ liệu thực nghiệm của người dùng", null, "Không có nguồn bên ngoài; mô hình hoàn toàn dựa trên dữ liệu đã nhập."),
    };

    public IReadOnlyList<string> Assumptions { get; } = new[]
    {
        "Gộp nhiều kỹ thuật trong một mô hình có thể kém chính xác hơn mô hình riêng từng kỹ thuật (C, D) — hãy thu hẹp khoảng góc nếu cần.",
        "Cần tối thiểu (số hệ số + 2) lần bắn cho mỗi phương pháp hồi quy.",
    };

    public sealed record Built(List<GunnyExperiment> Data, FitReport Report, bool WindVaries, double ConstantWind);

    public static Built Build(ModelContext ctx)
    {
        var p = ctx.Settings.Custom;
        var data = ctx.WithLanding.Where(e => e.Angle >= p.MinAngle && e.Angle <= p.MaxAngle).ToList();
        var samples = data.Select(e => new EmpiricalSample(new[] { e.Angle, e.Power, e.SignedWind }, e.EffectiveLanding!.Value, e.Id)).ToList();
        var windVaries = samples.Count > 0 && samples.Max(s => s.X[2]) - samples.Min(s => s.X[2]) > 1e-9;
        static double S2(double a) => Math.Sin(2 * a * Math.PI / 180);

        var specs = windVaries
            ? new List<RegressionSpec>
            {
                new(EmpiricalMethod.LinearRegression, "Hồi quy tuyến tính D = a + b·θ + c·P + d·W", x => new[] { 1, x[0], x[1], x[2] }, new[] { "1", "θ", "P", "W" }),
                new(EmpiricalMethod.QuadraticRegression, "Hồi quy bậc 2 theo θ, P (+ W)", x => new[] { 1, x[0], x[1], x[0] * x[0], x[1] * x[1], x[0] * x[1], x[2] }, new[] { "1", "θ", "P", "θ²", "P²", "θ·P", "W" }),
                new(EmpiricalMethod.PhysicsShapeRegression, "Dạng vật lý D = a + b·P²·sin2θ + c·W", x => new[] { 1, x[1] * x[1] * S2(x[0]), x[2] }, new[] { "1", "P²·sin2θ", "W" }),
            }
            : new List<RegressionSpec>
            {
                new(EmpiricalMethod.LinearRegression, "Hồi quy tuyến tính D = a + b·θ + c·P", x => new[] { 1, x[0], x[1] }, new[] { "1", "θ", "P" }),
                new(EmpiricalMethod.QuadraticRegression, "Hồi quy bậc 2 theo θ, P", x => new[] { 1, x[0], x[1], x[0] * x[0], x[1] * x[1], x[0] * x[1] }, new[] { "1", "θ", "P", "θ²", "P²", "θ·P" }),
                new(EmpiricalMethod.PhysicsShapeRegression, "Dạng vật lý D = a + b·P²·sin2θ", x => new[] { 1, x[1] * x[1] * S2(x[0]) }, new[] { "1", "P²·sin2θ" }),
            };
        var report = EmpiricalFitter.FitBest(samples, specs, allowPiecewise: false, allowLookup: true,
            lookupScale: new[] { 2.0, 5.0, 1.0 }, allowed: p.Methods);
        return new Built(data, report, windVaries, samples.Count > 0 ? samples[0].X[2] : 0);
    }

    public ModelInfo Describe(ModelContext ctx)
    {
        var b = Build(ctx);
        var p = ctx.Settings.Custom;
        return new ModelInfo
        {
            Id = Id, Name = Name, Description = Description, Applicability = Applicability, Sources = Sources, Assumptions = Assumptions,
            Status = b.Report.Best != null ? ModelStatus.Fitted : ModelStatus.InsufficientData,
            StatusMessage = b.Report.Best != null ? b.Report.Message! : $"Chưa xác định: {b.Report.Message}",
            Parameters = new[]
            {
                new ModelParameterInfo { Name = "minAngle", Label = "Góc nhỏ nhất", Value = p.MinAngle, Unit = "°", Editable = true, Description = "" },
                new ModelParameterInfo { Name = "maxAngle", Label = "Góc lớn nhất", Value = p.MaxAngle, Unit = "°", Editable = true, Description = "" },
            },
            SubModels = new[]
            {
                new SubModelInfo
                {
                    Name = "Toàn bộ dữ liệu trong khoảng góc", Status = b.Report.Best != null ? ModelStatus.Fitted : ModelStatus.InsufficientData,
                    StatusMessage = b.Report.Message ?? "", Formula = b.Report.Best?.Formula, Samples = b.Data.Count, LoocvRmse = b.Report.BestReport?.LoocvRmse
                }
            }
        };
    }

    private static (double? y, bool ext, string? reason) PredictWith(Built b, double angle, double power, double wind, CustomEmpiricalParameters p)
    {
        if (b.Report.Best == null) return (null, false, $"Chưa xác định: {b.Report.Message}");
        if (angle < p.MinAngle || angle > p.MaxAngle) return (null, false, $"Góc ngoài khoảng {p.MinAngle:0.#}–{p.MaxAngle:0.#}°.");
        if (!b.WindVaries && Math.Abs(wind - b.ConstantWind) > 1e-6)
            return (null, false, $"Dữ liệu chỉ có gió = {b.ConstantWind:0.##}; chưa đủ để dự đoán với gió {wind:0.##}.");
        var x = new[] { angle, power, wind };
        return (b.Report.Best.Predict(x), b.Report.Best.IsExtrapolation(x), null);
    }

    public ModelPrediction Predict(GunnyShot shot, ModelContext ctx)
    {
        var (y, ext, reason) = PredictWith(Build(ctx), shot.Angle, shot.Power, shot.SignedWind, ctx.Settings.Custom);
        return new ModelPrediction { ModelId = Id, ModelName = Name, Supported = y.HasValue, Landing = y, Extrapolated = ext, Reason = reason };
    }

    public ModelAimResult SuggestAim(double target, double wind, ModelContext ctx)
    {
        var b = Build(ctx);
        var p = ctx.Settings.Custom;
        var probe = PredictWith(b, Math.Clamp(45, p.MinAngle, p.MaxAngle), 50, wind, p);
        if (probe.y == null) return new ModelAimResult { ModelId = Id, ModelName = Name, Message = probe.reason };

        // Chỉ quét góc trong phạm vi dữ liệu đã có (tránh ngoại suy theo góc quá xa).
        var aMin = (int)Math.Ceiling(b.Data.Min(e => e.Angle));
        var aMax = (int)Math.Floor(b.Data.Max(e => e.Angle));
        var sols = new List<AimSolution>();
        for (var a = aMin; a <= aMax; a++)
        {
            (double pw, double err) best = (double.NaN, double.MaxValue);
            for (var pw = 0.0; pw <= 100 + 1e-9; pw += 0.5)
            {
                var y = PredictWith(b, a, pw, wind, p).y!.Value;
                if (Math.Abs(y - target) < Math.Abs(best.err)) best = (pw, y - target);
            }
            var r = PredictWith(b, a, best.pw, wind, p);
            sols.Add(new AimSolution
            {
                ModelId = Id, ModelName = Name, Angle = a, Power = best.pw, SignedWind = wind, TargetDistance = target,
                PredictedLanding = r.y!.Value, Error = r.y!.Value - target, Extrapolated = r.ext,
                Note = b.Report.Best!.Label + (r.ext ? " · NGOẠI SUY" : "")
            });
        }
        return new ModelAimResult { ModelId = Id, ModelName = Name, Solutions = sols.OrderBy(s => Math.Abs(s.Error)).ToList() };
    }

    public ModelEvaluation Evaluate(ModelContext ctx)
    {
        var b = Build(ctx);
        var devs = b.Data.Select(e => PhysicsBaselineModel.Dev(e, PredictWith(b, e.Angle, e.Power, e.SignedWind, ctx.Settings.Custom).y)).ToList();
        return ModelEvaluation.From(this, b.Data.Count, devs, b.Report.Best == null ? $"Chưa xác định: {b.Report.Message}" : null,
            b.Report.Best?.Label, b.Report.Best?.Formula, b.Report.BestReport?.LoocvRmse);
    }
}
