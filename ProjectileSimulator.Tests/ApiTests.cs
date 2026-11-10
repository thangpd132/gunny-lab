using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectileSimulator.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Run_ReturnsTrajectoryAndSummary()
    {
        var resp = await _client.PostAsJsonAsync("/api/simulation/run", new { angle = 45, power = 50, distance = 5 });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("trajectory").GetArrayLength() > 10);
        Assert.True(json.GetProperty("flightTime").GetDouble() > 0);
        Assert.False(string.IsNullOrEmpty(json.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Run_InvalidInput_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/api/simulation/run", new { angle = 500, power = 50 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Compare_ReturnsOneEntryPerModel()
    {
        var resp = await _client.PostAsJsonAsync("/api/simulation/compare", new
        {
            @base = new { angle = 50, power = 60, wind = 8, dragType = "Quadratic" },
            models = new[] { "Basic", "Wind", "Drag", "Custom" }
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, json.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task Optimize_Works()
    {
        var resp = await _client.PostAsJsonAsync("/api/experiments/optimize", new
        {
            @base = new { },
            experiments = new[] { new { angle = 45, power = 50, wind = 0, distance = 5, observedLandingX = 6, observedLandingY = 0 } },
            method = "LeastSquares",
            parameters = new[] { new { name = "VelocityMultiplier", enabled = true, min = 0.5, max = 2, steps = 11 } }
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("bestMetrics").GetProperty("rootMeanSquareError").GetDouble() < 1e-3);
    }

    [Fact]
    public async Task IndexPage_IsServed()
    {
        var html = await _client.GetStringAsync("/");
        Assert.Contains("<canvas", html);
    }
}

public class GunnyApiTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gunnylab-api-" + Guid.NewGuid());
    private readonly HttpClient _client;

    public GunnyApiTests(WebApplicationFactory<Program> factory) =>
        _client = factory.WithWebHostBuilder(b => b.UseSetting("GunnyLab:DataDirectory", _dir)).CreateClient();

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public async Task Experiments_Crud_Import_Export()
    {
        var add = await _client.PostAsJsonAsync("/api/gunny/experiments", new { distance = 9, angle = 81, power = 100, wind = 0, windDirection = "Thuan", actualLanding = 9.2 });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var created = await add.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var upd = await _client.PutAsJsonAsync($"/api/gunny/experiments/{id}", new { distance = 9, angle = 82, power = 100, wind = 1, windDirection = "Nguoc", actualLanding = 8.9 });
        Assert.Equal(HttpStatusCode.OK, upd.StatusCode);

        var bad = await _client.PostAsJsonAsync("/api/gunny/experiments", new { distance = 9, angle = 300, power = 100 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var csv = new StringContent("distance,angle,power,wind\n5,65,70,0\n", System.Text.Encoding.UTF8, "text/plain");
        var imp = await _client.PostAsync("/api/gunny/experiments/import", csv);
        Assert.Equal(HttpStatusCode.OK, imp.StatusCode);

        var export = await _client.GetStringAsync("/api/gunny/experiments/export");
        Assert.Contains("82", export);
        Assert.Contains("nguoc", export);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/gunny/experiments/{id}")).StatusCode);
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/gunny/experiments");
        Assert.Equal(1, list.GetArrayLength());
    }

    [Fact]
    public async Task Models_Aim_Compare_Optimize_Work()
    {
        var models = await _client.GetFromJsonAsync<JsonElement>("/api/gunny/models");
        Assert.Equal(5, models.GetArrayLength());

        var aim = await _client.PostAsJsonAsync("/api/gunny/aim", new { targetDistance = 9, signedWind = 0, models = new[] { "superHigh", "physics" } });
        Assert.Equal(HttpStatusCode.OK, aim.StatusCode);
        var a = await aim.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(a.GetProperty("solutions").EnumerateArray(), s => s.GetProperty("modelId").GetString() == "superHigh" && s.GetProperty("angle").GetDouble() == 81);

        var cmp = await _client.PostAsJsonAsync("/api/gunny/compare", new { targetDistance = 8, signedWind = 0 });
        Assert.Equal(HttpStatusCode.OK, cmp.StatusCode);
        var c = await cmp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, c.GetProperty("entries").GetArrayLength());

        var opt = await _client.PostAsJsonAsync("/api/gunny/optimize", new { models = new[] { "superHigh" } });
        Assert.Equal(HttpStatusCode.OK, opt.StatusCode);

        var conv = await _client.PostAsJsonAsync("/api/gunny/calibration/convert", new { simDistance = 5.7 });
        var cv = await conv.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5.7 * cv.GetProperty("calibrationFactor").GetDouble(), cv.GetProperty("gunnyDistance").GetDouble(), 9);
    }
}
