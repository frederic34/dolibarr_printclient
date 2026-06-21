namespace PrintClient;

public class PrinterOption
{
    public string[] Values { get; set; } = [];
    public string Default { get; set; } = "";
}

public class PrinterInfo
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsAccepting { get; set; }
    public string DeviceUri { get; set; } = "";
    public Dictionary<string, PrinterOption> Options { get; set; } = [];
}
