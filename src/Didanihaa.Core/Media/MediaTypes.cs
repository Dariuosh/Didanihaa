using System.Text;

namespace Didanihaa.Core;

public sealed record SourceFolder(string Id, string Name, string StoredPath, string ResolvedPath, string CreatedUtc);
public sealed record MediaStreamInfo(string Kind, string? Codec, string? Language, int? Width, int? Height, int? Channels);
public sealed record MediaMetadata(string Status, double? DurationSeconds, string? Title, IReadOnlyList<MediaStreamInfo> Streams, string? Error = null)
{
    public static MediaMetadata Basic => new("basic", null, null, []);
}
public sealed record MediaCandidate(string Id, string SourceId, string RelativePath, string Title, string Kind,
    string MimeType, long SizeBytes, string ModifiedUtc, bool Published, bool Available, MediaMetadata Metadata);
public sealed record PublicMedia(string Id, string Title, string Kind, string MimeType, long SizeBytes,
    double? DurationSeconds, string StreamUrl, string? ThumbnailUrl = null);
public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit);
public sealed record MediaLocation(string SourceId, string Folder, string Name, int Index, int Total);
public sealed record ScanJob(string Id, string SourceId, string Status, int Scanned, int Errors,
    string CreatedUtc, string? FinishedUtc, string? Message);
public sealed record CatalogStatistics(int Sources, int Candidates, int Published, int Unavailable);
public sealed record ServiceState(bool Supported, bool Installed, string State, string? ExecutablePath = null);
public sealed record ServiceActionResult(bool Success, string Message);

public static class SearchText
{
    public static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC)
        .Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ').Trim().ToUpperInvariant();
}

public static class MediaTypes
{
    private static readonly Dictionary<string, (string Kind, string Mime)> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = ("video", "video/mp4"), [".m4v"] = ("video", "video/mp4"),
        [".mkv"] = ("video", "video/x-matroska"), [".webm"] = ("video", "video/webm"),
        [".avi"] = ("video", "video/x-msvideo"), [".mov"] = ("video", "video/quicktime"),
        [".ts"] = ("video", "video/mp2t"), [".m2ts"] = ("video", "video/mp2t"),
        [".mts"] = ("video", "video/mp2t"), [".mpg"] = ("video", "video/mpeg"),
        [".mpeg"] = ("video", "video/mpeg"), [".wmv"] = ("video", "video/x-ms-wmv"),
        [".3gp"] = ("video", "video/3gpp"), [".flv"] = ("video", "video/x-flv"),
        [".vob"] = ("video", "video/mpeg"),
        [".mp3"] = ("audio", "audio/mpeg"), [".m4a"] = ("audio", "audio/mp4"),
        [".aac"] = ("audio", "audio/aac"), [".flac"] = ("audio", "audio/flac"),
        [".wav"] = ("audio", "audio/wav"), [".ogg"] = ("audio", "audio/ogg"),
        [".opus"] = ("audio", "audio/ogg"), [".wma"] = ("audio", "audio/x-ms-wma"),
        [".aif"] = ("audio", "audio/aiff"), [".aiff"] = ("audio", "audio/aiff")
    };

    public static bool TryGet(string path, out string kind, out string mime)
    {
        if (Types.TryGetValue(Path.GetExtension(path), out var type))
        {
            (kind, mime) = type;
            return true;
        }
        kind = mime = "";
        return false;
    }
}
