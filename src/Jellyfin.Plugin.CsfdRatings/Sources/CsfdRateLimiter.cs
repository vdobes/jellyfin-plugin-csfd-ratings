// SPDX-License-Identifier: GPL-3.0-or-later

namespace Jellyfin.Plugin.CsfdRatings.Sources;

/// <summary>
/// Serialises every outbound call and enforces a minimum gap between them.
/// One instance, one concurrent request.
/// </summary>
public sealed class CsfdRateLimiter : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastCall = DateTimeOffset.MinValue;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        var delayMs = Math.Max(0, Plugin.Config.RequestDelayMs);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastCall;
            var minimum = TimeSpan.FromMilliseconds(delayMs);
            if (elapsed < minimum)
            {
                await Task.Delay(minimum - elapsed, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lastCall = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
