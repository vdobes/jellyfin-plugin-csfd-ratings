// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.CsfdRatings.Tasks;

/// <summary>
/// Undo. Puts back the CommunityRating that was there before the plugin first wrote to an item.
/// Run this before uninstalling, otherwise the original TMDb ratings are gone for good.
/// </summary>
public sealed class RestoreOriginalRatingsTask : IScheduledTask
{
    private readonly CsfdSyncService _sync;

    public RestoreOriginalRatingsTask(CsfdSyncService sync)
    {
        _sync = sync;
    }

    public string Name => "Obnovit původní hodnocení";

    public string Key => "CsfdRatingsRestore";

    public string Description =>
        "Vrátí do CommunityRating hodnotu, která tam byla před prvním zápisem pluginu. "
        + "Spusť před odinstalací pluginu.";

    public string Category => "ČSFD Ratings";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _sync.RestoreAllAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    // Never automatic. This is a destructive-ish operation the user has to ask for.
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
