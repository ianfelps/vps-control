using VpsControl.Api.Data;

namespace VpsControl.Api.Audit;

public static class AuditActions
{
    public const string LoginSucceeded = "auth.login";
    public const string LoginFailed = "auth.login_failed";
    public const string Logout = "auth.logout";
}

public sealed class AuditLog(AppDbContext db, TimeProvider time, IHttpContextAccessor http)
{
    public async Task WriteAsync(
        string actor,
        string action,
        string result,
        string? target = null,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = http.HttpContext;
        db.AuditEntries.Add(new AuditEntry
        {
            Timestamp = time.GetUtcNow(),
            Actor = Truncate(actor, 128),
            Action = action,
            Result = result,
            Target = target,
            Details = details is null ? null : Truncate(details, 2048),
            RemoteIp = ctx?.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
