using ProjectileSimulator.Models;
using ProjectileSimulator.Physics;

namespace ProjectileSimulator.Services;

/// <summary>Builds the A/B/C/D model variants from a base configuration.</summary>
public static class ModelPresets
{
    public static SimulationRequest Apply(SimulationRequest baseRequest, ModelPreset preset)
    {
        var r = baseRequest.Clone();
        switch (preset)
        {
            case ModelPreset.Basic:          // A: gravity only
                r.WindEnabled = false;
                r.DragType = DragType.None;
                break;
            case ModelPreset.Wind:           // B: gravity + wind
                r.WindEnabled = true;
                r.DragType = DragType.None;
                break;
            case ModelPreset.Drag:           // C: gravity + drag
                r.WindEnabled = false;
                if (r.DragType == DragType.None) r.DragType = DragType.Quadratic;
                break;
            case ModelPreset.Custom:         // D: as configured
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
        }
        return r;
    }

    public static string Label(ModelPreset p) => p switch
    {
        ModelPreset.Basic => "A: Basic Projectile",
        ModelPreset.Wind => "B: Projectile + Wind",
        ModelPreset.Drag => "C: Projectile + Air Resistance",
        ModelPreset.Custom => "D: Custom Model",
        _ => p.ToString()
    };
}
