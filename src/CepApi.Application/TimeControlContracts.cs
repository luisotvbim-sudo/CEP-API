using System.ComponentModel.DataAnnotations;
using CepApi.Domain;

namespace CepApi.Application;

public sealed record CreateWorkforceTeamRequest([Required, MaxLength(120)] string Name);

public sealed record UpdateWorkforceTeamRequest([Required, MaxLength(120)] string Name, bool IsActive);

public sealed record CreateTeamAssignmentRequest(
    Guid UserId,
    TeamAssignmentRole Role,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record EndTeamAssignmentRequest(DateOnly EffectiveTo);

public sealed record WorkforceTeamResponse(
    Guid Id,
    string Name,
    bool IsActive,
    int ActiveMembers,
    int ActiveManagers,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TeamAssignmentResponse(
    Guid Id,
    Guid TeamId,
    Guid UserId,
    string UserDisplayName,
    string UserEmail,
    TeamAssignmentRole Role,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DateTimeOffset CreatedAt);
