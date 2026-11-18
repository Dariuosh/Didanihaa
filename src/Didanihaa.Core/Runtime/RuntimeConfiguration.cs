using System.Text.Json;
using System.Text.Json.Serialization;

namespace Didanihaa.Core;

public sealed record RuntimeConfiguration
{
    public int PublicPort { get; init; } = 13521;
    public int MediaPort { get; init; } = 13522;
    public string DataDirectory { get; init; } = "data";
    public string TempDirectory { get; init; } = "temp/runtime";
    public string FfprobePath { get; init; } = "";
    public string HostExecutable { get; init; } = "publish/apps/host/Didanihaa.Host.exe";
    public string WebDirectory { get; init; } = "src/Didanihaa.WebUI/dist";
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtraSettings { get; init; }
}
