using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using StarfrontControl.Core;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var flags = new Dictionary<string, string>();
var words = new List<string>();
var jsonOutput = args.Contains("--json");
string? submittedRequestId = null;
var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "starfront-control", "cli.json");
try
{
    for (var index = 0; index < args.Length; index++)
    {
        var arg = args[index];
        if (arg is "--json" or "--dry-run" or "--wait" or "--help" or "--version") { flags[arg] = "true"; continue; }
        if (arg.StartsWith("--"))
        {
            if (arg is not ("--url" or "--token-file" or "--device-id" or "--request-id" or "--timeout" or "--params" or "--output")) throw new ArgumentException("Unknown option: " + arg);
            if (++index >= args.Length || args[index].StartsWith("--")) throw new ArgumentException("Missing option value: " + arg);
            flags[arg] = args[index];
        }
        else words.Add(arg);
    }
    jsonOutput |= words.FirstOrDefault() == "watch";
    if (flags.ContainsKey("--version"))
    {
        Print(new { name = "Starfront Control CLI", version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() });
        return 0;
    }
    if (words.Count == 0 || flags.ContainsKey("--help"))
    {
        Console.WriteLine("""
        Starfront Control
          init --url http://127.0.0.1:1890 --token-file PATH
          doctor                         Check service authentication and bridge
          status                         Read the cached full status
          devices                        Discover explicit device IDs
          sequence                       Read the full cached sequence tree
          camera connect --device-id ID [--dry-run] [--wait]
          mount connect --device-id ID [--dry-run] [--wait]
          camera disconnect [--dry-run] [--wait]
          mount disconnect [--dry-run] [--wait]
          DEVICE ACTION --params '{"parameter":value}' [--dry-run] [--wait]
          commands                       List command names and typed parameters
          operation REQUEST_ID           Inspect a retained command result
          watch                          Stream snapshots over WebSocket
          open                           Open the authenticated monitor page
          request get /api/v1/PATH        Read-only escape hatch; no legacy GET controls
        Options: --json, --version, --request-id UUID, --timeout SECONDS (default 30).
        Auth: STARFRONT_TOKEN / STARFRONT_URL override ~/.config/starfront-control/cli.json.
        --json emits JSON; watch emits newline-delimited JSON. Errors exit nonzero.
        Commands are never retried automatically. A CLI timeout does not cancel a device operation.
        """);
        return 0;
    }
    var values = File.Exists(configPath)
        ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(configPath), jsonOptions) ?? new()
        : new Dictionary<string, string>();
    if (words[0] == "init")
    {
        var url = flags.GetValueOrDefault("--url") ?? "http://127.0.0.1:1890";
        var tokenFile = flags.GetValueOrDefault("--token-file") ?? throw new ArgumentException("--token-file is required; tokens are not accepted on the command line.");
        var tokenValue = File.ReadAllText(tokenFile).Trim();
        if (tokenValue.Length < 32) throw new ArgumentException("Token is too short.");
        if (!new Uri(url).IsLoopback) throw new ArgumentException("This development CLI requires a loopback gateway URL.");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, JsonSerializer.Serialize(new { url, token = tokenValue }, jsonOptions));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(configPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Print(new { configured = true, url, configPath });
        return 0;
    }
    var baseUrl = Environment.GetEnvironmentVariable("STARFRONT_URL") ?? flags.GetValueOrDefault("--url") ?? values.GetValueOrDefault("url") ?? "http://127.0.0.1:1890";
    var token = Environment.GetEnvironmentVariable("STARFRONT_TOKEN") ?? values.GetValueOrDefault("token");
    if (string.IsNullOrEmpty(token))
    {
        if (words[0] == "doctor") { Print(new { configured = false, tokenAvailable = false, authSource = "missing", next = "Run init with --token-file." }); return 1; }
        throw new InvalidOperationException("Authentication is not configured. Run init or set STARFRONT_TOKEN.");
    }
    var timeout = int.Parse(flags.GetValueOrDefault("--timeout", "30"));
    if (timeout < 1 || timeout > 600) throw new ArgumentException("Timeout must be 1–600 seconds.");
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(timeout) };
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    async Task<JsonElement> Request(string method, string path, object? body = null)
    {
        using var message = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) message.Content = new StringContent(JsonSerializer.Serialize(body, jsonOptions), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(message, deadline.Token);
        var text = await response.Content.ReadAsStringAsync(deadline.Token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {SafeError(text)}");
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    async Task<ControlOperation?> Watch(string? operationId = null)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        var uri = new UriBuilder(baseUrl) { Scheme = baseUrl.StartsWith("https:") ? "wss" : "ws", Path = "/socket" }.Uri;
        using var watchStop = operationId is null ? new CancellationTokenSource() : CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; watchStop.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            await socket.ConnectAsync(uri, deadline.Token);
            var buffer = new byte[8192];
            while (!watchStop.IsCancellationRequested)
            {
                using var data = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, watchStop.Token);
                    if (result.MessageType == WebSocketMessageType.Close) throw new IOException("Monitor socket closed.");
                    if (data.Length + result.Count > 512 * 1024) throw new InvalidDataException("Frame too large.");
                    data.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                var frame = JsonSerializer.Deserialize<JsonElement>(data.GetBuffer().AsSpan(0, (int)data.Length));
                if (operationId is null) { Print(frame); continue; }
                if (frame.TryGetProperty("snapshot", out var snapshot) && snapshot.ValueKind == JsonValueKind.Object &&
                    snapshot.GetProperty("states").TryGetProperty("operations", out var operations))
                    foreach (var item in operations.GetProperty("value").GetProperty("items").EnumerateArray())
                    {
                        var operation = item.Deserialize<ControlOperation>(jsonOptions)!;
                        if (operation.RequestId == operationId && operation.CompletedAt is not null) return operation;
                    }
            }
            return null;
        }
        finally { Console.CancelKeyPress -= handler; socket.Abort(); }
    }

    switch (words[0])
    {
        case "doctor":
            var doctor = await Request("GET", "/api/v1/doctor");
            Print(new { configured = true, tokenAvailable = true, authSource = Environment.GetEnvironmentVariable("STARFRONT_TOKEN") is null ? "config" : "env", service = doctor });
            return doctor.GetProperty("bridgeConnected").GetBoolean() ? 0 : 1;
        case "image" when words.Count == 3 && words[1] == "download" && Guid.TryParse(words[2],out _):
            var destination=flags.GetValueOrDefault("--output") ?? throw new ArgumentException("--output is required.");
            using(var response=await http.GetAsync("/api/v1/images/"+words[2],HttpCompletionOption.ResponseHeadersRead,deadline.Token))
            {
                response.EnsureSuccessStatusCode();
                using var input=await response.Content.ReadAsStreamAsync(deadline.Token);
                using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write);
                var buffer=new byte[65536];long total=0;int read;
                while((read=await input.ReadAsync(buffer,deadline.Token))>0) { total+=read;if(total>128*1024*1024)throw new InvalidDataException("Download exceeds 128 MiB.");await output.WriteAsync(buffer.AsMemory(0,read),deadline.Token); }
                Print(new{downloaded=true,path=Path.GetFullPath(destination),bytes=total});
            }
            break;
        case "commands": Print(CommandCatalog.Definitions); break;
        case "status": Print(await Request("GET", "/api/v1/status")); break;
        case "sequence" when words.Count == 1:
            var sequenceState = await Request("GET", "/api/v1/status");
            if (sequenceState.TryGetProperty("snapshot", out var sequenceSnapshot) && sequenceSnapshot.ValueKind == JsonValueKind.Object && sequenceSnapshot.GetProperty("states").TryGetProperty("sequence", out var sequenceTopic))
                Print(new { bridge = sequenceState.GetProperty("bridge"), sequence = sequenceTopic });
            else throw new InvalidOperationException("Sequence telemetry is not available yet.");
            break;
        case "devices":
            var state = await Request("GET", "/api/v1/status");
            if (state.GetProperty("snapshot").ValueKind != JsonValueKind.Object) throw new InvalidOperationException("No bridge snapshot is available.");
            Print(state.GetProperty("snapshot").GetProperty("states").GetProperty("devices").GetProperty("value")); break;
        case "operation" when words.Count == 2 && Guid.TryParse(words[1], out _):
            Print(await Request("GET", "/api/v1/operations/" + words[1])); break;
        case "watch": await Watch(); break;
        case "open":
            var ticket = await Request("POST", "/api/v1/browser-session", new { });
            var launch = baseUrl.TrimEnd('/') + "/#session=" + ticket.GetProperty("ticket").GetString();
            var start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(launch);
            using (Process.Start(start)) { }
            Print(new { opened = true, url = baseUrl }); break;
        case "request" when words.Count == 3 && words[1].ToLowerInvariant() == "get":
            if (!words[2].StartsWith("/api/v1/", StringComparison.Ordinal) || words[2].Contains("..")) throw new ArgumentException("Only the new read API is supported.");
            Print(await Request("GET", words[2])); break;
        case var device when words.Count == 2 && CommandCatalog.Definitions.Any(x => x.Device == device && x.Action == words[1]):
            var command = new ControlCommand(flags.GetValueOrDefault("--request-id") ?? Guid.NewGuid().ToString(), words[0], words[1], flags.GetValueOrDefault("--device-id"), flags.TryGetValue("--params", out var parametersJson) ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parametersJson) : null);
            if (!Guid.TryParse(command.RequestId, out var identifier)) throw new ArgumentException("--request-id must be a UUID.");
            command = command with { RequestId = identifier.ToString("D") };
            CommandCatalog.Validate(command);
            submittedRequestId = command.RequestId;
            if (command.Action == "connect" && command.DeviceId is null) throw new ArgumentException("--device-id is required; use devices to discover IDs.");
            var submitted = await Request("POST", flags.ContainsKey("--dry-run") ? "/api/v1/commands/validate" : "/api/v1/commands", command);
            if (flags.ContainsKey("--wait") && !flags.ContainsKey("--dry-run"))
            {
                var operation = await Watch(command.RequestId) ?? throw new InvalidOperationException("Operation did not complete.");
                // The socket carries compact summaries; fetch the retained result
                // once at completion so parameters remain available in CLI JSON.
                var fullOperation = await Request("GET", "/api/v1/operations/" + command.RequestId);
                Print(fullOperation);
                return fullOperation.GetProperty("status").GetString() == "succeeded" ? 0 : 1;
            }
            Print(submitted); break;
        default: throw new ArgumentException("Unknown command. Run starfront-control --help.");
    }
    return 0;
}
catch (OperationCanceledException)
{
    Print(new { error = "Timed out or interrupted. A submitted operation may still be running; inspect its requestId before retrying.", code = "incomplete", requestId = submittedRequestId });
    return 2;
}
catch (Exception exception)
{
    Print(new { error = exception is HttpRequestException ? "Gateway connection failed." : exception.Message, code = "failed", requestId = submittedRequestId });
    return 1;
}

void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = !jsonOutput }));
static string SafeError(string text)
{
    try { return JsonSerializer.Deserialize<JsonElement>(text).GetProperty("error").GetString() ?? "Request failed."; }
    catch { return "Request failed."; }
}
