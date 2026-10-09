using Hotel.Application.Channels;
using Hotel.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Api.Infrastructure;

/// <summary>Maps exceptions to RFC 7807 problem details with a stable machine-readable "code".</summary>
public class ExceptionHandler(IProblemDetailsService problems, ILogger<ExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, code, title) = ex switch
        {
            DomainException d => (d.Code is "no_availability" or "room_occupied" ? 409 : 422, d.Code, d.Message),
            KeyNotFoundException => (404, "not_found", ex.Message),
            ChannelException => (502, "channel_error", ex.Message),
            DbUpdateConcurrencyException => (409, "concurrency_conflict", "The record was modified by someone else. Reload and try again."),
            BadHttpRequestException b => (b.StatusCode, "bad_request", b.Message),
            _ => (500, "server_error", "An unexpected error occurred."),
        };
        if (status >= 500) logger.LogError(ex, "Unhandled exception");

        http.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = ex,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code } },
        });
    }
}
