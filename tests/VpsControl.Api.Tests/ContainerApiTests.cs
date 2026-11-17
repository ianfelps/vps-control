using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VpsControl.Api.Tests;

/// <summary>Containers, ações, aplicações e alertas sobre o runtime fake (estado isolado por classe).</summary>
public sealed class ContainerApiTests : IClassFixture<ContainerApiTests.Factory>
{
    public sealed class Factory : IDisposable
    {
        public ApiFactory Api { get; } = new();

        public void Dispose() => Api.Dispose();
    }

    private readonly ApiFactory _factory;

    public ContainerApiTests(Factory fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _factory = fixture.Api;
    }

    private async Task<HttpClient> LoggedInAsync()
    {
        var client = _factory.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync()).StatusCode);
        return client;
    }

    private static async Task<JsonElement> WaitForContainersAsync(HttpClient client)
    {
        for (var i = 0; i < 40; i++)
        {
            var json = await client.GetFromJsonAsync<JsonElement>("/api/containers");
            if (json.GetProperty("items").GetArrayLength() > 0)
            {
                return json;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Sampler de containers não populou o cache.");
    }

    [Fact]
    public async Task Lists_containers_with_stats_and_string_enums()
    {
        var client = await LoggedInAsync();

        var json = await WaitForContainersAsync(client);
        var items = json.GetProperty("items").EnumerateArray().ToList();
        var web = items.Single(c => c.GetProperty("name").GetString() == "blog-web-1");

        Assert.Equal("healthy", web.GetProperty("health").GetString());
        Assert.Equal("blog", web.GetProperty("composeProject").GetString());
        Assert.Equal(JsonValueKind.Null, items.Single(c => c.GetProperty("name").GetString() == "backup-job").GetProperty("stats").ValueKind);
    }

    [Fact]
    public async Task Details_mask_secrets_and_reveal_is_audited()
    {
        var client = await LoggedInAsync();

        var details = await client.GetFromJsonAsync<JsonElement>("/api/containers/blog-db-1");
        var password = details.GetProperty("env").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "POSTGRES_PASSWORD");
        Assert.True(password.GetProperty("masked").GetBoolean());
        Assert.DoesNotContain("super-secret", details.GetRawText(), StringComparison.Ordinal);

        var reveal = await client.PostAsync("/api/containers/blog-db-1/env/reveal", null);
        Assert.Equal(HttpStatusCode.OK, reveal.StatusCode);
        Assert.Contains("super-secret", await reveal.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/audit");
        Assert.Contains(audit.EnumerateArray(), a =>
            a.GetProperty("action").GetString() == "container.env_reveal" && a.GetProperty("target").GetString() == "blog-db-1");
    }

    [Fact]
    public async Task Reveal_requires_antiforgery()
    {
        var client = await LoggedInAsync();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/containers/blog-db-1/env/reveal", null)).StatusCode);
    }

    [Fact]
    public async Task Stop_and_start_are_executed_and_audited()
    {
        var client = await LoggedInAsync();

        var stop = await client.PostAsync("/api/containers/shop-worker-1/stop", null);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        Assert.Equal("exited", (await stop.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());

        var start = await client.PostAsync("/api/containers/shop-worker-1/start", null);
        Assert.Equal("running", (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());

        var audit = (await client.GetFromJsonAsync<JsonElement>("/api/audit")).EnumerateArray().ToList();
        Assert.Contains(audit, a => a.GetProperty("action").GetString() == "container.stop" && a.GetProperty("result").GetString() == "success");
        Assert.Contains(audit, a => a.GetProperty("action").GetString() == "container.start" && a.GetProperty("target").GetString() == "shop-worker-1");
    }

    [Fact]
    public async Task Protected_container_returns_409_and_denial_is_audited()
    {
        var client = await LoggedInAsync();

        var response = await client.PostAsync("/api/containers/vps-control-api-1/stop", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var audit = await client.GetFromJsonAsync<JsonElement>("/api/audit");
        Assert.Contains(audit.EnumerateArray(), a =>
            a.GetProperty("action").GetString() == "container.stop" && a.GetProperty("result").GetString() == "denied");
    }

    [Theory]
    [InlineData("/api/containers/..%2F..%2Fetc/stop", HttpStatusCode.BadRequest)]
    [InlineData("/api/containers/blog-web-1/kill", HttpStatusCode.BadRequest)]
    [InlineData("/api/containers/blog-web-1/0", HttpStatusCode.BadRequest)]
    [InlineData("/api/containers/does-not-exist/restart", HttpStatusCode.NotFound)]
    public async Task Invalid_actions_and_ids_are_rejected(string path, HttpStatusCode expected)
    {
        var client = await LoggedInAsync();

        Assert.Equal(expected, (await client.PostAsync(path, null)).StatusCode);
    }

    [Fact]
    public async Task Logs_are_returned_with_tail()
    {
        var client = await LoggedInAsync();

        var lines = await client.GetFromJsonAsync<JsonElement>("/api/containers/blog-web-1/logs?tail=5");

        Assert.Equal(5, lines.GetArrayLength());
    }

    [Fact]
    public async Task Applications_are_grouped_by_compose_project_and_metadata_is_editable()
    {
        var client = await LoggedInAsync();
        await WaitForContainersAsync(client);

        var apps = (await client.GetFromJsonAsync<JsonElement>("/api/applications")).EnumerateArray().ToList();
        var shop = apps.Single(a => a.GetProperty("slug").GetString() == "shop");
        Assert.Equal("degraded", shop.GetProperty("status").GetString()); // shop-api unhealthy
        Assert.Equal(2, shop.GetProperty("containerCount").GetInt32());
        Assert.DoesNotContain(apps, a => a.GetProperty("slug").GetString() == "backup-job"); // standalone

        var bad = await client.PutAsJsonAsync("/api/applications/shop", new { displayName = "Loja", url = "javascript:alert(1)" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await client.PutAsJsonAsync("/api/applications/shop", new { displayName = "Loja", description = "E-commerce", url = "https://shop.example.com" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var detail = await client.GetFromJsonAsync<JsonElement>("/api/applications/shop");
        Assert.Equal("Loja", detail.GetProperty("application").GetProperty("displayName").GetString());
        Assert.Equal(2, detail.GetProperty("containers").GetArrayLength());
    }

    [Fact]
    public async Task Alerts_report_unhealthy_and_failed_containers()
    {
        var client = await LoggedInAsync();
        await WaitForContainersAsync(client);

        var alerts = (await client.GetFromJsonAsync<JsonElement>("/api/alerts")).EnumerateArray().ToList();

        Assert.Contains(alerts, a => a.GetProperty("id").GetString()!.StartsWith("container.unhealthy:", StringComparison.Ordinal)
                                     && a.GetProperty("severity").GetString() == "critical");
        Assert.Contains(alerts, a => a.GetProperty("id").GetString()!.StartsWith("container.exited:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Container_endpoints_require_authentication()
    {
        var anonymous = _factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/containers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/alerts")).StatusCode);
    }
}

public sealed class AdminRateLimitTests : IDisposable
{
    private readonly ApiFactory _factory = new() { AdminActionsPerMinute = 2 };

    [Fact]
    public async Task Admin_actions_are_throttled_per_user()
    {
        var client = _factory.CreateHttpsClient();
        await client.LoginAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/containers/blog-web-1/restart", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/containers/blog-web-1/restart", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/containers/blog-web-1/restart", null)).StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
