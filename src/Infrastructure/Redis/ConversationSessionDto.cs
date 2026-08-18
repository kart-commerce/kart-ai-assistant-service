using Kart.AiAssistant.Domain.ConversationSessions;

namespace Kart.AiAssistant.Infrastructure.Redis;

/// <summary>
/// The JSON wire shape stored at <c>ai-assistant:session:{conversationId}</c> (database-design.md's
/// Cache/Ephemeral-State Model section) — a plain DTO because <see cref="ConversationSession"/>
/// itself has a private constructor and only two public mutation paths (<c>Start</c>/<c>RecordTurn</c>),
/// neither of which can reproduce an arbitrary prior state (in particular,
/// <see cref="ConversationSession.UnqualifiedSuperlativeDefaultAlreadyDisclosed"/> is a running,
/// once-set-never-cleared flag that a single <c>RecordTurn</c> call replaying only the *last* turn's
/// provenance cannot reconstruct on its own). <see cref="ConversationSessionSerializer"/> is the only
/// place this DTO and the Domain aggregate are reconciled.
/// </summary>
public sealed record ConversationSessionDto(
    Guid ConversationId,
    string UserId,
    ResolvedIntent? LastResolvedIntent,
    TurnProvenance? LastTurnProvenance,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    bool UnqualifiedSuperlativeDefaultAlreadyDisclosed);
