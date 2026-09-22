using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace CepApi.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue("sub")!);
    protected Guid? CurrentOrganizationId => HttpContext.Items[OrganizationScopeAttribute.ItemKey] is Guid selected
        ? selected : Guid.TryParse(User.FindFirstValue("org_id"), out var id) ? id : null;
    protected string? IpAddress => HttpContext.Connection.RemoteIpAddress?.ToString();

    protected ObjectResult ApiProblem(int status, string title, string code, string? detail = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}
