using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;

namespace CepApi.Api.Controllers;

internal static class ApiResponseMappings
{
    public static UserResponse ToUserResponse(this ApplicationUser user)
        => new(user.Id, user.DisplayName, user.Email!, user.OrganizationId, user.Role, user.Status,
            user.ProductAccesses.Select(x => x.Product).Order().ToArray());

    public static InvitationResponse ToInvitationResponse(this Invitation invitation)
        => new(invitation.Id, invitation.Email, invitation.Role, invitation.CanUseRevit, invitation.CanUseZwcad,
            invitation.ExpiresAt, invitation.AcceptedAt, invitation.RevokedAt);
}
