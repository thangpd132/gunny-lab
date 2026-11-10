namespace ProjectileSimulator.Gunny.Models;

/// <summary>
/// Model B – Gunny Super High (bắn siêu cao, lực 100).
/// <code>
///   Angle = BaseAngle + AnglePerDivision · (ReferenceDistance − Distance) + WindAngleFactor · Wind
///   mặc định:  Angle = 81 + (9 − Distance) + 2·Wind,   Power = 100
/// </code>
/// Đây là quy tắc NGẮM (cho góc khi biết khoảng cách), không mô tả quỹ đạo. Dự đoán điểm rơi
/// là phép đảo của quy tắc: Distance = RefD + (BaseAngle + k·Wind − Angle) / AnglePerDivision.
/// Chỉ áp dụng cho lực ≈ 100 và góc trong vùng siêu cao — không dùng cho góc khác.
/// </summary>
public sealed class GunnySuperHighModel : IProjectileModel
{
    public const string ModelId = "superHigh";

    public string Id => ModelId;
    public string Name => "B · Gunny siêu cao (lực 100)";
    public string Description =>
        "Quy tắc thực nghiệm của cộng đồng Gunny PC cho kỹ thuật bắn siêu cao: màn hình chia 9 khoảng, mốc tham chiếu " +
        "khoảng cách 9 – góc 81° – lực 100 – gió 0; mỗi khoảng gần hơn tăng 1°, mỗi đơn vị gió thuận cộng 2°.";
    public string Applicability => "Lực ≈ 100 (± dung sai) và góc từ MinAngle đến 90°. Không áp dụng cho góc 65 hay góc thấp.";

    public IReadOnlyList<ModelSource> Sources { get; } = new[]
    {
        new ModelSource("Công thức bắn siêu cao – gunny.360game.vn", "https://gunny.360game.vn/tin-tuc/hang-ngay/cong-thuc-ban-sieu-cao.html",
            "Công thức Angle = 81 + (9 − Distance) + 2·Wind lấy theo nội dung người dùng trích từ bài viết này. " +
            "Phiên làm việc không truy cập được trang (bị chặn mạng) nên CHƯA đối chiếu trực tiếp."),
        new ModelSource("Bài tập 65 độ, siêu cao và cao thấp – gunny.360game.vn", "https://gunny.360game.vn/tin-tuc/cong-dong/hoan-thanh-bai-tap-65-do-sieu-cao-va-cao-thap.html",
            "Tài liệu cộng đồng liên quan; chưa đối chiếu trực tiếp."),
    };

    public IReadOnlyList<string> Assumptions { get; } = new[]
    {
        "Đây là công thức thực nghiệm của cộng đồng, không phải mã nguồn chính thức của game; có thể không đúng với phiên bản hiện tại.",
        "Wind là gió có dấu theo chiều bắn (thuận dương, ngược âm), đơn vị gió hiển thị trong game.",
        "Distance tính bằng khoảng chia màn hình Gunny (không phải đơn vị simulator).",
        "Góc tối thiểu coi là 'siêu cao' (MinAngle, mặc định 70°) là giả định để giới hạn phạm vi áp dụng.",
    };

    public ModelInfo Describe(ModelContext ctx)
    {
        var p = ctx.Settings.SuperHigh;
        return new ModelInfo
        {
            Id = Id, Name = Name, Description = Description, Applicability = Applicability,
            Sources = Sources, Assumptions = Assumptions, Status = ModelStatus.Ready, SupportsFlightTime = false,
            StatusMessage = $"Công thức hiện tại: Angle = {p.BaseAngle:0.###} + {p.AnglePerDivision:0.###}·({p.ReferenceDistance:0.###} − D) + {p.WindAngleFactor:0.###}·W, lực {p.Power:0.#}.",
            Parameters = new[]
            {
                P("baseAngle", "Góc mốc", p.BaseAngle, "Góc tại khoảng cách mốc, gió 0.", "°"),
                P("referenceDistance", "Khoảng cách mốc", p.ReferenceDistance, "Khoảng Gunny.", "khoảng"),
                P("anglePerDivision", "Độ / khoảng", p.AnglePerDivision, "Góc tăng thêm cho mỗi khoảng gần hơn mốc.", "°"),
                P("windAngleFactor", "Độ / đơn vị gió", p.WindAngleFactor, "Góc cộng thêm cho mỗi đơn vị gió thuận.", "°"),
                P("power", "Lực", p.Power, "Lực cố định của kỹ thuật.", ""),
                P("powerTolerance", "Dung sai lực", p.PowerTolerance, "Lần bắn có |lực − Lực| lớn hơn sẽ không thuộc mô hình.", ""),
                P("minAngle", "Góc tối thiểu", p.MinAngle, "Dưới góc này không coi là siêu cao (giả định).", "°"),
            }
        };
    }

    /// <summary>Góc khuyến nghị theo công thức.</summary>
    public static double AngleFor(SuperHighParameters p, double distance, double signedWind) =>
        p.BaseAngle + p.AnglePerDivision * (p.ReferenceDistance - distance) + p.WindAngleFactor * signedWind;

    /// <summary>Đảo công thức: khoảng cách mà góc này "nhắm tới".</summary>
    public static double DistanceFor(SuperHighParameters p, double angle, double signedWind)
    {
        if (Math.Abs(p.AnglePerDivision) < 1e-12) throw new ArgumentException("AnglePerDivision = 0: không đảo được công thức.");
        return p.ReferenceDistance + (p.BaseAngle + p.WindAngleFactor * signedWind - angle) / p.AnglePerDivision;
    }

    public static bool IsApplicable(SuperHighParameters p, double angle, double power, out string? reason)
    {
        if (Math.Abs(power - p.Power) > p.PowerTolerance) { reason = $"Chỉ áp dụng cho lực {p.Power:0.#} (± {p.PowerTolerance:0.#})."; return false; }
        if (angle < p.MinAngle || angle > 90) { reason = $"Chỉ áp dụng cho góc {p.MinAngle:0.#}–90°."; return false; }
        reason = null;
        return true;
    }

    public ModelPrediction Predict(GunnyShot shot, ModelContext ctx)
    {
        var p = ctx.Settings.SuperHigh;
        if (!IsApplicable(p, shot.Angle, shot.Power, out var reason))
            return new ModelPrediction { ModelId = Id, ModelName = Name, Supported = false, Reason = reason };
        return new ModelPrediction { ModelId = Id, ModelName = Name, Supported = true, Landing = DistanceFor(p, shot.Angle, shot.SignedWind) };
    }

    public ModelAimResult SuggestAim(double target, double wind, ModelContext ctx)
    {
        var p = ctx.Settings.SuperHigh;
        var angle = AngleFor(p, target, wind);
        if (angle < p.MinAngle || angle > 90)
            return new ModelAimResult
            {
                ModelId = Id, ModelName = Name,
                Message = $"Công thức cho góc {angle:0.#}°, nằm ngoài vùng siêu cao {p.MinAngle:0.#}–90° → không áp dụng."
            };
        // Góc trong game chỉnh theo từng độ; giữ cả giá trị lẻ và giá trị làm tròn.
        var sols = new List<AimSolution>();
        foreach (var a in new[] { angle, Math.Round(angle) }.Distinct())
        {
            var landing = DistanceFor(p, a, wind);
            sols.Add(new AimSolution
            {
                ModelId = Id, ModelName = Name, Angle = a, Power = p.Power, SignedWind = wind, TargetDistance = target,
                PredictedLanding = landing, Error = landing - target,
                Note = a == angle ? "Góc chính xác theo công thức" : "Góc làm tròn (game chỉnh theo từng độ)"
            });
        }
        return new ModelAimResult { ModelId = Id, ModelName = Name, Solutions = sols.OrderBy(s => Math.Abs(s.Error)).ToList() };
    }

    public ModelEvaluation Evaluate(ModelContext ctx)
    {
        var p = ctx.Settings.SuperHigh;
        var list = Applicable(ctx).ToList();
        var devs = list.Select(e => PhysicsBaselineModel.Dev(e, DistanceFor(p, e.Angle, e.SignedWind))).ToList();
        return ModelEvaluation.From(this, list.Count, devs, formula: $"Angle = {p.BaseAngle:0.###} + {p.AnglePerDivision:0.###}·({p.ReferenceDistance:0.###} − D) + {p.WindAngleFactor:0.###}·W");
    }

    public static IEnumerable<GunnyExperiment> Applicable(ModelContext ctx) =>
        ctx.WithLanding.Where(e => IsApplicable(ctx.Settings.SuperHigh, e.Angle, e.Power, out _));

    private static ModelParameterInfo P(string n, string l, double v, string d, string u) =>
        new() { Name = n, Label = l, Value = v, Description = d, Editable = true, Unit = u };
}
