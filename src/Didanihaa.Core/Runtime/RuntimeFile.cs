using System.Diagnostics;
using System.Text;

namespace Didanihaa.Core;

public static class RuntimeFile
{
    public static FileStream Lock(string path, TimeSpan timeout)
    {
        PathSafety.RejectExistingReparsePoints(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < timeout) { Thread.Sleep(20); }
        }
    }

    public static void Write(string path, string text, bool overwrite)
    {
        PathSafety.RejectExistingReparsePoints(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(Encoding.UTF8.GetBytes(text));
                output.Flush(true);
            }
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
