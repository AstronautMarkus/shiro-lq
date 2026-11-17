using Amazon;
using Amazon.S3;
using Microsoft.AspNetCore.StaticFiles;
using MySqlConnector;
using shiro_lq.Components;
using shiro_lq.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Secrets and settings. Dev: user-secrets (outside the repo). Prod: env vars, with __ instead of : (ConnectionStrings__Default).
string Required(string key) => builder.Configuration[key]
    ?? throw new InvalidOperationException($"Missing {key}. In dev: dotnet user-secrets set \"{key}\" \"...\" --project src");

builder.Services.AddSingleton(_ => new MySqlDataSource(Required("ConnectionStrings:Default")));
builder.Services.AddSingleton<IFileStorage>(builder.Configuration["Storage:Provider"] switch
{
    "S3" => new S3FileStorage(
        new AmazonS3Client(Required("Storage:S3:AccessKey"), Required("Storage:S3:SecretKey"), RegionEndpoint.GetBySystemName(Required("Storage:S3:Region"))),
        Required("Storage:S3:Bucket")),
    "Local" => new LocalFileStorage(Path.Combine(builder.Environment.ContentRootPath, "uploads")),
    _ => throw new InvalidOperationException("Set Storage:Provider to S3 or Local. In dev: dotnet user-secrets set \"Storage:Provider\" \"S3\" --project src"),
});
builder.Services.AddSingleton<ModStore>();

var app = builder.Build();

await app.Services.GetRequiredService<ModStore>().EnsureTablesAsync();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/mods/{id:int}/download", async (int id, ModStore store, IFileStorage storage) =>
    await store.GetAsync(id) is { } mod
        ? storage.Serve($"{ModStore.FilesDir}/{mod.FileName}", "application/octet-stream", mod.Name + Path.GetExtension(mod.FileName))
        : Results.NotFound());
// Uploaded mod images (thumbnail + photos). Only server-generated names (guid + image extension), so no path tricks reach the storage.
var contentTypes = new FileExtensionContentTypeProvider();
app.MapGet("/images/{name}", (string name, IFileStorage storage) =>
    Guid.TryParse(Path.GetFileNameWithoutExtension(name), out _) && ModStore.IsImage(name) && contentTypes.TryGetContentType(name, out var type)
        ? storage.Serve($"{ModStore.ImagesDir}/{name}", type)
        : Results.NotFound());
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
