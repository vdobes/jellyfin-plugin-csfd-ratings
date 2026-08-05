// SPDX-License-Identifier: GPL-3.0-or-later

using System.Net;
using Jellyfin.Plugin.CsfdRatings.Models;
using Jellyfin.Plugin.CsfdRatings.Services;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Sources;

/// <summary>
/// Talks to a node-csfd-api sidecar over plain HTTP/JSON.
///
/// The sidecar owns everything unpleasant: the Anubis proof-of-work challenge and the HTML
/// parsing. When ČSFD changes its markup we bump an image tag instead of chasing regexes.
/// Parsing of the JSON lives in <see cref="CsfdJsonParser"/> so it can be tested offline.
/// </summary>
public sealed class NodeCsfdApiSource : ICsfdSource
{
    /// <summary>Vykoupení z věznice Shawshank. Stable record, used as a reachability probe.</summary>
    private const string ProbeId = "2294";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CsfdRateLimiter _rateLimiter;
    private readonly RequestBudget _budget;
    private readonly ILogger<NodeCsfdApiSource> _logger;

    private bool _warnedAboutPublicUrl;

    public NodeCsfdApiSource(
        IHttpClientFactory httpClientFactory,
        CsfdRateLimiter rateLimiter,
        RequestBudget budget,
        ILogger<NodeCsfdApiSource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _rateLimiter = rateLimiter;
        _budget = budget;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CsfdCandidate>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var json = await GetAsync(
            $"{BaseUrl()}/search/{Uri.EscapeDataString(query.Trim())}", cancellationToken).ConfigureAwait(false);

        if (json is null)
        {
            throw new CsfdTransientException("ČSFD search returned no payload");
        }

        return CsfdJsonParser.ParseSearch(json);
    }

    public async Task<CsfdDetail?> GetDetailAsync(string csfdId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(csfdId))
        {
            return null;
        }

        var id = csfdId.Trim();
        var json = await GetAsync(
            $"{BaseUrl()}/movie/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);

        return json is null ? null : CsfdJsonParser.ParseDetail(json, id);
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetDetailAsync(ProbeId, cancellationToken).ConfigureAwait(false) is not null;
        }
        catch (CsfdBudgetExhaustedException)
        {
            // Not a connectivity problem, so do not report it as one.
            throw;
        }
        catch (CsfdAuthException)
        {
            // Reachable but refusing us. Surface the real reason instead of "unreachable".
            throw;
        }
        catch (CsfdThrottledException)
        {
            // Let the orchestrator stop the run with the correct reason.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ČSFD] Sidecar ping failed for {BaseUrl}", BaseUrl());
            return false;
        }
    }

    private static string BaseUrl() => Plugin.Config.ApiBaseUrl.TrimEnd('/');

    private async Task<string?> GetAsync(string url, CancellationToken cancellationToken)
    {
        WarnIfSidecarLooksPublic();

        if (!await _budget.TryConsumeAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new CsfdBudgetExhaustedException(
                $"Weekly request budget of {_budget.Limit} is spent", _budget.WindowResetsAt);
        }

        return await _rateLimiter.ExecuteAsync<string?>(async ct =>
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);

            // Do not touch client.Timeout: the factory hands out clients whose timeout may
            // already be locked in. A linked token gives us a per-call deadline instead.
            var timeout = TimeSpan.FromSeconds(Math.Clamp(Plugin.Config.RequestTimeoutSeconds, 5, 120));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(timeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // node-csfd-api turns on authentication when its API_KEY env var is set and then
            // expects the value here. Set per request rather than on the pooled client.
            var apiKey = Plugin.Config.ApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());
            }

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new CsfdTransientException($"Timeout after {timeout.TotalSeconds:0}s calling {url}");
            }
            catch (HttpRequestException ex)
            {
                throw new CsfdTransientException($"Network error calling {url}", ex);
            }

            using (response)
            {
                // A wrong or missing key is a configuration mistake, not a transient fault.
                // Retrying it five times with backoff would only obscure the cause.
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new CsfdAuthException(
                        string.IsNullOrWhiteSpace(Plugin.Config.ApiKey)
                            ? "Sidecar vyžaduje API klíč, ale v nastavení pluginu žádný není. "
                              + "Vyplň ho, nebo odeber API_KEY ze služby csfd-api."
                            : "Sidecar odmítl API klíč z nastavení pluginu. "
                              + "Zkontroluj, že odpovídá hodnotě API_KEY u služby csfd-api.");
                }

                if (response.StatusCode is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.Forbidden
                    or HttpStatusCode.ServiceUnavailable)
                {
                    throw new CsfdThrottledException(
                        $"Upstream returned {(int)response.StatusCode} for {url}",
                        response.Headers.RetryAfter?.Delta);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new CsfdTransientException($"Unexpected {(int)response.StatusCode} from {url}");
                }

                var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(payload) ? null : payload;
            }
        },
        cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// node-csfd-api may be configured with an API key, but it has no rate limiting of its own.
    /// Pointed at a public hostname without that key it becomes an open scraping proxy
    /// for csfd.cz, so warn once rather than let it happen silently.
    /// </summary>
    private void WarnIfSidecarLooksPublic()
    {
        if (_warnedAboutPublicUrl)
        {
            return;
        }

        _warnedAboutPublicUrl = true;

        if (!Uri.TryCreate(BaseUrl(), UriKind.Absolute, out var uri))
        {
            _logger.LogError("[ČSFD] Sidecar address '{Url}' is not a valid URL", Plugin.Config.ApiBaseUrl);
            return;
        }

        if (UrlSafety.IsPrivateHost(uri.Host))
        {
            return;
        }

        _logger.LogWarning(
            "[ČSFD] Sidecar host '{Host}' does not look like a private address. If it is reachable "
            + "from the internet without API_KEY, anyone can scrape ČSFD through your IP. Keep it "
            + "on the internal Docker network or protect it with authentication.",
            uri.Host);
    }
}
