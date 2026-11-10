using ProjectileSimulator.Gunny.Models;
using ProjectileSimulator.Models;

namespace ProjectileSimulator.Gunny;

/// <summary>
/// "Tìm góc và lực tối ưu" + "So sánh đường đạn".
/// Mọi phương án là DỰ ĐOÁN của mô hình; chỉ phương án trùng thông số với một lần bắn thực tế
/// đã trúng mới được đánh dấu "đã xác nhận".
/// </summary>
public sealed class AutoAimService
{
    private readonly GunnyModelRegistry _registry;

    public AutoAimService(GunnyModelRegistry registry) => _registry = registry;

    public AutoAimResponse Calculate(AutoAimRequest req)
    {
        var ctx = _registry.CreateContext();
        var results = new List<ModelAimResult>();
        foreach (var m in _registry.Select(req.Models))
        {
            var r = m.SuggestAim(req.TargetDistance, req.SignedWind, ctx);
            foreach (var s in r.Solutions) Confirm(s, ctx);
            results.Add(new ModelAimResult
            {
                ModelId = r.ModelId, ModelName = r.ModelName, Message = r.Message,
                Solutions = r.Solutions.Take(req.MaxPerModel).ToList()
            });
        }
        return new AutoAimResponse
        {
            TargetDistance = req.TargetDistance,
            SignedWind = req.SignedWind,
            PerModel = results,
            Solutions = results.SelectMany(r => r.Solutions)
                .OrderBy(s => s.Confirmed ? 0 : 1).ThenBy(s => Math.Abs(s.Error)).ToList()
        };
    }

    /// <summary>Đánh dấu phương án đã được xác nhận bằng dữ liệu thực nghiệm (nếu có).</summary>
    public static void Confirm(AimSolution s, ModelContext ctx)
    {
        var t = ctx.Settings.Confirmation;
        var matches = ctx.Experiments.Count(e =>
            Math.Abs(e.Angle - s.Angle) <= t.Angle &&
            Math.Abs(e.Power - s.Power) <= t.Power &&
            Math.Abs(e.SignedWind - s.SignedWind) <= t.Wind &&
            Math.Abs(e.Distance - s.TargetDistance) <= t.Distance &&
            (e.HitTarget == true || (e.ActualLanding.HasValue && Math.Abs(e.ActualLanding.Value - s.TargetDistance) <= t.Landing)));
        s.Confirmed = matches > 0;
        s.ConfirmCount = matches;
    }

    /// <summary>
    /// So sánh: lấy phương án tốt nhất của từng mô hình, chạy engine vật lý với đúng (góc, lực, gió) đó
    /// để có quỹ đạo vẽ lên canvas. Quỹ đạo KHÔNG bị chỉnh cho khớp: điểm rơi theo vật lý và điểm rơi
    /// theo mô hình được trả về riêng.
    /// </summary>
    public CompareModelsResponse Compare(AutoAimRequest req)
    {
        var ctx = _registry.CreateContext();
        var cal = ctx.Settings.Calibration;
        var entries = new List<ModelCompareEntry>();
        foreach (var m in _registry.Select(req.Models))
        {
            var aim = m.SuggestAim(req.TargetDistance, req.SignedWind, ctx);
            var best = aim.Solutions.FirstOrDefault();
            if (best == null)
            {
                entries.Add(new ModelCompareEntry { ModelId = m.Id, ModelName = m.Name, Message = aim.Message ?? "Không có phương án." });
                continue;
            }
            Confirm(best, ctx);
            var sim = PhysicsBaselineModel.Simulate(best.Angle, best.Power, best.SignedWind, req.TargetDistance, ctx, maxPoints: 1500);
            var physicsLanding = DistanceCalibrationService.SimToGunny(cal, sim.LandingX);
            entries.Add(new ModelCompareEntry
            {
                ModelId = m.Id, ModelName = best.ModelName, Solution = best,
                ModelLanding = best.PredictedLanding,
                ModelError = best.Error,
                PhysicsLanding = physicsLanding,
                PhysicsError = physicsLanding - req.TargetDistance,
                PhysicsFlightTime = sim.FlightTime,
                Trajectory = sim.Trajectory,
                Message = aim.Message
            });
        }
        return new CompareModelsResponse
        {
            TargetDistance = req.TargetDistance,
            SignedWind = req.SignedWind,
            TargetSimDistance = DistanceCalibrationService.GunnyToSim(cal, req.TargetDistance),
            CalibrationFactor = DistanceCalibrationService.Factor(cal),
            Entries = entries
        };
    }
}

public sealed class AutoAimRequest
{
    [System.ComponentModel.DataAnnotations.Range(0.1, 100)] public double TargetDistance { get; set; } = 5;

    /// <summary>Gió có dấu theo chiều bắn (thuận dương, ngược âm).</summary>
    [System.ComponentModel.DataAnnotations.Range(-100, 100)] public double SignedWind { get; set; }

    public List<string>? Models { get; set; }
    [System.ComponentModel.DataAnnotations.Range(1, 100)] public int MaxPerModel { get; set; } = 5;
}

public sealed class AutoAimResponse
{
    public double TargetDistance { get; init; }
    public double SignedWind { get; init; }
    public List<AimSolution> Solutions { get; init; } = new();
    public List<ModelAimResult> PerModel { get; init; } = new();
}

public sealed class ModelCompareEntry
{
    public string ModelId { get; init; } = "";
    public string ModelName { get; init; } = "";
    public AimSolution? Solution { get; init; }

    /// <summary>Điểm rơi theo chính mô hình (khoảng Gunny).</summary>
    public double? ModelLanding { get; init; }
    public double? ModelError { get; init; }

    /// <summary>Điểm rơi khi engine vật lý bắn đúng (góc, lực, gió) đó.</summary>
    public double? PhysicsLanding { get; init; }
    public double? PhysicsError { get; init; }
    public double? PhysicsFlightTime { get; init; }

    /// <summary>Quỹ đạo vật lý (đơn vị simulator).</summary>
    public IReadOnlyList<TrajectoryPoint> Trajectory { get; init; } = Array.Empty<TrajectoryPoint>();
    public string? Message { get; init; }
}

public sealed class CompareModelsResponse
{
    public double TargetDistance { get; init; }
    public double SignedWind { get; init; }
    public double TargetSimDistance { get; init; }
    public double CalibrationFactor { get; init; }
    public List<ModelCompareEntry> Entries { get; init; } = new();
}
