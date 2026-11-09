namespace ButikkPilot.Api.Hosting;

public static class SecurityHeaders
{
    // wasm-unsafe-eval is required by every WebAssembly runtime (it does NOT allow JS eval()).
    // style-src needs 'unsafe-inline' because Blazor applies inline style attributes at runtime.
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "manifest-src 'self'; " +
        "media-src 'none'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            // OnStarting so the headers survive the exception handler clearing the response.
            context.Response.OnStarting(() =>
            {
                var h = context.Response.Headers;
                h["Content-Security-Policy"] = ContentSecurityPolicy;
                h["X-Content-Type-Options"] = "nosniff";
                h["Referrer-Policy"] = "no-referrer";
                h["Permissions-Policy"] = "microphone=(self), camera=(), geolocation=(), payment=(), usb=()";
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                h["X-Frame-Options"] = "DENY";
                return Task.CompletedTask;
            });

            await next();
        });
}
