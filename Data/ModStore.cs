using MySqlConnector;
using shiro_lq.Models;

namespace shiro_lq.Data;

public class ModStore(MySqlDataSource db, IWebHostEnvironment env)
{
    private const string Columns = "id, name, description, type, file_name";

    // Outside wwwroot so files are only reachable through the download endpoint.
    // shortcut: local disk storage, move to object storage (S3/Azure Blob) before running more than one server.
    public string FilesDir { get; } = Path.Combine(env.ContentRootPath, "uploads", "mods");

    public async Task EnsureTableAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("""
            CREATE TABLE IF NOT EXISTS mods (
                id INT AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                description TEXT NOT NULL,
                type VARCHAR(20) NOT NULL,
                file_name VARCHAR(255) NOT NULL
            )
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<Mod>> GetAllAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand($"SELECT {Columns} FROM mods ORDER BY id DESC", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var mods = new List<Mod>();
        while (await reader.ReadAsync()) mods.Add(Read(reader));
        return mods;
    }

    public async Task<Mod?> GetAsync(int id)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand($"SELECT {Columns} FROM mods WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    // Saves the file to disk and inserts the row; sets mod.Id and mod.FileName.
    public async Task AddAsync(Mod mod, Stream file, string originalFileName)
    {
        // Never use the client's name on disk: only its extension is kept (GetExtension can't contain path separators).
        mod.FileName = Guid.NewGuid() + Path.GetExtension(originalFileName);
        Directory.CreateDirectory(FilesDir);
        var path = Path.Combine(FilesDir, mod.FileName);
        try
        {
            await using (var fs = File.Create(path)) await file.CopyToAsync(fs);

            await using var conn = await db.OpenConnectionAsync();
            await using var cmd = new MySqlCommand(
                "INSERT INTO mods (name, description, type, file_name) VALUES (@name, @description, @type, @fileName)", conn);
            cmd.Parameters.AddWithValue("@name", mod.Name);
            cmd.Parameters.AddWithValue("@description", mod.Description);
            cmd.Parameters.AddWithValue("@type", mod.Type.ToString());
            cmd.Parameters.AddWithValue("@fileName", mod.FileName);
            await cmd.ExecuteNonQueryAsync();
            mod.Id = (int)cmd.LastInsertedId;
        }
        catch
        {
            File.Delete(path); // don't leave orphan or half-written files behind
            throw;
        }
    }

    private static Mod Read(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Name = r.GetString(1),
        Description = r.GetString(2),
        Type = Enum.Parse<ModType>(r.GetString(3)),
        FileName = r.GetString(4),
    };
}
