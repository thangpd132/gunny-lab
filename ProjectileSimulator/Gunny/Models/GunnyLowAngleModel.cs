using ProjectileSimulator.Gunny.Empirical;

namespace ProjectileSimulator.Gunny.Models;

/// <summary>
/// Model D – Gunny góc thấp (mặc định 10°, 20°, 30°). Mỗi góc kỹ thuật là một mô hình con
/// độc lập, khớp từ dữ liệu riêng của góc đó. Không dùng quy tắc của góc 65 hay siêu cao.
/// </summary>
public sealed class GunnyLowAngleModel : IProjectileModel
{
    public const string ModelId = "lowAngle";

    public string Id => ModelId;
    public string Name => "D · Gunny góc thấp";
    public string Description =>
        "Các kỹ thuật góc thấp (mặc định 10°, 20°, 30°; có thể thêm/bớt). Mỗi góc có tham số riêng, học từ các lần bắn " +
        "thực tế ở đúng góc đó; chọn phương pháp theo sai số kiểm định chéo.";
    public string Applicability => "Chỉ các lần bắn có góc trùng một góc kỹ thuật (± dung sai).";

    public IReadOnlyList<ModelSource> Sources { get; } = new[]
    {
        new ModelSource("Tổng hợp công thức bắn góc thấp 10–20 – gunnymoinhat.blogspot.com", "https://gunnymoinhat.blogspot.com/2015/04/tong-hop-cong-thuc-ban-goc-thap-10-20.html",
            "Bài viết năm 2015 (có thể đã cũ so với phiên bản hiện tại). Phiên làm việc không truy cập được trang → " +
            "chưa trích được bảng/công thức nào; mô hình chỉ học từ dữ liệu bạn nhập."),
    };

    public IReadOnlyList<string> Assumptions { get; } = new[]
    {
        "Chưa xác định công thức góc thấp: không có hằng số nào được đặt sẵn.",
        "Mỗi góc kỹ thuật độc lập; dữ liệu góc 10° không ảnh hưởng mô hình 20°.",
    };

    private IEnumerable<FixedAngleEmpirical> Build(ModelContext ctx) =>
        ctx.Settings.LowAngle.Techniques.Select(a => new FixedAngleEmpirical(ctx.Experiments, a, ctx.Settings.LowAngle.AngleTolerance));

    public ModelInfo Describe(ModelContext ctx)
    {
        var subs = Build(ctx).ToList();
        var fitted = subs.Count(s => s.Fit != null);
        return new ModelInfo
        {
            Id = Id, Name = Name, Description = Description, Applicability = Applicability, Sources = Sources, Assumptions = Assumptions,
            Status = fitted > 0 ? ModelStatus.Fitted : ModelStatus.InsufficientData,
            StatusMessage = fitted > 0 ? $"Đã khớp {fitted}/{subs.Count} góc kỹ thuật." : "Chưa xác định: chưa đủ dữ liệu cho góc kỹ thuật nào.",
            Parameters = new[]
            {
                new ModelParameterInfo { Name = "angleTolerance", Label = "Dung sai góc", Value = ctx.Settings.LowAngle.AngleTolerance, Unit = "°", Editable = true, Description = "" },
            },
            SubModels = subs.Select(s => Gunny65DegreeModel.Sub(s, $"Góc {s.Angle:0.#}°")).ToList()
        };
    }

    public ModelPrediction Predict(GunnyShot shot, ModelContext ctx)
    {
        var f = Build(ctx).FirstOrDefault(s => Math.Abs(shot.Angle - s.Angle) <= s.Tolerance);
        if (f == null)
            return new ModelPrediction { ModelId = Id, ModelName = Name, Supported = false, Reason = $"Góc {shot.Angle:0.#}° không thuộc các góc kỹ thuật {string.Join("/", ctx.Settings.LowAngle.Techniques)}°." };
        var (landing, ext, reason) = f.Predict(shot.Power, shot.SignedWind);
        return new ModelPrediction { ModelId = Id, ModelName = $"{Name} {f.Angle:0.#}°", Supported = landing.HasValue, Landing = landing, Extrapolated = ext, Reason = reason };
    }

    public ModelAimResult SuggestAim(double target, double wind, ModelContext ctx)
    {
        var results = Build(ctx).Select(f => Gunny65DegreeModel.AimFixed(this, f, target, wind, $"{Name} {f.Angle:0.#}°")).ToList();
        var sols = results.SelectMany(r => r.Solutions).OrderBy(s => Math.Abs(s.Error)).ToList();
        return new ModelAimResult
        {
            ModelId = Id, ModelName = Name, Solutions = sols,
            Message = string.Join(" · ", results.Where(r => r.Message != null).Select(r => $"{r.ModelName}: {r.Message}")) is { Length: > 0 } m ? m : null
        };
    }

    public ModelEvaluation Evaluate(ModelContext ctx)
    {
        var subs = Build(ctx).ToList();
        var evals = subs.Select(s => Gunny65DegreeModel.EvaluateFixed(this, s)).ToList();
        var devs = evals.SelectMany(e => e.Deviations).ToList();
        return ModelEvaluation.From(this, evals.Sum(e => e.Applicable), devs,
            devs.Count == 0 ? "Chưa có lần bắn thực tế nào ở các góc kỹ thuật thấp." : null,
            method: string.Join(" | ", subs.Select((s, i) => $"{s.Angle:0.#}°: {evals[i].Method ?? "chưa xác định"}")),
            formula: string.Join(" | ", subs.Where(s => s.Fit != null).Select(s => $"{s.Angle:0.#}°: {s.Fit!.Formula}")));
    }
}
