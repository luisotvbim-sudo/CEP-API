using CepApi.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CepApi.Infrastructure.Services;

// Salted Identity V3 PBKDF2-SHA512; no plaintext or PIN-derived value is logged.
public sealed class PowerPinHasher
{
    private readonly PasswordHasher<PowerPinConfiguration> hasher = new(Options.Create(
        new PasswordHasherOptions { IterationCount = 210_000 }));

    public string Hash(PowerPinConfiguration configuration, string pin) => hasher.HashPassword(configuration, pin);
    public bool Verify(PowerPinConfiguration configuration, string pin)
        => hasher.VerifyHashedPassword(configuration, configuration.PinHash, pin) != PasswordVerificationResult.Failed;
}
