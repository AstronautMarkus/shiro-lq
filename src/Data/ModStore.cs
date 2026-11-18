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

    // Each mod keeps everything under its own folder: mods/{id}/files/ (only reachable through the download endpoint)
    // and mods/{id}/images/ (thumbnail + photos, served publicly). Deleting a mod deletes the whole folder.
    private static string Folder(Guid modId) => $"mods/{modId}";
    public static string FileKey(Guid modId, string name) => $"{Folder(modId)}/files/{name}";
    public static string ImageKey(Guid modId, string name) => $"{Folder(modId)}/images/{name}";

    public static bool IsImage(string fileName) => ImageExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    public async Task EnsureTablesAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("""
            CREATE TABLE IF NOT EXISTS mods (
                id BINARY(16) PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                description TEXT NOT NULL,
                type VARCHAR(20) NOT NULL,
                file_name VARCHAR(255) NOT NULL,
                thumbnail VARCHAR(255) NOT NULL
            );
            CREATE TABLE IF NOT EXISTS mod_images (
                id INT AUTO_INCREMENT PRIMARY KEY,
                mod_id BINARY(16) NOT NULL,
                file_name VARCHAR(255) NOT NULL,
                FOREIGN KEY (mod_id) REFERENCES mods(id) ON DELETE CASCADE
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // Without Images: the list only needs the thumbnail. UUIDv7 ids sort by creation time, so this is newest first.
    public async Task<List<Mod>> GetAllAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand($"SELECT {Columns} FROM mods ORDER BY id DESC", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var mods = new List<Mod>();
        while (await reader.ReadAsync()) mods.Add(Read(reader));
        return mods;
    }

    public async Task<Mod?> GetAsync(Guid id)
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

    // Insert when mod.Id is empty (it gets a new UUIDv7), update otherwise. Uploads the new files first, then saves the rows in one transaction.
    // mod.Images are the current photos to keep (none on insert) and newImages go after them; a null file or thumbnail keeps the current one.
    // mod only changes after the commit, so a failed or cancelled save leaves it as it was and can be retried.
    // progress gets the total bytes uploaded so far.
    public async Task SaveAsync(Mod mod, IBrowserFile? file, IBrowserFile? thumbnail, IReadOnlyList<IBrowserFile> newImages,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        if ((thumbnail is not null && !IsImage(thumbnail.Name)) || !newImages.All(i => IsImage(i.Name))) throw new ArgumentException("Images must be " + string.Join(", ", ImageExtensions));
        var old = mod.Id == Guid.Empty ? null : await GetAsync(mod.Id) ?? throw new KeyNotFoundException($"Mod {mod.Id} not found.");
        if (old is null && (file is null || thumbnail is null)) throw new ArgumentException("A new mod needs a file and a thumbnail.");
        List<string> images = old is null ? [] : [.. mod.Images];
        if (images.Except(old?.Images ?? []).Any()) throw new ArgumentException("Only the mod's current images can be kept.");
        if (images.Count + newImages.Count > MaxImages) throw new ArgumentException($"At most {MaxImages} images.");
        // Known before uploading anything: the files go under the mod's folder.
        var id = old?.Id ?? Guid.CreateVersion7();

        var saved = new List<string>();
        long uploaded = 0;
        async Task<string> Upload(IBrowserFile upload, Func<Guid, string, string> keyFor, long maxSize)
        {
            // Never use the client's name in storage: only its extension is kept (GetExtension can't contain path separators).
            var name = Guid.NewGuid() + Path.GetExtension(upload.Name);
            var key = keyFor(id, name);
            saved.Add(key);
            await using var input = new ProgressStream(upload.OpenReadStream(maxSize, cancellationToken), read =>
            {
                uploaded += read;
                progress?.Report(uploaded);
            });
            await storage.SaveAsync(key, input, upload.Size, cancellationToken);
            return name;
        }

        try
        {
            var fileName = file is null ? old!.FileName : await Upload(file, FileKey, MaxFileSize);
            var thumbnailName = thumbnail is null ? old!.Thumbnail : await Upload(thumbnail, ImageKey, MaxImageSize);
            foreach (var image in newImages) images.Add(await Upload(image, ImageKey, MaxImageSize));

            await using var conn = await db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var cmd = new MySqlCommand(old is null
                ? "INSERT INTO mods (id, name, description, type, file_name, thumbnail) VALUES (@id, @name, @description, @type, @fileName, @thumbnail)"
                : "UPDATE mods SET name = @name, description = @description, type = @type, file_name = @fileName, thumbnail = @thumbnail WHERE id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@name", mod.Name);
                cmd.Parameters.AddWithValue("@description", mod.Description);
                cmd.Parameters.AddWithValue("@type", mod.Type.ToString());
                cmd.Parameters.AddWithValue("@fileName", fileName);
                cmd.Parameters.AddWithValue("@thumbnail", thumbnailName);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = new MySqlCommand("DELETE FROM mod_images WHERE mod_id = @modId", conn, tx))
            {
                cmd.Parameters.AddWithValue("@modId", id);
                await cmd.ExecuteNonQueryAsync();
            }
            foreach (var image in images)
            {
                await using var cmd = new MySqlCommand("INSERT INTO mod_images (mod_id, file_name) VALUES (@modId, @fileName)", conn, tx);
                cmd.Parameters.AddWithValue("@modId", id);
                cmd.Parameters.AddWithValue("@fileName", image);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            (mod.Id, mod.FileName, mod.Thumbnail, mod.Images) = (id, fileName, thumbnailName, images);
        }
        catch
        {
            // Don't leave orphan or half-written files behind (for a new mod, not even its empty folder).
            if (old is null) await storage.DeleteFolderAsync(Folder(id));
            else foreach (var key in saved) await storage.DeleteAsync(key);
            throw;
        }

        // Only after the commit: until then the old rows still point at these files.
        if (old is not null)
            foreach (var key in Keys(old).Except(Keys(mod))) await storage.DeleteAsync(key);
    }

    public async Task DeleteAsync(Guid id)
    {
        await using (var conn = await db.OpenConnectionAsync())
        await using (var cmd = new MySqlCommand("DELETE FROM mods WHERE id = @id", conn)) // mod_images go with it (ON DELETE CASCADE)
        {
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        // The whole folder, so files left behind by an interrupted save go too.
        await storage.DeleteFolderAsync(Folder(id));
    }

    // Every storage key a mod's rows point at.
    private static IEnumerable<string> Keys(Mod mod) =>
        mod.Images.Append(mod.Thumbnail).Select(i => ImageKey(mod.Id, i)).Append(FileKey(mod.Id, mod.FileName));

    private static Mod Read(MySqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        Name = r.GetString(1),
        Description = r.GetString(2),
        Type = Enum.Parse<ModType>(r.GetString(3)),
        FileName = r.GetString(4),
        Thumbnail = r.GetString(5),
    };

    // Read-only pass-through that reports how many bytes each read returned (upload progress).
    private sealed class ProgressStream(Stream inner, Action<int> onRead) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Report(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Report(await inner.ReadAsync(buffer, cancellationToken));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        private int Report(int read)
        {
            onRead(read);
            return read;
        }
    }
}
