using System.Net;
using System.Text;
using Didanihaa.Core;
using Didanihaa.Infrastructure;

namespace Didanihaa.Control;

internal static class SimpleTvPage
{
    public static IResult Render(HttpRequest request, CatalogRepository repository, ProfileRepository profiles)
    {
        var query = request.Query;
        var source = query["sourceId"].ToString(); var folder = MediaFolders.Normalize(query["folder"].ToString());
        var kind = query["kind"].ToString() is "video" or "audio" ? query["kind"].ToString() : "";
        var search = query["search"].ToString(); var favorites = query["view"] == "favorites";
        var collectionView = query["view"].ToString() is "popular" or "playlists" ? query["view"].ToString() : "";
        var offset = int.TryParse(query["offset"], out var start) ? Math.Max(0, start) : 0;
        var selected = int.TryParse(query["index"], out var index) && index >= 0 ? index : -1;
        if (selected >= 0 && (selected < offset || selected >= (long)offset + 20)) offset = selected / 20 * 20;
        if (query["playId"].ToString() is { Length: > 0 } id)
            return repository.LocatePublished(id) is { } location ? Results.Redirect(Url(location.SourceId, location.Folder, "", "", 0, location.Index)) : Results.NotFound();
        var profile = profiles.Read();
        var collection = collectionView.Length > 0 ? repository.Playlists(kind.Length == 0 ? null : kind, collectionView == "popular" ? "popular" : "name", offset, 20, search: search) : null;
        var folders = collection is not null ? new FolderListing(null, "", null, collection.Items.Select(item => new MediaFolder(item.SourceId, item.Folder, item.Name, item.MediaCount, item.MediaCount, item.MediaCount)).ToArray(), 0, 0, 0)
            : favorites ? new FolderListing(null, "", null, [], 0, 0, 0) : repository.Folders(source.Length == 0 ? null : source, folder, true, search, kind.Length == 0 ? null : kind);
        var media = collection is not null ? new Page<MediaCandidate>([], collection.Total, offset, 20) : source.Length > 0 || favorites ? repository.Candidates(source.Length == 0 ? null : source, search, offset, 20, true,
            kind: kind.Length == 0 ? null : kind, folder: source.Length == 0 ? null : folder, ids: favorites ? profiles.Favorites() : null) : new Page<MediaCandidate>([], 0, 0, 20);
        var title = collectionView == "popular" ? "پربازدیدها" : collection is not null ? "پلی‌لیست‌های پوشه‌ای" : favorites ? "علاقه‌مندی‌ها" : folder.Split('/').LastOrDefault(part => part.Length > 0) ?? folders.SourceName ?? (kind == "audio" ? "صداها" : "ویدیوها و پوشه‌ها");
        var html = new StringBuilder("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>");
        html.Append(E(title)).Append(" · دیدنی‌ها</title><link rel=\"icon\" href=\"/assets/brand/app-icon.svg\" type=\"image/svg+xml\"><link rel=\"stylesheet\" href=\"/shared/simple-tv.css\"><link rel=\"stylesheet\" href=\"/shared/player-controls.css\"><script defer src=\"/shared/player-controls.js\"></script><script defer src=\"/shared/simple-tv.js\"></script></head><body class=\"simple-tv\"><header><a class=\"brand\" href=\"/tv/simple\"><img src=\"/assets/brand/logomark.svg\" width=\"38\" height=\"38\" alt=\"\"> دیدنی‌ها</a><span>نسخهٔ سادهٔ TV</span><a href=\"/tv/\">نسخهٔ اصلی</a></header><aside><nav aria-label=\"کتابخانه\"><div role=\"toolbar\" aria-label=\"بخش‌ها\" aria-orientation=\"vertical\">");
        Navigation(html, repository, profile.NavListLimit, "", "♡ علاقه‌مندی‌ها", true);
        Navigation(html, repository, profile.NavListLimit, "video", "▷ ویدیوها");
        Navigation(html, repository, profile.NavListLimit, "audio", "♫ صداها");
        html.Append("</div></nav><p>").Append(E(profile.Name)).Append(" · پروفایل مشترک</p></aside><main><h1>").Append(E(title)).Append("</h1><div class=\"simple-toolbar\">");
        if (source.Length > 0) Link(html, Url(folder.Length == 0 ? null : source, Parent(folder), kind, search), "بازگشت به " + (Parent(folder).Split('/').LastOrDefault(part => part.Length > 0) ?? "کتابخانه"));
        Link(html, Url(null, "", kind, ""), "کتابخانه");
        html.Append("</div><form method=\"get\"><label for=\"simple-search\">جست‌وجو</label><input id=\"simple-search\" name=\"search\" value=\"").Append(E(search)).Append("\"><input type=\"hidden\" name=\"sourceId\" value=\"").Append(E(source)).Append("\"><input type=\"hidden\" name=\"folder\" value=\"").Append(E(folder)).Append("\"><input type=\"hidden\" name=\"kind\" value=\"").Append(E(kind)).Append("\">");
        if (favorites) html.Append("<input type=\"hidden\" name=\"view\" value=\"favorites\">");
        else if (collectionView.Length > 0) html.Append("<input type=\"hidden\" name=\"view\" value=\"").Append(E(collectionView)).Append("\">");
        html.Append("<button>جست‌وجو</button></form>");
        if (selected >= 0 && source.Length > 0)
        {
            var current = repository.Candidates(source, search, selected, 1, true, kind: kind.Length == 0 ? null : kind, folder: folder).Items.FirstOrDefault();
            if (current is not null)
            {
                var tag = current.Kind == "video" ? "video" : "audio";
                html.Append("<section class=\"classic-player\" id=\"simple-player\"><h2>").Append(E(current.Title)).Append("</h2><div id=\"simple-media\"><").Append(tag).Append(" controls autoplay preload=\"metadata\" playsinline src=\"/media/").Append(E(current.Id)).Append('"');
                if (tag == "video") html.Append(" width=\"1280\" height=\"720\" poster=\"/thumbnails/").Append(E(current.Id)).Append('"');
                html.Append("></").Append(tag).Append("></div><div class=\"simple-toolbar\">");
                if (selected + 1 < media.Total || profile.RepeatFolder) Link(html, Url(source, folder, kind, search, offset, selected + 1 < media.Total ? selected + 1 : 0), "بعدی", "simple-next");
                if (selected > 0 || profile.RepeatFolder) Link(html, Url(source, folder, kind, search, offset, selected > 0 ? selected - 1 : media.Total - 1), "قبلی", "simple-previous");
                Link(html, Url(source, folder, kind, search, offset), "بستن پلیر");
                html.Append("<button id=\"simple-favorite\" type=\"button\" data-media-id=\"").Append(E(current.Id)).Append("\" disabled>علاقه‌مندی</button></div><p id=\"simple-status\" role=\"status\"></p></section>");
            }
        }
        if (selected >= 0 && source.Length > 0) html.Append("<h2 class=\"simple-list-heading\">قسمت‌های همین پوشه</h2>");
        html.Append("<div class=\"simple-list\" role=\"grid\" aria-label=\"پوشه‌ها و رسانه‌ها\">");
        foreach (var child in folders.Folders)
        {
            html.Append("<div role=\"row\"><div role=\"gridcell\">"); Link(html, Url(child.SourceId, child.Path, kind, collection is not null ? "" : search), child.Name + " · " + child.TotalFiles + " منتشرشده", className: "simple-item folder-item"); html.Append("</div></div>");
        }
        for (var position = 0; position < media.Items.Count; position++)
        {
            var item = media.Items[position]; var href = favorites ? "/tv/simple?playId=" + Uri.EscapeDataString(item.Id) : Url(source, folder, kind, search, offset, offset + position);
            html.Append("<div role=\"row\"><div role=\"gridcell\"><a class=\"simple-item\" href=\"").Append(E(href)).Append('"');
            if (selected == offset + position) html.Append(" aria-current=\"true\""); html.Append('>');
            if (item.Kind == "video") html.Append("<img width=\"160\" height=\"90\" loading=\"lazy\" alt=\"\" src=\"/thumbnails/").Append(E(item.Id)).Append("\">");
            html.Append("<span>").Append(E(item.Title)).Append("</span>"); if (selected == offset + position) html.Append("<small class=\"simple-current\">در حال پخش</small>"); html.Append("</a></div></div>");
        }
        html.Append("</div>");
        if (media.Items.Count == 0 && folders.Folders.Count == 0) html.Append("<p class=\"empty\">رسانه‌ای در این بخش نیست. علاقه‌مندی را از پلیر اضافه کن، یا پوشه‌ای را در ادمین منتشر کن.</p>");
        html.Append("<div class=\"simple-toolbar\">");
        if (offset + 20 < media.Total) Link(html, Url(source, folder, kind, search, offset + 20, favorites: favorites, view: collectionView), "صفحهٔ بعد");
        if (offset > 0) Link(html, Url(source, folder, kind, search, Math.Max(0, offset - 20), favorites: favorites, view: collectionView), "صفحهٔ قبل");
        html.Append("</div></main><footer>جهت‌ها: پیمایش · OK: انتخاب · Back: بازگشت</footer></body></html>");
        return Results.Content(html.ToString(), "text/html; charset=utf-8");
    }
    private static string E(string text) => WebUtility.HtmlEncode(text);
    private static string Parent(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
    private static string Url(string? source, string folder, string kind, string search, int offset = 0, int? index = null, bool favorites = false, string view = "")
    {
        var values = new List<KeyValuePair<string, string?>>();
        if (!string.IsNullOrEmpty(source)) { values.Add(new("sourceId", source)); values.Add(new("folder", folder)); }
        if (kind.Length > 0) values.Add(new("kind", kind)); if (search.Length > 0) values.Add(new("search", search));
        if (offset > 0) values.Add(new("offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (index.HasValue) values.Add(new("index", index.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (favorites) values.Add(new("view", "favorites"));
        else if (view.Length > 0) values.Add(new("view", view));
        return "/tv/simple" + QueryString.Create(values);
    }
    private static void Navigation(StringBuilder html, CatalogRepository repository, int limit, string kind, string name, bool favorites = false)
    {
        Link(html, Url(null, "", kind, "", favorites: favorites), name, className: "simple-category");
        var playlists = repository.Playlists(kind.Length == 0 ? null : kind, limit: limit, favoritesOnly: favorites);
        html.Append("<div class=\"simple-subnav\">");
        foreach (var item in playlists.Items) Link(html, Url(item.SourceId, item.Folder, kind, "", favorites: favorites), item.Name, className: "simple-folder-link");
        if (!favorites)
        {
            Link(html, Url(null, "", kind, "", view: "popular"), "↗ پربازدیدها", className: "simple-dynamic");
            Link(html, Url(null, "", kind, "", view: "playlists"), "نمایش همه", className: "simple-show-all");
        }
        else if (playlists.Total > limit) Link(html, Url(null, "", "", "", favorites: true), "نمایش همه", className: "simple-show-all");
        html.Append("</div>");
    }
    private static void Link(StringBuilder html, string href, string label, string? id = null, string? className = null)
    {
        html.Append("<a href=\"").Append(E(href)).Append('"'); if (id is not null) html.Append(" id=\"").Append(E(id)).Append('"');
        if (className is not null) html.Append(" class=\"").Append(E(className)).Append('"'); html.Append('>').Append(E(label)).Append("</a>");
    }
}
