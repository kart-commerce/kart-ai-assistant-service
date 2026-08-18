namespace Kart.AiAssistant.Domain.Enums;

/// <summary>Mirrors kart-identity-service's PlatformRoleClaims claim VALUES exactly
/// ("admin"/"support_agent") — only the two roles this capability ever admits (ADR-0025).</summary>
public enum PlatformRole
{
    Admin,
    SupportAgent,
}

public static class PlatformRoleClaimValues
{
    public const string Admin = "admin";
    public const string SupportAgent = "support_agent";

    public static PlatformRole? FromClaimValue(string value) => value switch
    {
        Admin => PlatformRole.Admin,
        SupportAgent => PlatformRole.SupportAgent,
        _ => null,
    };
}
