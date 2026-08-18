using System.Text;
using System.Text.Json;
using Kart.AiAssistant.Application.Common.Interfaces;
using Kart.AiAssistant.Application.Common.Models;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Kart.AiAssistant.Infrastructure.ExternalClients;

/// <summary>
/// This service's exactly-one synchronous downstream dependency (ADR-0024) — calls whichever
/// `/internal/v1/...` endpoint <c>QueryPlan.EndpointPath</c> names, with <c>QueryPlan.Parameters</c>
/// as the query string, and passes the raw JSON body back unmodified (FR-003: "the assistant never
/// caches or locally recomputes what Analytics itself returns"). <see cref="QueryPlan.BaselineComparisonPlan"/>
/// is deliberately not followed here — a period-over-period comparison is two independent calls to
/// this same method (application-code orchestration, per that field's own doc comment: "diffed by
/// kart-ai-assistant-service"), not a client-level concern.
/// </summary>
public sealed class AnalyticsServiceClient : IAnalyticsClient
{
    private readonly HttpClient _httpClient;

    public AnalyticsServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AnalyticsQueryOutcome> ExecuteAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        var requestUri = BuildRequestUri(plan);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(requestUri, cancellationToken);
        }
        catch (Exception exception) when (ResiliencePolicies.IsCircuitBreakerOrTimeout(exception))
        {
            return Failure(exception is BrokenCircuitException
                ? "kart-analytics-service is temporarily unavailable (circuit open)."
                : "kart-analytics-service timed out.");
        }
        catch (HttpRequestException exception)
        {
            return Failure($"kart-analytics-service call failed: {exception.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Failure($"kart-analytics-service returned {(int)response.StatusCode} {response.StatusCode}.");
            }

            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseSuccess(rawBody);
        }
    }

    /// <summary>Every dashboard/funnel response shares the same envelope root fields
    /// (`generatedAt`/`isProvisional`/`reconciledThrough` — §13.2's response contract,
    /// kart-analytics-service's own `DashboardEnvelope`), serialized camelCase by ASP.NET Core's
    /// default JSON options — parsed here so the raw body can still be handed to the Application
    /// layer unmodified for its own per-metric extraction.</summary>
    private static AnalyticsQueryOutcome ParseSuccess(string rawBody)
    {
        var isProvisional = false;
        DateOnly? reconciledThrough = null;

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("isProvisional", out var provisionalElement) && provisionalElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    isProvisional = provisionalElement.GetBoolean();
                }

                if (root.TryGetProperty("reconciledThrough", out var reconciledElement) &&
                    reconciledElement.ValueKind == JsonValueKind.String &&
                    DateOnly.TryParse(reconciledElement.GetString(), out var parsedDate))
                {
                    reconciledThrough = parsedDate;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed body from a dependency this service doesn't own the shape of — surfaced as
            // a successful call with the raw (unparsed) body still passed through; the Application
            // layer's own consumers will fail their own parsing, which is the correct failure point
            // (this client's job is transport, not schema validation of Analytics' own payload).
        }

        return new AnalyticsQueryOutcome(IsSuccess: true, RawBodyJson: rawBody, IsProvisional: isProvisional, ReconciledThrough: reconciledThrough, FailureReason: null);
    }

    private static AnalyticsQueryOutcome Failure(string reason) =>
        new(IsSuccess: false, RawBodyJson: null, IsProvisional: false, ReconciledThrough: null, FailureReason: reason);

    private static string BuildRequestUri(QueryPlan plan)
    {
        if (plan.Parameters.Count == 0)
        {
            return plan.EndpointPath;
        }

        var queryBuilder = new StringBuilder();
        foreach (var (key, value) in plan.Parameters)
        {
            queryBuilder.Append(queryBuilder.Length == 0 ? '?' : '&');
            queryBuilder.Append(Uri.EscapeDataString(key));
            queryBuilder.Append('=');
            queryBuilder.Append(Uri.EscapeDataString(value));
        }

        return $"{plan.EndpointPath}{queryBuilder}";
    }
}
