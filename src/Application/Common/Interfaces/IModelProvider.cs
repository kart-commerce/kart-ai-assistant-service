using Kart.AiAssistant.Domain.ConversationSessions;

namespace Kart.AiAssistant.Application.Common.Interfaces;

/// <summary>
/// The platform's model-gateway abstraction (PLATFORM_BLUEPRINT.md §8.1), narrowed to the two
/// call shapes this service needs (source spec §11): a structured-output "plan" call and a
/// grounded "explain" call. No Application code ever depends on a concrete provider — swapping
/// the mock implementation registered today for a real Anthropic/OpenAI adapter is a one-line DI
/// registration change in Infrastructure's `DependencyInjection.cs`, never a change here.
/// </summary>
public interface IModelProvider
{
    Task<PlanCallResult> PlanAsync(PlanCallRequest request, CancellationToken cancellationToken);

    Task<ExplainCallResult> ExplainAsync(ExplainCallRequest request, CancellationToken cancellationToken);
}

/// <summary>Also the source of "requires clarification" — the planner is the only place that
/// decides ambiguity, per §15.1.</summary>
public sealed record PlanCallRequest(
    string Message,
    ResolvedIntent? PriorIntent,
    bool PriorSuperlativeDefaultAlreadyDisclosed);

public sealed record PlanCallResult(
    ResolvedIntentPatch? Patch,
    ClarificationRequest? Clarification,
    long LatencyMs,
    TokenUsage TokenUsage);

public sealed record ClarificationRequest(string Question, IReadOnlyList<string> Options);

public sealed record ExplainCallRequest(ResolvedIntent Intent, string RawResultJson);

public sealed record ExplainCallResult(string AnswerText, long LatencyMs, TokenUsage TokenUsage);

public sealed record TokenUsage(int PromptTokens, int CompletionTokens);
