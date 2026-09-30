namespace CepApi.Domain;

public static class DomainRules
{
    public static bool WouldRemoveLastAdministrator(
        UserRole currentRole,
        UserStatus currentStatus,
        UserRole requestedRole,
        UserStatus requestedStatus,
        bool hasOtherActiveAdministrator)
    {
        var currentlyAdmin = currentRole == UserRole.OrganizationAdmin && currentStatus == UserStatus.Active;
        var remainsAdmin = requestedRole == UserRole.OrganizationAdmin && requestedStatus == UserStatus.Active;
        return currentlyAdmin && !remainsAdmin && !hasOtherActiveAdministrator;
    }
}
