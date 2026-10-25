using System.Text.Json;

namespace StarfrontControl.Core;

public sealed record SequenceNotification(long Number, string Message, bool AttachPreview);

/// <summary>A bounded, process-local event window carried on the existing pushed snapshot.</summary>
public sealed class SequenceNotificationOutbox
{
    private readonly Queue<SequenceNotification> items = new();
    private long number;
    public const int Capacity = 16;
    public const int MessageLimit = 512;
    public SequenceNotification[] Add(string message, bool attachPreview)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Trim().Length > MessageLimit)
            throw new ArgumentException("Enter a message of 1–512 characters.", nameof(message));
        lock (items)
        {
            items.Enqueue(new(++number, message.Trim(), attachPreview));
            while (items.Count > Capacity) items.Dequeue();
            return items.ToArray();
        }
    }
}
