namespace Didanihaa.Core;

public sealed class RuntimeStopSignal : IDisposable
{
    private readonly EventWaitHandle signal;
    private readonly RegisteredWaitHandle registration;

    private RuntimeStopSignal(EventWaitHandle signal, Action request)
    {
        this.signal = signal;
        registration = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => request(), null, Timeout.Infinite, true);
    }

    public static string Name(string nonce) => @"Local\Didanihaa.Stop." + nonce;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static RuntimeStopSignal? Listen(string[] args, Action request)
    {
        var nonce = RuntimeLayout.Argument(args, "--shutdown-event");
        if (nonce is null) return null;
        if (!Guid.TryParseExact(nonce, "N", out _) || !EventWaitHandle.TryOpenExisting(Name(nonce), out var signal))
            throw new InvalidOperationException("سیگنال توقف راه‌انداز در دسترس نیست.");
        return new RuntimeStopSignal(signal, request);
    }

    public void Dispose()
    {
        registration.Unregister(null);
        signal.Dispose();
    }
}
