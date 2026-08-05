// SPDX-License-Identifier: GPL-3.0-or-later

namespace Jellyfin.Plugin.CsfdRatings.Configuration;

/// <summary>
/// Parses the free-text override list from the config page.
///
/// One pair per line, "jellyfinItemId = csfdId". Blank lines and lines starting with #
/// are ignored so the user can keep notes next to their entries. Anything malformed is
/// skipped silently rather than throwing: a typo in a settings textarea must never take
/// the plugin down.
/// </summary>
public static class ManualMatchMap
{
    public static IReadOnlyDictionary<Guid, string> Parse(string? raw)
    {
        var result = new Dictionary<Guid, string>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        foreach (var line in raw.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOfAny(['=', ':', ',', ';']);
            if (separator <= 0)
            {
                continue;
            }

            var left = trimmed[..separator].Trim();
            var right = trimmed[(separator + 1)..].Trim();

            if (!Guid.TryParse(left, out var itemId) || right.Length == 0)
            {
                continue;
            }

            // Accept a full ČSFD URL as well as a bare id.
            var csfdId = ExtractId(right);
            if (csfdId is not null)
            {
                result[itemId] = csfdId;
            }
        }

        return result;
    }

    /// <summary>Pulls "2294" out of "2294", "film/2294" or a full csfd.cz URL.</summary>
    public static string? ExtractId(string value)
    {
        var digits = new string(value.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : null;
    }
}
