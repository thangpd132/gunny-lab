using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ProjectileSimulator.Gunny;

// ============================================================================
// Kiểu dữ liệu dùng chung cho các mô hình Gunny (V3).
//
// QUY ƯỚC ĐƠN VỊ: mọi khoảng cách trong namespace này tính bằng "khoảng Gunny"
// (số khoảng chia trên màn hình game), KHÔNG phải đơn vị simulator (u).
// Việc quy đổi giữa hai hệ do DistanceCalibrationService đảm nhiệm.
//
// QUY ƯỚC GIÓ: gió có dấu theo chiều bắn — dương = gió thuận (cùng chiều bắn),
// âm = gió ngược.
// ============================================================================

/// <summary>Hướng gió so với chiều bắn.</summary>
public enum WindDirection
{
    /// <summary>Gió thuận: thổi cùng chiều bắn (gió có dấu dương).</summary>
    Thuan,

    /// <summary>Gió ngược: thổi ngược chiều bắn (gió có dấu âm).</summary>
    Nguoc
}

/// <summary>Một lần bắn thực tế người dùng ghi lại từ Gunny PC.</summary>
public sealed class GunnyExperiment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Khoảng cách tới mục tiêu (khoảng Gunny).</summary>
    [Range(0, 100)] public double Distance { get; set; }

    [Range(0, 180)] public double Angle { get; set; }
    [Range(0, 100)] public double Power { get; set; }

    /// <summary>Độ lớn gió (≥ 0). Hướng xem <see cref="WindDirection"/>.</summary>
    [Range(0, 100)] public double Wind { get; set; }

    public WindDirection WindDirection { get; set; } = WindDirection.Thuan;

    [MaxLength(80)] public string? Weapon { get; set; }

    /// <summary>Vị trí đạn rơi thực tế, đo từ người bắn (khoảng Gunny). Có thể để trống.</summary>
    [Range(-100, 100)] public double? ActualLanding { get; set; }

    public bool? HitTarget { get; set; }

    [MaxLength(500)] public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gió có dấu theo chiều bắn.</summary>
    [JsonIgnore] public double SignedWind => WindDirection == WindDirection.Nguoc ? -Wind : Wind;

    /// <summary>
    /// Điểm rơi dùng để tính sai số: ActualLanding nếu có; nếu trống nhưng ghi "Trúng" thì coi
    /// như rơi đúng tại Distance; ngược lại không dùng được (null).
    /// </summary>
    [JsonIgnore] public double? EffectiveLanding => ActualLanding ?? (HitTarget == true ? Distance : null);
}

/// <summary>Đầu vào dự đoán của một mô hình (đơn vị khoảng Gunny).</summary>
public readonly record struct GunnyShot(double Angle, double Power, double SignedWind, double TargetDistance, string? Weapon = null);

/// <summary>Nguồn tham khảo của một mô hình.</summary>
public sealed record ModelSource(string Title, string? Url, string Note);

public sealed class ModelParameterInfo
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public double Value { get; init; }
    public string? Unit { get; init; }
    public string Description { get; init; } = "";
    public bool Editable { get; init; }
}

public enum ModelStatus
{
    /// <summary>Mô hình dùng được ngay (công thức hoặc vật lý).</summary>
    Ready,

    /// <summary>Đã khớp được từ dữ liệu thực nghiệm.</summary>
    Fitted,

    /// <summary>Chưa đủ dữ liệu thực nghiệm — chưa xác định được công thức.</summary>
    InsufficientData
}

/// <summary>Mô tả đầy đủ một mô hình: tên, mô tả, nguồn, điều kiện áp dụng, tham số.</summary>
public sealed class ModelInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Applicability { get; init; } = "";
    public IReadOnlyList<ModelSource> Sources { get; init; } = Array.Empty<ModelSource>();
    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ModelParameterInfo> Parameters { get; init; } = Array.Empty<ModelParameterInfo>();
    public ModelStatus Status { get; init; }
    public string StatusMessage { get; init; } = "";
    public bool SupportsFlightTime { get; init; }

    /// <summary>Các mô hình con (ví dụ từng kỹ thuật góc thấp 10°/20°/30°).</summary>
    public IReadOnlyList<SubModelInfo> SubModels { get; init; } = Array.Empty<SubModelInfo>();
}

public sealed class SubModelInfo
{
    public string Name { get; init; } = "";
    public ModelStatus Status { get; init; }
    public string StatusMessage { get; init; } = "";
    public string? Formula { get; init; }
    public int Samples { get; init; }
    public double? LoocvRmse { get; init; }
}

/// <summary>Dự đoán điểm rơi của một mô hình cho một lần bắn.</summary>
public sealed class ModelPrediction
{
    public string ModelId { get; init; } = "";
    public string ModelName { get; init; } = "";
    public bool Supported { get; init; }

    /// <summary>Lý do không dự đoán được (ngoài điều kiện áp dụng, thiếu dữ liệu…).</summary>
    public string? Reason { get; init; }

    /// <summary>Điểm rơi dự đoán (khoảng Gunny, đo từ người bắn).</summary>
    public double? Landing { get; init; }

    /// <summary>Thời gian bay (s) — chỉ mô hình vật lý hỗ trợ.</summary>
    public double? FlightTime { get; init; }

    /// <summary>True nếu dự đoán nằm ngoài phạm vi dữ liệu đã có (ngoại suy).</summary>
    public bool Extrapolated { get; init; }
}

/// <summary>Một phương án ngắm (góc, lực) do mô hình đề xuất.</summary>
public sealed class AimSolution
{
    public string ModelId { get; init; } = "";
    public string ModelName { get; init; } = "";
    public double Angle { get; init; }
    public double Power { get; init; }
    public double SignedWind { get; init; }
    public double TargetDistance { get; init; }
    public double PredictedLanding { get; init; }

    /// <summary>PredictedLanding − TargetDistance (khoảng Gunny).</summary>
    public double Error { get; init; }
    public double? FlightTime { get; init; }

    /// <summary>
    /// True nếu có ít nhất một lần bắn thực tế gần như cùng thông số đã trúng / rơi đúng mục tiêu.
    /// False = CHỈ là dự đoán của mô hình, chưa kiểm chứng trong game.
    /// </summary>
    public bool Confirmed { get; set; }
    public int ConfirmCount { get; set; }
    public string? Note { get; init; }
    public bool Extrapolated { get; init; }
}
