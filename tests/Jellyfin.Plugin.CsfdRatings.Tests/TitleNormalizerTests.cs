// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Matching;
using Xunit;

namespace Jellyfin.Plugin.CsfdRatings.Tests;

public class TitleNormalizerTests
{
    [Theory]
    [InlineData("Vykoupení z věznice Shawshank", "vykoupeni z veznice shawshank")]
    [InlineData("VYKOUPENÍ", "vykoupeni")]
    [InlineData("Přežít!", "prezit")]
    [InlineData("Č.Š.Ž.Ř", "c s z r")]
    [InlineData("  více   mezer  ", "vice mezer")]
    [InlineData("Amélie z Montmartru", "amelie z montmartru")]
    public void StripsDiacriticsAndPunctuation(string input, string expected) =>
        Assert.Equal(expected, TitleNormalizer.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void EmptyishInputYieldsEmptyString(string? input) =>
        Assert.Equal(string.Empty, TitleNormalizer.Normalize(input));

    [Theory]
    [InlineData("The Shawshank Redemption", "the shawshank redemption")]
    [InlineData("Spider-Man: No Way Home", "spider man no way home")]
    [InlineData("WALL·E", "wall e")]
    [InlineData("Ocean's Eleven", "ocean s eleven")]
    public void HandlesLatinTitles(string input, string expected) =>
        Assert.Equal(expected, TitleNormalizer.Normalize(input));

    [Fact]
    public void EqualIgnoresDiacriticsAndCase() =>
        Assert.True(TitleNormalizer.Equal("Vykoupení z věznice Shawshank", "vykoupeni z veznice shawshank"));

    [Fact]
    public void EqualIsFalseForDifferentTitles() =>
        Assert.False(TitleNormalizer.Equal("Vykoupení", "Vykoupení z věznice Shawshank"));

    [Fact]
    public void EmptyNeverEqualsAnything()
    {
        // Guards against a null title silently matching a candidate with no title.
        Assert.False(TitleNormalizer.Equal(null, null));
        Assert.False(TitleNormalizer.Equal("", ""));
        Assert.False(TitleNormalizer.Equal("!!!", "???"));
    }

    [Fact]
    public void DirectorNamesFoldTheSameWay() =>
        Assert.True(TitleNormalizer.SamePerson("Miloš Forman", "Milos Forman"));

    [Fact]
    public void DifferentDirectorsDoNotMatch() =>
        Assert.False(TitleNormalizer.SamePerson("Miloš Forman", "Jan Svěrák"));
}
