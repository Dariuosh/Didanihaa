using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Didanihaa.Core;
using Didanihaa.Platform.Windows;

namespace Didanihaa.Launcher;

internal sealed class Startup(RuntimeLayout layout)
{
    private sealed record Session(RuntimeIdentity Runtime);
    private sealed record Backend(RuntimeRole Role, string Executable, int Port, string Endpoint)
    {
        public Uri GetUrl() => new($"http://127.0.0.1:{Port}/{Endpoint}");
    }

    public async Task<int> RunAsync(bool showBrowser, bool loopbackOnly, ConsoleShutdown shutdown)
    {
        var directory = RuntimeSettings.StatePath(layout.Root);
        using var controller = TryLock(Path.Combine(directory, "controller.lock"));
        if (controller is null)
        {
            Console.WriteLine("Didanihaa is already running from this folder.");
            return 0;
        }
        var control = new Backend(RuntimeRole.Control, layout.ResolveOwnedPath("apps/control/Didanihaa.Control.exe"),
            layout.Configuration.PublicPort, "admin/api/session");
        var media = new Backend(RuntimeRole.Media, layout.HostExecutable, layout.Configuration.MediaPort, "_internal/runtime");
        var admin = new Uri(control.GetUrl(), "/admin/");
        foreach (var (path, component) in new[]
        {
            (control.Executable, "Control executable"),
            (media.Executable, "Media executable"),
            (Path.Combine(layout.WebPath, "admin", "index.html"), "dashboard page")
        })
        {
            PathSafety.RejectExistingReparsePoints(path);
            if (!File.Exists(path)) throw new LauncherException($"Missing {component}. Extract the entire package.");
        }

        var ledger = new ProcessLedger(layout.Root);
        var records = new List<OwnedProcess>();
        using var job = new BackendJob();
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(1) };
        var token = shutdown.Token;
        try
        {
            using (var startupLock = RuntimeFile.Lock(Path.Combine(directory, "startup.lock"), TimeSpan.FromSeconds(2)))
            {
                Console.WriteLine("Starting...");
                records = ledger.Read();
                var existingControl = await DiscoverAsync(client, control, token);
                var service = new WindowsServiceManager(layout).ReadState();
                var ownsService = service is { Installed: true, Owned: true };
                var serviceActive = ownsService && service.State is ("running" or "starting");
                var existingMedia = await DiscoverAsync(client, media, token, serviceActive);
                ledger.Write(records);
                var controlIdentity = existingControl ?? await StartAsync(control);
                PrintReady(controlIdentity, job.Owns(RuntimeRole.Control), false);
                if (existingMedia is not null || serviceActive)
                    PrintReady(await WaitForAsync(client, media, null, token), false, serviceActive);
                else if (!ownsService)
                    PrintReady(await StartAsync(media), true, false);
                else
                    Console.WriteLine("Media | Windows service stopped. Start it from the dashboard.");

                using var page = await client.GetAsync(admin, HttpCompletionOption.ResponseHeadersRead, token);
                if (!page.IsSuccessStatusCode || page.Content.Headers.ContentType?.MediaType != "text/html")
                    throw new LauncherException("The dashboard is not ready. Check the package's web folder.");
                Console.WriteLine($"Dashboard: {admin}");
                Console.WriteLine("Logs: temp/runtime");
                Console.WriteLine("To quit: press Ctrl+C, close the window, or type Q and press Enter.");
                if (showBrowser)
                {
                    try { OpenDashboard(admin, controlIdentity, directory); }
                    catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
                    { Console.Error.WriteLine("Could not open the browser. Open the dashboard URL manually."); }
                }
            }

            _ = Task.Run(() =>
            {
                while (!token.IsCancellationRequested && Console.ReadLine() is { } line)
                    if (line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)) { shutdown.Request(); break; }
            });
            var mediaExited = false;
            while (!token.IsCancellationRequested)
            {
                if (job.Exited(RuntimeRole.Control))
                {
                    shutdown.Request();
                    throw new LauncherException("Control stopped unexpectedly. Check temp/runtime.");
                }
                if (!mediaExited && job.Exited(RuntimeRole.Media))
                {
                    mediaExited = true;
                    Console.Error.WriteLine("Media stopped. The dashboard is still available.");
                }
                await Task.Delay(500, token);
            }
            return 0;
        }
        finally
        {
            if (job.Owns(RuntimeRole.Control) || job.Owns(RuntimeRole.Media))
            {
                TryWriteStatus("Stopping Didanihaa...");
                var graceful = await job.StopAsync();
                TryWriteStatus(graceful ? "Owned processes stopped." : "Shutdown timed out. Owned processes were terminated.");
                try
                {
                    using var startupLock = RuntimeFile.Lock(Path.Combine(directory, "startup.lock"), TimeSpan.Zero);
                    ledger.Write(ledger.Read());
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException) { }
            }
        }

        async Task<RuntimeIdentity> StartAsync(Backend backend)
        {
            token.ThrowIfCancellationRequested();
            Console.WriteLine($"{backend.Role} | Starting on port {backend.Port}");
            var child = job.Start(backend.Role, backend.Executable, layout.Root, loopbackOnly);
            records.Add(new(backend.Role.ToString(), child.Id, backend.Executable, child.StartTime.ToUniversalTime()));
            ledger.Write(records);
            return await WaitForAsync(client, backend, child, token);
        }
    }

    private static FileStream? TryLock(string path)
    {
        try { return RuntimeFile.Lock(path, TimeSpan.Zero); }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { return null; }
    }

    private static void PrintReady(RuntimeIdentity identity, bool owned, bool service)
    {
        var mode = owned ? "started by this launcher" : service ? "Windows service" : "independent process";
        Console.WriteLine($"{identity.Role} | Ready (PID {identity.ProcessId}, {mode})");
    }

    private static void TryWriteStatus(string message)
    {
        // The console can already be unavailable during CTRL_CLOSE_EVENT.
        try { Console.WriteLine(message); } catch (IOException) { }
    }

    private async Task<RuntimeIdentity?> DiscoverAsync(HttpClient client, Backend backend, CancellationToken token, bool ownedServiceStarting = false)
    {
        var identity = await ReadIdentityAsync(client, backend, token);
        if (identity is not null) return identity;
        if (!ownedServiceStarting && await CanConnectAsync(backend.Port, token))
            throw new LauncherException($"{backend.Role} port {backend.Port} is in use by an unverified process.");
        return null;
    }

    private async Task<RuntimeIdentity> WaitForAsync(HttpClient client, Backend backend, Process? child, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(20) && child is not { HasExited: true })
        {
            var identity = await ReadIdentityAsync(client, backend, token, requireReady: true);
            if (identity is not null && (child is null || child.Id == identity.ProcessId)) return identity;
            await Task.Delay(150, token);
        }
        throw new LauncherException($"{backend.Role} did not become ready on port {backend.Port}. Check temp/runtime.");
    }

    private async Task<RuntimeIdentity?> ReadIdentityAsync(HttpClient client, Backend backend, CancellationToken token, bool requireReady = false)
    {
        try
        {
            var identity = backend.Role == RuntimeRole.Control
                ? (await client.GetFromJsonAsync<Session>(backend.GetUrl(), token))?.Runtime
                : (await client.GetFromJsonAsync<RuntimeStatus>(backend.GetUrl(), token)) is { } media && (!requireReady || media.CatalogReady)
                    ? media.Identity : null;
            return identity?.IsCurrent(layout.Root, backend.Role, backend.Executable) == true ? identity : null;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is HttpRequestException or JsonException or NotSupportedException)
        { return null; }
    }

    private static async Task<bool> CanConnectAsync(int port, CancellationToken token)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(400));
        try { await client.ConnectAsync(System.Net.IPAddress.Loopback, port, timeout.Token); return true; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
        catch (SocketException) { return false; }
    }

    private static void OpenDashboard(Uri admin, RuntimeIdentity control, string directory)
    {
        var marker = Path.Combine(directory, "browser-open.txt");
        PathSafety.RejectExistingReparsePoints(marker);
        if (File.Exists(marker) && File.ReadAllText(marker) == control.InstanceId
            && DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < TimeSpan.FromSeconds(5)) return;
        using var browser = Process.Start(new ProcessStartInfo(admin.AbsoluteUri) { UseShellExecute = true });
        RuntimeFile.Write(marker, control.InstanceId, true);
    }
}
