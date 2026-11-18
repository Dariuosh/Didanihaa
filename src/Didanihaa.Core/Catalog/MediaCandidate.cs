namespace Didanihaa.Core;

public sealed record MediaCandidate(string Id, string SourceId, string RelativePath, string Title, string Kind,
    string MimeType, long SizeBytes, string ModifiedUtc, bool Published, bool Available, MediaMetadata Metadata);
