using MySqlConnector;
using shiro_lq.Models;

namespace shiro_lq.Data;

public class ChannelStore(MySqlDataSource db)
{
    // Needs the videos table (VideoStore) for the count.
    private const string Select = """
        SELECT c.id, c.name, c.url, c.description, c.is_main, (SELECT COUNT(*) FROM videos v WHERE v.channel_id = c.id)
        FROM channels c
        """;

    public async Task EnsureTableAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("""
            CREATE TABLE IF NOT EXISTS channels (
                id INT AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                url VARCHAR(255) NOT NULL UNIQUE,
                description VARCHAR(500) NOT NULL,
                is_main BOOLEAN NOT NULL DEFAULT FALSE
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // The main channel first, then the rest by name.
    public async Task<List<Channel>> GetAllAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand($"{Select} ORDER BY c.is_main DESC, c.name", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var channels = new List<Channel>();
        while (await reader.ReadAsync()) channels.Add(Read(reader));
        return channels;
    }

    // Null only while there are no channels.
    public async Task<Channel?> GetMainAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand($"{Select} WHERE c.is_main", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    // Insert when Id is 0, update otherwise. False when another channel already has that URL.
    // A new channel becomes the main one when there is none (the first one). Updates never touch is_main.
    public async Task<bool> SaveAsync(Channel channel)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(channel.Id == 0
            ? "INSERT INTO channels (name, url, description, is_main) SELECT @name, @url, @description, NOT EXISTS(SELECT 1 FROM channels WHERE is_main)"
            : "UPDATE channels SET name = @name, url = @url, description = @description WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", channel.Id);
        cmd.Parameters.AddWithValue("@name", channel.Name);
        cmd.Parameters.AddWithValue("@url", channel.Url);
        cmd.Parameters.AddWithValue("@description", channel.Description ?? "");
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
        if (channel.Id == 0) channel.Id = (int)cmd.LastInsertedId;
        return true;
    }

    // One statement, so there's never zero or two main channels. False (and nothing changes) when the channel doesn't exist.
    public async Task<bool> SetMainAsync(int id)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("UPDATE channels c JOIN channels target ON target.id = @id SET c.is_main = (c.id = @id)", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // The main channel can't be deleted: make another one main first. False when nothing was deleted.
    public async Task<bool> DeleteAsync(int id)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("DELETE FROM channels WHERE id = @id AND NOT is_main", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static Channel Read(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Name = r.GetString(1),
        Url = r.GetString(2),
        Description = r.GetString(3),
        IsMain = r.GetBoolean(4),
        VideoCount = (int)r.GetInt64(5),
    };
}
