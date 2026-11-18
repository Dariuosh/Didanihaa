using System.Security.Cryptography;
using System.Text;

namespace Didanihaa.Core;

public static class RuntimeRoot
{
    public static string Normalize(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        PathSafety.RejectExistingReparsePoints(root);
        return root;
    }

    public static string Id(string root)
    {
        var canonical = Normalize(root);
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string Resolve(string[] args, string applicationDirectory, string workingDirectory)
    {
        var explicitRoot = RuntimeLayout.Argument(args, "--root");
        return Normalize(explicitRoot is not null ? Path.GetFullPath(explicitRoot, workingDirectory)
            : Find(applicationDirectory) ?? Find(workingDirectory)
                ?? throw new InvalidOperationException("تنظیمات دیدنی‌ها پیدا نشد؛ --root را مشخص کنید."));
    }

    private static string? Find(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "didanihaa.json"))
                || File.Exists(Path.Combine(directory.FullName, "didanihaa.example.json"))) return directory.FullName;
        return null;
    }
}
