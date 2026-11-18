using System.Text.Json;
using Didanihaa.Core;
using Didanihaa.Infrastructure;
using Didanihaa.Tests.Fixtures;
using Xunit;

namespace Didanihaa.Tests;

public sealed class MediaToolTests
{
    [Fact]
    public void AutomaticChoicePrefersBundledPerToolAndReadsTheLegacyDefault()
    {
        using var workspace = new TestWorkspace();
        var layout = new RuntimeLayout(workspace.Root, new() { FfprobePath = "tools/ffprobe.exe" });
        var legacy = AddTool(workspace.Root, "tools", "ffprobe.exe");
        var bundled = AddTool(workspace.Root, "ffmpeg/bin", "ffmpeg.exe");
        Assert.Equal((legacy, "legacy"), layout.MediaToolPath("ffprobe.exe"));
        Assert.Equal((bundled, "bundled"), layout.MediaToolPath("ffmpeg.exe"));
        var bundledProbe = AddTool(workspace.Root, "ffmpeg/bin", "ffprobe.exe");
        Assert.Equal((bundledProbe, "bundled"), layout.MediaToolPath("ffprobe.exe"));
    }

    [Theory]
    [InlineData("ffprobe.exe")]
    [InlineData("ffmpeg.exe")]
    public async Task ExplicitSingleToolNeverMixesWithAnotherInstallation(string present)
    {
        using var workspace = new TestWorkspace();
        var installed = AddTool(workspace.Root, "ابزار رسانه/bin", present);
        var other = present == "ffprobe.exe" ? "ffmpeg.exe" : "ffprobe.exe";
        AddTool(workspace.Root, "ffmpeg/bin", other);
        var layout = WriteSettings(workspace.Root);
        layout.SaveProbePath(installed);
        var tools = new MediaToolCatalog(layout);
        var missing = other == "ffprobe.exe" ? await tools.ProbeAsync(TestContext.Current.CancellationToken) : await tools.EncoderAsync(TestContext.Current.CancellationToken);
        Assert.Equal("missing", missing.Status.State);
        Assert.Equal("configured", missing.Status.Source);
        Assert.Equal(installed, layout.MediaToolPath(present).Path);
        Assert.False(Path.IsPathRooted(layout.Configuration.FfprobePath));
        layout.SaveProbePath("");
        Assert.Equal("", RuntimeLayout.Load(["--root", workspace.Root]).MediaToolOverride);
        Assert.Equal("bundled", layout.MediaToolPath(other).Source);
    }

    [Fact]
    public async Task AnInvalidExecutableDoesNotStopScanningOrUnpublishUnchangedMedia()
    {
        using var fixture = await SetupLibraryFixture.CreateAsync();
        AddTool(fixture.Layout.Root, "ffmpeg/bin", "ffprobe.exe");
        var tools = new MediaToolCatalog(fixture.Layout);
        var (path, status) = await tools.ProbeAsync(TestContext.Current.CancellationToken);
        Assert.Null(path);
        Assert.Equal("invalid", status.State);
        Assert.Equal("missing", (await tools.EncoderAsync(TestContext.Current.CancellationToken)).Status.State);

        var job = fixture.Catalog.QueueScan(fixture.Source.Id);
        Assert.True(fixture.Catalog.ClaimScan(job.Id));
        await new LibraryScanner(fixture.Catalog, new MetadataProbe(tools)).RunAsync(job, TestContext.Current.CancellationToken);
        Assert.Equal("completed", fixture.Catalog.Jobs().Single(item => item.Id == job.Id).Status);
        Assert.Equal("basic", fixture.Catalog.Find(fixture.Published.Id, true)!.Metadata.Status);
        Assert.Null(fixture.Catalog.Find(fixture.Pending.Id, true));
    }

    [Fact]
    public void InvalidPathDoesNotOverwriteTheExistingChoice()
    {
        using var workspace = new TestWorkspace();
        var layout = WriteSettings(workspace.Root);
        var path = AddTool(workspace.Root, "bin", "ffmpeg.exe");
        layout.SaveProbePath(path);
        var previous = layout.Configuration.FfprobePath;
        Assert.Throws<ArgumentException>(() => layout.SaveProbePath("../outside/ffprobe.exe"));
        if (OperatingSystem.IsWindows())
            Assert.Throws<ArgumentException>(() => layout.SaveProbePath(@"\\server\share\ffprobe.exe"));
        Assert.Equal(previous, RuntimeLayout.Load(["--root", workspace.Root]).Configuration.FfprobePath);
    }

    private static string AddTool(string root, string folder, string name)
    {
        var directory = Path.GetFullPath(Path.Combine(root, folder));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "deliberately not executable");
        return path;
    }

    private static RuntimeLayout WriteSettings(string root)
    {
        var configuration = new RuntimeConfiguration();
        File.WriteAllText(Path.Combine(root, "didanihaa.json"), JsonSerializer.Serialize(configuration));
        return new(root, configuration);
    }
}
