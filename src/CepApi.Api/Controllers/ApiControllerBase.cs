using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CepApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace CepApi.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue("sub")!);
    protected Guid? CurrentOrganizationId => Guid.TryParse(User.FindFirstValue("org_id"), out var id) ? id : null;
    protected UserRole CurrentRole => Enum.Parse<UserRole>(User.FindFirstValue("role")!);
    protected string? IpAddress => HttpContext.Connection.RemoteIpAddress?.ToString();

    protected static (int Page, int PageSize) NormalizePage(int page, int pageSize, int maximumPageSize = 100)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, maximumPageSize));

    protected ObjectResult ApiProblem(int status, string title, string code, string? detail = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}
