namespace Didanihaa.Core;

public sealed record MediaMetadata(string Status, double? DurationSeconds, string? Title, IReadOnlyList<MediaStreamInfo> Streams, string? Error = null)
{
    public static MediaMetadata Basic => new("basic", null, null, []);
}
