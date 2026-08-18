namespace Kart.AiAssistant.Domain.ConversationSessions;

/// <summary>
/// Small metadata about how the current turn's intent was produced, carried alongside
/// <see cref="ResolvedIntent"/> so a later turn (or the audit trail) can tell "this was a
/// follow-up refinement" from "this was a context reset" from "this was a first turn" without
/// re-deriving it from the raw transcript (FR-007's "logged either way" requirement).
/// </summary>
public sealed record TurnProvenance(
    bool WasFollowUp,
    bool WasContextReset,
    bool UnqualifiedSuperlativeDefaultApplied,
    DateTimeOffset ResolvedAt);
