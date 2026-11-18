using Microsoft.AspNetCore.Identity;
using MySqlConnector;
using shiro_lq.Models;

namespace shiro_lq.Data;

public class AdminStore(MySqlDataSource db)
{
    // PBKDF2 with a per-password salt, from ASP.NET Core itself.
    private static readonly PasswordHasher<Admin> Hasher = new();

    private const string Columns = "id, name, username, email, password_hash";

    public async Task EnsureTableAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("""
            CREATE TABLE IF NOT EXISTS admins (
                id INT AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                username VARCHAR(50) NOT NULL UNIQUE,
                email VARCHAR(255) NOT NULL UNIQUE,
                password_hash VARCHAR(255) NOT NULL
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> AnyAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("SELECT EXISTS(SELECT 1 FROM admins)", conn);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync());
    }

    public async Task<List<Admin>> GetAllAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand($"SELECT {Columns} FROM admins ORDER BY name", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var admins = new List<Admin>();
        while (await reader.ReadAsync()) admins.Add(Read(reader));
        return admins;
    }

    public Task<Admin?> GetAsync(int id) => FindAsync("id", id);

    // Null when the username doesn't exist or the password is wrong; callers can't tell which (no user enumeration).
    public async Task<Admin?> VerifyAsync(string username, string password)
    {
        var admin = await FindAsync("username", username);
        return admin is not null && Hasher.VerifyHashedPassword(admin, admin.PasswordHash, password) != PasswordVerificationResult.Failed
            ? admin
            : null;
    }

    // Insert when Id is 0, update otherwise. False when the username or email already belongs to another admin.
    // An empty Password keeps the current one, so the hash never has to travel through the UI.
    public async Task<bool> SaveAsync(Admin admin)
    {
        string? hash = string.IsNullOrEmpty(admin.Password) ? null : Hasher.HashPassword(admin, admin.Password);
        if (admin.Id == 0 && hash is null) throw new ArgumentException("A new admin needs a password.");

        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(admin.Id == 0
            ? "INSERT INTO admins (name, username, email, password_hash) VALUES (@name, @username, @email, @hash)"
            : "UPDATE admins SET name = @name, username = @username, email = @email, password_hash = COALESCE(@hash, password_hash) WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", admin.Id);
        cmd.Parameters.AddWithValue("@name", admin.Name);
        cmd.Parameters.AddWithValue("@username", admin.Username);
        cmd.Parameters.AddWithValue("@email", admin.Email);
        cmd.Parameters.AddWithValue("@hash", hash);
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
        if (admin.Id == 0) admin.Id = (int)cmd.LastInsertedId;
        return true;
    }

    public async Task DeleteAsync(int id)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("DELETE FROM admins WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // column is always one of our literals, never user input.
    private async Task<Admin?> FindAsync(string column, object value)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = new MySqlCommand($"SELECT {Columns} FROM admins WHERE {column} = @value", conn);
        cmd.Parameters.AddWithValue("@value", value);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    private static Admin Read(MySqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Name = r.GetString(1),
        Username = r.GetString(2),
        Email = r.GetString(3),
        PasswordHash = r.GetString(4),
    };
}
