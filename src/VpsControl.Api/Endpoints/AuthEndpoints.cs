using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpsControl.Api.Audit;
using VpsControl.Api.Data;
using VpsControl.Api.Security;

namespace VpsControl.Api.Endpoints;

public sealed record LoginRequest(string Username, string Password);

public sealed record CurrentUser(string Username, string? TailscaleUser);

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapGet("/antiforgery", (HttpContext ctx, IOptions<AuthOptions> auth) =>
        {
            SecurityMiddleware.IssueAntiforgeryToken(ctx, auth.Value.SecureCookies);
            return TypedResults.NoContent();
        }).AllowAnonymous();

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (HttpContext ctx, AuditLog audit, IOptions<AuthOptions> auth) =>
        {
            var name = ctx.User.Identity?.Name ?? "unknown";
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            await audit.WriteAsync(name, AuditActions.Logout, "success").ConfigureAwait(false);
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
            SecurityMiddleware.IssueAntiforgeryToken(ctx, auth.Value.SecureCookies);
            return TypedResults.NoContent();
        }).RequireAuthorization();

        group.MapGet("/me", (ClaimsPrincipal user, HttpContext ctx) => TypedResults.Ok(new CurrentUser(
            user.Identity!.Name!,
            ctx.Request.Headers[SecurityMiddleware.TailscaleUserHeader].FirstOrDefault())))
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext ctx,
        AppDbContext db,
        IPasswordHasher<AdminUser> hasher,
        AuditLog audit,
        IOptions<AuthOptions> auth,
        CancellationToken ct)
    {
        var username = (request.Username ?? string.Empty).Trim();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username, ct).ConfigureAwait(false);
        var verification = user is null
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty);

        if (user is null || verification == PasswordVerificationResult.Failed)
        {
            await audit.WriteAsync(username.Length == 0 ? "(vazio)" : username, AuditActions.LoginFailed, "denied", cancellationToken: ct)
                .ConfigureAwait(false);
            return TypedResults.Problem("Usuário ou senha inválidos.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password!);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, user.Username), new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            CookieAuthenticationDefaults.AuthenticationScheme));
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal).ConfigureAwait(false);
        await audit.WriteAsync(user.Username, AuditActions.LoginSucceeded, "success", cancellationToken: ct).ConfigureAwait(false);

        // Token antiforgery é vinculado à identidade: reemite para o usuário autenticado.
        ctx.User = principal;
        SecurityMiddleware.IssueAntiforgeryToken(ctx, auth.Value.SecureCookies);

        return TypedResults.Ok(new CurrentUser(user.Username, ctx.Request.Headers[SecurityMiddleware.TailscaleUserHeader].FirstOrDefault()));
    }
}
