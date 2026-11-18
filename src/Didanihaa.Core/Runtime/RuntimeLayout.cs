namespace Didanihaa.Core;

public sealed class RuntimeLayout(string root, RuntimeConfiguration configuration)
{
    public string Root { get; } = RuntimeRoot.Normalize(root);
    public RuntimeConfiguration Configuration { get; private set; } = configuration;
    public string DataPath => ResolveOwnedPath(Configuration.DataDirectory);
    public string DatabasePath => Path.Combine(DataPath, "catalog.db");
    public string TempPath => ResolveOwnedPath(Configuration.TempDirectory);
    public string FfprobePath => Path.IsPathFullyQualified(Configuration.FfprobePath)
        ? Path.GetFullPath(Configuration.FfprobePath) : ResolveOwnedPath(Configuration.FfprobePath);
    public string HostExecutable => ResolveOwnedPath(Configuration.HostExecutable);
    public string WebPath => ResolveOwnedPath(Configuration.WebDirectory);
    public string[] MediaExecutableCandidates => File.Exists(Path.Combine(Root, "Didanihaa.slnx"))
        ? [HostExecutable, ResolveOwnedPath("src/Didanihaa.Media/bin/Debug/net11.0/Didanihaa.Host.exe"),
            ResolveOwnedPath("src/Didanihaa.Media/bin/Release/net11.0/Didanihaa.Host.exe")]
        : [HostExecutable];

    public static RuntimeLayout Load(string[] args)
    {
        var root = RuntimeRoot.Resolve(args, AppContext.BaseDirectory, Environment.CurrentDirectory);
        return new(root, RuntimeSettings.Load(root));
    }

    internal void Validate()
    {
        if (Configuration.PublicPort is < 1024 or > 65535 || Configuration.MediaPort is < 1024 or > 65535
            || Configuration.PublicPort == Configuration.MediaPort)
            throw new InvalidOperationException("پورت شبکه و پورت داخلی پخش باید متفاوت و در بازه 1024..65535 باشند.");
        if (string.IsNullOrWhiteSpace(Configuration.FfprobePath)) throw new ArgumentException("مسیر ابزار مشخص نشده است.");
        _ = DataPath; _ = TempPath; _ = FfprobePath; _ = HostExecutable; _ = WebPath;
    }

    public static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 == args.Length) throw new ArgumentException($"مقدار {name} مشخص نشده است.");
        return args[index + 1];
    }

    public string ResolveOwnedPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("مسیر اجرایی مشخص نشده است.");
        if (Path.IsPathFullyQualified(relativePath)) throw new ArgumentException("مسیرهای اجرایی باید نسبت به ریشه باشند.");
        var result = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!PathSafety.IsWithin(Root, result) || PathSafety.SamePath(Root, result))
            throw new ArgumentException("مسیر اجرایی باید داخل ریشه باشد.");
        PathSafety.RejectExistingReparsePoints(result);
        return result;
    }

    public string ResolveSource(string storedPath) => Path.GetFullPath(Path.Combine(Root, storedPath));
    public string StoreSource(string absolutePath) => PathSafety.IsWithin(Root, absolutePath)
        ? Path.GetRelativePath(Root, absolutePath) : Path.GetFullPath(absolutePath);

    private readonly object settingsLock = new();
    public void SaveProbePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096) throw new ArgumentException("مسیر ابزار معتبر نیست.");
        path = Path.GetFullPath(Path.Combine(Root, path.Trim().Trim('"')));
        if (Directory.Exists(path)) path = Path.Combine(path, "ffprobe.exe");
        if (Path.GetFileName(path).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(Path.GetDirectoryName(path)!, "ffprobe.exe");
        if (!Path.GetFileName(path).Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new ArgumentException("ffprobe.exe در این مسیر پیدا نشد؛ پوشه bin یا خود فایل را مشخص کنید.");
        lock (settingsLock)
        {
            Configuration = RuntimeSettings.Update(Root, current => current with { FfprobePath = StoreSource(path) });
        }
    }

}

public static class PathSafety
{
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static bool SamePath(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), Comparison);

    public static bool IsWithin(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.GetFullPath(path);
        return SamePath(root, path) || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, Comparison);
    }

    public static void RejectReparsePoints(string path)
    {
        var full = Path.GetFullPath(path);
        var pathRoot = Path.GetPathRoot(full)!;
        var current = pathRoot;
        foreach (var segment in full[pathRoot.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("مسیر شامل پیوند یا reparse point است.");
        }
    }

    public static void RejectExistingReparsePoints(string path)
    {
        var full = Path.GetFullPath(path);
        var pathRoot = Path.GetPathRoot(full)!;
        var current = pathRoot;
        foreach (var segment in full[pathRoot.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("مسیر شامل پیوند یا reparse point است.");
        }
    }

    public static string ResolveMedia(string sourceRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new IOException("مسیر رسانه باید نسبی باشد.");
        var resolved = Path.GetFullPath(Path.Combine(sourceRoot, relativePath));
        if (!IsWithin(sourceRoot, resolved) || SamePath(sourceRoot, resolved)) throw new IOException("مسیر رسانه خارج از منبع است.");
        RejectReparsePoints(resolved);
        return resolved;
    }
}
