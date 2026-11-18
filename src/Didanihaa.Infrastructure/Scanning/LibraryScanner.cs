using System.Globalization;
using Didanihaa.Core;

namespace Didanihaa.Infrastructure;

public sealed class LibraryScanner(CatalogRepository repository, MetadataProbe probe)
{
    public async Task RunAsync(ScanJob job, CancellationToken cancellationToken)
    {
        var scanned = 0; var errors = 0; string? lastError = null;
        try
        {
            var source = repository.Sources().Single(item => item.Id == job.SourceId);
            PathSafety.RejectReparsePoints(source.ResolvedPath);
            if (!Directory.Exists(source.ResolvedPath)) throw new IOException("پوشه منبع در دسترس نیست.");
            var executable = await probe.FindExecutableAsync(cancellationToken);
            Stack<string> directories = new(); directories.Push(source.ResolvedPath);
            while (directories.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    PathSafety.RejectReparsePoints(directory);
                    foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var attributes = File.GetAttributes(path);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { errors++; lastError = "یک مسیر پیوندی برای ایمنی کنار گذاشته شد."; continue; }
                            if ((attributes & FileAttributes.Directory) != 0) { directories.Push(path); continue; }
                            if (!MediaTypes.TryGet(path, out var kind, out var mime)) continue;
                            var relative = Path.GetRelativePath(source.ResolvedPath, path);
                            _ = PathSafety.ResolveMedia(source.ResolvedPath, relative);
                            var file = new FileInfo(path);
                            var size = file.Length; var modified = file.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture);
                            var previous = repository.FindByPath(source.Id, relative);
                            var unchanged = previous is not null && previous.SizeBytes == size && previous.ModifiedUtc == modified;
                            var metadata = unchanged && previous!.Metadata.Status == "complete" ? previous.Metadata
                                : executable is null ? MediaMetadata.Basic
                                : await probe.ReadAsync(path, executable, cancellationToken);
                            file.Refresh();
                            if (!file.Exists || file.Length != size || file.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture) != modified)
                                throw new IOException("فایل هنگام اسکن تغییر کرد؛ بعداً دوباره اسکن کنید.");
                            var item = new MediaCandidate(previous?.Id ?? Guid.NewGuid().ToString("N"), source.Id, relative,
                                string.IsNullOrWhiteSpace(metadata.Title) ? Path.GetFileNameWithoutExtension(path) : metadata.Title,
                                kind, mime, size, modified, false, true, metadata);
                            repository.SaveScanned(item, job.Id); scanned++;
                            if (metadata.Status == "error") { errors++; lastError = "metadata برخی فایل‌ها خوانده نشد؛ جزئیات در فهرست نامزدهاست."; }
                            if (scanned % 25 == 0) repository.UpdateScan(job.Id, scanned, errors, "running", lastError);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { errors++; lastError = "دسترسی یا خواندن برخی فایل‌ها موفق نبود؛ اسکن مجدد لازم است."; }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { errors++; lastError = "دسترسی به بخشی از پوشه‌ها موفق نبود؛ موارد قدیمی حفظ شدند."; }
            }
            // Only an error-free traversal is evidence that an unseen file has disappeared.
            if (errors == 0) repository.MarkMissing(source.Id, job.Id);
            repository.UpdateScan(job.Id, scanned, errors, errors == 0 ? "completed" : "completed-with-errors", lastError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { repository.UpdateScan(job.Id, scanned, errors, "interrupted", "اسکن متوقف شد؛ امکان اسکن مجدد وجود دارد."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { repository.UpdateScan(job.Id, scanned, errors + 1, "failed", "پوشه منبع قابل اسکن نیست؛ مسیر و دسترسی را بررسی کنید."); }
    }
}
