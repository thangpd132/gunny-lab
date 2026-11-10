using ProjectileSimulator.Gunny.Empirical;
using ProjectileSimulator.Gunny.Models;
using ProjectileSimulator.Models;
using ProjectileSimulator.Services;

namespace ProjectileSimulator.Gunny;

/// <summary>
/// Tìm bộ tham số phù hợp nhất với dữ liệu bắn thực tế, cho từng mô hình:
/// <list type="bullet">
///   <item>Model A (vật lý): Grid search → Levenberg–Marquardt trên hệ số vận tốc (và hệ số gió nếu dữ liệu có nhiều mức gió).</item>
///   <item>Model B (siêu cao): bình phương tối thiểu tuyến tính cho Angle = c₀ + a·(RefD − D) + k·W.</item>
///   <item>Model C, D, E: tự khớp từ dữ liệu (chọn phương pháp theo LOOCV) — kết quả luôn được dùng ngay.</item>
/// </list>
/// Chỉ dùng dữ liệu thực nghiệm người dùng đã nhập; không sinh dữ liệu giả để đánh giá.
/// </summary>
public sealed class ModelOptimizationService
{
    private readonly GunnyModelRegistry _registry;
    private readonly OptimizationService _physicsOptimizer;
    private readonly GunnySettingsStore _store;

    public ModelOptimizationService(GunnyModelRegistry registry, OptimizationService physicsOptimizer, GunnySettingsStore store)
    {
        _registry = registry;
        _physicsOptimizer = physicsOptimizer;
        _store = store;
    }

    public List<ModelOptimizationResult> OptimizeAll(IEnumerable<string>? modelIds, CancellationToken ct = default)
    {
        var ctx = _registry.CreateContext();
        return _registry.Select(modelIds).Select(m => m switch
        {
            GunnySuperHighModel sh => OptimizeSuperHigh(sh, ctx),
            PhysicsBaselineModel ph => OptimizePhysics(ph, ctx, ct),
            _ => Empirical(m, ctx)
        }).ToList();
    }

    /// <summary>Lưu tham số đề xuất của Model A hoặc B vào cấu hình.</summary>
    public GunnyResearchSettings Apply(string modelId, Dictionary<string, double> p)
    {
        var s = _store.Get();
        switch (modelId)
        {
            case GunnySuperHighModel.ModelId:
                if (p.TryGetValue("baseAngle", out var b)) s.SuperHigh.BaseAngle = b;
                if (p.TryGetValue("anglePerDivision", out var a)) s.SuperHigh.AnglePerDivision = a;
                if (p.TryGetValue("windAngleFactor", out var k)) s.SuperHigh.WindAngleFactor = k;
                break;
            case PhysicsBaselineModel.ModelId:
                // Giữ nguyên quy đổi cự ly hiện tại: nếu vẫn neo theo phát tham chiếu thì đổi hệ số vận tốc sẽ
                // làm quy đổi đổi theo và triệt tiêu hiệu chỉnh.
                s.Calibration.Mode = CalibrationMode.Manual;
                if (p.TryGetValue("velocityMultiplier", out var vm)) s.Physics.Base.Calibration.VelocityMultiplier = vm;
                if (p.TryGetValue("windMultiplier", out var wm)) s.Physics.Base.Calibration.WindMultiplier = wm;
                break;
            default:
                throw new ArgumentException("Chỉ áp dụng được tham số cho mô hình A (physics) và B (superHigh); mô hình C/D/E tự khớp từ dữ liệu.");
        }
        return _store.Save(s);
    }

    // ------------------------------------------------------------------ Model B

    internal static ModelOptimizationResult OptimizeSuperHigh(GunnySuperHighModel m, ModelContext ctx)
    {
        var before = m.Evaluate(ctx);
        var p = ctx.Settings.SuperHigh;
        var data = GunnySuperHighModel.Applicable(ctx).ToList();
        var windVaries = data.Count > 0 && data.Max(e => e.SignedWind) - data.Min(e => e.SignedWind) > 1e-9;

        // y = Angle (− k·W nếu gió không đổi), x = RefD − D_thực tế. Hồi quy tuyến tính bình phương tối thiểu.
        var samples = data.Select(e => new EmpiricalSample(
            new[] { p.ReferenceDistance - e.EffectiveLanding!.Value, e.SignedWind },
            windVaries ? e.Angle : e.Angle - p.WindAngleFactor * e.SignedWind, e.Id)).ToList();
        var spec = windVaries
            ? new RegressionSpec(EmpiricalMethod.LinearRegression, "Angle = c₀ + a·(RefD − D) + k·W", x => new[] { 1, x[0], x[1] }, new[] { "1", "(RefD−D)", "W" })
            : new RegressionSpec(EmpiricalMethod.LinearRegression, "Angle = c₀ + a·(RefD − D) (giữ nguyên k)", x => new[] { 1, x[0] }, new[] { "1", "(RefD−D)" });

        var need = spec.Terms.Length + 1;
        if (samples.Count < need)
            return Result(m, before, null, null, false,
                $"Chưa đủ dữ liệu: cần ≥ {need} lần bắn siêu cao (lực {p.Power:0.#}, góc ≥ {p.MinAngle:0.#}°) có điểm rơi, hiện có {samples.Count}." +
                (windVaries ? "" : " Dữ liệu chưa có nhiều mức gió nên không ước lượng được hệ số gió."));

        var (fit, reason) = EmpiricalFitter.TrainRegression(samples, spec);
        if (fit == null || Math.Abs(fit.Coefficients[1]) < 1e-6)
            return Result(m, before, null, null, false, $"Không ước lượng được: {reason ?? "hệ số độ/khoảng ≈ 0"}.");

        var suggested = new Dictionary<string, double>
        {
            ["baseAngle"] = fit.Coefficients[0],
            ["anglePerDivision"] = fit.Coefficients[1],
            ["windAngleFactor"] = windVaries ? fit.Coefficients[2] : p.WindAngleFactor,
        };
        var s2 = GunnySettingsStore.Clone(ctx.Settings);
        s2.SuperHigh.BaseAngle = suggested["baseAngle"];
        s2.SuperHigh.AnglePerDivision = suggested["anglePerDivision"];
        s2.SuperHigh.WindAngleFactor = suggested["windAngleFactor"];
        var after = m.Evaluate(new ModelContext { Settings = s2, Experiments = ctx.Experiments, Engine = ctx.Engine });
        return Result(m, before, after, suggested, true,
            $"Bình phương tối thiểu trên {samples.Count} lần bắn siêu cao{(windVaries ? "" : "; hệ số gió giữ nguyên vì dữ liệu chỉ có một mức gió")}.",
            "Bình phương tối thiểu tuyến tính");
    }

    // ------------------------------------------------------------------ Model A

    internal ModelOptimizationResult OptimizePhysics(PhysicsBaselineModel m, ModelContext ctx, CancellationToken ct)
    {
        var before = m.Evaluate(ctx);
        var cal = ctx.Settings.Calibration;
        var data = ctx.WithLanding.Where(e => e.Angle <= 90 && e.EffectiveLanding > 0).ToList();
        if (data.Count < 2)
            return Result(m, before, null, null, false, $"Chưa đủ dữ liệu: cần ≥ 2 lần bắn có điểm rơi > 0, hiện có {data.Count}.");

        var windVaries = data.Max(e => e.SignedWind) - data.Min(e => e.SignedWind) > 1e-9;
        var experiments = data.Select(e => new ExperimentData
        {
            Label = e.Id.ToString(),
            Distance = Math.Clamp(DistanceCalibrationService.GunnyToSim(cal, Math.Max(0.1, e.Distance)), 0.1, 100),
            Angle = e.Angle,
            Power = e.Power,
            Wind = DistanceCalibrationService.GunnyWindToSim(cal, e.SignedWind),
            ObservedLandingX = DistanceCalibrationService.GunnyToSim(cal, e.EffectiveLanding!.Value),
            ObservedLandingY = 0,
        }).ToList();
        var parameters = new List<ParameterRange>
        {
            new() { Name = TunableParameter.VelocityMultiplier, Enabled = true, Min = 0.2, Max = 5, Steps = 49 },
        };
        if (windVaries) parameters.Add(new() { Name = TunableParameter.WindMultiplier, Enabled = true, Min = -5, Max = 5, Steps = 21 });

        var opt = _physicsOptimizer.Optimize(new OptimizationRequest
        {
            Base = ctx.Settings.Physics.Base.Clone(),
            Experiments = experiments,
            Method = OptimizationMethod.GridThenLeastSquares,
            Parameters = parameters,
            MaxIterations = 60,
            FlightTimeWeight = 0,
        }, ct);

        var suggested = new Dictionary<string, double> { ["velocityMultiplier"] = opt.BestParameters["VelocityMultiplier"] };
        if (windVaries) suggested["windMultiplier"] = opt.BestParameters["WindMultiplier"];

        var s2 = GunnySettingsStore.Clone(ctx.Settings);
        s2.Physics.Base.Calibration.VelocityMultiplier = suggested["velocityMultiplier"];
        if (windVaries) s2.Physics.Base.Calibration.WindMultiplier = suggested["windMultiplier"];
        var after = m.Evaluate(new ModelContext { Settings = s2, Experiments = ctx.Experiments, Engine = ctx.Engine });
        return Result(m, before, after, suggested, true,
            $"Grid search → Levenberg–Marquardt trên {data.Count} lần bắn, quy đổi cự ly giữ cố định ({cal.SimUnitsPerDivision:0.####} u/khoảng). " +
            "Khi áp dụng, chế độ quy đổi chuyển sang 'Nhập tay' để giữ nguyên hệ số." +
            (windVaries ? "" : " Dữ liệu chỉ có một mức gió nên không tối ưu hệ số gió."),
            "Grid search + Levenberg–Marquardt");
    }

    // ------------------------------------------------------------------ Model C/D/E

    private static ModelOptimizationResult Empirical(IProjectileModel m, ModelContext ctx)
    {
        var ev = m.Evaluate(ctx);
        return new ModelOptimizationResult
        {
            ModelId = m.Id, ModelName = m.Name, Before = ev, After = ev, CanApply = false,
            Method = ev.Method, Message = ev.Message ?? "Mô hình tự khớp từ dữ liệu (đã dùng ngay, không cần áp dụng). " +
                (ev.LoocvRmse.HasValue ? $"Sai số kiểm định chéo (LOOCV RMSE) = {ev.LoocvRmse:0.###} khoảng — ước lượng sai số cho lần bắn mới." : "")
        };
    }

    private static ModelOptimizationResult Result(IProjectileModel m, ModelEvaluation before, ModelEvaluation? after,
        Dictionary<string, double>? suggested, bool canApply, string message, string? method = null) => new()
    {
        ModelId = m.Id, ModelName = m.Name, Before = before, After = after ?? before,
        SuggestedParameters = suggested, CanApply = canApply, Message = message, Method = method
    };
}

public sealed class ModelOptimizationResult
{
    public string ModelId { get; init; } = "";
    public string ModelName { get; init; } = "";
    public string? Method { get; init; }

    /// <summary>Sai số với tham số hiện tại.</summary>
    public ModelEvaluation Before { get; init; } = new();

    /// <summary>Sai số với tham số đề xuất (bằng Before nếu không có đề xuất).</summary>
    public ModelEvaluation After { get; init; } = new();
    public Dictionary<string, double>? SuggestedParameters { get; init; }
    public bool CanApply { get; init; }
    public string Message { get; init; } = "";
}
