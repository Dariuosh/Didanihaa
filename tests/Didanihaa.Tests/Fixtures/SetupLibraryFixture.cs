using System.Text.Json;
using Didanihaa.Core;
using Didanihaa.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Didanihaa.Tests.Fixtures;

internal sealed class SetupLibraryFixture : IDisposable
{
    public TestWorkspace Workspace { get; } = new();
    public RuntimeLayout Layout { get; }
    public CatalogRepository Catalog { get; }
    public ProfileRepository Profiles { get; }
    public SourceFolder Source { get; private set; } = null!;
    public MediaCandidate Published { get; private set; } = null!;
    public MediaCandidate Pending { get; private set; } = null!;

    private SetupLibraryFixture()
    {
        Layout = new(Workspace.Root, new RuntimeConfiguration());
        File.WriteAllText(Path.Combine(Layout.Root, "didanihaa.json"), JsonSerializer.Serialize(Layout.Configuration, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Catalog = new(Layout);
        Catalog.Initialize();
        Profiles = new(Layout);
    }

    public static async Task<SetupLibraryFixture> CreateAsync()
    {
        var fixture = new SetupLibraryFixture();
        try
        {
            var sourcePath = Path.Combine(fixture.Layout.Root, "رسانه نمونه");
            Directory.CreateDirectory(sourcePath);
            WriteSilentWave(Path.Combine(sourcePath, "قطعه 01.wav"));
            WriteSilentWave(Path.Combine(sourcePath, "قطعه 02.wav"));
            fixture.Source = fixture.Catalog.AddSource("کتابخانهٔ آزمایشی", sourcePath);
            var job = fixture.Catalog.QueueScan(fixture.Source.Id);
            if (!fixture.Catalog.ClaimScan(job.Id)) throw new InvalidOperationException("Fixture scan could not be claimed.");
            await new LibraryScanner(fixture.Catalog, new MetadataProbe(fixture.Layout)).RunAsync(job, CancellationToken.None);
            if (fixture.Catalog.Jobs().Single().Status != "completed") throw new InvalidOperationException("Fixture scan did not complete.");
            var candidates = fixture.Catalog.Candidates(fixture.Source.Id, null, 0, 10).Items;
            var selected = candidates.Single(item => item.RelativePath == "قطعه 01.wav");
            fixture.Pending = candidates.Single(item => item.RelativePath == "قطعه 02.wav");
            fixture.Catalog.SetPublished([selected.Id], true);
            fixture.Published = fixture.Catalog.Find(selected.Id)!;
            fixture.Profiles.Favorite(selected.Id, true);
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    private static void WriteSilentWave(string path)
    {
        // Generate 100 ms of mono PCM silence; no media/tool from the device is used.
        const int sampleRate = 8000, dataBytes = sampleRate / 10 * sizeof(short);
        using var output = new BinaryWriter(File.Create(path));
        output.Write("RIFF"u8); output.Write(36 + dataBytes); output.Write("WAVEfmt "u8);
        output.Write(16); output.Write((short)1); output.Write((short)1); output.Write(sampleRate);
        output.Write(sampleRate * sizeof(short)); output.Write((short)sizeof(short)); output.Write((short)16);
        output.Write("data"u8); output.Write(dataBytes); output.Write(new byte[dataBytes]);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Workspace.Dispose();
    }
}
