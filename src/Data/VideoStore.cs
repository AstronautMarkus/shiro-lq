using MySqlConnector;
using shiro_lq.Models;

namespace shiro_lq.Data;

public class VideoStore(MySqlDataSource db)
{
    // Needs the channels table first (channel_id points there). A deleted channel takes its videos with it.
    public async Task EnsureTableAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("""
            CREATE TABLE IF NOT EXISTS videos (
                id VARCHAR(20) PRIMARY KEY,
                channel_id INT NOT NULL,
                title VARCHAR(255) NOT NULL,
                published_at DATETIME NOT NULL,
                INDEX (published_at),
                FOREIGN KEY (channel_id) REFERENCES channels(id) ON DELETE CASCADE
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // Newest first, with each one's channel name. A null channelId is every channel; a null limit, every video.
    public async Task<List<Video>> GetLatestAsync(int? channelId = null, int? limit = null)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand($"""
            SELECT v.id, v.channel_id, c.name, v.title, v.published_at
            FROM videos v JOIN channels c ON c.id = v.channel_id
            {(channelId is null ? "" : "WHERE v.channel_id = @channelId")}
            ORDER BY v.published_at DESC {(limit is null ? "" : "LIMIT @limit")}
            """, conn);
        cmd.Parameters.AddWithValue("@channelId", channelId);
        cmd.Parameters.AddWithValue("@limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync();
        var videos = new List<Video>();
        while (await reader.ReadAsync())
            videos.Add(new()
            {
                Id = reader.GetString(0),
                ChannelId = reader.GetInt32(1),
                ChannelName = reader.GetString(2),
                Title = reader.GetString(3),
                PublishedAt = reader.GetDateTime(4),
            });
        return videos;
    }

    // Saves a channel's latest videos (its feed) in one transaction: new ones appear and edited titles change.
    // Every video since the oldest of these is in the list, so a saved one from that time that's missing was deleted
    // or made private on YouTube, and goes. Older videos are kept as they are: the feed can't say anything about them.
    public async Task SaveLatestAsync(int channelId, IReadOnlyList<Video> videos)
    {
        if (videos.Count == 0) return;
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var cmd = new MySqlCommand("DELETE FROM videos WHERE channel_id = @channelId AND published_at >= @oldest", conn, tx))
        {
            cmd.Parameters.AddWithValue("@channelId", channelId);
            cmd.Parameters.AddWithValue("@oldest", videos.Min(v => v.PublishedAt));
            await cmd.ExecuteNonQueryAsync();
        }

        // One multi-row INSERT instead of a round trip per video.
        // IGNORE: a video already listed under another channel (the same channel added twice, by handle and by id) stays there.
        await using var insert = new MySqlCommand("", conn, tx);
        insert.Parameters.AddWithValue("@channelId", channelId);
        var rows = new List<string>();
        for (var i = 0; i < videos.Count; i++)
        {
            rows.Add($"(@id{i}, @channelId, @title{i}, @publishedAt{i})");
            insert.Parameters.AddWithValue($"@id{i}", videos[i].Id);
            insert.Parameters.AddWithValue($"@title{i}", videos[i].Title);
            insert.Parameters.AddWithValue($"@publishedAt{i}", videos[i].PublishedAt);
        }
        insert.CommandText = $"INSERT IGNORE INTO videos (id, channel_id, title, published_at) VALUES {string.Join(", ", rows)}";
        await insert.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }
}
