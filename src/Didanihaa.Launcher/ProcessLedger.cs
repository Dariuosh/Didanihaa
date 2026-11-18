using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Didanihaa.Core;

namespace Didanihaa.Launcher;

internal sealed record OwnedProcess(string Role, int Id, string Executable, DateTimeOffset StartedUtc)
{
    public bool IsRunning()
    {
        if (Role is not ("Control" or "Media") || Id <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(Id);
            return process.MainModule?.FileName is { } path && PathSafety.SamePath(path, Executable)
                && process.StartTime.ToUniversalTime() == StartedUtc.UtcDateTime;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }
}

internal sealed class ProcessLedger(string root)
{
    private sealed record State(string Root, string? Mode, List<OwnedProcess> Processes);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string CurrentPath => Path.Combine(RuntimeSettings.StatePath(root), "processes.json");
    public List<OwnedProcess> Read()
    {
        var path = File.Exists(CurrentPath) ? CurrentPath : Path.Combine(root, "temp", "dev", "processes.json");
        PathSafety.RejectExistingReparsePoints(path);
        if (!File.Exists(path)) return [];
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path), Json);
        if (state is null || string.IsNullOrEmpty(state.Root) || state.Processes is null || state.Mode is not (null or "Runtime"))
            throw new LauncherException("Invalid process state. Check temp/runtime/processes.json.");
        // A copied portable folder never inherits ownership of the original's processes.
        return PathSafety.SamePath(state.Root, root) ? state.Processes.Where(process => process.IsRunning()).ToList() : [];
    }

    public void Write(List<OwnedProcess> processes) =>
        RuntimeFile.Write(CurrentPath, JsonSerializer.Serialize(new State(root, "Runtime", processes), Json), true);
}
