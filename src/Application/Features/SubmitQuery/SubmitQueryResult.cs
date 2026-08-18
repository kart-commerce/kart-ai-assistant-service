using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;

namespace Kart.AiAssistant.Application.Features.SubmitQuery;

/// <summary>Which of api-contract.yaml's three discriminated response shapes a turn produced.</summary>
public enum SubmitQueryResultKind
{
    Answer,
    Clarification,
    Error,
}

/// <summary>The structured data table behind an answer (FR-005, §13.2's <c>AssistantResultTable</c>)
/// — always present for <see cref="SubmitQueryResultKind.Answer"/>, never omitted in favor of a
/// chart alone. <see cref="Rows"/> may be empty (FR-009's "supported, zero results" case) but is
/// never null.</summary>
public sealed record AssistantResultTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>§13.2's <c>visualization</c> object — <c>Type</c> is the one field every successful
/// answer populates (never null for <see cref="SubmitQueryResultKind.Answer"/>, per
/// ddd-model.md), chosen by <see cref="VisualizationSelector"/>.</summary>
public sealed record AssistantVisualization(VisualizationType Type, string Title, string? XAxis, string? YAxis);

/// <summary>§13.2's <c>metadata</c> object — provenance/observability facts surfaced on every
/// answer (FR-012: <see cref="IsProvisional"/>/<see cref="ReconciledThrough"/> are never omitted
/// when the underlying Analytics result carries them).</summary>
public sealed record AssistantResponseMetadata(
    ResolvedIntent Intent,
    string Source,
    bool? IsProvisional,
    DateOnly? ReconciledThrough,
    DateTimeOffset GeneratedAt);

/// <summary>The (a) success shape — source spec §13.2's full answer envelope.</summary>
public sealed record AssistantAnswer(
    string AnswerText,
    AssistantResultTable Data,
    AssistantVisualization Visualization,
    AssistantResponseMetadata Metadata);

/// <summary>The (b) clarification shape — §15.3, returned instead of a data answer; no query was
/// executed for this turn.</summary>
public sealed record AssistantClarification(string Question, IReadOnlyList<string> Options);

/// <summary>The (c) error shape — api-contract.yaml's reconciled three-member
/// <see cref="ResponseErrorType"/> enum (<c>unsupported</c>/<c>unavailable</c>/
/// <c>expired-session</c>; <c>ambiguous</c>/<c>unauthorized</c>/<c>no-data</c> are represented by
/// the other two variants or never reach this layer at all — see that enum's own doc comment).</summary>
public sealed record AssistantError(ResponseErrorType Type, string Message);

/// <summary>
/// The single discriminated result of one <see cref="SubmitQuery.SubmitQueryCommand"/> turn —
/// models api-contract.yaml's `oneOf [AssistantAnswerResponse, AssistantClarificationResponse,
/// AssistantErrorResponse]` losslessly as one C# type rather than three separate MediatR response
/// types, since exactly one of <see cref="Answer"/>/<see cref="Clarification"/>/<see cref="Error"/>
/// is ever populated (indicated by <see cref="Kind"/>) and a caller (e.g. the Api layer's response
/// mapper) needs a single return type to switch on. <see cref="ConversationId"/> is null only for
/// the <c>expired-session</c> error variant (api-contract.yaml: "never echoed back as if it were
/// still valid").
/// </summary>
public sealed record SubmitQueryResult(
    SubmitQueryResultKind Kind,
    Guid? ConversationId,
    AssistantAnswer? Answer,
    AssistantClarification? Clarification,
    AssistantError? Error)
{
    public static SubmitQueryResult ForAnswer(Guid conversationId, AssistantAnswer answer) =>
        new(SubmitQueryResultKind.Answer, conversationId, answer, null, null);

    public static SubmitQueryResult ForClarification(Guid conversationId, AssistantClarification clarification) =>
        new(SubmitQueryResultKind.Clarification, conversationId, null, clarification, null);

    public static SubmitQueryResult ForError(Guid? conversationId, AssistantError error) =>
        new(SubmitQueryResultKind.Error, conversationId, null, null, error);
}
