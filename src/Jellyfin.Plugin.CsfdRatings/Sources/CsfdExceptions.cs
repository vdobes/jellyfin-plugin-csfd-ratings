// SPDX-License-Identifier: GPL-3.0-or-later

namespace Jellyfin.Plugin.CsfdRatings.Sources;

/// <summary>
/// The upstream signalled rate limiting or bot protection. The caller must stop the whole run,
/// keep existing values and try again on the next scheduled pass.
/// </summary>
public class CsfdThrottledException : Exception
{
    public CsfdThrottledException(string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// The weekly request budget is spent. Like throttling, this ends the run cleanly;
/// unlike throttling it is self-inflicted and resets on a known date.
/// </summary>
public class CsfdBudgetExhaustedException : Exception
{
    public CsfdBudgetExhaustedException(string message, DateTimeOffset resetsAt)
        : base(message)
    {
        ResetsAt = resetsAt;
    }

    public DateTimeOffset ResetsAt { get; }
}

/// <summary>
/// The sidecar rejected our credentials. A configuration problem, so it aborts the run
/// immediately instead of burning retries on something that cannot fix itself.
/// </summary>
public class CsfdAuthException : Exception
{
    public CsfdAuthException(string message)
        : base(message)
    {
    }
}

/// <summary>A recoverable failure: timeout, 5xx, malformed payload.</summary>
public class CsfdTransientException : Exception
{
    public CsfdTransientException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
