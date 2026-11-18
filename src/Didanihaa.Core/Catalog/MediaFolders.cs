using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Didanihaa.Core;

public sealed record MediaFolder(string SourceId, string Path, string Name, int TotalFiles, int DirectFiles, int PublishedFiles);
public sealed record FolderListing(string? SourceId, string Path, string? SourceName, IReadOnlyList<MediaFolder> Folders,
    int DirectFiles, int DirectAvailableFiles, int DirectPublishedFiles);
public sealed record PublicPlaylist(string SourceId, string Folder, string Name, string SourceName, int MediaCount, long PlayCount, string? ThumbnailUrl);

public static partial class MediaFolders
{
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path = path.Replace('\\', '/');
        if (path.StartsWith('/') || path.IndexOfAny([':', '\0']) >= 0 || path.Split('/').Any(part => part is "." or "..") || path.Contains("//", StringComparison.Ordinal))
            throw new ArgumentException("مسیر پوشه باید داخل منبع انتخاب‌شده باشد.");
        return path.TrimEnd('/');
    }

    public static int CompareNames(string? first, string? second)
    {
        var left = NumberParts().Split(first ?? ""); var right = NumberParts().Split(second ?? "");
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var comparison = BigInteger.TryParse(left[index], NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber)
                && BigInteger.TryParse(right[index], NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber)
                ? leftNumber.CompareTo(rightNumber) : StringComparer.OrdinalIgnoreCase.Compare(left[index], right[index]);
            if (comparison != 0) return comparison;
        }
        var countComparison = left.Length.CompareTo(right.Length);
        return countComparison != 0 ? countComparison : StringComparer.OrdinalIgnoreCase.Compare(first, second);
    }

    [GeneratedRegex("([0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex NumberParts();
}
