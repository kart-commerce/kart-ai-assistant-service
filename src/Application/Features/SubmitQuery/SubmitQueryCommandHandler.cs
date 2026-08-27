using System.Diagnostics;
using System.Text.Json;
using Kart.AiAssistant.Application.Common;
using Kart.AiAssistant.Application.Common.Interfaces;
using Kart.AiAssistant.Application.Common.Models;
using Kart.AiAssistant.Application.Common.Registry;
using Kart.AiAssistant.Domain.AuditRecords;
using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;
using Kart.Shared.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Kart.AiAssistant.Application.Features.SubmitQuery;

/// <summary>
/// The full per-turn orchestration (source spec §3.1's pipeline, §11's AI architecture, ddd-model.md's
/// "Cross-Aggregate Interaction"): resolve/lock the conversation session, plan (LLM call #1),
/// merge the follow-up patch (FR-007), validate against <see cref="MetricEndpointRegistry"/>
/// (FR-009), execute the resolved <see cref="QueryPlan"/>(s) against Analytics (FR-003), explain
/// (LLM call #2) and ground (FR-004), assemble the table/visualization (FR-005/FR-006), persist the
/// turn's <see cref="AuditRecord"/> (FR-011) and the session's new state, release the lock, and
/// return exactly one discriminated <see cref="SubmitQueryResult"/>. Every reachable outcome —
/// answer, clarification, or error — writes exactly one <see cref="AuditRecord"/> (ddd-model.md's
/// own invariant), and no unhandled exception escapes as a raw 500 (FR-010's "no leakage of which
/// check failed" posture, applied here to internal faults too) unless the audit write itself is
/// what throws, in which case it is deliberately left to bubble and be logged, never swallowed.
/// </summary>
public sealed class SubmitQueryCommandHandler(
    IConversationSessionRepository sessionRepository,
    IAnalyticsClient analyticsClient,
    IModelProvider modelProvider,
    IAuditRecordRepository auditRecordRepository,
    IClock clock,
    ILogger<SubmitQueryCommandHandler> logger)
    : IRequestHandler<SubmitQueryCommand, Result<SubmitQueryResult>>
{
    private static readonly TimeSpan SessionLockTimeout = TimeSpan.FromSeconds(10);

    // UX-1 (requirement-spec §8) is an explicitly open human decision on the exact session TTL;
    // 20 minutes mirrors §14.3's own recommended default ("the same boundary as the user's own
    // kart-admin-web session idle timeout") rather than inventing an unrelated number.
    private static readonly TimeSpan SessionIdleTtl = TimeSpan.FromMinutes(20);

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<Result<SubmitQueryResult>> Handle(SubmitQueryCommand request, CancellationToken cancellationToken)
    {
        var turnId = Guid.NewGuid();
        var now = clock.UtcNow;
        var isNewConversation = request.ConversationId is null;
        var conversationId = request.ConversationId ?? Guid.NewGuid();
        IAsyncDisposable? sessionLock = null;

        try
        {
            ConversationSession session;
            if (isNewConversation)
            {
                session = ConversationSession.Start(conversationId, request.UserId, now);
            }
            else
            {
                var existing = await sessionRepository.GetAsync(conversationId, cancellationToken);
                if (existing is null)
                {
                    // edge-cases.md "Conversation Session Expiry Mid-Conversation" — a supplied
                    // conversationId that no longer resolves is treated as expired, never as if it
                    // referenced a still-live (but empty) session.
                    await PersistAuditAsync(
                        turnId, request, request.ConversationId!.Value, resolvedIntentJson: null,
                        clarificationIssued: false, clarificationQuestion: null, toolsInvoked: null, dataSource: null,
                        queryParametersJson: null, executionTimeMs: null, llmPlanLatencyMs: null, llmExplainLatencyMs: null,
                        llmTokenUsageJson: null, resultSize: null, isProvisional: null, reconciledThrough: null,
                        groundingCheckResult: GroundingCheckResult.NotApplicable, errorsJson: SerializeErrors(["expired-session"]),
                        finalResponseSummary: "expired-session", now, cancellationToken);

                    return Result.Success(SubmitQueryResult.ForError(null, new AssistantError(
                        ResponseErrorType.ExpiredSession,
                        "This conversation has expired or no longer exists. Please restate your full question.")));
                }

                sessionLock = await sessionRepository.TryAcquireLockAsync(conversationId, SessionLockTimeout, cancellationToken);
                if (sessionLock is null)
                {
                    // edge-cases.md "Concurrent Requests in the Same Conversation Session".
                    await PersistAuditAsync(
                        turnId, request, conversationId, resolvedIntentJson: null, clarificationIssued: false,
                        clarificationQuestion: null, toolsInvoked: null, dataSource: null, queryParametersJson: null,
                        executionTimeMs: null, llmPlanLatencyMs: null, llmExplainLatencyMs: null, llmTokenUsageJson: null,
                        resultSize: null, isProvisional: null, reconciledThrough: null,
                        groundingCheckResult: GroundingCheckResult.NotApplicable,
                        errorsJson: SerializeErrors(["unavailable:session-lock"]), finalResponseSummary: "session-busy", now, cancellationToken);

                    return Result.Success(SubmitQueryResult.ForError(conversationId, new AssistantError(
                        ResponseErrorType.Unavailable,
                        "A previous message in this conversation is still being processed. Please wait and try again.")));
                }

                session = existing;
            }

            var planRequest = new PlanCallRequest(request.Message, session.LastResolvedIntent, session.UnqualifiedSuperlativeDefaultAlreadyDisclosed);
            PlanCallResult planResult;
            try
            {
                planResult = await modelProvider.PlanAsync(planRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Stage {Stage}: plan call failed for conversation {ConversationId}", "PlanCallFailed", conversationId);

                await PersistAuditAsync(
                    turnId, request, conversationId, resolvedIntentJson: null, clarificationIssued: false,
                    clarificationQuestion: null, toolsInvoked: null, dataSource: null, queryParametersJson: null,
                    executionTimeMs: null, llmPlanLatencyMs: null, llmExplainLatencyMs: null, llmTokenUsageJson: null,
                    resultSize: null, isProvisional: null, reconciledThrough: null, groundingCheckResult: GroundingCheckResult.NotApplicable,
                    errorsJson: SerializeErrors(["unavailable:plan"]), finalResponseSummary: "plan-call-unavailable", now, cancellationToken);

                return Result.Success(SubmitQueryResult.ForError(conversationId, new AssistantError(
                    ResponseErrorType.Unavailable, "The assistant is temporarily unavailable. Please try again shortly.")));
            }

            if (planResult.Clarification is { } clarification)
            {
                // FR-008/§15 — clarification turns never reach Analytics or the explain call.
                await PersistAuditAsync(
                    turnId, request, conversationId, resolvedIntentJson: null, clarificationIssued: true,
                    clarificationQuestion: clarification.Question, toolsInvoked: null, dataSource: null, queryParametersJson: null,
                    executionTimeMs: null, llmPlanLatencyMs: planResult.LatencyMs, llmExplainLatencyMs: null,
                    llmTokenUsageJson: SerializeTokenUsage(planResult.TokenUsage, null), resultSize: null,
                    isProvisional: null, reconciledThrough: null, groundingCheckResult: GroundingCheckResult.NotApplicable,
                    errorsJson: null, finalResponseSummary: clarification.Question, now, cancellationToken);

                return Result.Success(SubmitQueryResult.ForClarification(
                    conversationId, new AssistantClarification(clarification.Question, clarification.Options)));
            }

            var patch = planResult.Patch ?? new ResolvedIntentPatch();
            var basis = patch.IsContextReset || session.LastResolvedIntent is null ? ResolvedIntent.Empty : session.LastResolvedIntent;
            var merged = basis.MergeFollowUp(patch);

            // §15.2's soft-default: the planner already had the chance to hard-stop into a
            // Clarification above and chose not to, so an unqualified ranking/superlative resolves
            // to Revenue here, deterministically, per the closed §6 metric registry — never the
            // LLM's own choice.
            var superlativeDefaultApplied = false;
            if (merged.Metric is null && merged.Ranking is not null)
            {
                merged = merged with { Metric = MetricType.Revenue };
                superlativeDefaultApplied = true;
            }

            if (!MetricEndpointRegistry.TryResolve(merged.Metric, merged.Entity, merged.Ranking is not null, out _))
            {
                var unsupportedMessage = BuildUnsupportedMessage(merged);
                await PersistAuditAsync(
                    turnId, request, conversationId, resolvedIntentJson: SerializeIntent(merged), clarificationIssued: false,
                    clarificationQuestion: null, toolsInvoked: null, dataSource: null, queryParametersJson: null,
                    executionTimeMs: null, llmPlanLatencyMs: planResult.LatencyMs, llmExplainLatencyMs: null,
                    llmTokenUsageJson: SerializeTokenUsage(planResult.TokenUsage, null), resultSize: null,
                    isProvisional: null, reconciledThrough: null, groundingCheckResult: GroundingCheckResult.NotApplicable,
                    errorsJson: SerializeErrors(["unsupported"]), finalResponseSummary: unsupportedMessage, now, cancellationToken);

                return Result.Success(SubmitQueryResult.ForError(
                    conversationId, new AssistantError(ResponseErrorType.Unsupported, unsupportedMessage)));
            }

            var planBuildResult = QueryPlanBuilder.Build(merged);
            if (planBuildResult.IsFailure)
            {
                var unsupportedMessage = BuildUnsupportedMessage(merged);
                await PersistAuditAsync(
                    turnId, request, conversationId, resolvedIntentJson: SerializeIntent(merged), clarificationIssued: false,
                    clarificationQuestion: null, toolsInvoked: null, dataSource: null, queryParametersJson: null,
                    executionTimeMs: null, llmPlanLatencyMs: planResult.LatencyMs, llmExplainLatencyMs: null,
                    llmTokenUsageJson: SerializeTokenUsage(planResult.TokenUsage, null), resultSize: null,
                    isProvisional: null, reconciledThrough: null, groundingCheckResult: GroundingCheckResult.NotApplicable,
                    errorsJson: SerializeErrors([$"unsupported:{planBuildResult.Error.Code}"]), finalResponseSummary: unsupportedMessage, now, cancellationToken);

                return Result.Success(SubmitQueryResult.ForError(
                    conversationId, new AssistantError(ResponseErrorType.Unsupported, unsupportedMessage)));
            }

            var planOutcome = planBuildResult.Value;
            var stopwatch = Stopwatch.StartNew();
            var primaryOutcome = await analyticsClient.ExecuteAsync(planOutcome.Plan, cancellationToken);
            AnalyticsQueryOutcome? baselineOutcome = null;
            if (planOutcome.Plan.BaselineComparisonPlan is { } baselinePlan)
            {
                baselineOutcome = await analyticsClient.ExecuteAsync(baselinePlan, cancellationToken);
            }

            stopwatch.Stop();

            if (!primaryOutcome.IsSuccess || (baselineOutcome is not null && !baselineOutcome.IsSuccess))
            {
                await PersistAuditAsync(
                    turnId, request, conversationId, resolvedIntentJson: SerializeIntent(merged), clarificationIssued: false,
                    clarificationQuestion: null, toolsInvoked: SerializeToolsInvoked(planOutcome, baselineOutcome is not null),
                    dataSource: "kart-analytics-service", queryParametersJson: SerializeParameters(planOutcome),
                    executionTimeMs: stopwatch.ElapsedMilliseconds, llmPlanLatencyMs: planResult.LatencyMs, llmExplainLatencyMs: null,
                    llmTokenUsageJson: SerializeTokenUsage(planResult.TokenUsage, null), resultSize: null,
                    isProvisional: null, reconciledThrough: null, groundingCheckResult: GroundingCheckResult.NotApplicable,
                    errorsJson: SerializeErrors(["unavailable:analytics"]), finalResponseSummary: "analytics-unavailable", now, cancellationToken);

                return Result.Success(SubmitQueryResult.ForError(conversationId, new AssistantError(
                    ResponseErrorType.Unavailable, "The underlying data source is temporarily unavailable. Please retry shortly.")));
            }

            var explainRawJson = BuildExplainRawJson(primaryOutcome, baselineOutcome);

            string answerText;
            var groundingResult = GroundingCheckResult.NotApplicable;
            long? explainLatencyMs = null;
            TokenUsage? explainTokenUsage = null;
            try
            {
                var explainResult = await modelProvider.ExplainAsync(new ExplainCallRequest(merged, explainRawJson), cancellationToken);
                explainLatencyMs = explainResult.LatencyMs;
                explainTokenUsage = explainResult.TokenUsage;

                var grounding = GroundingValidator.Validate(explainResult.AnswerText, explainRawJson);
                if (grounding.Passed)
                {
                    answerText = explainResult.AnswerText;
                    groundingResult = GroundingCheckResult.Pass;
                }
                else
                {
                    answerText = GroundingValidator.BuildTemplateFallback(merged, explainRawJson);
                    groundingResult = GroundingCheckResult.Fail;
                    logger.LogWarning(
                        "Stage {Stage}: grounding check failed for turn {TurnId} — ungrounded tokens: {Tokens}",
                        "GroundingCheckFailed", turnId, string.Join(", ", grounding.UngroundedTokens));
                }
            }
            catch (Exception ex)
            {
                // §19: an explain-call failure after data was already fetched successfully falls
                // back to the same template answer FR-004's grounding-failure path already builds —
                // the table/chart never depends on the prose step succeeding.
                logger.LogWarning(ex, "Stage {Stage}: explain call failed for turn {TurnId}", "ExplainCallFailed", turnId);
                answerText = GroundingValidator.BuildTemplateFallback(merged, explainRawJson);
            }

            if (superlativeDefaultApplied && !session.UnqualifiedSuperlativeDefaultAlreadyDisclosed)
            {
                answerText += " Showing results by revenue — the default for an unqualified \"top selling\" request. " +
                              "Ask for \"by units sold\" or \"by orders\" for a different ranking.";
            }

            if (planOutcome.WasRankingLimitClamped)
            {
                answerText += $" Note: the requested limit of {planOutcome.OriginalRequestedLimit} exceeds what this query " +
                              $"supports — showing the top {merged.Ranking!.Limit} instead.";
            }

            var table = ResultTableAssembler.Assemble(merged, primaryOutcome.RawBodyJson);
            var shape = BuildResultShape(merged, planOutcome, table);
            var explicitVisualizationRequested = patch.VisualizationHint is not null;
            var visualizationType = VisualizationSelector.Select(merged, shape, explicitVisualizationRequested) ?? VisualizationType.TableOnly;
            var visualization = new AssistantVisualization(
                visualizationType,
                BuildVisualizationTitle(merged),
                BuildAxisLabel(merged, isX: true, visualizationType),
                BuildAxisLabel(merged, isX: false, visualizationType));

            var metadata = new AssistantResponseMetadata(
                merged,
                $"kart-analytics-service:{planOutcome.Plan.EndpointPath}",
                primaryOutcome.IsProvisional,
                primaryOutcome.ReconciledThrough,
                now);

            var answer = new AssistantAnswer(answerText, table, visualization, metadata);

            session.RecordTurn(
                merged,
                new TurnProvenance(
                    WasFollowUp: !isNewConversation && !patch.IsContextReset,
                    WasContextReset: patch.IsContextReset,
                    UnqualifiedSuperlativeDefaultApplied: superlativeDefaultApplied,
                    ResolvedAt: now),
                now);
            await sessionRepository.SaveAsync(session, SessionIdleTtl, cancellationToken);

            await PersistAuditAsync(
                turnId, request, conversationId, resolvedIntentJson: SerializeIntent(merged), clarificationIssued: false,
                clarificationQuestion: null, toolsInvoked: SerializeToolsInvoked(planOutcome, baselineOutcome is not null),
                dataSource: "kart-analytics-service", queryParametersJson: SerializeParameters(planOutcome),
                executionTimeMs: stopwatch.ElapsedMilliseconds, llmPlanLatencyMs: planResult.LatencyMs, llmExplainLatencyMs: explainLatencyMs,
                llmTokenUsageJson: SerializeTokenUsage(planResult.TokenUsage, explainTokenUsage), resultSize: table.Rows.Count,
                isProvisional: primaryOutcome.IsProvisional, reconciledThrough: primaryOutcome.ReconciledThrough,
                groundingCheckResult: groundingResult, errorsJson: null,
                finalResponseSummary: SerializeFinalSummary(answerText, visualizationType), now, cancellationToken);

            return Result.Success(SubmitQueryResult.ForAnswer(conversationId, answer));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stage {Stage}: unhandled exception processing turn {TurnId}", "SubmitQueryUnhandledException", turnId);

            // FR-010's "no leakage of which specific check failed" posture, applied to internal
            // faults too — the caller sees the same generic Unavailable shape as any other
            // downstream outage, never a raw 500 or an internal exception message. If persisting
            // the audit record itself throws, that exception is deliberately left to bubble (never
            // silently swallowed) rather than caught a second time here.
            await PersistAuditAsync(
                turnId, request, conversationId, resolvedIntentJson: null, clarificationIssued: false,
                clarificationQuestion: null, toolsInvoked: null, dataSource: null, queryParametersJson: null,
                executionTimeMs: null, llmPlanLatencyMs: null, llmExplainLatencyMs: null, llmTokenUsageJson: null,
                resultSize: null, isProvisional: null, reconciledThrough: null, groundingCheckResult: GroundingCheckResult.NotApplicable,
                errorsJson: SerializeErrors(["unavailable:unhandled"]), finalResponseSummary: "unhandled-exception", now, cancellationToken);

            return Result.Success(SubmitQueryResult.ForError(conversationId, new AssistantError(
                ResponseErrorType.Unavailable, "The assistant is temporarily unavailable. Please try again shortly.")));
        }
        finally
        {
            if (sessionLock is not null)
            {
                await sessionLock.DisposeAsync();
            }
        }
    }

    private async Task PersistAuditAsync(
        Guid turnId,
        SubmitQueryCommand request,
        Guid conversationId,
        string? resolvedIntentJson,
        bool clarificationIssued,
        string? clarificationQuestion,
        string? toolsInvoked,
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
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var record = AuditRecord.Create(
            turnId: turnId,
            conversationId: conversationId,
            userId: request.UserId,
            role: request.Role,
            userQuestion: request.Message,
            resolvedIntentJson: resolvedIntentJson,
            clarificationIssued: clarificationIssued,
            clarificationQuestion: clarificationQuestion,
            toolsInvokedJson: toolsInvoked,
            dataSource: dataSource,
            queryParametersJson: queryParametersJson,
            executionTimeMs: executionTimeMs,
            llmPlanLatencyMs: llmPlanLatencyMs,
            llmExplainLatencyMs: llmExplainLatencyMs,
            llmTokenUsageJson: llmTokenUsageJson,
            resultSize: resultSize,
            isProvisional: isProvisional,
            reconciledThrough: reconciledThrough,
            groundingCheckResult: groundingCheckResult,
            errorsJson: errorsJson,
            finalResponseSummary: finalResponseSummary,
            timestamp: timestamp);

        await auditRecordRepository.AddAsync(record, cancellationToken);
    }

    private static string BuildUnsupportedMessage(ResolvedIntent merged)
    {
        var what = merged.Entity is { } entity
            ? entity.ToWire().Replace('_', ' ')
            : merged.Metric?.ToWire().Replace('_', ' ') ?? "this request";

        return $"I can't currently answer questions about {what} — that capability isn't supported today. " +
               "Try asking about products, revenue, orders, fulfillment, inventory, promotions, users, reviews, " +
               "notifications, or admin activity instead.";
    }

    private static ResultShape BuildResultShape(ResolvedIntent merged, QueryPlanBuildOutcome planOutcome, AssistantResultTable table)
    {
        var rowCount = table.Rows.Count;
        var isRanking = merged.Ranking is not null && planOutcome.Registration.IsNewProductPerformanceEndpoint;
        var isLogRows = planOutcome.Plan.EndpointPath.Contains("admin-audit", StringComparison.OrdinalIgnoreCase);
        var isFunnel = merged.Entity == EntityType.Funnel;
        var isComparison = merged.Comparison is not null;
        var isDistribution = merged.Entity == EntityType.Review && merged.Dimensions.Contains(DimensionType.StarRating);
        var isTimeSeries = !isRanking && !isLogRows && merged.Dimensions.Contains(DimensionType.Time) && rowCount >= 2;

        // §13.1's "share-of-total" framing has no structural signal on the fixed ResolvedIntent
        // schema (it is a phrasing judgment, not a field) — left false here deterministically; a
        // donut chart is still reachable via an explicitly-honored visualizationHint.
        const bool isShareOfTotal = false;

        return new ResultShape(rowCount, isRanking, isTimeSeries, isLogRows, isDistribution, isFunnel, isComparison, isShareOfTotal);
    }

    private static string BuildVisualizationTitle(ResolvedIntent merged)
    {
        if (merged.Ranking is { } ranking)
        {
            var direction = ranking.Direction == RankDirection.Desc ? "Top" : "Bottom";
            var entityLabel = merged.Entity?.ToWire() ?? "results";
            var metricLabel = (merged.Metric ?? MetricType.Revenue).ToWire().Replace('_', ' ');
            return $"{direction} {ranking.Limit} {entityLabel}s by {metricLabel}";
        }

        if (merged.Comparison is not null)
        {
            var metricLabel = (merged.Metric?.ToWire() ?? "metric").Replace('_', ' ');
            return $"{metricLabel} — current vs. prior period";
        }

        var label = (merged.Metric?.ToWire() ?? merged.Entity?.ToWire() ?? "Result").Replace('_', ' ');
        return $"{label} overview";
    }

    private static string? BuildAxisLabel(ResolvedIntent merged, bool isX, VisualizationType type)
    {
        if (type is VisualizationType.TableOnly or VisualizationType.SingleStat)
        {
            return null;
        }

        var metricLabel = (merged.Metric ?? MetricType.Revenue).ToWire();
        var entityLabel = merged.Entity?.ToWire() ?? "item";

        if (type == VisualizationType.HorizontalBarChart)
        {
            return isX ? metricLabel : entityLabel;
        }

        if (type == VisualizationType.DonutChart)
        {
            return isX ? entityLabel : metricLabel;
        }

        return isX ? "time" : metricLabel;
    }

    private static string BuildExplainRawJson(AnalyticsQueryOutcome primary, AnalyticsQueryOutcome? baseline)
    {
        if (baseline is null)
        {
            return string.IsNullOrWhiteSpace(primary.RawBodyJson) ? "{}" : primary.RawBodyJson;
        }

        using var currentDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(primary.RawBodyJson) ? "{}" : primary.RawBodyJson);
        using var baselineDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(baseline.RawBodyJson) ? "{}" : baseline.RawBodyJson);

        var currentRevenue = ExtractAggregateNumber(currentDoc.RootElement, "revenue");
        var baselineRevenue = ExtractAggregateNumber(baselineDoc.RootElement, "revenue");
        var currentOrderCount = ExtractAggregateNumber(currentDoc.RootElement, "orderCount");
        var baselineOrderCount = ExtractAggregateNumber(baselineDoc.RootElement, "orderCount");

        // FR-002's period-over-period rule: "diffed by kart-ai-assistant-service" — computed here,
        // never by the LLM, and included in the same payload the grounding check validates the
        // explanation against so a stated "%change" is always a verifiable, pre-computed figure
        // (edge-cases.md's "Grounding-Check Failure... False Negative" fix).
        decimal? pctChange = currentRevenue is not null && baselineRevenue is not null && baselineRevenue != 0
            ? Math.Round((currentRevenue.Value - baselineRevenue.Value) / baselineRevenue.Value * 100, 2)
            : null;

        var combined = new
        {
            current = currentDoc.RootElement,
            baseline = baselineDoc.RootElement,
            revenue = currentRevenue,
            baselineRevenue,
            orderCount = currentOrderCount,
            baselineOrderCount,
            pctChange,
        };

        return JsonSerializer.Serialize(combined);
    }

    private static decimal? ExtractAggregateNumber(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var direct))
                {
                    return direct;
                }

                if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("amount", out var amount) &&
                    amount.ValueKind == JsonValueKind.Number && amount.TryGetDecimal(out var amt))
                {
                    return amt;
                }
            }

            if (element.TryGetProperty("series", out var series) && series.ValueKind == JsonValueKind.Array)
            {
                decimal? sum = null;
                foreach (var bucket in series.EnumerateArray())
                {
                    var bucketValue = ExtractAggregateNumber(bucket, propertyName);
                    if (bucketValue is not null)
                    {
                        sum = (sum ?? 0) + bucketValue.Value;
                    }
                }

                return sum;
            }
        }

        return null;
    }

    private static string SerializeIntent(ResolvedIntent intent) => JsonSerializer.Serialize(intent, AuditJsonOptions);

    private static string SerializeTokenUsage(TokenUsage plan, TokenUsage? explain) =>
        JsonSerializer.Serialize(new { plan, explain }, AuditJsonOptions);

    private static string SerializeErrors(IReadOnlyList<string> categories) => JsonSerializer.Serialize(categories, AuditJsonOptions);

    private static string SerializeToolsInvoked(QueryPlanBuildOutcome outcome, bool includesBaseline)
    {
        var tools = new List<string> { $"GET {outcome.Plan.EndpointPath}" };
        if (includesBaseline)
        {
            tools.Add($"GET {outcome.Plan.EndpointPath} (baseline)");
        }

        return JsonSerializer.Serialize(tools, AuditJsonOptions);
    }

    private static string SerializeParameters(QueryPlanBuildOutcome outcome) =>
        JsonSerializer.Serialize(
            new { current = outcome.Plan.Parameters, baseline = outcome.Plan.BaselineComparisonPlan?.Parameters },
            AuditJsonOptions);

    private static string SerializeFinalSummary(string answerText, VisualizationType visualizationType) =>
        JsonSerializer.Serialize(new { answerText, visualizationType = visualizationType.ToWire() }, AuditJsonOptions);
}
