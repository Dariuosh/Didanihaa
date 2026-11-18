using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;
using Didanihaa.Core;

namespace Didanihaa.Infrastructure;

public sealed record MediaToolStatus(string State, string Source, string? Version = null)
{
    public bool Available => State == "ready";
}

public sealed class MediaToolCatalog(RuntimeLayout layout)
{
    private readonly ToolCheck probe = new("ffprobe");
    private readonly ToolCheck encoder = new("ffmpeg");

    public Task<(string? Path, MediaToolStatus Status)> ProbeAsync(CancellationToken cancellationToken) =>
        probe.ReadAsync(layout, cancellationToken);

    public Task<(string? Path, MediaToolStatus Status)> EncoderAsync(CancellationToken cancellationToken) =>
        encoder.ReadAsync(layout, cancellationToken);

    private sealed class ToolCheck(string name)
    {
        private readonly SemaphoreSlim gate = new(1);
        private (string Path, long Length, DateTime LastWrite) fingerprint;
        private MediaToolStatus? previous;
        private DateTime expires;

        public async Task<(string? Path, MediaToolStatus Status)> ReadAsync(RuntimeLayout layout, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            var source = "configured";
            try
            {
                var location = layout.MediaToolPath(name + ".exe");
                source = location.Source;
                PathSafety.RejectExistingReparsePoints(location.Path);
                var file = new FileInfo(location.Path);
                if (!file.Exists)
                {
                    previous = null;
                    return (null, new("missing", source));
                }
                var current = (location.Path, file.Length, file.LastWriteTimeUtc);
                if (previous is null || fingerprint != current || previous.Source != source || expires <= DateTime.UtcNow)
                {
                    previous = await CheckVersionAsync(location.Path, name, source, cancellationToken);
                    fingerprint = current;
                    expires = DateTime.UtcNow.AddMinutes(previous.Available ? 10 : 1);
                }
                return (previous.Available ? location.Path : null, previous);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                previous = null;
                return (null, new("invalid", source));
            }
            finally { gate.Release(); }
        }
    }

    internal static async Task<MediaToolStatus> CheckVersionAsync(
        string path, string name, string source, CancellationToken cancellationToken, TimeSpan? limit = null)
    {
        // Reject non-program files before Windows tries compatibility handling.
        try
        {
            using var image = new PEReader(File.OpenRead(path));
            if (image.PEHeaders.PEHeader is null || (image.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) != 0)
                return new("invalid", source);
        }
        catch (Exception error) when (error is BadImageFormatException or IOException or UnauthorizedAccessException)
        { return new("invalid", source); }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit ?? TimeSpan.FromSeconds(3));
        using var process = new Process
        {
            StartInfo = new(path)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(path)!
            }
        };
        process.StartInfo.ArgumentList.Add("-version");
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            process.Start();
            var output = CaptureAsync(process.StandardOutput, timeout.Token);
            var error = CaptureAsync(process.StandardError, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, error).WaitAsync(timeout.Token);
            var version = FindVersion(await output, name) ?? FindVersion(await error, name);
            return process.ExitCode == 0 && version is not null ? new("ready", source, version) : new("invalid", source);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new("timeout", source); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return new("invalid", source); }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await process.WaitForExitAsync(cleanup.Token);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
        }
    }

    private static async Task<string> CaptureAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        const int captureLimit = 4096;
        var captured = new StringBuilder();
        var buffer = new char[1024];
        int length;
        while ((length = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            captured.Append(buffer, 0, Math.Min(length, captureLimit - captured.Length));
        return captured.ToString();
    }

    private static string? FindVersion(string output, string name)
    {
        var prefix = name + " version ";
        foreach (var line in output.Split('\n'))
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var version = line[prefix.Length..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (version is { Length: > 0 and <= 128 } && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+'))
                return version;
        }
        return null;
    }
}
