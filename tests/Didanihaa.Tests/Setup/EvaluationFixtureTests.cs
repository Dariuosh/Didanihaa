using System.Text.Json;
using Didanihaa.Core;
using Didanihaa.Tests.Fixtures;
using Xunit;

namespace Didanihaa.Tests;

public sealed class EvaluationFixtureTests
{
    [Fact]
    public async Task SyntheticLibraryContainsPendingPublishedAndFavoriteMedia()
    {
        using var fixture = await SetupLibraryFixture.CreateAsync();
        Assert.Equal(new CatalogStatistics(1, 2, 1, 0), fixture.Catalog.Statistics());
        Assert.False(fixture.Pending.Published);
        Assert.Null(fixture.Catalog.Find(fixture.Pending.Id, true));
        Assert.Equal(fixture.Published.Id, Assert.Single(fixture.Catalog.Candidates(null, null, 0, 10, true).Items).Id);
        Assert.Equal(fixture.Published.Id, Assert.Single(fixture.Profiles.Favorites(true)));
        Assert.Equal("basic", fixture.Published.Metadata.Status);
        Assert.False(File.Exists(fixture.Layout.FfprobePath));
        Assert.False(Path.IsPathFullyQualified(fixture.Source.StoredPath));
        Assert.Contains(" ", fixture.Layout.Root);
        Assert.Contains("آزمایشی", fixture.Layout.Root);
        using var wave = new BinaryReader(File.OpenRead(Path.Combine(fixture.Source.ResolvedPath, fixture.Published.RelativePath)));
        Assert.Equal("RIFF"u8.ToArray(), wave.ReadBytes(4));
        Assert.Equal(wave.BaseStream.Length - 8, wave.ReadInt32());
        Assert.Equal("WAVE"u8.ToArray(), wave.ReadBytes(4));
    }

    [Fact]
    public async Task TwoPortableRootsKeepCatalogAndProfileChangesIndependent()
    {
        using var first = await SetupLibraryFixture.CreateAsync();
        using var second = await SetupLibraryFixture.CreateAsync();
        Assert.NotEqual(first.Layout.DatabasePath, second.Layout.DatabasePath);
        first.Catalog.SetPublished([first.Published.Id], false);
        first.Profiles.Favorite(first.Published.Id, false);
        Assert.Empty(first.Profiles.Favorites(true));
        Assert.Empty(first.Catalog.Candidates(null, null, 0, 10, true).Items);
        Assert.Single(second.Catalog.Candidates(null, null, 0, 10, true).Items);
        Assert.Equal(second.Published.Id, Assert.Single(second.Profiles.Favorites(true)));
        Assert.True(PathSafety.IsWithin(TestWorkspace.EvaluationPath, first.Layout.Root));
        Assert.True(PathSafety.IsWithin(TestWorkspace.EvaluationPath, second.Layout.Root));
    }

    [Fact]
    public void FixtureRemovalPreservesAnotherOwnedWorkspace()
    {
        using var first = new TestWorkspace();
        using var second = new TestWorkspace();
        var survivor = Path.Combine(second.Root, "keep.txt");
        File.WriteAllText(survivor, "another fixture");
        first.Dispose();
        Assert.False(Directory.Exists(first.ContainerPath));
        Assert.Equal("another fixture", File.ReadAllText(survivor));
    }

    [Fact]
    public void CleanupRefusesAnUnownedDirectory()
    {
        using var workspace = new TestWorkspace();
        var owner = File.ReadAllText(workspace.MarkerPath);
        try
        {
            File.WriteAllText(workspace.MarkerPath, "another owner");
            Assert.Throws<InvalidOperationException>(() => workspace.Dispose());
            Assert.True(Directory.Exists(workspace.Root));
        }
        finally { File.WriteAllText(workspace.MarkerPath, owner); }
    }

    [Fact]
    public void ExplicitFixtureRootDoesNotReadAnInvalidParentConfiguration()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(Path.Combine(workspace.ContainerPath, "didanihaa.json"), "invalid parent config");
        File.WriteAllText(Path.Combine(workspace.Root, "didanihaa.json"), JsonSerializer.Serialize(new RuntimeConfiguration()));
        var loaded = RuntimeLayout.Load(["--root", workspace.Root]);
        Assert.Equal(workspace.Root, loaded.Root);
        Assert.True(PathSafety.IsWithin(workspace.Root, loaded.DatabasePath));
    }

    [Fact]
    public void ApplicationPathIsFixedAndSeparateFromDisposableData()
    {
        using var first = new TestWorkspace();
        using var second = new TestWorkspace();
        Assert.Equal(Path.Combine(TestWorkspace.EvaluationPath, "app"), TestWorkspace.ApplicationPath);
        Assert.False(PathSafety.IsWithin(first.ContainerPath, TestWorkspace.ApplicationPath));
        Assert.False(PathSafety.IsWithin(second.ContainerPath, TestWorkspace.ApplicationPath));
        Assert.NotEqual(first.ContainerPath, second.ContainerPath);
    }
}
