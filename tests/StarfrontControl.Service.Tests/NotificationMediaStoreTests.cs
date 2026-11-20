using StarfrontControl.Service;
using Xunit;

public sealed class NotificationMediaStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "starfront-notification-media-" + Guid.NewGuid());
    private static readonly byte[] Jpeg = [0xff, 0xd8, 1, 2, 0xff, 0xd9];
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-11-06T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact] public void OvernightImageAndOriginalCapabilitySurviveRestart()
    {
        var time = new Clock(); var store = new NotificationMediaStore(root, time);
        var key = store.Put("original-notification", Jpeg);
        time.Now += TimeSpan.FromHours(12);
        store = new NotificationMediaStore(root, time);
        Assert.Equal(key, store.Key("original-notification"));
        Assert.Equal(Jpeg, store.Read("original-notification", key));
        Assert.Null(store.Read("original-notification", "wrong-key"));
        Assert.Null(store.Read("unknown", key));
        Assert.Equal(key, store.Put("original-notification", [0xff, 0xd8, 9, 0xff, 0xd9]));
        Assert.Equal(Jpeg, store.Read("original-notification", key));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(root, "index.json")));
    }
    [Fact] public void SevenDayExpiryIsEnforcedAndExpiredFilesArePruned()
    {
        var time = new Clock(); var store = new NotificationMediaStore(root, time);
        var key = store.Put("old", Jpeg); time.Now += NotificationMediaStore.Retention;
        Assert.Null(store.Read("old", key)); Assert.Null(store.Key("old"));
        _ = new NotificationMediaStore(root, time);
        Assert.Empty(Directory.GetFiles(root, "*.jpg"));
    }
    [Fact] public void MoreThanThirtyTwoImagesRemainAvailableWithBoundedDiskUsage()
    {
        var time = new Clock(); var store = new NotificationMediaStore(root, time);
        var first = store.Put("0", Jpeg);
        for (var i = 1; i <= NotificationMediaStore.MaximumItems; i++)
        {
            time.Now += TimeSpan.FromSeconds(1); store.Put(i.ToString(), Jpeg);
            if (i == 40) Assert.Equal(Jpeg, store.Read("0", first));
        }
        Assert.Null(store.Key("0")); Assert.Equal(NotificationMediaStore.MaximumItems, Directory.GetFiles(root, "*.jpg").Length);
        Assert.NotNull(new NotificationMediaStore(root, time).Key(NotificationMediaStore.MaximumItems.ToString()));
    }
    [Fact] public void InvalidOversizedAndMissingFilesCannotBeServed()
    {
        var store = new NotificationMediaStore(root);
        Assert.Throws<ArgumentException>(() => store.Put("x", [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => store.Put("x", new byte[100_001]));
        var key = store.Put("../../outside", Jpeg);
        Assert.All(Directory.GetFiles(root, "*.jpg"), f => Assert.Equal(Path.GetFullPath(root), Path.GetDirectoryName(f)));
        File.Delete(Directory.GetFiles(root, "*.jpg").Single());
        Assert.Null(store.Read("../../outside", key));
    }
    [Theory]
    [InlineData("M 31 B â€” 120s repeating cycle", "M 31 B — 120s repeating cycle")]
    [InlineData("Rosette â€“ SHO", "Rosette – SHO")]
    [InlineData("M 31 B — 120s repeating cycle", "M 31 B — 120s repeating cycle")]
    [InlineData("O · latest camera preview", "O · latest camera preview")]
    [InlineData("星空 · NGC 6992", "星空 · NGC 6992")]
    public void RepairOnlyRecognizableEncodingDamage(string input, string expected) => Assert.Equal(expected, NotificationText.Repair(input));
}
