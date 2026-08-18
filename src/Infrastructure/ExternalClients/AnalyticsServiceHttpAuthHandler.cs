using System.Net.Http.Headers;

namespace Kart.AiAssistant.Infrastructure.ExternalClients;

/// <summary>
/// Attaches the Bearer token this service authenticates its one downstream call (Analytics) with —
/// mirrors kart-admin-service's own <c>ServicePrincipalAuthHandler</c>. Reuses
/// <see cref="IdentityClientCredentialsTokenProvider"/> so the token is fetched once and cached/
/// refreshed regardless of how many concurrent Analytics calls are in flight.
/// </summary>
public sealed class AnalyticsServiceHttpAuthHandler : DelegatingHandler
{
    private readonly IdentityClientCredentialsTokenProvider _tokenProvider;

    public AnalyticsServiceHttpAuthHandler(IdentityClientCredentialsTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
