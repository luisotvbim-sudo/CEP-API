using CepApi.Application;
using CepApi.Domain;
using CepApi.Infrastructure.Persistence;
using CepApi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace CepApi.Api.Startup;

internal static class PowerPinProvisioning
{
    public static async Task RunAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Use um terminal interativo: o PIN não pode vir de arquivo, pipe ou argumentos.");
        Console.Write("Novo PIN administrativo (6 dígitos, sem eco): ");
        var pin = ReadSecret();
        Console.Write("Confirme o PIN (sem eco): ");
        var confirmation = ReadSecret();
        if (pin.Length != 6 || pin.Any(c => c is < '0' or > '9') || pin != confirmation)
            throw new InvalidOperationException("PIN inválido ou confirmação diferente. Nada foi alterado.");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PowerPinHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Serializes first provisioning as well as concurrent rotations.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718930113)");
        var configuration = await db.Set<PowerPinConfiguration>()
            .FromSqlRaw("SELECT * FROM time_control.power_pin_configuration WHERE \"Id\" = 1 FOR UPDATE")
            .SingleOrDefaultAsync();
        if (configuration is null)
        {
            configuration = new PowerPinConfiguration { PinHash = "" };
            db.Add(configuration);
        }
        configuration.PinHash = hasher.Hash(configuration, pin);
        configuration.Version = Guid.NewGuid();
        configuration.UpdatedAt = clock.UtcNow;
        await scope.ServiceProvider.GetRequiredService<IAuditService>().WriteAsync("power.pin_rotated",
            details: new { configuration.Version, configuration.UpdatedAt, source = "interactive_cli" });
        await transaction.CommitAsync();
        Console.WriteLine("PIN configurado. Janelas anteriores invalidadas. Nenhum valor secreto foi exibido.");
    }

    private static string ReadSecret()
    {
        var characters = new List<char>(6);
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Escape) throw new OperationCanceledException("Operação cancelada.");
            if (key.Key == ConsoleKey.Backspace) { if (characters.Count > 0) characters.RemoveAt(characters.Count - 1); }
            else if (characters.Count < 7) characters.Add(key.KeyChar);
        }
        Console.WriteLine();
        return new string(characters.ToArray());
    }
}
