using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using StarfrontControl.Core;

namespace StarfrontControl.Service;

public sealed class BridgeConnection(ServiceConfiguration config) : BackgroundService
{
    public SnapshotHub Clients { get; } = new(16);
    public event Action<string>? SnapshotReceived;
    private readonly object gate = new();
    private string snapshot = "null";
    private readonly SequenceTiming sequenceTiming = new();
    private string frame = "{}";
    private bool connected;
    private DateTimeOffset? receivedAt;
    public bool Connected { get { lock (gate) return connected; } }
    public string Read() { lock (gate) return frame; }

    private void Publish(bool online, string? next = null)
    {
        lock (gate)
        {
            connected = online;
            if (!online) sequenceTiming.Disconnected();
            if (next is not null) { snapshot = next; receivedAt = DateTimeOffset.UtcNow; }
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteStartObject("bridge");
                writer.WriteBoolean("connected", connected);
                if (receivedAt is not null) writer.WriteString("lastReceivedAt", receivedAt.Value);
                writer.WriteEndObject();
                writer.WritePropertyName("snapshot");
                writer.WriteRawValue(snapshot, skipInputValidation: true);
                writer.WriteEndObject();
            }
            frame = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
            Clients.Publish(frame);
        }
        SnapshotReceived?.Invoke(frame);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Publish(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", "Bearer " + config.BridgeToken);
                if (config.BridgeHostHeader is { } host) socket.Options.SetRequestHeader("Host", host);
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                var uri = new UriBuilder(config.BridgeUrl) { Scheme = config.BridgeUrl.StartsWith("https:") ? "wss" : "ws", Path = "/socket/v1" }.Uri;
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    connect.CancelAfter(TimeSpan.FromSeconds(10));
                    await socket.ConnectAsync(uri, connect.Token);
                }
                var buffer = new byte[8192];
                while (socket.State == WebSocketState.Open)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        deadline.CancelAfter(TimeSpan.FromSeconds(35));
                        result = await socket.ReceiveAsync(buffer, deadline.Token);
                        if (result.MessageType == WebSocketMessageType.Close) throw new IOException("Bridge closed.");
                        if (message.Length + result.Count > 512 * 1024) throw new InvalidDataException("Bridge frame exceeds limit.");
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    using var valid = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 40 });
                    if (valid.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported schema.");
                    Publish(true, sequenceTiming.Enrich(valid.RootElement, DateTimeOffset.UtcNow));
                }
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested) { Publish(false); }
            if (!stoppingToken.IsCancellationRequested)
                try { await Task.Delay(2000, stoppingToken); } catch (OperationCanceledException) { }
        }
        Publish(false);
    }
}
