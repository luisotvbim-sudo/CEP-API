using System.Security.Claims;
using CepApi.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CepApi.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId => Guid.Parse(User.FindFirstValue("sub")!);
    protected Guid? CurrentOrganizationId => HttpContext.Items[OrganizationScopeAttribute.ItemKey] is Guid selected
        ? selected : Guid.TryParse(User.FindFirstValue("org_id"), out var id) ? id : null;
    protected UserRole CurrentRole => Enum.Parse<UserRole>(User.FindFirstValue("role")!);
    protected Guid ScopedOrganizationId => HttpContext.Items[OrganizationScopeAttribute.ItemKey] is Guid id
        ? id : throw new InvalidOperationException("The endpoint requires OrganizationScopeAttribute.");
    protected string? IpAddress => HttpContext.Connection.RemoteIpAddress?.ToString();

    protected static (int Page, int PageSize) NormalizePage(int page, int pageSize, int maximumPageSize = 100)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, maximumPageSize));

    protected ObjectResult ApiProblem(int status, string title, string code, string? detail = null)
        => StatusCode(status, ApiProblems.Create(HttpContext, status, title, code, detail));

    protected ActionResult IdentityProblem(IdentityResult result, string field, string code)
        => ValidationProblem(ApiProblems.Enrich(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [field] = result.Errors.Select(error => error.Description).ToArray()
        }), HttpContext, code));
}
