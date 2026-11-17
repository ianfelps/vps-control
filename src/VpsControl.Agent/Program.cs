using Microsoft.AspNetCore.Server.Kestrel.Core;
using VpsControl.Agent;
using VpsControl.Agent.Services;
using VpsControl.Core.Metrics;
using VpsControl.Core.Metrics.ProcFs;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Host.UseSystemd();

var options = builder.Configuration.GetSection(AgentOptions.Section).Get<AgentOptions>() ?? new AgentOptions();
var useSocket = !string.IsNullOrWhiteSpace(options.SocketPath) && OperatingSystem.IsLinux();

builder.WebHost.ConfigureKestrel(k =>
{
    if (useSocket)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.SocketPath!)!);
        if (File.Exists(options.SocketPath))
        {
            File.Delete(options.SocketPath!); // socket órfão de execução anterior
        }

        k.ListenUnixSocket(options.SocketPath!, l => l.Protocols = HttpProtocols.Http2);
    }
    else
    {
        // Somente desenvolvimento: TCP restrito a loopback.
        k.ListenLocalhost(options.TcpPort, l => l.Protocols = HttpProtocols.Http2);
    }
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(LoadToken(options, builder.Environment));
builder.Services.AddSingleton<IHostMetricsProvider>(sp =>
    options.MetricsSource.Equals("Fake", StringComparison.OrdinalIgnoreCase) || !OperatingSystem.IsLinux()
        ? new FakeHostMetricsProvider(sp.GetRequiredService<TimeProvider>())
        : new ProcFsHostMetricsProvider(sp.GetRequiredService<TimeProvider>()));
builder.Services.AddGrpc(o => o.Interceptors.Add<TokenAuthInterceptor>());

var app = builder.Build();
app.MapGrpcService<AgentGrpcService>();

if (useSocket)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // root:vpsctl 0660 — o grupo vem do Group= da unit systemd.
        File.SetUnixFileMode(
            options.SocketPath!,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
    });
}

app.Logger.LogInformation(
    "Agente escutando em {Endpoint} (métricas: {Source})",
    useSocket ? $"unix:{options.SocketPath}" : $"http://localhost:{options.TcpPort}",
    options.MetricsSource);

await app.RunAsync().ConfigureAwait(false);

static AgentToken LoadToken(AgentOptions options, IHostEnvironment env)
{
    if (!string.IsNullOrWhiteSpace(options.TokenFile) && File.Exists(options.TokenFile))
    {
        var value = File.ReadAllText(options.TokenFile).Trim();
        if (value.Length < 32)
        {
            throw new InvalidOperationException($"Token em {options.TokenFile} deve ter ao menos 32 caracteres.");
        }

        return new AgentToken(value);
    }

    if (env.IsDevelopment())
    {
        return new AgentToken(null);
    }

    throw new InvalidOperationException(
        $"Arquivo de token '{options.TokenFile}' não encontrado. Gere com deploy/scripts/install-agent.sh.");
}
