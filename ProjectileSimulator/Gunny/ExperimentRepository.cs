using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectileSimulator.Gunny;

/// <summary>
/// Lưu trữ dữ liệu bắn thực tế dưới dạng JSON trên đĩa (giữ lại sau khi tắt ứng dụng).
/// Thread-safe; mỗi thay đổi được ghi ra file ngay (ghi file tạm rồi đổi tên).
/// </summary>
public sealed class ExperimentRepository
{
    public static readonly string[] CsvColumns =
        { "distance", "angle", "power", "wind", "windDirection", "weapon", "actualLanding", "hitTarget", "notes" };

    private readonly string _file;
    private readonly object _lock = new();
    private List<GunnyExperiment> _items;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ExperimentRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _file = Path.Combine(dataDirectory, "gunny-experiments.json");
        _items = Load();
    }

    public string FilePath => _file;

    public IReadOnlyList<GunnyExperiment> GetAll()
    {
        lock (_lock) return _items.Select(Copy).ToList();
    }

    public GunnyExperiment? Get(Guid id)
    {
        lock (_lock) return _items.FirstOrDefault(e => e.Id == id) is { } e ? Copy(e) : null;
    }

    public GunnyExperiment Add(GunnyExperiment e)
    {
        Validate(e);
        lock (_lock)
        {
            var item = Copy(e);
            if (item.Id == Guid.Empty || _items.Any(x => x.Id == item.Id)) item.Id = Guid.NewGuid();
            item.CreatedAt = DateTime.UtcNow;
            _items.Add(item);
            Save();
            return Copy(item);
        }
    }

    public GunnyExperiment? Update(Guid id, GunnyExperiment e)
    {
        Validate(e);
        lock (_lock)
        {
            var i = _items.FindIndex(x => x.Id == id);
            if (i < 0) return null;
            var item = Copy(e);
            item.Id = id;
            item.CreatedAt = _items[i].CreatedAt;
            _items[i] = item;
            Save();
            return Copy(item);
        }
    }

    public bool Delete(Guid id)
    {
        lock (_lock)
        {
            var n = _items.RemoveAll(x => x.Id == id);
            if (n > 0) Save();
            return n > 0;
        }
    }

    public void Clear()
    {
        lock (_lock) { _items.Clear(); Save(); }
    }

    // ------------------------------------------------------------------ CSV

    public string ExportCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', CsvColumns));
        foreach (var e in GetAll())
        {
            sb.AppendLine(string.Join(',', new[]
            {
                Num(e.Distance), Num(e.Angle), Num(e.Power), Num(e.Wind),
                e.WindDirection == WindDirection.Nguoc ? "nguoc" : "thuan",
                Cell(e.Weapon), e.ActualLanding.HasValue ? Num(e.ActualLanding.Value) : "",
                e.HitTarget switch { true => "yes", false => "no", null => "" },
                Cell(e.Notes)
            }));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Nhập CSV. Dòng lỗi bị bỏ qua và được báo lại (kèm số dòng); dòng hợp lệ được thêm.
    /// </summary>
    public CsvImportResult ImportCsv(string csv, bool replace)
    {
        var (rows, errors) = ParseCsv(csv);
        lock (_lock)
        {
            if (replace) _items.Clear();
            foreach (var r in rows) { r.Id = Guid.NewGuid(); r.CreatedAt = DateTime.UtcNow; _items.Add(r); }
            Save();
        }
        return new CsvImportResult { Imported = rows.Count, Errors = errors };
    }

    public static (List<GunnyExperiment> rows, List<string> errors) ParseCsv(string csv)
    {
        var rows = new List<GunnyExperiment>();
        var errors = new List<string>();
        var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int? headerLine = null;
        string[]? header = null;
        char sep = ',';

        for (var li = 0; li < lines.Length; li++)
        {
            var raw = lines[li].Trim();
            if (raw.Length == 0 || raw.StartsWith('#')) continue;
            if (header == null)
            {
                sep = new[] { ',', ';', '\t' }.OrderByDescending(c => raw.Count(ch => ch == c)).First();
                header = SplitCsv(raw, sep).Select(NormalizeHeader).ToArray();
                headerLine = li;
                if (!header.Contains("distance") || !header.Contains("angle") || !header.Contains("power"))
                {
                    errors.Add($"Dòng {li + 1}: thiếu tiêu đề bắt buộc (distance, angle, power).");
                    return (rows, errors);
                }
                continue;
            }

            var cells = SplitCsv(raw, sep);
            try
            {
                string? Get(string key)
                {
                    var i = Array.IndexOf(header, key);
                    return i >= 0 && i < cells.Count ? cells[i].Trim() : null;
                }

                var e = new GunnyExperiment
                {
                    Distance = ParseNum(Get("distance"), "distance") ?? throw new FormatException("thiếu distance"),
                    Angle = ParseNum(Get("angle"), "angle") ?? throw new FormatException("thiếu angle"),
                    Power = ParseNum(Get("power"), "power") ?? throw new FormatException("thiếu power"),
                    Wind = 0,
                    Weapon = Empty(Get("weapon")),
                    ActualLanding = ParseNum(Get("actualLanding"), "actualLanding"),
                    HitTarget = ParseBool(Get("hitTarget")),
                    Notes = Empty(Get("notes")),
                };
                var wind = ParseNum(Get("wind"), "wind") ?? 0;
                var dir = ParseDirection(Get("windDirection"));
                // Gió âm trong cột wind cũng được hiểu là gió ngược.
                e.Wind = Math.Abs(wind);
                e.WindDirection = dir ?? (wind < 0 ? WindDirection.Nguoc : WindDirection.Thuan);
                Validate(e);
                rows.Add(e);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                errors.Add($"Dòng {li + 1}: {ex.Message}");
            }
        }
        if (headerLine == null) errors.Add("Không có dữ liệu.");
        return (rows, errors);
    }

    // ------------------------------------------------------------------ helpers

    public static void Validate(GunnyExperiment e)
    {
        static void Check(bool ok, string msg) { if (!ok) throw new ArgumentException(msg); }
        Check(double.IsFinite(e.Distance) && e.Distance >= 0 && e.Distance <= 100, "Distance phải trong [0, 100].");
        Check(double.IsFinite(e.Angle) && e.Angle >= 0 && e.Angle <= 180, "Angle phải trong [0, 180].");
        Check(double.IsFinite(e.Power) && e.Power >= 0 && e.Power <= 100, "Power phải trong [0, 100].");
        Check(double.IsFinite(e.Wind) && e.Wind >= 0 && e.Wind <= 100, "Wind phải trong [0, 100] (hướng ghi ở WindDirection).");
        Check(!e.ActualLanding.HasValue || (double.IsFinite(e.ActualLanding.Value) && Math.Abs(e.ActualLanding.Value) <= 100),
            "ActualLanding phải trong [−100, 100].");
        Check(Enum.IsDefined(e.WindDirection), "WindDirection không hợp lệ.");
        Check((e.Weapon?.Length ?? 0) <= 80, "Weapon tối đa 80 ký tự.");
        Check((e.Notes?.Length ?? 0) <= 500, "Notes tối đa 500 ký tự.");
    }

    private static string NormalizeHeader(string h)
    {
        var k = new string(RemoveDiacritics(h.Trim().ToLowerInvariant()).Where(char.IsLetterOrDigit).ToArray());
        return k switch
        {
            "distance" or "khoangcach" or "kc" => "distance",
            "angle" or "goc" => "angle",
            "power" or "luc" => "power",
            "wind" or "gio" => "wind",
            "winddirection" or "huonggio" or "direction" => "windDirection",
            "weapon" or "vukhi" or "sung" => "weapon",
            "actuallanding" or "actuallandingdistance" or "landing" or "diemroi" => "actualLanding",
            "hittarget" or "hit" or "trung" => "hitTarget",
            "notes" or "note" or "ghichu" => "notes",
            _ => k
        };
    }

    /// <summary>"Khoảng cách" → "khoang cach" (bỏ dấu tiếng Việt, đ → d).</summary>
    internal static string RemoveDiacritics(string s)
    {
        var d = s.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC);
    }

    private static double? ParseNum(string? s, string field)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().Replace(',', '.');
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)) return v;
        throw new FormatException($"{field} không phải số: \"{s}\"");
    }

    private static bool? ParseBool(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return RemoveDiacritics(s.Trim().ToLowerInvariant()) switch
        {
            "yes" or "y" or "true" or "1" or "x" or "co" or "có" or "trung" or "trúng" => true,
            "no" or "n" or "false" or "0" or "khong" or "không" or "truot" or "trượt" => false,
            _ => throw new FormatException($"hitTarget không hợp lệ: \"{s}\" (dùng yes/no)")
        };
    }

    private static WindDirection? ParseDirection(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return RemoveDiacritics(s.Trim().ToLowerInvariant()) switch
        {
            "thuan" or "thuận" or "+" or "tail" or "tailwind" or "with" => WindDirection.Thuan,
            "nguoc" or "ngược" or "-" or "head" or "headwind" or "against" => WindDirection.Nguoc,
            _ => throw new FormatException($"windDirection không hợp lệ: \"{s}\" (dùng thuan/nguoc)")
        };
    }

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Num(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Cell(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r', ';' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    internal static List<string> SplitCsv(string line, char sep)
    {
        var res = new List<string>();
        var cur = new StringBuilder();
        var q = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (q)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (c == '"') q = false;
                else cur.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == sep) { res.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        res.Add(cur.ToString());
        return res;
    }

    private static GunnyExperiment Copy(GunnyExperiment e) => new()
    {
        Id = e.Id, Distance = e.Distance, Angle = e.Angle, Power = e.Power, Wind = e.Wind,
        WindDirection = e.WindDirection, Weapon = e.Weapon, ActualLanding = e.ActualLanding,
        HitTarget = e.HitTarget, Notes = e.Notes, CreatedAt = e.CreatedAt
    };

    private List<GunnyExperiment> Load()
    {
        if (!File.Exists(_file)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<GunnyExperiment>>(File.ReadAllText(_file), Json) ?? new();
        }
        catch (JsonException)
        {
            // File hỏng: giữ bản sao để người dùng khôi phục thủ công, bắt đầu danh sách rỗng.
            File.Copy(_file, _file + ".corrupt-" + DateTime.UtcNow.Ticks, overwrite: true);
            return new();
        }
    }

    private void Save()
    {
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items, Json));
        File.Move(tmp, _file, overwrite: true);
    }
}

public sealed class CsvImportResult
{
    public int Imported { get; init; }
    public List<string> Errors { get; init; } = new();
}

/// <summary>Lưu <see cref="GunnyResearchSettings"/> ra JSON.</summary>
public sealed class GunnySettingsStore
{
    private readonly string _file;
    private readonly object _lock = new();
    private readonly DistanceCalibrationService _calibration;
    private GunnyResearchSettings _settings;

    public GunnySettingsStore(string dataDirectory, DistanceCalibrationService calibration)
    {
        Directory.CreateDirectory(dataDirectory);
        _file = Path.Combine(dataDirectory, "gunny-settings.json");
        _calibration = calibration;
        _settings = Load();
    }

    /// <summary>Bản sao sâu của cấu hình hiện tại.</summary>
    public GunnyResearchSettings Get()
    {
        lock (_lock) return Clone(_settings);
    }

    public GunnyResearchSettings Save(GunnyResearchSettings s)
    {
        lock (_lock)
        {
            Normalize(s);
            _settings = Clone(s);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_settings, ExperimentRepository.Json));
            File.Move(tmp, _file, overwrite: true);
            return Clone(_settings);
        }
    }

    public GunnyResearchSettings Reset() => Save(new GunnyResearchSettings());

    private void Normalize(GunnyResearchSettings s)
    {
        // Physics base luôn tắt va chạm mục tiêu và giữ đủ điểm để tìm điểm rơi chính xác.
        s.Physics.Base.TargetCollisionEnabled = false;
        if (s.Calibration.Mode == CalibrationMode.ReferenceShot || !(s.Calibration.SimUnitsPerDivision > 0))
            _calibration.AnchorToReferenceShot(s.Calibration, s.Physics.Base);
        s.LowAngle.Techniques = s.LowAngle.Techniques.Where(double.IsFinite).Distinct().OrderBy(x => x).ToList();
    }

    private GunnyResearchSettings Load()
    {
        GunnyResearchSettings? s = null;
        if (File.Exists(_file))
        {
            try { s = JsonSerializer.Deserialize<GunnyResearchSettings>(File.ReadAllText(_file), ExperimentRepository.Json); }
            catch (JsonException) { s = null; }
        }
        s ??= new GunnyResearchSettings();
        Normalize(s);
        return s;
    }

    internal static GunnyResearchSettings Clone(GunnyResearchSettings s) =>
        JsonSerializer.Deserialize<GunnyResearchSettings>(JsonSerializer.Serialize(s, ExperimentRepository.Json), ExperimentRepository.Json)!;
}
