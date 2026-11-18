using System.Text.Json;
using Didanihaa.Core;
using Didanihaa.Infrastructure;
using Didanihaa.Platform.Windows;
using Didanihaa.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Didanihaa.Tests;

public sealed class CatalogTests : IDisposable
{
    private readonly TestWorkspace workspace;
    private readonly string root;
    private readonly RuntimeLayout layout;
    private readonly CatalogRepository repository;
    private readonly string mediaPath;

    public CatalogTests()
    {
        workspace = new();
        root = workspace.Root;
        mediaPath = Path.Combine(root, "Media");
        Directory.CreateDirectory(mediaPath);
        layout = new(root, new RuntimeConfiguration());
        File.WriteAllText(Path.Combine(root, "didanihaa.json"), JsonSerializer.Serialize(layout.Configuration));
        repository = new(layout);
        repository.Initialize();
    }

    private async Task<SourceFolder> AddAndScanAsync(string name = "کتابخانه")
    {
        var source = repository.AddSource(name, mediaPath);
        await ScanAsync(source.Id);
        return source;
    }

    private async Task ScanAsync(string sourceId)
    {
        var job = repository.QueueScan(sourceId);
        Assert.True(repository.ClaimScan(job.Id));
        await new LibraryScanner(repository, new MetadataProbe(layout)).RunAsync(job, CancellationToken.None);
        Assert.Equal("completed", repository.Jobs().Single(item => item.Id == job.Id).Status);
    }

    [Fact]
    public async Task ScanRequiresExplicitPublication()
    {
        File.WriteAllBytes(Path.Combine(mediaPath, "آهنگ.mp3"), [1, 2, 3]);
        await AddAndScanAsync();
        var item = Assert.Single(repository.Candidates(null, null, 0, 50).Items);
        Assert.False(item.Published);
        Assert.Empty(repository.Candidates(null, null, 0, 50, true).Items);
        Assert.Null(repository.Find(item.Id, true));
        repository.SetPublished([item.Id], true);
        Assert.Single(repository.Candidates(null, null, 0, 50, true).Items);
        repository.SetPublished([item.Id], false);
        Assert.Null(repository.Find(item.Id, true));
    }

    [Fact]
    public async Task UnchangedRescanPreservesIdentityAndPublication()
    {
        File.WriteAllBytes(Path.Combine(mediaPath, "film.mp4"), [1, 2, 3]);
        var source = await AddAndScanAsync();
        var first = Assert.Single(repository.Candidates(null, null, 0, 50).Items);
        repository.SetPublished([first.Id], true);
        await ScanAsync(source.Id);
        var second = Assert.Single(repository.Candidates(null, null, 0, 50).Items);
        Assert.Equal(first.Id, second.Id); Assert.True(second.Published);
    }

    [Fact]
    public async Task ChangedFileReturnsToReview()
    {
        var file = Path.Combine(mediaPath, "film.mp4"); File.WriteAllBytes(file, [1, 2, 3]);
        var source = await AddAndScanAsync();
        var item = Assert.Single(repository.Candidates(null, null, 0, 50).Items);
        repository.SetPublished([item.Id], true);
        File.WriteAllBytes(file, [1, 2, 3, 4]);
        await ScanAsync(source.Id);
        Assert.False(repository.Find(item.Id)!.Published);
        Assert.Null(repository.Find(item.Id, true));
    }

    [Fact]
    public async Task MissingFileIsUnavailableAndCannotBeRepublished()
    {
        var file = Path.Combine(mediaPath, "film.mp4"); File.WriteAllBytes(file, [1]);
        var source = await AddAndScanAsync();
        var item = Assert.Single(repository.Candidates(null, null, 0, 50).Items);
        repository.SetPublished([item.Id], true); File.Delete(file);
        await ScanAsync(source.Id);
        Assert.False(repository.Find(item.Id)!.Available);
        Assert.Null(repository.Find(item.Id, true));
        Assert.Equal(0, repository.SetPublished([item.Id], true));
        Assert.Equal(1, repository.Statistics().Unavailable);
    }

    [Fact]
    public async Task FailedSourceScanPreservesPreviouslyKnownItems()
    {
        var file = Path.Combine(mediaPath, "film.mp4"); File.WriteAllBytes(file, [1]);
        var source = await AddAndScanAsync();
        var item = Assert.Single(repository.Candidates(null, null, 0, 50).Items);
        repository.SetPublished([item.Id], true);
        var moved = Path.Combine(root, "Offline");
        Assert.True(PathSafety.IsWithin(root, moved)); Assert.True(PathSafety.IsWithin(root, mediaPath));
        Directory.Move(mediaPath, moved);
        var job = repository.QueueScan(source.Id); Assert.True(repository.ClaimScan(job.Id));
        await new LibraryScanner(repository, new MetadataProbe(layout)).RunAsync(job, CancellationToken.None);
        Assert.Equal("failed", repository.Jobs().Single(candidate => candidate.Id == job.Id).Status);
        Assert.True(repository.Find(item.Id)!.Published);
    }

    [Fact]
    public async Task PersianSearchNormalizesArabicLetters()
    {
        File.WriteAllBytes(Path.Combine(mediaPath, "كتاب زندگي.mp3"), [1]);
        await AddAndScanAsync();
        Assert.Single(repository.Candidates(null, "کتاب زندگی", 0, 50).Items);
    }

    [Fact]
    public void SourcesInsideRootAreStoredRelativelyAndCannotOverlap()
    {
        var source = repository.AddSource("media", mediaPath);
        Assert.Equal("Media", source.StoredPath);
        var child = Path.Combine(mediaPath, "child"); Directory.CreateDirectory(child);
        Assert.Throws<ArgumentException>(() => repository.AddSource("duplicate", child));
    }

    [Fact]
    public void RuntimeInternalsCannotBeAddedAsMedia()
    {
        Assert.Throws<ArgumentException>(() => repository.AddSource("root", root));
        Assert.Throws<ArgumentException>(() => repository.AddSource("data", layout.DataPath));
    }

    [Fact]
    public void DuplicateScanRequestsShareOneJobAndRecoveryDoesNotPublish()
    {
        var source = repository.AddSource("source", mediaPath);
        var first = repository.QueueScan(source.Id);
        Assert.Equal(first.Id, repository.QueueScan(source.Id).Id);
        Assert.True(repository.ClaimScan(first.Id));
        Assert.False(repository.ClaimScan(first.Id));
        repository.RecoverInterruptedScans();
        Assert.Equal("interrupted", Assert.Single(repository.Jobs()).Status);
        Assert.NotEqual(first.Id, repository.QueueScan(source.Id).Id);
    }

    [Fact]
    public void TraversalAndPrefixSiblingsAreRejected()
    {
        Assert.Throws<IOException>(() => PathSafety.ResolveMedia(mediaPath, "../outside.mp4"));
        Assert.Throws<IOException>(() => PathSafety.ResolveMedia(mediaPath, Path.Combine(root, "outside.mp4")));
        Assert.False(PathSafety.IsWithin(mediaPath, mediaPath + "-other/file.mp4"));
        Assert.Throws<ArgumentException>(() => layout.ResolveOwnedPath("../outside"));
    }

    [Fact]
    public void RuntimeCreatesLocalConfigurationFromPortableExampleWhenAbsent()
    {
        File.Delete(Path.Combine(root, "didanihaa.json"));
        File.WriteAllText(Path.Combine(root, "didanihaa.example.json"), JsonSerializer.Serialize(layout.Configuration));
        var loaded = RuntimeLayout.Load(["--root", root]);
        Assert.Equal(root, loaded.Root);
        Assert.Equal(layout.Configuration.PublicPort, loaded.Configuration.PublicPort);
        Assert.True(File.Exists(Path.Combine(root, "didanihaa.json")));
    }

    [Fact]
    public void ProbeSettingsAcceptToolDirectoryAndPersistRelativePath()
    {
        var tools = Path.Combine(root, "tools"); Directory.CreateDirectory(tools);
        File.WriteAllBytes(Path.Combine(tools, "ffprobe.exe"), []);
        layout.SaveProbePath(tools);
        Assert.False(Path.IsPathRooted(layout.Configuration.FfprobePath));
        Assert.Equal(Path.Combine(tools, "ffprobe.exe"), layout.FfprobePath);
        Assert.Equal(layout.Configuration.FfprobePath, RuntimeLayout.Load(["--root", root]).Configuration.FfprobePath);
        Assert.Throws<ArgumentException>(() => layout.SaveProbePath(Path.Combine(tools, "missing.exe")));
    }

    [Fact]
    public void ProbeParsesDurationStreamsAndLanguageWithoutConverting()
    {
        var result = MetadataProbe.Parse("""
            {"format":{"duration":"123.5","tags":{"title":"فیلم"}},"streams":[
            {"codec_type":"video","codec_name":"h264","width":1920,"height":1080},
            {"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"fas"}}]}
            """);
        Assert.Equal(123.5, result.DurationSeconds); Assert.Equal("فیلم", result.Title);
        Assert.Equal(1920, result.Streams[0].Width); Assert.Equal("fas", result.Streams[1].Language);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    public void InvalidProbeDurationIsIgnored(string duration) =>
        Assert.Null(MetadataProbe.Parse($"{{\"format\":{{\"duration\":\"{duration}\"}}}}").DurationSeconds);

    [Fact]
    public async Task StatusAndKindFiltersApplyBeforePagination()
    {
        File.WriteAllBytes(Path.Combine(mediaPath, "audio.mp3"), [1]);
        File.WriteAllBytes(Path.Combine(mediaPath, "video.mp4"), [1]);
        await AddAndScanAsync();
        var audio = repository.Candidates(null, null, 0, 50, kind: "audio").Items.Single();
        repository.SetPublished([audio.Id], true);
        var review = repository.Candidates(null, null, 0, 1, state: "review");
        Assert.Equal(1, review.Total); Assert.Equal("video", Assert.Single(review.Items).Kind);
        Assert.Equal(audio.Id, Assert.Single(repository.Candidates(null, null, 0, 1, state: "published").Items).Id);
        Assert.Empty(repository.Candidates(null, null, 0, 50, kind: "video", state: "published").Items);
        Assert.Throws<ArgumentException>(() => repository.Candidates(null, null, 0, 50, state: "invalid"));
    }

    [Fact]
    public void WindowsArgumentsQuoteSpacesAndTrailingSlashes()
    {
        Assert.Equal("\"E:\\My Media\"", WindowsServiceManager.Quote("E:\\My Media"));
        Assert.Equal("\"E:\\Media\\\\\"", WindowsServiceManager.Quote("E:\\Media\\"));
    }

    [Fact]
    public async Task FolderPlaylistsAreDirectPrivateAndNaturallyOrdered()
    {
        Directory.CreateDirectory(Path.Combine(mediaPath, "Season 2", "Extras"));
        Directory.CreateDirectory(Path.Combine(mediaPath, "Private"));
        foreach (var name in new[] { "Episode 10.mp4", "Episode 2.mp4", "Episode 1.mp4" })
            File.WriteAllBytes(Path.Combine(mediaPath, "Season 2", name), [1]);
        File.WriteAllBytes(Path.Combine(mediaPath, "Season 2", "Extras", "secret.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(mediaPath, "Private", "private.mp4"), [1]);
        var source = await AddAndScanAsync();
        Assert.Empty(repository.Folders(null, "", true).Folders);
        var roots = repository.Folders(source.Id, "");
        Assert.Equal(2, roots.Folders.Count);
        var season = Assert.Single(roots.Folders, item => item.Name == "Season 2");
        Assert.Equal(4, season.TotalFiles); Assert.Equal(3, season.DirectFiles);
        Assert.Equal(3, repository.SetFolderPublished(source.Id, "Season 2", true));
        var playlist = repository.Candidates(source.Id, null, 0, 100, true, folder: "Season 2");
        Assert.Equal(new[] { "Episode 1", "Episode 2", "Episode 10" }, playlist.Items.Select(item => item.Title));
        Assert.Equal("Episode 2", Assert.Single(repository.Candidates(source.Id, null, 1, 1, true, folder: "Season 2").Items).Title);
        Assert.Equal(3, repository.Folders(source.Id, "Season 2", true).DirectPublishedFiles);
        Assert.Empty(repository.Folders(source.Id, "Season 2", true).Folders);
        Assert.Equal("Season 2", Assert.Single(repository.Folders(source.Id, "", true).Folders).Name);
        Assert.Equal(3, repository.SetFolderPublished(source.Id, "Season 2", false));
        Assert.Empty(repository.Folders(null, "", true).Folders);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/")]
    [InlineData("C:/Media")]
    [InlineData("season/../private")]
    [InlineData("season//private")]
    public void FolderTraversalAndGlobalPublicationAreRejected(string folder)
    {
        Assert.Throws<ArgumentException>(() => repository.Folders(null, folder));
        Assert.Throws<ArgumentException>(() => repository.SetFolderPublished("source", folder, true));
    }

    [Fact]
    public void FolderPublicationRequiresExplicitScope()
    {
        Assert.Throws<ArgumentNullException>(() => repository.SetFolderPublished("source", null!, true));
        Assert.Throws<ArgumentException>(() => repository.SetFolderPublished("", "", true));
        Assert.Throws<ArgumentException>(() => repository.Candidates(null, null, 0, 10, folder: ""));
    }

    [Fact]
    public async Task SharedProfilePersistsAcrossClientsAndRejectsPrivateFavorites()
    {
        File.WriteAllBytes(Path.Combine(mediaPath, "public.mp3"), [1]);
        File.WriteAllBytes(Path.Combine(mediaPath, "private.mp3"), [1]);
        await AddAndScanAsync();
        var items = repository.Candidates(null, null, 0, 10).Items;
        var published = Assert.Single(items, item => item.Title == "public"); var unpublished = Assert.Single(items, item => item.Title == "private");
        repository.SetPublished([published.Id], true);
        var firstClient = new ProfileRepository(layout); var secondClient = new ProfileRepository(layout);
        firstClient.Favorite(unpublished.Id, true); Assert.Empty(firstClient.Favorites());
        firstClient.Favorite(published.Id, true); firstClient.Favorite(published.Id, true);
        Assert.Equal(published.Id, Assert.Single(secondClient.Favorites()));
        firstClient.Update("خانهٔ ما", false, true);
        Assert.Equal(new HomeProfile("home", "خانهٔ ما", false, true), secondClient.Read());
        repository.SetPublished([published.Id], false);
        Assert.Empty(firstClient.Favorites(true));
        Assert.Equal(published.Id, Assert.Single(firstClient.Favorites()));
        Assert.Empty(repository.Candidates(null, null, 0, 10, true, ids: firstClient.Favorites()).Items);
        secondClient.Favorite(published.Id, false); Assert.Empty(firstClient.Favorites());
    }

    [Fact]
    public async Task VersionOneMigrationPreservesExistingCatalogAndPublication()
    {
        File.WriteAllBytes(Path.Combine(mediaPath, "kept.mp3"), [1]); await AddAndScanAsync();
        var item = Assert.Single(repository.Candidates(null, null, 0, 10).Items); repository.SetPublished([item.Id], true);
        using (var connection = new SqliteConnection($"Data Source={layout.DatabasePath}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE media_plays; DROP TABLE profile_favorites; DROP TABLE profiles; PRAGMA user_version=1;"; command.ExecuteNonQuery();
        }
        repository.Initialize(); repository.Initialize();
        Assert.Equal(item.Id, Assert.Single(repository.Candidates(null, null, 0, 10, true).Items).Id);
        Assert.Equal("خانه", new ProfileRepository(layout).Read().Name);
        Assert.Single(repository.Sources());
    }

    [Fact]
    public async Task FavoritePlaybackLocatesTheOriginalFolderAndNaturalPosition()
    {
        var directory = Path.Combine(mediaPath, "Season"); Directory.CreateDirectory(directory);
        foreach (var number in new[] { 1, 2, 10 }) File.WriteAllBytes(Path.Combine(directory, $"Episode {number}.mp4"), [1]);
        var source = await AddAndScanAsync(); repository.SetFolderPublished(source.Id, "Season", true);
        var second = Assert.Single(repository.Candidates(source.Id, "Episode 2", 0, 10).Items);
        var location = repository.LocatePublished(second.Id);
        Assert.Equal(new MediaLocation(source.Id, "Season", "Season", 1, 3), location);
        Assert.Equal(second.Id, Assert.Single(repository.Candidates(null, null, 0, 10, true, ids: [second.Id]).Items).Id);
        Assert.Empty(repository.Candidates(null, null, 0, 10, true, ids: []).Items);
        repository.SetPublished([second.Id], false); Assert.Null(repository.LocatePublished(second.Id));
    }

    [Fact]
    public async Task CachedThumbnailsRespectPublicationAndFileChanges()
    {
        var original = Path.Combine(mediaPath, "preview.mp4"); File.WriteAllBytes(original, [1]); await AddAndScanAsync();
        var item = Assert.Single(repository.Candidates(null, null, 0, 10).Items);
        var directory = Path.Combine(layout.TempPath, "thumbnails"); Directory.CreateDirectory(directory);
        var cached = Path.Combine(directory, ThumbnailService.CacheKey(item) + ".jpg"); File.WriteAllBytes(cached, [255, 216, 255, 217]);
        using var thumbnails = new ThumbnailService(layout, repository);
        Assert.Null(await thumbnails.GetAsync(item.Id, true, CancellationToken.None));
        Assert.Equal(cached, await thumbnails.GetAsync(item.Id, false, CancellationToken.None));
        repository.SetPublished([item.Id], true); Assert.Equal(cached, await thumbnails.GetAsync(item.Id, true, CancellationToken.None));
        File.WriteAllBytes(original, [1, 2]); Assert.Null(await thumbnails.GetAsync(item.Id, true, CancellationToken.None));
        Assert.NotEqual(ThumbnailService.CacheKey(item), ThumbnailService.CacheKey(item with { SizeBytes = 2 }));
        Assert.Equal(ThumbnailService.CacheKey(item), ThumbnailService.CacheKey(item with { Title = "renamed" }));
    }

    [Fact]
    public async Task VersionTwoMigrationPreservesProfileFavoritesAndPublication()
    {
        File.WriteAllBytes(Path.Combine(mediaPath, "kept.mp3"), [1]); await AddAndScanAsync();
        var item = Assert.Single(repository.Candidates(null, null, 0, 10).Items); repository.SetPublished([item.Id], true);
        var profiles = new ProfileRepository(layout); profiles.Update("خانهٔ قبلی", false, true); profiles.Favorite(item.Id, true);
        using (var connection = new SqliteConnection($"Data Source={layout.DatabasePath}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE media_plays; ALTER TABLE profiles DROP COLUMN nav_list_limit; PRAGMA user_version=2;"; command.ExecuteNonQuery();
        }
        // The previous application would be a separate process, without cached v3 schema statements.
        SqliteConnection.ClearAllPools();
        repository.Initialize(); repository.Initialize();
        Assert.Equal(new HomeProfile("home", "خانهٔ قبلی", false, true, 5), profiles.Read());
        Assert.Equal(item.Id, Assert.Single(profiles.Favorites(true)));
        Assert.Equal(item.Id, Assert.Single(repository.Candidates(null, null, 0, 10, true).Items).Id);
    }

    [Fact]
    public async Task PlaylistsAreDirectPublishedFilteredPagedAndRankedByRecordedPlayback()
    {
        var names = new[] { "Season 1", "Season 2", "Season 3", "Season 4", "Season 5", "Season 10", "Private" };
        foreach (var name in names)
        {
            var directory = Path.Combine(mediaPath, name); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "clip.mp4"), [1]); File.WriteAllBytes(Path.Combine(directory, "song.wav"), [1]);
        }
        var source = await AddAndScanAsync(); foreach (var name in names.Where(name => name != "Private")) repository.SetFolderPublished(source.Id, name, true);
        var profiles = new ProfileRepository(layout);
        Assert.Empty(repository.Playlists("video", "popular").Items);
        Assert.Equal(names.Take(5), repository.Playlists("video", limit: 5).Items.Select(item => item.Name));
        Assert.Equal("Season 10", Assert.Single(repository.Playlists("video", offset: 5, limit: 5).Items).Name);
        Assert.Equal("Season 2", Assert.Single(repository.Playlists("video", search: "Season 2").Items).Name);
        var privateItem = Assert.Single(repository.Candidates(source.Id, null, 0, 10, kind: "video", folder: "Private").Items);
        Assert.False(profiles.RecordPlayback(privateItem.Id));
        var most = Assert.Single(repository.Candidates(source.Id, null, 0, 10, kind: "video", folder: "Season 10").Items);
        var second = Assert.Single(repository.Candidates(source.Id, null, 0, 10, kind: "video", folder: "Season 2").Items);
        Assert.True(profiles.RecordPlayback(most.Id)); Assert.True(profiles.RecordPlayback(most.Id)); Assert.True(profiles.RecordPlayback(second.Id));
        Assert.Equal(new[] { "Season 10", "Season 2" }, repository.Playlists("video", "popular").Items.Select(item => item.Name));
        Assert.Empty(repository.Playlists("audio", "popular").Items);
        Assert.All(repository.Playlists("video").Items, item => Assert.Equal(1, item.MediaCount));
        profiles.Favorite(second.Id, true); Assert.Equal("Season 2", Assert.Single(repository.Playlists("video", favoritesOnly: true).Items).Name);
        repository.SetPublished([most.Id], false); Assert.Equal("Season 2", Assert.Single(repository.Playlists("video", "popular").Items).Name);
        Assert.False(profiles.RecordPlayback(most.Id));
        profiles.Update("خانه", true, false, 7); Assert.Equal(7, new ProfileRepository(layout).Read().NavListLimit);
        profiles.Update("نام جدید", false, true); Assert.Equal(7, profiles.Read().NavListLimit);
        Assert.Throws<ArgumentException>(() => profiles.Update("خانه", true, false, 21));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        workspace.Dispose();
    }
}
