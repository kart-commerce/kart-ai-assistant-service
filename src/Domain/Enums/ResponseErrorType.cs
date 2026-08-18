namespace Kart.AiAssistant.Domain.Enums;

/// <summary>
/// The reconciled error taxonomy (api-contract.yaml's "Error-taxonomy reconciliation" comment):
/// "ambiguous" is represented by the dedicated clarification response, "unauthorized"/"no-data"
/// never reach a 200 body, so only these three remain.
/// </summary>
public enum ResponseErrorType
{
    Unsupported,
    Unavailable,
    ExpiredSession,
}

/// <summary>Distinguishes "unsupported capability" (FR-009) from "supported, execution failed
/// mid-flight" so callers can log/alert differently even though both currently render the same
/// Unavailable wire type for anything that isn't a registry miss.</summary>
public enum GroundingCheckResult
{
    NotApplicable,
    Pass,
    Fail,
}
