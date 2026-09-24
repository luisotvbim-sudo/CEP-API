using System.Security.Claims;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Authorization;

public sealed class OrganizationScopeService(AppDbContext db)
{
    public async Task<Guid> ResolveAsync(
        ClaimsPrincipal principal,
        Guid? requestedOrganizationId,
        CancellationToken cancellationToken)
    {
        var role = ParseRole(principal);
        var claimedOrganizationId = ParseOrganizationId(principal);

        if (role == UserRole.SystemAdmin)
        {
            var organizationId = requestedOrganizationId ?? throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "An organization must be selected.",
                "organization_scope_required");
            if (!await db.Organizations.AsNoTracking().AnyAsync(
                    x => x.Id == organizationId, cancellationToken))
                throw new ApiProblemException(
                    StatusCodes.Status404NotFound,
                    "The selected organization was not found.",
                    "organization_not_found");
            return organizationId;
        }

        if (claimedOrganizationId is null)
            throw new ApiProblemException(
                StatusCodes.Status403Forbidden,
                "The account is not assigned to an organization.",
                "organization_scope_missing");

        if (requestedOrganizationId is not null && requestedOrganizationId != claimedOrganizationId)
            throw new ApiProblemException(
                StatusCodes.Status403Forbidden,
                "The selected organization is outside the account scope.",
                "organization_scope_forbidden");

        return claimedOrganizationId.Value;
    }

    public static UserRole ParseRole(ClaimsPrincipal principal)
        => Enum.TryParse<UserRole>(principal.FindFirstValue("role"), out var role)
            ? role
            : throw new ApiProblemException(
                StatusCodes.Status403Forbidden,
                "The account role is invalid.",
                "invalid_account_role");

    private static Guid? ParseOrganizationId(ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue("org_id"), out var organizationId)
            ? organizationId
            : null;
}
