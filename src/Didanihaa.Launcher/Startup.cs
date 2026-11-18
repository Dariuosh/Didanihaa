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

    public async Task<int> RunAsync(bool showBrowser, bool loopbackOnly)
    {
        var directory = RuntimeSettings.StatePath(layout.Root);
        FileStream gate;
        try { gate = RuntimeFile.Lock(Path.Combine(directory, "startup.lock"), TimeSpan.Zero); }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { return 0; }
        using var startupLock = gate;
        var control = new Backend(RuntimeRole.Control, layout.ResolveOwnedPath("apps/control/Didanihaa.Control.exe"),
            layout.Configuration.PublicPort, "admin/api/session");
        var media = new Backend(RuntimeRole.Media, layout.HostExecutable, layout.Configuration.MediaPort, "_internal/runtime");
        var admin = new Uri(control.GetUrl(), "/admin/");
        foreach (var path in new[] { control.Executable, media.Executable, Path.Combine(layout.WebPath, "admin", "index.html") })
        {
            PathSafety.RejectExistingReparsePoints(path);
            if (!File.Exists(path)) throw new InvalidOperationException($"فایل بسته پیدا نشد: {Path.GetFileName(path)}. کل بسته را استخراج کن.");
        }

        var ledger = new ProcessLedger(layout.Root);
        var records = ledger.Read();
        var children = new List<Process>();
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(1) };
        RuntimeIdentity controlIdentity;
        try
        {
            var existingControl = await DiscoverAsync(client, control);
            var service = new WindowsServiceManager(layout).ReadState();
            var ownsService = service is { Installed: true, Owned: true };
            var serviceActive = ownsService && service.State is ("running" or "starting");
            var existingMedia = await DiscoverAsync(client, media, serviceActive);
            ledger.Write(records);
            controlIdentity = existingControl ?? await StartAsync(control);
            if (existingMedia is not null || serviceActive) await WaitForAsync(client, media, null);
            else if (!ownsService) await StartAsync(media);
            using var page = await client.GetAsync(admin, HttpCompletionOption.ResponseHeadersRead);
            if (!page.IsSuccessStatusCode || page.Content.Headers.ContentType?.MediaType != "text/html")
                throw new InvalidOperationException("داشبورد آماده نشد. فایل‌های web بسته را بررسی کن.");
        }
        catch
        {
            // Roll back only children launched in this attempt. Reused processes and SCM stay independent.
            foreach (var process in children.AsEnumerable().Reverse())
            {
                try
                {
                    if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(3000); }
                    if (process.HasExited) records.RemoveAll(record => record.Id == process.Id);
                }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
            }
            ledger.Write(records);
            throw;
        }
        finally { foreach (var child in children) child.Dispose(); }

        if (showBrowser) OpenDashboard(admin, controlIdentity, directory);
        return 0;

        async Task<RuntimeIdentity> StartAsync(Backend backend)
        {
            var start = new ProcessStartInfo(backend.Executable)
            {
                WorkingDirectory = layout.Root, UseShellExecute = false, CreateNoWindow = true
            };
            start.ArgumentList.Add("--root");
            start.ArgumentList.Add(layout.Root);
            start.ArgumentList.Add("--launcher-log");
            if (backend.Role == RuntimeRole.Control && loopbackOnly) start.ArgumentList.Add("--loopback-only");
            var child = Process.Start(start) ?? throw new InvalidOperationException("اجرای بخش برنامه آغاز نشد.");
            children.Add(child);
            _ = child.SafeHandle;
            records.Add(new(backend.Role.ToString(), child.Id, backend.Executable, child.StartTime.ToUniversalTime()));
            ledger.Write(records);
            return await WaitForAsync(client, backend, child);
        }
    }

    private async Task<RuntimeIdentity?> DiscoverAsync(HttpClient client, Backend backend, bool ownedServiceStarting = false)
    {
        var identity = await ReadIdentityAsync(client, backend);
        if (identity is not null) return identity;
        if (!ownedServiceStarting && await CanConnectAsync(backend.Port))
            throw new InvalidOperationException($"درگاه {backend.Port} برای {backend.Role} اشغال است. اجرای متعلق به این پوشه تأیید نشد.");
        return null;
    }

    private async Task<RuntimeIdentity> WaitForAsync(HttpClient client, Backend backend, Process? child)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(20) && child is not { HasExited: true })
        {
            var identity = await ReadIdentityAsync(client, backend, requireReady: true);
            if (identity is not null && (child is null || child.Id == identity.ProcessId)) return identity;
            await Task.Delay(150);
        }
        throw new InvalidOperationException($"{backend.Role} روی درگاه {backend.Port} آماده نشد. گزارش temp/runtime را بررسی کن.");
    }

    private async Task<RuntimeIdentity?> ReadIdentityAsync(HttpClient client, Backend backend, bool requireReady = false)
    {
        try
        {
            var identity = backend.Role == RuntimeRole.Control
                ? (await client.GetFromJsonAsync<Session>(backend.GetUrl()))?.Runtime
                : (await client.GetFromJsonAsync<RuntimeStatus>(backend.GetUrl())) is { } media && (!requireReady || media.CatalogReady)
                    ? media.Identity : null;
            return identity?.IsCurrent(layout.Root, backend.Role, backend.Executable) == true ? identity : null;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or NotSupportedException)
        { return null; }
    }

    private static async Task<bool> CanConnectAsync(int port)
    {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        try { await client.ConnectAsync(System.Net.IPAddress.Loopback, port, timeout.Token); return true; }
        catch (Exception error) when (error is SocketException or OperationCanceledException) { return false; }
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
