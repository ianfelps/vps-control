using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using VpsControl.Api.Audit;
using VpsControl.Api.Security;
using VpsControl.Core.Containers;

namespace VpsControl.Api.Containers;

public sealed record ContainerActionResult(string Id, string Action, string State);

public static class ContainerEndpoints
{
    public static IEndpointRouteBuilder MapContainerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/containers").WithTags("Containers").RequireAuthorization();

        group.MapGet("/", (ContainersService service) => TypedResults.Ok(service.Snapshot));

        group.MapGet("/{id}", async Task<Results<Ok<ContainerDetails>, ProblemHttpResult>> (
            string id, ContainersService service, CancellationToken ct) =>
            await Handle<Ok<ContainerDetails>>(id, async () => TypedResults.Ok(
                await service.Runtime.InspectAsync(id, revealEnv: false, ct).ConfigureAwait(false))).ConfigureAwait(false));

        group.MapPost("/{id}/env/reveal", async Task<Results<Ok<ContainerDetails>, ProblemHttpResult>> (
            string id, ContainersService service, AuditLog audit, ClaimsPrincipal user, CancellationToken ct) =>
            await Handle<Ok<ContainerDetails>>(id, async () =>
            {
                var details = await service.Runtime.InspectAsync(id, revealEnv: true, ct).ConfigureAwait(false);
                await audit.WriteAsync(user.Identity!.Name!, AuditActions.ContainerEnvReveal, "success", details.Summary.Name, cancellationToken: ct)
                    .ConfigureAwait(false);
                return TypedResults.Ok(details);
            }).ConfigureAwait(false))
            .RequireAuthorization(p => p.RequireClaim(Permissions.ClaimType, Permissions.SecretsReveal))
            .RequireRateLimiting(Permissions.AdminRateLimitPolicy);

        group.MapGet("/{id}/logs", async Task<Results<Ok<List<LogLine>>, ProblemHttpResult>> (
            string id, int? tail, ContainersService service, CancellationToken ct) =>
            await Handle<Ok<List<LogLine>>>(id, async () =>
            {
                var lines = new List<LogLine>();
                await foreach (var line in service.Runtime.LogsAsync(id, Math.Clamp(tail ?? 200, 1, GuardedContainerRuntime.MaxTail), follow: false, ct)
                                   .ConfigureAwait(false))
                {
                    lines.Add(line);
                }

                return TypedResults.Ok(lines);
            }).ConfigureAwait(false));

        group.MapPost("/{id}/{action}", ExecuteAsync)
            .RequireAuthorization(p => p.RequireClaim(Permissions.ClaimType, Permissions.ContainersManage))
            .RequireRateLimiting(Permissions.AdminRateLimitPolicy);

        return app;
    }

    private static async Task<Results<Ok<ContainerActionResult>, ProblemHttpResult>> ExecuteAsync(
        string id,
        string action,
        ContainersService service,
        AuditLog audit,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!Enum.TryParse<ContainerAction>(action, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)
            || int.TryParse(action, out _))
        {
            return TypedResults.Problem("Ação inválida. Use start, stop ou restart.", statusCode: StatusCodes.Status400BadRequest);
        }

        var actor = user.Identity!.Name!;
        var auditAction = AuditActions.ContainerAction(parsed);
        string target = id;

        var result = await Handle<Ok<ContainerActionResult>>(id, async () =>
        {
            // Nome legível na auditoria; também falha cedo se o container não existir.
            target = (await service.Runtime.InspectAsync(id, revealEnv: false, ct).ConfigureAwait(false)).Summary.Name;
            await service.Runtime.ExecuteAsync(id, parsed, ct).ConfigureAwait(false);
            await audit.WriteAsync(actor, auditAction, "success", target, cancellationToken: ct).ConfigureAwait(false);

            // Atualiza o cache e notifica os clientes sem esperar o próximo ciclo do sampler.
            await service.RefreshAsync(includeStats: false, ct).ConfigureAwait(false);
            var state = service.Snapshot.Items.FirstOrDefault(c => c.Id.StartsWith(id, StringComparison.Ordinal) || c.Name == id)?.State
                ?? "unknown";
            return TypedResults.Ok(new ContainerActionResult(id, parsed.ToString().ToLowerInvariant(), state));
        }).ConfigureAwait(false);

        if (result.Result is ProblemHttpResult problem)
        {
            var outcome = problem.StatusCode switch
            {
                StatusCodes.Status409Conflict => "denied",       // container protegido
                StatusCodes.Status400BadRequest or StatusCodes.Status404NotFound => "rejected",
                _ => "failed",
            };
            await audit.WriteAsync(actor, auditAction, outcome, target,
                problem.ProblemDetails.Detail, ct).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>Exceções de domínio → respostas HTTP (400/404/409/502).</summary>
    private static async Task<Results<T, ProblemHttpResult>> Handle<T>(string id, Func<Task<T>> body)
        where T : IResult
    {
        if (!GuardedContainerRuntime.IsValidId(id))
        {
            return TypedResults.Problem("Identificador de container inválido.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (ContainerNotFoundException)
        {
            return TypedResults.Problem($"Container '{id}' não encontrado.", statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidContainerIdException)
        {
            return TypedResults.Problem("Identificador de container inválido.", statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ContainerProtectedException e)
        {
            return TypedResults.Problem(e.Message, title: "Container protegido", statusCode: StatusCodes.Status409Conflict);
        }
        catch (Grpc.Core.RpcException)
        {
            return TypedResults.Problem("Agente indisponível.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException e)
        {
            return TypedResults.Problem(e.Message, title: "Erro do Docker", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
