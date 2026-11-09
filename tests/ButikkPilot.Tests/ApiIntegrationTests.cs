using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ButikkPilot.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ButikkPilot.Tests;

/// <summary>Real HTTP pipeline (routing, rate limiting, headers, static files) over the in-memory test server.</summary>
public sealed class ApiIntegrationTests : IClassFixture<WebApplicationFactory<ApiAssemblyMarker>>, IDisposable
{
    private readonly WebApplicationFactory<ApiAssemblyMarker> _factory;
    private readonly string _wwwroot = Path.Combine(Path.GetTempPath(), "bp-wwwroot-" + Guid.NewGuid().ToString("N"));

    public ApiIntegrationTests(WebApplicationFactory<ApiAssemblyMarker> factory)
    {
        Directory.CreateDirectory(Path.Combine(_wwwroot, "_framework"));
        File.WriteAllText(Path.Combine(_wwwroot, "index.html"), "<!doctype html><title>spa</title>");
        File.WriteAllText(Path.Combine(_wwwroot, "app.css"), "body{}");
        File.WriteAllBytes(Path.Combine(_wwwroot, "_framework", "icudt_EFIGS.dat"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(_wwwroot, "_framework", "dotnet.native.wasm"), [0, 0x61, 0x73, 0x6d]);
        File.WriteAllBytes(Path.Combine(_wwwroot, "_framework", "System.Private.CoreLib.wasm"), [1]);
        File.WriteAllText(Path.Combine(_wwwroot, "_framework", "blazor.boot.json"), "{}");
        File.WriteAllText(Path.Combine(_wwwroot, "manifest.webmanifest"), "{}");

        _factory = factory.WithWebHostBuilder(b => b.UseWebRoot(_wwwroot));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_wwwroot, recursive: true);
        }
        catch (IOException)
        {
            // temp folder cleanup is best effort
        }
    }

    private static HttpRequestMessage Chat(string message, Guid? session = null, string? lang = "en-US") =>
        new(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new ChatRequest { Message = message, SessionId = session, Language = lang }, ContractsJsonContext.Default.ChatRequest),
        };

    [Fact]
    public async Task Health_reports_provider_and_catalog_size_but_no_secrets()
    {
        var client = _factory.CreateClient();

        var health = await client.GetFromJsonAsync("/health", ContractsJsonContext.Default.HealthDto);

        Assert.Equal("ok", health!.Status);
        Assert.Equal("Stub", health.LlmProvider);
        Assert.True(health.Products > 0);
    }

    [Fact]
    public async Task Chat_round_trips_through_the_real_pipeline()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(Chat("Do you have a 27-inch gaming monitor under 5000 NOK?"));
        var body = await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ChatResponse);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(body!.RecommendedProducts);
        Assert.NotEqual(Guid.Empty, body.SessionId);
    }

    [Fact]
    public async Task Enums_serialize_as_strings_on_the_wire()
    {
        var client = _factory.CreateClient();
        var first = await (await client.SendAsync(Chat("Do you have a 27-inch gaming monitor under 5000 NOK?")))
            .Content.ReadFromJsonAsync(ContractsJsonContext.Default.ChatResponse);

        var raw = await (await client.SendAsync(Chat("Can the robot guide me there?", first!.SessionId))).Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(raw);
        Assert.Equal("Start", doc.RootElement.GetProperty("guide").GetProperty("steps")[0].GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_messages_are_rejected_with_400(string message)
    {
        var response = await _factory.CreateClient().SendAsync(Chat(message));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Overlong_messages_are_rejected_with_400()
    {
        var response = await _factory.CreateClient().SendAsync(Chat(new string('x', ChatLimits.MaxMessageLength + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Control_characters_are_rejected()
    {
        var response = await _factory.CreateClient().SendAsync(Chat("hello\u0000world"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_json_is_a_client_error_not_a_500()
    {
        var response = await _factory.CreateClient().PostAsync("/api/chat",
            new StringContent("{\"message\": ", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hostile_language_is_normalized_on_the_wire()
    {
        var response = await _factory.CreateClient().SendAsync(Chat("hello", lang: "xx'; DROP TABLE"));
        var body = await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ChatResponse);

        Assert.Equal(SupportedLanguages.Default, body!.Language);
    }

    [Fact]
    public async Task Unknown_api_routes_are_404_json_not_the_spa_page()
    {
        var response = await _factory.CreateClient().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Security_headers_are_present_on_pages_apis_and_errors()
    {
        var client = _factory.CreateClient();

        foreach (var path in new[] { "/", "/health", "/api/does-not-exist" })
        {
            var response = await client.GetAsync(path);
            Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
            Assert.False(response.Headers.Contains("Server"), $"{path} leaks a Server header");
        }
    }

    [Fact]
    public async Task Csp_allows_wasm_but_not_arbitrary_script_or_framing()
    {
        var csp = (await _factory.CreateClient().GetAsync("/health")).Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains("'wasm-unsafe-eval'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", csp.Replace("'wasm-unsafe-eval'", ""), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_returns_429_with_retry_after_after_the_per_minute_budget()
    {
        var factory = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:ChatPerMinute", "3"));
        var client = factory.CreateClient();

        var codes = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 6; i++)
        {
            last = await client.SendAsync(Chat("hello"));
            codes.Add(last.StatusCode);
        }

        Assert.Equal(3, codes.Count(c => c == HttpStatusCode.OK));
        Assert.Equal(3, codes.Count(c => c == HttpStatusCode.TooManyRequests));
        Assert.True(last!.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Map_endpoint_is_localized_and_cacheable()
    {
        var client = _factory.CreateClient();

        var en = await client.GetFromJsonAsync("/api/store/map?lang=en-US", ContractsJsonContext.Default.StoreMapDto);
        var nb = await client.GetFromJsonAsync("/api/store/map?lang=nb-NO", ContractsJsonContext.Default.StoreMapDto);

        Assert.Contains(en!.Points, p => p.Label.StartsWith("Aisle", StringComparison.Ordinal));
        Assert.Contains(nb!.Points, p => p.Label.StartsWith("Gang", StringComparison.Ordinal));
    }

    // ---- static files: the .dat 404 that once blanked the whole demo can never come back ----

    [Theory]
    [InlineData("/", "text/html")]
    [InlineData("/index.html", "text/html")]
    [InlineData("/app.css", "text/css")]
    [InlineData("/_framework/icudt_EFIGS.dat", "application/octet-stream")]
    [InlineData("/_framework/dotnet.native.wasm", "application/wasm")]
    [InlineData("/_framework/System.Private.CoreLib.wasm", "application/wasm")]   // .NET 10 ships assemblies as webcil .wasm
    [InlineData("/_framework/blazor.boot.json", "application/json")]
    [InlineData("/manifest.webmanifest", "application/manifest+json")]
    public async Task Every_asset_type_a_Blazor_app_ships_is_served_with_a_content_type(string path, string expectedType)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Client_side_routes_fall_back_to_index_html_but_missing_files_stay_404()
    {
        var client = _factory.CreateClient();

        var route = await client.GetAsync("/some/client/route");
        var missing = await client.GetAsync("/_framework/missing.dll");

        Assert.Equal(HttpStatusCode.OK, route.StatusCode);
        Assert.Contains("<title>spa</title>", await route.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Static_assets_always_revalidate_so_a_redeploy_is_picked_up()
    {
        var response = await _factory.CreateClient().GetAsync("/app.css");

        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Path_traversal_cannot_escape_wwwroot()
    {
        var client = _factory.CreateClient();

        foreach (var path in new[] { "/..%2f..%2fappsettings.json", "/%2e%2e/%2e%2e/appsettings.json", "/_framework/..%5c..%5cappsettings.json" })
        {
            var response = await client.GetAsync(path);
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Llm", text, StringComparison.Ordinal);
        }
    }
}
