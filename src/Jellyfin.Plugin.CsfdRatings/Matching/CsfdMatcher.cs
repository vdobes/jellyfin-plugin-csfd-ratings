// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Data.Enums;
using Jellyfin.Plugin.CsfdRatings.Configuration;
using Jellyfin.Plugin.CsfdRatings.Models;
using Jellyfin.Plugin.CsfdRatings.Sources;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Matching;

/// <summary>
/// Turns a Jellyfin movie into a ČSFD title.
///
/// The decision rules live in <see cref="CandidateSelector"/> so they can be tested without
/// a Jellyfin host. This class only gathers inputs, calls the source and interprets the result.
/// </summary>
public sealed class CsfdMatcher
{
    public const string ProviderKey = "Csfd";

    private readonly ICsfdSource _source;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<CsfdMatcher> _logger;

    public CsfdMatcher(ICsfdSource source, ILibraryManager libraryManager, ILogger<CsfdMatcher> logger)
    {
        _source = source;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public async Task<CsfdResolveResult> ResolveAsync(BaseItem item, CancellationToken cancellationToken)
    {
        // 1. Manual override from the config page wins over everything, including a stored id.
        var overrides = ManualMatchMap.Parse(Plugin.Config.ManualMatches);
        if (overrides.TryGetValue(item.Id, out var overrideId))
        {
            var manual = await FetchAsync(overrideId, MatchMethod.Manual, $"manual:{overrideId}", cancellationToken)
                .ConfigureAwait(false);
            if (manual.Status != CsfdStatus.ErrorTransient)
            {
                return manual;
            }

            _logger.LogWarning(
                "[ČSFD] Manual override {CsfdId} for {Title} did not resolve", overrideId, item.Name);
        }

        // 2. An id confirmed earlier, or typed into the metadata editor. No searching needed.
        if (item.ProviderIds.TryGetValue(ProviderKey, out var storedId) && !string.IsNullOrWhiteSpace(storedId))
        {
            var stored = await FetchAsync(
                storedId, MatchMethod.StoredProviderId, $"id:{storedId}", cancellationToken).ConfigureAwait(false);
            if (stored.Status != CsfdStatus.ErrorTransient)
            {
                return stored;
            }

            _logger.LogWarning(
                "[ČSFD] Stored id {CsfdId} for {Title} no longer resolves, falling back to search",
                storedId,
                item.Name);
        }

        // 3. Search: original title first, then the localised one.
        var queries = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.OriginalTitle))
        {
            queries.Add(item.OriginalTitle);
        }

        if (!string.IsNullOrWhiteSpace(item.Name)
            && !string.Equals(item.Name, item.OriginalTitle, StringComparison.OrdinalIgnoreCase))
        {
            queries.Add(item.Name);
        }

        if (queries.Count == 0)
        {
            return new CsfdResolveResult { Status = CsfdStatus.NotFound, Error = "Item has no usable title" };
        }

        var directors = GetDirectors(item);
        var pool = new Dictionary<string, CsfdCandidate>(StringComparer.OrdinalIgnoreCase);
        var usedQueries = new List<string>();
        SelectionOutcome outcome = SelectionOutcome.None;

        foreach (var query in queries)
        {
            usedQueries.Add(query);

            foreach (var candidate in await _source.SearchAsync(query, cancellationToken).ConfigureAwait(false))
            {
                pool.TryAdd(candidate.CsfdId, candidate);
            }

            outcome = Select(item, directors, pool.Values.ToList());
            if (outcome.IsMatch)
            {
                // An exact hit on the original title makes the second call pointless.
                break;
            }
        }

        var queryUsed = string.Join(" | ", usedQueries);

        if (outcome is { IsMatch: true, Winner: not null })
        {
            return await FetchAsync(outcome.Winner.CsfdId, outcome.Method, queryUsed, cancellationToken)
                .ConfigureAwait(false);
        }

        if (pool.Count == 0)
        {
            return new CsfdResolveResult { Status = CsfdStatus.NotFound, QueryUsed = queryUsed };
        }

        // 4. Candidates exist but none is unambiguous. A human decides; we never guess.
        return new CsfdResolveResult
        {
            Status = CsfdStatus.NeedsReview,
            Candidates = pool.Values.OrderBy(c => c.Year ?? int.MaxValue).Take(10).ToList(),
            QueryUsed = queryUsed
        };
    }

    private static SelectionOutcome Select(
        BaseItem item,
        IReadOnlyList<string> directors,
        IReadOnlyList<CsfdCandidate> candidates) =>
        CandidateSelector.Select(
            item.Name,
            item.OriginalTitle,
            item.ProductionYear,
            directors,
            candidates,
            Plugin.Config.AllowYearToleranceWithDirector);

    private async Task<CsfdResolveResult> FetchAsync(
        string csfdId,
        MatchMethod method,
        string queryUsed,
        CancellationToken cancellationToken)
    {
        var detail = await _source.GetDetailAsync(csfdId, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return new CsfdResolveResult
            {
                Status = CsfdStatus.ErrorTransient,
                Error = $"Detail for {csfdId} could not be fetched",
                QueryUsed = queryUsed
            };
        }

        return new CsfdResolveResult
        {
            Status = detail.RatingPercent is > 0 ? CsfdStatus.Resolved : CsfdStatus.ResolvedNoRating,
            Method = method,
            Detail = detail,
            QueryUsed = queryUsed
        };
    }

    private IReadOnlyList<string> GetDirectors(BaseItem item)
    {
        try
        {
            return _libraryManager.GetPeople(item)
                .Where(p => p.Type == PersonKind.Director)
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ČSFD] Could not read people for {Title}", item.Name);
            return [];
        }
    }
}
