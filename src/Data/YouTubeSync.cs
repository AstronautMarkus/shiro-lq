using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using shiro_lq.Models;

namespace shiro_lq.Data;

// Copies every channel's public videos from its YouTube RSS feed (public, no API key) into the videos table:
// when the app starts, every 15 minutes after that, and when the panel asks.
// The feed only has the latest 15 videos: the site keeps every one it has seen, but older ones never come in.
public partial class YouTubeSync(ChannelStore channels, VideoStore videos, ILogger<YouTubeSync> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly HttpClient Http = new();
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom", Yt = "http://www.youtube.com/xml/schemas/2015";

    // One channel at a time: the timer and the panel's button would otherwise rewrite the same rows at once.
    private readonly SemaphoreSlim gate = new(1, 1);
    // Channel link -> channel id (UC...). An @handle link needs the channel's page (~2.5 MB) to find it: once per link while the app runs.
    private readonly Dictionary<string, string> ids = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SyncAllAsync(stoppingToken);
            }
            // An exception out of here would stop the whole app: log it and try again on the next tick.
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(e, "Syncing the YouTube channels failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // The names of the channels that failed (each one logged). One broken link doesn't stop the rest.
    public async Task<List<string>> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var failed = new List<string>();
        foreach (var channel in await channels.GetAllAsync())
            if (!await SyncAsync(channel, cancellationToken)) failed.Add(channel.Name);
        return failed;
    }

    // False when it failed (logged): a wrong link, YouTube down, no internet... The site then keeps the videos it had.
    public async Task<bool> SyncAsync(Channel channel, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await videos.SaveLatestAsync(channel.Id, await FetchAsync(channel, cancellationToken));
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Syncing channel {Id} ({Url}) failed", channel.Id, channel.Url);
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<Video>> FetchAsync(Channel channel, CancellationToken cancellationToken)
    {
        if ((channel.YouTubeId ?? ids.GetValueOrDefault(channel.Url)) is not { } id)
        {
            var page = await Http.GetStringAsync(channel.Url, cancellationToken);
            id = ids[channel.Url] = CanonicalRegex().Match(page) is { Success: true } match
                ? match.Groups[1].Value
                : throw new InvalidOperationException($"No YouTube channel at {channel.Url}");
        }

        using var response = await Http.GetAsync($"https://www.youtube.com/feeds/videos.xml?channel_id={id}", cancellationToken);
        // A channel with no videos yet has no feed. Nothing is saved, so nothing is lost either if YouTube just hiccuped.
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        var feed = await XDocument.LoadAsync(await response.Content.ReadAsStreamAsync(cancellationToken), LoadOptions.None, cancellationToken);
        return feed.Root!.Elements(Atom + "entry").Select(entry => new Video
        {
            Id = (string)entry.Element(Yt + "videoId")!,
            ChannelId = channel.Id,
            Title = (string)entry.Element(Atom + "title")!,
            PublishedAt = ((DateTimeOffset)entry.Element(Atom + "published")!).UtcDateTime,
        }).ToList();
    }

    // Every channel page says which channel it is: <link rel="canonical" href="https://www.youtube.com/channel/UC...">.
    [GeneratedRegex(@"<link rel=""canonical"" href=""https://www\.youtube\.com/channel/(UC[\w-]{22})""")]
    private static partial Regex CanonicalRegex();
}
