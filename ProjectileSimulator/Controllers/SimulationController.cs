using Microsoft.AspNetCore.Mvc;
using ProjectileSimulator.Models;
using ProjectileSimulator.Services;

namespace ProjectileSimulator.Controllers;

[ApiController]
[Route("api/simulation")]
public sealed class SimulationController : ControllerBase
{
    private readonly IPhysicsEngine _engine;

    public SimulationController(IPhysicsEngine engine) => _engine = engine;

    /// <summary>Default request (used by the UI "Reset" button).</summary>
    [HttpGet("defaults")]
    public ActionResult<SimulationRequest> Defaults() => new SimulationRequest();

    /// <summary>Runs one simulation and returns the trajectory and the shot summary.</summary>
    [HttpPost("run")]
    public ActionResult<SimulationResult> Run([FromBody] SimulationRequest request)
    {
        try
        {
            return _engine.Simulate(request);
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(detail: ex.Message);
        }
    }

    /// <summary>Runs the base request under several model presets and/or extra scenarios.</summary>
    [HttpPost("compare")]
    public ActionResult<CompareResponse> Compare([FromBody] CompareRequest request)
    {
        if (request.Models.Count == 0 && request.Scenarios.Count == 0)
            return ValidationProblem(detail: "Provide at least one model preset or scenario.");
        if (request.Models.Count + request.Scenarios.Count > 50)
            return ValidationProblem(detail: "At most 50 simulations per comparison.");

        try
        {
            var response = new CompareResponse();
            foreach (var preset in request.Models.Distinct())
            {
                var r = ModelPresets.Apply(request.Base, preset);
                response.Entries.Add(new CompareEntry
                {
                    Label = ModelPresets.Label(preset),
                    Model = preset.ToString(),
                    Result = _engine.Simulate(r)
                });
            }
            foreach (var s in request.Scenarios)
                response.Entries.Add(new CompareEntry { Label = s.Label, Result = _engine.Simulate(s.Request) });
            return response;
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(detail: ex.Message);
        }
    }
}
