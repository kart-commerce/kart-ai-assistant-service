namespace Kart.AiAssistant.Infrastructure.ExternalClients;

/// <summary>
/// Binds the "IdentityService" configuration section — this service's own service-principal
/// credentials for kart-identity-service's OAuth2 Client Credentials grant (<c>POST /v1/auth/token</c>,
/// confirmed real endpoint, kart-identity-service's own <c>AuthEndpoints.cs</c>), requesting
/// <c>analytics.dashboards.read</c> (FR-003/§17: "the exact mechanism Analytics already requires of
/// every internal caller"). Default port 8081 is kart-identity-service's own real local dev port
/// (its <c>launchSettings.json</c> http profile / kart-devops/ports.env's <c>IDENTITY_PORT</c>).
/// </summary>
public sealed class IdentityServiceOptions
{
    public const string SectionName = "IdentityService";

    public string BaseUrl { get; set; } = "http://localhost:8081";

    /// <summary>kart-identity-service's unversioned, unauthenticated JWKS discovery path
    /// (JwksEndpoints.cs) — this service validates every inbound caller's JWT signature against it.</summary>
    public string JwksUri { get; set; } = "http://localhost:8081/.well-known/jwks.json";

    /// <summary>Secret — must come from config/GlobalConfig, never a committed default (validated
    /// at startup in non-Testing environments, see <c>InfrastructureDependencyInjection</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Secret — must come from config/GlobalConfig, never a committed default.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    public string Scope { get; set; } = "analytics.dashboards.read";
}
