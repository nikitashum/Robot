using System.Text.Json.Serialization;

namespace ButikkPilot.Contracts;

/// <summary>Everything the floor-plan needs, already localized by the server.</summary>
public sealed class StoreMapDto
{
    public required string StoreName { get; init; }
    public IReadOnlyList<MapPointDto> Points { get; init; } = [];
}

public sealed class MapPointDto
{
    /// <summary>Stable key: "DEPT/AISLE". Matches <see cref="LocationInfo.PointKey"/>.</summary>
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
}

public sealed class LocationInfo
{
    public required string ShelfLocation { get; init; }
    public required string PointKey { get; init; }
    public required string DepartmentName { get; init; }
    public required string AisleLabel { get; init; }
    public required string ShelfLabel { get; init; }
    public required string Directions { get; init; }
    public required int MapX { get; init; }
    public required int MapY { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<GuideStepKind>))]
public enum GuideStepKind
{
    Start,
    Department,
    Aisle,
    Arrive,
}

/// <summary>Structured (language-neutral) so the UI localizes the sentence and a real robot can report the same steps.</summary>
public sealed class GuideStep
{
    public required GuideStepKind Kind { get; init; }
    public required string Label { get; init; }
}

public sealed class GuideResult
{
    public required int EstimatedSeconds { get; init; }
    public IReadOnlyList<GuideStep> Steps { get; init; } = [];
    public required int TargetX { get; init; }
    public required int TargetY { get; init; }
}
