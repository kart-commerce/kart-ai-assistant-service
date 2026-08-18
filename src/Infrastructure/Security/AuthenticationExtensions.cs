using System.Security.Claims;
using Kart.AiAssistant.Domain.Enums;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Kart.AiAssistant.Infrastructure.Security;

/// <summary>
/// FR-010/§17's three-check RBAC model, this service's own middle check: a JWT that already
/// cleared the Gateway's coarse `Admin`/`Support Agent` role check (§17 check 1) must additionally
/// carry this service's own `ai-assistant.query` scope (§17 check 2, ADR-0025) before a query
/// reaches the model gateway or Analytics. The coarse-role check is re-verified here too (defense
/// in depth — the Gateway is supposed to already filter this, per the task's own framing), so a
/// caller that somehow reaches this service without going through the Gateway's own check is still
/// rejected. Mirrors kart-analytics-service's own `AnalyticsDashboardsRead` policy shape and
/// kart-admin-service's own `AddAdminAuthentication` JWT wiring.
/// </summary>
public static class AuthenticationExtensions
{
    public const string AiAssistantQueryPolicy = "AiAssistantQuery";

    private const string RolesClaimType = "roles";
    private const string ScopesClaimType = "scopes";
    private const string SpaceDelimitedScopeClaimType = "scope";
    private const string RequiredScopeValue = "ai-assistant.query";

    public static IServiceCollection AddAiAssistantAuthentication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClient<JwksSigningKeyResolver>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(SetJwtBearerOptions);

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwksSigningKeyResolver>((options, resolver) =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // kart-identity-service's JwtAccessTokenGenerator sets neither `iss` nor `aud`
                    // on the tokens it mints (confirmed) — validating either here would reject
                    // every real token, exactly as kart-admin-service's own AuthenticationExtensions
                    // already documents for the same reason.
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeyResolver = resolver.ResolveSigningKeys,
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AiAssistantQueryPolicy, policy => policy.RequireAssertion(context =>
                HasRequiredRole(context.User) && HasRequiredScope(context.User)));

        return services;
    }

    private static bool HasRequiredRole(ClaimsPrincipal user) =>
        user.FindAll(RolesClaimType).Any(claim =>
            claim.Value == PlatformRoleClaimValues.Admin || claim.Value == PlatformRoleClaimValues.SupportAgent);

    private static bool HasRequiredScope(ClaimsPrincipal user) =>
        user.FindAll(ScopesClaimType).Any(claim => claim.Value == RequiredScopeValue)
        || user.FindAll(SpaceDelimitedScopeClaimType).Any(claim =>
            claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(RequiredScopeValue));

    private static void SetJwtBearerOptions(JwtBearerOptions options)
    {
        // Keep JWT claim names ("sub", "roles", "scopes") unchanged instead of the default
        // ClaimTypes XML-URI remapping — every claim lookup above (and downstream in Application)
        // depends on the raw claim type names kart-identity-service actually issues.
        options.MapInboundClaims = false;

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Kart.AiAssistant.Infrastructure.Security.Authentication");
                logger.LogWarning(context.Exception, "Stage {Stage}: JWT authentication failed on {Path}", "AuthenticationFailed", context.Request.Path);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Kart.AiAssistant.Infrastructure.Security.Authentication");
                logger.LogDebug("Stage {Stage}: JWT validated for subject {Subject} on {Path}", "AuthenticationSucceeded", context.Principal?.FindFirst("sub")?.Value, context.Request.Path);
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Kart.AiAssistant.Infrastructure.Security.Authentication");
                logger.LogWarning("Stage {Stage}: JWT challenge on {Path} - {Error} {ErrorDescription}", "AuthenticationChallenged", context.Request.Path, context.Error, context.ErrorDescription);
                return Task.CompletedTask;
            },
        };
    }
}
