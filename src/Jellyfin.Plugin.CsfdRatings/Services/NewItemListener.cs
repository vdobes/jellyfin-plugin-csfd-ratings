// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Sources;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Services;

/// <summary>
/// Picks up movies added by a library scan and looks them up shortly afterwards.
///
/// Additions are debounced: a scan that imports 200 files fires 200 events, and we want
/// one batch a few minutes later rather than 200 immediate lookups racing the scan.
/// </summary>
public sealed class NewItemListener : IHostedService, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMinutes(3);

    private readonly ILibraryManager _libraryManager;
    private readonly CsfdSyncService _sync;
    private readonly ILogger<NewItemListener> _logger;

    private readonly HashSet<Guid> _pending = [];
    private readonly SemaphoreSlim _drainGate = new(1, 1);
    private readonly Lock _pendingLock = new();

    private Timer? _timer;
    private CancellationTokenSource? _stopping;

    public NewItemListener(
        ILibraryManager libraryManager,
        CsfdSyncService sync,
        ILogger<NewItemListener> logger)
    {
        _libraryManager = libraryManager;
        _sync = sync;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = new CancellationTokenSource();
        _libraryManager.ItemAdded += OnItemAdded;
        _timer = new Timer(_ => _ = DrainAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _stopping?.Cancel();
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (!Plugin.Config.Enabled || !Plugin.Config.SyncOnItemAdded)
        {
            return;
        }

        if (e.Item is not Movie movie)
        {
            return;
        }

        lock (_pendingLock)
        {
            _pending.Add(movie.Id);
        }

        // Restart the countdown on every addition so the batch runs once the scan settles.
        _timer?.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    private async Task DrainAsync()
    {
        if (_stopping is null || _stopping.IsCancellationRequested)
        {
            return;
        }

        if (!await _drainGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            Guid[] batch;
            lock (_pendingLock)
            {
                batch = [.. _pending];
                _pending.Clear();
            }

            if (batch.Length == 0)
            {
                return;
            }

            _logger.LogInformation("[ČSFD] {Count} newly added movies queued for lookup", batch.Length);

            var token = _stopping.Token;
            await _sync.Cache.EnsureLoadedAsync(token).ConfigureAwait(false);

            HashSet<Guid>? allowedIds = null;
            if ((Plugin.Config.LibraryIds?.Length ?? 0) > 0)
            {
                allowedIds = _sync.GetMovies().Select(movie => movie.Id).ToHashSet();
            }

            foreach (var id in batch)
            {
                token.ThrowIfCancellationRequested();

                var item = _libraryManager.GetItemById(id);
                if (item is null
                    || (allowedIds is not null && !allowedIds.Contains(id)))
                {
                    continue;
                }

                try
                {
                    await _sync.ProcessAsync(item, token).ConfigureAwait(false);
                }
                catch (CsfdThrottledException)
                {
                    _logger.LogWarning("[ČSFD] Throttled while handling new items, leaving the rest to the task");
                    break;
                }
            }

        }
        catch (OperationCanceledException)
        {
            // Server is shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ČSFD] New item batch failed");
        }
        finally
        {
            // Persist completed work and consumed requests even if a later item in the batch
            // failed or the server started shutting down.
            try
            {
                await _sync.Cache.SaveAsync(CancellationToken.None).ConfigureAwait(false);
                await _sync.Budget.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ČSFD] Could not persist new item batch state");
            }
            finally
            {
                _drainGate.Release();
            }

            // An addition can arrive while a long batch is being processed. Its timer may fire
            // while the gate is held, so explicitly schedule another drain for anything pending.
            var hasPending = false;
            lock (_pendingLock)
            {
                hasPending = _pending.Count > 0;
            }

            if (hasPending && _stopping is { IsCancellationRequested: false })
            {
                _timer?.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _stopping?.Dispose();
        _drainGate.Dispose();
    }
}
