// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Models;

namespace Jellyfin.Plugin.CsfdRatings.Matching;

/// <summary>What the selector decided and why.</summary>
public sealed record SelectionOutcome(MatchMethod Method, CsfdCandidate? Winner)
{
    public bool IsMatch => Winner is not null;

    public static readonly SelectionOutcome None = new(MatchMethod.None, null);
}

/// <summary>
/// The matching rules, with no Jellyfin types involved so they can be unit tested directly.
///
/// The sidecar exposes no TMDb or IMDb identifier, so title, year and director are the only
/// axes available. There is nothing to cross-check a guess against, which is why every rule
/// here demands a single unambiguous winner and the caller falls through to NeedsReview
/// otherwise. A wrong rating is worse than a missing one.
/// </summary>
public static class CandidateSelector
{
    public static SelectionOutcome Select(
        string? name,
        string? originalTitle,
        int? productionYear,
        IReadOnlyList<string> directors,
        IReadOnlyList<CsfdCandidate> candidates,
        bool allowYearToleranceWithDirector)
    {
        if (candidates.Count == 0 || productionYear is not int year)
        {
            // Without a year there is no second axis, so nothing can be called unambiguous.
            return SelectionOutcome.None;
        }

        var titleMatches = candidates
            .Where(c => TitleNormalizer.Equal(name, c.Title) || TitleNormalizer.Equal(originalTitle, c.Title))
            .ToList();

        var exact = titleMatches.Where(c => c.Year == year).ToList();
        if (exact.Count == 1)
        {
            return new SelectionOutcome(MatchMethod.ExactTitleAndYear, exact[0]);
        }

        if (!allowYearToleranceWithDirector || directors.Count == 0)
        {
            return SelectionOutcome.None;
        }

        var tolerant = titleMatches
            .Where(c => c.Year is int cy
                        && Math.Abs(cy - year) == 1
                        && c.Directors.Any(d => directors.Any(known => TitleNormalizer.SamePerson(d, known))))
            .ToList();

        return tolerant.Count == 1
            ? new SelectionOutcome(MatchMethod.YearToleranceWithDirector, tolerant[0])
            : SelectionOutcome.None;
    }
}
