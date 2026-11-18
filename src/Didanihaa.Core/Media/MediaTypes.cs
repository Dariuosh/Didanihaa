namespace Didanihaa.Core;

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
