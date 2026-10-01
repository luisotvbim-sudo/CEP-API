using System.Security.Cryptography;
using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Services;

namespace CepApi.UnitTests;

public sealed class PowerPinTests
{
    [Fact]
    public void Dedicated_hash_is_salted_and_request_to_string_redacts_pin()
    {
        var pin = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
        var configuration = new PowerPinConfiguration { PinHash = "" };
        var hasher = new PowerPinHasher();
        configuration.PinHash = hasher.Hash(configuration, pin);
        Assert.NotEqual(configuration.PinHash, hasher.Hash(configuration, pin));
        Assert.True(hasher.Verify(configuration, pin));
        Assert.False(hasher.Verify(configuration, (pin[0] == '9' ? "0" : "9") + pin[1..]));
        Assert.DoesNotContain(pin, new PowerActionUnlockRequest { Pin = pin }.ToString());
    }
}
