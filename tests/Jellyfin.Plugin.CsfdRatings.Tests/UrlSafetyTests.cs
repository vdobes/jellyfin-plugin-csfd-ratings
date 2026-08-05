// SPDX-License-Identifier: GPL-3.0-or-later

using Jellyfin.Plugin.CsfdRatings.Sources;
using Xunit;

namespace Jellyfin.Plugin.CsfdRatings.Tests;

public class UrlSafetyTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("csfd-api")]          // Docker Compose service name
    [InlineData("10.0.0.5")]
    [InlineData("172.16.3.9")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.1.1")]
    [InlineData("nas.local")]
    [InlineData("box.lan")]
    [InlineData("svc.internal")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    public void RecognisesPrivateHosts(string host) =>
        Assert.True(UrlSafety.IsPrivateHost(host), host);

    [Theory]
    [InlineData("csfd.example.com")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]        // just outside 172.16.0.0/12
    [InlineData("172.15.255.255")]
    [InlineData("193.85.1.1")]
    [InlineData("2001:4860:4860::8888")]
    public void FlagsPubliclyRoutableHosts(string host) =>
        Assert.False(UrlSafety.IsPrivateHost(host), host);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyHostIsNotConsideredPrivate(string? host) =>
        Assert.False(UrlSafety.IsPrivateHost(host));
}
