using System.Globalization;

namespace StarfrontControl.Core;

public static class SafeOrDeadline
{
    private static readonly string[] DeadlineFormats =
        ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mmzzz"];

    public static bool TryParseDeadline(string? value, out DateTimeOffset deadline) =>
        DateTimeOffset.TryParseExact(value, DeadlineFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out deadline);

    // True permits recovery. False lets the sequencer advance without running it.
    public static async Task<bool> WaitAsync(
        DateTimeOffset deadline, Func<bool> isSafe, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return false;
            if (isSafe()) return DateTimeOffset.UtcNow < deadline;

            await Task.Delay(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), token);
        }
    }
}
