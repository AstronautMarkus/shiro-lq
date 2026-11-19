using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace shiro_lq.Models;

public partial class Channel
{
    // A channel link by handle (youtube.com/@ShiroLQ) or by id (youtube.com/channel/UC...): the two forms YouTubeSync can find the channel id from.
    // Anything after it (/videos, ?si=...) is allowed and ignored. Also keeps javascript: and other links out, since the site renders it as a link.
    public const string UrlPattern = @"^https://(www\.|m\.)?youtube\.com/(@[\w.-]+|channel/(?<id>UC[\w-]{22}))([/?]\S*)?$";

    public int Id { get; set; }

    [Required(ErrorMessage = "El nombre es obligatorio."), StringLength(100, ErrorMessage = "Máximo 100 caracteres.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "El enlace es obligatorio."), StringLength(255, ErrorMessage = "Máximo 255 caracteres.")]
    [RegularExpression(UrlPattern, ErrorMessage = "Pega el enlace del canal, ej: https://www.youtube.com/@ShiroLQ")]
    public string Url { get; set; } = "";

    [StringLength(500, ErrorMessage = "Máximo 500 caracteres.")]
    public string Description { get; set; } = "";

    // Exactly one channel is the main one (the official channel). Only ChannelStore.SetMainAsync moves it.
    public bool IsMain { get; set; }

    // How many of its videos the site has (synced by YouTubeSync).
    public int VideoCount { get; set; }

    // The channel id (UC...) when the link has it; null for an @handle link, which YouTubeSync looks up on the channel's page.
    public string? YouTubeId => UrlRegex().Match(Url).Groups["id"] is { Success: true } id ? id.Value : null;

    [GeneratedRegex(UrlPattern)]
    private static partial Regex UrlRegex();
}
