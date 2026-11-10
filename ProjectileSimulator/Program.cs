using System.Text.Json.Serialization;
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

var app = builder.Build();

app.UseDefaultFiles();   // serves wwwroot/index.html at "/"
app.UseStaticFiles();
app.MapControllers();

app.Run();

/// <summary>Exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program { }
