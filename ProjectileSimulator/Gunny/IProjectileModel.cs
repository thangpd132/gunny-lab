using ProjectileSimulator.Services;

namespace ProjectileSimulator.Gunny;

/// <summary>
/// Một mô hình tính toán đường đạn / cách ngắm. Mỗi kỹ thuật bắn là một lớp riêng —
/// không trộn công thức của các kỹ thuật khác nhau vào một chỗ.
/// Muốn thêm công thức mới: tạo lớp mới cài đặt interface này và đăng ký trong <see cref="GunnyModelRegistry"/>.
/// </summary>
public interface IProjectileModel
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    string Applicability { get; }
    IReadOnlyList<ModelSource> Sources { get; }
    IReadOnlyList<string> Assumptions { get; }

    /// <summary>Mô tả + tham số hiện tại + trạng thái (sẵn sàng / đã khớp / chưa đủ dữ liệu).</summary>
    ModelInfo Describe(ModelContext ctx);

    /// <summary>Dự đoán điểm rơi (khoảng Gunny) cho một lần bắn.</summary>
    ModelPrediction Predict(GunnyShot shot, ModelContext ctx);

    /// <summary>Đề xuất (góc, lực) để rơi đúng khoảng cách mục tiêu với gió đã cho.</summary>
    ModelAimResult SuggestAim(double targetDistance, double signedWind, ModelContext ctx);

    /// <summary>Sai số của mô hình (tham số hiện tại) trên các lần bắn thực tế phù hợp điều kiện áp dụng.</summary>
    ModelEvaluation Evaluate(ModelContext ctx);
}

/// <summary>Mọi thứ một mô hình cần để tính: cấu hình, dữ liệu thực nghiệm, engine vật lý.</summary>
public sealed class ModelContext
{
    public required GunnyResearchSettings Settings { get; init; }

    /// <summary>Dữ liệu thực nghiệm (đã lọc theo vũ khí nếu có).</summary>
    public required IReadOnlyList<GunnyExperiment> Experiments { get; init; }

    public required IPhysicsEngine Engine { get; init; }

    /// <summary>Chỉ các lần bắn có điểm rơi dùng được (ActualLanding, hoặc trúng mục tiêu).</summary>
    public IEnumerable<GunnyExperiment> WithLanding => Experiments.Where(e => e.EffectiveLanding.HasValue);
}

public sealed class ModelAimResult
{
    public string ModelId { get; init; } = "";
    public string ModelName { get; init; } = "";
    public List<AimSolution> Solutions { get; init; } = new();

    /// <summary>Giải thích khi không có phương án (ngoài phạm vi, thiếu dữ liệu…).</summary>
    public string? Message { get; init; }
}

public sealed class ShotDeviation
{
    public Guid ExperimentId { get; init; }
    public double Distance { get; init; }
    public double Angle { get; init; }
    public double Power { get; init; }
    public double SignedWind { get; init; }
    public double Actual { get; init; }
    public double? Predicted { get; init; }

    /// <summary>Predicted − Actual (khoảng Gunny).</summary>
    public double? Error { get; init; }
    public string? Note { get; init; }
}

public sealed class ModelEvaluation
{
    public string ModelId { get; init; } = "";
    public string ModelName { get; init; } = "";

    /// <summary>Số lần bắn thoả điều kiện áp dụng và có điểm rơi.</summary>
    public int Applicable { get; init; }
    public int Evaluated { get; init; }
    public double? Mae { get; init; }
    public double? Rmse { get; init; }
    public double? MaxError { get; init; }

    /// <summary>LOOCV RMSE (chỉ mô hình học từ dữ liệu) — ước lượng sai số trên lần bắn MỚI.</summary>
    public double? LoocvRmse { get; init; }
    public string? Method { get; init; }
    public string? Formula { get; init; }
    public List<ShotDeviation> Deviations { get; init; } = new();
    public string? Message { get; init; }

    public static ModelEvaluation From(IProjectileModel m, int applicable, List<ShotDeviation> devs, string? message = null,
        string? method = null, string? formula = null, double? loocv = null)
    {
        var errs = devs.Where(d => d.Error.HasValue).Select(d => d.Error!.Value).ToList();
        return new ModelEvaluation
        {
            ModelId = m.Id,
            ModelName = m.Name,
            Applicable = applicable,
            Evaluated = errs.Count,
            Mae = errs.Count > 0 ? errs.Average(Math.Abs) : null,
            Rmse = errs.Count > 0 ? Math.Sqrt(errs.Average(e => e * e)) : null,
            MaxError = errs.Count > 0 ? errs.Max(Math.Abs) : null,
            LoocvRmse = loocv,
            Method = method,
            Formula = formula,
            Deviations = devs,
            Message = message ?? (errs.Count == 0 ? "Chưa có lần bắn thực tế nào thoả điều kiện áp dụng của mô hình này." : null)
        };
    }
}
