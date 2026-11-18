namespace Didanihaa.Core;

public sealed class RuntimeLayout(string root, RuntimeConfiguration configuration)
{
    public string Root { get; } = RuntimeRoot.Normalize(root);
    public RuntimeConfiguration Configuration { get; private set; } = configuration;
    public string DataPath => ResolveOwnedPath(Configuration.DataDirectory);
    public string DatabasePath => Path.Combine(DataPath, "catalog.db");
    public string TempPath => ResolveOwnedPath(Configuration.TempDirectory);
    public string FfprobePath => MediaToolPath("ffprobe.exe").Path;
    public string MediaToolOverride => IsAutomaticToolPath(Configuration.FfprobePath) ? "" : Configuration.FfprobePath;
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
        _ = DataPath; _ = TempPath; _ = HostExecutable; _ = WebPath;
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
    public (string Path, string Source) MediaToolPath(string executable)
    {
        var configured = MediaToolOverride;
        if (configured.Length > 0)
        {
            var path = ResolveLocalToolPath(configured);
            var directory = IsToolExecutable(path) ? Path.GetDirectoryName(path)! : path;
            return (Path.Combine(directory, executable), "configured");
        }

        var bundled = Path.Combine(Root, "ffmpeg", "bin", executable);
        PathSafety.RejectExistingReparsePoints(bundled);
        if (File.Exists(bundled)) return (bundled, "bundled");
        var legacy = Path.Combine(Root, "tools", executable);
        PathSafety.RejectExistingReparsePoints(legacy);
        return File.Exists(legacy) ? (legacy, "legacy") : (bundled, "bundled");
    }

    public void SaveProbePath(string? path)
    {
        var stored = "";
        if (!string.IsNullOrWhiteSpace(path))
        {
            var absolute = ResolveLocalToolPath(path);
            // A folder or either of the two known executable names selects the pair.
            var directory = Directory.Exists(absolute) ? absolute
                : IsToolExecutable(absolute) ? Path.GetDirectoryName(absolute)!
                : throw new ArgumentException("پوشهٔ bin یا یکی از فایل‌های ffmpeg.exe و ffprobe.exe را وارد کن.");
            var probe = Path.Combine(directory, "ffprobe.exe");
            var encoder = Path.Combine(directory, "ffmpeg.exe");
            PathSafety.RejectExistingReparsePoints(directory);
            if (!File.Exists(probe) && !File.Exists(encoder))
                throw new ArgumentException("در این پوشه ffmpeg.exe یا ffprobe.exe پیدا نشد.");
            stored = StoreSource(probe);
            if (IsAutomaticToolPath(stored)) stored = Path.Combine(".", stored);
        }
        lock (settingsLock)
        {
            Configuration = RuntimeSettings.Update(Root, current => current with { FfprobePath = stored });
        }
    }

    private string ResolveLocalToolPath(string path)
    {
        path = path.Trim().Trim('"');
        if (path.Length is 0 or > 4096 || Path.IsPathRooted(path) && !Path.IsPathFullyQualified(path))
            throw new ArgumentException("مسیر ابزار معتبر نیست.");
        var absolute = Path.GetFullPath(path, Root);
        if (OperatingSystem.IsWindows() && absolute.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("مسیر ابزار باید روی همین دستگاه باشد.");
        if (!Path.IsPathFullyQualified(path) && !PathSafety.IsWithin(Root, absolute))
            throw new ArgumentException("مسیر نسبی ابزار باید داخل پوشهٔ دیدنی‌ها باشد.");
        PathSafety.RejectExistingReparsePoints(absolute);
        return absolute;
    }

    private static bool IsAutomaticToolPath(string? path) => string.IsNullOrWhiteSpace(path)
        || path.Replace('\\', '/').Equals("tools/ffprobe.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsToolExecutable(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase);
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
