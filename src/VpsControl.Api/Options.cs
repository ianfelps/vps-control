namespace VpsControl.Api;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string AdminUsername { get; set; } = "admin";

    /// <summary>
    /// Usada apenas para criar o admin quando o banco está vazio. Depois pode ser removida do ambiente.
    /// </summary>
    public string? InitialAdminPassword { get; set; }

    /// <summary>Se não vazio, exige header Tailscale-User-Login com um destes valores (ex.: "voce@github").</summary>
    public IReadOnlyList<string> TailscaleAllowedUsers { get; set; } = [];

    /// <summary>Marca cookies como Secure. Desligar apenas em desenvolvimento HTTP.</summary>
    public bool SecureCookies { get; set; } = true;

    public int SessionHours { get; set; } = 8;

    /// <summary>Tentativas de login por minuto por IP.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 5;

    /// <summary>Ações administrativas (start/stop/restart, revelar env, editar app) por minuto por usuário.</summary>
    public int AdminActionsPerMinute { get; set; } = 30;
}

public sealed class MetricsOptions
{
    public const string Section = "Metrics";

    /// <summary>"Agent" (produção), "Fake" (dev/teste) ou "ProcFs" (API rodando direto no host Linux).</summary>
    public string Source { get; set; } = "Agent";

    public string? AgentSocketPath { get; set; } = "/run/vps-control/agent.sock";

    /// <summary>Alternativa ao socket para desenvolvimento, ex.: http://localhost:5081.</summary>
    public string? AgentUrl { get; set; }

    public string? AgentTokenFile { get; set; } = "/etc/vps-control/agent.token";

    public double SampleIntervalSeconds { get; set; } = 2;

    public int HistoryMinutes { get; set; } = 60;

    /// <summary>Intervalo de atualização da lista/stats de containers.</summary>
    public double ContainerIntervalSeconds { get; set; } = 5;
}
