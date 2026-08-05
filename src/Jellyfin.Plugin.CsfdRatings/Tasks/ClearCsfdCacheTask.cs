// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Tasks;

/// <summary>
/// Empties the lookup cache so the next refresh starts from scratch.
/// Note this does not change any rating on its own - run the refresh task afterwards.
/// </summary>
public sealed class ClearCsfdCacheTask : IScheduledTask
{
    private readonly CsfdSyncService _sync;
    private readonly ILogger<ClearCsfdCacheTask> _logger;

    public ClearCsfdCacheTask(CsfdSyncService sync, ILogger<ClearCsfdCacheTask> logger)
    {
        _sync = sync;
        _logger = logger;
    }

    public string Name => "Vymazat ČSFD cache";

    public string Key => "CsfdRatingsClearCache";

    public string Description =>
        "Smaže uloženou cache párování a hodnocení. Samo o sobě nic nepřepíše - "
        + "je potřeba pak spustit načtení hodnocení.";

    public string Category => "ČSFD Ratings";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _sync.Cache.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        var count = _sync.Cache.GetAll().Count;

        _sync.Cache.Clear();
        await _sync.Cache.SaveAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("[ČSFD] Cleared {Count} cache entries", count);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
