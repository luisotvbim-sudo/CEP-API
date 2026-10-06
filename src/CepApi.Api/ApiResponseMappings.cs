using System.Text.Json;
using CepApi.Application;
using CepApi.Api.Controllers;
using CepApi.Domain;
using CepApi.Infrastructure.Identity;

namespace CepApi.Api;

internal static class ApiResponseMappings
{
    public static UserResponse ToUserResponse(this ApplicationUser user)
        => new(user.Id, user.DisplayName, user.Email!, user.OrganizationId, user.Role, user.Status,
            user.ProductAccesses.Select(x => x.Product).Order().ToArray());

    public static InvitationResponse ToInvitationResponse(this Invitation invitation)
        => new(invitation.Id, invitation.Email, invitation.Role, invitation.CanUseRevit, invitation.CanUseZwcad,
            invitation.ExpiresAt, invitation.AcceptedAt, invitation.RevokedAt);

    public static AuditEventResponse ToAuditEventResponse(this AuditEvent entry)
        => new(entry.Id, entry.OrganizationId, entry.ActorUserId, entry.TargetUserId,
            entry.Action, entry.DetailsJson is null ? null : JsonSerializer.Deserialize<JsonElement>(entry.DetailsJson),
            entry.IpAddress, entry.CreatedAt);
}
