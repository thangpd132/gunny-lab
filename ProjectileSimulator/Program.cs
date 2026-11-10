using System.Text.Json.Serialization;
using ProjectileSimulator.Gunny;
using ProjectileSimulator.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        // Enums travel as strings ("RungeKutta4", "Quadratic", ...) — easier to read and debug.
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    });

// The engine is stateless and thread-safe → singletons.
builder.Services.AddSingleton<IPhysicsEngine, PhysicsEngine>();
builder.Services.AddSingleton<ReverseEngineeringService>();
builder.Services.AddSingleton<OptimizationService>();

// --- Nghiên cứu công thức Gunny (V3) ---
// Dữ liệu thực nghiệm + cấu hình được lưu thành JSON trong thư mục này (mặc định ./App_Data).
var dataDir = builder.Configuration["GunnyLab:DataDirectory"];
dataDir = string.IsNullOrWhiteSpace(dataDir)
    ? Path.Combine(builder.Environment.ContentRootPath, "App_Data")
    : Path.GetFullPath(dataDir, builder.Environment.ContentRootPath);
builder.Services.AddSingleton<DistanceCalibrationService>();
builder.Services.AddSingleton(_ => new ExperimentRepository(dataDir));
builder.Services.AddSingleton(sp => new GunnySettingsStore(dataDir, sp.GetRequiredService<DistanceCalibrationService>()));
builder.Services.AddSingleton<GunnyModelRegistry>();
builder.Services.AddSingleton<ModelOptimizationService>();
builder.Services.AddSingleton<AutoAimService>();

var app = builder.Build();

app.UseDefaultFiles();   // serves wwwroot/index.html at "/"
app.UseStaticFiles();
app.MapControllers();

app.Run();

/// <summary>Exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program { }
