using System.Globalization;
using System.Text.Json;
using Didanihaa.Core;
using Microsoft.Data.Sqlite;

namespace Didanihaa.Infrastructure;

public sealed class CatalogRepository(RuntimeLayout layout)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string MediaFilter = """
         WHERE ($source IS NULL OR source_id=$source)
           AND ($ids IS NULL OR id IN (SELECT value FROM json_each($ids)))
           AND ($search='' OR instr(search_title,$search)>0)
           AND ($kind IS NULL OR kind=$kind)
           AND ($state IS NULL
             OR ($state='review' AND published=0 AND available=1)
             OR ($state='published' AND published=1 AND available=1)
             OR ($state='unavailable' AND available=0))
           AND ($folder IS NULL OR
             (substr(replace(relative_path,'\','/'),1,length($prefix))=$prefix
              AND instr(substr(replace(relative_path,'\','/'),length($prefix)+1),'/')=0))
        """;

    private SqliteConnection Open(bool readOnly = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = layout.DatabasePath, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = true, DefaultTimeout = 10
        }.ToString());
        connection.Open();
        connection.CreateCollation("MEDIA_NATURAL", MediaFolders.CompareNames);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;";
        command.ExecuteNonQuery();
        return connection;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(layout.DataPath);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version > 3) throw new InvalidOperationException("نسخه کاتالوگ از این برنامه جدیدتر است.");
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteScalar();
        if (version == 3) return;
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        if (version == 0)
        {
        command.CommandText = """
            CREATE TABLE sources(id TEXT PRIMARY KEY, name TEXT NOT NULL, path TEXT NOT NULL UNIQUE, created_utc TEXT NOT NULL);
            CREATE TABLE media(
                id TEXT PRIMARY KEY, source_id TEXT NOT NULL REFERENCES sources(id), relative_path TEXT NOT NULL,
                title TEXT NOT NULL, search_title TEXT NOT NULL, kind TEXT NOT NULL, mime_type TEXT NOT NULL,
                size_bytes INTEGER NOT NULL, modified_utc TEXT NOT NULL, published INTEGER NOT NULL DEFAULT 0,
                available INTEGER NOT NULL DEFAULT 1, metadata_json TEXT NOT NULL, seen_job TEXT NOT NULL,
                UNIQUE(source_id, relative_path));
            CREATE INDEX media_public ON media(published, available, title);
            CREATE INDEX media_source ON media(source_id, title);
            CREATE TABLE scan_jobs(id TEXT PRIMARY KEY, source_id TEXT NOT NULL REFERENCES sources(id),
                status TEXT NOT NULL, scanned INTEGER NOT NULL DEFAULT 0, errors INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL, finished_utc TEXT, message TEXT);
            CREATE UNIQUE INDEX one_scan_per_source ON scan_jobs(source_id) WHERE status IN ('queued','running');
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
        }
        if (version < 2)
        {
        command.CommandText = """
            CREATE TABLE profiles(id TEXT PRIMARY KEY, name TEXT NOT NULL, autoplay INTEGER NOT NULL DEFAULT 1, repeat_folder INTEGER NOT NULL DEFAULT 0);
            INSERT INTO profiles(id,name) VALUES('home','خانه');
            CREATE TABLE profile_favorites(profile_id TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
                media_id TEXT NOT NULL REFERENCES media(id) ON DELETE CASCADE, created_utc TEXT NOT NULL, PRIMARY KEY(profile_id,media_id));
            PRAGMA user_version=2;
            """;
        command.ExecuteNonQuery();
        }
        command.CommandText = """
            ALTER TABLE profiles ADD COLUMN nav_list_limit INTEGER NOT NULL DEFAULT 5 CHECK(nav_list_limit BETWEEN 1 AND 20);
            CREATE TABLE media_plays(media_id TEXT PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
                play_count INTEGER NOT NULL DEFAULT 0, last_played_utc TEXT NOT NULL);
            PRAGMA user_version=3;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public IReadOnlyList<SourceFolder> Sources()
    {
        using var connection = Open(true);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,name,path,created_utc FROM sources ORDER BY created_utc;";
        using var reader = command.ExecuteReader();
        List<SourceFolder> items = [];
        while (reader.Read()) items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            layout.ResolveSource(reader.GetString(2)), reader.GetString(3)));
        return items;
    }

    public Page<PublicPlaylist> Playlists(string? kind, string sort = "name", int offset = 0, int limit = 20, bool favoritesOnly = false, string? search = null)
    {
        if (kind is not (null or "audio" or "video") || sort is not ("name" or "popular")) throw new ArgumentException("نوع یا ترتیب پلی‌لیست معتبر نیست.");
        offset = Math.Max(0, offset); limit = Math.Clamp(limit, 1, 100);
        using var connection = Open(true); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.source_id,m.relative_path,s.name,m.kind,m.id,coalesce(p.play_count,0)
            FROM media m JOIN sources s ON s.id=m.source_id LEFT JOIN media_plays p ON p.media_id=m.id
            WHERE m.published=1 AND m.available=1 AND ($kind IS NULL OR m.kind=$kind)
              AND ($favorites=0 OR EXISTS(SELECT 1 FROM profile_favorites f WHERE f.profile_id='home' AND f.media_id=m.id))
            """;
        command.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value); command.Parameters.AddWithValue("$favorites", favoritesOnly);
        using var reader = command.ExecuteReader();
        List<(string Source, string Folder, string SourceName, string Kind, string Id, long Plays)> rows = [];
        while (reader.Read())
        {
            var path = reader.GetString(1).Replace('\\', '/'); var separator = path.LastIndexOf('/');
            rows.Add((reader.GetString(0), separator < 0 ? "" : path[..separator], reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5)));
        }
        var normalizedSearch = SearchText.Normalize(search ?? "");
        var playlists = rows.GroupBy(row => (row.Source, row.Folder)).Select(group =>
        {
            var first = group.First(); var thumbnail = group.FirstOrDefault(row => row.Kind == "video").Id;
            return new PublicPlaylist(first.Source, first.Folder, first.Folder.Length == 0 ? first.SourceName : first.Folder.Split('/')[^1], first.SourceName,
                group.Count(), group.Sum(row => row.Plays), thumbnail is null ? null : $"/thumbnails/{thumbnail}");
        }).Where(item => (sort != "popular" || item.PlayCount > 0) && (normalizedSearch.Length == 0 || SearchText.Normalize(item.Name + " " + item.SourceName + " " + item.Folder).Contains(normalizedSearch, StringComparison.Ordinal))).ToArray();
        var names = Comparer<string>.Create(MediaFolders.CompareNames);
        var ordered = sort == "popular" ? playlists.OrderByDescending(item => item.PlayCount).ThenBy(item => item.Name, names)
            : playlists.OrderBy(item => item.Name, names).ThenBy(item => item.SourceName, names);
        return new(ordered.ThenBy(item => item.SourceId, StringComparer.Ordinal).ThenBy(item => item.Folder, names).Skip(offset).Take(limit).ToArray(), playlists.Length, offset, limit);
    }

    public SourceFolder AddSource(string name, string path)
    {
        path = Path.GetFullPath(Path.Combine(layout.Root, path.Trim()));
        if (!Directory.Exists(path)) throw new ArgumentException("پوشه وجود ندارد یا قابل دسترسی نیست.");
        PathSafety.RejectReparsePoints(path);
        // Runtime internals must never become a source, even through an ancestor folder.
        foreach (var protectedPath in new[] { layout.DataPath, layout.TempPath, layout.WebPath, Path.GetDirectoryName(layout.HostExecutable)! })
            if (PathSafety.IsWithin(path, protectedPath) || PathSafety.IsWithin(protectedPath, path))
                throw new ArgumentException("پوشه منبع باید از فایل‌های اجرایی و داده‌های سرویس جدا باشد.");
        if (Sources().Any(source => PathSafety.IsWithin(source.ResolvedPath, path) || PathSafety.IsWithin(path, source.ResolvedPath)))
            throw new ArgumentException("این پوشه با یک منبع موجود هم‌پوشانی دارد.");
        var source = new SourceFolder(Guid.NewGuid().ToString("N"), string.IsNullOrWhiteSpace(name)
            ? new DirectoryInfo(path).Name : name.Trim(), layout.StoreSource(path), path, UtcNow());
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO sources VALUES($id,$name,$path,$created);";
        Add(command, ("$id", source.Id), ("$name", source.Name), ("$path", source.StoredPath), ("$created", source.CreatedUtc));
        command.ExecuteNonQuery();
        return source;
    }

    public Page<MediaCandidate> Candidates(string? sourceId, string? search, int offset, int limit, bool publicOnly = false, string? kind = null, string? state = null, string? folder = null, IReadOnlyList<string>? ids = null)
    {
        if (state is not (null or "review" or "published" or "unavailable")) throw new ArgumentException("وضعیت فیلتر معتبر نیست.");
        if (kind is not (null or "video" or "audio")) throw new ArgumentException("نوع رسانه معتبر نیست.");
        offset = Math.Max(0, offset); limit = Math.Clamp(limit, 1, 100);
        using var connection = Open(true);
        using var command = connection.CreateCommand();
        var where = MediaFilter + (publicOnly ? " AND published=1 AND available=1" : "");
        AddMediaParameters(command, sourceId, search, kind, state, folder);
        command.Parameters["$ids"].Value = ids is null ? DBNull.Value : JsonSerializer.Serialize(ids.Take(200));
        command.CommandText = "SELECT count(*) FROM media" + where;
        var total = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        command.CommandText = "SELECT id,source_id,relative_path,title,kind,mime_type,size_bytes,modified_utc,published,available,metadata_json FROM media"
            + where + (folder is null ? " ORDER BY title COLLATE NOCASE,id" : " ORDER BY relative_path COLLATE MEDIA_NATURAL,id") + " LIMIT $limit OFFSET $offset;";
        Add(command, ("$limit", limit), ("$offset", offset));
        using var reader = command.ExecuteReader();
        List<MediaCandidate> items = [];
        while (reader.Read()) items.Add(ReadMedia(reader));
        return new(items, total, offset, limit);
    }

    public FolderListing Folders(string? sourceId, string? path, bool publicOnly = false, string? search = null, string? kind = null, string? state = null)
    {
        var folder = MediaFolders.Normalize(path ?? "");
        var sources = Sources();
        if (sourceId is null)
        {
            List<MediaFolder> roots = [];
            foreach (var source in sources)
            {
                var summary = ReadFolder(source, "", publicOnly, search, kind, state);
                var total = summary.DirectFiles + summary.Folders.Sum(child => child.TotalFiles);
                var published = summary.DirectPublishedFiles + summary.Folders.Sum(child => child.PublishedFiles);
                if (!publicOnly || total > 0) roots.Add(new(source.Id, "", source.Name, total, summary.DirectFiles, published));
            }
            return new(null, "", null, roots, 0, 0, 0);
        }
        var selected = sources.FirstOrDefault(source => source.Id == sourceId);
        return selected is null ? new(sourceId, folder, null, [], 0, 0, 0) : ReadFolder(selected, folder, publicOnly, search, kind, state);
    }

    private FolderListing ReadFolder(SourceFolder source, string folder, bool publicOnly, string? search, string? kind, string? state)
    {
        using var connection = Open(true); using var command = connection.CreateCommand();
        command.CommandText = "SELECT relative_path,published,available FROM media" + MediaFilter + (publicOnly ? " AND published=1 AND available=1" : "");
        AddMediaParameters(command, source.Id, search, kind, state, null);
        using var reader = command.ExecuteReader();
        var prefix = folder.Length == 0 ? "" : folder + "/";
        var groups = new Dictionary<string, (int Total, int Direct, int Published)>(StringComparer.Ordinal);
        var direct = 0; var available = 0; var directPublished = 0;
        while (reader.Read())
        {
            var relative = reader.GetString(0).Replace('\\', '/');
            if (!relative.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var remainder = relative[prefix.Length..]; var separator = remainder.IndexOf('/');
            var published = reader.GetBoolean(1) && reader.GetBoolean(2);
            if (separator < 0)
            { direct++; if (reader.GetBoolean(2)) available++; if (published) directPublished++; continue; }
            var name = remainder[..separator];
            groups.TryGetValue(name, out var group);
            groups[name] = (group.Total + 1, group.Direct + (remainder[(separator + 1)..].Contains('/') ? 0 : 1), group.Published + (published ? 1 : 0));
        }
        var folders = groups.Select(group => new MediaFolder(source.Id, prefix + group.Key, group.Key, group.Value.Total, group.Value.Direct, group.Value.Published))
            .OrderBy(item => item.Name, Comparer<string>.Create(MediaFolders.CompareNames)).ToList();
        return new(source.Id, folder, source.Name, folders, direct, available, directPublished);
    }

    public int SetFolderPublished(string sourceId, string folder, bool published, string? search = null, string? kind = null, string? state = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentNullException.ThrowIfNull(folder);
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE media SET published=$published" + MediaFilter + (published ? " AND available=1" : "");
        AddMediaParameters(command, sourceId, search, kind, state, folder);
        Add(command, ("$published", published ? 1 : 0));
        return command.ExecuteNonQuery();
    }

    private static void AddMediaParameters(SqliteCommand command, string? sourceId, string? search, string? kind, string? state, string? folder)
    {
        if (state is not (null or "review" or "published" or "unavailable")) throw new ArgumentException("وضعیت فیلتر معتبر نیست.");
        if (kind is not (null or "video" or "audio")) throw new ArgumentException("نوع رسانه معتبر نیست.");
        if (folder is not null && string.IsNullOrEmpty(sourceId)) throw new ArgumentException("ابتدا پوشهٔ منبع را انتخاب کنید.");
        var normalized = folder is null ? null : MediaFolders.Normalize(folder);
        Add(command, ("$source", sourceId), ("$search", SearchText.Normalize(search ?? "")), ("$kind", kind), ("$state", state),
            ("$folder", normalized), ("$prefix", string.IsNullOrEmpty(normalized) ? "" : normalized + "/"), ("$ids", null));
    }

    public MediaLocation? LocatePublished(string id)
    {
        var item = Find(id, true); if (item is null) return null;
        var path = item.RelativePath.Replace('\\', '/'); var separator = path.LastIndexOf('/');
        var folder = separator < 0 ? "" : path[..separator];
        using var connection = Open(true); using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(CASE WHEN replace(relative_path,'\\','/') COLLATE MEDIA_NATURAL < $target THEN 1 END),COUNT(*) FROM media"
            + MediaFilter + " AND published=1 AND available=1";
        AddMediaParameters(command, item.SourceId, null, null, null, folder); Add(command, ("$target", path));
        using var reader = command.ExecuteReader(); reader.Read();
        var name = folder.Length > 0 ? folder.Split('/')[^1] : Sources().FirstOrDefault(source => source.Id == item.SourceId)?.Name ?? "پلی‌لیست";
        return new(item.SourceId, folder, name, reader.GetInt32(0), reader.GetInt32(1));
    }

    public MediaCandidate? Find(string id, bool publicOnly = false)
    {
        using var connection = Open(true);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,source_id,relative_path,title,kind,mime_type,size_bytes,modified_utc,published,available,metadata_json FROM media WHERE id=$id"
            + (publicOnly ? " AND published=1 AND available=1" : "");
        Add(command, ("$id", id));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadMedia(reader) : null;
    }

    public MediaCandidate? FindByPath(string sourceId, string relativePath)
    {
        using var connection = Open(true);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,source_id,relative_path,title,kind,mime_type,size_bytes,modified_utc,published,available,metadata_json FROM media WHERE source_id=$source AND relative_path=$path";
        Add(command, ("$source", sourceId), ("$path", relativePath));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadMedia(reader) : null;
    }

    public void SaveScanned(MediaCandidate item, string jobId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO media(id,source_id,relative_path,title,search_title,kind,mime_type,size_bytes,modified_utc,published,available,metadata_json,seen_job)
            VALUES($id,$source,$path,$title,$search,$kind,$mime,$size,$modified,0,1,$metadata,$job)
            ON CONFLICT(source_id,relative_path) DO UPDATE SET
                title=excluded.title,search_title=excluded.search_title,kind=excluded.kind,mime_type=excluded.mime_type,
                published=CASE WHEN media.size_bytes=excluded.size_bytes AND media.modified_utc=excluded.modified_utc THEN media.published ELSE 0 END,
                size_bytes=excluded.size_bytes,modified_utc=excluded.modified_utc,available=1,metadata_json=excluded.metadata_json,seen_job=excluded.seen_job;
            """;
        Add(command, ("$id", item.Id), ("$source", item.SourceId), ("$path", item.RelativePath), ("$title", item.Title),
            ("$search", SearchText.Normalize(item.Title)), ("$kind", item.Kind), ("$mime", item.MimeType),
            ("$size", item.SizeBytes), ("$modified", item.ModifiedUtc), ("$metadata", JsonSerializer.Serialize(item.Metadata, Json)), ("$job", jobId));
        command.ExecuteNonQuery();
    }

    public int SetPublished(IReadOnlyList<string> ids, bool published)
    {
        if (ids.Count is < 1 or > 100) throw new ArgumentException("بین ۱ تا ۱۰۰ مورد انتخاب کنید.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var updated = 0;
        foreach (var id in ids.Distinct())
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE media SET published=$published WHERE id=$id" + (published ? " AND available=1" : "");
            Add(command, ("$id", id), ("$published", published ? 1 : 0));
            updated += command.ExecuteNonQuery();
        }
        transaction.Commit();
        return updated;
    }

    public CatalogStatistics Statistics()
    {
        using var connection = Open(true);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT count(*) FROM sources),count(*),coalesce(sum(published=1 AND available=1),0),coalesce(sum(available=0),0) FROM media;";
        using var reader = command.ExecuteReader(); reader.Read();
        return new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    public ScanJob QueueScan(string sourceId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var id = Guid.NewGuid().ToString("N");
        command.CommandText = "INSERT OR IGNORE INTO scan_jobs(id,source_id,status,created_utc) VALUES($id,$source,'queued',$created);";
        Add(command, ("$id", id), ("$source", sourceId), ("$created", UtcNow()));
        command.ExecuteNonQuery();
        return Jobs().First(job => job.SourceId == sourceId && job.Status is "queued" or "running");
    }

    public IReadOnlyList<ScanJob> Jobs()
    {
        using var connection = Open(true);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,source_id,status,scanned,errors,created_utc,finished_utc,message FROM scan_jobs ORDER BY created_utc DESC LIMIT 100;";
        using var reader = command.ExecuteReader();
        List<ScanJob> jobs = [];
        while (reader.Read()) jobs.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
            reader.GetInt32(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return jobs;
    }

    public bool ClaimScan(string jobId)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE scan_jobs SET status='running' WHERE id=$id AND status='queued';";
        Add(command, ("$id", jobId));
        return command.ExecuteNonQuery() == 1;
    }

    public void RecoverInterruptedScans()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE scan_jobs SET status='interrupted',finished_utc=$now,message='اسکن با بسته شدن پنل قطع شد؛ دوباره اسکن کنید.' WHERE status='running';";
        Add(command, ("$now", UtcNow())); command.ExecuteNonQuery();
    }

    public void UpdateScan(string id, int scanned, int errors, string status, string? message = null)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE scan_jobs SET scanned=$scanned,errors=$errors,status=$status,message=$message,finished_utc=$finished WHERE id=$id;";
        Add(command, ("$id", id), ("$scanned", scanned), ("$errors", errors), ("$status", status), ("$message", message),
            ("$finished", status is "queued" or "running" ? null : UtcNow()));
        command.ExecuteNonQuery();
    }

    public void MarkMissing(string sourceId, string jobId)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE media SET available=0 WHERE source_id=$source AND seen_job<>$job;";
        Add(command, ("$source", sourceId), ("$job", jobId)); command.ExecuteNonQuery();
    }

    private static MediaCandidate ReadMedia(SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
        reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetString(7), reader.GetBoolean(8),
        reader.GetBoolean(9), JsonSerializer.Deserialize<MediaMetadata>(reader.GetString(10), Json) ?? MediaMetadata.Basic);

    private static void Add(SqliteCommand command, params (string Name, object? Value)[] values)
    {
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
    public static string UtcNow() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}
