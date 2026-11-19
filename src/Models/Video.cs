using System.Globalization;

namespace shiro_lq.Models;

// A public YouTube video, copied from its channel by YouTubeSync. Read-only here: it's edited on YouTube.
public class Video
{
    // YouTube's own id, the v= of the link.
    public string Id { get; set; } = "";
    public int ChannelId { get; set; }
    public string ChannelName { get; set; } = "";
    public string Title { get; set; } = "";
    // UTC.
    public DateTime PublishedAt { get; set; }

    public string Url => $"https://www.youtube.com/watch?v={Id}";
    // Every video has hqdefault. It's 4:3 with black bars, which the 16:9 .video box crops off (object-fit: cover).
    public string ThumbnailUrl => $"https://i.ytimg.com/vi/{Id}/hqdefault.jpg";
    // youtube-nocookie: the player sets no tracking cookies until someone presses play.
    public string EmbedUrl => $"https://www.youtube-nocookie.com/embed/{Id}";

    // shortcut: the UTC date, a day ahead for uploads late at night in the Americas; convert to the audience's time zone if that matters.
    public string Date => PublishedAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
