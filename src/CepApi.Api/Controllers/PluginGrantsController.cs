using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Controllers;

[Route("api/v1/plugin/grants")]
[Authorize(Roles = $"{nameof(UserRole.SystemAdmin)},{nameof(UserRole.OrganizationAdmin)},{nameof(UserRole.User)}")]
[OrganizationScope]
public sealed class PluginGrantsController(
    AppDbContext db,
    ITokenService tokenService,
    IClock clock,
    IAuditService audit) : ApiControllerBase
{
    [HttpPost]
    [EnableRateLimiting("account")]
    public async Task<ActionResult<PluginGrantResponse>> Create(
        CreatePluginGrantRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PluginVersion) || string.IsNullOrWhiteSpace(request.InstallationId))
            return ApiProblem(StatusCodes.Status400BadRequest, "Plugin version and installation id are required.", "invalid_plugin_client");

        var user = await db.Users.AsNoTracking().Include(x => x.Organization).Include(x => x.ProductAccesses)
            .SingleAsync(x => x.Id == CurrentUserId, cancellationToken);
        var organizationId = CurrentOrganizationId!.Value;
        var organizationActive = await db.Organizations.AnyAsync(x => x.Id == organizationId && x.Status == OrganizationStatus.Active, cancellationToken);
        if (user.Status != UserStatus.Active || !organizationActive)
            return ApiProblem(StatusCodes.Status403Forbidden, "Account or organization is inactive.", "account_inactive");
        if (user.Role != UserRole.SystemAdmin && !user.ProductAccesses.Any(x => x.Product == request.Product))
            return ApiProblem(StatusCodes.Status403Forbidden, "Product access was not granted.", "product_access_denied");

        var tokenUser = new TokenUser(user.Id, user.DisplayName, user.Email!, organizationId, user.Role);
        var grant = tokenService.CreatePluginGrant(tokenUser, request.Product, clock.UtcNow);
        await audit.WriteAsync("plugin.grant_issued", organizationId, user.Id, user.Id,
            new { product = request.Product.ToString(), request.PluginVersion, request.InstallationId }, IpAddress, cancellationToken);
        return Ok(new PluginGrantResponse(grant.Token, grant.ExpiresAt));
    }
}
