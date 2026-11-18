using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Didanihaa.Core;

namespace Didanihaa.Platform.Windows;

public sealed class WindowsServiceManager(RuntimeLayout layout)
{
    public const string ServiceName = "Didanihaa.Media";
    private static readonly HashSet<string> Actions = ["install", "start", "stop", "restart"];

    public ServiceState ReadState()
    {
        if (!OperatingSystem.IsWindows()) return new(false, false, "unsupported");
        var manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) return new(true, false, "access-denied");
        try
        {
            var service = OpenService(manager, ServiceName, 5);
            if (service == IntPtr.Zero)
                return Marshal.GetLastWin32Error() == 1060 ? new(true, false, "not-installed") : new(true, false, "access-denied");
            try
            {
                if (!QueryServiceStatus(service, out var status)) return new(true, true, "unknown");
                string? executable = null;
                QueryServiceConfig(service, IntPtr.Zero, 0, out var bytes);
                if (bytes > 0)
                {
                    var buffer = Marshal.AllocHGlobal(checked((int)bytes));
                    try
                    {
                        if (QueryServiceConfig(service, buffer, bytes, out _))
                            executable = Marshal.PtrToStringUni(Marshal.PtrToStructure<ServiceConfiguration>(buffer).BinaryPath);
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                return new(true, true, status.CurrentState switch
                {
                    1 => "stopped", 2 => "starting", 3 => "stopping", 4 => "running", 7 => "paused", _ => "pending"
                }, executable, MatchesInstallation(executable, layout));
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    public async Task<ServiceActionResult> RequestAsync(string action, CancellationToken cancellationToken)
    {
        if (!Actions.Contains(action)) throw new ArgumentException("عملیات سرویس معتبر نیست.");
        if (!OperatingSystem.IsWindows()) return new(false, "مدیریت Windows Service فقط در ویندوز قابل استفاده است.");
        if (ReadState() is { Installed: true, Owned: false })
            return new(false, "سرویس ثبت‌شده متعلق به این پوشه نیست یا مالکیت آن مشخص نیست؛ عملیات انجام نشد.");
        if (action == "install" && !File.Exists(layout.HostExecutable))
            return new(false, "ابتدا tools/Build-Publish.ps1 را اجرا کنید؛ فایل اجرایی سرویس هنوز ساخته نشده است.");
        Directory.CreateDirectory(layout.TempPath);
        var resultPath = Path.Combine(layout.TempPath, $"service-{Guid.NewGuid():N}.json");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("آدرس برنامه پیدا نشد.");
        List<string> arguments = [];
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments.Add(Assembly.GetEntryAssembly()!.Location);
        arguments.AddRange(["--root", layout.Root, "--service-action", action, "--result", resultPath]);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, string.Join(" ", arguments.Select(Quote)))
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })
                ?? throw new InvalidOperationException("اجرای helper موفق نبود.");
            // The operation remains owned until the helper exits, even if the browser disconnects.
            await process.WaitForExitAsync(CancellationToken.None);
            if (!File.Exists(resultPath)) return new(false, "helper نتیجه‌ای ثبت نکرد؛ وضعیت سرویس را بررسی کنید.");
            return JsonSerializer.Deserialize<ServiceActionResult>(await File.ReadAllTextAsync(resultPath, cancellationToken))
                ?? new(false, "پاسخ helper معتبر نبود.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { return new(false, "درخواست دسترسی ویندوز توسط کاربر لغو شد."); }
        finally { if (File.Exists(resultPath)) File.Delete(resultPath); }
    }

    public async Task<int> RunHelperAsync(string action, string resultPath)
    {
        if (!Actions.Contains(action) || !PathSafety.IsWithin(layout.TempPath, resultPath)
            || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(resultPath), "^service-[a-f0-9]{32}\\.json$")) return 2;
        ServiceActionResult result;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("این عملیات به ویندوز نیاز دارد.");
            if (ReadState() is { Installed: true, Owned: false })
                throw new InvalidOperationException("سرویس ثبت‌شده متعلق به این پوشه نیست یا مالکیت آن مشخص نیست؛ عملیات انجام نشد.");
            if (action == "install")
            {
                if (!File.Exists(layout.HostExecutable)) throw new FileNotFoundException("خروجی publish سرویس پیدا نشد.");
                if (ReadState().State is not ("stopped" or "not-installed"))
                    throw new InvalidOperationException("پیش از نصب یا ثبت مسیر جدید، سرویس را متوقف کنید.");
                // Only the application root is granted read/execute; external media ACLs remain explicit.
                await RunCommandAsync(Path.Combine(Environment.SystemDirectory, "icacls.exe"),
                    [layout.Root, "/grant", "*S-1-5-19:(OI)(CI)RX"]);
                var statePath = RuntimeSettings.StatePath(layout.Root);
                PathSafety.RejectExistingReparsePoints(statePath);
                Directory.CreateDirectory(statePath);
                await RunCommandAsync(Path.Combine(Environment.SystemDirectory, "icacls.exe"),
                    [statePath, "/grant", "*S-1-5-19:(OI)(CI)M"]);
                var command = ReadState().Installed ? "config" : "create";
                await ScAsync([command, ServiceName, "binPath=", $"{Quote(layout.HostExecutable)} --root {Quote(layout.Root)}",
                    "start=", "delayed-auto", "obj=", "NT AUTHORITY\\LocalService", "DisplayName=", "Didanihaa Media"]);
                await ScAsync(["description", ServiceName, "Didanihaa portable LAN media service"]);
            }
            else
            {
                if (!ReadState().Installed) throw new InvalidOperationException("سرویس هنوز نصب نشده است.");
                if (action is "stop" or "restart")
                {
                    if (ReadState().State != "stopped") await ScAsync(["stop", ServiceName]);
                    await WaitForAsync("stopped");
                }
                if (action is "start" or "restart")
                {
                    if (ReadState().State != "running") await ScAsync(["start", ServiceName]);
                    await WaitForAsync("running");
                }
            }
            result = new(true, "عملیات سرویس با موفقیت انجام شد.");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or TimeoutException)
        { result = new(false, ex.Message); }
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result));
        return result.Success ? 0 : 1;
    }

    private async Task WaitForAsync(string state)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(40))
        {
            if (ReadState().State == state) return;
            await Task.Delay(300);
        }
        throw new TimeoutException("سرویس در زمان مقرر به وضعیت موردنظر نرسید؛ وضعیت و دسترسی‌ها را بررسی کنید.");
    }

    private static Task ScAsync(string[] arguments) => RunCommandAsync(Path.Combine(Environment.SystemDirectory, "sc.exe"), arguments);
    public static bool MatchesInstallation(string? command, RuntimeLayout layout) =>
        string.Equals(command, $"{Quote(layout.HostExecutable)} --root {Quote(layout.Root)}", StringComparison.OrdinalIgnoreCase);
    private static async Task RunCommandAsync(string executable, string[] arguments)
    {
        using var process = new Process { StartInfo = new(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }};
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("زمان اجرای فرمان مدیریت سرویس پایان یافت."); }
        var message = (await output) + (await error);
        if (process.ExitCode != 0) throw new InvalidOperationException(message.Trim());
    }

    public static string Quote(string value)
    {
        StringBuilder result = new("\""); var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceConfiguration
    {
        public uint ServiceType, StartType, ErrorControl;
        public IntPtr BinaryPath, LoadOrderGroup;
        public uint TagId;
        public IntPtr Dependencies, StartName, DisplayName;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig(IntPtr service, IntPtr configuration, uint size, out uint needed);
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
