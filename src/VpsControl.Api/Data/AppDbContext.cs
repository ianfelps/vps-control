using Microsoft.EntityFrameworkCore;

namespace VpsControl.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> Users => Set<AdminUser>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AdminUser>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(64);
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.HasIndex(a => a.Timestamp);
            e.Property(a => a.Actor).HasMaxLength(128);
            e.Property(a => a.Action).HasMaxLength(64);
            e.Property(a => a.Target).HasMaxLength(256);
            e.Property(a => a.Result).HasMaxLength(32);
            e.Property(a => a.RemoteIp).HasMaxLength(64);
            e.Property(a => a.Details).HasMaxLength(2048);
            // SQLite não ordena DateTimeOffset nativamente; armazena como ticks UTC.
            e.Property(a => a.Timestamp).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAppendOnlyAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceAppendOnlyAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Auditoria é append-only: a aplicação nunca altera nem remove registros.</summary>
    private void EnforceAppendOnlyAudit()
    {
        var tampered = ChangeTracker.Entries<AuditEntry>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (tampered)
        {
            throw new InvalidOperationException("Registros de auditoria não podem ser alterados ou removidos.");
        }
    }
}

public sealed class AdminUser
{
    public int Id { get; set; }

    public required string Username { get; set; }

    public required string PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public required string Actor { get; set; }

    public required string Action { get; set; }

    public string? Target { get; set; }

    public required string Result { get; set; }

    public string? RemoteIp { get; set; }

    public string? Details { get; set; }
}
