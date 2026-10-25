using System.Text.Json;
namespace StarfrontControl.Core;

public sealed record ControlCommand(string RequestId, string Device, string Action, string? DeviceId = null, IReadOnlyDictionary<string, JsonElement>? Parameters = null);
public sealed record ControlOperation(string RequestId, string Device, string Action, string? DeviceId,
    string Status, DateTimeOffset AcceptedAt, DateTimeOffset? CompletedAt = null, string? Error = null, IReadOnlyDictionary<string, JsonElement>? Parameters = null);
public sealed class CommandConflictException(string message) : Exception(message);

/// <summary>Bounded deduplication window and one active operation per device.</summary>
public sealed class CommandCoordinator(int capacity = 128)
{
    private readonly object gate = new();
    private readonly Dictionary<string, ControlOperation> operations = new();
    private readonly HashSet<string> activeDevices = new();
    public event Action<ControlOperation>? Changed;
    public int ActiveCount { get { lock (gate) return activeDevices.Count; } }
    public ControlOperation[] Read() { lock (gate) return operations.Values.ToArray(); }
    public ControlOperation? Find(string id)
    {
        if (!Guid.TryParse(id, out var identifier)) return null;
        lock (gate) return operations.GetValueOrDefault(identifier.ToString("D"));
    }

    public ControlOperation Submit(ControlCommand command, Func<Task> execute)
    {
        if (!Guid.TryParse(command.RequestId, out var identifier)) throw new ArgumentException("requestId must be a UUID.");
        // UUID casing and accepted textual forms must not create new executions.
        command = command with { RequestId = identifier.ToString("D"), Parameters = command.Parameters?.ToDictionary(x => x.Key, x => x.Value.Clone()) };
        var definition = CommandCatalog.Validate(command);
        if (command.DeviceId?.Length > 512) throw new ArgumentException("Device ID is too long.");
        ControlOperation operation;
        lock (gate)
        {
            if (operations.TryGetValue(command.RequestId, out var existing))
            {
                if (!CommandCatalog.Same(existing, command))
                    throw new CommandConflictException("requestId already identifies a different command.");
                return existing;
            }
            var reservation = command.Device + (definition.Interrupt ? ":interrupt" : "");
            if (activeDevices.Contains(reservation) || (!definition.Interrupt && activeDevices.Contains(command.Device + ":interrupt"))) throw new CommandConflictException("Device already has an active operation.");
            if (operations.Count >= capacity)
            {
                var oldest = operations.Values.Where(x => x.CompletedAt is not null).MinBy(x => x.AcceptedAt);
                if (oldest is null) throw new CommandConflictException("Operation capacity reached.");
                operations.Remove(oldest.RequestId);
            }
            operation = new(command.RequestId, command.Device, command.Action, command.DeviceId, "accepted", DateTimeOffset.UtcNow, Parameters: command.Parameters);
            operations.Add(command.RequestId, operation);
            activeDevices.Add(reservation);
        }
        Notify(operation);
        _ = Task.Run(() => Run(operation, execute));
        return operation;
    }

    private async Task Run(ControlOperation operation, Func<Task> execute)
    {
        Set(operation with { Status = "running" });
        try
        {
            await execute().ConfigureAwait(false);
            Set(operation with { Status = "succeeded", CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (OperationCanceledException)
        {
            Set(operation with { Status = "cancelled", CompletedAt = DateTimeOffset.UtcNow });
        }
        catch (CommandConflictException e) when(operation.Device=="sequence" && operation.Action is "insertDraft" or "save") {
            Set(operation with { Status="rejected",CompletedAt=DateTimeOffset.UtcNow,Error=e.Message });
        }
        catch (Exception)
        {
            // Detailed exceptions stay in the local application log, never in a
            // network response that could contain driver credentials or paths.
            Set(operation with { Status = "failed", CompletedAt = DateTimeOffset.UtcNow, Error = "Device operation failed; inspect the local N.I.N.A. log." });
        }
    }

    private void Set(ControlOperation operation)
    {
        lock (gate)
        {
            operations[operation.RequestId] = operation;
            if (operation.CompletedAt is not null) activeDevices.Remove(operation.Device + (CommandCatalog.Definitions.Any(x => x.Device == operation.Device && x.Action == operation.Action && x.Interrupt) ? ":interrupt" : ""));
        }
        Notify(operation);
    }

    private void Notify(ControlOperation operation)
    {
        // Notification handlers must be short enqueue operations. A diagnostics
        // failure must not strand a device reservation or repeat an operation.
        try { Changed?.Invoke(operation); } catch { }
    }
}
