using System.Text.Json;
using System.Text.Json.Serialization;

namespace ButikkPilot.Contracts;

/// <summary>
/// Source-generated serializer metadata. Required for the browser client: Release builds are IL-trimmed and
/// reflection-based System.Text.Json can silently lose properties. The API uses the same context so both
/// sides serialize identically (camelCase, case-insensitive).
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(StoreMapDto))]
[JsonSerializable(typeof(HealthDto))]
public sealed partial class ContractsJsonContext : JsonSerializerContext;

public sealed class HealthDto
{
    public required string Status { get; init; }
    public required string LlmProvider { get; init; }
    public required int Products { get; init; }
}
