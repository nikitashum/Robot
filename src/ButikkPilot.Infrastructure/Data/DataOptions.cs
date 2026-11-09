using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ButikkPilot.Infrastructure.Data;

public sealed class DataOptions
{
    public const string Section = "Data";

    /// <summary>Folder containing products.json and store-layout.json (absolute, or relative to the app/repo).</summary>
    public string Directory { get; set; } = "data";
}

/// <summary>Locates the data folder: configured absolute path, next to the binaries, content root, or walking up (dev/tests).</summary>
public sealed class DataPathResolver
{
    private readonly Lazy<string> _directory;

    public DataPathResolver(IOptions<DataOptions> options, IHostEnvironment env)
    {
        var configured = options.Value.Directory;
        _directory = new Lazy<string>(() => Resolve(configured, env.ContentRootPath));
    }

    public string Directory => _directory.Value;

    public string FilePath(string fileName) => Path.Combine(Directory, fileName);

    private static string Resolve(string configured, string contentRoot)
    {
        if (Path.IsPathRooted(configured))
        {
            return System.IO.Directory.Exists(configured)
                ? configured
                : throw new DirectoryNotFoundException($"Configured data directory not found: {configured}");
        }

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, configured),
            Path.Combine(contentRoot, configured),
        };

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            candidates.Add(Path.Combine(dir.FullName, configured));
        }

        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "products.json")))
            ?? throw new DirectoryNotFoundException(
                $"Could not find '{configured}/products.json'. Searched: {string.Join("; ", candidates.Distinct())}");
    }
}
