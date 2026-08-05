// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Models;

namespace Jellyfin.Plugin.CsfdRatings.Sources;

public interface ICsfdSource
{
    /// <summary>Free-text search. Returns movie candidates only.</summary>
    Task<IReadOnlyList<CsfdCandidate>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>Detail of a single title including the rating.</summary>
    Task<CsfdDetail?> GetDetailAsync(string csfdId, CancellationToken cancellationToken);

    /// <summary>Cheap reachability probe used by the config page and by task start-up.</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken);
}
