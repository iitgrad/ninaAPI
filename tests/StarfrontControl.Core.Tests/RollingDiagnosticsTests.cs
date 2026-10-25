using StarfrontControl.Core;
using Xunit;

namespace StarfrontControl.Core.Tests;

public class RollingDiagnosticsTests
{
    [Fact]
    public void LongLogRunStaysWithinDiskBudgetAndLeavesOtherLogsAlone()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(directory);
            var unrelated = Path.Combine(directory, "NINA.log");
            File.WriteAllText(unrelated, "Preserve host log");
            var log = new RollingDiagnostics(directory, 1024, 3);
            for (var index = 0; index < 10_000; index++) log.Write($"{{\"sample\":{index}}}");
            var files = new DirectoryInfo(directory).GetFiles("metrics-*.jsonl");
            Assert.InRange(files.Length, 1, 3);
            Assert.True(files.Sum(file => file.Length) <= 3072);
            Assert.Equal(files.Sum(file => file.Length), log.DiskBytes);
            Assert.Contains("9999", File.ReadAllText(Path.Combine(directory, "metrics-0.jsonl")));
            Assert.Equal("Preserve host log", File.ReadAllText(unrelated));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void OversizedRecordIsRejectedBeforeWriting()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var log = new RollingDiagnostics(directory, 100, 1);
            Assert.Throws<ArgumentException>(() => log.Write(new string('x', 101)));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
