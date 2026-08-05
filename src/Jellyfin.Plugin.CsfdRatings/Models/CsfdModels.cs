// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.CsfdRatings.Models;

/// <summary>
/// Why a cache entry looks the way it does. Drives retry behaviour.
/// </summary>
public enum CsfdStatus
{
    /// <summary>Never attempted.</summary>
    Unknown = 0,

    /// <summary>Matched and a rating is available.</summary>
    Resolved = 1,

    /// <summary>Matched but ČSFD has no rating for the title yet.</summary>
    ResolvedNoRating = 2,

    /// <summary>Candidates existed but none was unambiguous. Never auto-applied.</summary>
    NeedsReview = 3,

    /// <summary>Search returned nothing usable.</summary>
    NotFound = 4,

    /// <summary>Temporary failure. Retried after RetryAfterUtc.</summary>
    ErrorTransient = 5,

    /// <summary>Gave up. Only a manual reset retries this.</summary>
    ErrorPermanent = 6
}

/// <summary>How the match was established. Written to the log and the review report.</summary>
public enum MatchMethod
{
    None = 0,
    StoredProviderId = 1,
    ExactTitleAndYear = 2,
    YearToleranceWithDirector = 3,
    Manual = 4
}

/// <summary>A single candidate returned by ČSFD search.</summary>
public sealed class CsfdCandidate
{
    public string CsfdId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public string? Url { get; set; }

    public string? Type { get; set; }

    public IReadOnlyList<string> Directors { get; set; } = [];
}

/// <summary>Detail of one ČSFD title, as returned by the sidecar.</summary>
public sealed class CsfdDetail
{
    public string CsfdId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    /// <summary>Rating in percent, 0-100. Null when unrated.</summary>
    public int? RatingPercent { get; set; }

    public int? RatingCount { get; set; }

    public string? Url { get; set; }

    public IReadOnlyList<string> Directors { get; set; } = [];
}

/// <summary>Outcome of one resolve attempt.</summary>
public sealed class CsfdResolveResult
{
    public CsfdStatus Status { get; set; } = CsfdStatus.Unknown;

    public MatchMethod Method { get; set; } = MatchMethod.None;

    public CsfdDetail? Detail { get; set; }

    public IReadOnlyList<CsfdCandidate> Candidates { get; set; } = [];

    public string? Error { get; set; }

    public string? QueryUsed { get; set; }
}

/// <summary>Persisted per-item state.</summary>
public sealed class CsfdCacheEntry
{
    public string ItemId { get; set; } = string.Empty;

    public CsfdStatus Status { get; set; } = CsfdStatus.Unknown;

    public MatchMethod Method { get; set; } = MatchMethod.None;

    public string? CsfdId { get; set; }

    public int? RatingPercent { get; set; }

    public int? RatingCount { get; set; }

    public string? MatchedTitle { get; set; }

    public int? MatchedYear { get; set; }

    public string? Url { get; set; }

    /// <summary>Hash of the Jellyfin fields used for matching. A change re-opens NotFound entries.</summary>
    public string? Fingerprint { get; set; }

    public string? QueryUsed { get; set; }

    public DateTimeOffset? FetchedUtc { get; set; }

    public DateTimeOffset? AttemptedUtc { get; set; }

    public DateTimeOffset? RetryAfterUtc { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    /// <summary>Ambiguous candidates kept for the admin review report.</summary>
    public IReadOnlyList<CsfdCandidate> Candidates { get; set; } = [];

    /// <summary>Library title at the time of the attempt. Only for the report.</summary>
    public string? LibraryTitle { get; set; }

    public int? LibraryYear { get; set; }

    [JsonIgnore]
    public bool HasRating => Status == CsfdStatus.Resolved && RatingPercent is > 0;
}
