using System.Diagnostics;
using System.Text.Json;

namespace Didanihaa.Core;

public sealed class RuntimeInstance : IDisposable
{
    public const string RequestHeader = "X-Didanihaa-Instance";
    private readonly FileStream gate;
    private readonly string descriptorPath;
    private bool disposed;
    public RuntimeIdentity Identity { get; }

    private RuntimeInstance(FileStream gate, string descriptorPath, RuntimeIdentity identity)
    { this.gate = gate; this.descriptorPath = descriptorPath; Identity = identity; }

    public static RuntimeInstance Acquire(RuntimeLayout layout, RuntimeRole role)
    {
        FileStream gate;
        try
        {
            gate = RuntimeFile.Lock(Path.Combine(RuntimeSettings.StatePath(layout.Root), role.ToString().ToLowerInvariant() + ".lock"), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException($"نقش {role} این پوشه در حال اجراست یا مسیر وضعیت قابل نوشتن نیست.", ex); }
        try
        {
            using var process = Process.GetCurrentProcess();
            var identity = new RuntimeIdentity(1, RuntimeRoot.Id(layout.Root), Guid.NewGuid().ToString("N"), role.ToString(),
                process.Id, Environment.ProcessPath ?? throw new InvalidOperationException("مسیر فایل اجرایی پیدا نشد."), process.StartTime.ToUniversalTime());
            var descriptorPath = RuntimeIdentity.DescriptorPath(layout.Root, role);
            RuntimeFile.Write(descriptorPath, JsonSerializer.Serialize(identity, new JsonSerializerOptions(JsonSerializerDefaults.Web)), true);
            return new(gate, descriptorPath, identity);
        }
        catch { gate.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { File.Delete(descriptorPath); }
        finally { gate.Dispose(); }
    }
}
