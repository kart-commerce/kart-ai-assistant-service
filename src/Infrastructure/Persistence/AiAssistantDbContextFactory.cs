using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kart.AiAssistant.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory `dotnet ef migrations add`/`database update` use to build
/// <see cref="AiAssistantDbContext"/> without spinning up the full Api host. Never used at
/// runtime — the app's own DI registration (Infrastructure/DependencyInjection.cs) takes over
/// there. Mirrors kart-admin-service's own <c>AdminDbContextFactory</c> exactly (kart-conventions.md's
/// Database Migrations & Startup Readiness section).
///
/// Local Postgres port: 5434, not 5433 — 5433 is already this platform's shared-infra default
/// (kart-devops/ports.env's <c>POSTGRES_PORT</c>) and is what kart-admin-service/kart-identity-
/// service/kart-analytics-service's own `.env.example` files already point at for a single shared
/// local Postgres instance; 5434 avoids colliding with that when this service's own local Postgres
/// is run separately.
/// </summary>
public sealed class AiAssistantDbContextFactory : IDesignTimeDbContextFactory<AiAssistantDbContext>
{
    public AiAssistantDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AI_ASSISTANT_DB_CONNECTION_STRING")
            ?? "Host=localhost;Port=5434;Database=kart_ai_assistant;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AiAssistantDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new AiAssistantDbContext(optionsBuilder.Options);
    }
}
