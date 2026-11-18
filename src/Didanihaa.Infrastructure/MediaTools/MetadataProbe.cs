using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Didanihaa.Core;

namespace Didanihaa.Infrastructure;

public sealed class MetadataProbe(RuntimeLayout layout)
{
    public bool IsAvailable => File.Exists(layout.FfprobePath);

    public async Task<MediaMetadata> ReadAsync(string file, CancellationToken cancellationToken)
    {
        if (!IsAvailable) return MediaMetadata.Basic;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process { StartInfo = new ProcessStartInfo(layout.FfprobePath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        }};
        foreach (var argument in new[] { "-v", "error", "-protocol_whitelist", "file,pipe", "-show_entries",
                     "format=duration:format_tags=title:stream=codec_type,codec_name,width,height,channels:stream_tags=language", "-of", "json", file })
            process.StartInfo.ArgumentList.Add(argument);
        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0) return new("error", null, null, [], error[..Math.Min(error.Length, 1000)]);
            return Parse(output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new("error", null, null, [], "زمان خواندن metadata بیش از حد مجاز شد.");
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or JsonException)
        {
            return new("error", null, null, [], "خواندن metadata موفق نبود.");
        }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
        }
    }

    public static MediaMetadata Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        double? duration = null; string? title = null;
        if (root.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var value) && double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number) && number >= 0) duration = number;
            if (format.TryGetProperty("tags", out var tags)) title = String(tags, "title");
        }
        List<MediaStreamInfo> streams = [];
        if (root.TryGetProperty("streams", out var streamArray))
            foreach (var stream in streamArray.EnumerateArray())
                streams.Add(new(String(stream, "codec_type") ?? "unknown", String(stream, "codec_name"),
                    stream.TryGetProperty("tags", out var tags) ? String(tags, "language") : null,
                    Number(stream, "width"), Number(stream, "height"), Number(stream, "channels")));
        return new("complete", duration, title, streams);
    }

    private static string? String(JsonElement element, string key) => element.TryGetProperty(key, out var value) ? value.GetString() : null;
    private static int? Number(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.TryGetInt32(out var number) ? number : null;
}
