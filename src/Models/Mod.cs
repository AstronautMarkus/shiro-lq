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

    // Public URL of one of this mod's images (the thumbnail or a photo), served by the endpoint in Program.cs.
    public string ImageUrl(string fileName) => $"mods/{Id}/images/{fileName}";
}
