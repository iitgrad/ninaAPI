namespace StarfrontControl.Core;

public static class SequencePresentation
{
    public static string State(string status, string instructionType) => status switch
    {
        "CREATED" => "pending",
        "RUNNING" when instructionType.StartsWith("Wait", StringComparison.Ordinal) => "waiting",
        "RUNNING" => "running",
        "FINISHED" => "completed",
        "FAILED" => "failed",
        "SKIPPED" => "skipped",
        "DISABLED" => "disabled",
        _ => "unknown"
    };
}
