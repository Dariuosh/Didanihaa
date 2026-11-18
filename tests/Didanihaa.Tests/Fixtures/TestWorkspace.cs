using Didanihaa.Core;

namespace Didanihaa.Tests.Fixtures;

internal sealed class TestWorkspace : IDisposable
{
    public static string EvaluationPath { get; } = Path.Combine(FindCheckout(), "temp", "automation", "setup-runtime");
    public static string ApplicationPath => Path.Combine(EvaluationPath, "app");
    private static string DataParent => Path.Combine(EvaluationPath, "data");
    private readonly string owner = Guid.NewGuid().ToString("N");
    private bool disposed;

    public string ContainerPath { get; }
    public string Root { get; }
    public string MarkerPath => Path.Combine(ContainerPath, ".didanihaa-test-owner");

    public TestWorkspace()
    {
        // Discover the source checkout by its solution, never by user configuration.
        CreateEvaluationDirectory(DataParent);
        CreateEvaluationDirectory(ApplicationPath);
        ContainerPath = Path.Combine(DataParent, owner);
        Root = Path.Combine(ContainerPath, "دیدنی‌های آزمایشی با فاصله");
        Directory.CreateDirectory(Root);
        File.WriteAllText(MarkerPath, owner);
    }

    private static string FindCheckout()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Didanihaa.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Tests must run beneath a Didanihaa source checkout.");
    }

    private static void CreateEvaluationDirectory(string path)
    {
        var ancestor = new DirectoryInfo(path);
        while (!ancestor.Exists) ancestor = ancestor.Parent ?? throw new InvalidOperationException("Evaluation parent is missing.");
        PathSafety.RejectReparsePoints(ancestor.FullName);
        Directory.CreateDirectory(path);
        PathSafety.RejectReparsePoints(path);
    }

    public void Dispose()
    {
        if (disposed) return;
        if (!PathSafety.IsWithin(DataParent, ContainerPath) || PathSafety.SamePath(DataParent, ContainerPath))
            throw new InvalidOperationException("Unsafe fixture cleanup target.");
        PathSafety.RejectReparsePoints(ContainerPath);
        // Inspect before recursing so a link cannot redirect fixture cleanup.
        var directories = new Stack<string>();
        directories.Push(ContainerPath);
        while (directories.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Fixture cleanup refuses reparse points.");
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(entry);
            }
        if (!File.Exists(MarkerPath) || File.ReadAllText(MarkerPath) != owner)
            throw new InvalidOperationException("Fixture cleanup requires its ownership marker.");
        Directory.Delete(ContainerPath, true);
        disposed = true;
    }
}
