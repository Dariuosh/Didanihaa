using System.Security.Cryptography;
using System.Text;
using Didanihaa.Infrastructure;

namespace Didanihaa.Control;

internal static class ProfileEndpoints
{
    public static void Map(WebApplication app, ProfileRepository profiles, CatalogRepository catalog)
    {
        var mutationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        bool Authorized(HttpContext context)
        {
            var supplied = context.Request.Headers["X-Didanihaa-Profile"].ToString();
            return supplied.Length == mutationToken.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(supplied), Encoding.ASCII.GetBytes(mutationToken));
        }
        app.MapGet("/api/profile", () => Results.Ok(new { profile = profiles.Read(), favoriteIds = profiles.Favorites(true), mutationToken }));
        app.MapGet("/api/playlists", (HttpRequest request) => Results.Ok(catalog.Playlists(
            request.Query["kind"].ToString() is { Length: > 0 } kind ? kind : null,
            request.Query["sort"].ToString() is { Length: > 0 } sort ? sort : "name",
            int.TryParse(request.Query["offset"], out var offset) ? offset : 0,
            int.TryParse(request.Query["limit"], out var limit) ? limit : 20,
            request.Query["favorites"] == "true", request.Query["search"])));
        app.MapPost("/api/profile/playback", (HttpContext context, PlaybackRequest request) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            return Results.Ok(new { recorded = profiles.RecordPlayback(request.MediaId) });
        });
        app.MapPost("/api/profile/favorites", (HttpContext context, FavoriteRequest request) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            profiles.Favorite(request.MediaId, request.Favorite); return Results.Ok(new { favoriteIds = profiles.Favorites(true) });
        });
        app.MapPost("/api/profile/preferences", (HttpContext context, PreferencesRequest request) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            profiles.Preferences(request.Autoplay, request.RepeatFolder); return Results.Ok(new { saved = true });
        });
        app.MapGet("/admin/api/profile", () =>
        {
            var ids = profiles.Favorites();
            return Results.Ok(new { profile = profiles.Read(), favoriteIds = ids, items = ids.Select(id => catalog.Find(id))
                .OfType<Didanihaa.Core.MediaCandidate>().Select(item => new { item.Id, item.Title, visible = item.Published && item.Available }).ToArray() });
        });
        app.MapPost("/admin/api/profile/favorites", (FavoriteRequest request) => { profiles.Favorite(request.MediaId, request.Favorite); return Results.Ok(new { saved = true }); });
        app.MapPost("/admin/api/profile", (ProfileRequest request) =>
        { profiles.Update(request.Name, request.Autoplay, request.RepeatFolder, request.NavListLimit); return Results.Ok(new { saved = true }); });
    }
    private sealed record FavoriteRequest(string MediaId, bool Favorite);
    private sealed record PreferencesRequest(bool Autoplay, bool RepeatFolder);
    private sealed record ProfileRequest(string Name, bool Autoplay, bool RepeatFolder, int? NavListLimit = null);
    private sealed record PlaybackRequest(string MediaId);
}
