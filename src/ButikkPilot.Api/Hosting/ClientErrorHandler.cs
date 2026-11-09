using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ButikkPilot.Api.Hosting;

/// <summary>
/// Malformed JSON, oversized bodies and similar are the client's fault. Without this the global exception handler
/// reports them as 500s, which pollutes alerts and tells callers (wrongly) that retrying might help.
/// </summary>
public sealed class ClientErrorHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException bad)
        {
            return false; // real server fault: fall through to the generic 500 ProblemDetails
        }

        httpContext.Response.StatusCode = bad.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = bad.StatusCode, Title = "The request could not be processed." },
            cancellationToken);
        return true;
    }
}
