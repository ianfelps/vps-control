using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VpsControl.Api.Data;
using VpsControl.Api.Metrics;
using VpsControl.Core.Metrics;

namespace VpsControl.Api.Endpoints;

public sealed record HealthResponse(string Status, string Version);

public sealed record AuditEntryDto(
    long Id,
    DateTimeOffset Timestamp,
    string Actor,
    string Action,
    string? Target,
    string Result,
    string? RemoteIp,
    string? Details);

public static class SystemEndpoints
{
    private static readonly string Version =
        typeof(SystemEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness para Docker healthcheck: sem dados, sem auth.
        app.MapGet("/healthz", () => TypedResults.Text("ok")).AllowAnonymous().ExcludeFromDescription();

        app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok", Version)))
            .AllowAnonymous()
            .WithTags("System");

        var system = app.MapGroup("/api/system").WithTags("System").RequireAuthorization();

        system.MapGet("/status", (MetricsState state) => TypedResults.Ok(state.Status));

        system.MapGet("/snapshot", Results<Ok<HostSnapshot>, ProblemHttpResult> (MetricsState state) =>
            state.Latest is { } latest
                ? TypedResults.Ok(latest)
                : TypedResults.Problem(
                    state.Status.LastError ?? "Ainda sem amostras.",
                    title: "Métricas indisponíveis",
                    statusCode: StatusCodes.Status503ServiceUnavailable));

        system.MapGet("/history", (MetricsState state) => TypedResults.Ok(state.History()));

        app.MapGet("/api/audit", async (AppDbContext db, int? limit, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 100, 1, 500);
            var rows = await db.AuditEntries
                .OrderByDescending(a => a.Id)
                .Take(take)
                .Select(a => new AuditEntryDto(a.Id, a.Timestamp, a.Actor, a.Action, a.Target, a.Result, a.RemoteIp, a.Details))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return TypedResults.Ok(rows);
        }).WithTags("Audit").RequireAuthorization();

        return app;
    }
}
