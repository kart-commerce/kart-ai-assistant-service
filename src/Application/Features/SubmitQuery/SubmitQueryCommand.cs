using Kart.AiAssistant.Domain.Enums;
using Kart.Shared.Domain;
using MediatR;

namespace Kart.AiAssistant.Application.Features.SubmitQuery;

/// <summary>
/// One conversational turn (source spec §21.1's <c>AssistantQueryRequest</c>, FR-001) — the single
/// MediatR entry point this whole Application layer exists to serve. <see cref="ConversationId"/>
/// null starts a new <c>ConversationSession</c> (ddd-model.md); non-null continues an existing one
/// as a follow-up (FR-007, §14). <see cref="Role"/> is the Gateway/Identity-issued coarse role
/// claim, carried here only for the audit trail (§20) — RBAC itself has already run before this
/// command is ever dispatched (FR-010).
/// </summary>
public sealed record SubmitQueryCommand(Guid? ConversationId, string Message, string UserId, PlatformRole Role)
    : IRequest<Result<SubmitQueryResult>>;
