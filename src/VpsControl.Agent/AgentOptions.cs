namespace VpsControl.Agent;

public sealed class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>Caminho do Unix socket. Vazio = escuta em TCP 127.0.0.1:<see cref="TcpPort"/> (apenas dev).</summary>
    public string? SocketPath { get; set; } = "/run/vps-control/agent.sock";

    public int TcpPort { get; set; } = 5081;

    /// <summary>Arquivo (modo 0600/0640) com o token compartilhado entre API e agente.</summary>
    public string? TokenFile { get; set; } = "/etc/vps-control/agent.token";

    /// <summary>"ProcFs" (padrão em Linux) ou "Fake".</summary>
    public string MetricsSource { get; set; } = "ProcFs";
}
