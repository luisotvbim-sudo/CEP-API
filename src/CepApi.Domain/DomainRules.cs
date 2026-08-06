namespace CepApi.Domain;

public static class DomainRules
{
    public static bool WouldRemoveLastAdministrator(
        Guid targetUserId,
        UserRole currentRole,
        UserStatus currentStatus,
        UserRole requestedRole,
        UserStatus requestedStatus,
        IReadOnlyCollection<Guid> otherActiveAdministratorIds)
    {
        var currentlyAdmin = currentRole == UserRole.OrganizationAdmin && currentStatus == UserStatus.Active;
        var remainsAdmin = requestedRole == UserRole.OrganizationAdmin && requestedStatus == UserStatus.Active;
        return currentlyAdmin && !remainsAdmin && !otherActiveAdministratorIds.Any(id => id != targetUserId);
    }
}
