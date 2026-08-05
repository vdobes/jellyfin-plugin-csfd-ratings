// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.CsfdRatings.Models;

namespace Jellyfin.Plugin.CsfdRatings.Sources;

/// <summary>
/// Reads node-csfd-api payloads field by field instead of deserialising into POCOs.
///
/// The upstream shape drifts between releases: year is a number in search results and a
/// string in details, creators is an object on one entry and an empty array on the next.
/// Optional fields are read defensively, but an invalid root payload is reported as a
/// transient failure. Treating a broken sidecar response as "no match" would cache a false
/// NotFound result for days. Kept separate from the HTTP client so the parsing can be tested
/// against captured payloads.
/// </summary>
public static class CsfdJsonParser
{
    /// <summary>Movie candidates from a /search/{query} payload. Series and users are ignored.</summary>
    public static IReadOnlyList<CsfdCandidate> ParseSearch(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new CsfdTransientException("ČSFD search returned an empty payload");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return ParseSearch(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new CsfdTransientException("ČSFD search returned malformed JSON", ex);
        }
    }

    public static IReadOnlyList<CsfdCandidate> ParseSearch(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("movies", out var movies)
            || movies.ValueKind != JsonValueKind.Array)
        {
            throw new CsfdTransientException("ČSFD search payload does not contain a movies array");
        }

        var results = new List<CsfdCandidate>();
        foreach (var element in movies.EnumerateArray())
        {
            var candidate = ReadCandidate(element);
            if (candidate is not null)
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    /// <summary>Detail from a /movie/{id} payload.</summary>
    public static CsfdDetail? ParseDetail(string json, string fallbackId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return ParseDetail(document.RootElement, fallbackId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static CsfdDetail? ParseDetail(JsonElement root, string fallbackId)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = ReadString(root, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new CsfdDetail
        {
            CsfdId = ReadString(root, "id") ?? fallbackId,
            Title = title,
            Year = ReadYear(root, "year"),
            RatingPercent = ReadPercent(root, "rating"),
            RatingCount = ReadInt(root, "ratingCount"),
            Url = ReadString(root, "url"),
            Directors = ReadDirectors(root)
        };
    }

    private static CsfdCandidate? ReadCandidate(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = ReadString(element, "id");
        var title = ReadString(element, "title");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new CsfdCandidate
        {
            CsfdId = id,
            Title = title,
            Year = ReadYear(element, "year"),
            Url = ReadString(element, "url"),
            Type = ReadString(element, "type"),
            Directors = ReadDirectors(element)
        };
    }

    /// <summary>
    /// creators is an object with a directors array, or an empty array when the upstream had
    /// nothing to report. Both are handled; anything else yields no directors.
    /// </summary>
    public static IReadOnlyList<string> ReadDirectors(JsonElement element)
    {
        if (!element.TryGetProperty("creators", out var creators)
            || creators.ValueKind != JsonValueKind.Object
            || !creators.TryGetProperty("directors", out var directors)
            || directors.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var director in directors.EnumerateArray())
        {
            var name = director.ValueKind switch
            {
                JsonValueKind.Object => ReadString(director, "name"),
                JsonValueKind.String => director.GetString(),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        return names;
    }

    public static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var number) =>
                number.ToString(CultureInfo.InvariantCulture),
            _ => null
        };
    }

    public static int? ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>Year arrives as a number in search results and as a string in details.</summary>
    public static int? ReadYear(JsonElement element, string property)
    {
        var year = ReadInt(element, property);
        return year is >= 1870 and <= 2200 ? year : null;
    }

    /// <summary>Rating is 0-100. Zero means unrated, not "zero stars".</summary>
    public static int? ReadPercent(JsonElement element, string property)
    {
        var percent = ReadInt(element, property);
        return percent is > 0 and <= 100 ? percent : null;
    }

    /// <summary>ČSFD percent to a Jellyfin community rating. 95 % becomes 9.5.</summary>
    public static float ToStars(int percent) =>
        (float)Math.Round(percent / 10.0, 1, MidpointRounding.AwayFromZero);
}
