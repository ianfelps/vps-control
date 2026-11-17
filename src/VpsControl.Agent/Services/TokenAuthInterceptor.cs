using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using VpsControl.Contracts;

namespace VpsControl.Agent.Services;

/// <summary>Exige o token compartilhado em toda chamada. Comparação em tempo constante.</summary>
public sealed class TokenAuthInterceptor(AgentToken token, ILogger<TokenAuthInterceptor> logger) : Interceptor
{
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);
        Authorize(context);
        return continuation(request, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);
        Authorize(context);
        return continuation(request, responseStream, context);
    }

    private void Authorize(ServerCallContext context)
    {
        if (token.Value is null)
        {
            return; // somente em Development sem token configurado (ver Program.cs)
        }

        var provided = context.RequestHeaders.GetValue(AgentMapping.TokenHeader) ?? string.Empty;
        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(token.Value));
        if (!ok)
        {
            logger.LogWarning("Chamada rejeitada: token inválido para {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "invalid agent token"));
        }
    }
}

public sealed record AgentToken(string? Value);
