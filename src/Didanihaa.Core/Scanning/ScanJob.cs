namespace Didanihaa.Core;

public sealed record ScanJob(string Id, string SourceId, string Status, int Scanned, int Errors,
    string CreatedUtc, string? FinishedUtc, string? Message);
