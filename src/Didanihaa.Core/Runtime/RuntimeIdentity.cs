using System.Diagnostics;
using System.Text.Json;

namespace Didanihaa.Core;

public enum RuntimeRole { Control, Media }

public sealed record RuntimeIdentity(int Protocol, string RootId, string InstanceId, string Role,
    int ProcessId, string ExecutablePath, DateTimeOffset StartedUtc)
{
    public static string DescriptorPath(string root, RuntimeRole role) =>
        Path.Combine(RuntimeSettings.StatePath(root), role.ToString().ToLowerInvariant() + ".identity.json");

    public bool IsCurrent(string root, RuntimeRole role, params string[] expectedExecutables)
    {
        try
        {
            if (Protocol != 1 || Role != role.ToString() || RootId != RuntimeRoot.Id(root)
                || !Guid.TryParseExact(InstanceId, "N", out _) || ProcessId <= 0
                || !expectedExecutables.Any(path => PathSafety.SamePath(path, ExecutablePath))) return false;
            var descriptorPath = DescriptorPath(root, role);
            PathSafety.RejectExistingReparsePoints(descriptorPath);
            var descriptor = JsonSerializer.Deserialize<RuntimeIdentity>(File.ReadAllText(descriptorPath), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (descriptor != this) return false;
            using var process = Process.GetProcessById(ProcessId);
            // The host captured its executable path; compare it with the local
            // descriptor without requiring cross-account module enumeration.
            return Math.Abs((process.StartTime.ToUniversalTime() - StartedUtc.UtcDateTime).TotalMilliseconds) < 10;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException) { return false; }
    }
}
