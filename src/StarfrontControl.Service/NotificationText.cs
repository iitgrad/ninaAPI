using System.Text;

namespace StarfrontControl.Service;

public static class NotificationText
{
    // Windows PowerShell's default ANSI decoding can turn UTF-8 punctuation
    // into sequences such as â€”. Only undo a lossless, recognizable conversion.
    public static string Repair(string value)
    {
        var ansi = (Encoding)CodePagesEncodingProvider.Instance.GetEncoding(1252)!.Clone();
        ansi.EncoderFallback = EncoderFallback.ExceptionFallback;
        var utf8 = new UTF8Encoding(false, true);
        for (var pass = 0; pass < 2 && Suspect(value); pass++)
        {
            try
            {
                var decoded = utf8.GetString(ansi.GetBytes(value));
                if (decoded == value) break;
                value = decoded;
            }
            catch (ArgumentException) { break; }
        }
        return value;
    }
    private static bool Suspect(string value) => value.Contains("â€", StringComparison.Ordinal)
        || value.Contains('Ã') || value.Contains('Â');
}
