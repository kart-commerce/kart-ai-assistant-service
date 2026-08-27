namespace Kart.AiAssistant.Application.Common.Models;

/// <summary>Result of calling Analytics for one <see cref="QueryPlan"/> leg. <see cref="IsSuccess"/>
/// false means the call itself failed (timeout/5xx/circuit-open) — FR-003's own success path
/// still passes the raw body through unmodified even if e.g. the result set is empty (that is a
/// normal, successful "zero rows" outcome, not a failure — FR-009/§19's distinguishable
/// "unsupported" vs. "supported, empty" rule).</summary>
public sealed record AnalyticsQueryOutcome(
    bool IsSuccess,
    string? RawBodyJson,
    bool IsProvisional,
    DateOnly? ReconciledThrough,
    string? FailureReason);
