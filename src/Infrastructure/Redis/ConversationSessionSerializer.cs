using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kart.AiAssistant.Domain.ConversationSessions;

namespace Kart.AiAssistant.Infrastructure.Redis;

/// <summary>
/// Serializes/rehydrates <see cref="ConversationSession"/> for Redis storage. Every field
/// <see cref="ConversationSession"/> exposes is a plain public getter, so serialization is a
/// straight DTO projection (<see cref="ConversationSessionDto"/>) via <c>System.Text.Json</c> —
/// no reflection needed on that side. Rehydration is the hard direction: the aggregate's own public
/// surface (private constructor, <c>Start</c>/<c>RecordTurn</c>) has no way to reproduce an
/// arbitrary persisted state (see <see cref="ConversationSessionDto"/>'s doc comment), and Domain
/// is a fixed contract this service may not touch to add an internal factory method. Reflectively
/// materializing the instance here is Infrastructure-local plumbing, not a change to
/// <see cref="ConversationSession"/>'s own public API — its constructor stays private, its only
/// real business-logic mutation paths stay `Start`/`RecordTurn`; this is the persistence layer
/// doing what an ORM's own change-tracker/materializer would otherwise do for it.
/// </summary>
public static class ConversationSessionSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(ConversationSession session)
    {
        var dto = new ConversationSessionDto(
            session.ConversationId,
            session.UserId,
            session.LastResolvedIntent,
            session.LastTurnProvenance,
            session.CreatedAt,
            session.LastActivityAt,
            session.UnqualifiedSuperlativeDefaultAlreadyDisclosed);

        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static ConversationSession Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<ConversationSessionDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Stored ConversationSession JSON deserialized to null.");

        var session = (ConversationSession)RuntimeHelpers.GetUninitializedObject(typeof(ConversationSession));

        SetProperty(session, nameof(ConversationSession.ConversationId), dto.ConversationId);
        SetProperty(session, nameof(ConversationSession.UserId), dto.UserId);
        SetProperty(session, nameof(ConversationSession.LastResolvedIntent), dto.LastResolvedIntent);
        SetProperty(session, nameof(ConversationSession.LastTurnProvenance), dto.LastTurnProvenance);
        SetProperty(session, nameof(ConversationSession.CreatedAt), dto.CreatedAt);
        SetProperty(session, nameof(ConversationSession.LastActivityAt), dto.LastActivityAt);
        SetProperty(session, nameof(ConversationSession.UnqualifiedSuperlativeDefaultAlreadyDisclosed), dto.UnqualifiedSuperlativeDefaultAlreadyDisclosed);

        return session;
    }

    private static void SetProperty(ConversationSession session, string propertyName, object? value)
    {
        var property = typeof(ConversationSession).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"ConversationSession has no property named {propertyName}.");

        property.SetValue(session, value);
    }
}
