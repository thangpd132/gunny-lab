using System.ComponentModel.DataAnnotations;
using ProjectileSimulator.Models;

namespace ProjectileSimulator.Gunny;

/// <summary>
/// Toàn bộ cấu hình của khu vực nghiên cứu Gunny (lưu thành JSON, giữ lại sau khi tắt ứng dụng).
/// </summary>
public sealed class GunnyResearchSettings
{
    public DistanceCalibration Calibration { get; set; } = new();
    public PhysicsBaselineParameters Physics { get; set; } = new();
    public SuperHighParameters SuperHigh { get; set; } = new();
    public Angle65Parameters Angle65 { get; set; } = new();
    public LowAngleParameters LowAngle { get; set; } = new();
    public CustomEmpiricalParameters Custom { get; set; } = new();
    public ConfirmationTolerances Confirmation { get; set; } = new();

    /// <summary>Chỉ dùng các lần bắn của vũ khí này cho mô hình thực nghiệm (null/rỗng = tất cả).</summary>
    [MaxLength(80)] public string? WeaponFilter { get; set; }
}

public enum CalibrationMode
{
    /// <summary>
    /// Neo theo phát tham chiếu: mô phỏng vật lý phát (ReferenceAngle, ReferencePower, gió 0);
    /// điểm rơi được gán bằng ReferenceDistance khoảng Gunny.
    /// </summary>
    ReferenceShot,

    /// <summary>Người dùng nhập trực tiếp số đơn vị simulator cho một khoảng Gunny.</summary>
    Manual
}

/// <summary>
/// Quy đổi khoảng cách simulator ↔ Gunny. KHÔNG mặc định 1 u = 1 khoảng Gunny.
/// <code>
///   GunnyDistance     = SimDistance × CalibrationFactor
///   CalibrationFactor = 1 / SimUnitsPerDivision
///   PixelsPerDivision = ReferenceScreenWidthPx / ScreenDivisions
/// </code>
/// </summary>
public sealed class DistanceCalibration
{
    /// <summary>Số khoảng chia trên một màn hình Gunny (theo mô tả tài liệu [1]: 9).</summary>
    [Range(1, 100)] public int ScreenDivisions { get; set; } = 9;

    /// <summary>Chiều rộng màn hình tham chiếu (pixel) — để quy đổi số đo pixel sang khoảng.</summary>
    [Range(100, 10000)] public double ReferenceScreenWidthPx { get; set; } = 1000;

    /// <summary>Số đơn vị simulator tương ứng với 1 khoảng Gunny. ≤ 0 = chưa hiệu chỉnh.</summary>
    [Range(0, 1000)] public double SimUnitsPerDivision { get; set; }

    public CalibrationMode Mode { get; set; } = CalibrationMode.ReferenceShot;

    [Range(0, 90)] public double ReferenceAngle { get; set; } = 81;
    [Range(1, 100)] public double ReferencePower { get; set; } = 100;
    [Range(0.1, 100)] public double ReferenceDistance { get; set; } = 9;

    /// <summary>Số đơn vị gió simulator ứng với 1 đơn vị gió Gunny (giả định, mặc định 1).</summary>
    [Range(-100, 100)] public double WindScale { get; set; } = 1;
}

/// <summary>Model A – các tham số vật lý (dùng lại SimulationRequest của engine hiện tại).</summary>
public sealed class PhysicsBaselineParameters
{
    /// <summary>
    /// Cấu hình engine vật lý. Distance/Angle/Power/Wind trong đây bị ghi đè cho mỗi lần bắn.
    /// </summary>
    public SimulationRequest Base { get; set; } = new()
    {
        TargetCollisionEnabled = false,
        MaxTrajectoryPoints = 0,
        DeltaTime = 0.004,
        Integrator = Physics.IntegratorType.SemiImplicitEuler,
        WindEnabled = true,
        WindMode = Physics.WindMode.WindAcceleration,
        DragType = Physics.DragType.None,
    };

    /// <summary>Góc ưu tiên khi chọn một phương án vật lý để so sánh (nhiều góc cùng trúng).</summary>
    [Range(1, 89)] public double PreferredAngle { get; set; } = 45;
}

/// <summary>
/// Model B – siêu cao: Angle = BaseAngle + AnglePerDivision·(ReferenceDistance − D) + WindAngleFactor·W.
/// </summary>
public sealed class SuperHighParameters
{
    [Range(0, 90)] public double BaseAngle { get; set; } = 81;
    [Range(0.1, 100)] public double ReferenceDistance { get; set; } = 9;
    [Range(-10, 10)] public double AnglePerDivision { get; set; } = 1;
    [Range(-20, 20)] public double WindAngleFactor { get; set; } = 2;
    [Range(1, 100)] public double Power { get; set; } = 100;

    /// <summary>Lần bắn chỉ được coi là "siêu cao" khi |Power − Power| ≤ dung sai này.</summary>
    [Range(0, 50)] public double PowerTolerance { get; set; } = 0.5;

    /// <summary>Góc nhỏ nhất coi là kỹ thuật siêu cao (giả định, chỉnh được).</summary>
    [Range(0, 90)] public double MinAngle { get; set; } = 70;
}

/// <summary>Model C – góc 65°: không có công thức cố định, chỉ học từ dữ liệu.</summary>
public sealed class Angle65Parameters
{
    [Range(0, 90)] public double Angle { get; set; } = 65;
    [Range(0, 10)] public double AngleTolerance { get; set; } = 0.5;
}

/// <summary>Model D – góc thấp: mỗi góc kỹ thuật là một mô hình con độc lập.</summary>
public sealed class LowAngleParameters
{
    public List<double> Techniques { get; set; } = new() { 10, 20, 30 };
    [Range(0, 10)] public double AngleTolerance { get; set; } = 0.5;
}

/// <summary>Model E – mô hình thực nghiệm tuỳ chỉnh trên toàn bộ dữ liệu (lọc theo khoảng góc).</summary>
public sealed class CustomEmpiricalParameters
{
    [Range(0, 90)] public double MinAngle { get; set; } = 0;
    [Range(0, 90)] public double MaxAngle { get; set; } = 90;

    /// <summary>Các phương pháp được phép thử (rỗng = tất cả).</summary>
    public List<Empirical.EmpiricalMethod> Methods { get; set; } = new();
}

/// <summary>Ngưỡng để coi một phương án là "đã xác nhận bằng dữ liệu thực nghiệm".</summary>
public sealed class ConfirmationTolerances
{
    [Range(0, 10)] public double Angle { get; set; } = 0.5;
    [Range(0, 20)] public double Power { get; set; } = 1;
    [Range(0, 10)] public double Wind { get; set; } = 0.5;
    [Range(0, 10)] public double Distance { get; set; } = 0.25;

    /// <summary>|điểm rơi thực tế − mục tiêu| ≤ ngưỡng này thì coi như trúng.</summary>
    [Range(0, 10)] public double Landing { get; set; } = 0.25;
}
