// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Services;

/// <summary>
/// A rolling seven-day cap on outbound requests.
///
/// The per-request delay limits the burst rate, but nothing stops a badly configured
/// library from generating tens of thousands of lookups over a week. This is the backstop:
/// once the budget is gone the run ends cleanly and existing ratings stay as they are.
/// The window is a simple sliding reset rather than a calendar week, so a single huge
/// backfill cannot be followed by another one the next morning.
/// </summary>
public sealed class RequestBudget
{
    private readonly ILogger<RequestBudget> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _windowStart = DateTimeOffset.UtcNow;
    private int _used;
    private bool _loaded;

    public RequestBudget(ILogger<RequestBudget> logger)
    {
        _logger = logger;
    }

    private sealed class State
    {
        public DateTimeOffset WindowStart { get; set; }

        public int Used { get; set; }
    }

    private static string StatePath
    {
        get
        {
            var folder = Plugin.Instance?.DataFolderPath
                         ?? Path.Combine(Path.GetTempPath(), "jellyfin-csfd-ratings");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "budget.json");
        }
    }

    public int Limit => Math.Max(0, Plugin.Config.MaxRequestsPerWeek);

    public int Used => _used;

    public int Remaining => Limit == 0 ? int.MaxValue : Math.Max(0, Limit - _used);

    public DateTimeOffset WindowResetsAt => _windowStart.AddDays(7);

    /// <summary>
    /// Reserves one request. Returns false when the weekly budget is exhausted.
    /// </summary>
    public async Task<bool> TryConsumeAsync(CancellationToken cancellationToken)
    {
        if (Limit == 0)
        {
            return true;
        }

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _windowStart >= TimeSpan.FromDays(7))
            {
                _windowStart = now;
                _used = 0;
                _logger.LogInformation("[ČSFD] Weekly request window reset");
            }

            if (_used >= Limit)
            {
                return false;
            }

            _used++;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            var path = StatePath;
            if (File.Exists(path))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                    var state = JsonSerializer.Deserialize<State>(json);
                    if (state is not null)
                    {
                        _windowStart = state.WindowStart;
                        _used = state.Used;
                    }
                }
                catch (Exception ex)
                {
                    // Losing the counter is harmless; starting a fresh window is the safe default.
                    _logger.LogWarning(ex, "[ČSFD] Budget state unreadable, starting a new window");
                    _windowStart = DateTimeOffset.UtcNow;
                    _used = 0;
                }
            }

            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ČSFD] Could not persist request budget");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _windowStart = DateTimeOffset.UtcNow;
            _used = 0;
            _loaded = true;
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        var path = StatePath;
        var temp = path + ".tmp";
        var json = JsonSerializer.Serialize(new State { WindowStart = _windowStart, Used = _used });
        await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }
}
