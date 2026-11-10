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
