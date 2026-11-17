using Microsoft.AspNetCore.Components.Forms;
using MySqlConnector;
using shiro_lq.Models;

namespace shiro_lq.Data;

public class ModStore(MySqlDataSource db, IFileStorage storage)
{
    public const long MaxFileSize = 500L * 1024 * 1024;
    public const long MaxImageSize = 10L * 1024 * 1024;
    public const int MaxImages = 10;

    // No SVG: images are served inline, and an SVG can carry scripts.
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private const string Columns = "id, name, description, type, file_name, thumbnail";

    // Storage key prefixes. Mod files are only reachable through the download endpoint; images are served under /images.
    public const string FilesDir = "mods";
    public const string ImagesDir = "images";

    public static bool IsImage(string fileName) => ImageExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    public async Task EnsureTablesAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("""
            CREATE TABLE IF NOT EXISTS mods (
                id INT AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                description TEXT NOT NULL,
                type VARCHAR(20) NOT NULL,
                file_name VARCHAR(255) NOT NULL,
                thumbnail VARCHAR(255) NOT NULL
            );
            CREATE TABLE IF NOT EXISTS mod_images (
                id INT AUTO_INCREMENT PRIMARY KEY,
                mod_id INT NOT NULL,
                file_name VARCHAR(255) NOT NULL,
                FOREIGN KEY (mod_id) REFERENCES mods(id) ON DELETE CASCADE
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // Without Images: the list only needs the thumbnail.
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
        Mod mod;
        await using (var cmd = new MySqlCommand($"SELECT {Columns} FROM mods WHERE id = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            mod = Read(reader);
        }

        await using var imagesCmd = new MySqlCommand("SELECT file_name FROM mod_images WHERE mod_id = @id ORDER BY id", conn);
        imagesCmd.Parameters.AddWithValue("@id", id);
        await using var imagesReader = await imagesCmd.ExecuteReaderAsync();
        while (await imagesReader.ReadAsync()) mod.Images.Add(imagesReader.GetString(0));
        return mod;
    }

    // Insert when mod.Id is 0, update otherwise. Saves the new files first, then the rows in one transaction; sets Id, FileName, Thumbnail and Images.
    // On update, a null file or thumbnail keeps the current one, and new images replace all current ones.
    public async Task SaveAsync(Mod mod, IBrowserFile? file, IBrowserFile? thumbnail, IReadOnlyList<IBrowserFile> images)
    {
        if (images.Count > MaxImages) throw new ArgumentException($"At most {MaxImages} images.");
        if ((thumbnail is not null && !IsImage(thumbnail.Name)) || !images.All(i => IsImage(i.Name))) throw new ArgumentException("Images must be " + string.Join(", ", ImageExtensions));
        var old = mod.Id == 0 ? null : await GetAsync(mod.Id) ?? throw new KeyNotFoundException($"Mod {mod.Id} not found.");
        if (old is null && (file is null || thumbnail is null)) throw new ArgumentException("A new mod needs a file and a thumbnail.");
        var replaceImages = old is null || images.Count > 0;

        var saved = new List<string>();
        try
        {
            mod.FileName = file is null ? old!.FileName : await SaveFileAsync(file, FilesDir, MaxFileSize, saved);
            mod.Thumbnail = thumbnail is null ? old!.Thumbnail : await SaveFileAsync(thumbnail, ImagesDir, MaxImageSize, saved);
            if (replaceImages)
            {
                mod.Images = [];
                foreach (var image in images) mod.Images.Add(await SaveFileAsync(image, ImagesDir, MaxImageSize, saved));
            }
            else mod.Images = old!.Images;

            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var cmd = new MySqlCommand(old is null
                ? "INSERT INTO mods (name, description, type, file_name, thumbnail) VALUES (@name, @description, @type, @fileName, @thumbnail)"
                : "UPDATE mods SET name = @name, description = @description, type = @type, file_name = @fileName, thumbnail = @thumbnail WHERE id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", mod.Id);
                cmd.Parameters.AddWithValue("@name", mod.Name);
                cmd.Parameters.AddWithValue("@description", mod.Description);
                cmd.Parameters.AddWithValue("@type", mod.Type.ToString());
                cmd.Parameters.AddWithValue("@fileName", mod.FileName);
                cmd.Parameters.AddWithValue("@thumbnail", mod.Thumbnail);
                await cmd.ExecuteNonQueryAsync();
                if (old is null) mod.Id = (int)cmd.LastInsertedId;
            }
            if (replaceImages)
            {
                await using (var cmd = new MySqlCommand("DELETE FROM mod_images WHERE mod_id = @modId", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@modId", mod.Id);
                    await cmd.ExecuteNonQueryAsync();
                }
                foreach (var image in mod.Images)
                {
                    await using var cmd = new MySqlCommand("INSERT INTO mod_images (mod_id, file_name) VALUES (@modId, @fileName)", conn, tx);
                    cmd.Parameters.AddWithValue("@modId", mod.Id);
                    cmd.Parameters.AddWithValue("@fileName", image);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            await tx.CommitAsync();
        }
        catch
        {
            foreach (var key in saved) await storage.DeleteAsync(key); // don't leave orphan or half-written files behind
            throw;
        }

        // Only after the commit: until then the old rows still point at these files.
        if (old is not null)
            foreach (var key in Keys(old).Except(Keys(mod))) await storage.DeleteAsync(key);
    }

    public async Task DeleteAsync(int id)
    {
        if (await GetAsync(id) is not { } mod) return;
        await using (var conn = await db.OpenConnectionAsync())
        await using (var cmd = new MySqlCommand("DELETE FROM mods WHERE id = @id", conn)) // mod_images go with it (ON DELETE CASCADE)
        {
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        foreach (var key in Keys(mod)) await storage.DeleteAsync(key);
    }

    // Every storage key a mod uses.
    private static IEnumerable<string> Keys(Mod mod) =>
        mod.Images.Select(i => $"{ImagesDir}/{i}").Append($"{ImagesDir}/{mod.Thumbnail}").Append($"{FilesDir}/{mod.FileName}");

    private async Task<string> SaveFileAsync(IBrowserFile file, string dir, long maxSize, List<string> saved)
    {
        // Never use the client's name in storage: only its extension is kept (GetExtension can't contain path separators).
        var name = Guid.NewGuid() + Path.GetExtension(file.Name);
        var key = $"{dir}/{name}";
        saved.Add(key);
        await using var input = file.OpenReadStream(maxSize);
        await storage.SaveAsync(key, input, file.Size);
        return name;
    }

    private static Mod Read(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Name = r.GetString(1),
        Description = r.GetString(2),
        Type = Enum.Parse<ModType>(r.GetString(3)),
        FileName = r.GetString(4),
        Thumbnail = r.GetString(5),
    };
}
