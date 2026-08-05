// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Configuration;
using Jellyfin.Plugin.CsfdRatings.Matching;
using Jellyfin.Plugin.CsfdRatings.Models;
using Jellyfin.Plugin.CsfdRatings.Services;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.CsfdRatings.Tests;

/// <summary>
/// Covers the irreversible half of the plugin: overwriting CommunityRating and putting it back.
/// </summary>
public class RatingWriterTests : IDisposable
{
    private readonly RatingWriter _writer = new(NullLogger<RatingWriter>.Instance);

    private static void UseConfig(Action<PluginConfiguration> configure)
    {
        var config = new PluginConfiguration();
        configure(config);
        Plugin.ConfigOverride = config;
    }

    private static Movie MovieWith(float? communityRating) =>
        new() { Name = "Vykoupení z věznice Shawshank", ProductionYear = 1994, CommunityRating = communityRating };

    private static CsfdCacheEntry Resolved(int percent = 95) =>
        new() { Status = CsfdStatus.Resolved, RatingPercent = percent, CsfdId = "2294" };

    public void Dispose() => Plugin.ConfigOverride = null;

    [Fact]
    public void WritesStarsAndProviderId()
    {
        UseConfig(_ => { });
        var movie = MovieWith(8.2f);

        Assert.True(_writer.Apply(movie, Resolved()));
        Assert.Equal(9.5f, movie.CommunityRating!.Value, 3);
        Assert.Equal("2294", movie.ProviderIds[CsfdMatcher.ProviderKey]);
    }

    [Fact]
    public void StashesTheOriginalRatingOnce()
    {
        UseConfig(_ => { });
        var movie = MovieWith(8.2f);

        _writer.Apply(movie, Resolved());
        Assert.Equal("8.2", movie.ProviderIds[RatingWriter.OriginalRatingKey]);

        // A second pass must not overwrite the stash with our own value.
        _writer.Apply(movie, Resolved(73));
        Assert.Equal("8.2", movie.ProviderIds[RatingWriter.OriginalRatingKey]);
    }

    [Fact]
    public void RemembersThatThereWasNoOriginalRating()
    {
        UseConfig(_ => { });
        var movie = MovieWith(null);

        _writer.Apply(movie, Resolved());
        Assert.True(_writer.RestoreOriginal(movie));
        Assert.Null(movie.CommunityRating);
    }

    [Fact]
    public void DryRunChangesNothingAndReportsNoChange()
    {
        // Reporting a change here would make the caller persist a dry run.
        UseConfig(c => c.DryRun = true);
        var movie = MovieWith(8.2f);

        Assert.False(_writer.Apply(movie, Resolved()));
        Assert.Equal(8.2f, movie.CommunityRating!.Value, 3);
        Assert.False(movie.ProviderIds.ContainsKey(RatingWriter.OriginalRatingKey));
        Assert.False(movie.ProviderIds.ContainsKey(CsfdMatcher.ProviderKey));
    }

    [Fact]
    public void RoundTripRestoresTheExactValue()
    {
        UseConfig(_ => { });
        var movie = MovieWith(8.2f);

        _writer.Apply(movie, Resolved());
        Assert.Equal(9.5f, movie.CommunityRating!.Value, 3);

        Assert.True(_writer.RestoreOriginal(movie));
        Assert.Equal(8.2f, movie.CommunityRating!.Value, 3);
        Assert.False(movie.ProviderIds.ContainsKey(RatingWriter.OriginalRatingKey));
    }

    [Fact]
    public void RestoreIsIdempotent()
    {
        UseConfig(_ => { });
        var movie = MovieWith(8.2f);
        _writer.Apply(movie, Resolved());

        Assert.True(_writer.RestoreOriginal(movie));
        Assert.False(_writer.RestoreOriginal(movie));
    }

    [Fact]
    public void NothingIsStashedWhenPreservationIsOff()
    {
        UseConfig(c => c.PreserveOriginalRating = false);
        var movie = MovieWith(8.2f);

        Assert.True(_writer.Apply(movie, Resolved()));
        Assert.False(movie.ProviderIds.ContainsKey(RatingWriter.OriginalRatingKey));
        Assert.False(_writer.RestoreOriginal(movie));
    }

    [Theory]
    [InlineData(CsfdStatus.NeedsReview)]
    [InlineData(CsfdStatus.NotFound)]
    [InlineData(CsfdStatus.ResolvedNoRating)]
    [InlineData(CsfdStatus.ErrorTransient)]
    public void OnlyResolvedEntriesAreApplied(CsfdStatus status)
    {
        UseConfig(_ => { });
        var movie = MovieWith(8.2f);

        Assert.False(_writer.Apply(movie, new CsfdCacheEntry { Status = status, RatingPercent = 95 }));
        Assert.Equal(8.2f, movie.CommunityRating!.Value, 3);
    }

    [Fact]
    public void ZeroPercentIsNeverApplied()
    {
        // ČSFD reports 0 for unrated titles. Writing 0.0 stars would be a lie.
        UseConfig(_ => { });
        var movie = MovieWith(8.2f);

        Assert.False(_writer.Apply(
            movie, new CsfdCacheEntry { Status = CsfdStatus.Resolved, RatingPercent = 0 }));
        Assert.Equal(8.2f, movie.CommunityRating!.Value, 3);
    }

    [Fact]
    public void ReapplyingTheSameRatingIsNotAChange()
    {
        UseConfig(_ => { });
        var movie = MovieWith(8.2f);

        Assert.True(_writer.Apply(movie, Resolved()));
        Assert.False(_writer.Apply(movie, Resolved()));
    }

    [Fact]
    public void UnreadableStashLeavesTheCurrentValueAlone()
    {
        UseConfig(_ => { });
        var movie = MovieWith(9.5f);
        movie.ProviderIds[RatingWriter.OriginalRatingKey] = "not a number";

        Assert.False(_writer.RestoreOriginal(movie));
        Assert.Equal(9.5f, movie.CommunityRating!.Value, 3);
    }
}
