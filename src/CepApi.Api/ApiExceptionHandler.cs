using Microsoft.AspNetCore.Diagnostics;

namespace CepApi.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception is ApiProblemException expected
            ? ApiProblems.Create(httpContext, expected.StatusCode, expected.Title, expected.Code, expected.ProblemDetail)
            : ApiProblems.Create(httpContext, StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.", "unexpected_error");
        if (exception is not ApiProblemException)
            logger.LogError(exception, "Unhandled request error. CorrelationId={CorrelationId}", httpContext.TraceIdentifier);
        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem
        });
    }
}
