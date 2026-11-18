using Didanihaa.Core;
using Didanihaa.Infrastructure;
using Microsoft.Extensions.FileProviders;

return await LauncherLog.RunBackendAsync(args, RuntimeRole.Media, RunAsync);

async Task<int> RunAsync()
{
    var layout = RuntimeLayout.Load(args);
    using var instance = RuntimeInstance.Acquire(layout, RuntimeRole.Media);
    using var launcherLog = LauncherLog.Open(args, layout, RuntimeRole.Media);
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = layout.Root });
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.AddServerHeader = false;
        options.ListenLocalhost(layout.Configuration.MediaPort);
    });
    builder.Services.AddWindowsService(options => options.ServiceName = "Didanihaa.Media");
    builder.Services.AddSingleton(layout);
    builder.Services.AddSingleton<CatalogRepository>();
    var app = builder.Build();
    app.Use(async (context, next) =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; media-src 'self'; connect-src 'self'; frame-ancestors 'none'";
        if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/media") || context.Request.Path == "/health")
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Headers[RuntimeInstance.RequestHeader] != instance.Identity.InstanceId)
            { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "نمونهٔ سرویس پخش تغییر کرده است؛ دوباره تلاش کنید." }); return; }
            if (!File.Exists(layout.DatabasePath))
            { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "کاتالوگ هنوز آماده نیست؛ پنل مدیریت را اجرا کنید." }); return; }
        }
        try { await next(); }
        catch (ArgumentException) when (!context.Response.HasStarted)
        { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "پوشه یا فیلتر درخواست‌شده معتبر نیست." }); }
    });

    if (Directory.Exists(Path.Combine(layout.WebPath, "tv")))
    {
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(layout.WebPath, "tv")), RequestPath = "/tv" });
        if (Directory.Exists(Path.Combine(layout.WebPath, "shared")))
            app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(layout.WebPath, "shared")), RequestPath = "/shared" });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(layout.WebPath, "assets")), RequestPath = "/assets" });
    }
    app.MapGet("/", () => Results.Redirect("/tv/"));
    app.MapGet("/tv/", () => File.Exists(Path.Combine(layout.WebPath, "tv/index.html"))
        ? Results.File(Path.Combine(layout.WebPath, "tv/index.html"), "text/html; charset=utf-8")
        : Results.Problem("ابتدا رابط وب را با npm run build بسازید.", statusCode: 503));
    app.MapGet("/health", () => Results.Ok(new { service = "Didanihaa.Media", catalogReady = File.Exists(layout.DatabasePath), mode = "original-files" }));
    // This endpoint is deliberately absent from Control's public forwarding routes.
    app.MapGet("/_internal/runtime", (HttpContext context) =>
        context.Connection.RemoteIpAddress is { } remote && System.Net.IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote)
            ? Results.Ok(new RuntimeStatus(instance.Identity, File.Exists(layout.DatabasePath))) : Results.NotFound());
    app.MapGet("/api/catalog", (HttpRequest request, CatalogRepository repository) =>
    {
        var offset = int.TryParse(request.Query["offset"], out var start) ? start : 0;
        var limit = int.TryParse(request.Query["limit"], out var count) ? count : 40;
        var kind = request.Query["kind"].ToString();
        var sourceId = request.Query["sourceId"].ToString();
        var page = repository.Candidates(sourceId.Length == 0 ? null : sourceId, request.Query["search"], offset, limit, true,
            kind is "video" or "audio" ? kind : null, folder: request.Query.ContainsKey("folder") ? request.Query["folder"].ToString() : null,
            ids: request.Query.ContainsKey("ids") ? request.Query["ids"].ToString().Split(',').Take(200).ToArray() : null);
        return Results.Ok(new Page<PublicMedia>(page.Items.Select(item => new PublicMedia(item.Id, item.Title, item.Kind,
            item.MimeType, item.SizeBytes, item.Metadata.DurationSeconds, $"/media/{item.Id}",
            item.Kind == "video" ? $"/thumbnails/{item.Id}" : null)).ToList(), page.Total, page.Offset, page.Limit));
    });
    IResult ViewerFolders(HttpRequest request, CatalogRepository repository)
    {
        var sourceId = request.Query["sourceId"].ToString(); var kind = request.Query["kind"].ToString();
        return Results.Ok(repository.Folders(sourceId.Length == 0 ? null : sourceId, request.Query["folder"], true,
            request.Query["search"], kind is "video" or "audio" ? kind : null));
    }
    app.MapGet("/api/folders", ViewerFolders);
    app.MapGet("/api/viewer-folders", ViewerFolders);
    app.MapGet("/api/location/{id}", (string id, CatalogRepository repository) => repository.LocatePublished(id) is { } location
        ? Results.Ok(location) : Results.NotFound());
    app.MapMethods("/media/{id}", ["GET", "HEAD"], (string id, CatalogRepository repository) =>
    {
        var item = repository.Find(id, true);
        if (item is null) return Results.NotFound();
        var source = repository.Sources().FirstOrDefault(source => source.Id == item.SourceId);
        if (source is null) return Results.NotFound();
        FileStream? stream = null;
        try
        {
            var path = PathSafety.ResolveMedia(source.ResolvedPath, item.RelativePath);
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var modified = File.GetLastWriteTimeUtc(path);
            if (stream.Length != item.SizeBytes || modified.ToString("O", System.Globalization.CultureInfo.InvariantCulture) != item.ModifiedUtc)
            { stream.Dispose(); return Results.NotFound(); }
            return Results.File(stream, item.MimeType, lastModified: new DateTimeOffset(modified), enableRangeProcessing: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { stream?.Dispose(); return Results.NotFound(); }
    });
    app.Logger.LogInformation("Didanihaa Media (internal): http://localhost:{Port}/health — root: {Root}", layout.Configuration.MediaPort, layout.Root);
    await app.RunAsync();
    return 0;
}
