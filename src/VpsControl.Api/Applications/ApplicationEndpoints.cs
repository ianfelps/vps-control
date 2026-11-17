using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VpsControl.Api.Audit;
using VpsControl.Api.Containers;
using VpsControl.Api.Data;
using VpsControl.Api.Security;
using VpsControl.Core.Containers;

namespace VpsControl.Api.Applications;

public enum ApplicationStatus
{
    Running,
    Degraded,
    Stopped,
    Missing,
}

public sealed record ApplicationView(
    string Slug,
    string DisplayName,
    string? Description,
    string? Url,
    ApplicationStatus Status,
    int ContainerCount,
    int RunningCount,
    int UnhealthyCount,
    double CpuPercent,
    long MemoryUsageBytes,
    bool HasMetadata);

public sealed record ApplicationDetails(ApplicationView Application, IReadOnlyList<ContainerView> Containers);

public sealed record UpdateApplicationRequest(string? DisplayName, string? Description, string? Url);

/// <summary>Aplicações = projetos Docker Compose (label com.docker.compose.project) + metadados no SQLite.</summary>
public static partial class ApplicationEndpoints
{
    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex SlugPattern();

    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/applications").WithTags("Applications").RequireAuthorization();

        group.MapGet("/", async (ContainersService containers, AppDbContext db, CancellationToken ct) =>
        {
            var metadata = await db.Applications.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
            return TypedResults.Ok(Build(containers.Snapshot.Items, metadata).Select(a => a.Application).ToList());
        });

        group.MapGet("/{slug}", async Task<Results<Ok<ApplicationDetails>, NotFound>> (
            string slug, ContainersService containers, AppDbContext db, CancellationToken ct) =>
        {
            var metadata = await db.Applications.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
            var found = Build(containers.Snapshot.Items, metadata).FirstOrDefault(a => a.Application.Slug == slug);
            return found is null ? TypedResults.NotFound() : TypedResults.Ok(found);
        });

        group.MapPut("/{slug}", UpdateAsync)
            .RequireAuthorization(p => p.RequireClaim(Permissions.ClaimType, Permissions.ApplicationsEdit))
            .RequireRateLimiting(Permissions.AdminRateLimitPolicy);

        return app;
    }

    private static async Task<Results<Ok<ApplicationView>, ValidationProblem>> UpdateAsync(
        string slug,
        UpdateApplicationRequest request,
        ContainersService containers,
        AppDbContext db,
        AuditLog audit,
        TimeProvider time,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!SlugPattern().IsMatch(slug))
        {
            errors["slug"] = ["Slug inválido."];
        }

        var displayName = request.DisplayName?.Trim();
        var description = request.Description?.Trim();
        var url = request.Url?.Trim();
        if (displayName is { Length: > 80 })
        {
            errors["displayName"] = ["Máximo de 80 caracteres."];
        }

        if (description is { Length: > 500 })
        {
            errors["description"] = ["Máximo de 500 caracteres."];
        }

        if (!string.IsNullOrEmpty(url) && (url.Length > 300
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
        {
            errors["url"] = ["Informe uma URL http(s) válida."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var entity = await db.Applications.FindAsync([slug], ct).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new ApplicationMetadata { Slug = slug };
            db.Applications.Add(entity);
        }

        entity.DisplayName = string.IsNullOrEmpty(displayName) ? null : displayName;
        entity.Description = string.IsNullOrEmpty(description) ? null : description;
        entity.Url = string.IsNullOrEmpty(url) ? null : url;
        entity.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(user.Identity!.Name!, AuditActions.ApplicationUpdate, "success", slug, cancellationToken: ct)
            .ConfigureAwait(false);

        var metadata = await db.Applications.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(Build(containers.Snapshot.Items, metadata).First(a => a.Application.Slug == slug).Application);
    }

    internal static List<ApplicationDetails> Build(IReadOnlyList<ContainerView> containers, IReadOnlyList<ApplicationMetadata> metadata)
    {
        var byProject = containers
            .Where(c => c.ComposeProject is not null)
            .GroupBy(c => c.ComposeProject!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var meta = metadata.ToDictionary(m => m.Slug, StringComparer.Ordinal);

        return byProject.Keys.Union(meta.Keys)
            .Select(slug =>
            {
                var items = byProject.GetValueOrDefault(slug) ?? [];
                meta.TryGetValue(slug, out var m);
                var running = items.Count(c => c.State == "running");
                var unhealthy = items.Count(c => c.Health == ContainerHealth.Unhealthy);
                var status = items.Count == 0 ? ApplicationStatus.Missing
                    : running == 0 ? ApplicationStatus.Stopped
                    : running < items.Count || unhealthy > 0 ? ApplicationStatus.Degraded
                    : ApplicationStatus.Running;
                var view = new ApplicationView(
                    slug,
                    m?.DisplayName ?? slug,
                    m?.Description,
                    m?.Url,
                    status,
                    items.Count,
                    running,
                    unhealthy,
                    Math.Round(items.Sum(c => c.Stats?.CpuPercent ?? 0), 1),
                    items.Sum(c => c.Stats?.MemoryUsageBytes ?? 0),
                    m is not null);
                return new ApplicationDetails(view, items);
            })
            .OrderBy(a => a.Application.Status == ApplicationStatus.Missing)
            .ThenBy(a => a.Application.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
