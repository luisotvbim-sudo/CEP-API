using System.Security.Claims;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Authorization;

public sealed class OrganizationScopeService(AppDbContext db)
{
    public async Task<Guid> ResolveAsync(HttpContext context)
    {
        var selected = context.Request.Query["organizationId"];
        if (context.User.IsInRole(nameof(UserRole.SystemAdmin)))
        {
            if (selected.Count != 1 || !Guid.TryParse(selected[0], out var organizationId))
                throw new ApiProblemException(StatusCodes.Status400BadRequest,
                    "Select an organization using organizationId.", "organization_context_required");
            if (!await db.Organizations.AnyAsync(x => x.Id == organizationId, context.RequestAborted))
                throw new ApiProblemException(StatusCodes.Status404NotFound,
                    "Organization not found.", "organization_not_found");
            return organizationId;
        }

        if (!Guid.TryParse(context.User.FindFirstValue("org_id"), out var claimedOrganizationId))
            throw new ApiProblemException(StatusCodes.Status403Forbidden,
                "Organization membership is required.", "organization_context_required");
        if (selected.Count > 0 && (selected.Count != 1 ||
            !Guid.TryParse(selected[0], out var requested) || requested != claimedOrganizationId))
            throw new ApiProblemException(StatusCodes.Status403Forbidden,
                "Cannot select another organization.", "organization_context_forbidden");
        return claimedOrganizationId;
    }
}
