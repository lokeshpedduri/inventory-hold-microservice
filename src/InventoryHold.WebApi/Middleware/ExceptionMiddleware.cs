using System.Net.Mime;
using System.Text.Json;
using InventoryHold.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.WebApi.Middleware;

/// <summary>
/// Global exception handler that converts well-known domain exceptions to RFC 7807
/// Problem Details responses (<c>application/problem+json</c>), and maps unexpected
/// exceptions to a generic 500 without leaking stack traces.
/// </summary>
/// <remarks>
/// WHY a middleware rather than <c>UseExceptionHandler</c> with a lambda: we need to
/// match several specific exception types and return different status codes per type.
/// A dedicated middleware keeps the mapping table in one place — controllers stay
/// ignorant of HTTP status details beyond their own happy-path returns.
/// </remarks>
public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="env">Host environment — used to gate stack-trace inclusion in dev.</param>
    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    /// <summary>Invokes the pipeline and catches domain + unexpected exceptions.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (status, title, detail) = exception switch
        {
            // 404 — hold not found
            HoldNotFoundException ex =>
                (StatusCodes.Status404NotFound,
                 "Hold not found",
                 ex.Message),

            // 409 — insufficient stock (race lost, or oversell attempt)
            InsufficientStockException ex =>
                (StatusCodes.Status409Conflict,
                 "Insufficient stock",
                 ex.Message),

            // 409 — hold already in terminal state (Released or Expired)
            HoldAlreadyTerminalException ex =>
                (StatusCodes.Status409Conflict,
                 "Hold already terminal",
                 ex.Message),

            // 500 — anything unexpected; log at Error level
            _ =>
                (StatusCodes.Status500InternalServerError,
                 "An unexpected error occurred",
                 _env.IsDevelopment() ? exception.ToString() : "Please try again later.")
        };

        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);
        else
            _logger.LogWarning(exception, "Domain exception: {Title}", title);

        var problem = new ProblemDetails
        {
            Type = $"https://httpstatuses.com/{status}",
            Title = title,
            Detail = detail,
            Status = status,
            Instance = context.Request.Path,
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
