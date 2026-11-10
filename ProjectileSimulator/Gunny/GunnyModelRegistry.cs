using ProjectileSimulator.Gunny.Models;
using ProjectileSimulator.Services;

namespace ProjectileSimulator.Gunny;

/// <summary>Danh sách mô hình đã đăng ký + tạo ngữ cảnh tính toán từ cấu hình và dữ liệu hiện tại.</summary>
public sealed class GunnyModelRegistry
{
    private readonly IPhysicsEngine _engine;
    private readonly GunnySettingsStore _settings;
    private readonly ExperimentRepository _repo;

    public GunnyModelRegistry(IPhysicsEngine engine, GunnySettingsStore settings, ExperimentRepository repo)
    {
        _engine = engine;
        _settings = settings;
        _repo = repo;
        Models = new IProjectileModel[]
        {
            new PhysicsBaselineModel(),
            new GunnySuperHighModel(),
            new Gunny65DegreeModel(),
            new GunnyLowAngleModel(),
            new CustomEmpiricalModel(),
        };
    }

    public IReadOnlyList<IProjectileModel> Models { get; }

    public IProjectileModel? Find(string id) => Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<IProjectileModel> Select(IEnumerable<string>? ids)
    {
        var list = ids?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (list == null || list.Count == 0) return Models;
        var unknown = list.Where(id => Find(id) == null).ToList();
        if (unknown.Count > 0) throw new ArgumentException("Mô hình không tồn tại: " + string.Join(", ", unknown));
        return list.Select(id => Find(id)!);
    }

    public ModelContext CreateContext(GunnyResearchSettings? settingsOverride = null)
    {
        var s = settingsOverride ?? _settings.Get();
        var data = _repo.GetAll();
        if (!string.IsNullOrWhiteSpace(s.WeaponFilter))
            data = data.Where(e => string.Equals(e.Weapon?.Trim(), s.WeaponFilter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return new ModelContext { Settings = s, Experiments = data, Engine = _engine };
    }
}
