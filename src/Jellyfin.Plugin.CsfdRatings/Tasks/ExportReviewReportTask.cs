// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Text;
using Jellyfin.Plugin.CsfdRatings.Models;
using Jellyfin.Plugin.CsfdRatings.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Tasks;

/// <summary>
/// Writes everything the plugin refused to guess into a CSV in the plugin data folder.
/// This is how you find out what needs a manual ČSFD id.
/// </summary>
public sealed class ExportReviewReportTask : IScheduledTask
{
    private readonly CsfdSyncService _sync;
    private readonly ILogger<ExportReviewReportTask> _logger;

    public ExportReviewReportTask(CsfdSyncService sync, ILogger<ExportReviewReportTask> logger)
    {
        _sync = sync;
        _logger = logger;
    }

    public string Name => "Vypsat filmy k ručnímu párování";

    public string Key => "CsfdRatingsReviewReport";

    public string Description =>
        "Uloží CSV se všemi filmy, které se nepodařilo jednoznačně spárovat, "
        + "včetně kandidátů a jejich ČSFD ID.";

    public string Category => "ČSFD Ratings";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _sync.Cache.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var entries = _sync.Cache
            .GetByStatus(CsfdStatus.NeedsReview, CsfdStatus.NotFound, CsfdStatus.ErrorPermanent)
            .OrderBy(e => e.LibraryTitle, StringComparer.CurrentCulture)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("Status;Nazev;Rok;Dotaz;Kandidati;Chyba");

        foreach (var entry in entries)
        {
            var candidates = string.Join(
                " | ",
                entry.Candidates.Select(c =>
                    $"{c.CsfdId}={c.Title} ({c.Year?.ToString(CultureInfo.InvariantCulture) ?? "?"})"));

            builder
                .Append(entry.Status).Append(';')
                .Append(Escape(entry.LibraryTitle)).Append(';')
                .Append(entry.LibraryYear?.ToString(CultureInfo.InvariantCulture)).Append(';')
                .Append(Escape(entry.QueryUsed)).Append(';')
                .Append(Escape(candidates)).Append(';')
                .Append(Escape(entry.LastError))
                .AppendLine();
        }

        var folder = Plugin.Instance?.DataFolderPath ?? Path.GetTempPath();
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "csfd-review.csv");

        // UTF-8 with BOM so Excel opens the diacritics correctly.
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(true), cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("[ČSFD] Wrote {Count} items needing attention to {Path}", entries.Count, path);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var cleaned = value.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',');
        return cleaned.Contains('"', StringComparison.Ordinal)
            ? cleaned.Replace("\"", "\"\"", StringComparison.Ordinal)
            : cleaned;
    }
}
