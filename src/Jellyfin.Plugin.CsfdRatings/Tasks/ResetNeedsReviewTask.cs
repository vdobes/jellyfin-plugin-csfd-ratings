// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Models;
using Jellyfin.Plugin.CsfdRatings.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Tasks;

/// <summary>
/// Drops the cache entries the plugin refused to decide on, so the next refresh tries again.
///
/// Useful after adding manual overrides, after correcting titles in the library, or once
/// ČSFD has added the missing title. Errors marked permanent are cleared too - they only
/// ever got that way by exhausting retries.
/// </summary>
public sealed class ResetNeedsReviewTask : IScheduledTask
{
    private readonly CsfdSyncService _sync;
    private readonly ILogger<ResetNeedsReviewTask> _logger;

    public ResetNeedsReviewTask(CsfdSyncService sync, ILogger<ResetNeedsReviewTask> logger)
    {
        _sync = sync;
        _logger = logger;
    }

    public string Name => "Znovu zkusit nespárované filmy";

    public string Key => "CsfdRatingsResetReview";

    public string Category => "ČSFD Ratings";

    public string Description =>
        "Zahodí záznamy ve stavu k ručnímu párování, nenalezeno a trvalá chyba, "
        + "aby je další načtení zkusilo znovu. Hodnocení už zapsaná zůstanou.";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _sync.Cache.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var stale = _sync.Cache.GetByStatus(
            CsfdStatus.NeedsReview,
            CsfdStatus.NotFound,
            CsfdStatus.ErrorPermanent,
            CsfdStatus.ErrorTransient);

        foreach (var entry in stale)
        {
            if (Guid.TryParseExact(entry.ItemId, "N", out var id))
            {
                _sync.Cache.Remove(id);
            }
        }

        await _sync.Cache.SaveAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("[ČSFD] Reset {Count} unresolved entries", stale.Count);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
