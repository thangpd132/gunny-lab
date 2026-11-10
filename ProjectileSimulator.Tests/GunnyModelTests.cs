using ProjectileSimulator.Gunny;
using ProjectileSimulator.Gunny.Empirical;
using ProjectileSimulator.Gunny.Models;
using ProjectileSimulator.Services;

namespace ProjectileSimulator.Tests;

/// <summary>
/// Kiểm tra các mô hình Gunny V3.
/// LƯU Ý: mọi "dữ liệu thực nghiệm" trong file này là dữ liệu TỔNG HỢP do test tự tạo để kiểm tra
/// thuật toán (nội suy, hồi quy, tối ưu). Chúng KHÔNG phải dữ liệu Gunny và không chứng minh độ
/// chính xác của mô hình so với game thật.
/// </summary>
public class GunnyModelTests
{
    private readonly PhysicsEngine _engine = new();

    private ModelContext Ctx(IEnumerable<GunnyExperiment>? data = null, Action<GunnyResearchSettings>? configure = null)
    {
        var s = new GunnyResearchSettings();
        new DistanceCalibrationService(_engine).AnchorToReferenceShot(s.Calibration, s.Physics.Base);
        configure?.Invoke(s);
        return new ModelContext { Settings = s, Experiments = (data ?? Array.Empty<GunnyExperiment>()).ToList(), Engine = _engine };
    }

    private static GunnyExperiment Exp(double d, double angle, double power, double signedWind, double? landing, bool? hit = null) => new()
    {
        Distance = d, Angle = angle, Power = power, Wind = Math.Abs(signedWind),
        WindDirection = signedWind < 0 ? WindDirection.Nguoc : WindDirection.Thuan, ActualLanding = landing, HitTarget = hit
    };

    // ================================================================ Model B – siêu cao (1–5)

    [Theory]
    [InlineData(9, 0, 81)]   // 1. mốc tham chiếu
    [InlineData(8, 0, 82)]   // 2. gần hơn 1 khoảng → +1°
    [InlineData(10, 0, 80)]  // 3. xa hơn 1 khoảng → −1°
    [InlineData(9, 1, 83)]   // 4. gió thuận 1 → +2°
    [InlineData(9, -1, 79)]  // 5. gió ngược 1 → −2°
    [InlineData(7, 2, 87)]   // kết hợp: 81 + 2 + 4
    public void SuperHigh_AngleFormula(double distance, double wind, double expectedAngle)
    {
        var p = new SuperHighParameters();
        Assert.Equal(expectedAngle, GunnySuperHighModel.AngleFor(p, distance, wind), 9);
        // Đảo công thức phải trả lại đúng khoảng cách.
        Assert.Equal(distance, GunnySuperHighModel.DistanceFor(p, expectedAngle, wind), 9);
    }

    [Fact]
    public void SuperHigh_SuggestAim_UsesPower100_AndPredictMatches()
    {
        var m = new GunnySuperHighModel();
        var ctx = Ctx();
        var aim = m.SuggestAim(8, 1, ctx);
        var best = aim.Solutions[0];
        Assert.Equal(84, best.Angle, 9);
        Assert.Equal(100, best.Power);
        Assert.Equal(0, best.Error, 9);
        Assert.False(best.Confirmed); // chưa có dữ liệu thực tế → chỉ là dự đoán

        var pred = m.Predict(new GunnyShot(84, 100, 1, 8), ctx);
        Assert.True(pred.Supported);
        Assert.Equal(8, pred.Landing!.Value, 9);
    }

    [Fact]
    public void SuperHigh_IsNotAppliedOutsideItsTechnique()
    {
        var m = new GunnySuperHighModel();
        Assert.False(m.Predict(new GunnyShot(81, 80, 0, 9), Ctx()).Supported);  // lực khác 100
        Assert.False(m.Predict(new GunnyShot(65, 100, 0, 9), Ctx()).Supported); // góc 65 không phải siêu cao
        // Mục tiêu quá xa → góc theo công thức < MinAngle → không áp dụng
        var far = m.SuggestAim(30, 0, Ctx());
        Assert.Empty(far.Solutions);
        Assert.Contains("ngoài vùng siêu cao", far.Message);
    }

    // ================================================================ 6. Quy đổi khoảng cách

    [Fact]
    public void Calibration_ConvertsSimulatorToGunnyDistance()
    {
        var c = new DistanceCalibration { SimUnitsPerDivision = 0.5, ScreenDivisions = 9, ReferenceScreenWidthPx = 900, Mode = CalibrationMode.Manual };
        Assert.Equal(2, DistanceCalibrationService.Factor(c), 12);
        Assert.Equal(11.4, DistanceCalibrationService.SimToGunny(c, 5.7), 12);   // Gunny = Sim × Factor
        Assert.Equal(5.7, DistanceCalibrationService.GunnyToSim(c, 11.4), 12);
        Assert.Equal(100, DistanceCalibrationService.PixelsPerDivision(c), 12);
        Assert.Equal(4.5, DistanceCalibrationService.PixelsToGunny(c, 450), 12);
    }

    [Fact]
    public void Calibration_IsNotOneToOneByDefault_AndAnchorsReferenceShot()
    {
        var ctx = Ctx();
        Assert.NotEqual(1.0, ctx.Settings.Calibration.SimUnitsPerDivision, 3);
        // Sau khi neo, vật lý bắn 81°/lực 100/gió 0 rơi đúng tại 9 khoảng.
        var p = new PhysicsBaselineModel().Predict(new GunnyShot(81, 100, 0, 9), ctx);
        Assert.Equal(9, p.Landing!.Value, 6);
        Assert.NotNull(p.FlightTime);
    }

    [Fact]
    public void Calibration_ThrowsWhenNotCalibrated()
    {
        Assert.Throws<InvalidOperationException>(() => DistanceCalibrationService.SimToGunny(new DistanceCalibration { SimUnitsPerDivision = 0 }, 5));
    }

    // ================================================================ 7. Nội suy dữ liệu thực nghiệm

    [Fact]
    public void PiecewiseInterpolation_InterpolatesBetweenPoints()
    {
        var s = new List<EmpiricalSample>
        {
            new(new[] { 60.0, 0 }, 4), new(new[] { 70.0, 0 }, 5), new(new[] { 80.0, 0 }, 6.5),
            new(new[] { 70.0, 0 }, 5.2), // trùng lực → lấy trung bình 5.1
        };
        var (fit, _) = EmpiricalFitter.TrainPiecewise(s);
        Assert.NotNull(fit);
        Assert.Equal(4.55, fit!.Predict(new[] { 65.0, 0 }), 9);
        Assert.Equal(5.8, fit.Predict(new[] { 75.0, 0 }), 9);
        Assert.Equal(5.1, fit.Predict(new[] { 70.0, 0 }), 9);
        Assert.True(fit.IsExtrapolation(new[] { 90.0, 0 }));
        Assert.False(fit.IsExtrapolation(new[] { 72.0, 0 }));
    }

    [Fact]
    public void Angle65_LearnsOnlyFromData_AndSolvesPower()
    {
        var m = new Gunny65DegreeModel();
        Assert.Equal(ModelStatus.InsufficientData, m.Describe(Ctx()).Status);
        Assert.Empty(m.SuggestAim(5, 0, Ctx()).Solutions);

        // dữ liệu tổng hợp tuyến tính: landing = 0.05·P + 1.6
        var data = new[] { 60, 64, 68, 72, 76 }.Select(p => Exp(5, 65, p, 0, 0.05 * p + 1.6)).ToList();
        data.Add(Exp(5, 20, 50, 0, 3)); // góc khác → không được dùng cho mô hình 65°
        var ctx = Ctx(data);
        var info = m.Describe(ctx);
        Assert.Equal(ModelStatus.Fitted, info.Status);
        Assert.Equal(5, info.SubModels[0].Samples);

        var pred = m.Predict(new GunnyShot(65, 70, 0, 5), ctx);
        Assert.Equal(5.1, pred.Landing!.Value, 6);

        var aim = m.SuggestAim(5.0, 0, ctx);
        Assert.Equal(68, aim.Solutions[0].Power, 1);
        Assert.Equal(65, aim.Solutions[0].Angle);

        // Dữ liệu chỉ có gió 0 → không dự đoán với gió khác (không bịa ảnh hưởng của gió).
        var windy = m.Predict(new GunnyShot(65, 70, 2, 5), ctx);
        Assert.False(windy.Supported);
        Assert.Contains("gió", windy.Reason);
    }

    [Fact]
    public void LowAngle_TechniquesAreIndependent()
    {
        var data = new[] { 40, 50, 60, 70 }.Select(p => Exp(4, 20, p, 0, p / 15.0)).ToList();
        var ctx = Ctx(data);
        var m = new GunnyLowAngleModel();
        var info = m.Describe(ctx);
        Assert.Equal(ModelStatus.InsufficientData, info.SubModels.Single(s => s.Name.Contains("10")).Status);
        Assert.Equal(ModelStatus.Fitted, info.SubModels.Single(s => s.Name.Contains("20")).Status);
        Assert.False(m.Predict(new GunnyShot(10, 50, 0, 4), ctx).Supported);
        Assert.True(m.Predict(new GunnyShot(20, 50, 0, 4), ctx).Supported);
        Assert.False(m.Predict(new GunnyShot(45, 50, 0, 4), ctx).Supported); // không phải góc kỹ thuật
    }

    // ================================================================ 8. Tính toán sai số

    [Fact]
    public void ErrorMetrics_AreComputedCorrectly()
    {
        var devs = new List<ShotDeviation>
        {
            new() { Actual = 5, Predicted = 5.3, Error = 0.3 },
            new() { Actual = 6, Predicted = 5.6, Error = -0.4 },
            new() { Actual = 7, Predicted = null, Error = null }, // không áp dụng → không tính
        };
        var e = ModelEvaluation.From(new GunnySuperHighModel(), 3, devs);
        Assert.Equal(2, e.Evaluated);
        Assert.Equal(0.35, e.Mae!.Value, 12);
        Assert.Equal(Math.Sqrt((0.09 + 0.16) / 2), e.Rmse!.Value, 12);
        Assert.Equal(0.4, e.MaxError!.Value, 12);

        var (mae, rmse) = EmpiricalFitter.Errors(new[] { new EmpiricalSample(new[] { 1.0 }, 2), new EmpiricalSample(new[] { 2.0 }, 2) }, x => x[0]);
        Assert.Equal(0.5, mae, 12);
        Assert.Equal(Math.Sqrt(0.5), rmse, 12);
    }

    [Fact]
    public void SuperHigh_Evaluate_ReportsDeviationPerShot()
    {
        // landing thực tế 8.5 cho phát 82° gió 0 → công thức dự đoán 8 → sai số −0.5
        var ctx = Ctx(new[] { Exp(8, 82, 100, 0, 8.5), Exp(5, 65, 70, 0, 5) });
        var ev = new GunnySuperHighModel().Evaluate(ctx);
        Assert.Equal(1, ev.Applicable);
        Assert.Equal(-0.5, ev.Deviations[0].Error!.Value, 9);
    }

    [Fact]
    public void EffectiveLanding_UsesHitWhenLandingMissing()
    {
        Assert.Equal(6, Exp(6, 65, 70, 0, null, hit: true).EffectiveLanding);
        Assert.Null(Exp(6, 65, 70, 0, null, hit: false).EffectiveLanding);
    }

    // ================================================================ 9. Import / Export

    [Fact]
    public void Csv_RoundTrip_AndPersistence()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gunnylab-test-" + Guid.NewGuid());
        try
        {
            var repo = new ExperimentRepository(dir);
            repo.Add(new GunnyExperiment { Distance = 5, Angle = 65, Power = 68, Wind = 1.5, WindDirection = WindDirection.Nguoc, Weapon = "Súng, A", ActualLanding = 5.1, HitTarget = false, Notes = "ghi \"chú\"" });
            repo.Add(new GunnyExperiment { Distance = 9, Angle = 81, Power = 100 });
            var csv = repo.ExportCsv();

            var (rows, errors) = ExperimentRepository.ParseCsv(csv);
            Assert.Empty(errors);
            Assert.Equal(2, rows.Count);
            var a = rows[0];
            Assert.Equal((5, 65, 68, 1.5, WindDirection.Nguoc, "Súng, A", 5.1, false, "ghi \"chú\""),
                (a.Distance, a.Angle, a.Power, a.Wind, a.WindDirection, a.Weapon, a.ActualLanding!.Value, a.HitTarget!.Value, a.Notes));
            Assert.Null(rows[1].ActualLanding);
            Assert.Null(rows[1].HitTarget);

            // Lưu lại sau khi "tắt ứng dụng": mở repository mới trên cùng thư mục.
            var reopened = new ExperimentRepository(dir);
            Assert.Equal(2, reopened.GetAll().Count);

            var result = reopened.ImportCsv(csv, replace: true);
            Assert.Equal(2, result.Imported);
            Assert.Equal(2, new ExperimentRepository(dir).GetAll().Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Csv_Import_ReportsBadLines_AndParsesVietnameseValues()
    {
        var csv = "Khoảng cách;Góc;Lực;Gió;Hướng gió;Trúng\n5,5;65;70;1;ngược;có\nx;65;70;0;;\n6;65;72;-2;;không\n6;200;72;0;;\n";
        var (rows, errors) = ExperimentRepository.ParseCsv(csv);
        Assert.Equal(2, rows.Count);
        Assert.Equal(5.5, rows[0].Distance);
        Assert.Equal(-1, rows[0].SignedWind);
        Assert.True(rows[0].HitTarget);
        Assert.Equal(-2, rows[1].SignedWind);      // gió âm → ngược
        Assert.False(rows[1].HitTarget);
        Assert.Equal(2, errors.Count);
        Assert.StartsWith("Dòng 3", errors[0]);
        Assert.StartsWith("Dòng 5", errors[1]);
    }

    // ================================================================ 10. Tối ưu tham số

    [Fact]
    public void SuperHigh_LeastSquares_RecoversParameters()
    {
        // dữ liệu tổng hợp sinh từ Angle = 80 + 1.2·(9 − D) + 1.5·W (không phải số liệu Gunny)
        var data = new List<GunnyExperiment>();
        foreach (var (d, w) in new[] { (9.0, 0.0), (8.0, 0.0), (7.0, 1.0), (10.0, -1.0), (6.5, 0.5), (8.5, -0.5) })
        {
            var angle = 80 + 1.2 * (9 - d) + 1.5 * w;
            data.Add(Exp(d, angle, 100, w, d));
        }
        var ctx = Ctx(data);
        var r = ModelOptimizationService.OptimizeSuperHigh(new GunnySuperHighModel(), ctx);
        Assert.True(r.CanApply, r.Message);
        Assert.Equal(80, r.SuggestedParameters!["baseAngle"], 6);
        Assert.Equal(1.2, r.SuggestedParameters["anglePerDivision"], 6);
        Assert.Equal(1.5, r.SuggestedParameters["windAngleFactor"], 6);
        Assert.True(r.After.Rmse < 1e-6);
        Assert.True(r.Before.Rmse > r.After.Rmse);
    }

    [Fact]
    public void SuperHigh_Optimization_RequiresEnoughData()
    {
        var r = ModelOptimizationService.OptimizeSuperHigh(new GunnySuperHighModel(), Ctx(new[] { Exp(9, 81, 100, 0, 9) }));
        Assert.False(r.CanApply);
        Assert.Contains("Chưa đủ dữ liệu", r.Message);
    }

    [Fact]
    public void Physics_Optimization_RecoversVelocityMultiplier()
    {
        // Dữ liệu tổng hợp: điểm rơi tính bằng chính engine với VelocityMultiplier = 1.15.
        var ctx0 = Ctx();
        var truth = GunnySettingsStore.Clone(ctx0.Settings);
        truth.Physics.Base.Calibration.VelocityMultiplier = 1.15;
        var truthCtx = new ModelContext { Settings = truth, Experiments = Array.Empty<GunnyExperiment>(), Engine = _engine };
        var phys = new PhysicsBaselineModel();
        var data = new[] { (45.0, 50.0), (60.0, 70.0), (30.0, 60.0), (70.0, 80.0) }
            .Select(t => Exp(5, t.Item1, t.Item2, 0, phys.Predict(new GunnyShot(t.Item1, t.Item2, 0, 5), truthCtx).Landing)).ToList();

        var ctx = new ModelContext { Settings = ctx0.Settings, Experiments = data, Engine = _engine };
        var svc = new ModelOptimizationService(null!, new OptimizationService(new ReverseEngineeringService(_engine)), null!);
        var r = svc.OptimizePhysics(phys, ctx, CancellationToken.None);
        Assert.True(r.CanApply, r.Message);
        Assert.Equal(1.15, r.SuggestedParameters!["velocityMultiplier"], 2);
        Assert.True(r.After.Rmse < 0.02, $"RMSE {r.After.Rmse}");
    }

    [Fact]
    public void Empirical_ModelSelection_UsesLoocv()
    {
        // dữ liệu tổng hợp bậc 2: D = 1 + 0.001·P²
        var s = new[] { 30.0, 40, 50, 60, 70, 80, 90 }.Select(p => new EmpiricalSample(new[] { p, 0.0 }, 1 + 0.001 * p * p)).ToList();
        var specs = new[]
        {
            new RegressionSpec(EmpiricalMethod.LinearRegression, "lin", x => new[] { 1, x[0] }, new[] { "1", "P" }),
            new RegressionSpec(EmpiricalMethod.QuadraticRegression, "quad", x => new[] { 1, x[0], x[0] * x[0] }, new[] { "1", "P", "P²" }),
        };
        var report = EmpiricalFitter.FitBest(s, specs, allowPiecewise: true, allowLookup: true, lookupScale: new[] { 5.0, 1.0 });
        Assert.Equal(EmpiricalMethod.QuadraticRegression, report.Best!.Method);
        Assert.True(report.BestReport!.LoocvRmse < 1e-6);
        Assert.Equal(4, report.Candidates.Count);
        Assert.All(report.Candidates.Where(c => c.Method != EmpiricalMethod.QuadraticRegression), c => Assert.True(c.LoocvRmse > report.BestReport.LoocvRmse));
    }

    [Fact]
    public void Empirical_RefusesDegenerateData()
    {
        // mọi lần bắn cùng một lực → không xác định được hệ số của lực
        var s = Enumerable.Range(0, 5).Select(i => new EmpiricalSample(new[] { 70.0, 0 }, 5 + 0.01 * i)).ToList();
        var (fit, reason) = EmpiricalFitter.TrainRegression(s, new RegressionSpec(EmpiricalMethod.LinearRegression, "lin", x => new[] { 1, x[0] }, new[] { "1", "P" }));
        Assert.Null(fit);
        Assert.Contains("đa dạng", reason);
    }

    [Fact]
    public void CustomModel_FitsAcrossAngles()
    {
        // dữ liệu tổng hợp dạng vật lý: D = 0.3 + 0.0009·P²·sin2θ
        var data = new List<GunnyExperiment>();
        foreach (var a in new[] { 30.0, 45, 60, 70 })
            foreach (var p in new[] { 50.0, 70, 90 })
                data.Add(Exp(5, a, p, 0, 0.3 + 0.0009 * p * p * Math.Sin(2 * a * Math.PI / 180)));
        var ctx = Ctx(data);
        var m = new CustomEmpiricalModel();
        var ev = m.Evaluate(ctx);
        Assert.Equal("Dạng vật lý D = a + b·P²·sin2θ", ev.Method);
        Assert.True(ev.Rmse < 1e-6);
        var aim = m.SuggestAim(5, 0, ctx);
        Assert.NotEmpty(aim.Solutions);
        Assert.True(Math.Abs(aim.Solutions[0].Error) < 0.05);
    }

    // ================================================================ Xác nhận bằng dữ liệu

    [Fact]
    public void AimSolution_IsConfirmedOnlyByMatchingRealHit()
    {
        var ctx = Ctx(new[] { Exp(8, 84, 100, 1, null, hit: true) });
        var sol = new GunnySuperHighModel().SuggestAim(8, 1, ctx).Solutions[0];
        AutoAimService.Confirm(sol, ctx);
        Assert.True(sol.Confirmed);
        Assert.Equal(1, sol.ConfirmCount);

        var other = new GunnySuperHighModel().SuggestAim(7, 1, ctx).Solutions[0];
        AutoAimService.Confirm(other, ctx);
        Assert.False(other.Confirmed);
    }
}
