// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Matching;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.CsfdRatings.Providers;

/// <summary>
/// Registers "Csfd" as a first-class external id.
///
/// This is what makes manual matching bearable: the field shows up in the item's metadata
/// editor, so fixing a NeedsReview movie is a matter of pasting the ČSFD id there. The next
/// run sees the stored id and skips searching entirely.
/// </summary>
public sealed class CsfdExternalId : IExternalId
{
    public string ProviderName => "ČSFD";

    public string Key => CsfdMatcher.ProviderKey;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    public bool Supports(IHasProviderIds item) => item is Movie;
}

/// <summary>Turns the stored id into a clickable link on the item page.</summary>
public sealed class CsfdExternalUrlProvider : IExternalUrlProvider
{
    public string Name => "ČSFD";

    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is Movie
            && item.ProviderIds.TryGetValue(CsfdMatcher.ProviderKey, out var id)
            && !string.IsNullOrWhiteSpace(id))
        {
            yield return $"https://www.csfd.cz/film/{id}/prehled/";
        }
    }
}
