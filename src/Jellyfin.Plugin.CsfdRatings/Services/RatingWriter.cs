// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using Jellyfin.Plugin.CsfdRatings.Matching;
using Jellyfin.Plugin.CsfdRatings.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Services;

/// <summary>
/// The only place that touches CommunityRating.
///
/// Overwriting CommunityRating is a permanent mutation of the Jellyfin database - Jellyfin
/// keeps no history and MetadataField has no entry for ratings, so the field cannot be locked.
/// Before the first write the previous value is stashed in ProviderIds so that
/// <see cref="RestoreOriginalAsync"/> can put it back. Turning off PreserveOriginalRating
/// makes the change irreversible; that is the user's choice, not the default.
/// </summary>
public sealed class RatingWriter
{
    /// <summary>ProviderIds key holding the pre-plugin CommunityRating.</summary>
    public const string OriginalRatingKey = "CsfdOriginalCommunityRating";

    /// <summary>Marker written when the item had no rating at all before the plugin touched it.</summary>
    private const string NoOriginalValue = "none";

    private readonly ILogger<RatingWriter> _logger;

    public RatingWriter(ILogger<RatingWriter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Applies the rating to the item in memory. Returns true when something changed
    /// and the caller therefore needs to persist the item.
    /// </summary>
    public bool Apply(BaseItem item, CsfdCacheEntry entry)
    {
        if (entry.Status != CsfdStatus.Resolved || entry.RatingPercent is not > 0)
        {
            return false;
        }

        var target = (float)Math.Round(entry.RatingPercent.Value / 10.0, 1, MidpointRounding.AwayFromZero);

        // Bail out before touching the item at all. Reporting "changed" here would make the
        // caller persist a dry run, which is exactly what the setting is meant to prevent.
        if (Plugin.Config.DryRun)
        {
            _logger.LogInformation(
                "[ČSFD] DRY RUN {Title}: CommunityRating {Old} -> {New} ({Percent}%, csfdId={CsfdId})",
                item.Name,
                item.CommunityRating,
                target,
                entry.RatingPercent,
                entry.CsfdId);
            return false;
        }

        var changed = false;

        if (!string.IsNullOrWhiteSpace(entry.CsfdId)
            && (!item.ProviderIds.TryGetValue(CsfdMatcher.ProviderKey, out var existing)
                || !string.Equals(existing, entry.CsfdId, StringComparison.Ordinal)))
        {
            item.ProviderIds[CsfdMatcher.ProviderKey] = entry.CsfdId;
            changed = true;
        }

        changed |= StashOriginal(item);

        if (item.CommunityRating is null || Math.Abs(item.CommunityRating.Value - target) > 0.001f)
        {
            item.CommunityRating = target;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Records the value that was in CommunityRating before the plugin first wrote to it.
    /// Only ever written once per item.
    /// </summary>
    private bool StashOriginal(BaseItem item)
    {
        if (!Plugin.Config.PreserveOriginalRating)
        {
            return false;
        }

        if (item.ProviderIds.ContainsKey(OriginalRatingKey))
        {
            return false;
        }

        item.ProviderIds[OriginalRatingKey] = item.CommunityRating.HasValue
            ? item.CommunityRating.Value.ToString("R", CultureInfo.InvariantCulture)
            : NoOriginalValue;

        return true;
    }

    /// <summary>
    /// Puts the stashed value back and removes the plugin's bookkeeping keys.
    /// Returns true when the item changed.
    /// </summary>
    public bool RestoreOriginal(BaseItem item)
    {
        if (!item.ProviderIds.TryGetValue(OriginalRatingKey, out var stored))
        {
            return false;
        }

        if (string.Equals(stored, NoOriginalValue, StringComparison.OrdinalIgnoreCase))
        {
            item.CommunityRating = null;
        }
        else if (float.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var original))
        {
            item.CommunityRating = original;
        }
        else
        {
            _logger.LogWarning(
                "[ČSFD] Stored original rating '{Value}' for {Title} is unreadable, leaving current value alone",
                stored,
                item.Name);
            return false;
        }

        item.ProviderIds.Remove(OriginalRatingKey);
        return true;
    }

    public async Task<bool> RestoreOriginalAsync(BaseItem item, CancellationToken cancellationToken)
    {
        if (Plugin.Config.DryRun)
        {
            if (item.ProviderIds.ContainsKey(OriginalRatingKey))
            {
                _logger.LogInformation("[ČSFD] DRY RUN would restore original rating on {Title}", item.Name);
            }

            return false;
        }

        if (!RestoreOriginal(item))
        {
            return false;
        }

        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
