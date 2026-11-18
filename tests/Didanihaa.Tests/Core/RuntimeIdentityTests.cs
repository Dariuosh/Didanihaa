using System.Text.Json;
using Didanihaa.Core;
using Didanihaa.Tests.Fixtures;
using Xunit;

namespace Didanihaa.Tests;

public sealed class RuntimeIdentityTests
{
    [Fact]
    public void SameRoleIsExclusiveButDifferentRolesAndRootsRemainIndependent()
    {
        using var first = new TestWorkspace(); using var second = new TestWorkspace();
        var layout = new RuntimeLayout(first.Root, new());
        using var control = RuntimeInstance.Acquire(layout, RuntimeRole.Control);
        Assert.Throws<InvalidOperationException>(() => RuntimeInstance.Acquire(layout, RuntimeRole.Control));
        using var media = RuntimeInstance.Acquire(layout, RuntimeRole.Media);
        using var otherControl = RuntimeInstance.Acquire(new(second.Root, new()), RuntimeRole.Control);
        Assert.NotEqual(control.Identity.RootId, otherControl.Identity.RootId);
        Assert.True(control.Identity.IsCurrent(first.Root, RuntimeRole.Control, Environment.ProcessPath!));
        Assert.True(media.Identity.IsCurrent(first.Root, RuntimeRole.Media, Environment.ProcessPath!));
        Assert.False(otherControl.Identity.IsCurrent(first.Root, RuntimeRole.Control, Environment.ProcessPath!));
    }

    [Fact]
    public void ReleaseRemovesDescriptorAndAReusedPidGetsADifferentInstance()
    {
        using var workspace = new TestWorkspace();
        var layout = new RuntimeLayout(workspace.Root, new());
        RuntimeIdentity oldIdentity;
        using (var first = RuntimeInstance.Acquire(layout, RuntimeRole.Media)) { oldIdentity = first.Identity; }
        Assert.False(File.Exists(RuntimeIdentity.DescriptorPath(workspace.Root, RuntimeRole.Media)));
        using var next = RuntimeInstance.Acquire(layout, RuntimeRole.Media);
        Assert.Equal(oldIdentity.ProcessId, next.Identity.ProcessId);
        Assert.Equal(oldIdentity.StartedUtc, next.Identity.StartedUtc);
        Assert.NotEqual(oldIdentity.InstanceId, next.Identity.InstanceId);
        Assert.False(oldIdentity.IsCurrent(workspace.Root, RuntimeRole.Media, Environment.ProcessPath!));
        Assert.True(next.Identity.IsCurrent(workspace.Root, RuntimeRole.Media, Environment.ProcessPath!));
    }

    [Fact]
    public void RoleExecutableAndDescriptorMustAllMatch()
    {
        using var workspace = new TestWorkspace();
        using var instance = RuntimeInstance.Acquire(new(workspace.Root, new()), RuntimeRole.Control);
        Assert.False(instance.Identity.IsCurrent(workspace.Root, RuntimeRole.Media, Environment.ProcessPath!));
        Assert.False(instance.Identity.IsCurrent(workspace.Root, RuntimeRole.Control, Path.Combine(workspace.Root, "other.exe")));
        File.WriteAllText(RuntimeIdentity.DescriptorPath(workspace.Root, RuntimeRole.Control), "not JSON");
        Assert.False(instance.Identity.IsCurrent(workspace.Root, RuntimeRole.Control, Environment.ProcessPath!));
    }

    [Fact]
    public void ADescriptorCannotSubstituteForTheActualProcessStartTime()
    {
        using var workspace = new TestWorkspace();
        using var instance = RuntimeInstance.Acquire(new(workspace.Root, new()), RuntimeRole.Media);
        var stale = instance.Identity with { StartedUtc = instance.Identity.StartedUtc.AddSeconds(-5) };
        File.WriteAllText(RuntimeIdentity.DescriptorPath(workspace.Root, RuntimeRole.Media), JsonSerializer.Serialize(stale, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(stale.IsCurrent(workspace.Root, RuntimeRole.Media, Environment.ProcessPath!));
    }

    [Fact]
    public void ADeadProcessRecordDoesNotProveReadiness()
    {
        using var workspace = new TestWorkspace();
        using var instance = RuntimeInstance.Acquire(new(workspace.Root, new()), RuntimeRole.Media);
        var dead = instance.Identity with { ProcessId = int.MaxValue };
        File.WriteAllText(RuntimeIdentity.DescriptorPath(workspace.Root, RuntimeRole.Media), JsonSerializer.Serialize(dead, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(dead.IsCurrent(workspace.Root, RuntimeRole.Media, Environment.ProcessPath!));
    }
}
