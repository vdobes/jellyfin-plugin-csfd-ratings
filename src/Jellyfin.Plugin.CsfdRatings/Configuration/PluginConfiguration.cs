// SPDX-License-Identifier: GPL-3.0-or-later

using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CsfdRatings.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Master switch. When false the provider and tasks do nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Base URL of the node-csfd-api sidecar. Keep this on an internal Docker network.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "http://csfd-api:3000";

    /// <summary>
    /// Optional API key for the sidecar. node-csfd-api enables authentication when its
    /// API_KEY environment variable is set, and then expects the value in an x-api-key
    /// header. Leave empty when the sidecar runs without a key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Minimum delay between two outbound requests, in milliseconds.</summary>
    public int RequestDelayMs { get; set; } = 2000;

    /// <summary>HTTP timeout for a single sidecar call, in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 20;

    /// <summary>How long a resolved rating stays fresh before it is refreshed.</summary>
    public int RefreshTtlDays { get; set; } = 90;

    /// <summary>How long to wait before retrying an item that could not be matched.</summary>
    public int NotFoundRetryDays { get; set; } = 7;

    /// <summary>
    /// Safety cap on how many items a single scheduled run may process.
    /// Zero means no limit.
    /// </summary>
    public int MaxItemsPerRun { get; set; }

    /// <summary>
    /// Hard ceiling on outbound requests within any rolling seven-day window.
    /// The per-request delay caps the burst rate; this caps the total. Zero disables it.
    /// </summary>
    public int MaxRequestsPerWeek { get; set; } = 2000;

    /// <summary>
    /// Manual overrides, one per line as "jellyfinItemId = csfdId".
    /// Takes precedence over everything, including a stored provider id.
    /// Lines starting with # are treated as notes.
    /// </summary>
    public string ManualMatches { get; set; } = string.Empty;

    /// <summary>
    /// Allow a candidate whose year differs by one, but only when the director matches exactly.
    /// </summary>
    public bool AllowYearToleranceWithDirector { get; set; } = true;

    /// <summary>
    /// Store the pre-existing CommunityRating so it can be restored later.
    /// Turning this off makes the overwrite irreversible.
    /// </summary>
    public bool PreserveOriginalRating { get; set; } = true;

    /// <summary>Queue newly added movies for a lookup shortly after they appear.</summary>
    public bool SyncOnItemAdded { get; set; } = true;

    /// <summary>
    /// Restrict processing to these library (collection folder) ids.
    /// Empty means every movie library.
    /// </summary>
    public string[] LibraryIds { get; set; } = [];

    /// <summary>
    /// When true the plugin only logs what it would do and never writes to the database.
    /// Useful for the first run.
    /// </summary>
    public bool DryRun { get; set; }
}
