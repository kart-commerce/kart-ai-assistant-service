using Kart.AiAssistant.Application.Common.Interfaces;
using Kart.AiAssistant.Infrastructure.Common;
using Kart.AiAssistant.Infrastructure.ExternalClients;
using Kart.AiAssistant.Infrastructure.ModelGateway;
using Kart.AiAssistant.Infrastructure.Persistence;
using Kart.AiAssistant.Infrastructure.Persistence.Repositories;
using Kart.AiAssistant.Infrastructure.Redis;
using Kart.AiAssistant.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Kart.AiAssistant.Infrastructure;

public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();

        AddPersistence(services, configuration);
        AddRedis(services, configuration);
        AddDownstreamClients(services, configuration);

        services.AddSingleton<IModelProvider, MockModelProvider>();

        services.AddAiAssistantAuthentication();

        return services;
    }

    /// <summary>
    /// database-design.md's single-table write model — no env-var fallback here (unlike the
    /// design-time-only <see cref="AiAssistantDbContextFactory"/>): the running app is always
    /// launched with a real host configuration (GlobalConfig-layered `ConnectionStrings:AiAssistantDatabase`),
    /// the same precedence kart-admin-service's own `AddInfrastructure` uses for `AdminDatabase`.
    /// </summary>
    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AiAssistantDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("AiAssistantDatabase")));

        services.AddScoped<IAuditRecordRepository, AuditRecordRepository>();
    }

    /// <summary>
    /// design-decisions.md's "Conversation-Session Storage" decision — one shared Redis deployment,
    /// namespaced (`ai-assistant:session:*` / `ai-assistant:session-lock:*`), lazily connected on
    /// first resolve rather than at registration time (mirrors kart-identity-service's own
    /// `IConnectionMultiplexer` registration).
    /// </summary>
    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? configuration["Redis:ConnectionString"] ?? "localhost:6379"));

        services.AddScoped<IConversationSessionRepository, RedisConversationSessionRepository>();
    }

    /// <summary>
    /// This service's exactly-one synchronous downstream dependency (ADR-0024):
    /// kart-analytics-service, called with a client-credentials token minted by
    /// kart-identity-service. Each typed HttpClient below gets its own independent Polly policy
    /// instance (design-decisions.md's "three independent circuit breakers... never a shared
    /// breaker") — mirrors kart-admin-service's own `AddDownstreamClients` shape exactly.
    /// </summary>
    private static void AddDownstreamClients(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AnalyticsServiceOptions>()
            .Bind(configuration.GetSection(AnalyticsServiceOptions.SectionName));

        services.AddOptions<IdentityServiceOptions>()
            .Bind(configuration.GetSection(IdentityServiceOptions.SectionName))
            .Validate(
                options => IsTestingEnvironment(configuration) || (!string.IsNullOrWhiteSpace(options.ClientId) && !string.IsNullOrWhiteSpace(options.ClientSecret)),
                "IdentityService:ClientId and IdentityService:ClientSecret must be configured (via the GlobalConfig-layered secret file) outside the Testing environment.")
            .ValidateOnStart();

        // Identity's client-credentials token endpoint gets its own plain HttpClient (no
        // Polly retry needed for a token fetch this provider already de-dupes internally via its
        // own SemaphoreSlim) plus the shared circuit breaker for the actual Analytics calls below.
        services.AddHttpClient<IdentityClientCredentialsTokenProvider>((sp, client) =>
            ConfigureEndpoint(client, sp.GetRequiredService<IOptions<IdentityServiceOptions>>().Value.BaseUrl));

        services.AddTransient<AnalyticsServiceHttpAuthHandler>();

        services.AddHttpClient<IAnalyticsClient, AnalyticsServiceClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<AnalyticsServiceOptions>>().Value;
                ConfigureEndpoint(client, options.BaseUrl);
            })
            .AddHttpMessageHandler<AnalyticsServiceHttpAuthHandler>()
            .AddPolicyHandler((sp, _) => ResiliencePolicies.BuildPolicy(
                TimeSpan.FromMilliseconds(sp.GetRequiredService<IOptions<AnalyticsServiceOptions>>().Value.TimeoutMilliseconds)));
    }

    private static bool IsTestingEnvironment(IConfiguration configuration) =>
        string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Testing", StringComparison.OrdinalIgnoreCase)
        || string.Equals(configuration["DOTNET_ENVIRONMENT"], "Testing", StringComparison.OrdinalIgnoreCase);

    private static void ConfigureEndpoint(HttpClient client, string baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            client.BaseAddress = new Uri(baseUrl);
        }

        // The per-attempt timeout is enforced by ResiliencePolicies' own Polly timeout policy;
        // HttpClient's own Timeout is set generously above that so it never fires first.
        client.Timeout = TimeSpan.FromSeconds(10);
    }
}
