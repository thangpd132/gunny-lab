using ProjectileSimulator.Models;
using ProjectileSimulator.Services;

namespace ProjectileSimulator.Gunny;

/// <summary>
/// Quy đổi khoảng cách giữa simulator (u) và Gunny (khoảng chia màn hình).
/// Không bao giờ ngầm coi 1 u = 1 khoảng: hệ số luôn lấy từ <see cref="DistanceCalibration"/>.
/// </summary>
public sealed class DistanceCalibrationService
{
    private readonly IPhysicsEngine _engine;

    public DistanceCalibrationService(IPhysicsEngine engine) => _engine = engine;

    /// <summary>Hệ số: khoảng Gunny trên mỗi đơn vị simulator.</summary>
    public static double Factor(DistanceCalibration c)
    {
        EnsureCalibrated(c);
        return 1.0 / c.SimUnitsPerDivision;
    }

    public static double SimToGunny(DistanceCalibration c, double sim) => sim * Factor(c);

    public static double GunnyToSim(DistanceCalibration c, double gunny)
    {
        EnsureCalibrated(c);
        return gunny * c.SimUnitsPerDivision;
    }

    public static double PixelsPerDivision(DistanceCalibration c) => c.ReferenceScreenWidthPx / c.ScreenDivisions;

    /// <summary>Số pixel đo trên ảnh chụp màn hình tham chiếu → khoảng Gunny.</summary>
    public static double PixelsToGunny(DistanceCalibration c, double px) => px / PixelsPerDivision(c);

    public static double GunnyToPixels(DistanceCalibration c, double gunny) => gunny * PixelsPerDivision(c);

    /// <summary>Gió Gunny có dấu → giá trị gió cho engine vật lý.</summary>
    public static double GunnyWindToSim(DistanceCalibration c, double signedWind) => signedWind * c.WindScale;

    /// <summary>
    /// Neo theo phát tham chiếu: mô phỏng phát (ReferenceAngle, ReferencePower, gió 0) bằng engine
    /// vật lý và gán điểm rơi đó = ReferenceDistance khoảng Gunny.
    /// Hệ quả: Model A và Model B trùng nhau đúng tại mốc tham chiếu — do cách neo, không phải do
    /// vật lý khớp Gunny.
    /// </summary>
    public double AnchorToReferenceShot(DistanceCalibration c, SimulationRequest physicsBase)
    {
        var r = physicsBase.Clone();
        r.Angle = c.ReferenceAngle;
        r.Power = c.ReferencePower;
        r.Wind = 0;
        r.TargetCollisionEnabled = false;
        r.MaxTrajectoryPoints = 2;
        var landing = _engine.Simulate(r).LandingX;
        if (!(landing > 0))
            throw new ArgumentException("Phát tham chiếu không bay về phía trước; không thể neo quy đổi cự ly.");
        c.SimUnitsPerDivision = landing / c.ReferenceDistance;
        return c.SimUnitsPerDivision;
    }

    private static void EnsureCalibrated(DistanceCalibration c)
    {
        if (!(c.SimUnitsPerDivision > 0))
            throw new InvalidOperationException("Chưa hiệu chỉnh quy đổi cự ly (SimUnitsPerDivision ≤ 0).");
    }
}

/// <summary>Kết quả quy đổi trả về cho giao diện.</summary>
public sealed class CalibrationConversion
{
    public double SimDistance { get; init; }
    public double GunnyDistance { get; init; }
    public double Pixels { get; init; }
    public double CalibrationFactor { get; init; }
    public double SimUnitsPerDivision { get; init; }
    public double PixelsPerDivision { get; init; }
}
