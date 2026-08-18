using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace Kart.AiAssistant.Infrastructure.ExternalClients;

/// <summary>
/// design-decisions.md's "Resilience Pattern for LLM-Provider and Analytics Outages" decision:
/// a short per-call timeout, a single bounded retry, and one independent circuit breaker per
/// call-site — "three independent circuit breakers... never a shared breaker" so an open Analytics
/// breaker never also degrades the model-gateway calls, and vice versa. Mirrors
/// kart-admin-service's own <c>ResiliencePolicies.BuildPolicy</c> exactly; each call to
/// <see cref="BuildPolicy"/> returns a fresh breaker instance, so every typed HttpClient this
/// service registers against this policy gets its own independent breaker state.
/// </summary>
public static class ResiliencePolicies
{
    public static IAsyncPolicy<HttpResponseMessage> BuildPolicy(TimeSpan timeout)
    {
        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(timeout, TimeoutStrategy.Optimistic);

        // Bounded retry (2 attempts, short fixed backoff) on transient faults and 5xx/408.
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(2, attempt => TimeSpan.FromMilliseconds(50 * attempt));

        // After 5 consecutive faults, stop sending requests to this one call-site's peer for 15s
        // and fail fast — a single slow/down dependency must not cascade into this service's other
        // call-sites (design-decisions.md's own blast-radius reasoning).
        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(15));

        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy, timeoutPolicy);
    }

    public static bool IsCircuitBreakerOrTimeout(Exception exception) =>
        exception is BrokenCircuitException or TimeoutRejectedException;
}
