// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Caching;
using Jellyfin.Plugin.CsfdRatings.Matching;
using Jellyfin.Plugin.CsfdRatings.Services;
using Jellyfin.Plugin.CsfdRatings.Sources;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.CsfdRatings;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<RatingCache>();
        serviceCollection.AddSingleton<CsfdRateLimiter>();
        serviceCollection.AddSingleton<RequestBudget>();
        serviceCollection.AddSingleton<ICsfdSource, NodeCsfdApiSource>();
        serviceCollection.AddSingleton<CsfdMatcher>();
        serviceCollection.AddSingleton<RatingWriter>();
        serviceCollection.AddSingleton<CsfdSyncService>();
        serviceCollection.AddHostedService<NewItemListener>();
    }
}
