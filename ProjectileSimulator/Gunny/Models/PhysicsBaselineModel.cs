using ProjectileSimulator.Models;

namespace ProjectileSimulator.Gunny.Models;

/// <summary>
/// Model A – Physics Baseline: engine ném xiên hiện tại (bước thời gian, gió, lực cản tuỳ chọn).
/// Dùng làm mô hình THAM CHIẾU để so sánh, không phải công thức Gunny.
/// </summary>
public sealed class PhysicsBaselineModel : IProjectileModel
{
    public const string ModelId = "physics";

    public string Id => ModelId;
    public string Name => "A · Vật lý tham chiếu (Physics Baseline)";
    public string Description =>
        "Mô phỏng ném xiên theo từng bước thời gian bằng Physics Engine C# hiện có: V0 = k·VelMul·(Power·PowMul)^exp, " +
        "trọng lực g·GravMul, gió dạng gia tốc ngang (hoặc khối khí trôi), lực cản tuỳ chọn. " +
        "Khoảng cách được quy đổi sang khoảng Gunny qua Distance Calibration.";
    public string Applicability => "Mọi góc 0–90° (bắn về phía mục tiêu), mọi lực 0–100.";

    public IReadOnlyList<ModelSource> Sources { get; } = new[]
    {
        new ModelSource("Cơ học ném xiên (sách giáo khoa vật lý)", null,
            "Công thức X = X0 + Vx·t, Y = Y0 + Vy·t − ½gt²; tích phân số Semi-implicit Euler."),
    };

    public IReadOnlyList<string> Assumptions { get; } = new[]
    {
        "Lực Gunny 0–100 dùng trực tiếp làm Power của engine (cùng thang đo) — giả định.",
        "Gió Gunny × WindScale = gió của engine (mặc định WindScale = 1) — giả định, chưa kiểm chứng.",
        "Nếu quy đổi cự ly ở chế độ 'neo theo phát tham chiếu' thì mô hình này trùng Model B đúng tại mốc 81°/lực 100/9 khoảng do cách neo, KHÔNG phải vì vật lý khớp Gunny.",
    };

    public ModelInfo Describe(ModelContext ctx)
    {
        var c = ctx.Settings.Physics.Base.Calibration;
        var b = ctx.Settings.Physics.Base;
        return new ModelInfo
        {
            Id = Id, Name = Name, Description = Description, Applicability = Applicability,
            Sources = Sources, Assumptions = Assumptions, Status = ModelStatus.Ready, SupportsFlightTime = true,
            StatusMessage = "Luôn dùng được (không cần dữ liệu). Có thể tối ưu hệ số bằng dữ liệu thực nghiệm.",
            Parameters = new[]
            {
                P("velocityMultiplier", "Hệ số vận tốc", c.VelocityMultiplier, "Nhân vào V0.", true),
                P("gravityMultiplier", "Hệ số trọng lực", c.GravityMultiplier, "Nhân vào g.", true),
                P("windMultiplier", "Hệ số gió", c.WindMultiplier, "Nhân vào gió.", true),
                P("powerToVelocity", "k: lực → vận tốc", c.PowerToVelocity, "V0 = k·Power khi các hệ số = 1.", true, "u/s"),
                P("gravity", "Trọng lực g", b.Gravity, "", true, "u/s²"),
                P("preferredAngle", "Góc ưu tiên khi so sánh", ctx.Settings.Physics.PreferredAngle, "Nhiều góc cùng trúng – chọn góc gần giá trị này.", true, "°"),
            }
        };
    }

    public ModelPrediction Predict(GunnyShot shot, ModelContext ctx)
    {
        if (shot.Angle < 0 || shot.Angle > 90)
            return Unsupported("Chỉ hỗ trợ góc 0–90° (bắn về phía mục tiêu).");
        var r = Simulate(shot.Angle, shot.Power, shot.SignedWind, shot.TargetDistance, ctx);
        return new ModelPrediction
        {
            ModelId = Id, ModelName = Name, Supported = true,
            Landing = DistanceCalibrationService.SimToGunny(ctx.Settings.Calibration, r.LandingX),
            FlightTime = r.FlightTime
        };
    }

    public ModelAimResult SuggestAim(double target, double wind, ModelContext ctx)
    {
        var sols = new List<AimSolution>();
        for (var angle = 1; angle <= 89; angle++)
        {
            var s = SolvePower(angle, wind, target, ctx);
            if (s != null) sols.Add(s);
        }
        var pref = ctx.Settings.Physics.PreferredAngle;
        return new ModelAimResult
        {
            ModelId = Id, ModelName = Name,
            Solutions = sols.OrderBy(s => Math.Round(Math.Abs(s.Error), 2)).ThenBy(s => Math.Abs(s.Angle - pref)).ToList(),
            Message = sols.Count == 0 ? "Không góc nào với lực ≤ 100 bắn tới được khoảng cách này." : null
        };
    }

    /// <summary>Với góc cố định, tìm lực (chia đôi) để điểm rơi = mục tiêu.</summary>
    public AimSolution? SolvePower(double angle, double wind, double target, ModelContext ctx)
    {
        double Landing(double p) =>
            DistanceCalibrationService.SimToGunny(ctx.Settings.Calibration, Simulate(angle, p, wind, target, ctx).LandingX);
        double lo = 0, hi = 100;
        double flo = Landing(lo) - target, fhi = Landing(hi) - target;
        if (flo > 0 || fhi < 0) return null;      // không bắn tới / luôn vượt quá
        for (var i = 0; i < 30 && hi - lo > 0.01; i++)
        {
            var mid = 0.5 * (lo + hi);
            if (Landing(mid) - target < 0) lo = mid; else hi = mid;
        }
        var power = Math.Round(0.5 * (lo + hi), 1);
        var r = Simulate(angle, power, wind, target, ctx);
        var landing = DistanceCalibrationService.SimToGunny(ctx.Settings.Calibration, r.LandingX);
        return new AimSolution
        {
            ModelId = Id, ModelName = Name, Angle = angle, Power = power, SignedWind = wind, TargetDistance = target,
            PredictedLanding = landing, Error = landing - target, FlightTime = r.FlightTime,
            Note = "Lực làm tròn 0.1"
        };
    }

    public ModelEvaluation Evaluate(ModelContext ctx)
    {
        var devs = new List<ShotDeviation>();
        var list = ctx.WithLanding.Where(e => e.Angle <= 90).ToList();
        foreach (var e in list)
        {
            var p = Predict(new GunnyShot(e.Angle, e.Power, e.SignedWind, e.Distance), ctx);
            devs.Add(Dev(e, p.Landing));
        }
        return ModelEvaluation.From(this, list.Count, devs);
    }

    /// <summary>Chạy engine vật lý: khoảng cách Gunny → đơn vị simulator theo hiệu chỉnh.</summary>
    public static SimulationResult Simulate(double angle, double power, double signedWind, double targetGunny, ModelContext ctx, int maxPoints = 2)
    {
        var cal = ctx.Settings.Calibration;
        var r = ctx.Settings.Physics.Base.Clone();
        r.Angle = angle;
        r.Power = power;
        r.Wind = DistanceCalibrationService.GunnyWindToSim(cal, signedWind);
        r.Distance = Math.Clamp(DistanceCalibrationService.GunnyToSim(cal, Math.Max(0.1, targetGunny)), 0.1, 100);
        r.TargetCollisionEnabled = false;
        r.MaxTrajectoryPoints = maxPoints;
        return ctx.Engine.Simulate(r);
    }

    internal static ShotDeviation Dev(GunnyExperiment e, double? predicted, string? note = null) => new()
    {
        ExperimentId = e.Id, Distance = e.Distance, Angle = e.Angle, Power = e.Power, SignedWind = e.SignedWind,
        Actual = e.EffectiveLanding!.Value, Predicted = predicted, Error = predicted - e.EffectiveLanding, Note = note
    };

    private ModelPrediction Unsupported(string reason) => new() { ModelId = Id, ModelName = Name, Supported = false, Reason = reason };

    private static ModelParameterInfo P(string n, string l, double v, string d, bool ed, string? u = null) =>
        new() { Name = n, Label = l, Value = v, Description = d, Editable = ed, Unit = u };
}
