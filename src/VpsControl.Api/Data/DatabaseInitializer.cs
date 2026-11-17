using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace VpsControl.Api.Data;

/// <summary>Aplica migrations e cria o admin inicial quando o banco está vazio.</summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopes,
    IOptions<AuthOptions> auth,
    TimeProvider time,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        if (await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var options = auth.Value;
        if (string.IsNullOrWhiteSpace(options.InitialAdminPassword))
        {
            logger.LogWarning(
                "Nenhum usuário cadastrado e Auth:InitialAdminPassword vazio. Defina VPSCTL_Auth__InitialAdminPassword para criar o admin.");
            return;
        }

        if (options.InitialAdminPassword.Length < 12)
        {
            throw new InvalidOperationException("Auth:InitialAdminPassword deve ter ao menos 12 caracteres.");
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminUser>>();
        var user = new AdminUser
        {
            Username = options.AdminUsername,
            PasswordHash = string.Empty,
            CreatedAt = time.GetUtcNow(),
        };
        user.PasswordHash = hasher.HashPassword(user, options.InitialAdminPassword);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Usuário admin '{Username}' criado.", user.Username);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
