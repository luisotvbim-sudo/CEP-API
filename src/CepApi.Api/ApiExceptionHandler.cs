using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CepApi.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is ApiProblemException problem)
        {
            httpContext.Response.StatusCode = problem.StatusCode;
            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = problem.StatusCode,
                    Title = problem.Title,
                    Detail = problem.ProblemDetail,
                    Extensions = { ["code"] = problem.Code, ["correlationId"] = httpContext.TraceIdentifier }
                }
            });
        }

        logger.LogError(exception, "Unhandled request error. CorrelationId={CorrelationId}", httpContext.TraceIdentifier);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Extensions = { ["code"] = "unexpected_error", ["correlationId"] = httpContext.TraceIdentifier }
            }
        });
    }
}
