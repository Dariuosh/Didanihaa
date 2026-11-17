using System.Globalization;
using Didanihaa.Core;
using Microsoft.Data.Sqlite;

namespace Didanihaa.Infrastructure;

public sealed record HomeProfile(string Id, string Name, bool Autoplay, bool RepeatFolder, int NavListLimit = 5);

public sealed class ProfileRepository(RuntimeLayout layout)
{
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = layout.DatabasePath, Mode = SqliteOpenMode.ReadWrite, DefaultTimeout = 10 }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;"; command.ExecuteNonQuery(); return connection;
    }
    public HomeProfile Read()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,name,autoplay,repeat_folder,nav_list_limit FROM profiles WHERE id='home'";
        using var reader = command.ExecuteReader(); reader.Read(); return new(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetInt32(4));
    }
    public string[] Favorites(bool publicOnly = false)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT media_id FROM profile_favorites WHERE profile_id='home'"
            + (publicOnly ? " AND media_id IN (SELECT id FROM media WHERE published=1 AND available=1)" : "")
            + " ORDER BY created_utc DESC LIMIT 200";
        using var reader = command.ExecuteReader(); List<string> ids = []; while (reader.Read()) ids.Add(reader.GetString(0)); return ids.ToArray();
    }
    public void Favorite(string mediaId, bool favorite)
    {
        if (string.IsNullOrWhiteSpace(mediaId) || mediaId.Length > 100) throw new ArgumentException("شناسهٔ رسانه معتبر نیست.");
        using var connection = Open(); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.Parameters.AddWithValue("$media", mediaId);
        if (favorite)
        {
            command.CommandText = "SELECT COUNT(*) FROM profile_favorites WHERE profile_id='home' AND media_id<>$media";
            if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) >= 200) throw new ArgumentException("در این پروفایل تا ۲۰۰ علاقه‌مندی قابل ثبت است.");
            command.CommandText = "INSERT OR IGNORE INTO profile_favorites(profile_id,media_id,created_utc) SELECT 'home',$media,$now WHERE EXISTS(SELECT 1 FROM media WHERE id=$media AND published=1 AND available=1)";
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        else command.CommandText = "DELETE FROM profile_favorites WHERE profile_id='home' AND media_id=$media";
        command.ExecuteNonQuery(); transaction.Commit();
    }
    public void Preferences(bool autoplay, bool repeat)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE profiles SET autoplay=$autoplay,repeat_folder=$repeat WHERE id='home'";
        command.Parameters.AddWithValue("$autoplay", autoplay); command.Parameters.AddWithValue("$repeat", repeat); command.ExecuteNonQuery();
    }
    public bool RecordPlayback(string mediaId)
    {
        if (string.IsNullOrWhiteSpace(mediaId) || mediaId.Length > 100) throw new ArgumentException("شناسهٔ رسانه معتبر نیست.");
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO media_plays(media_id,play_count,last_played_utc)
            SELECT id,1,$now FROM media WHERE id=$media AND published=1 AND available=1
            ON CONFLICT(media_id) DO UPDATE SET play_count=media_plays.play_count+1,last_played_utc=excluded.last_played_utc
            """;
        command.Parameters.AddWithValue("$media", mediaId); command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        return command.ExecuteNonQuery() > 0;
    }
    public void Update(string name, bool autoplay, bool repeat, int? navListLimit = null)
    {
        name = name?.Trim() ?? ""; if (name.Length is < 1 or > 80) throw new ArgumentException("نام پروفایل باید بین ۱ تا ۸۰ نویسه باشد.");
        if (navListLimit is < 1 or > 20) throw new ArgumentException("تعداد لیست‌های منو باید بین ۱ تا ۲۰ باشد.");
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE profiles SET name=$name,autoplay=$autoplay,repeat_folder=$repeat,nav_list_limit=coalesce($limit,nav_list_limit) WHERE id='home'";
        command.Parameters.AddWithValue("$limit", (object?)navListLimit ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", name); command.Parameters.AddWithValue("$autoplay", autoplay); command.Parameters.AddWithValue("$repeat", repeat); command.ExecuteNonQuery();
    }
}
