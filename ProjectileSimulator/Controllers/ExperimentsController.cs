using Microsoft.AspNetCore.Mvc;
using ProjectileSimulator.Models;
using ProjectileSimulator.Services;

namespace ProjectileSimulator.Controllers;

[ApiController]
[Route("api/experiments")]
public sealed class ExperimentsController : ControllerBase
{
    private readonly ReverseEngineeringService _re;
    private readonly OptimizationService _optimizer;

    public ExperimentsController(ReverseEngineeringService re, OptimizationService optimizer)
    {
        _re = re;
        _optimizer = optimizer;
    }

    /// <summary>Default parameter ranges for the optimizer UI.</summary>
    [HttpGet("parameters")]
    public ActionResult<List<ParameterRange>> Parameters() => OptimizationService.DefaultRanges();

    /// <summary>Predicted vs observed landing positions + MAE / RMSE for the current parameters.</summary>
    [HttpPost("evaluate")]
    public ActionResult<EvaluationResult> Evaluate([FromBody] EvaluationRequest request)
    {
        try
        {
            return _re.Evaluate(request.Base, request.Experiments, request.FlightTimeWeight);
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(detail: ex.Message);
        }
    }

    /// <summary>Fits the enabled parameters to the experiments (grid search and/or least squares).</summary>
    [HttpPost("optimize")]
    public ActionResult<OptimizationResult> Optimize([FromBody] OptimizationRequest request, CancellationToken ct)
    {
        try
        {
            return _optimizer.Optimize(request, ct);
        }
        catch (ArgumentException ex)
        {
            return ValidationProblem(detail: ex.Message);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is ArgumentException))
        {
            return ValidationProblem(detail: "A parameter combination is invalid: " + ex.InnerExceptions[0].Message);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499);
        }
    }
}
