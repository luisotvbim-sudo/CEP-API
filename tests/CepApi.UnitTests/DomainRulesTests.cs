using CepApi.Domain;

namespace CepApi.UnitTests;

public sealed class DomainRulesTests
{
    [Fact]
    public void Removing_the_only_active_administrator_is_rejected()
    {
        var targetId = Guid.NewGuid();

        var result = DomainRules.WouldRemoveLastAdministrator(
            targetId, UserRole.OrganizationAdmin, UserStatus.Active,
            UserRole.User, UserStatus.Active, []);

        Assert.True(result);
    }

    [Fact]
    public void Removing_an_administrator_is_allowed_when_another_one_is_active()
    {
        var result = DomainRules.WouldRemoveLastAdministrator(
            Guid.NewGuid(), UserRole.OrganizationAdmin, UserStatus.Active,
            UserRole.User, UserStatus.Active, [Guid.NewGuid()]);

        Assert.False(result);
    }

    [Fact]
    public void Updating_a_regular_user_does_not_trigger_the_last_admin_rule()
    {
        var result = DomainRules.WouldRemoveLastAdministrator(
            Guid.NewGuid(), UserRole.User, UserStatus.Active,
            UserRole.User, UserStatus.Suspended, []);

        Assert.False(result);
    }
}
