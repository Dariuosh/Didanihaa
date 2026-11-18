using Didanihaa.Core;
using Didanihaa.Platform.Windows;
using Didanihaa.Tests.Fixtures;
using Xunit;

namespace Didanihaa.Tests;

public sealed class ServiceOwnershipTests
{
    [Fact]
    public void RegisteredCommandMustMatchExecutableAndCanonicalRoot()
    {
        using var first = new TestWorkspace(); using var second = new TestWorkspace();
        var layout = new RuntimeLayout(first.Root, new());
        var command = $"{WindowsServiceManager.Quote(layout.HostExecutable)} --root {WindowsServiceManager.Quote(layout.Root)}";
        Assert.True(WindowsServiceManager.MatchesInstallation(command, layout));
        Assert.False(WindowsServiceManager.MatchesInstallation(command, new(second.Root, new())));
        Assert.False(WindowsServiceManager.MatchesInstallation(command + " --other", layout));
        Assert.False(WindowsServiceManager.MatchesInstallation(null, layout));
        var otherBinary = $"{WindowsServiceManager.Quote(Path.Combine(first.Root, "another.exe"))} --root {WindowsServiceManager.Quote(layout.Root)}";
        Assert.False(WindowsServiceManager.MatchesInstallation(otherBinary, layout));
    }
}
