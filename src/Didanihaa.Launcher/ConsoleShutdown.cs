using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Didanihaa.Launcher;

internal sealed class ConsoleShutdown : IDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly ManualResetEventSlim finished = new();
    private readonly Handler handler;

    public CancellationToken Token => stopping.Token;

    public ConsoleShutdown()
    {
        handler = Handle;
        // An invoking shell can pass down the flag that ignores Ctrl+C.
        if (!SetConsoleCtrlHandler(null, false) || !SetConsoleCtrlHandler(handler, true)) throw new Win32Exception();
    }

    public void Request() => _ = stopping.CancelAsync();

    private bool Handle(uint signal)
    {
        if (signal is not (0 or 1 or 2)) return false; // Ctrl+C, Ctrl+Break, console Close.
        Request();
        // Windows can terminate this process when the Close handler returns.
        if (signal == 2) finished.Wait(TimeSpan.FromSeconds(4));
        return true;
    }

    public void Dispose()
    {
        finished.Set();
        SetConsoleCtrlHandler(handler, false);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool Handler(uint signal);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(Handler? handler, [MarshalAs(UnmanagedType.Bool)] bool add);
}
