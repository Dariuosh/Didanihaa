using System.Text.Json;

namespace Didanihaa.Core;

public static class RuntimeSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static string StatePath(string root) => Path.Combine(RuntimeRoot.Normalize(root), "temp", "runtime");

    public static RuntimeConfiguration Load(string root)
    {
        var destination = Path.Combine(root, "didanihaa.json");
        if (File.Exists(destination)) return Read(root, destination);
        try
        {
            using var gate = RuntimeFile.Lock(Path.Combine(StatePath(root), "configuration.lock"), TimeSpan.FromSeconds(2));
            if (File.Exists(destination)) return Read(root, destination);
            var example = Path.Combine(root, "didanihaa.example.json");
            var configuration = Read(root, example);
            RuntimeFile.Write(destination, File.ReadAllText(example), false);
            return configuration;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("ایجاد تنظیمات محلی ممکن نشد؛ دسترسی نوشتن و قفل تنظیمات را بررسی کنید.", ex); }
    }

    public static RuntimeConfiguration Update(string root, Func<RuntimeConfiguration, RuntimeConfiguration> update)
    {
        try
        {
            using var gate = RuntimeFile.Lock(Path.Combine(StatePath(root), "configuration.lock"), TimeSpan.FromSeconds(2));
            var destination = Path.Combine(root, "didanihaa.json");
            var original = File.Exists(destination) ? destination : Path.Combine(root, "didanihaa.example.json");
            var updated = update(Read(root, original));
            new RuntimeLayout(root, updated).Validate();
            RuntimeFile.Write(destination, JsonSerializer.Serialize(updated, Json), true);
            return updated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("ذخیرهٔ تنظیمات ممکن نشد؛ دسترسی نوشتن و قفل تنظیمات را بررسی کنید.", ex); }
    }

    private static RuntimeConfiguration Read(string root, string path)
    {
        try
        {
            PathSafety.RejectExistingReparsePoints(path);
            var configuration = JsonSerializer.Deserialize<RuntimeConfiguration>(File.ReadAllText(path), Json)
                ?? throw new JsonException("Empty configuration.");
            new RuntimeLayout(root, configuration).Validate();
            return configuration;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        { throw new InvalidOperationException("تنظیمات قابل خواندن یا معتبر نیست؛ didanihaa.json و مسیرهای آن را بررسی کنید.", ex); }
    }
}
