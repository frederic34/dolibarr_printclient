using System.Diagnostics;
using PrintClient;

var builder = WebApplication.CreateBuilder(args);

// Le fichier de config est dans le volume monté, ou localement en dev
var configPath = Directory.Exists("/app/temp")
    ? Path.Combine("/app/temp", "config.json")
    : Path.Combine(Directory.GetCurrentDirectory(), "temp", "config.json");

Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

builder.Services.AddSingleton(new ConfigService(configPath));
builder.Services.AddSingleton<PrintStatus>();
builder.Services.AddSingleton<PrintBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PrintBackgroundService>());

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/config", (ConfigService cfg) =>
    Results.Ok(cfg.Current));

app.MapPost("/api/config", (AppConfig newConfig, ConfigService cfg) =>
{
    cfg.Save(newConfig);
    return Results.Ok(cfg.Current);
});

app.MapGet("/api/status", (PrintStatus status) =>
    Results.Ok(status));

app.MapPost("/api/poll", (PrintBackgroundService svc) =>
{
    svc.TriggerImmediatePoll();
    return Results.Ok();
});

app.MapGet("/api/printers", () =>
{
    try
    {
        var info = new ProcessStartInfo
        {
            FileName = "lpstat",
            Arguments = "-a",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(info)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        var printers = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' ')[0])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return Results.Ok(printers);
    }
    catch
    {
        return Results.Ok(Array.Empty<string>());
    }
});

app.Run();
