namespace PrintClient;

public class PrintStatus
{
    public string LastMessage { get; set; } = "En attente du premier sondage...";
    public DateTime? LastPollTime { get; set; }
    public int JobsProcessed { get; set; }
    public int JobsFailed { get; set; }
    public bool IsRunning { get; set; }
}
