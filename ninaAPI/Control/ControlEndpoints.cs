using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using EmbedIO.WebSockets;
using StarfrontControl.Core;

namespace ninaAPI.Control;

public sealed class ControlAuthModule(ControlConfiguration configuration) : WebModuleBase("/")
{
    protected override Task OnRequestAsync(IHttpContext context)
    {
        // Native clients connect by address over the private network; browsers use the local gateway.
        if (!string.IsNullOrEmpty(context.Request.Headers["Origin"])) throw HttpException.Forbidden("Use the local monitor gateway.");
        return Task.CompletedTask;
    }
    public override bool IsFinalHandler => false;
}

public sealed class ControlEndpoints : WebApiController
{
    private ControlAdapter Adapter => ControlAdapter.Current ?? throw new HttpException(503, "Adapter is stopped.");
    private Task Respond(object value, int status = 200)
    {
        HttpContext.Response.StatusCode = status;
        return HttpContext.SendStringAsync(JsonSerializer.Serialize(value, ControlAdapter.Json), "application/json", Encoding.UTF8);
    }

    [Route(HttpVerbs.Get, "/status")]
    public Task Status() => HttpContext.SendStringAsync(Adapter.Store.Read().Json, "application/json", Encoding.UTF8);

    [Route(HttpVerbs.Get, "/sequence/insertion-schema")]
    public async Task InsertionSchema([QueryField] string type) {
        try {
            var schema=await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => SequenceInsertion.Schema(type));
            await Respond(schema);
        } catch (ArgumentException e) { await Respond(new {error=e.Message},400); }
        catch (Exception) { await Respond(new {error="Detached schema unavailable in this host."},503); }
    }

    [Route(HttpVerbs.Get, "/diagnostics")]
    public Task Diagnostics() => Respond(Adapter.CachedMetrics);

    [Route(HttpVerbs.Get, "/operations/{id}")]
    public Task Operation(string id)
    {
        var operation = Adapter.Commands.Find(id);
        return operation is null ? Respond(new { error = "Operation not retained or not found." }, 404) : Respond(operation);
    }

    [Route(HttpVerbs.Get, "/images/{id}")]
    public async Task DownloadImage(string id)
    {
        var path=Adapter.SavedFile(id);
        if(path is null) { await Respond(new {error="Saved image is not retained or exceeds the download bound."},404);return; }
        HttpContext.Response.ContentType="application/octet-stream";
        HttpContext.Response.Headers["Content-Disposition"]="attachment; filename=starfront-image.fits";
        using var file=File.OpenRead(path);
        await file.CopyToAsync(HttpContext.Response.OutputStream,HttpContext.CancellationToken);
    }

    [Route(HttpVerbs.Post, "/commands")]
    public Task Command() => Submit(false);

    [Route(HttpVerbs.Post, "/commands/validate")]
    public Task Validate() => Submit(true);

    private async Task Submit(bool dryRun)
    {
        try
        {
            var buffer = new byte[8193];
            var used = 0;
            while (used < buffer.Length)
            {
                var read = await HttpContext.Request.InputStream.ReadAsync(buffer.AsMemory(used), HttpContext.CancellationToken);
                if (read == 0) break;
                used += read;
            }
            if (used > 8192) { await Respond(new { error = "Command body too large." }, 413); return; }
            var command = JsonSerializer.Deserialize<ControlCommand>(buffer.AsSpan(0, used), ControlAdapter.Json)
                ?? throw new ArgumentException("Command body is required.");
            if (!Guid.TryParse(command.RequestId, out var identifier)) throw new ArgumentException("requestId must be a UUID.");
            command = command with { RequestId = identifier.ToString("D") };
            var existing = Adapter.Commands.Find(command.RequestId);
            if (existing is not null && !CommandCatalog.Same(existing, command))
                throw new CommandConflictException("requestId was already used for a different command.");
            if (existing is null)
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Adapter.Validate(command));
            if (existing is null) await Adapter.ValidateRemoteDependencies(command);
            if (dryRun) { await Respond(new { valid = true, dryRun = true, command }); return; }
            var operation = Adapter.Commands.Submit(command, () => Adapter.Execute(command));
            await Respond(operation, 202);
        }
        catch (CommandConflictException exception) { await Respond(new { error = exception.Message }, 409); }
        catch (ArgumentException exception) { await Respond(new { error = exception.Message }, 400); }
        catch (JsonException) { await Respond(new { error = "Invalid command JSON." }, 400); }
        catch (Exception e) { NINA.Core.Utility.Logger.Error(e,"Starfront command validation failed");await Respond(new {error="Host validation is unavailable; inspect the local N.I.N.A. log."},503); }
    }
}

public sealed class ControlSocket : WebSocketModule
{
    private readonly ControlAdapter adapter;
    private sealed class Client(SnapshotHub.Subscription subscription, CancellationTokenSource stop)
    {
        public SnapshotHub.Subscription Subscription { get; } = subscription;
        public CancellationTokenSource Stop { get; } = stop;
        public Task Sender { get; set; } = Task.CompletedTask;
    }
    private readonly ConcurrentDictionary<string, Client> clients = new();
    public static ControlSocket Current { get; private set; }
    public int RetainedClients => clients.Count;
    public long SendFailures;
    public ControlSocket(ControlAdapter adapter) : base("/socket/v1", true)
    {
        this.adapter = adapter;
        Current = this;
        MaxMessageSize = 1024;
    }

    protected override Task OnClientConnectedAsync(IWebSocketContext context)
    {
        SnapshotHub.Subscription subscription;
        try { subscription = adapter.Hub.Subscribe(); }
        catch (InvalidOperationException) { return context.WebSocket.CloseAsync(CancellationToken.None); }
        var stop = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var client = new Client(subscription, stop);
        var token = stop.Token;
        clients[context.Id] = client;
        client.Sender = Task.Run(() => SendLoop(context, subscription, token));
        return Task.CompletedTask;
    }

    private async Task SendLoop(IWebSocketContext context, SnapshotHub.Subscription subscription, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var json = await subscription.ReadAsync(token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                await context.WebSocket.SendAsync(Encoding.UTF8.GetBytes(json), true, deadline.Token).WaitAsync(deadline.Token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            Interlocked.Increment(ref SendFailures);
            context.WebSocket.Dispose();
        }
        finally
        {
            RemoveClient(context.Id);
            subscription.Dispose();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await context.WebSocket.CloseAsync(deadline.Token);
            }
            catch (Exception) { }
        }
    }

    protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        => Task.CompletedTask; // State only; writes use authenticated POST endpoints.

    private void RemoveClient(string id)
    {
        if (clients.TryRemove(id, out var client))
        {
            client.Stop.Cancel();
            client.Subscription.Dispose();
            _ = client.Sender.ContinueWith(_ => client.Stop.Dispose(), TaskScheduler.Default);
        }
    }

    protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
    {
        RemoveClient(context.Id);
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var id in clients.Keys) RemoveClient(id);
        if (ReferenceEquals(Current, this)) Current = null;
        base.Dispose(disposing);
    }
}
