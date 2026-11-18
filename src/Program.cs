using System.Security.Claims;
using System.Threading.RateLimiting;
using Amazon;
using Amazon.S3;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

// Guid columns are BINARY(16) in standard byte order: UUIDv7 ids stay time-ordered in the index and match MySQL's BIN_TO_UUID().
builder.Services.AddSingleton(_ => new MySqlDataSource(
    new MySqlConnectionStringBuilder(Required("ConnectionStrings:Default")) { GuidFormat = MySqlGuidFormat.Binary16 }.ConnectionString));
builder.Services.AddSingleton<IFileStorage>(builder.Configuration["Storage:Provider"] switch
{
    "S3" => new S3FileStorage(
        new AmazonS3Client(Required("Storage:S3:AccessKey"), Required("Storage:S3:SecretKey"), RegionEndpoint.GetBySystemName(Required("Storage:S3:Region"))),
        Required("Storage:S3:Bucket")),
    "Local" => new LocalFileStorage(Path.Combine(builder.Environment.ContentRootPath, "uploads")),
    _ => throw new InvalidOperationException("Set Storage:Provider to S3 or Local. In dev: dotnet user-secrets set \"Storage:Provider\" \"S3\" --project src"),
});
builder.Services.AddSingleton<ModStore>();
builder.Services.AddSingleton<AdminStore>();

// Admin panel: cookie login at /admin/login, pages under /admin require it.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/admin/login";
    // A deleted admin loses access on the next request instead of when the cookie expires.
    options.Events.OnValidatePrincipal = async context =>
    {
        var admins = context.HttpContext.RequestServices.GetRequiredService<AdminStore>();
        if (!int.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || await admins.GetAsync(id) is null)
            context.RejectPrincipal();
    };
});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
// Slows down password guessing: the login page takes 10 requests per minute per IP.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return new ValueTask(context.HttpContext.Response.WriteAsync("Demasiados intentos. Espera un minuto y vuelve a probar."));
    };
});

var app = builder.Build();

await app.Services.GetRequiredService<ModStore>().EnsureTablesAsync();
var admins = app.Services.GetRequiredService<AdminStore>();
await admins.EnsureTableAsync();
// First run only: the first admin comes from config like the other secrets. After that, admins are managed in /admin/admins.
if (!await admins.AnyAsync())
    await admins.SaveAsync(new() { Name = Required("Admin:Name"), Username = Required("Admin:Username"), Email = Required("Admin:Email"), Password = Required("Admin:Password") });

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/mods/{id:guid}/download", async (Guid id, ModStore store, IFileStorage storage) =>
    await store.GetAsync(id) is { } mod
        ? storage.Serve(ModStore.FileKey(mod.Id, mod.FileName), "application/octet-stream", mod.Name + Path.GetExtension(mod.FileName))
        : Results.NotFound());
// Uploaded mod images (thumbnail + photos). Only a guid id and server-generated names (guid + image extension), so no path tricks reach the storage.
var contentTypes = new FileExtensionContentTypeProvider();
app.MapGet("/mods/{id:guid}/images/{name}", (Guid id, string name, IFileStorage storage) =>
    Guid.TryParse(Path.GetFileNameWithoutExtension(name), out _) && ModStore.IsImage(name) && contentTypes.TryGetContentType(name, out var type)
        ? storage.Serve(ModStore.ImageKey(id, name), type)
        : Results.NotFound());
app.MapPost("/admin/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    // Checked by hand: UseAntiforgery only rejects endpoints that bind form data, and this one reads none.
    if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();
    await context.SignOutAsync();
    return Results.Redirect("/admin/login");
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
