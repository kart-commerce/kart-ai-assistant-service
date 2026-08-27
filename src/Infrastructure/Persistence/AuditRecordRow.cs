using Kart.AiAssistant.Domain.Enums;

namespace Kart.AiAssistant.Infrastructure.Persistence;

/// <summary>
/// EF-Core-mapped persistence model for <see cref="Domain.AuditRecords.AuditRecord"/>. A separate
/// POCO, not the Domain type itself, because <see cref="Domain.AuditRecords.AuditRecord"/>'s
/// constructor is private (constructed only via its own <c>Create</c> factory, per its own
/// "every field set once, at construction" invariant) — EF Core needs a type it can materialize
/// freely, and the Domain type is deliberately not that type (ddd-model.md's persistence-ignorance
/// stance). <see cref="AuditRecordRowMapper"/> is the only place these two shapes are reconciled.
/// </summary>
public sealed class AuditRecordRow
{
    public Guid TurnId { get; set; }

    public Guid ConversationId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public PlatformRole Role { get; set; }

    public string UserQuestion { get; set; } = string.Empty;

    /// <summary>ResolvedIntent value object (§8 schema) — jsonb.</summary>
    public string? ResolvedIntentJson { get; set; }

    public bool ClarificationIssued { get; set; }

    public string? ClarificationQuestion { get; set; }

    /// <summary>Endpoint(s) called this turn — jsonb (a JSON array, not a Postgres TEXT[], so this
    /// stays plain "one column type" consistent with the rest of this row's JSON columns).</summary>
    public string? ToolsInvokedJson { get; set; }

    public string? DataSource { get; set; }

    /// <summary>The realized from/to/granularity/filters actually sent — jsonb.</summary>
    public string? QueryParametersJson { get; set; }

    public long? ExecutionTimeMs { get; set; }

    public long? LlmPlanLatencyMs { get; set; }

    public long? LlmExplainLatencyMs { get; set; }

    /// <summary>LlmTokenUsage {plan:{...}, explain:{...}} — jsonb.</summary>
    public string? LlmTokenUsageJson { get; set; }

    public int? ResultSize { get; set; }

    public bool? IsProvisional { get; set; }

    public DateOnly? ReconciledThrough { get; set; }

    public GroundingCheckResult GroundingCheckResult { get; set; }

    /// <summary>ErrorDetail[] — jsonb.</summary>
    public string? ErrorsJson { get; set; }

    public string? FinalResponseSummary { get; set; }

    public DateTimeOffset Timestamp { get; set; }
}
