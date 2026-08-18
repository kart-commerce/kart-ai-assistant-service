using Kart.AiAssistant.Domain.Enums;

namespace Kart.AiAssistant.Domain.AuditRecords;

/// <summary>
/// One append-only row per turn (FR-011, §20's exact field list) — its own aggregate root, not a
/// child of <see cref="ConversationSessions.ConversationSession"/>, because a turn's audit record
/// must remain queryable long after the session it belonged to has expired from Redis
/// (ddd-model.md: "turns outlive a session's active lifetime"). Never updated after creation —
/// every field is set once, at construction, from data already fetched in the same turn.
/// </summary>
public sealed class AuditRecord
{
    public Guid TurnId { get; private set; }
    public Guid ConversationId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public PlatformRole Role { get; private set; }
    public string UserQuestion { get; private set; } = string.Empty;
    public string? ResolvedIntentJson { get; private set; }
    public bool ClarificationIssued { get; private set; }
    public string? ClarificationQuestion { get; private set; }
    public string? ToolsInvokedJson { get; private set; }
    public string? DataSource { get; private set; }
    public string? QueryParametersJson { get; private set; }
    public long? ExecutionTimeMs { get; private set; }
    public long? LlmPlanLatencyMs { get; private set; }
    public long? LlmExplainLatencyMs { get; private set; }
    public string? LlmTokenUsageJson { get; private set; }
    public int? ResultSize { get; private set; }
    public bool? IsProvisional { get; private set; }
    public DateOnly? ReconciledThrough { get; private set; }
    public GroundingCheckResult GroundingCheckResult { get; private set; }
    public string? ErrorsJson { get; private set; }
    public string? FinalResponseSummary { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }

    private AuditRecord()
    {
    }

    public static AuditRecord Create(
        Guid turnId,
        Guid conversationId,
        string userId,
        PlatformRole role,
        string userQuestion,
        string? resolvedIntentJson,
        bool clarificationIssued,
        string? clarificationQuestion,
        string? toolsInvokedJson,
        string? dataSource,
        string? queryParametersJson,
        long? executionTimeMs,
        long? llmPlanLatencyMs,
        long? llmExplainLatencyMs,
        string? llmTokenUsageJson,
        int? resultSize,
        bool? isProvisional,
        DateOnly? reconciledThrough,
        GroundingCheckResult groundingCheckResult,
        string? errorsJson,
        string? finalResponseSummary,
        DateTimeOffset timestamp) => new()
    {
        TurnId = turnId,
        ConversationId = conversationId,
        UserId = userId,
        Role = role,
        UserQuestion = userQuestion,
        ResolvedIntentJson = resolvedIntentJson,
        ClarificationIssued = clarificationIssued,
        ClarificationQuestion = clarificationQuestion,
        ToolsInvokedJson = toolsInvokedJson,
        DataSource = dataSource,
        QueryParametersJson = queryParametersJson,
        ExecutionTimeMs = executionTimeMs,
        LlmPlanLatencyMs = llmPlanLatencyMs,
        LlmExplainLatencyMs = llmExplainLatencyMs,
        LlmTokenUsageJson = llmTokenUsageJson,
        ResultSize = resultSize,
        IsProvisional = isProvisional,
        ReconciledThrough = reconciledThrough,
        GroundingCheckResult = groundingCheckResult,
        ErrorsJson = errorsJson,
        FinalResponseSummary = finalResponseSummary,
        Timestamp = timestamp,
    };
}
