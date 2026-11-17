namespace VpsControl.Api.Security;

/// <summary>
/// Permissões por operação. Hoje o admin recebe todas no login; a separação permite, no futuro,
/// usuários somente leitura sem mudar os endpoints.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "vpsctl:perm";

    public const string ContainersManage = "containers.manage";
    public const string SecretsReveal = "secrets.reveal";
    public const string ApplicationsEdit = "applications.edit";

    public static readonly string[] Admin = [ContainersManage, SecretsReveal, ApplicationsEdit];

    public const string AdminRateLimitPolicy = "admin-actions";
}
