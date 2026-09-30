using CepApi.Domain;

namespace CepApi.UnitTests;

public sealed class DomainRulesTests
{
    [Fact]
    public void Removing_the_only_active_administrator_is_rejected()
    {
        var result = DomainRules.WouldRemoveLastAdministrator(
            UserRole.OrganizationAdmin, UserStatus.Active,
            UserRole.User, UserStatus.Active, false);

        Assert.True(result);
    }

    [Fact]
    public void Removing_an_administrator_is_allowed_when_another_one_is_active()
    {
        var result = DomainRules.WouldRemoveLastAdministrator(
            UserRole.OrganizationAdmin, UserStatus.Active,
            UserRole.User, UserStatus.Active, true);

        Assert.False(result);
    }

    [Fact]
    public void Updating_a_regular_user_does_not_trigger_the_last_admin_rule()
    {
        var result = DomainRules.WouldRemoveLastAdministrator(
            UserRole.User, UserStatus.Active,
            UserRole.User, UserStatus.Suspended, false);

        Assert.False(result);
    }
}
