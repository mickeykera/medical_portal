using System.Text.Json;
using MedicalPortal.Infrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Web.Middleware;

public class PhiSafeExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PhiSafeExceptionHandlerMiddleware> _logger;
    private readonly IPhiScrubber _scrubber;

    public PhiSafeExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<PhiSafeExceptionHandlerMiddleware> logger,
        IPhiScrubber scrubber)
    {
        _next = next;
        _logger = logger;
        _scrubber = scrubber;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var traceId = context.TraceIdentifier;

            // Scrub the exception before logging to guarantee no PHI leaks to log aggregators
            var sanitizedEx = _scrubber.ScrubException(ex);
            _logger.LogError(sanitizedEx, "Unhandled exception occurred during request execution. TraceId: {TraceId}", traceId);

            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";

                var payload = new
                {
                    error = "An error occurred while processing your request.",
                    traceId = traceId
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
            }
        }
    }
}
