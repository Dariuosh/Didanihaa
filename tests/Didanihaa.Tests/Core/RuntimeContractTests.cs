using System.Text.Json;
using Didanihaa.Core;
using Didanihaa.Tests.Fixtures;
using Xunit;

namespace Didanihaa.Tests;

public sealed class RuntimeContractTests
{
    private static void WriteConfiguration(string root, RuntimeConfiguration? configuration = null, bool example = false) =>
        File.WriteAllText(Path.Combine(root, example ? "didanihaa.example.json" : "didanihaa.json"),
            JsonSerializer.Serialize(configuration ?? new(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    [Fact]
    public void RootResolutionUsesExplicitWorkingDirectoryAndApplicationPriority()
    {
        using var application = new TestWorkspace();
        using var working = new TestWorkspace();
        WriteConfiguration(application.Root); WriteConfiguration(working.Root);
        Assert.Equal(application.Root, RuntimeRoot.Resolve([], application.Root, working.Root));
        Assert.Equal(working.Root, RuntimeRoot.Resolve(["--root", Path.GetRelativePath(application.Root, working.Root)], application.Root, application.Root));
        Assert.Equal(RuntimeRoot.Id(working.Root), RuntimeRoot.Id(Path.Combine(working.Root, ".") + Path.DirectorySeparatorChar));
        if (OperatingSystem.IsWindows()) Assert.Equal(RuntimeRoot.Id(working.Root), RuntimeRoot.Id(working.Root.ToUpperInvariant()));
    }

    [Fact]
    public void FirstLoadCreatesConfigurationOnceAndPreservesUnknownSettings()
    {
        using var workspace = new TestWorkspace();
        var example = Path.Combine(workspace.Root, "didanihaa.example.json");
        const string text = """{"publicPort":24510,"futureSetting":{"enabled":true}}""";
        File.WriteAllText(example, text);
        var first = RuntimeLayout.Load(["--root", workspace.Root]);
        Assert.Equal(24510, first.Configuration.PublicPort);
        Assert.Equal(text, File.ReadAllText(Path.Combine(workspace.Root, "didanihaa.json")));
        File.WriteAllText(example, "invalid replacement example");
        Assert.Equal(24510, RuntimeLayout.Load(["--root", workspace.Root]).Configuration.PublicPort);
        Assert.True(first.Configuration.ExtraSettings!["futureSetting"].GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void MalformedLocalConfigurationDoesNotFallBackOrOverwrite()
    {
        using var workspace = new TestWorkspace();
        WriteConfiguration(workspace.Root, example: true);
        var local = Path.Combine(workspace.Root, "didanihaa.json");
        File.WriteAllText(local, "broken local settings");
        Assert.Throws<InvalidOperationException>(() => RuntimeLayout.Load(["--root", workspace.Root]));
        Assert.Equal("broken local settings", File.ReadAllText(local));
    }

    [Fact]
    public void MalformedExampleDoesNotCreateLocalConfiguration()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(Path.Combine(workspace.Root, "didanihaa.example.json"), "invalid example");
        Assert.Throws<InvalidOperationException>(() => RuntimeLayout.Load(["--root", workspace.Root]));
        Assert.False(File.Exists(Path.Combine(workspace.Root, "didanihaa.json")));
    }

    [Theory]
    [InlineData(1023, 13522)]
    [InlineData(65536, 13522)]
    [InlineData(13521, 13521)]
    [InlineData(13521, 0)]
    public void InvalidPortsAreRejectedBeforeInitialization(int publicPort, int mediaPort)
    {
        using var workspace = new TestWorkspace();
        WriteConfiguration(workspace.Root, new() { PublicPort = publicPort, MediaPort = mediaPort }, example: true);
        Assert.Throws<InvalidOperationException>(() => RuntimeLayout.Load(["--root", workspace.Root]));
        Assert.False(File.Exists(Path.Combine(workspace.Root, "didanihaa.json")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("")]
    [InlineData(".")]
    public void InvalidOwnedPathsAreRejectedWithoutChangingSettings(string path)
    {
        using var workspace = new TestWorkspace();
        WriteConfiguration(workspace.Root, new() { DataDirectory = path });
        var original = File.ReadAllText(Path.Combine(workspace.Root, "didanihaa.json"));
        Assert.Throws<InvalidOperationException>(() => RuntimeLayout.Load(["--root", workspace.Root]));
        Assert.Equal(original, File.ReadAllText(Path.Combine(workspace.Root, "didanihaa.json")));
    }

    [Fact]
    public async Task ConcurrentUpdatesPreserveIndependentChangesAndUnknownFields()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(Path.Combine(workspace.Root, "didanihaa.json"), """{"futureSetting":{"value":7}}""");
        await Task.WhenAll(
            Task.Run(() => RuntimeSettings.Update(workspace.Root, current => current with { PublicPort = 24510 })),
            Task.Run(() => RuntimeSettings.Update(workspace.Root, current => current with { TempDirectory = "temp/changed" })));
        var loaded = RuntimeLayout.Load(["--root", workspace.Root]);
        Assert.Equal(24510, loaded.Configuration.PublicPort);
        Assert.Equal("temp/changed", loaded.Configuration.TempDirectory);
        Assert.Equal(7, loaded.Configuration.ExtraSettings!["futureSetting"].GetProperty("value").GetInt32());
        Assert.Empty(Directory.GetFiles(workspace.Root, "didanihaa.json.*.tmp"));
    }

    [Fact]
    public void InvalidUpdateKeepsExistingConfigurationIntact()
    {
        using var workspace = new TestWorkspace();
        WriteConfiguration(workspace.Root);
        var original = File.ReadAllText(Path.Combine(workspace.Root, "didanihaa.json"));
        Assert.Throws<InvalidOperationException>(() => RuntimeSettings.Update(workspace.Root, current => current with { MediaPort = current.PublicPort }));
        Assert.Equal(original, File.ReadAllText(Path.Combine(workspace.Root, "didanihaa.json")));
    }

    [Fact]
    public void FailedReplacementPreservesReadOnlyConfigurationAndRemovesStagingFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var workspace = new TestWorkspace();
        WriteConfiguration(workspace.Root);
        var destination = Path.Combine(workspace.Root, "didanihaa.json");
        var original = File.ReadAllText(destination);
        File.SetAttributes(destination, File.GetAttributes(destination) | FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<InvalidOperationException>(() => RuntimeSettings.Update(workspace.Root, current => current with { PublicPort = 24510 }));
            Assert.Equal(original, File.ReadAllText(destination));
            Assert.Empty(Directory.GetFiles(workspace.Root, "didanihaa.json.*.tmp"));
        }
        finally { File.SetAttributes(destination, FileAttributes.Normal); }
    }

    [Fact]
    public void ProbeUpdateReadsLatestSettingsInsteadOfReplacingAStaleSnapshot()
    {
        using var workspace = new TestWorkspace();
        WriteConfiguration(workspace.Root);
        var layout = RuntimeLayout.Load(["--root", workspace.Root]);
        RuntimeSettings.Update(workspace.Root, current => current with { PublicPort = 24510 });
        var tools = Path.Combine(workspace.Root, "tools"); Directory.CreateDirectory(tools);
        var probe = Path.Combine(tools, "ffprobe.exe"); File.WriteAllBytes(probe, []);
        layout.SaveProbePath(probe);
        Assert.Equal(24510, layout.Configuration.PublicPort);
        Assert.Equal(24510, RuntimeLayout.Load(["--root", workspace.Root]).Configuration.PublicPort);
    }
}
