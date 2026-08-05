// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Configuration;
using Xunit;

namespace Jellyfin.Plugin.CsfdRatings.Tests;

public class ManualMatchMapTests
{
    private static readonly Guid ItemA = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ItemB = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void ParsesSimplePairs()
    {
        var map = ManualMatchMap.Parse($"{ItemA} = 2294\n{ItemB}=8852");

        Assert.Equal("2294", map[ItemA]);
        Assert.Equal("8852", map[ItemB]);
    }

    [Fact]
    public void AcceptsFullCsfdUrl()
    {
        var map = ManualMatchMap.Parse($"{ItemA} = https://www.csfd.cz/film/2294-vykoupeni/prehled/");
        Assert.Equal("2294", map[ItemA]);
    }

    [Fact]
    public void IgnoresCommentsAndBlankLines()
    {
        var map = ManualMatchMap.Parse($"# poznámka\n\n   \n{ItemA}=2294\n# další");
        Assert.Single(map);
        Assert.Equal("2294", map[ItemA]);
    }

    [Theory]
    [InlineData("naprosto rozbity radek")]
    [InlineData("not-a-guid = 2294")]
    [InlineData("= 2294")]
    [InlineData("11111111-2222-3333-4444-555555555555 = bez cisla")]
    public void MalformedLinesAreSkippedNotThrown(string line)
    {
        // A typo in a settings textarea must never take the plugin down.
        var map = ManualMatchMap.Parse(line);
        Assert.Empty(map);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyInputYieldsEmptyMap(string? raw) => Assert.Empty(ManualMatchMap.Parse(raw));

    [Fact]
    public void AcceptsAlternativeSeparators()
    {
        Assert.Equal("2294", ManualMatchMap.Parse($"{ItemA}: 2294")[ItemA]);
        Assert.Equal("2294", ManualMatchMap.Parse($"{ItemA}, 2294")[ItemA]);
    }

    [Theory]
    [InlineData("2294", "2294")]
    [InlineData("film/2294", "2294")]
    [InlineData("https://www.csfd.cz/film/8852-pulp-fiction/", "8852")]
    [InlineData("bez cisla", null)]
    public void ExtractIdHandlesTheUsualShapes(string input, string? expected) =>
        Assert.Equal(expected, ManualMatchMap.ExtractId(input));
}
