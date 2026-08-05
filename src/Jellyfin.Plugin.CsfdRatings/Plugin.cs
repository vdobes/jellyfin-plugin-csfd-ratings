// Jellyfin ČSFD Ratings plugin
// Copyright (C) 2026
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using Jellyfin.Plugin.CsfdRatings.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.CsfdRatings;

/// <summary>
/// Writes ČSFD ratings into the native Jellyfin CommunityRating field.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "ČSFD Ratings";

    public override string Description =>
        "Zapisuje hodnocení z ČSFD do pole CommunityRating, aby bylo vidět ve všech klientech.";

    // Must match the guid in meta.json.
    public override Guid Id { get; } = new("3f8c1a72-6b4d-4f2e-9a51-0c7d8e5b91a4");

    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = Name,
            DisplayName = Name,
            EmbeddedResourcePath = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.Configuration.configPage.html",
                GetType().Namespace)
        }
    ];

    /// <summary>
    /// Test seam. When set, it wins over the loaded plugin instance.
    /// Only tests should assign this; production code leaves it null.
    /// </summary>
    public static PluginConfiguration? ConfigOverride { get; set; }

    /// <summary>
    /// Gets the configuration, falling back to defaults when the plugin is not loaded
    /// (for example inside unit tests).
    /// </summary>
    public static PluginConfiguration Config =>
        ConfigOverride ?? Instance?.Configuration ?? new PluginConfiguration();
}
