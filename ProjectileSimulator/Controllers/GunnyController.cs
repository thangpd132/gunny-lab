using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using ProjectileSimulator.Gunny;

namespace ProjectileSimulator.Controllers;

/// <summary>API nghiên cứu công thức Gunny (V3): mô hình, quy đổi cự ly, dữ liệu thực nghiệm, tối ưu, tìm góc/lực.</summary>
[ApiController]
[Route("api/gunny")]
public sealed class GunnyController : ControllerBase
{
    private readonly GunnyModelRegistry _registry;
    private readonly GunnySettingsStore _settings;
    private readonly ExperimentRepository _repo;
    private readonly ModelOptimizationService _optimizer;
    private readonly AutoAimService _aim;
    private readonly DistanceCalibrationService _calibration;

    public GunnyController(GunnyModelRegistry registry, GunnySettingsStore settings, ExperimentRepository repo,
        ModelOptimizationService optimizer, AutoAimService aim, DistanceCalibrationService calibration)
    {
        _registry = registry; _settings = settings; _repo = repo; _optimizer = optimizer; _aim = aim; _calibration = calibration;
    }

    // ------------------------------------------------------------------ mô hình

    [HttpGet("models")]
    public ActionResult<IEnumerable<ModelInfo>> Models()
    {
        var ctx = _registry.CreateContext();
        return Ok(_registry.Models.Select(m => m.Describe(ctx)));
    }

    /// <summary>Dự đoán điểm rơi của mọi mô hình cho một lần bắn.</summary>
    [HttpPost("predict")]
    public IActionResult Predict([FromBody] PredictRequest req) => Guard(() =>
    {
        var ctx = _registry.CreateContext();
        var shot = new GunnyShot(req.Angle, req.Power, req.SignedWind, req.Distance);
        return Ok(_registry.Select(req.Models).Select(m => m.Predict(shot, ctx)));
    });

    // ------------------------------------------------------------------ cấu hình + quy đổi

    [HttpGet("settings")]
    public ActionResult<GunnyResearchSettings> GetSettings() => _settings.Get();

    [HttpPut("settings")]
    public IActionResult PutSettings([FromBody] GunnyResearchSettings s) => Guard(() =>
    {
        if (s.Custom.MinAngle > s.Custom.MaxAngle) throw new ArgumentException("Góc nhỏ nhất phải ≤ góc lớn nhất (Model E).");
        if (s.Calibration.Mode == CalibrationMode.Manual && !(s.Calibration.SimUnitsPerDivision > 0))
            throw new ArgumentException("Chế độ nhập tay cần SimUnitsPerDivision > 0.");
        var errors = new List<ValidationResult>();
        foreach (var o in new object[] { s.Calibration, s.SuperHigh, s.Angle65, s.LowAngle, s.Custom, s.Confirmation, s.Physics, s.Physics.Base, s.Physics.Base.Calibration })
            Validator.TryValidateObject(o, new ValidationContext(o), errors, true);
        if (s.LowAngle.Techniques.Any(a => a < 0 || a > 90)) errors.Add(new ValidationResult("Góc kỹ thuật thấp phải trong 0–90°."));
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors.Select(e => e.ErrorMessage)));
        return Ok(_settings.Save(s));
    });

    [HttpPost("settings/reset")]
    public ActionResult<GunnyResearchSettings> ResetSettings() => _settings.Reset();

    /// <summary>Neo quy đổi cự ly theo phát tham chiếu và lưu lại.</summary>
    [HttpPost("calibration/anchor")]
    public IActionResult Anchor() => Guard(() =>
    {
        var s = _settings.Get();
        s.Calibration.Mode = CalibrationMode.ReferenceShot;
        return Ok(_settings.Save(s));
    });

    /// <summary>Quy đổi một giá trị: truyền đúng một trong simDistance / gunnyDistance / pixels.</summary>
    [HttpPost("calibration/convert")]
    public IActionResult Convert([FromBody] ConvertRequest req) => Guard(() =>
    {
        var c = _settings.Get().Calibration;
        double g = req.GunnyDistance
            ?? (req.SimDistance.HasValue ? DistanceCalibrationService.SimToGunny(c, req.SimDistance.Value)
            : req.Pixels.HasValue ? DistanceCalibrationService.PixelsToGunny(c, req.Pixels.Value)
            : throw new ArgumentException("Cần simDistance, gunnyDistance hoặc pixels."));
        return Ok(new CalibrationConversion
        {
            GunnyDistance = g,
            SimDistance = DistanceCalibrationService.GunnyToSim(c, g),
            Pixels = DistanceCalibrationService.GunnyToPixels(c, g),
            CalibrationFactor = DistanceCalibrationService.Factor(c),
            SimUnitsPerDivision = c.SimUnitsPerDivision,
            PixelsPerDivision = DistanceCalibrationService.PixelsPerDivision(c),
        });
    });

    // ------------------------------------------------------------------ dữ liệu thực nghiệm

    [HttpGet("experiments")]
    public ActionResult<IReadOnlyList<GunnyExperiment>> List() => Ok(_repo.GetAll());

    [HttpPost("experiments")]
    public IActionResult Add([FromBody] GunnyExperiment e) => Guard(() => Ok(_repo.Add(e)));

    [HttpPut("experiments/{id:guid}")]
    public IActionResult Update(Guid id, [FromBody] GunnyExperiment e) => Guard(() =>
        _repo.Update(id, e) is { } r ? Ok(r) : NotFound());

    [HttpDelete("experiments/{id:guid}")]
    public IActionResult Delete(Guid id) => _repo.Delete(id) ? NoContent() : NotFound();

    [HttpDelete("experiments")]
    public IActionResult Clear() { _repo.Clear(); return NoContent(); }

    [HttpGet("experiments/export")]
    public IActionResult Export() =>
        File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(_repo.ExportCsv())).ToArray(), "text/csv; charset=utf-8", "gunny-experiments.csv");

    /// <summary>Body là nội dung CSV (text/plain hoặc text/csv). ?replace=true để thay toàn bộ.</summary>
    [HttpPost("experiments/import")]
    [Consumes("text/plain", "text/csv")]
    public async Task<ActionResult<CsvImportResult>> Import([FromQuery] bool replace = false)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();
        if (text.Length > 2_000_000) return ValidationProblem(detail: "Tệp CSV quá lớn (tối đa 2 MB).");
        var (rows, errors) = ExperimentRepository.ParseCsv(text);
        if (rows.Count == 0) return ValidationProblem(detail: "Không nhập được dòng nào. " + string.Join(" ", errors.Take(5)));
        return _repo.ImportCsv(text, replace);
    }

    // ------------------------------------------------------------------ tối ưu / tìm góc / so sánh

    [HttpPost("optimize")]
    public IActionResult Optimize([FromBody] OptimizeModelsRequest req, CancellationToken ct) =>
        Guard(() => Ok(_optimizer.OptimizeAll(req.Models, ct)));

    [HttpPost("optimize/apply")]
    public IActionResult Apply([FromBody] ApplyParametersRequest req) =>
        Guard(() => Ok(_optimizer.Apply(req.ModelId, req.Parameters)));

    [HttpPost("aim")]
    public IActionResult Aim([FromBody] AutoAimRequest req) => Guard(() => Ok(_aim.Calculate(req)));

    [HttpPost("compare")]
    public IActionResult Compare([FromBody] AutoAimRequest req) => Guard(() => Ok(_aim.Compare(req)));

    private IActionResult Guard(Func<IActionResult> f)
    {
        try { return f(); }
        catch (ArgumentException ex) { return ValidationProblem(detail: ex.Message); }
        catch (InvalidOperationException ex) { return ValidationProblem(detail: ex.Message); }
    }
}

public sealed class PredictRequest
{
    [Range(0, 100)] public double Distance { get; set; } = 5;
    [Range(0, 180)] public double Angle { get; set; } = 45;
    [Range(0, 100)] public double Power { get; set; } = 50;
    [Range(-100, 100)] public double SignedWind { get; set; }
    public List<string>? Models { get; set; }
}

public sealed class ConvertRequest
{
    public double? SimDistance { get; set; }
    public double? GunnyDistance { get; set; }
    public double? Pixels { get; set; }
}

public sealed class OptimizeModelsRequest
{
    public List<string>? Models { get; set; }
}

public sealed class ApplyParametersRequest
{
    [Required] public string ModelId { get; set; } = "";
    [Required] public Dictionary<string, double> Parameters { get; set; } = new();
}
