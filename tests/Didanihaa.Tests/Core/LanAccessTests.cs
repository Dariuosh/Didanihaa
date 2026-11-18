using System.Net;
using Didanihaa.Core;
using Xunit;

namespace Didanihaa.Tests;

public sealed class LanAccessTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.1.25", true)]
    [InlineData("10.9.8.7", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("169.254.10.20", true)]
    [InlineData("::ffff:192.168.1.25", true)]
    [InlineData("fc00::1234", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("172.15.255.255", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::ffff:8.8.8.8", false)]
    [InlineData("2001:4860:4860::8888", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::", false)]
    public void OnlyLocalAndLanClientsAreAllowed(string address, bool expected) =>
        Assert.Equal(expected, LanAccess.IsLocalClient(IPAddress.Parse(address)));

    [Theory]
    [InlineData("192.168.1.10", 13521, "192.168.1.10", true)]
    [InlineData("192.168.1.10", 13522, "192.168.1.10", false)]
    [InlineData("attacker.invalid", 13521, "192.168.1.10", false)]
    [InlineData("192.168.1.10.attacker.invalid", 13521, "192.168.1.10", false)]
    [InlineData("192.168.1.11", 13521, "192.168.1.10", false)]
    [InlineData("127.0.0.1", 13521, "::ffff:127.0.0.1", true)]
    [InlineData("localhost", 13521, "::1", true)]
    [InlineData("[::1]", 13521, "::1", true)]
    public void HostMustNameThisListenerAndPort(string host, int port, string localAddress, bool expected) =>
        Assert.Equal(expected, LanAccess.IsValidHost(host, port, 13521, IPAddress.Parse(localAddress)));
}
