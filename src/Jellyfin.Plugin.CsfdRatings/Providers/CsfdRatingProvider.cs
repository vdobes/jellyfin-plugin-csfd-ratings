// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Caching;
using Jellyfin.Plugin.CsfdRatings.Models;
using Jellyfin.Plugin.CsfdRatings.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Providers;

/// <summary>
/// Re-applies the cached ČSFD rating after every metadata refresh.
///
/// Custom metadata providers run as a post-processing pass once the remote providers are
/// finished, so whatever TMDb just wrote into CommunityRating gets replaced here.
/// IHasOrder only sorts this provider against other custom providers; it is the provider
/// *kind* that puts it after TMDb.
///
/// No network I/O here. A library refresh has to stay fast; fetching is the scheduled
/// task's job.
/// </summary>
public sealed class CsfdRatingProvider : ICustomMetadataProvider<Movie>, IHasOrder
{
    private readonly RatingCache _cache;
    private readonly RatingWriter _writer;
    private readonly ILogger<CsfdRatingProvider> _logger;

    public CsfdRatingProvider(RatingCache cache, RatingWriter writer, ILogger<CsfdRatingProvider> logger)
    {
        _cache = cache;
        _writer = writer;
        _logger = logger;
    }

    public string Name => "ČSFD Ratings";

    /// <summary>High value so any other custom provider that touches ratings runs first.</summary>
    public int Order => 1000;

    public async Task<ItemUpdateType> FetchAsync(
        Movie item,
        MetadataRefreshOptions options,
        CancellationToken cancellationToken)
    {
        if (!Plugin.Config.Enabled)
        {
            return ItemUpdateType.None;
        }

        await _cache.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var entry = _cache.Get(item.Id);
        if (entry is null)
        {
            _logger.LogDebug("[ČSFD] No cache entry for {Title} yet, scheduled task will pick it up", item.Name);
            return ItemUpdateType.None;
        }

        if (entry.Status != CsfdStatus.Resolved)
        {
            return ItemUpdateType.None;
        }

        return _writer.Apply(item, entry) ? ItemUpdateType.MetadataEdit : ItemUpdateType.None;
    }
}
