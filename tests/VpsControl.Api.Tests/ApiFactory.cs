using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VpsControl.Api.Tests;

/// <summary>Sobe a API com SQLite temporário e métricas fake.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "test-password-123";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"vpsctl-test-{Guid.NewGuid():N}.db");

    public string[] TailscaleAllowedUsers { get; init; } = [];

    public int LoginAttemptsPerMinute { get; init; } = 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
        builder.UseSetting("Auth:InitialAdminPassword", Password);
        builder.UseSetting("Auth:SecureCookies", "true");
        builder.UseSetting("Auth:LoginAttemptsPerMinute", LoginAttemptsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Metrics:Source", "Fake");
        builder.UseSetting("Metrics:SampleIntervalSeconds", "0.5");
        for (var i = 0; i < TailscaleAllowedUsers.Length; i++)
        {
            builder.UseSetting($"Auth:TailscaleAllowedUsers:{i}", TailscaleAllowedUsers[i]);
        }
    }

    /// <summary>Cliente HTTPS (cookies Secure) com cookies persistentes.</summary>
    public HttpClient CreateHttpsClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // arquivo temporário; ignora se ainda estiver em uso
        }
    }
}

internal static class ClientExtensions
{
    /// <summary>Obtém o token antiforgery e o coloca como header padrão.</summary>
    public static async Task PrimeAntiforgeryAsync(this HttpClient client, HttpResponseMessage? fromResponse = null)
    {
        var response = fromResponse ?? await client.GetAsync("/api/auth/antiforgery");
        var token = response.Headers.GetValues("Set-Cookie")
            .Select(c => c.Split(';')[0])
            .Where(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))
            .Select(c => WebUtility.UrlDecode(c["XSRF-TOKEN=".Length..]))
            .Last();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
    }

    public static async Task<HttpResponseMessage> LoginAsync(this HttpClient client, string password = ApiFactory.Password)
    {
        await client.PrimeAntiforgeryAsync();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password });
        if (response.IsSuccessStatusCode)
        {
            await client.PrimeAntiforgeryAsync(response);
        }

        return response;
    }
}
