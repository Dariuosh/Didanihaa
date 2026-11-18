namespace Didanihaa.Core;

public sealed record PublicMedia(string Id, string Title, string Kind, string MimeType, long SizeBytes,
    double? DurationSeconds, string StreamUrl, string? ThumbnailUrl = null);
