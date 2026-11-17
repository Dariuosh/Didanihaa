using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Didanihaa.Core;

namespace Didanihaa.Infrastructure;

// Derived, disposable still images only. Playback always serves the untouched original file.
public sealed class ThumbnailService(RuntimeLayout layout, CatalogRepository repository) : IDisposable
{
    private readonly SemaphoreSlim generation = new(1);
    private readonly ConcurrentDictionary<string, DateTime> failures = new();
    public string ToolPath => Path.Combine(Path.GetDirectoryName(layout.FfprobePath)!, "ffmpeg.exe");
    public bool IsAvailable => File.Exists(ToolPath);
    public static string CacheKey(MediaCandidate item) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"v1\n{item.Id}\n{item.SizeBytes}\n{item.ModifiedUtc}"))).ToLowerInvariant();

    public async Task<string?> GetAsync(string id, bool publicOnly, CancellationToken cancellationToken)
    {
        var item = repository.Find(id, publicOnly);
        if (item is null || item.Kind != "video" || !item.Available) return null;
        var source = repository.Sources().FirstOrDefault(source => source.Id == item.SourceId);
        if (source is null) return null;
        string original;
        try
        {
            original = PathSafety.ResolveMedia(source.ResolvedPath, item.RelativePath);
            if (!Matches(original, item)) return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        var key = CacheKey(item);
        var directory = Path.Combine(layout.TempPath, "thumbnails");
        try { Directory.CreateDirectory(directory); PathSafety.RejectReparsePoints(directory); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        var destination = Path.Combine(directory, key + ".jpg");
        if (File.Exists(destination)) return destination;
        if (!IsAvailable || failures.TryGetValue(key, out var failed) && DateTime.UtcNow - failed < TimeSpan.FromMinutes(5)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var acquired = false; Process? process = null;
        var temporary = Path.Combine(directory, key + "." + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            await generation.WaitAsync(timeout.Token); acquired = true;
            if (File.Exists(destination)) return destination;
            if (!Matches(original, item)) return null;
            process = new Process { StartInfo = new ProcessStartInfo(ToolPath)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
            }};
            var seek = item.Metadata.DurationSeconds is > 0 ? Math.Min(item.Metadata.DurationSeconds.Value * .1, 30) : 0;
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-protocol_whitelist", "file,pipe",
                "-ss", seek.ToString("0.###", CultureInfo.InvariantCulture), "-i", original, "-map", "0:v:0", "-frames:v", "1",
                "-an", "-sn", "-dn", "-vf", "scale=640:-2", "-q:v", "4", "-threads", "1", "-update", "1", "-y", temporary })
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stderr, stdout);
            if (process.ExitCode != 0 || !File.Exists(temporary) || new FileInfo(temporary).Length is < 4 or > 2_000_000 || !Matches(original, item))
            { RememberFailure(key); return null; }
            // A rescan/publication change during extraction must not expose the old preview.
            var latest = repository.Find(id, publicOnly);
            if (latest is null || !latest.Available || CacheKey(latest) != key) return null;
            File.Move(temporary, destination, true);
            return destination;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { RememberFailure(key); return null; }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited) { process.Kill(true); using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await process.WaitForExitAsync(cleanup.Token); }
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
                process.Dispose();
            }
            if (acquired) generation.Release();
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private void RememberFailure(string key)
    { if (failures.Count > 2048) failures.Clear(); failures[key] = DateTime.UtcNow; }
    private static bool Matches(string file, MediaCandidate item) => new FileInfo(file) is { Exists: true } info
        && info.Length == item.SizeBytes && info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture) == item.ModifiedUtc;
    public void Dispose() => generation.Dispose();
}
