using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PersonalTracker.Api.Infrastructure;

/// <summary>Maps domain exceptions to RFC 7807 problem responses. Unknown errors never leak details.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case RequestValidationException validation:
                problem = new ValidationProblemDetails(validation.Errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Please check the highlighted fields."
                };
                break;
            case NotFoundException notFound:
                problem = new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = notFound.Message };
                break;
            case ConflictException conflict:
                problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = conflict.Message };
                if (conflict.Details is not null)
                    foreach (var (key, value) in conflict.Details) problem.Extensions[key] = value;
                break;
            case AuthenticationFailedException auth:
                problem = new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = auth.Message };
                break;
            case BadHttpRequestException bad:
                problem = new ProblemDetails { Status = bad.StatusCode, Title = "The request could not be understood." };
                break;
            case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                return true; // client went away; nothing to write
            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Something went wrong on our side. Please try again."
                };
                break;
        }

        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
        return true;
    }
}
