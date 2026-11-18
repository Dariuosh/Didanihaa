using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Didanihaa.Control;
using Didanihaa.Core;
using Didanihaa.Infrastructure;
using Didanihaa.Platform.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Yarp.ReverseProxy.Forwarder;

var layout = RuntimeLayout.Load(args);
var serviceManager = new WindowsServiceManager(layout);
if (RuntimeLayout.Argument(args, "--service-action") is { } action)
    return await serviceManager.RunHelperAsync(action, RuntimeLayout.Argument(args, "--result") ?? "");

using var instance = RuntimeInstance.Acquire(layout, RuntimeRole.Control);
Directory.CreateDirectory(layout.TempPath);
var repository = new CatalogRepository(layout);
repository.Initialize();
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
using var serviceGate = new SemaphoreSlim(1);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = layout.Root });
builder.WebHost.ConfigureKestrel(options => { options.AddServerHeader = false; options.ListenAnyIP(layout.Configuration.PublicPort); });
builder.Services.AddSingleton(layout);
builder.Services.AddSingleton(repository);
builder.Services.AddSingleton<MediaToolCatalog>();
builder.Services.AddSingleton(services => new MetadataProbe(services.GetRequiredService<MediaToolCatalog>()));
builder.Services.AddSingleton<ThumbnailService>();
builder.Services.AddSingleton<LibraryScanner>();
builder.Services.AddHostedService<ScanWorker>();
builder.Services.AddSingleton(serviceManager);
builder.Services.AddSingleton(new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(1) });
builder.Services.AddSingleton<MediaRuntimeClient>();
builder.Services.AddHttpForwarder();
var app = builder.Build();
using var mediaClient = new HttpMessageInvoker(new SocketsHttpHandler
{
    UseProxy = false, AllowAutoRedirect = false, UseCookies = false,
    AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(3)
});
var mediaDestination = $"http://127.0.0.1:{layout.Configuration.MediaPort}/";
var forwarding = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(2) };

app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    if (context.Connection.RemoteIpAddress is not { } remote || !LanAccess.IsLocalClient(remote)
        || context.Connection.LocalIpAddress is not { } local
        || !LanAccess.IsValidHost(host, context.Request.Host.Port, layout.Configuration.PublicPort, local))
    { context.Response.StatusCode = 403; return; }
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; media-src 'self'; frame-ancestors 'none'";
    var origin = context.Request.Headers.Origin.ToString();
    if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        || uri.Scheme != "http" || !string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase)))
    { context.Response.StatusCode = 403; return; }
    if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
    {
        if (!context.Request.Path.StartsWithSegments("/api/profile") && context.Request.Headers["X-Didanihaa-Token"] != token)
        { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "نشست معتبر نیست؛ صفحه را دوباره باز کنید." }); return; }
    }
    try { await next(); }
    catch (Exception ex) when (ex is ArgumentException or IOException or SqliteException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = ex is ArgumentException ? 400 : 409;
        await context.Response.WriteAsJsonAsync(new { error = ex is SqliteException ? "عملیات کاتالوگ انجام نشد؛ وضعیت منبع و اسکن را بررسی کنید." : ex.Message });
    }
});

if (Directory.Exists(Path.Combine(layout.WebPath, "admin")))
{
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(layout.WebPath, "admin")), RequestPath = "/admin" });
    if (Directory.Exists(Path.Combine(layout.WebPath, "shared")))
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(layout.WebPath, "shared")), RequestPath = "/shared" });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(layout.WebPath, "assets")), RequestPath = "/assets" });
}
if (Directory.Exists(Path.Combine(layout.WebPath, "tv")))
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(layout.WebPath, "tv")), RequestPath = "/tv" });
app.MapGet("/", () => Results.Redirect("/tv/"));
app.MapGet("/admin/", (HttpRequest request) => request.Path.Value?.EndsWith('/') == false
    ? Results.Redirect("/admin/" + request.QueryString)
    : File.Exists(Path.Combine(layout.WebPath, "admin/index.html"))
    ? Results.File(Path.Combine(layout.WebPath, "admin/index.html"), "text/html; charset=utf-8")
    : Results.Problem("ابتدا رابط وب را با npm run build بسازید.", statusCode: 503));
app.MapGet("/tv/", () => File.Exists(Path.Combine(layout.WebPath, "tv/index.html"))
    ? Results.File(Path.Combine(layout.WebPath, "tv/index.html"), "text/html; charset=utf-8")
    : Results.Problem("ابتدا رابط وب را بسازید.", statusCode: 503));
app.MapGet("/tv/simple", (HttpRequest request) => SimpleTvPage.Render(request, repository, new ProfileRepository(layout)));
foreach (var path in new[] { "/api/catalog", "/api/viewer-folders", "/api/location/{id}", "/media/{id}", "/health" })
    app.MapMethods(path, ["GET", "HEAD"], async (HttpContext context, IHttpForwarder forwarder, MediaRuntimeClient runtimeClient) =>
    {
        var peer = await runtimeClient.ReadReadyAsync(context.RequestAborted);
        if (peer is null)
        { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "پخش این کتابخانه آماده نیست؛ وضعیت سرویس را بررسی کنید." }); return; }
        // Replace any client-supplied value. The receiving instance checks it too,
        // so a different process taking the port after this check cannot serve data.
        context.Request.Headers[RuntimeInstance.RequestHeader] = peer.Identity.InstanceId;
        await forwarder.SendAsync(context, mediaDestination, mediaClient, forwarding, HttpTransformer.Default);
    });
var management = app.MapGroup("/admin/api");
ProfileEndpoints.Map(app, new ProfileRepository(layout), repository);
app.MapGet("/thumbnails/{id}", async (string id, HttpContext context, ThumbnailService thumbnails) =>
    await Thumbnail(id, true, context, thumbnails));
management.MapGet("/thumbnails/{id}", async (string id, HttpContext context, ThumbnailService thumbnails) =>
    await Thumbnail(id, false, context, thumbnails));
management.MapGet("/session", () => Results.Ok(new { token, root = layout.Root,
    publicPort = layout.Configuration.PublicPort, viewerUrls = LanAccess.ViewerUrls(layout.Configuration.PublicPort), runtime = instance.Identity }));
management.MapGet("/settings", ToolSettings);
management.MapPost("/settings", async (ProbeSettingsRequest request, HttpContext context, MediaToolCatalog tools) =>
{
    if (!CanConfigureTools(context))
        return Results.Json(new { error = "برای تغییر مسیر ابزار، داشبورد را روی دستگاه میزبان با آدرس localhost باز کن." }, statusCode: 403);
    if (request.FfprobePath is null) return Results.BadRequest(new { error = "مقدار مسیر ابزار در درخواست مشخص نشده است." });
    try
    {
        layout.SaveProbePath(request.FfprobePath);
        return await ToolSettings(context, tools);
    }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});
management.MapGet("/status", async (WindowsServiceManager services, MediaRuntimeClient runtimeClient) =>
{
    var mediaOnline = await runtimeClient.ReadReadyAsync() is not null;
    return Results.Ok(new { statistics = repository.Statistics(), service = services.ReadState(), mediaOnline, jobs = repository.Jobs() });
});
management.MapGet("/sources", () => Results.Ok(repository.Sources()));
management.MapPost("/sources", (AddSourceRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Path) || request.Path.Length > 4096 || request.Name?.Length > 256)
        return Results.BadRequest(new { error = "نام یا مسیر پوشه معتبر نیست." });
    var source = repository.AddSource(request.Name ?? "", request.Path);
    return Results.Created($"/api/sources/{source.Id}", new { source, job = repository.QueueScan(source.Id) });
});
management.MapPost("/sources/{id}/scan", (string id) => repository.Sources().Any(source => source.Id == id)
    ? Results.Ok(repository.QueueScan(id)) : Results.NotFound());
management.MapGet("/media", (HttpRequest request) =>
{
    var offset = int.TryParse(request.Query["offset"], out var start) ? start : 0;
    var limit = int.TryParse(request.Query["limit"], out var count) ? count : 50;
    var sourceId = request.Query["sourceId"].ToString();
    var kind = request.Query["kind"].ToString();
    var state = request.Query["state"].ToString();
    return Results.Ok(repository.Candidates(sourceId.Length == 0 ? null : sourceId, request.Query["search"], offset, limit,
        kind: kind.Length == 0 ? null : kind, state: state.Length == 0 ? null : state,
        folder: request.Query.ContainsKey("folder") ? request.Query["folder"].ToString() : null));
});
management.MapGet("/folders", (HttpRequest request) => Results.Ok(repository.Folders(
    EmptyToNull(request.Query["sourceId"]), request.Query["folder"], search: request.Query["search"],
    kind: EmptyToNull(request.Query["kind"]), state: EmptyToNull(request.Query["state"]))));
management.MapPost("/folders/publication", (FolderPublicationRequest request) => Results.Ok(new
{
    updated = repository.SetFolderPublished(request.SourceId, request.Folder, request.Published,
        request.Search, EmptyToNull(request.Kind), EmptyToNull(request.State))
}));
management.MapPost("/publication", (PublicationRequest request) => request.Ids is null
    ? Results.BadRequest(new { error = "فایلی انتخاب نشده است." })
    : Results.Ok(new { updated = repository.SetPublished(request.Ids, request.Published) }));
management.MapPost("/service/{action}", async (string action, WindowsServiceManager services) =>
{
    if (!await serviceGate.WaitAsync(0)) return Results.Conflict(new { error = "یک عملیات سرویس در حال انجام است." });
    try
    {
        var result = await services.RequestAsync(action, CancellationToken.None);
        return result.Success ? Results.Ok(result) : Results.Conflict(new { error = result.Message });
    }
    finally { serviceGate.Release(); }
});
app.Logger.LogInformation("Didanihaa LAN: http://localhost:{Port}/admin/ — root: {Root}", layout.Configuration.PublicPort, layout.Root);
await app.RunAsync();
return 0;

static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

static bool CanConfigureTools(HttpContext context) =>
    context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote)
    && context.Connection.LocalIpAddress is { } local && IPAddress.IsLoopback(local.IsIPv4MappedToIPv6 ? local.MapToIPv4() : local);

async Task<IResult> ToolSettings(HttpContext context, MediaToolCatalog tools)
{
    var probe = tools.ProbeAsync(context.RequestAborted);
    var encoder = tools.EncoderAsync(context.RequestAborted);
    await Task.WhenAll(probe, encoder);
    var canConfigure = CanConfigureTools(context);
    return Results.Ok(new
    {
        ffprobePath = canConfigure ? layout.MediaToolOverride : "",
        canConfigure,
        ffprobe = (await probe).Status,
        ffmpeg = (await encoder).Status
    });
}

async Task<IResult> Thumbnail(string id, bool publicOnly, HttpContext context, ThumbnailService thumbnails)
{
    var item = repository.Find(id, publicOnly);
    if (item is null || item.Kind != "video" || !item.Available) return Results.NotFound();
    var image = await thumbnails.GetAsync(id, publicOnly, context.RequestAborted);
    // Re-check publication even for cache hits; cached files are never served as a static directory.
    if (repository.Find(id, publicOnly) is not { Available: true }) return Results.NotFound();
    if (image is not null) return Results.File(image, "image/jpeg");
    return Results.Text("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"640\" height=\"360\" viewBox=\"0 0 640 360\"><rect width=\"640\" height=\"360\" fill=\"#26364d\"/><path d=\"M280 125l95 55-95 55z\" fill=\"#d0c7ee\"/></svg>", "image/svg+xml");
}

internal sealed record AddSourceRequest(string? Name, string Path);
internal sealed record PublicationRequest(string[]? Ids, bool Published);
internal sealed record ProbeSettingsRequest(string? FfprobePath);
internal sealed record FolderPublicationRequest(string SourceId, string Folder, bool Published, string? Search, string? Kind, string? State);
