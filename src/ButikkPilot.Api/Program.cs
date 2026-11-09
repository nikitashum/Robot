using System.Threading.RateLimiting;
using ButikkPilot.Api.Hosting;
using ButikkPilot.Contracts;
using ButikkPilot.Core.Abstractions;
using ButikkPilot.Infrastructure;
using ButikkPilot.Infrastructure.Llm;
using ButikkPilot.RobotAdapter;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 16 * 1024; // chat messages are tiny; refuse anything bigger early
});

builder.Services.AddButikkPilot(builder.Configuration);
builder.Services.AddSingleton<IRobotNavigation, SimulationRobotAdapter>(); // swap for a real robot adapter here

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ClientErrorHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, ContractsJsonContext.Default));

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/wasm", "application/octet-stream"]);
});

builder.Services.AddOptions<RateLimitOptions>().Bind(builder.Configuration.GetSection(RateLimitOptions.Section));
builder.Services.AddRateLimiter(_ => { });
// Configured lazily from IOptions so the final configuration (env vars, test overrides) always wins.
builder.Services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitOptions>>((o, rateOptions) =>
{
    var rate = rateOptions.Value;
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.AddPolicy("chat", http => RateLimitPartition.GetSlidingWindowLimiter(ClientKey.From(http), _ => new SlidingWindowRateLimiterOptions
    {
        PermitLimit = rate.ChatPerMinute,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 0,
    }));

    // One global ceiling on simultaneous chats so no mix of clients can stampede the LLM.
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        http.Request.Path.StartsWithSegments("/api/chat")
            ? RateLimitPartition.GetConcurrencyLimiter("chat-global", _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = rate.MaxConcurrentChats,
                QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter("other"));

    o.OnRejected = async (context, ct) =>
    {
        // Not every limiter reports a precise wait (the concurrency one and chained limiters do not), but a 429
        // without Retry-After makes clients hammer us. Fall back to the full window, which is always safe.
        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 60;
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { title = "Too many requests", status = 429 }, ct);
    };
});

if (builder.Environment.IsDevelopment())
{
    // Split dev mode only (Blazor dev server on another port). Production is same-origin and needs no CORS.
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var u) && u.IsLoopback)
        .WithHeaders("Content-Type")
        .WithMethods("GET", "POST")));
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseSecurityHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseResponseCompression();
app.UseRateLimiter();
if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

// Blazor ships files whose extensions the default provider does not know (e.g. ICU .dat). Unknown types are
// refused with 404 by design, which once left the demo as a blank page, so map them explicitly.
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".dat"] = "application/octet-stream";
contentTypes.Mappings[".blat"] = "application/octet-stream";
contentTypes.Mappings[".webcil"] = "application/octet-stream";

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache", // always revalidate (ETag), so a redeploy is picked up at once
});

app.MapGet("/health", async (IProductCatalog catalog, IOptions<LlmOptions> llm) =>
    Results.Ok(new HealthDto
    {
        Status = "ok",
        LlmProvider = llm.Value.Provider.ToString(),
        Products = (await catalog.GetAllAsync()).Count,
    }));

var api = app.MapGroup("/api");

api.MapPost("/chat", async (ChatRequest request, IConversationService chat, CancellationToken ct) =>
{
    var errors = ChatRequestValidator.Validate(request);
    return errors.Count > 0
        ? Results.ValidationProblem(errors)
        : Results.Ok(await chat.ProcessUserMessageAsync(request, ct));
}).RequireRateLimiting("chat");

api.MapGet("/store/map", async (string? lang, IStoreNavigation store, HttpContext http, CancellationToken ct) =>
{
    http.Response.Headers.CacheControl = "public, max-age=300";
    return Results.Ok(await store.GetMapAsync(SupportedLanguages.Normalize(lang), ct));
});

// Unknown API routes must be a 404, not the SPA's index.html.
api.MapFallback(() => Results.NotFound());

var webRoot = app.Environment.WebRootPath;
if (webRoot is not null && File.Exists(Path.Combine(webRoot, "index.html")))
{
    app.MapFallbackToFile("index.html");
}
else
{
    app.MapGet("/", () => Results.Text("ButikkPilot API is running. No UI is bundled (wwwroot/index.html missing). See README."));
}

app.Run();

/// <summary>
/// Lets integration tests host the app with WebApplicationFactory. A dedicated type (instead of Program) avoids a
/// clash with the Blazor project's own Program class when both are referenced.
/// </summary>
public sealed class ApiAssemblyMarker;
