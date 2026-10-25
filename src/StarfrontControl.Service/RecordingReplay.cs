using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StarfrontControl.Service;

public sealed record ReplayMarker(long At, string Kind, string Label);
public sealed record ReplayGap(long Start, long End);
public sealed record ReplayChunk(long At, string File);
public sealed record ReplayIndex(string Id, string Fingerprint, JsonObject Recording, long Start, long End,
    long FirstTelemetryAt, long LastTelemetryAt, List<ReplayChunk> Chunks, List<ReplayMarker> Events, List<ReplayGap> Gaps, int OmittedEvents, bool Truncated);

/// <summary>Read-only journals; disposable seek cache is separate from recorder data and its process.</summary>
public sealed class RecordingReplay {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex Id = new("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.CultureInvariant);
    private readonly string root, cache;
    private readonly SemaphoreSlim gate = new(1);
    private const long CacheLimit = 512L * 1024 * 1024;
    public RecordingReplay(ServiceConfiguration config) : this(config.RecordingsDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/starfront-recorder/recordings"),
        config.ReplayCacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/starfront-web/replay-cache")) { }
    public RecordingReplay(string root, string cache) {
        this.root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); this.cache = Path.GetFullPath(cache).TrimEnd(Path.DirectorySeparatorChar);
        if(this.root==this.cache || this.cache.StartsWith(this.root+Path.DirectorySeparatorChar,StringComparison.Ordinal) || this.root.StartsWith(this.cache+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            throw new ArgumentException("Replay cache must be separate from original recordings.");
    }
    private static void Plain(string path) { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked recording paths are not supported."); }
    private string Folder(string id) {
        if (!Id.IsMatch(id)) throw new ArgumentException("Invalid recording ID.");
        Plain(root); var dir = Path.Combine(root, id); Plain(dir); return dir;
    }
    public object List() {
        if (!Directory.Exists(root)) return Array.Empty<object>();
        Plain(root);
        return Directory.EnumerateDirectories(root).Where(p => Id.IsMatch(Path.GetFileName(p))).Take(1001).Select(p => {
            Plain(p); var file=Path.Combine(p,"manifest.json");Plain(file);
            if(new FileInfo(file).Length>16384)throw new InvalidDataException("Recording metadata exceeds bounds.");
            return JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        }).OrderByDescending(m => m["startedAt"]?.GetValue<string>()).Take(1000).ToArray();
    }
    public async Task<ReplayIndex> Index(string id, CancellationToken cancel = default) {
        await gate.WaitAsync(cancel);
        try { return await Task.Run(() => Build(id), cancel); } finally { gate.Release(); }
    }
    private ReplayIndex Build(string id) {
        var dir=Folder(id);var manifest=Path.Combine(dir,"manifest.json");Plain(manifest);
        if(new FileInfo(manifest).Length>16384)throw new InvalidDataException("Recording metadata exceeds bounds.");
        var raw=File.ReadAllText(manifest);var meta=JsonNode.Parse(raw)!.AsObject();
        if(meta["state"]?.GetValue<string>()=="recording")throw new InvalidOperationException("This recording is still active. Replay is available after it closes.");
        var files=Directory.GetFiles(dir,"events-*.ndjson.gz").OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        if(files.Length>256 || files.Sum(f=>new FileInfo(f).Length)>1024L*1024*1024)throw new InvalidDataException("Recording exceeds replay bounds.");
        foreach(var file in files)Plain(file);
        var fingerprint=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("replay-1:"+raw+string.Join(";",files.Select(f=>new FileInfo(f).Length+":"+File.GetLastWriteTimeUtc(f).Ticks)))));
        Directory.CreateDirectory(cache);Plain(cache);var dest=Path.Combine(cache,id);var indexFile=Path.Combine(dest,"index.json");
        if(File.Exists(indexFile)) { Plain(dest);Plain(indexFile);var old=JsonSerializer.Deserialize<ReplayIndex>(File.ReadAllText(indexFile),Json);if(old?.Fingerprint==fingerprint){Directory.SetLastWriteTimeUtc(dest,DateTime.UtcNow);return old;}Directory.Delete(dest,true); }
        // Only owned cache directories are disposable. Original recordings are never changed.
        var priorCaches=Directory.GetDirectories(cache).Where(p=>Id.IsMatch(Path.GetFileName(p))).OrderBy(Directory.GetLastWriteTimeUtc).ToList();
        long Size(string p)=>Directory.EnumerateFiles(p,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length);
        foreach(var stale in Directory.GetDirectories(cache).Where(p=>p.EndsWith(".building",StringComparison.Ordinal)&&Id.IsMatch(Path.GetFileName(p)[..^9]))) {Plain(stale);Directory.Delete(stale,true);}
        var reserve=files.Sum(f=>new FileInfo(f).Length)*2+8*1024*1024;
        if(new DriveInfo(Path.GetPathRoot(cache)!).AvailableFreeSpace<Math.Min(CacheLimit,reserve)+512L*1024*1024)throw new InvalidDataException("Not enough free space for disposable replay cache.");
        while(priorCaches.Count>0 && (priorCaches.Count>=2 || priorCaches.Sum(Size)+reserve>CacheLimit)) {var prior=priorCaches[0];Plain(prior);Directory.Delete(prior,true);priorCaches.RemoveAt(0);}
        var temp=Path.Combine(cache,id+".building");if(Directory.Exists(temp)){Plain(temp);Directory.Delete(temp,true);}Directory.CreateDirectory(temp);
        long start=Millis(meta["startedAt"]),end=Millis(meta["finishedAt"] ?? meta["updatedAt"]);
        var state=new ReplayState();var chunks=new List<ReplayChunk>();var events=new List<ReplayMarker>();var gaps=new List<ReplayGap>();int omitted=0;bool truncated=false;long written=0,lastIndex=-1,firstTelemetry=0;
        StreamWriter? output=null;FileStream? chunkFile=null;long chunkAt=long.MinValue;string target="",step="",roof="",image="";bool exposing=false;var failures=new HashSet<string>();
        void Mark(long at,string kind,string label){if(events.Count<10000)events.Add(new(at,kind,label.Length>180?label[..180]:label));else omitted++;}
        void CloseChunk(){output?.Dispose();output=null;if(chunkFile is not null){written+=new FileInfo(chunkFile.Name).Length;chunkFile=null;}if(written>CacheLimit)throw new InvalidDataException("Replay cache limit reached.");}
        try {
            foreach(var file in files) {
                try {
                    using var input=File.OpenRead(file);using var gzip=new GZipStream(input,CompressionMode.Decompress);using var reader=new StreamReader(gzip);
                    while(true) {
                        string? line;
                        try { line=reader.ReadLine(); } catch(InvalidDataException) when(meta["state"]?.GetValue<string>()=="interrupted") {truncated=true;break;}
                        if(line is null)break;
                        if(line.Length>1024*1024)throw new InvalidDataException("Journal entry exceeds replay bounds.");
                        JsonObject row;
                        try {row=JsonNode.Parse(line)!.AsObject();}catch(JsonException){truncated=true;break;}
                        var index=row["index"]?.GetValue<long>()??-1;if(index!=lastIndex+1)throw new InvalidDataException("Journal indices are not contiguous.");lastIndex=index;
                        var at=Millis(row["receivedAt"]);if(at<start)throw new InvalidDataException("Journal time precedes recording.");
                        end=Math.Max(end,at);
                        if(output is null || at-chunkAt>=60000) {
                            CloseChunk();chunkAt=at;var name=$"chunk-{chunks.Count:D5}.gz";if(chunks.Count>=3000)throw new InvalidDataException("Recording duration exceeds replay bounds.");
                            chunks.Add(new(at,name));chunkFile=new FileStream(Path.Combine(temp,name),FileMode.CreateNew,FileAccess.Write);
                            output=new StreamWriter(new GZipStream(chunkFile,CompressionLevel.Fastest));
                            output.WriteLine(JsonSerializer.Serialize(new {kind="seed",state},Json));
                        }
                        output.WriteLine(line);var priorGap=state.GapStart;state.Apply(row);if(firstTelemetry==0&&state.LastTelemetryAt>0)firstTelemetry=state.LastTelemetryAt;
                        var kind=row["kind"]?.GetValue<string>();
                        if(kind=="gap-start")Mark(at,"gap","Telemetry disconnected");
                        if(kind=="gap-end" && priorGap is { } gap) {gaps.Add(new(gap,at));Mark(at,"connection","Telemetry restored");}
                        if(kind=="finish")Mark(at,"finish","Recording "+(row["data"]?["state"]?.GetValue<string>()??"ended"));
                        if(kind is not ("checkpoint" or "update"))continue;
                        var q=state.States["sequence"]?["value"];var nodes=q?["nodes"] as JsonArray;var active=q?["activeNodeIds"] as JsonArray;
                        if(nodes is not null && active?.FirstOrDefault()?.GetValue<string>() is { } activeId) {
                            var n=nodes.OfType<JsonObject>().FirstOrDefault(n=>n["id"]?.GetValue<string>()==activeId);
                            var key=activeId+":"+state.SessionId;
                            if(key!=step && n is not null){step=key;Mark(at,"step",n["name"]?.GetValue<string>()??"Sequence step");}
                            var seen=new HashSet<string>();
                            while(n is not null && seen.Add(n["id"]!.GetValue<string>())) {
                                if(n["type"]?.GetValue<string>()=="DeepSkyObjectContainer") {var name=n["name"]?.GetValue<string>()??"Target";if(name!=target){target=name;Mark(at,"target",name);}break;}
                                var parent=n["parentId"]?.GetValue<string>();n=parent is null?null:nodes.OfType<JsonObject>().FirstOrDefault(n=>n["id"]?.GetValue<string>()==parent);
                            }
                        }
                        var observedFailures=new HashSet<string>();
                        foreach(var op in (state.States["operations"]?["value"]?["items"] as JsonArray ?? new()).OfType<JsonObject>()) {
                            if(op["status"]?.GetValue<string>() is not ("failed" or "rejected"))continue;
                            var key=op["requestId"]?.GetValue<string>()??"";observedFailures.Add(key);
                            if(!failures.Contains(key))Mark(at,"error","Observed failure: "+op["device"]?.GetValue<string>()+" "+op["action"]?.GetValue<string>());
                        }
                        failures=observedFailures;
                        var safety=state.States["safety"]?["value"];var r=safety?["connected"]?.GetValue<bool>()==true?safety["isSafe"]?.GetValue<bool>()==true?"Roof open":"Roof closed":"Roof unknown";
                        if(r!=roof){roof=r;Mark(at,"roof",r);}
                        var cam=state.States["camera"]?["value"];var exp=cam?["isExposing"]?.GetValue<bool>()==true;if(exp&&!exposing)Mark(at,"exposure","Exposure started");exposing=exp;
                        var img=state.States["image"]?["value"]?["filename"]?.GetValue<string>();if(img is not null && img!=image){image=img;Mark(at,"image",Path.GetFileName(img));}
                    }
                } finally { }
            }
            CloseChunk();
            if(state.GapStart is { } open)gaps.Add(new(open,end));else if(truncated && state.LastTelemetryAt>0)gaps.Add(new(state.LastTelemetryAt,end));
            var result=new ReplayIndex(id,fingerprint,meta,start,Math.Max(start,end),firstTelemetry,state.LastTelemetryAt,chunks,events,gaps,omitted,truncated);
            File.WriteAllText(Path.Combine(temp,"index.json"),JsonSerializer.Serialize(result,Json));Directory.Move(temp,dest);return result;
        }catch{output?.Dispose();if(Directory.Exists(temp))Directory.Delete(temp,true);throw;}
    }
    public async Task<object> At(string id,long at,CancellationToken cancel=default) {
        await gate.WaitAsync(cancel);
        try {
            return await Task.Run<object>(()=>{
                var index=Build(id);at=Math.Clamp(at,index.Start,index.End);var chunk=index.Chunks.LastOrDefault(c=>c.At<=at);var state=new ReplayState();
                if(chunk is not null) {
                    using var input=File.OpenRead(Path.Combine(cache,id,chunk.File));using var gz=new GZipStream(input,CompressionMode.Decompress);using var reader=new StreamReader(gz);
                    var seed=JsonNode.Parse(reader.ReadLine()!)!;state=seed["state"]!.Deserialize<ReplayState>(Json)!;
                    while(reader.ReadLine() is { } line){var row=JsonNode.Parse(line)!.AsObject();if(Millis(row["receivedAt"])>at)break;state.Apply(row);}
                }
                var gap=index.Gaps.Any(g=>at>=g.Start&&at<g.End)||state.GapStart is not null||state.LastTelemetryAt==0;
                if(state.States["preview"]?["value"] is JsonObject preview && preview["recordingFile"]?.GetValue<string>() is { } media) {
                    if(!Regex.IsMatch(media,"^previews/[0-9a-f]{64}\\.jpg$"))throw new InvalidDataException("Invalid recorded image reference.");
                    var path=Path.Combine(Folder(id),media);Plain(Path.GetDirectoryName(path)!);Plain(path);
                    if(new FileInfo(path).Length>256*1024)throw new InvalidDataException("Recorded image exceeds bounds.");preview["jpeg"]=Convert.ToBase64String(File.ReadAllBytes(path));
                }
                return new {mode="replay",recordingId=id,at,lastTelemetryAt=state.LastTelemetryAt,gap,
                    snapshot=new {schemaVersion=1,sessionId="replay:"+id+":"+state.SessionId,revision=state.Revision,states=state.States}};
            },cancel);
        }finally{gate.Release();}
    }
    private static long Millis(JsonNode? value)=>DateTimeOffset.Parse(value?.GetValue<string>()??throw new InvalidDataException("Missing journal time.")).ToUnixTimeMilliseconds();
    public sealed class ReplayState {
        public JsonObject States {get;set;}=new();public string SessionId {get;set;}="";public long Revision {get;set;}public long LastTelemetryAt {get;set;}public long? GapStart {get;set;}
        public string TimingScope {get;set;}="";public bool TimingBaseline {get;set;}=true;
        public Dictionary<string,StepObservation> Timings {get;set;}=new();
        public sealed class StepObservation {public string State {get;set;}="";public string? Started {get;set;}public string? Ended {get;set;}public string? Seen {get;set;}}
        public void Apply(JsonObject row) {
            var kind=row["kind"]?.GetValue<string>();var at=Millis(row["receivedAt"]);
            if(kind=="gap-start"){GapStart=at;TimingBaseline=true;return;}if(kind=="gap-end"){GapStart=null;return;}
            if(kind is not ("checkpoint" or "update"))return;
            var data=row["data"]!;if(kind=="checkpoint")States=new();
            foreach(var pair in data["states"]!.AsObject())States[pair.Key]=pair.Value?.DeepClone();
            foreach(var key in data["removed"]?.AsArray()??new JsonArray())States.Remove(key!.GetValue<string>());
            SessionId=data["sessionId"]!.GetValue<string>();Revision=data["revision"]!.GetValue<long>();LastTelemetryAt=at;
            if(data["states"]?["sequence"] is not null)ObserveSteps(at);
        }
        private void ObserveSteps(long at) {
            var sequence=States["sequence"]?["value"];var scope=SessionId+"/"+sequence?["sequenceId"]?.GetValue<string>();
            if(scope!=TimingScope){Timings.Clear();TimingScope=scope;TimingBaseline=true;}
            var stamp=DateTimeOffset.FromUnixTimeMilliseconds(at).ToString("O");var present=new HashSet<string>();
            foreach(var n in (sequence?["nodes"] as JsonArray ?? new()).OfType<JsonObject>().Take(256)) {
                if(n["id"]?.GetValue<string>() is not { } id)continue;present.Add(id);var state=n["state"]?.GetValue<string>()??"unknown";bool active=state is "running" or "waiting";
                if(!Timings.TryGetValue(id,out var timing)){timing=new(){State=state,Seen=active?stamp:null};Timings[id]=timing;}
                else if(timing.State!=state){if(state=="pending")timing.Started=timing.Ended=timing.Seen=null;
                    else if(!TimingBaseline&&active&&timing.State is not ("running" or "waiting")){timing.Started=stamp;timing.Ended=timing.Seen=null;}
                    else if(!TimingBaseline&&state is "completed" or "failed" or "skipped" or "cancelled")timing.Ended=stamp;
                    timing.State=state;}
                if(n["timing"] is null&&(timing.Started is not null||timing.Ended is not null||timing.Seen is not null))n["timing"]=new JsonObject { ["source"]="recording-received",["startedAt"]=timing.Started,["endedAt"]=timing.Ended,["seenActiveAt"]=timing.Seen };
            }
            foreach(var id in Timings.Keys.Where(k=>!present.Contains(k)).ToArray())Timings.Remove(id);TimingBaseline=false;
        }
    }
}
