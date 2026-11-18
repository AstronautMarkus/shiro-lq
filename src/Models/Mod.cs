using System.ComponentModel.DataAnnotations;

namespace shiro_lq.Models;

// Stored by name in MySQL: adding values is safe, renaming or removing one breaks existing rows.
public enum ModType { Mod, Skin, Map, Item, Other }

public static class ModTypeExtensions
{
    // Spanish label shown in the UI.
    public static string Label(this ModType type) => type switch
    {
        ModType.Map => "Mapa",
        ModType.Item => "Objeto",
        ModType.Other => "Otro",
        _ => type.ToString(),
    };
}

public class Mod
{
    // UUIDv7, set by ModStore on insert: sorts by creation time (good for the index) and URLs don't reveal how many mods exist.
    public Guid Id { get; set; }

    [Required(ErrorMessage = "El nombre es obligatorio."), StringLength(100, ErrorMessage = "Máximo 100 caracteres.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "La descripción es obligatoria."), StringLength(5000, ErrorMessage = "Máximo 5000 caracteres.")]
    public string Description { get; set; } = "";

    public ModType Type { get; set; }

    // Server-generated file name, stored at ModStore.FileKey(Id, FileName).
    public string FileName { get; set; } = "";

    // Server-generated file names, stored at ModStore.ImageKey(Id, name).
    public string Thumbnail { get; set; } = "";
    public List<string> Images { get; set; } = [];

    // Who published it. Null when that account was deleted: the mod stays (ON DELETE SET NULL).
    public int? AuthorId { get; set; }
    public string? AuthorName { get; set; }

    // Private mods are only seen by logged-in accounts: hidden from the public lists, 404 for everyone else.
    public bool IsPublic { get; set; } = true;

    // Public URL of one of this mod's images (the thumbnail or a photo), served by the endpoint in Program.cs.
    public string ImageUrl(string fileName) => $"mods/{Id}/images/{fileName}";

    // The description for cards: on one line and cut at a word with "…" so long texts can't break the layout.
    public string Summary(int maxLength = 140)
    {
        var text = string.Join(' ', Description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= maxLength) return text;
        var cut = text.LastIndexOf(' ', maxLength);
        if (cut < maxLength / 2) cut = maxLength; // one huge word: cut inside it
        if (char.IsHighSurrogate(text[cut - 1])) cut--; // don't split an emoji in half
        return text[..cut].TrimEnd(' ', ',', '.', ';', ':') + "…";
    }
}
