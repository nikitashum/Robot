using System.Text.Json;
using ButikkPilot.Contracts;
using ButikkPilot.Core.Abstractions;
using ButikkPilot.Core.Localization;
using ButikkPilot.Core.Models;

namespace ButikkPilot.Infrastructure.Data;

public sealed class JsonStoreNavigation : IStoreNavigation
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Lazy<Task<StoreLayout>> _layout;

    public JsonStoreNavigation(DataPathResolver paths)
    {
        _layout = new Lazy<Task<StoreLayout>>(() => LoadAsync(paths.FilePath("store-layout.json")));
    }

    public Task<StoreLayout> GetLayoutAsync(CancellationToken ct = default) => _layout.Value;

    public async Task<StoreMapDto> GetMapAsync(string language, CancellationToken ct = default)
    {
        var layout = await _layout.Value;
        var lang = SupportedLanguages.Normalize(language);
        var points = layout.Departments
            .SelectMany(d => d.Aisles.Select(a => new MapPointDto
            {
                Key = $"{d.Id}/{a.Id}",
                Label = a.Label.Get(lang),
                X = a.MapX,
                Y = a.MapY,
            }))
            .ToList();

        return new StoreMapDto { StoreName = layout.StoreName, Points = points };
    }

    public async Task<LocationInfo?> ResolveLocationAsync(string shelfLocation, string language, CancellationToken ct = default)
    {
        var parts = shelfLocation.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return null;
        }

        var layout = await _layout.Value;
        var lang = SupportedLanguages.Normalize(language);

        var dept = layout.Departments.FirstOrDefault(d => Eq(d.Id, parts[0]));
        var aisle = dept?.Aisles.FirstOrDefault(a => Eq(a.Id, parts[1]));
        var shelf = aisle?.Shelves.FirstOrDefault(s => Eq(s.Id, parts[2]));
        if (dept is null || aisle is null || shelf is null)
        {
            return null;
        }

        var code = shelf.Id.StartsWith("SHELF-", StringComparison.OrdinalIgnoreCase) ? shelf.Id["SHELF-".Length..] : shelf.Id;
        return new LocationInfo
        {
            ShelfLocation = $"{dept.Id}/{aisle.Id}/{shelf.Id}",
            PointKey = $"{dept.Id}/{aisle.Id}",
            DepartmentName = dept.Name.Get(lang),
            AisleLabel = aisle.Label.Get(lang),
            ShelfLabel = $"{ServerMessages.ShelfWord(lang)} {code}",
            Directions = aisle.Directions.Get(lang),
            MapX = aisle.MapX,
            MapY = aisle.MapY,
        };
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static async Task<StoreLayout> LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<StoreLayout>(stream, JsonOptions)
            ?? throw new InvalidDataException($"{path} is empty.");
    }
}
