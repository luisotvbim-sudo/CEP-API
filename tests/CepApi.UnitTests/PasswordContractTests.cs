using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CepApi.Application;
using CepApi.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace CepApi.UnitTests;

public sealed class PasswordContractTests
{
    [Fact]
    public void Identity_allows_six_characters_without_complexity_requirements()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().Build(),
            new HostingEnvironment { EnvironmentName = "Development" });
        using var provider = services.BuildServiceProvider();
        var password = provider.GetRequiredService<IOptions<IdentityOptions>>().Value.Password;
        Assert.Equal(6, password.RequiredLength);
        Assert.False(password.RequireDigit);
        Assert.False(password.RequireLowercase);
        Assert.False(password.RequireUppercase);
        Assert.False(password.RequireNonAlphanumeric);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void New_password_boundaries_are_consistent(int length, bool expected)
    {
        var password = new string('a', length);
        object[] requests =
        [
            new AcceptInvitationRequest("person@example.com", "test-code", "Person", password, null),
            new ResetPasswordRequest("person@example.com", "test-code", password),
            new ChangePasswordRequest("old-password", password)
        ];
        foreach (var request in requests)
        {
            // MVC reads validation metadata from primary constructor parameters
            // for records, rather than from their generated properties.
            var parameter = request.GetType().GetConstructors().Single().GetParameters()
                .Single(x => x.Name is "Password" or "NewPassword");
            Assert.Equal(expected, parameter.GetCustomAttributes<ValidationAttribute>()
                .All(attribute => attribute.IsValid(password)));
        }
    }
}
