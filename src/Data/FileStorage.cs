using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Net.Http.Headers;

namespace shiro_lq.Data;

// Where uploaded files live. Keys look like "mods/{name}" or "images/{name}".
// Picked at startup with Storage:Provider (see Program.cs).
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, long length);
    Task DeleteAsync(string key);
    // Sends the file to the browser; with downloadName it comes as an attachment with that name.
    IResult Serve(string key, string contentType, string? downloadName = null);
}

// Fallback: files on this server's disk. Only works with a single server, and the folder must survive deploys.
public class LocalFileStorage(string root) : IFileStorage
{
    public async Task SaveAsync(string key, Stream content, long length)
    {
        var path = Path.Combine(root, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var output = File.Create(path);
        await content.CopyToAsync(output);
    }

    public Task DeleteAsync(string key)
    {
        File.Delete(Path.Combine(root, key));
        return Task.CompletedTask;
    }

    public IResult Serve(string key, string contentType, string? downloadName = null)
    {
        var path = Path.Combine(root, key);
        return File.Exists(path) ? Results.File(path, contentType, downloadName) : Results.NotFound();
    }
}

public class S3FileStorage(IAmazonS3 s3, string bucket) : IFileStorage
{
    // The browser file stream can't seek, so the SDK needs the length up front.
    public Task SaveAsync(string key, Stream content, long length) =>
        s3.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, InputStream = content, Headers = { ContentLength = length } });

    public Task DeleteAsync(string key) => s3.DeleteObjectAsync(bucket, key);

    // Redirects to a short-lived signed URL: the bucket stays private and big downloads don't go through this server.
    public IResult Serve(string key, string contentType, string? downloadName = null)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(15),
            ResponseHeaderOverrides = { ContentType = contentType },
        };
        if (downloadName is not null)
        {
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(downloadName); // encodes non-ASCII names (ñ, tildes) correctly
            request.ResponseHeaderOverrides.ContentDisposition = disposition.ToString();
        }
        return Results.Redirect(s3.GetPreSignedURL(request));
    }
}
