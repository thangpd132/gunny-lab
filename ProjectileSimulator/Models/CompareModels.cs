using System.ComponentModel.DataAnnotations;

namespace ProjectileSimulator.Models;

/// <summary>Model presets used by Compare / Reverse Engineering mode.</summary>
public enum ModelPreset
{
    /// <summary>Model A: gravity only (no wind, no drag).</summary>
    Basic,

    /// <summary>Model B: gravity + wind (no drag).</summary>
    Wind,

    /// <summary>Model C: gravity + air resistance (no wind).</summary>
    Drag,

    /// <summary>Model D: exactly what the user configured (any combination + calibration).</summary>
    Custom
}

public sealed class CompareRequest
{
    /// <summary>Base configuration (the "Custom" model).</summary>
    [Required] public SimulationRequest Base { get; set; } = new();

    /// <summary>Presets to run on top of the base configuration.</summary>
    public List<ModelPreset> Models { get; set; } = new();

    /// <summary>Additional fully specified scenarios (e.g. several powers or angles).</summary>
    [MaxLength(50)] public List<LabeledScenario> Scenarios { get; set; } = new();
}

public sealed class LabeledScenario
{
    public string Label { get; set; } = "";
    [Required] public SimulationRequest Request { get; set; } = new();
}

public sealed class CompareEntry
{
    public string Label { get; init; } = "";
    public string? Model { get; init; }
    public SimulationResult Result { get; init; } = new();
}

public sealed class CompareResponse
{
    public List<CompareEntry> Entries { get; init; } = new();
}
