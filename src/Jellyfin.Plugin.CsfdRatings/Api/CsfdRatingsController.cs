// SPDX-License-Identifier: GPL-3.0-or-later

using System.Net.Mime;
using Jellyfin.Plugin.CsfdRatings.Models;
using Jellyfin.Plugin.CsfdRatings.Services;
using Jellyfin.Plugin.CsfdRatings.Sources;
using Jellyfin.Plugin.CsfdRatings.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Api;

/// <summary>
/// Endpoints behind the buttons on the plugin's settings page.
///
/// Only cheap operations run inline. Anything that touches the whole library is handed to the
/// scheduled task system instead, so it shows up with progress and a cancel button in the
/// Dashboard rather than blocking an HTTP request for twenty minutes.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Plugins/CsfdRatings")]
[Produces(MediaTypeNames.Application.Json)]
public class CsfdRatingsController : ControllerBase
{
    private readonly CsfdSyncService _sync;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<CsfdRatingsController> _logger;

    public CsfdRatingsController(
        CsfdSyncService sync,
        ITaskManager taskManager,
        ILogger<CsfdRatingsController> logger)
    {
        _sync = sync;
        _taskManager = taskManager;
        _logger = logger;
    }

    /// <summary>Counts, budget and last run - everything the settings page displays.</summary>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CsfdStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        await _sync.Cache.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _sync.Budget.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var counts = _sync.GetStatusCounts();

        return new CsfdStatusDto
        {
            Enabled = Plugin.Config.Enabled,
            DryRun = Plugin.Config.DryRun,
            MovieCount = _sync.GetMovies().Count,
            Resolved = counts.GetValueOrDefault(nameof(CsfdStatus.Resolved)),
            NeedsReview = counts.GetValueOrDefault(nameof(CsfdStatus.NeedsReview)),
            NotFound = counts.GetValueOrDefault(nameof(CsfdStatus.NotFound)),
            NoRating = counts.GetValueOrDefault(nameof(CsfdStatus.ResolvedNoRating)),
            Errors = counts.GetValueOrDefault(nameof(CsfdStatus.ErrorTransient))
                     + counts.GetValueOrDefault(nameof(CsfdStatus.ErrorPermanent)),
            WeeklyLimit = _sync.Budget.Limit,
            WeeklyUsed = _sync.Budget.Used,
            WeeklyResetsAt = _sync.Budget.WindowResetsAt,
            LastRunUtc = _sync.LastRunUtc,
            LastRunSummary = _sync.LastRun?.ToString()
        };
    }

    /// <summary>One request to the sidecar to prove it is reachable and parsing correctly.</summary>
    [HttpPost("TestConnection")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CsfdTestDto>> TestConnection(CancellationToken cancellationToken)
    {
        var url = Plugin.Config.ApiBaseUrl;

        try
        {
            var detail = await _sync.Source.GetDetailAsync("2294", cancellationToken).ConfigureAwait(false);
            if (detail is null)
            {
                return new CsfdTestDto
                {
                    Success = false,
                    Message = $"Sidecar na {url} odpověděl, ale detail se nepodařilo přečíst."
                };
            }

            return new CsfdTestDto
            {
                Success = true,
                Message = $"OK — {detail.Title} ({detail.Year}): {detail.RatingPercent}% "
                          + $"z {detail.RatingCount} hodnocení."
            };
        }
        catch (CsfdBudgetExhaustedException ex)
        {
            return new CsfdTestDto
            {
                Success = false,
                Message = $"Týdenní limit dotazů je vyčerpán, obnoví se {ex.ResetsAt:g}."
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ČSFD] Connection test failed");
            return new CsfdTestDto { Success = false, Message = $"Chyba: {ex.Message}" };
        }
    }

    /// <summary>Queues the refresh task.</summary>
    [HttpPost("Run")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Run()
    {
        _taskManager.Execute<RefreshCsfdRatingsTask>();
        return NoContent();
    }

    /// <summary>Queues the restore task.</summary>
    [HttpPost("Restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Restore()
    {
        _taskManager.Execute<RestoreOriginalRatingsTask>();
        return NoContent();
    }

    /// <summary>Queues the review-reset task.</summary>
    [HttpPost("ResetReview")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult ResetReview()
    {
        _taskManager.Execute<ResetNeedsReviewTask>();
        return NoContent();
    }

    /// <summary>Queues the CSV report task.</summary>
    [HttpPost("ExportReview")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult ExportReview()
    {
        _taskManager.Execute<ExportReviewReportTask>();
        return NoContent();
    }

    /// <summary>The unresolved items, so the page can list them without reading the CSV.</summary>
    [HttpGet("Review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CsfdReviewDto>>> GetReview(CancellationToken cancellationToken)
    {
        await _sync.Cache.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var items = _sync.Cache
            .GetByStatus(CsfdStatus.NeedsReview, CsfdStatus.NotFound, CsfdStatus.ErrorPermanent)
            .OrderBy(e => e.LibraryTitle, StringComparer.CurrentCulture)
            .Take(200)
            .Select(e => new CsfdReviewDto
            {
                ItemId = e.ItemId,
                Title = e.LibraryTitle,
                Year = e.LibraryYear,
                Status = e.Status.ToString(),
                Candidates = e.Candidates
                    .Select(c => new CsfdCandidateDto { CsfdId = c.CsfdId, Title = c.Title, Year = c.Year })
                    .ToList()
            })
            .ToList();

        return items;
    }
}

public sealed class CsfdStatusDto
{
    public bool Enabled { get; set; }

    public bool DryRun { get; set; }

    public int MovieCount { get; set; }

    public int Resolved { get; set; }

    public int NeedsReview { get; set; }

    public int NotFound { get; set; }

    public int NoRating { get; set; }

    public int Errors { get; set; }

    public int WeeklyLimit { get; set; }

    public int WeeklyUsed { get; set; }

    public DateTimeOffset WeeklyResetsAt { get; set; }

    public DateTimeOffset? LastRunUtc { get; set; }

    public string? LastRunSummary { get; set; }
}

public sealed class CsfdTestDto
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;
}

public sealed class CsfdReviewDto
{
    public string ItemId { get; set; } = string.Empty;

    public string? Title { get; set; }

    public int? Year { get; set; }

    public string Status { get; set; } = string.Empty;

    public IReadOnlyList<CsfdCandidateDto> Candidates { get; set; } = [];
}

public sealed class CsfdCandidateDto
{
    public string CsfdId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }
}
