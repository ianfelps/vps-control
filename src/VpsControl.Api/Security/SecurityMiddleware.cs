using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace VpsControl.Api.Security;

public static class SecurityMiddleware
{
    public const string TailscaleUserHeader = "Tailscale-User-Login";
    public const string XsrfCookie = "XSRF-TOKEN";
    public const string XsrfHeader = "X-XSRF-TOKEN";

    /// <summary>
    /// Allowlist de identidades Tailscale. O Tailscale Serve injeta o header; como a API só escuta em
    /// 127.0.0.1, ele não pode ser forjado por clientes da tailnet.
    /// </summary>
    public static IApplicationBuilder UseTailscaleAllowlist(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            // Entradas vazias (ex.: variável de ambiente não preenchida no compose) não contam.
            var allowed = ctx.RequestServices.GetRequiredService<IOptionsMonitor<AuthOptions>>().CurrentValue
                .TailscaleAllowedUsers.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (allowed.Count > 0 && !IsHealthProbe(ctx.Request.Path))
            {
                var user = ctx.Request.Headers[TailscaleUserHeader].ToString();
                if (!allowed.Contains(user, StringComparer.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new { error = "tailnet identity not allowed" }).ConfigureAwait(false);
                    return;
                }
            }

            await next(ctx).ConfigureAwait(false);
        });

    /// <summary>Valida o token antiforgery (double-submit) em toda mutação sob /api.</summary>
    public static IApplicationBuilder UseApiAntiforgery(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var method = ctx.Request.Method;
            var unsafeMethod = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));
            if (unsafeMethod && ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
                if (!await antiforgery.IsRequestValidAsync(ctx).ConfigureAwait(false))
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await ctx.Response.WriteAsJsonAsync(new { error = "invalid antiforgery token" }).ConfigureAwait(false);
                    return;
                }
            }

            await next(ctx).ConfigureAwait(false);
        });

    /// <summary>Emite token antiforgery vinculado ao usuário atual em cookie legível pelo JS.</summary>
    public static void IssueAntiforgeryToken(HttpContext ctx, bool secureCookies)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        ctx.Response.Cookies.Append(XsrfCookie, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = secureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }

    private static bool IsHealthProbe(PathString path) => path.StartsWithSegments("/healthz", StringComparison.Ordinal);
}
