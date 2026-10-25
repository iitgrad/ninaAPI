namespace StarfrontControl.Core;

public sealed record SafetyConnectionStatus(string Policy,bool Connecting,string? Error,DateTimeOffset? RetryAt);
/// <summary>One connection owner. Viewers request checks; they never own retry loops.</summary>
public sealed class SafetyConnectionManager(Func<bool> connected,Func<Task> connect,Func<DateTimeOffset>? clock=null) : IDisposable {
    private readonly object gate=new();
    private readonly Func<DateTimeOffset> now=clock??(()=>DateTimeOffset.UtcNow);
    private Task? attempt;
    private int failures;
    private DateTimeOffset retryAt;
    private string? error;
    private bool stopped;
    public event Action<SafetyConnectionStatus>? Changed;
    public SafetyConnectionStatus Status { get { lock(gate)return new("always-connected",attempt is {IsCompleted:false},error,error==null?null:retryAt); } }
    public Task EnsureAsync() {
        lock(gate) {
            if(stopped)return Task.CompletedTask;
            if(attempt is {IsCompleted:false})return attempt;
            if(connected()){failures=0;error=null;Changed?.Invoke(Status);return Task.CompletedTask;}
            if(now()<retryAt)return Task.CompletedTask;
            var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            attempt=completion.Task;
            Changed?.Invoke(Status);
            _=Run(completion);
            return completion.Task;
        }
    }
    private async Task Run(TaskCompletionSource completion) {
        try {
            await connect();
            if(!connected())throw new InvalidOperationException("Safety monitor did not connect.");
            lock(gate){failures=0;error=null;}
            completion.TrySetResult();
        } catch(Exception e) {
            lock(gate){error=e.GetBaseException().Message;retryAt=now().AddSeconds(Math.Min(30,Math.Pow(2,Math.Min(failures++,5))));}
            completion.TrySetException(e);
        } finally {lock(gate){if(!stopped)Changed?.Invoke(Status);}}
    }
    public void Dispose(){lock(gate){stopped=true;Changed=null;}}
}
