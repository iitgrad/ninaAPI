using System.Threading.Channels;

namespace StarfrontControl.Core;

/// <summary>One pending snapshot per client. Producers never await readers.</summary>
public sealed class SnapshotHub(int maxClients = 8)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Subscription> clients = new();
    private string? latest;
    private long coalesced;
    public int ClientCount { get { lock (gate) return clients.Count; } }
    public long CoalescedCount => Interlocked.Read(ref coalesced);

    public Subscription Subscribe()
    {
        lock (gate)
        {
            if (clients.Count >= maxClients) throw new InvalidOperationException("Client limit reached.");
            var client = new Subscription(this);
            clients.Add(client.Id, client);
            if (latest is not null) client.Queue.Writer.TryWrite(latest);
            return client;
        }
    }

    public void Publish(string json)
    {
        lock (gate)
        {
            latest = json;
            foreach (var client in clients.Values)
            {
                if (!client.Queue.Writer.TryWrite(json))
                {
                    if (client.Queue.Reader.TryRead(out _)) Interlocked.Increment(ref coalesced);
                    client.Queue.Writer.TryWrite(json);
                }
            }
        }
    }

    public sealed class Subscription : IDisposable
    {
        private readonly SnapshotHub owner;
        internal Guid Id { get; } = Guid.NewGuid();
        internal Channel<string> Queue { get; } = Channel.CreateBounded<string>(1);
        internal Subscription(SnapshotHub owner) => this.owner = owner;
        public ValueTask<string> ReadAsync(CancellationToken token) => Queue.Reader.ReadAsync(token);
        public void Dispose()
        {
            lock (owner.gate)
            {
                owner.clients.Remove(Id);
                Queue.Writer.TryComplete();
                while (Queue.Reader.TryRead(out _)) { }
            }
        }
    }
}
