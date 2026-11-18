using System.ComponentModel;
using System.Text;
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
            Console.OutputEncoding = Encoding.UTF8;
            shutdown = new ConsoleShutdown();
            var layout = new RuntimeLayout(root, RuntimeSettings.Load(root));
            return new Startup(layout).RunAsync(showBrowser, args.Contains("--loopback-only", StringComparer.Ordinal), shutdown)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (shutdown?.Token.IsCancellationRequested == true) { return 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or Win32Exception or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            try { RuntimeFile.Write(Path.Combine(RuntimeSettings.StatePath(root), "launcher-error.txt"), error.ToString(), true); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException or ArgumentException) { }
            Console.Error.WriteLine(error.Message);
            if (showBrowser && shutdown is not null && !shutdown.Token.IsCancellationRequested && !Console.IsInputRedirected)
            {
                Console.WriteLine("برای بستن پنجره Enter بزن.");
                var enter = Task.Run(Console.ReadLine);
                try { enter.Wait(shutdown.Token); } catch (OperationCanceledException) { }
            }
            return 1;
        }
        finally { shutdown?.Dispose(); }
    }
}
