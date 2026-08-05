// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Data.Enums;
using Jellyfin.Plugin.CsfdRatings.Caching;
using Jellyfin.Plugin.CsfdRatings.Matching;
using Jellyfin.Plugin.CsfdRatings.Models;
using Jellyfin.Plugin.CsfdRatings.Sources;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Services;

/// <summary>
/// Orchestrates lookups: decides what is stale, calls the source, updates the cache
/// and writes the rating. Movies only, by design.
/// </summary>
public sealed class CsfdSyncService : IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly ICsfdSource _source;
    private readonly CsfdMatcher _matcher;
    private readonly RatingCache _cache;
    private readonly RatingWriter _writer;
    private readonly RequestBudget _budget;
    private readonly ILogger<CsfdSyncService> _logger;
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    public CsfdSyncService(
        ILibraryManager libraryManager,
        ICsfdSource source,
        CsfdMatcher matcher,
        RatingCache cache,
        RatingWriter writer,
        RequestBudget budget,
        ILogger<CsfdSyncService> logger)
    {
        _libraryManager = libraryManager;
        _source = source;
        _matcher = matcher;
        _cache = cache;
        _writer = writer;
        _budget = budget;
        _logger = logger;
    }

    public RatingCache Cache => _cache;

    public RequestBudget Budget => _budget;

    public ICsfdSource Source => _source;

    /// <summary>Timestamp and result of the last completed run, for the admin page.</summary>
    public SyncSummary? LastRun { get; private set; }

    public DateTimeOffset? LastRunUtc { get; private set; }

    /// <summary>Every movie the plugin is allowed to touch.</summary>
    public IReadOnlyList<BaseItem> GetMovies()
    {
        var libraryIds = Plugin.Config.LibraryIds ?? [];

        if (libraryIds.Length == 0)
        {
            return _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                Recursive = true,
                IsVirtualItem = false
            });
        }

        var result = new List<BaseItem>();
        foreach (var raw in libraryIds)
        {
            if (!Guid.TryParse(raw, out var parentId))
            {
                continue;
            }

            result.AddRange(_libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                Recursive = true,
                IsVirtualItem = false,
                ParentId = parentId
            }));
        }

        return result.DistinctBy(i => i.Id).ToList();
    }

    /// <summary>
    /// Decides whether an item needs a network call right now.
    /// Resolved entries expire after RefreshTtlDays - the whole point of running our own plugin.
    /// </summary>
    public bool NeedsLookup(BaseItem item, CsfdCacheEntry? entry, DateTimeOffset now)
    {
        if (entry is null)
        {
            return true;
        }

        var config = Plugin.Config;
        var signatureChanged = !string.Equals(entry.Fingerprint, ItemSignature.For(item), StringComparison.Ordinal);

        return entry.Status switch
        {
            CsfdStatus.Resolved or CsfdStatus.ResolvedNoRating =>
                signatureChanged
                || entry.FetchedUtc is null
                || entry.FetchedUtc.Value.AddDays(Math.Max(1, config.RefreshTtlDays)) <= now,

            // A human has to pick. Only a metadata change reopens it automatically.
            CsfdStatus.NeedsReview => signatureChanged,

            CsfdStatus.NotFound =>
                signatureChanged
                || entry.AttemptedUtc is null
                || entry.AttemptedUtc.Value.AddDays(Math.Max(1, config.NotFoundRetryDays)) <= now,

            CsfdStatus.ErrorTransient => entry.RetryAfterUtc is null || entry.RetryAfterUtc <= now,

            // Needs a manual reset.
            CsfdStatus.ErrorPermanent => false,

            _ => true
        };
    }

    /// <summary>
    /// Looks the item up, updates the cache and applies the rating.
    /// Throttling and budget exhaustion propagate so the caller can abort the whole run.
    /// </summary>
    public async Task<CsfdCacheEntry> ProcessAsync(BaseItem item, CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = _cache.Get(item.Id);
            if (!NeedsLookup(item, existing, DateTimeOffset.UtcNow))
            {
                return existing!;
            }

            return await ProcessCoreAsync(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<CsfdCacheEntry> ProcessCoreAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = _cache.Get(item.Id) ?? new CsfdCacheEntry { ItemId = item.Id.ToString("N") };

        entry.LibraryTitle = item.Name;
        entry.LibraryYear = item.ProductionYear;
        entry.Fingerprint = ItemSignature.For(item);
        entry.AttemptedUtc = now;
        entry.AttemptCount++;

        try
        {
            var result = await _matcher.ResolveAsync(item, cancellationToken).ConfigureAwait(false);

            entry.Status = result.Status;
            entry.Method = result.Method;
            entry.QueryUsed = result.QueryUsed;
            entry.Candidates = result.Candidates;
            entry.LastError = result.Error;

            if (result.Detail is not null)
            {
                entry.CsfdId = result.Detail.CsfdId;
                entry.RatingPercent = result.Detail.RatingPercent;
                entry.RatingCount = result.Detail.RatingCount;
                entry.MatchedTitle = result.Detail.Title;
                entry.MatchedYear = result.Detail.Year;
                entry.Url = result.Detail.Url;
                entry.FetchedUtc = now;
                entry.RetryAfterUtc = null;
                entry.AttemptCount = 0;
            }
            else if (result.Status == CsfdStatus.ErrorTransient)
            {
                entry.RetryAfterUtc = now.AddHours(BackoffHours(entry.AttemptCount));
                if (entry.AttemptCount >= 5)
                {
                    entry.Status = CsfdStatus.ErrorPermanent;
                }
            }

            LogOutcome(item, entry);

            if (entry.Status == CsfdStatus.Resolved && _writer.Apply(item, entry))
            {
                await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (CsfdThrottledException)
        {
            // Says nothing about this title, so do not hold it against the item.
            entry.AttemptCount = Math.Max(0, entry.AttemptCount - 1);
            _cache.Upsert(entry);
            throw;
        }
        catch (CsfdBudgetExhaustedException)
        {
            entry.AttemptCount = Math.Max(0, entry.AttemptCount - 1);
            _cache.Upsert(entry);
            throw;
        }
        catch (CsfdAuthException)
        {
            // Nothing to do with this title either.
            entry.AttemptCount = Math.Max(0, entry.AttemptCount - 1);
            _cache.Upsert(entry);
            throw;
        }
        catch (CsfdTransientException ex)
        {
            entry.Status = entry.AttemptCount >= 5 ? CsfdStatus.ErrorPermanent : CsfdStatus.ErrorTransient;
            entry.LastError = ex.Message;
            entry.RetryAfterUtc = now.AddHours(BackoffHours(entry.AttemptCount));
            _logger.LogWarning("[ČSFD] {Title}: {Error}", item.Name, ex.Message);
        }

        _cache.Upsert(entry);
        return entry;
    }

    /// <summary>
    /// Full pass over the library. Stops cleanly on throttling or an exhausted budget
    /// and keeps every existing value.
    /// </summary>
    public async Task<SyncSummary> RunAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<SyncSummary> RunCoreAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var summary = new SyncSummary();

        if (!Plugin.Config.Enabled)
        {
            _logger.LogInformation("[ČSFD] Plugin disabled, nothing to do");
            return summary;
        }

        await _cache.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _budget.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var movies = GetMovies();
        var due = movies.Where(m => NeedsLookup(m, _cache.Get(m.Id), now)).ToList();

        var cap = Plugin.Config.MaxItemsPerRun;
        if (cap > 0 && due.Count > cap)
        {
            _logger.LogInformation("[ČSFD] {Total} items due, processing {Cap} this run", due.Count, cap);
            due = due.Take(cap).ToList();
        }

        summary.Total = due.Count;
        _logger.LogInformation(
            "[ČSFD] {Due} of {All} movies need a lookup, {Remaining} requests left in the weekly budget",
            due.Count,
            movies.Count,
            _budget.Limit == 0 ? -1 : _budget.Remaining);

        if (due.Count == 0)
        {
            // Applying a fresh cache entry is local and must remain possible after a dry run
            // even when there is no reason to spend another network request.
            await ApplyFreshCachedRatingsAsync(movies, now, cancellationToken).ConfigureAwait(false);
            return await FinishRunAsync(summary, progress, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (!await _source.PingAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogError(
                    "[ČSFD] Sidecar at {Url} is not reachable, aborting before touching anything",
                    Plugin.Config.ApiBaseUrl);
                summary.Aborted = true;
                summary.AbortReason = "Sidecar unreachable";
                return await FinishRunAsync(summary, progress, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (CsfdAuthException ex)
        {
            _logger.LogError("[ČSFD] {Message}", ex.Message);
            summary.Aborted = true;
            summary.AbortReason = ex.Message;
            return await FinishRunAsync(summary, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (CsfdBudgetExhaustedException ex)
        {
            _logger.LogWarning("[ČSFD] Weekly request budget spent, resuming after {ResetsAt:u}", ex.ResetsAt);
            summary.Aborted = true;
            summary.AbortReason = $"Weekly budget spent, resets {ex.ResetsAt:u}";
            return await FinishRunAsync(summary, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (CsfdThrottledException ex)
        {
            _logger.LogWarning("[ČSFD] Upstream is throttling ({Message}), stopping this run", ex.Message);
            summary.Aborted = true;
            summary.AbortReason = "Throttled by upstream";
            return await FinishRunAsync(summary, progress, cancellationToken).ConfigureAwait(false);
        }

        await ApplyFreshCachedRatingsAsync(movies, now, cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < due.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var entry = await ProcessCoreAsync(due[i], cancellationToken).ConfigureAwait(false);
                summary.Count(entry.Status);
            }
            catch (CsfdThrottledException ex)
            {
                _logger.LogWarning(
                    "[ČSFD] Upstream is throttling ({Message}). Stopping this run, existing ratings are untouched.",
                    ex.Message);
                summary.Aborted = true;
                summary.AbortReason = "Throttled by upstream";
                break;
            }
            catch (CsfdBudgetExhaustedException ex)
            {
                _logger.LogWarning(
                    "[ČSFD] Weekly request budget spent, resuming after {ResetsAt:u}. "
                    + "Raise the limit in settings if this is too tight.",
                    ex.ResetsAt);
                summary.Aborted = true;
                summary.AbortReason = $"Weekly budget spent, resets {ex.ResetsAt:u}";
                break;
            }
            catch (CsfdAuthException ex)
            {
                _logger.LogError("[ČSFD] {Message}", ex.Message);
                summary.Aborted = true;
                summary.AbortReason = ex.Message;
                break;
            }

            progress?.Report((i + 1) * 100.0 / due.Count);

            // Persist periodically so a restart mid-run does not lose everything.
            if ((i + 1) % 25 == 0)
            {
                await _cache.SaveAsync(cancellationToken).ConfigureAwait(false);
                await _budget.SaveAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return await FinishRunAsync(summary, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Puts every stashed CommunityRating back. Used by the restore task.</summary>
    public async Task<int> RestoreAllAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RestoreAllCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<int> RestoreAllCoreAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var movies = GetMovies();
        var restored = 0;

        for (var i = 0; i < movies.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _writer.RestoreOriginalAsync(movies[i], cancellationToken).ConfigureAwait(false))
            {
                restored++;
            }

            progress?.Report((i + 1) * 100.0 / movies.Count);
        }

        _logger.LogInformation("[ČSFD] Restored original CommunityRating on {Count} items", restored);
        return restored;
    }

    private async Task ApplyFreshCachedRatingsAsync(
        IReadOnlyList<BaseItem> movies,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var movie in movies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = _cache.Get(movie.Id);
            if (entry is null || entry.Status != CsfdStatus.Resolved || NeedsLookup(movie, entry, now))
            {
                continue;
            }

            if (_writer.Apply(movie, entry))
            {
                await movie.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<SyncSummary> FinishRunAsync(
        SyncSummary summary,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await _cache.SaveAsync(cancellationToken).ConfigureAwait(false);
        await _budget.SaveAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(100);

        LastRun = summary;
        LastRunUtc = DateTimeOffset.UtcNow;

        _logger.LogInformation("[ČSFD] Run finished: {Summary}", summary);
        return summary;
    }

    /// <summary>Counts per status, for the admin page.</summary>
    public IReadOnlyDictionary<string, int> GetStatusCounts()
    {
        var counts = _cache.GetAll()
            .GroupBy(e => e.Status)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        foreach (var status in Enum.GetNames<CsfdStatus>())
        {
            counts.TryAdd(status, 0);
        }

        return counts;
    }

    private static double BackoffHours(int attempt) =>
        Math.Min(24, Math.Pow(2, Math.Min(attempt, 4)));

    private void LogOutcome(BaseItem item, CsfdCacheEntry entry)
    {
        switch (entry.Status)
        {
            case CsfdStatus.Resolved:
                _logger.LogInformation(
                    "[ČSFD] {Title} ({Year}) -> {Matched} ({MatchedYear}) {Percent}% from {Votes} votes via {Method}",
                    item.Name,
                    item.ProductionYear,
                    entry.MatchedTitle,
                    entry.MatchedYear,
                    entry.RatingPercent,
                    entry.RatingCount,
                    entry.Method);
                break;

            case CsfdStatus.NeedsReview:
                _logger.LogInformation(
                    "[ČSFD] {Title} ({Year}) ambiguous, {Count} candidates, left for review",
                    item.Name,
                    item.ProductionYear,
                    entry.Candidates.Count);
                break;

            case CsfdStatus.NotFound:
                _logger.LogInformation("[ČSFD] {Title} ({Year}) not found", item.Name, item.ProductionYear);
                break;

            case CsfdStatus.ResolvedNoRating:
                _logger.LogInformation("[ČSFD] {Title} matched but has no rating yet", item.Name);
                break;
        }
    }

    public void Dispose() => _syncGate.Dispose();
}

public sealed class SyncSummary
{
    public int Total { get; set; }

    public int Resolved { get; set; }

    public int NeedsReview { get; set; }

    public int NotFound { get; set; }

    public int NoRating { get; set; }

    public int Errors { get; set; }

    public bool Aborted { get; set; }

    public string? AbortReason { get; set; }

    public void Count(CsfdStatus status)
    {
        switch (status)
        {
            case CsfdStatus.Resolved: Resolved++; break;
            case CsfdStatus.ResolvedNoRating: NoRating++; break;
            case CsfdStatus.NeedsReview: NeedsReview++; break;
            case CsfdStatus.NotFound: NotFound++; break;
            case CsfdStatus.ErrorTransient:
            case CsfdStatus.ErrorPermanent: Errors++; break;
        }
    }

    public override string ToString() =>
        $"total={Total} resolved={Resolved} review={NeedsReview} notfound={NotFound} " +
        $"norating={NoRating} errors={Errors} aborted={Aborted}{(AbortReason is null ? string.Empty : $" ({AbortReason})")}";
}
