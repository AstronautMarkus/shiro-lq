using Microsoft.AspNetCore.Components.Forms;
using MySqlConnector;
using shiro_lq.Models;

namespace shiro_lq.Data;

public class ModStore(MySqlDataSource db, IWebHostEnvironment env)
{
    public const long MaxFileSize = 500L * 1024 * 1024;
    public const long MaxImageSize = 10L * 1024 * 1024;
    public const int MaxImages = 10;

    // No SVG: images are served inline, and an SVG can carry scripts.
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private const string Columns = "id, name, description, type, file_name, thumbnail";

    // shortcut: local disk storage, move to object storage (S3/Azure Blob) before running more than one server.
    // Mod files stay private (only reachable through the download endpoint); images are served publicly under /images.
    public string FilesDir { get; } = Path.Combine(env.ContentRootPath, "uploads", "mods");
    public string ImagesDir { get; } = Path.Combine(env.ContentRootPath, "uploads", "images");

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

    // Saves every file to disk, then inserts the mod and its images in one transaction; sets Id, FileName, Thumbnail and Images.
    public async Task AddAsync(Mod mod, IBrowserFile file, IBrowserFile thumbnail, IReadOnlyList<IBrowserFile> images)
    {
        if (images.Count > MaxImages) throw new ArgumentException($"At most {MaxImages} images.");
        if (!IsImage(thumbnail.Name) || !images.All(i => IsImage(i.Name))) throw new ArgumentException("Images must be " + string.Join(", ", ImageExtensions));

        var saved = new List<string>();
        try
        {
            mod.FileName = await SaveAsync(file, FilesDir, MaxFileSize, saved);
            mod.Thumbnail = await SaveAsync(thumbnail, ImagesDir, MaxImageSize, saved);
            mod.Images = [];
            foreach (var image in images) mod.Images.Add(await SaveAsync(image, ImagesDir, MaxImageSize, saved));

            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var cmd = new MySqlCommand(
                "INSERT INTO mods (name, description, type, file_name, thumbnail) VALUES (@name, @description, @type, @fileName, @thumbnail)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@name", mod.Name);
                cmd.Parameters.AddWithValue("@description", mod.Description);
                cmd.Parameters.AddWithValue("@type", mod.Type.ToString());
                cmd.Parameters.AddWithValue("@fileName", mod.FileName);
                cmd.Parameters.AddWithValue("@thumbnail", mod.Thumbnail);
                await cmd.ExecuteNonQueryAsync();
                mod.Id = (int)cmd.LastInsertedId;
            }
            foreach (var image in mod.Images)
            {
                await using var cmd = new MySqlCommand("INSERT INTO mod_images (mod_id, file_name) VALUES (@modId, @fileName)", conn, tx);
                cmd.Parameters.AddWithValue("@modId", mod.Id);
                cmd.Parameters.AddWithValue("@fileName", image);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        catch
        {
            foreach (var path in saved) File.Delete(path); // don't leave orphan or half-written files behind
            throw;
        }
    }

    private static async Task<string> SaveAsync(IBrowserFile file, string dir, long maxSize, List<string> saved)
    {
        // Never use the client's name on disk: only its extension is kept (GetExtension can't contain path separators).
        var name = Guid.NewGuid() + Path.GetExtension(file.Name);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        saved.Add(path);
        await using var input = file.OpenReadStream(maxSize);
        await using var output = File.Create(path);
        await input.CopyToAsync(output);
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
