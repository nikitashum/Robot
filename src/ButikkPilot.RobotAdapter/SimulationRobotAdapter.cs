using ButikkPilot.Contracts;
using ButikkPilot.Core.Abstractions;

namespace ButikkPilot.RobotAdapter;

/// <summary>
/// Pretends to be a delivery/guide robot. Produces structured, language-neutral steps; the UI renders the
/// sentences. A real adapter (e.g. for a Pudu robot) implements the same interface and returns real progress.
/// </summary>
public sealed class SimulationRobotAdapter : IRobotNavigation
{
    // Entrance position on the demo floor plan (map units; keep in sync with FloorPlan.razor) and an assumed robot speed.
    private const int EntranceX = 35;
    private const int EntranceY = 45;
    private const double UnitsPerSecond = 6.0;

    public Task<GuideResult> GuideCustomerToAsync(LocationInfo location, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        var dx = location.MapX - EntranceX;
        var dy = location.MapY - EntranceY;
        var seconds = 15 + (int)Math.Round(Math.Sqrt((dx * dx) + (dy * dy)) / UnitsPerSecond);

        var result = new GuideResult
        {
            EstimatedSeconds = seconds,
            TargetX = location.MapX,
            TargetY = location.MapY,
            Steps =
            [
                new GuideStep { Kind = GuideStepKind.Start, Label = string.Empty },
                new GuideStep { Kind = GuideStepKind.Department, Label = location.DepartmentName },
                new GuideStep { Kind = GuideStepKind.Aisle, Label = location.AisleLabel },
                new GuideStep { Kind = GuideStepKind.Arrive, Label = location.ShelfLabel },
            ],
        };

        return Task.FromResult(result);
    }
}
