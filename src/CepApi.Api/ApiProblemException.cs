namespace CepApi.Api;

public sealed class ApiProblemException(int statusCode, string title, string code, string? detail = null)
    : Exception(detail ?? title)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
    public string Code { get; } = code;
    public string? ProblemDetail { get; } = detail;
}
