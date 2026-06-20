namespace PrintClient;

public class AppConfig
{
    public string ApiUrl { get; set; } = "http://host.docker.internal/api/index.php";
    public string ApiKey { get; set; } = "";
    public string PrinterName { get; set; } = "AL-C2800";
    public int PollingIntervalSeconds { get; set; } = 30;
}
