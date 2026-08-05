// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.CsfdRatings.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CsfdRatings.Caching;

/// <summary>
/// In-memory dictionary backed by a single JSON file in the plugin data folder.
/// Writes go through a temp file and an atomic move so a crash mid-write cannot
/// leave a truncated cache behind.
/// </summary>
public sealed class RatingCache : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<RatingCache> _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, CsfdCacheEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _loaded;

    public RatingCache(ILogger<RatingCache> logger)
    {
        _logger = logger;
    }

    private static string CacheFilePath
    {
        get
        {
            var folder = Plugin.Instance?.DataFolderPath
                         ?? Path.Combine(Path.GetTempPath(), "jellyfin-csfd-ratings");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "ratings.json");
        }
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            var path = CacheFilePath;
            if (File.Exists(path))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                    var entries = JsonSerializer.Deserialize<List<CsfdCacheEntry>>(json, SerializerOptions);
                    if (entries is not null)
                    {
                        foreach (var entry in entries.Where(e => !string.IsNullOrEmpty(e.ItemId)))
                        {
                            _entries[entry.ItemId] = entry;
                        }
                    }

                    _logger.LogInformation("[ČSFD] Loaded {Count} cache entries", _entries.Count);
                }
                catch (Exception ex)
                {
                    // A corrupt cache must never stop the plugin from loading. Rename and start fresh.
                    _logger.LogError(ex, "[ČSFD] Cache file unreadable, moving it aside");
                    TryQuarantine(path);
                }
            }

            _loaded = true;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public CsfdCacheEntry? Get(Guid itemId) =>
        _entries.TryGetValue(itemId.ToString("N"), out var entry) ? entry : null;

    public IReadOnlyList<CsfdCacheEntry> GetAll() => _entries.Values.ToList();

    public IReadOnlyList<CsfdCacheEntry> GetByStatus(params CsfdStatus[] statuses)
    {
        var wanted = new HashSet<CsfdStatus>(statuses);
        return _entries.Values.Where(e => wanted.Contains(e.Status)).ToList();
    }

    public void Upsert(CsfdCacheEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ItemId))
        {
            return;
        }

        _entries[entry.ItemId] = entry;
    }

    public bool Remove(Guid itemId) => _entries.TryRemove(itemId.ToString("N"), out _);

    public void Clear() => _entries.Clear();

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = CacheFilePath;
            var temp = path + ".tmp";
            var snapshot = _entries.Values.OrderBy(e => e.LibraryTitle ?? e.ItemId).ToList();

            var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
            await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ČSFD] Failed to persist cache");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void TryQuarantine(string path)
    {
        try
        {
            File.Move(path, $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ČSFD] Could not move corrupt cache aside");
        }
    }

    public void Dispose() => _writeGate.Dispose();
}
