using System.ComponentModel;
using Didanihaa.Core;

namespace Didanihaa.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var showBrowser = !args.Contains("--no-browser", StringComparer.Ordinal);
        var root = AppContext.BaseDirectory;
        ConsoleShutdown? shutdown = null;
        try
        {
            shutdown = new ConsoleShutdown();
            Console.WriteLine($"Didanihaa | Launcher PID {Environment.ProcessId}");
            var layout = new RuntimeLayout(root, RuntimeSettings.Load(root));
            return new Startup(layout).RunAsync(showBrowser, args.Contains("--loopback-only", StringComparer.Ordinal), shutdown)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (shutdown?.Token.IsCancellationRequested == true) { return 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or Win32Exception or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(error is LauncherException ? error.Message : "Didanihaa encountered a runtime error.");
            try
            {
                var directory = RuntimeSettings.StatePath(root);
                PathSafety.RejectExistingReparsePoints(directory);
                Directory.CreateDirectory(directory);
                RuntimeFile.Write(Path.Combine(directory, "launcher-error.txt"), error.ToString(), true);
                Console.Error.WriteLine("Details: temp/runtime/launcher-error.txt");
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException or ArgumentException)
            { Console.Error.WriteLine("Could not write temp/runtime/launcher-error.txt. Check folder permissions."); }
            if (showBrowser && shutdown is not null && !shutdown.Token.IsCancellationRequested && !Console.IsInputRedirected)
            {
                Console.WriteLine("Press Enter to close.");
                var enter = Task.Run(Console.ReadLine);
                try { enter.Wait(shutdown.Token); } catch (OperationCanceledException) { }
            }
            return 1;
        }
        finally { shutdown?.Dispose(); }
    }
}
