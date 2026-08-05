// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.CsfdRatings.Matching;

/// <summary>
/// Folds a title down to a comparable form: no diacritics, no punctuation,
/// lower case, single spaces. "Vykoupení z věznice Shawshank" and
/// "vykoupeni z veznice shawshank" must compare equal.
/// </summary>
public static class TitleNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var ch in decomposed)
        {
            // Drop the combining accents left behind by FormD.
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                // Any run of punctuation or whitespace collapses to one space.
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim().Normalize(NormalizationForm.FormC);
    }

    public static bool Equal(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        return a.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);
    }

    /// <summary>Name comparison for directors. Same folding, exact result required.</summary>
    public static bool SamePerson(string? left, string? right) => Equal(left, right);
}
