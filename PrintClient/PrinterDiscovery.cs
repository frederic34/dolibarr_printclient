using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PrintClient;

public static class PrinterDiscovery
{
    // Collecte nom + statut + isAccepting (rapide, sans options)
    public static List<PrinterInfo> CollectBasic()
    {
        var printers = ParseStatuses();
        ApplyAccepting(printers);
        return [.. printers.Values];
    }

    // Collecte complète avec URI et toutes les options CUPS (pour sync Dolibarr)
    public static List<PrinterInfo> CollectFull()
    {
        var printers = ParseStatuses();
        ApplyAccepting(printers);
        ApplyDeviceUris(printers);

        foreach (var p in printers.Values)
            p.Options = ParseOptions(p.Name);

        return [.. printers.Values];
    }

    private static Dictionary<string, PrinterInfo> ParseStatuses()
    {
        var result = new Dictionary<string, PrinterInfo>();
        foreach (var line in Run("lpstat", "-p").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Formats possibles :
            // "printer NAME is idle.  enabled since ..."
            // "printer NAME is processing; enabled since ..."
            // "printer NAME is now printing JOBID.  enabled since ..."
            // "printer NAME disabled since ..."
            var m = Regex.Match(line, @"^printer\s+(\S+)\s+(.+)");
            if (!m.Success) continue;
            var name = m.Groups[1].Value;
            var rest = m.Groups[2].Value.ToLower();
            var status = rest.Contains("idle")       ? "idle"
                       : rest.Contains("processing") ? "processing"
                       : rest.Contains("now")        ? "processing"
                       : rest.StartsWith("disabled") ? "stopped"
                       : rest.Split(' ')[0].TrimEnd('.');
            result[name] = new PrinterInfo { Name = name, Status = status };
        }
        return result;
    }

    private static void ApplyAccepting(Dictionary<string, PrinterInfo> printers)
    {
        foreach (var line in Run("lpstat", "-a").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(line, @"^(\S+)\s+accepting requests");
            if (m.Success && printers.TryGetValue(m.Groups[1].Value, out var p))
                p.IsAccepting = true;
        }
    }

    private static void ApplyDeviceUris(Dictionary<string, PrinterInfo> printers)
    {
        foreach (var line in Run("lpstat", "-v").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "device for NAME: socket://..."
            var m = Regex.Match(line, @"^device for\s+(\S+):\s+(.+)");
            if (m.Success && printers.TryGetValue(m.Groups[1].Value, out var p))
                p.DeviceUri = m.Groups[2].Value.Trim();
        }
    }

    private static Dictionary<string, PrinterOption> ParseOptions(string printerName)
    {
        var options = new Dictionary<string, PrinterOption>();
        foreach (var line in Run("lpoptions", $"-p {printerName} -l").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "PageSize/Media Size: *A4 A3 Letter ..."
            var colon = line.IndexOf(':');
            if (colon < 0) continue;

            var keyPart   = line[..colon].Trim();
            var valuesPart = line[(colon + 1)..].Trim();

            var slash = keyPart.IndexOf('/');
            var key   = slash >= 0 ? keyPart[..slash] : keyPart;

            var tokens     = valuesPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var defaultVal = tokens.FirstOrDefault(v => v.StartsWith('*'))?.TrimStart('*') ?? "";
            var values     = tokens.Select(v => v.TrimStart('*')).ToArray();

            options[key] = new PrinterOption { Values = values, Default = defaultVal };
        }
        return options;
    }

    private static string Run(string cmd, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }
        catch { return ""; }
    }
}
