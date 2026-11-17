namespace VpsControl.Core.Containers;

/// <summary>Mascara valores de variáveis de ambiente com nomes sensíveis.</summary>
public static class EnvMasker
{
    public const string Mask = "••••••••";

    private static readonly string[] SensitiveFragments =
    [
        "PASSWORD", "PASSWD", "SECRET", "TOKEN", "KEY", "PRIVATE", "CREDENTIAL", "AUTH",
        "API_KEY", "ACCESS", "SALT", "COOKIE", "SESSION", "DSN", "CONNECTION",
    ];

    public static bool IsSensitive(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var upper = name.ToUpperInvariant();
        return SensitiveFragments.Any(f => upper.Contains(f, StringComparison.Ordinal));
    }

    /// <summary>Converte "NOME=valor" em <see cref="EnvVar"/>, mascarando se necessário.</summary>
    public static IReadOnlyList<EnvVar> Parse(IEnumerable<string>? env, bool reveal)
    {
        if (env is null)
        {
            return [];
        }

        var result = new List<EnvVar>();
        foreach (var entry in env)
        {
            var eq = entry.IndexOf('=', StringComparison.Ordinal);
            var name = eq < 0 ? entry : entry[..eq];
            var value = eq < 0 ? string.Empty : entry[(eq + 1)..];
            // URLs com credenciais embutidas (postgres://user:senha@host) também são sensíveis.
            var sensitive = IsSensitive(name) || ContainsUrlCredentials(value);
            result.Add(sensitive && !reveal ? new EnvVar(name, Mask, true) : new EnvVar(name, value, false));
        }

        return result;
    }

    private static bool ContainsUrlCredentials(string value)
    {
        var scheme = value.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
        {
            return false;
        }

        var at = value.IndexOf('@', scheme);
        var colon = value.IndexOf(':', scheme + 3);
        return at > 0 && colon > 0 && colon < at;
    }
}
