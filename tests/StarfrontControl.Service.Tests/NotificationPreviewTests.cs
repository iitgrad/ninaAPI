using SkiaSharp;
using StarfrontControl.Service;
using System.Reflection;
using System.Text.Json;
using Xunit;

public sealed class NotificationPreviewTests
{
    private static byte[] Jpeg(int width, int height, Func<int, byte> level)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
                bitmap.SetPixel(x, y, new SKColor(level(x), level(x), level(x)));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return encoded.ToArray();
    }

    [Fact] public void LowersBackgroundPreservesHighlightsAndDoesNotMutateInput()
    {
        var input = Jpeg(256, 32, x => (byte)x); var original = input.ToArray();
        var output = NotificationPreview.Render(input);
        Assert.NotNull(output); Assert.InRange(output.Length, 4, NotificationPreview.MaximumBytes);
        Assert.Equal(original, input);
        using var bitmap = SKBitmap.Decode(output);
        Assert.Equal(256, bitmap.Width); Assert.Equal(32, bitmap.Height);
        Assert.InRange(bitmap.GetPixel(51, 16).Red, 18, 23);
        Assert.InRange(bitmap.GetPixel(0, 16).Red, 0, 2);
        Assert.InRange(bitmap.GetPixel(255, 16).Red, 252, 255);
        Assert.True(bitmap.GetPixel(128, 16).Red > bitmap.GetPixel(51, 16).Red);
    }

    [Fact] public void ReducesVisibleBackgroundNoiseWithoutClippingShadows()
    {
        var input = Jpeg(256, 32, x => (byte)(40 + x % 24));
        using var before = SKBitmap.Decode(input);
        using var after = SKBitmap.Decode(NotificationPreview.Render(input));
        var a = Enumerable.Range(0, 256).Select(x => (int)before.GetPixel(x, 16).Red).ToArray();
        var b = Enumerable.Range(0, 256).Select(x => (int)after.GetPixel(x, 16).Red).ToArray();
        Assert.True(b.Max() - b.Min() < a.Max() - a.Min());
        Assert.True(b.Min() > 0);
    }

    [Fact] public void RejectsInvalidAndOversizedInputBeforePixelAllocation()
    {
        Assert.Null(NotificationPreview.Render([]));
        Assert.Null(NotificationPreview.Render([0xff, 0xd8, 1, 2, 0xff, 0xd9]));
        Assert.Null(NotificationPreview.Render(new byte[100001]));
        Assert.Null(NotificationPreview.Render(Jpeg(801, 1, _ => 51)));
        Assert.NotNull(NotificationPreview.Render(Jpeg(800, 800, _ => 51)));
    }

    [Fact] public void StoredNotificationRemainsExactlyTheRenderedCopyAfterRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var output = NotificationPreview.Render(Jpeg(100, 100, _ => 51))!;
            var store = new NotificationMediaStore(root); var key = store.Put("gentler", output);
            Assert.Equal(output, new NotificationMediaStore(root).Read("gentler", key));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact] public async Task PushWorkerUsesGentlerCopyFromPushedTelemetry()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var input = Jpeg(100, 100, _ => 51); var original = input.ToArray();
            using var service = new PushNotifications(storageRoot: root);
            await service.StartAsync(CancellationToken.None);
            try
            {
                service.Observe(JsonSerializer.Serialize(new { bridge = new { connected = true }, snapshot = new {
                    sessionId = "isolated-preview-test", states = new { preview = new {
                        observedAt = DateTimeOffset.UtcNow, value = new { jpeg = Convert.ToBase64String(input) } } } } }));
                var field = typeof(PushNotifications).GetField("latestPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
                for (var i = 0; i < 100 && field.GetValue(service) is null; i++) await Task.Delay(20);
                Assert.Equal(NotificationPreview.Render(input), (byte[]?)field.GetValue(service));
                Assert.Null(service.LastDeliveryError);
                Assert.Equal(original, input);
            }
            finally { await service.StopAsync(CancellationToken.None); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
