// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Sources;
using Xunit;

namespace Jellyfin.Plugin.CsfdRatings.Tests;

/// <summary>
/// Fixtures are trimmed copies of real node-csfd-api responses. The quirks they capture are
/// deliberate: year is a string in details but a number in search, creators is sometimes an
/// empty array, and rating 0 means "unrated" rather than "zero".
/// </summary>
public class CsfdJsonParserTests
{
    private const string DetailJson = """
    {
      "id": 2294,
      "title": "Vykoupení z věznice Shawshank",
      "year": "1994",
      "rating": 95,
      "ratingCount": 116378,
      "type": "film",
      "url": "https://www.csfd.cz/film/2294",
      "creators": {
        "directors": [{ "id": 1, "name": "Frank Darabont", "url": "https://www.csfd.cz/tvurce/1" }],
        "actors": [{ "id": 2, "name": "Tim Robbins" }]
      }
    }
    """;

    private const string SearchJson = """
    {
      "movies": [
        {
          "id": 2294,
          "title": "Vykoupení z věznice Shawshank",
          "year": 1994,
          "url": "https://www.csfd.cz/film/2294",
          "type": "film",
          "creators": { "directors": [{ "id": 1, "name": "Frank Darabont" }] }
        },
        {
          "id": 42424,
          "title": "Vykoupení",
          "year": 1930,
          "url": "https://www.csfd.cz/film/42424",
          "type": "film",
          "creators": []
        }
      ],
      "tvSeries": [
        { "id": 706181, "title": "Vězeňský doktor", "year": 2019, "type": "series" }
      ],
      "users": [{ "id": 912, "user": "BART!" }]
    }
    """;

    [Fact]
    public void ParsesDetail()
    {
        var detail = CsfdJsonParser.ParseDetail(DetailJson, "fallback");

        Assert.NotNull(detail);
        Assert.Equal("2294", detail!.CsfdId);
        Assert.Equal("Vykoupení z věznice Shawshank", detail.Title);
        Assert.Equal(1994, detail.Year);
        Assert.Equal(95, detail.RatingPercent);
        Assert.Equal(116378, detail.RatingCount);
        Assert.Equal(["Frank Darabont"], detail.Directors);
    }

    [Fact]
    public void ParsesSearchAndIgnoresSeriesAndUsers()
    {
        var candidates = CsfdJsonParser.ParseSearch(SearchJson);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("2294", candidates[0].CsfdId);
        Assert.Equal(1994, candidates[0].Year);
        Assert.Equal(["Frank Darabont"], candidates[0].Directors);
    }

    [Fact]
    public void EmptyCreatorsArrayYieldsNoDirectors()
    {
        // The upstream sends `"creators": []` when it has nothing. Must not throw.
        var candidates = CsfdJsonParser.ParseSearch(SearchJson);
        Assert.Empty(candidates[1].Directors);
    }

    [Fact]
    public void UnratedTitleHasNoPercent()
    {
        var detail = CsfdJsonParser.ParseDetail(
            """{ "id": 1, "title": "Nehodnocený", "year": 2025, "rating": 0 }""", "1");

        Assert.NotNull(detail);
        Assert.Null(detail!.RatingPercent);
    }

    [Theory]
    [InlineData("""{ "id": 1, "title": "X", "rating": 101 }""")]
    [InlineData("""{ "id": 1, "title": "X", "rating": -5 }""")]
    [InlineData("""{ "id": 1, "title": "X", "rating": "nesmysl" }""")]
    [InlineData("""{ "id": 1, "title": "X" }""")]
    public void OutOfRangeOrMissingRatingIsNull(string json) =>
        Assert.Null(CsfdJsonParser.ParseDetail(json, "1")!.RatingPercent);

    [Theory]
    [InlineData("""{ "id": 1, "title": "X", "year": 1869 }""")]
    [InlineData("""{ "id": 1, "title": "X", "year": 2300 }""")]
    [InlineData("""{ "id": 1, "title": "X", "year": "brzy" }""")]
    public void ImplausibleYearIsNull(string json) =>
        Assert.Null(CsfdJsonParser.ParseDetail(json, "1")!.Year);

    [Fact]
    public void YearAcceptsBothStringAndNumber()
    {
        Assert.Equal(1994, CsfdJsonParser.ParseDetail("""{"id":1,"title":"X","year":"1994"}""", "1")!.Year);
        Assert.Equal(1994, CsfdJsonParser.ParseDetail("""{"id":1,"title":"X","year":1994}""", "1")!.Year);
    }

    [Fact]
    public void DetailWithoutTitleIsRejected() =>
        Assert.Null(CsfdJsonParser.ParseDetail("""{ "id": 1, "rating": 90 }""", "1"));

    [Fact]
    public void FallbackIdIsUsedWhenPayloadHasNone() =>
        Assert.Equal("777", CsfdJsonParser.ParseDetail("""{ "title": "X" }""", "777")!.CsfdId);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ broken")]
    [InlineData("[]")]
    [InlineData("null")]
    public void InvalidSearchPayloadIsTransientWhileInvalidDetailIsRejected(string json)
    {
        // A broken detail becomes a retryable null in the matcher. Search must distinguish a
        // broken response from a legitimate empty movies array to avoid caching false NotFound.
        Assert.Null(CsfdJsonParser.ParseDetail(json, "1"));
        Assert.Throws<CsfdTransientException>(() => CsfdJsonParser.ParseSearch(json));
    }

    [Fact]
    public void SearchWithoutMoviesKeyIsTransient() =>
        Assert.Throws<CsfdTransientException>(() =>
            CsfdJsonParser.ParseSearch("""{ "tvSeries": [], "users": [] }"""));

    [Fact]
    public void EmptyMoviesArrayIsAValidEmptyResult() =>
        Assert.Empty(CsfdJsonParser.ParseSearch("""{ "movies": [] }"""));

    [Fact]
    public void CandidatesMissingIdOrTitleAreSkipped()
    {
        var json = """
        { "movies": [ { "id": 1 }, { "title": "Bez id" }, { "id": 2, "title": "OK", "year": 2000 } ] }
        """;

        var candidates = CsfdJsonParser.ParseSearch(json);
        Assert.Single(candidates);
        Assert.Equal("2", candidates[0].CsfdId);
    }

    [Theory]
    [InlineData(95, 9.5f)]
    [InlineData(100, 10f)]
    [InlineData(73, 7.3f)]
    [InlineData(1, 0.1f)]
    [InlineData(66, 6.6f)]
    public void PercentConvertsToStars(int percent, float expected) =>
        Assert.Equal(expected, CsfdJsonParser.ToStars(percent), 3);
}
