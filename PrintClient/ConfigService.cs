using System.Text.Json;

namespace PrintClient;

public class ConfigService
{
    private readonly string _configPath;
    private AppConfig _config;
    private readonly object _lock = new();

    public ConfigService(string configPath)
    {
        _configPath = configPath;
        _config = Load();
    }

    public AppConfig Current
    {
        get { lock (_lock) { return _config; } }
    }

    public void Save(AppConfig config)
    {
        lock (_lock)
        {
            _config = config;
            File.WriteAllText(_configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private AppConfig Load()
    {
        if (!File.Exists(_configPath))
            return new AppConfig();
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configPath)) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }
}
