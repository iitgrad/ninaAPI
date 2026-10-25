using System.Text.Json;
using StarfrontControl.Core;
using Xunit;

public sealed class NotificationProjectionTests
{
    private static JsonElement Snapshot(bool running = false, bool safe = true, int image = 0, string session = "a", string target = "Veil F", string imageType = "LIGHT", bool failed = false, bool completed = false) => JsonSerializer.SerializeToElement(new {
        sessionId = session, states = new {
            application = new { value = new { sequenceRunning = running } },
            safety = new { value = new { connected = true, isSafe = safe } },
            image = new { value = new { number = image, imageType, filter = "Ha" } },
            sequence = new { value = new { nodes = new object[] {
                new { id = "root", parentId = (string?)null, type = "SequenceRootContainer", name = "Tonight", state = completed ? "completed" : "running" },
                new { id = "target", parentId = "root", type = "DeepSkyObjectContainer", name = target, state = "running" },
                new { id = "step", parentId = "target", type = "TakeExposure", name = "Take Exposure", state = failed ? "failed" : "running" }
            } } }
        }
    });
    [Fact] public void InitialAndReconnectSnapshotsNeverReplayOldAlerts()
    {
        var p = new NotificationProjection();
        Assert.Empty(p.Observe(Snapshot(true, false, 20, failed: true)));
        Assert.Empty(p.Observe(Snapshot(true, false, 20, failed: true)));
        Assert.Empty(p.Observe(Snapshot(true, true, 30), baseline: true));
        Assert.Empty(p.Observe(Snapshot(true, false, 30, session: "new", failed: true)));
    }
    [Fact] public void StartsSafetyFailuresAndCompletionAreDistinctAndDeduplicated()
    {
        var p = new NotificationProjection(); p.Observe(Snapshot());
        Assert.Contains(p.Observe(Snapshot(true)), n => n.Title == "Sequence started");
        var change = p.Observe(Snapshot(true, false, failed: true));
        Assert.Contains(change, n => n.Title == "Roof closed");
        Assert.Contains(change, n => n.Title == "Sequence instruction failed");
        Assert.Empty(p.Observe(Snapshot(true, false, failed: true)));
        Assert.Contains(p.Observe(Snapshot(true, true, failed: true)), n => n.Kind == "safety" && n.Title == "Roof open");
        Assert.Contains(p.Observe(Snapshot(false, false, completed: true)), n => n.Title == "Sequence completed");
    }
    [Fact] public void ImagesSendFirstAndEveryFifthAndIgnoreRepeatedSnapshots()
    {
        var p = new NotificationProjection(); p.Observe(Snapshot(true));
        for (var i = 1; i <= 10; i++) {
            var notices = p.Observe(Snapshot(true, image: i));
            Assert.Equal(i is 1 or 5 or 10, notices.Any(n => n.Kind == "image"));
            Assert.Empty(p.Observe(Snapshot(true, image: i)));
        }
    }
    [Fact] public void CalibrationHasNoSciencePreviewAndFlatFilterIsAnnouncedOnce()
    {
        var p = new NotificationProjection(); p.Observe(Snapshot(true));
        var flat = p.Observe(Snapshot(true, image: 1, imageType: "FLAT"));
        Assert.Contains(flat, n => n.Title == "Taking flats · Ha");
        Assert.DoesNotContain(flat, n => n.Kind == "image");
        Assert.Empty(p.Observe(Snapshot(true, image: 2, imageType: "FLAT")));
        Assert.Empty(p.Observe(Snapshot(true, image: 3, imageType: "DARK")));
    }
    [Fact] public void TargetChangesAreDetectedFromSequenceAncestry()
    {
        var p = new NotificationProjection(); p.Observe(Snapshot(true));
        var changed = p.Observe(Snapshot(true, target: "Orion"));
        Assert.Contains(changed, n => n.Kind == "target" && n.Target == "Orion");
    }
    private static JsonElement WithMessages(int count, string session = "a", bool preview = false) => JsonSerializer.SerializeToElement(new {
        sessionId = session, states = new {
            sequenceNotifications = new { value = new { items = Enumerable.Range(1, count).Select(n => new { number = n, message = "Message " + n, attachPreview = preview }).ToArray() } }
        }
    });
    [Fact] public void ExplicitMessagesSurviveCoalescingAndDoNotReplayOnReconnect()
    {
        var p = new NotificationProjection(); Assert.Empty(p.Observe(WithMessages(0)));
        var notices = p.Observe(WithMessages(3, preview: true));
        Assert.Equal(3, notices.Count); Assert.All(notices, n => { Assert.Equal("sequence", n.Kind); Assert.True(n.AttachPreview); });
        Assert.Equal(new[] {"Message 1", "Message 2", "Message 3"}, notices.Select(n => n.Body));
        Assert.Empty(p.Observe(WithMessages(3)));
        Assert.Empty(p.Observe(WithMessages(4), baseline: true));
        Assert.Single(p.Observe(WithMessages(5)));
        Assert.Empty(p.Observe(WithMessages(6, session: "b")));
        Assert.Single(p.Observe(WithMessages(7, session: "b")));
    }
    [Fact] public void ExplicitMessageOutboxIsBoundedAndValidatesInput()
    {
        var box = new SequenceNotificationOutbox();
        Assert.Throws<ArgumentException>(() => box.Add(" ", false));
        Assert.Throws<ArgumentException>(() => box.Add(new string('x', 513), false));
        for (var i = 0; i < 30; i++) box.Add("Update", false);
        var items = box.Add(" Final ", true);
        Assert.Equal(16, items.Length); Assert.Equal(16, items.First().Number);
        Assert.Equal("Final", items.Last().Message); Assert.True(items.Last().AttachPreview);
    }
}
