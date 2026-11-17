using Microsoft.Extensions.FileProviders;
using MySqlConnector;
using shiro_lq.Components;
using shiro_lq.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Dev: comes from user-secrets (outside the repo). Prod: from the ConnectionStrings__Default env var.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default. In dev: dotnet user-secrets set \"ConnectionStrings:Default\" \"...\"");
builder.Services.AddSingleton(_ => new MySqlDataSource(connectionString));
builder.Services.AddSingleton<ModStore>();

var app = builder.Build();

var modStore = app.Services.GetRequiredService<ModStore>();
await modStore.EnsureTablesAsync();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
// Uploaded mod images (thumbnail + photos). MapStaticAssets only serves files that existed at build time.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Directory.CreateDirectory(modStore.ImagesDir).FullName),
    RequestPath = "/images",
});

app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/mods/{id:int}/download", async (int id, ModStore store) =>
    await store.GetAsync(id) is { } mod
        ? Results.File(Path.Combine(store.FilesDir, mod.FileName), "application/octet-stream", mod.Name + Path.GetExtension(mod.FileName))
        : Results.NotFound());
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
