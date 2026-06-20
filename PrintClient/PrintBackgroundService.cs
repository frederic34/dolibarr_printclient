using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace PrintClient;

public class PrintTask
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string Modulepart { get; set; } = "";
    public string JobId { get; set; } = "";
    public int Status { get; set; }
}

public class DolibarrDownload
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string Content { get; set; } = "";
    public string Encoding { get; set; } = "";
}

public class PrintBackgroundService : BackgroundService
{
    private readonly ConfigService _configService;
    private readonly PrintStatus _status;
    private readonly ILogger<PrintBackgroundService> _logger;
    private readonly SemaphoreSlim _pollSignal = new(0, 1);
    private HttpClient? _httpClient;
    private string _lastApiUrl = "";
    private string _lastApiKey = "";

    public PrintBackgroundService(ConfigService configService, PrintStatus status, ILogger<PrintBackgroundService> logger)
    {
        _configService = configService;
        _status = status;
        _logger = logger;
    }

    public void TriggerImmediatePoll()
    {
        try { _pollSignal.Release(); } catch (SemaphoreFullException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _status.IsRunning = true;
        _logger.LogInformation("Service d'impression démarré");

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = _configService.Current;
            _status.LastPollTime = DateTime.Now;
            try
            {
                await CheckAndPrintTasks(config, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _status.LastMessage = $"Erreur: {ex.Message}";
                _logger.LogError(ex, "Erreur lors du sondage");
            }

            try
            {
                // Attend l'intervalle configuré ou un déclenchement manuel via TriggerImmediatePoll()
                await _pollSignal.WaitAsync(TimeSpan.FromSeconds(config.PollingIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _status.IsRunning = false;
    }

    private async Task CheckAndPrintTasks(AppConfig config, CancellationToken ct)
    {
        var httpClient = GetOrCreateClient(config);
        _logger.LogInformation("Sondage des tâches...");

        var tasks = await httpClient.GetFromJsonAsync<List<PrintTask>>(
            config.ApiUrl + "/printjobapi/printjobs?sqlfilters=status%3A%3D%3A0", ct);

        if (tasks == null || tasks.Count == 0)
        {
            _status.LastMessage = $"Aucune tâche en attente ({DateTime.Now:HH:mm:ss})";
            return;
        }

        foreach (var task in tasks)
        {
            if (ct.IsCancellationRequested) break;

            _logger.LogInformation("Traitement de la tâche ID: {Id}", task.Id);

            var tempDir = Path.Combine(Path.GetTempPath(), "PrintJobs");
            Directory.CreateDirectory(tempDir);
            var localPath = Path.Combine(tempDir, Path.GetFileName(task.FileName));

            try
            {
                var url = $"{config.ApiUrl}/documents/download?original_file={task.FileName}&modulepart={task.Modulepart}";
                var download = await httpClient.GetFromJsonAsync<DolibarrDownload>(url, ct);
                await File.WriteAllBytesAsync(localPath, Convert.FromBase64String(download!.Content), ct);

                if (PrintFile(config.PrinterName, localPath))
                {
                    await httpClient.PutAsync($"{config.ApiUrl}/printjobapi/printjobs/{task.Id}?status=1", null, ct);
                    _status.JobsProcessed++;
                    _status.LastMessage = $"Tâche {task.Id} imprimée ({DateTime.Now:HH:mm:ss})";
                }
                else
                {
                    await httpClient.PutAsync($"{config.ApiUrl}/printjobapi/printjobs/{task.Id}?status=99", null, ct);
                    _status.JobsFailed++;
                    _status.LastMessage = $"Échec tâche {task.Id} ({DateTime.Now:HH:mm:ss})";
                }
            }
            finally
            {
                if (File.Exists(localPath)) File.Delete(localPath);
            }
        }
    }

    private bool PrintFile(string printerName, string filePath)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "lp",
                Arguments = $"-d {printerName} \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(info)!;
            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode == 0)
            {
                _logger.LogInformation("Succès CUPS: {Output}", output.Trim());
                return true;
            }

            _logger.LogError("Erreur CUPS: {Error}", error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur critique d'impression");
            return false;
        }
    }

    private HttpClient GetOrCreateClient(AppConfig config)
    {
        if (_httpClient != null && _lastApiUrl == config.ApiUrl && _lastApiKey == config.ApiKey)
            return _httpClient;

        _httpClient?.Dispose();

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        var c = new HttpClient(handler);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        c.DefaultRequestHeaders.Add("User-Agent", "PrintClient v2.0");
        c.DefaultRequestHeaders.Add("DOLAPIKEY", config.ApiKey);

        _lastApiUrl = config.ApiUrl;
        _lastApiKey = config.ApiKey;
        _httpClient = c;
        return c;
    }

    public override void Dispose()
    {
        _httpClient?.Dispose();
        _pollSignal.Dispose();
        base.Dispose();
    }
}
