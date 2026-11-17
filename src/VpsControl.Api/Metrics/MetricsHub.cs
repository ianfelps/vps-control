using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace VpsControl.Api.Metrics;

/// <summary>Canal de tempo real. O servidor envia "snapshot" e "status" a cada amostra.</summary>
[Authorize]
public sealed class MetricsHub : Hub
{
    public const string Path = "/hubs/metrics";
}
