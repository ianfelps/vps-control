using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using VpsControl.Api;
using VpsControl.Api.Audit;
using VpsControl.Api.Data;
using VpsControl.Api.Endpoints;
using VpsControl.Api.Metrics;
using VpsControl.Api.Security;
using VpsControl.Core.Metrics;
using VpsControl.Core.Metrics.ProcFs;

// `VpsControl.Api healthcheck`: usado pelo HEALTHCHECK do container (imagem chiseled não tem curl).
if (args is ["healthcheck", ..])
{
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/healthz")).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        return 1;
    }
    catch (TaskCanceledException)
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables(prefix: "VPSCTL_");

// Geração do OpenAPI no build e o `dotnet ef` sobem o host sem rodar a aplicação: não iniciar serviços de fundo.
var isToolingHost = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name
    is "GetDocument.Insider" or "ef";

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

// ---------- Opções ----------
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));
builder.Services.Configure<MetricsOptions>(builder.Configuration.GetSection(MetricsOptions.Section));
var authOptions = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
var metricsOptions = builder.Configuration.GetSection(MetricsOptions.Section).Get<MetricsOptions>() ?? new MetricsOptions();

// ---------- Persistência ----------
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=.data/vpscontrol.db";
var dataDirectory = EnsureDatabaseDirectory(connectionString);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

// Chaves de sessão/antiforgery persistidas junto ao banco: sessões sobrevivem a restart/atualização do container.
builder.Services.AddDataProtection()
    .SetApplicationName("vps-control")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();
if (!isToolingHost)
{
    builder.Services.AddHostedService<DatabaseInitializer>();
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditLog>();

// ---------- Autenticação / segurança ----------
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "vpsctl.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = authOptions.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(authOptions.SessionHours);
        o.SlidingExpiration = true;
        // API: responde 401/403 em vez de redirecionar para página de login.
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = SecurityMiddleware.XsrfHeader;
    o.Cookie.Name = "vpsctl.af";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = authOptions.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, authOptions.LoginAttemptsPerMinute), Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Rede do compose (gateway do bridge) de onde o Tailscale Serve chega ao container.
    foreach (var cidr in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
    }
});

// ---------- Métricas ----------
builder.Services.AddSingleton(_ => new MetricsState(
    (int)(metricsOptions.HistoryMinutes * 60 / Math.Max(0.5, metricsOptions.SampleIntervalSeconds)),
    metricsOptions.Source));
builder.Services.AddSingleton<IHostMetricsProvider>(sp => metricsOptions.Source.ToUpperInvariant() switch
{
    "FAKE" => new FakeHostMetricsProvider(sp.GetRequiredService<TimeProvider>()),
    "PROCFS" when OperatingSystem.IsLinux() => new ProcFsHostMetricsProvider(sp.GetRequiredService<TimeProvider>()),
    "AGENT" => new AgentHostMetricsProvider(sp.GetRequiredService<IOptions<MetricsOptions>>()),
    var other => throw new InvalidOperationException($"Metrics:Source inválido para esta plataforma: {other}"),
});
if (!isToolingHost)
{
    builder.Services.AddHostedService<MetricsSampler>();
}


// Números estritos: o OpenAPI gera `number` em vez de `number | string` para o frontend.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict);
builder.Services.AddSignalR().AddJsonProtocol(o =>
    o.PayloadSerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

// ---------- Pipeline ----------
app.UseForwardedHeaders();
if (app.Configuration.GetValue("Proxy:AssumeHttps", defaultValue: true))
{
    // A API só é alcançável via Tailscale Serve, que termina o TLS e repassa HTTP puro. Sem isso,
    // cookies Secure e o antiforgery recusariam toda requisição (e o Serve pode não enviar X-Forwarded-Proto).
    app.Use((ctx, next) =>
    {
        ctx.Request.Scheme = Uri.UriSchemeHttps;
        return next(ctx);
    });
}

app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/healthz") ? Serilog.Events.LogEventLevel.Verbose
    : Serilog.Events.LogEventLevel.Information);
app.UseExceptionHandler();
app.UseTailscaleAllowlist();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseApiAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapSystemEndpoints();
app.MapAuthEndpoints();
app.MapHub<MetricsHub>(MetricsHub.Path);

// Rotas /api desconhecidas: 404 JSON, não o index.html da SPA.
app.Map("/api/{**rest}", () => TypedResults.NotFound()).ExcludeFromDescription();
app.MapFallbackToFile("index.html");

await app.RunAsync().ConfigureAwait(false);
return 0;

static string EnsureDatabaseDirectory(string connectionString)
{
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    var dir = dataSource == ":memory:"
        ? Path.Combine(Path.GetTempPath(), "vps-control")
        : Path.GetDirectoryName(Path.GetFullPath(dataSource)) ?? Directory.GetCurrentDirectory();
    Directory.CreateDirectory(dir);
    return dir;
}

public partial class Program;
