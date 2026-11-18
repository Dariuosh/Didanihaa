using System.Text;

namespace Didanihaa.Core;

public sealed class LauncherLog : IDisposable
{
    private readonly TextWriter originalOut = Console.Out;
    private readonly TextWriter originalError = Console.Error;
    private readonly TextWriter writer;

    private LauncherLog(string path)
    {
        PathSafety.RejectExistingReparsePoints(path);
        writer = TextWriter.Synchronized(new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true });
        Console.SetOut(writer);
        Console.SetError(writer);
        writer.WriteLine($"--- {DateTimeOffset.UtcNow:O} PID {Environment.ProcessId} ---");
    }

    public static LauncherLog? Open(string[] args, RuntimeLayout layout, RuntimeRole role)
    {
        if (!args.Contains("--launcher-log", StringComparer.Ordinal)) return null;
        var path = Path.Combine(RuntimeSettings.StatePath(layout.Root), role.ToString().ToLowerInvariant() + ".launcher.log");
        return new LauncherLog(path);
    }

    public static async Task<int> RunBackendAsync(string[] args, RuntimeRole role, Func<Task<int>> run)
    {
        try { return await run(); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (args.Contains("--launcher-log", StringComparer.Ordinal))
            {
                try
                {
                    var root = RuntimeRoot.Resolve(args, AppContext.BaseDirectory, Environment.CurrentDirectory);
                    var directory = RuntimeSettings.StatePath(root);
                    PathSafety.RejectExistingReparsePoints(directory);
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, role.ToString().ToLowerInvariant() + ".startup-error.txt");
                    RuntimeFile.Write(path, error.ToString(), true);
                }
                catch (Exception logError) when (logError is IOException or UnauthorizedAccessException
                    or ArgumentException or InvalidOperationException)
                { Console.Error.WriteLine(logError.Message); }
            }
            return 1;
        }
    }

    public void Dispose()
    {
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        writer.Dispose();
    }
}
