// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Matching;
using Jellyfin.Plugin.CsfdRatings.Models;
using Xunit;

namespace Jellyfin.Plugin.CsfdRatings.Tests;

/// <summary>
/// These tests encode the safety property the whole plugin rests on:
/// when in doubt, do not pick anything.
/// </summary>
public class CandidateSelectorTests
{
    private static CsfdCandidate Candidate(string id, string title, int? year, params string[] directors) =>
        new() { CsfdId = id, Title = title, Year = year, Directors = directors };

    private static SelectionOutcome Run(
        string? name,
        string? original,
        int? year,
        IReadOnlyList<CsfdCandidate> candidates,
        string[]? directors = null,
        bool tolerance = true) =>
        CandidateSelector.Select(name, original, year, directors ?? [], candidates, tolerance);

    [Fact]
    public void ExactTitleAndYearWins()
    {
        var result = Run(
            "Vykoupení z věznice Shawshank", "The Shawshank Redemption", 1994,
            [Candidate("2294", "Vykoupení z věznice Shawshank", 1994)]);

        Assert.True(result.IsMatch);
        Assert.Equal(MatchMethod.ExactTitleAndYear, result.Method);
        Assert.Equal("2294", result.Winner!.CsfdId);
    }

    [Fact]
    public void MatchesOnOriginalTitleToo()
    {
        var result = Run(
            "Vykoupení z věznice Shawshank", "The Shawshank Redemption", 1994,
            [Candidate("2294", "The Shawshank Redemption", 1994)]);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void DiacriticsDoNotBreakTheMatch()
    {
        var result = Run(
            "Vykoupeni z veznice Shawshank", null, 1994,
            [Candidate("2294", "Vykoupení z věznice Shawshank", 1994)]);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void WrongYearIsNotAMatch()
    {
        var result = Run(
            "Vykoupení z věznice Shawshank", null, 1994,
            [Candidate("2294", "Vykoupení z věznice Shawshank", 1998)],
            tolerance: false);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void TwoIdenticalTitlesInTheSameYearAreAmbiguous()
    {
        // Remakes and re-releases. Picking either one would be a coin flip.
        var result = Run(
            "Solaris", null, 2002,
            [
                Candidate("1", "Solaris", 2002),
                Candidate("2", "Solaris", 2002)
            ]);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void MissingProductionYearNeverMatches()
    {
        // Without a year there is no second axis, so even a single perfect title is not enough.
        var result = Run(
            "Vykoupení z věznice Shawshank", null, null,
            [Candidate("2294", "Vykoupení z věznice Shawshank", 1994)]);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void EmptyCandidateListYieldsNoMatch() =>
        Assert.False(Run("Cokoli", null, 2000, []).IsMatch);

    [Fact]
    public void YearToleranceRequiresADirectorMatch()
    {
        var candidates = new[] { Candidate("10", "Vratné lahve", 2007, "Jan Svěrák") };

        // Right director -> accepted.
        var withDirector = Run("Vratné lahve", null, 2006, candidates, ["Jan Svěrák"]);
        Assert.True(withDirector.IsMatch);
        Assert.Equal(MatchMethod.YearToleranceWithDirector, withDirector.Method);

        // No director information -> refused.
        Assert.False(Run("Vratné lahve", null, 2006, candidates).IsMatch);

        // Different director -> refused.
        Assert.False(Run("Vratné lahve", null, 2006, candidates, ["Zdeněk Svěrák"]).IsMatch);
    }

    [Fact]
    public void YearToleranceIsOnlyOneYear()
    {
        var candidates = new[] { Candidate("10", "Film", 2010, "Reziser") };
        Assert.False(Run("Film", null, 2008, candidates, ["Reziser"]).IsMatch);
    }

    [Fact]
    public void ToleranceCanBeSwitchedOff()
    {
        var candidates = new[] { Candidate("10", "Film", 2010, "Reziser") };
        Assert.False(Run("Film", null, 2009, candidates, ["Reziser"], tolerance: false).IsMatch);
    }

    [Fact]
    public void TwoToleranceCandidatesAreStillAmbiguous()
    {
        var candidates = new[]
        {
            Candidate("10", "Film", 2009, "Reziser"),
            Candidate("11", "Film", 2011, "Reziser")
        };

        Assert.False(Run("Film", null, 2010, candidates, ["Reziser"]).IsMatch);
    }

    [Fact]
    public void ExactMatchBeatsAmbiguityElsewhere()
    {
        // One exact hit plus unrelated noise from the same search must still resolve.
        var candidates = new[]
        {
            Candidate("1", "Vykoupení z věznice Shawshank", 1994),
            Candidate("2", "Vykoupení", 1994),
            Candidate("3", "Vykoupení z hříchu", 1994)
        };

        var result = Run("Vykoupení z věznice Shawshank", null, 1994, candidates);
        Assert.True(result.IsMatch);
        Assert.Equal("1", result.Winner!.CsfdId);
    }
}
