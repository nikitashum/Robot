using ButikkPilot.Web;
using ButikkPilot.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Production: UI and API share one origin. Split dev mode sets ApiBaseUrl in appsettings.Development.json.
var configuredApi = builder.Configuration["ApiBaseUrl"];
var apiBase = string.IsNullOrWhiteSpace(configuredApi)
    ? new Uri(builder.HostEnvironment.BaseAddress)
    : new Uri(configuredApi.TrimEnd('/') + "/");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = apiBase, Timeout = TimeSpan.FromSeconds(45) });
builder.Services.AddScoped<ButikkPilotApiClient>();
builder.Services.AddScoped<BrowserInterop>();
builder.Services.AddScoped<GuidedDemoRunner>();

await builder.Build().RunAsync();
