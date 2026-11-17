using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using VpsControl.Api.Endpoints;

namespace VpsControl.Api.Tests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_is_public()
    {
        var client = factory.CreateHttpsClient();

        var health = await client.GetFromJsonAsync<HealthResponse>("/api/health");

        Assert.Equal("ok", health!.Status);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
    }

    [Theory]
    [InlineData("/api/system/snapshot")]
    [InlineData("/api/system/history")]
    [InlineData("/api/audit")]
    [InlineData("/api/auth/me")]
    public async Task Protected_endpoints_require_authentication(string path)
    {
        var client = factory.CreateHttpsClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mutation_without_antiforgery_token_is_rejected()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_and_audited()
    {
        var attacker = factory.CreateHttpsClient();
        var denied = await attacker.LoginAsync("wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        var admin = factory.CreateHttpsClient();
        await admin.LoginAsync();
        var audit = await admin.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit");

        Assert.Contains(audit!, a => a.Action == "auth.login_failed" && a.Result == "denied");
    }

    [Fact]
    public async Task Login_grants_access_to_metrics()
    {
        var client = factory.CreateHttpsClient();

        var login = await client.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await client.GetFromJsonAsync<CurrentUser>("/api/auth/me");
        Assert.Equal("admin", me!.Username);

        // Sampler roda a cada 0,5 s no teste; aguarda a primeira amostra.
        HttpResponseMessage snapshot = null!;
        for (var i = 0; i < 20; i++)
        {
            snapshot = await client.GetAsync("/api/system/snapshot");
            if (snapshot.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(250);
        }

        Assert.Equal(HttpStatusCode.OK, snapshot.StatusCode);
        var body = await snapshot.Content.ReadAsStringAsync();
        Assert.Contains("\"hostname\":\"vps-dev\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_ends_session()
    {
        var client = factory.CreateHttpsClient();
        await client.LoginAsync();

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Login_works_over_plain_http_behind_tls_terminating_proxy()
    {
        // Tailscale Serve termina o TLS e repassa HTTP puro para 127.0.0.1:8080.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            HandleCookies = false,
        });

        var af = await client.GetAsync("/api/auth/antiforgery");
        var cookies = af.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).ToList();
        Assert.All(af.Headers.GetValues("Set-Cookie"), c => Assert.Contains("secure", c, StringComparison.OrdinalIgnoreCase));
        var xsrf = WebUtility.UrlDecode(cookies.Single(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))["XSRF-TOKEN=".Length..]);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "admin", password = ApiFactory.Password }),
        };
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        request.Headers.Add("X-XSRF-TOKEN", xsrf);

        var login = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Unknown_api_route_returns_404_not_spa()
    {
        var client = factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/does-not-exist")).StatusCode);
    }
}

public sealed class LoginRateLimitTests : IDisposable
{
    private readonly ApiFactory _factory = new() { LoginAttemptsPerMinute = 2 };

    [Fact]
    public async Task Brute_force_is_throttled()
    {
        var client = _factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync("wrong-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync("wrong-2")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.LoginAsync()).StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}

public sealed class BlankAllowlistTests : IDisposable
{
    // Simula VPSCTL_TAILSCALE_USER vazio no compose.
    private readonly ApiFactory _factory = new() { TailscaleAllowedUsers = [""] };

    [Fact]
    public async Task Blank_entry_disables_allowlist_instead_of_blocking_everyone()
    {
        var client = _factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("Tailscale-User-Login", "alice@github");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}

public sealed class TailscaleAllowlistTests : IDisposable
{
    private readonly ApiFactory _factory = new() { TailscaleAllowedUsers = ["alice@github"] };

    [Fact]
    public async Task Request_without_allowed_tailnet_identity_gets_403()
    {
        var client = _factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/health")).StatusCode);

        client.DefaultRequestHeaders.Add("Tailscale-User-Login", "mallory@github");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Allowed_tailnet_identity_passes_and_liveness_stays_open()
    {
        var anonymous = _factory.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/healthz")).StatusCode);

        var alice = _factory.CreateHttpsClient();
        alice.DefaultRequestHeaders.Add("Tailscale-User-Login", "alice@github");
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/api/health")).StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
