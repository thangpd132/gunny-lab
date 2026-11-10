using ProjectileSimulator.Gunny.Empirical;

namespace ProjectileSimulator.Gunny.Models;

/// <summary>
/// Model C – Gunny góc 65°. KHÔNG có công thức lực đặt sẵn: chưa có dữ liệu đáng tin cậy nào
/// được kiểm chứng trong dự án, nên mô hình chỉ học từ các lần bắn thực tế ở góc 65°
/// (nội suy / hồi quy, chọn theo LOOCV). Không dùng quy tắc chỉnh gió của kỹ thuật siêu cao.
/// </summary>
public sealed class Gunny65DegreeModel : IProjectileModel
{
    public const string ModelId = "angle65";

    public string Id => ModelId;
    public string Name => "C · Gunny góc 65°";
    public string Description =>
        "Kỹ thuật bắn cố định góc 65°, điều chỉnh bằng lực. Điểm rơi = f(lực, gió) được học hoàn toàn từ dữ liệu thực nghiệm " +
        "bạn nhập (nội suy từng đoạn, hồi quy tuyến tính/bậc 2, bảng tra) — phương pháp có sai số kiểm định chéo nhỏ nhất được chọn.";
    public string Applicability => "Chỉ các lần bắn có góc = 65° (± dung sai). Ảnh hưởng của gió chỉ được mô hình hoá khi dữ liệu có nhiều mức gió.";

    public IReadOnlyList<ModelSource> Sources { get; } = new[]
    {
        new ModelSource("Bài tập 65 độ, siêu cao và cao thấp – gunny.360game.vn", "https://gunny.360game.vn/tin-tuc/cong-dong/hoan-thanh-bai-tap-65-do-sieu-cao-va-cao-thap.html",
            "Theo mô tả của người dùng: góc 65 nhạy với lực; gió có thể cần chỉnh góc, gió nhỏ có thể cần chỉnh lực. " +
            "Chưa đối chiếu trực tiếp (trang bị chặn mạng) và chưa có số liệu cụ thể → không đặt công thức lực."),
    };

    public IReadOnlyList<string> Assumptions { get; } = new[]
    {
        "Chưa xác định công thức lực cho góc 65°: mọi dự đoán chỉ đến từ dữ liệu bạn nhập.",
        "Không dùng quy tắc '2° mỗi đơn vị gió' của bắn siêu cao cho góc 65°.",
        "Ngoài phạm vi lực/gió đã có dữ liệu, kết quả là ngoại suy và được đánh dấu.",
    };

    private FixedAngleEmpirical Build(ModelContext ctx) =>
        new(ctx.Experiments, ctx.Settings.Angle65.Angle, ctx.Settings.Angle65.AngleTolerance);

    public ModelInfo Describe(ModelContext ctx)
    {
        var f = Build(ctx);
        var p = ctx.Settings.Angle65;
        return new ModelInfo
        {
            Id = Id, Name = Name, Description = Description, Applicability = Applicability,
            Sources = Sources, Assumptions = Assumptions, Status = f.Status, StatusMessage = f.StatusMessage,
            Parameters = new[]
            {
                new ModelParameterInfo { Name = "angle", Label = "Góc kỹ thuật", Value = p.Angle, Unit = "°", Editable = true, Description = "Góc cố định của kỹ thuật." },
                new ModelParameterInfo { Name = "angleTolerance", Label = "Dung sai góc", Value = p.AngleTolerance, Unit = "°", Editable = true, Description = "Lần bắn có góc lệch nhiều hơn sẽ không thuộc mô hình." },
            },
            SubModels = new[] { Sub(f, $"Góc {p.Angle:0.#}°") }
        };
    }

    public ModelPrediction Predict(GunnyShot shot, ModelContext ctx)
    {
        var f = Build(ctx);
        if (Math.Abs(shot.Angle - f.Angle) > f.Tolerance)
            return new ModelPrediction { ModelId = Id, ModelName = Name, Supported = false, Reason = $"Chỉ áp dụng cho góc {f.Angle:0.#}°." };
        var (landing, ext, reason) = f.Predict(shot.Power, shot.SignedWind);
        return new ModelPrediction { ModelId = Id, ModelName = Name, Supported = landing.HasValue, Landing = landing, Extrapolated = ext, Reason = reason };
    }

    public ModelAimResult SuggestAim(double target, double wind, ModelContext ctx) => AimFixed(this, Build(ctx), target, wind);

    public ModelEvaluation Evaluate(ModelContext ctx) => EvaluateFixed(this, Build(ctx));

    // ------------------------------------------------------------------ dùng chung với Model D

    internal static SubModelInfo Sub(FixedAngleEmpirical f, string name) => new()
    {
        Name = name, Status = f.Status, StatusMessage = f.StatusMessage, Formula = f.Fit?.Formula,
        Samples = f.Data.Count, LoocvRmse = f.Report.BestReport?.LoocvRmse
    };

    internal static ModelAimResult AimFixed(IProjectileModel m, FixedAngleEmpirical f, double target, double wind, string? label = null)
    {
        var (probe, _, reason) = f.Predict(50, wind);
        if (probe == null) return new ModelAimResult { ModelId = m.Id, ModelName = label ?? m.Name, Message = reason };
        var sols = f.SolvePower(target, wind).Select(s => new AimSolution
        {
            ModelId = m.Id, ModelName = label ?? m.Name, Angle = f.Angle, Power = s.power, SignedWind = wind, TargetDistance = target,
            PredictedLanding = s.landing, Error = s.landing - target, Extrapolated = s.extrapolated,
            Note = $"{f.Fit!.Label}{(s.extrapolated ? " · NGOẠI SUY ngoài phạm vi dữ liệu" : "")}"
        }).OrderBy(s => Math.Abs(s.Error)).ToList();
        return new ModelAimResult { ModelId = m.Id, ModelName = label ?? m.Name, Solutions = sols };
    }

    internal static ModelEvaluation EvaluateFixed(IProjectileModel m, FixedAngleEmpirical f)
    {
        var devs = f.Data.Select(e =>
        {
            var (y, _, reason) = f.Predict(e.Power, e.SignedWind);
            return PhysicsBaselineModel.Dev(e, y, reason);
        }).ToList();
        return ModelEvaluation.From(m, f.Data.Count, devs, f.Fit == null ? f.StatusMessage : null,
            f.Fit?.Label, f.Fit?.Formula, f.Report.BestReport?.LoocvRmse);
    }
}
