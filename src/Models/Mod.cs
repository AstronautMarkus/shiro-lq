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
    public int Id { get; set; }

    [Required(ErrorMessage = "El nombre es obligatorio."), StringLength(100, ErrorMessage = "Máximo 100 caracteres.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "La descripción es obligatoria."), StringLength(5000, ErrorMessage = "Máximo 5000 caracteres.")]
    public string Description { get; set; } = "";

    public ModType Type { get; set; }

    // Server-generated file name under the ModStore.FilesDir storage prefix.
    public string FileName { get; set; } = "";

    // Server-generated file names under the ModStore.ImagesDir storage prefix.
    public string Thumbnail { get; set; } = "";
    public List<string> Images { get; set; } = [];
}
