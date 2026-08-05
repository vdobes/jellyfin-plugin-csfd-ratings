// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.CsfdRatings.Matching;

/// <summary>
/// Short hash of exactly the fields the matcher looks at.
///
/// When it changes, an item that previously could not be matched deserves one fresh attempt:
/// the user has most likely corrected the title or the year. Anything not used for matching
/// must stay out of the hash, otherwise unrelated metadata edits would trigger pointless lookups.
/// </summary>
public static class ItemSignature
{
    public static string For(BaseItem item)
    {
        var payload = string.Join(
            '|',
            TitleNormalizer.Normalize(item.Name),
            TitleNormalizer.Normalize(item.OriginalTitle),
            item.ProductionYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            item.GetType().Name);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes)[..16];
    }
}
