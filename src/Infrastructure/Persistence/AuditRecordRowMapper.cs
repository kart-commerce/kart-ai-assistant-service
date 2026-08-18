using Kart.AiAssistant.Domain.AuditRecords;

namespace Kart.AiAssistant.Infrastructure.Persistence;

/// <summary>One-way mapper (Domain -> row): <see cref="IAuditRecordRepository"/>-shaped usage is
/// write-only (<c>AddAsync</c>), so there is no row-&gt;Domain direction to maintain here.</summary>
public static class AuditRecordRowMapper
{
    public static AuditRecordRow ToRow(AuditRecord record) => new()
    {
        TurnId = record.TurnId,
        ConversationId = record.ConversationId,
        UserId = record.UserId,
        Role = record.Role,
        UserQuestion = record.UserQuestion,
        ResolvedIntentJson = record.ResolvedIntentJson,
        ClarificationIssued = record.ClarificationIssued,
        ClarificationQuestion = record.ClarificationQuestion,
        ToolsInvokedJson = record.ToolsInvokedJson,
        DataSource = record.DataSource,
        QueryParametersJson = record.QueryParametersJson,
        ExecutionTimeMs = record.ExecutionTimeMs,
        LlmPlanLatencyMs = record.LlmPlanLatencyMs,
        LlmExplainLatencyMs = record.LlmExplainLatencyMs,
        LlmTokenUsageJson = record.LlmTokenUsageJson,
        ResultSize = record.ResultSize,
        IsProvisional = record.IsProvisional,
        ReconciledThrough = record.ReconciledThrough,
        GroundingCheckResult = record.GroundingCheckResult,
        ErrorsJson = record.ErrorsJson,
        FinalResponseSummary = record.FinalResponseSummary,
        Timestamp = record.Timestamp,
    };
}
