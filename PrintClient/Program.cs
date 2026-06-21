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

app.MapGet("/api/jobs", async (ConfigService cfg) =>
{
    var config = cfg.Current;
    if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
        return Results.Ok(Array.Empty<PrintTask>());

    try
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.Add("DOLAPIKEY", config.ApiKey);
        http.DefaultRequestHeaders.Add("Accept", "application/json");

        var jobs = await http.GetFromJsonAsync<List<PrintTask>>(
            config.ApiUrl + "/printjobapi/printjobs?sqlfilters=status%3A%3D%3A0");

        return Results.Ok(jobs ?? []);
    }
    catch
    {
        return Results.Ok(Array.Empty<PrintTask>());
    }
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
