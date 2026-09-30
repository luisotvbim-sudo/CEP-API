using Microsoft.AspNetCore.Mvc;

namespace CepApi.Api;

internal static class ApiProblems
{
    public static ProblemDetails Create(HttpContext context, int status, string title, string code, string? detail = null)
        => Enrich(new ProblemDetails { Status = status, Title = title, Detail = detail }, context, code);

    public static T Enrich<T>(T problem, HttpContext context, string code) where T : ProblemDetails
    {
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = context.TraceIdentifier;
        return problem;
    }
}
