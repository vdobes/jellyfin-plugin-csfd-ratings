// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Configuration;
using Jellyfin.Plugin.CsfdRatings.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.CsfdRatings.Tests;

/// <summary>
/// The weekly budget is the last line of defence against hammering ČSFD,
/// so its accounting has to survive a restart.
/// </summary>
public class RequestBudgetTests : IDisposable
{
    public void Dispose() => Plugin.ConfigOverride = null;

    private static RequestBudget NewBudget(int limit)
    {
        Plugin.ConfigOverride = new PluginConfiguration { MaxRequestsPerWeek = limit };
        return new RequestBudget(NullLogger<RequestBudget>.Instance);
    }

    [Fact]
    public async Task ZeroMeansUnlimited()
    {
        var budget = NewBudget(0);
        await budget.ResetAsync(CancellationToken.None);

        for (var i = 0; i < 50; i++)
        {
            Assert.True(await budget.TryConsumeAsync(CancellationToken.None));
        }

        Assert.Equal(0, budget.Limit);
    }

    [Fact]
    public async Task StopsExactlyAtTheLimit()
    {
        var budget = NewBudget(3);
        await budget.ResetAsync(CancellationToken.None);

        Assert.True(await budget.TryConsumeAsync(CancellationToken.None));
        Assert.True(await budget.TryConsumeAsync(CancellationToken.None));
        Assert.True(await budget.TryConsumeAsync(CancellationToken.None));

        Assert.False(await budget.TryConsumeAsync(CancellationToken.None));
        Assert.False(await budget.TryConsumeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TracksRemaining()
    {
        var budget = NewBudget(5);
        await budget.ResetAsync(CancellationToken.None);

        await budget.TryConsumeAsync(CancellationToken.None);
        await budget.TryConsumeAsync(CancellationToken.None);

        Assert.Equal(2, budget.Used);
        Assert.Equal(3, budget.Remaining);
    }

    [Fact]
    public async Task ResetClearsTheWindow()
    {
        var budget = NewBudget(2);
        await budget.ResetAsync(CancellationToken.None);

        await budget.TryConsumeAsync(CancellationToken.None);
        await budget.TryConsumeAsync(CancellationToken.None);
        Assert.False(await budget.TryConsumeAsync(CancellationToken.None));

        await budget.ResetAsync(CancellationToken.None);
        Assert.True(await budget.TryConsumeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WindowResetsSevenDaysAfterItStarted()
    {
        var budget = NewBudget(10);
        await budget.ResetAsync(CancellationToken.None);

        var expected = DateTimeOffset.UtcNow.AddDays(7);
        Assert.True((budget.WindowResetsAt - expected).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ConsumptionSurvivesReload()
    {
        // Spend is counted in memory; without an explicit save a restart forgets part of it.
        var budget = NewBudget(10);
        await budget.ResetAsync(CancellationToken.None);

        await budget.TryConsumeAsync(CancellationToken.None);
        await budget.TryConsumeAsync(CancellationToken.None);
        await budget.SaveAsync(CancellationToken.None);

        var reloaded = new RequestBudget(NullLogger<RequestBudget>.Instance);
        await reloaded.EnsureLoadedAsync(CancellationToken.None);

        Assert.Equal(2, reloaded.Used);
    }

    [Fact]
    public async Task ConcurrentConsumersNeverExceedTheLimit()
    {
        var budget = NewBudget(20);
        await budget.ResetAsync(CancellationToken.None);

        var granted = await Task.WhenAll(
            Enumerable.Range(0, 100)
                .Select(_ => budget.TryConsumeAsync(CancellationToken.None)));

        Assert.Equal(20, granted.Count(g => g));
        Assert.Equal(20, budget.Used);
    }
}
