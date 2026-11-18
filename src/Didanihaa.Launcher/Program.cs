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
        try
        {
            var layout = new RuntimeLayout(root, RuntimeSettings.Load(root));
            return new Startup(layout).RunAsync(showBrowser, args.Contains("--loopback-only", StringComparer.Ordinal))
                .GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or Win32Exception or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (!showBrowser)
            {
                try { RuntimeFile.Write(Path.Combine(RuntimeSettings.StatePath(root), "launcher-error.txt"), error.ToString(), true); }
                catch (Exception logError) when (logError is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
            else
            {
                MessageBox.Show(error.Message, "دیدنی‌ها", MessageBoxButtons.OK, MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
            }
            return 1;
        }
    }
}
