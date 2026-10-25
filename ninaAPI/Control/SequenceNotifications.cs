using StarfrontControl.Core;

namespace ninaAPI.Control;

public sealed partial class ControlAdapter
{
    private readonly SequenceNotificationOutbox notificationOutbox = new();
    public void SendSequenceNotification(string message, bool attachPreview)
    {
        lock (gate)
        {
            if (disposed) throw new System.InvalidOperationException("Starfront Control is stopped.");
            Enqueue("sequenceNotifications", new { items = notificationOutbox.Add(message, attachPreview) });
        }
    }
}
