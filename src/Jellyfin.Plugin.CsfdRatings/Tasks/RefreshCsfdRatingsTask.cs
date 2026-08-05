// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.CsfdRatings.Tasks;

/// <summary>
/// The only task that talks to the network. Weekly by default: ČSFD ratings on established
/// titles move by tenths of a percent per year, so a daily crawl of the whole library would
/// be pure noise and a good way to get noticed.
/// </summary>
public sealed class RefreshCsfdRatingsTask : IScheduledTask
{
    private readonly CsfdSyncService _sync;

    public RefreshCsfdRatingsTask(CsfdSyncService sync)
    {
        _sync = sync;
    }

    public string Name => "Načíst hodnocení z ČSFD";

    public string Key => "CsfdRatingsRefresh";

    public string Description =>
        "Projde filmy, které nemají hodnocení nebo jim vypršelo TTL, a zapíše ČSFD hodnocení do CommunityRating.";

    public string Category => "ČSFD Ratings";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _sync.RunAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.WeeklyTrigger,
            DayOfWeek = System.DayOfWeek.Sunday,
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
        }
    ];
}
