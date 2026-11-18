using System.Text.Json;

namespace Didanihaa.Core;

public sealed record RuntimeConfiguration
{
    public int PublicPort { get; init; } = 13521;
    public int MediaPort { get; init; } = 13522;
    public string DataDirectory { get; init; } = "data";
    public string TempDirectory { get; init; } = "temp/runtime";
    public string FfprobePath { get; init; } = "tools/ffprobe.exe";
    public string HostExecutable { get; init; } = "publish/apps/host/Didanihaa.Host.exe";
    public string WebDirectory { get; init; } = "src/Didanihaa.WebUI/dist";
}

public sealed class RuntimeLayout(string root, RuntimeConfiguration configuration)
{
    public string Root { get; } = Path.GetFullPath(root);
    public RuntimeConfiguration Configuration { get; private set; } = configuration;
    public string DataPath => ResolveOwnedPath(Configuration.DataDirectory);
    public string DatabasePath => Path.Combine(DataPath, "catalog.db");
    public string TempPath => ResolveOwnedPath(Configuration.TempDirectory);
    public string FfprobePath => Path.IsPathFullyQualified(Configuration.FfprobePath)
        ? Path.GetFullPath(Configuration.FfprobePath) : ResolveOwnedPath(Configuration.FfprobePath);
    public string HostExecutable => ResolveOwnedPath(Configuration.HostExecutable);
    public string WebPath => ResolveOwnedPath(Configuration.WebDirectory);

    public static RuntimeLayout Load(string[] args)
    {
        var rootArgument = Argument(args, "--root");
        var root = rootArgument is null ? FindRoot(AppContext.BaseDirectory) ?? FindRoot(Environment.CurrentDirectory)
            ?? throw new InvalidOperationException("didanihaa.json پیدا نشد؛ --root را مشخص کنید.") : Path.GetFullPath(rootArgument);
        var configurationFile = Path.Combine(root, "didanihaa.json");
        if (!File.Exists(configurationFile)) configurationFile = Path.Combine(root, "didanihaa.example.json");
        var configuration = JsonSerializer.Deserialize<RuntimeConfiguration>(File.ReadAllText(configurationFile),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("تنظیمات معتبر نیست.");
        if (configuration.PublicPort is < 1024 or > 65535 || configuration.MediaPort is < 1024 or > 65535
            || configuration.PublicPort == configuration.MediaPort)
            throw new InvalidOperationException("پورت شبکه و پورت داخلی پخش باید متفاوت و در بازه 1024..65535 باشند.");
        var layout = new RuntimeLayout(root, configuration);
        _ = layout.DataPath; _ = layout.TempPath; _ = layout.FfprobePath; _ = layout.HostExecutable; _ = layout.WebPath;
        return layout;
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
        if (Path.IsPathFullyQualified(relativePath)) throw new ArgumentException("مسیرهای اجرایی باید نسبت به ریشه باشند.");
        var result = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!PathSafety.IsWithin(Root, result) || PathSafety.SamePath(Root, result))
            throw new ArgumentException("مسیر اجرایی باید داخل ریشه باشد.");
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
            var updated = Configuration with { FfprobePath = StoreSource(path) };
            var destination = Path.Combine(Root, "didanihaa.json");
            var temporary = destination + ".new";
            File.WriteAllText(temporary, JsonSerializer.Serialize(updated, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            File.Move(temporary, destination, true);
            Configuration = updated;
        }
    }

    private static string? FindRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "didanihaa.json")) || File.Exists(Path.Combine(directory.FullName, "didanihaa.example.json"))) return directory.FullName;
        return null;
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

    public static string ResolveMedia(string sourceRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new IOException("مسیر رسانه باید نسبی باشد.");
        var resolved = Path.GetFullPath(Path.Combine(sourceRoot, relativePath));
        if (!IsWithin(sourceRoot, resolved) || SamePath(sourceRoot, resolved)) throw new IOException("مسیر رسانه خارج از منبع است.");
        RejectReparsePoints(resolved);
        return resolved;
    }
}
