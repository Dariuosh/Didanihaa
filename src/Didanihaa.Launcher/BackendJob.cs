using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Didanihaa.Core;
using Didanihaa.Platform.Windows;

namespace Didanihaa.Launcher;

internal sealed class BackendJob : IDisposable
{
    private readonly SafeFileHandle handle;
    private readonly List<Child> children = [];
    private sealed record Child(RuntimeRole Role, Process Process, EventWaitHandle Stop);

    public BackendJob()
    {
        handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            var error = new Win32Exception();
            handle.Dispose();
            throw error;
        }
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION. The final handle closes with the controller.
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x00002000 } };
        if (SetInformationJobObject(handle, 9, ref limits, Marshal.SizeOf<ExtendedLimits>())) return;
        var failure = new Win32Exception();
        handle.Dispose();
        throw failure;
    }

    public Process Start(RuntimeRole role, string executable, string root, bool loopbackOnly)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var stop = new EventWaitHandle(false, EventResetMode.ManualReset, RuntimeStopSignal.Name(nonce));
        try
        {
            List<string> arguments = [executable, "--root", root, "--launcher-log", "--shutdown-event", nonce];
            if (role == RuntimeRole.Control && loopbackOnly) arguments.Add("--loopback-only");
            var command = new StringBuilder(string.Join(' ', arguments.Select(WindowsServiceManager.Quote)));
            var process = CreateMember(executable, root, command);
            children.Add(new(role, process, stop));
            return process;
        }
        catch { stop.Dispose(); throw; }
    }

    public bool Owns(RuntimeRole role) => children.Any(child => child.Role == role);
    public bool Exited(RuntimeRole role) => children.Any(child => child.Role == role && child.Process.HasExited);

    public async Task<bool> StopAsync()
    {
        // Signal both hosts before waiting so concurrent streams/scans get the same grace period.
        foreach (var child in children) child.Stop.Set();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var graceful = true;
        try { await Task.WhenAll(children.Select(child => child.Process.WaitForExitAsync(deadline.Token))); }
        catch (OperationCanceledException) { graceful = false; }
        finally { handle.Dispose(); }
        return graceful;
    }

    public void Dispose()
    {
        handle.Dispose();
        foreach (var child in children)
        {
            child.Stop.Dispose();
            child.Process.Dispose();
        }
    }

    private Process CreateMember(string executable, string root, StringBuilder command)
    {
        nuint bytes = 0;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
        if (bytes == 0) throw new Win32Exception();
        var attributes = Marshal.AllocHGlobal(checked((nint)bytes));
        var jobs = IntPtr.Zero;
        var initialized = false;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) throw new Win32Exception();
            initialized = true;
            jobs = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(jobs, handle.DangerousGetHandle());
            // PROC_THREAD_ATTRIBUTE_JOB_LIST adds the child to the job during process creation.
            if (!UpdateProcThreadAttribute(attributes, 0, 0x0002000D, jobs, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();
            var startup = new StartupInfoEx
            {
                Basic = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>() },
                Attributes = attributes
            };
            const uint extendedStartupInfo = 0x00080000, createNoWindow = 0x08000000;
            if (!CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, false, extendedStartupInfo | createNoWindow,
                IntPtr.Zero, root, ref startup, out var created)) throw new Win32Exception();
            try
            {
                var process = Process.GetProcessById(created.Pid);
                try { _ = process.SafeHandle; return process; }
                catch { process.Dispose(); throw; }
            }
            finally { CloseHandle(created.Thread); CloseHandle(created.Process); }
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (jobs != IntPtr.Zero) Marshal.FreeHGlobal(jobs);
            Marshal.FreeHGlobal(attributes);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadCount, WriteCount, OtherCount, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved, Desktop, Title;
        public uint X, Y, Width, Height, Columns, Rows, Fill, Flags;
        public ushort ShowWindow, ReservedCount;
        public IntPtr ReservedBytes, StdIn, StdOut, StdErr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { public StartupInfo Basic; public IntPtr Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { public IntPtr Process, Thread; public int Pid, Tid; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int type, ref ExtendedLimits info, int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value,
        nuint size, IntPtr previous, IntPtr returnedSize);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool CreateProcessW(string executable, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
