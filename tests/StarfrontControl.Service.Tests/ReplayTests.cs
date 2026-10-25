using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using StarfrontControl.Service;
using Xunit;
namespace StarfrontControl.Service.Tests;
public sealed class ReplayTests:IDisposable {
 readonly string dir=Path.Combine(Path.GetTempPath(),"starfront-replay-"+Guid.NewGuid());readonly string id=Guid.NewGuid().ToString();readonly long start=DateTimeOffset.Parse("2026-10-24T23:00:00Z").ToUnixTimeMilliseconds();
 string Root=>Path.Combine(dir,"recordings");string Cache=>Path.Combine(dir,"cache");string Folder=>Path.Combine(Root,id);
 string Stamp(long offset)=>DateTimeOffset.FromUnixTimeMilliseconds(start+offset).ToString("O");
 object Frame(string kind,long offset,object states,string[]? removed=null)=>new {kind,receivedAt=Stamp(offset),data=new {sessionId="recorded-session",revision=offset,states,removed=removed??[]}};
 void Write(params object[] rows){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"manifest.json"),JsonSerializer.Serialize(new {id,name="Night",rig="starfront",environment="equipment",state="completed",startedAt=Stamp(0),finishedAt=Stamp(300000),updatedAt=Stamp(300000),frames=rows.Length,previews=0}));using var f=File.Create(Path.Combine(Folder,"events-000001.ndjson.gz"));for(int i=0;i<rows.Length;i++){var row=JsonSerializer.SerializeToNode(rows[i])!.AsObject();row["index"]=i;using var gz=new GZipStream(f,CompressionLevel.Fastest,true);JsonSerializer.Serialize(gz,row);gz.WriteByte(10);}}
 RecordingReplay Reader()=>new(Root,Cache);
 async Task<JsonObject> At(RecordingReplay r,long offset)=>JsonSerializer.SerializeToNode(await r.At(id,start+offset))!.AsObject();
 [Fact]public async Task ScrubsCheckpointsDeltasRemovalsAndGapsWithoutChangingOriginals(){
  Write(Frame("checkpoint",0,new {camera=new {value=new {connected=true,temperature=10}},safety=new {value=new {connected=true,isSafe=false}}}),Frame("update",30000,new {camera=new {value=new {connected=true,temperature=5}}}),new {kind="gap-start",receivedAt=Stamp(60000),data=new {}},new {kind="gap-end",receivedAt=Stamp(180000),data=new {}},Frame("checkpoint",180000,new {camera=new {value=new {connected=false,temperature=8}}}),Frame("update",240000,new {},["camera"]));
  var original=File.ReadAllBytes(Path.Combine(Folder,"events-000001.ndjson.gz"));var r=Reader();var index=await r.Index(id);Assert.Single(index.Gaps);Assert.Equal(start+60000,index.Gaps[0].Start);Assert.True(index.Chunks.Count>=3);
  var a=await At(r,40000);Assert.Equal(5,a["snapshot"]!["states"]!["camera"]!["value"]!["temperature"]!.GetValue<int>());Assert.False(a["gap"]!.GetValue<bool>());
  var gap=await At(r,150000);Assert.True(gap["gap"]!.GetValue<bool>());Assert.Equal(start+30000,gap["lastTelemetryAt"]!.GetValue<long>());
  var b=await At(r,200000);Assert.False(b["gap"]!.GetValue<bool>());Assert.Null(b["snapshot"]!["states"]!["safety"]);
  Assert.Null((await At(r,280000))["snapshot"]!["states"]!["camera"]);
  Assert.Equal(10,(await At(r,0))["snapshot"]!["states"]!["camera"]!["value"]!["temperature"]!.GetValue<int>());
  Assert.Equal(original,File.ReadAllBytes(Path.Combine(Folder,"events-000001.ndjson.gz")));
 }
 [Fact]public async Task InterruptedOpenGapExtendsThroughEndAndDoesNotInventTerminalState(){
  Write(Frame("checkpoint",0,new {application=new {value=new {sequenceRunning=true}}}),new {kind="gap-start",receivedAt=Stamp(60000),data=new {}},new {kind="finish",receivedAt=Stamp(300000),data=new {state="interrupted"}});
  var r=Reader();Assert.True((await At(r,299000))["gap"]!.GetValue<bool>());Assert.True((await At(r,300000))["gap"]!.GetValue<bool>());Assert.Single((await r.Index(id)).Gaps);
 }
 [Fact]public async Task BlocksUnsafeIdsLinkedFilesAndActiveRecordings(){
  Write(Frame("checkpoint",0,new {}));var r=Reader();await Assert.ThrowsAsync<ArgumentException>(()=>r.Index("../../elsewhere"));
  var path=Path.Combine(Folder,"manifest.json");var m=JsonNode.Parse(File.ReadAllText(path))!;m["state"]="recording";File.WriteAllText(path,m.ToJsonString());await Assert.ThrowsAsync<InvalidOperationException>(()=>r.Index(id));
  m["state"]="completed";File.WriteAllText(path,m.ToJsonString());File.Move(path,path+".original");File.CreateSymbolicLink(path,path+".original");await Assert.ThrowsAsync<InvalidDataException>(()=>r.Index(id));
 }
 [Fact]public async Task RecordedPreviewComesFromArchiveAndDisallowsTraversal(){
  Write(Frame("checkpoint",0,new {preview=new {value=new {revision=1,recordingFile="previews/"+new string('a',64)+".jpg"}}}));Directory.CreateDirectory(Path.Combine(Folder,"previews"));File.WriteAllBytes(Path.Combine(Folder,"previews",new string('a',64)+".jpg"),[255,216,255,217]);
  Assert.Equal("/9j/2Q==",(await At(Reader(),0))["snapshot"]!["states"]!["preview"]!["value"]!["jpeg"]!.GetValue<string>());
 }
 [Fact]public async Task StepReceiptTimesFollowSelectedTimeAndDoNotBridgeGaps(){
  object Q(string state)=>new {sequence=new {value=new {sequenceId="sequence",nodes=new[]{new {id="step",state,name="Wait",type="WaitForTimeSpan",parentId=(string?)null}}}}};
  Write(Frame("checkpoint",0,Q("pending")),Frame("update",1000,Q("running")),Frame("update",2000,Q("completed")));
  var r=Reader();var running=await At(r,1500);var n=running["snapshot"]!["states"]!["sequence"]!["value"]!["nodes"]![0]!;
  Assert.Equal(Stamp(1000),n["timing"]!["startedAt"]!.GetValue<string>());Assert.Null(n["timing"]!["endedAt"]);
  Assert.Null((await At(r,500))["snapshot"]!["states"]!["sequence"]!["value"]!["nodes"]![0]!["timing"]);
  Assert.Equal(Stamp(2000),(await At(r,2500))["snapshot"]!["states"]!["sequence"]!["value"]!["nodes"]![0]!["timing"]!["endedAt"]!.GetValue<string>());
 }
 [Fact]public async Task JournalIntegrityFailuresAreNotHiddenAsInterruptedGaps(){
  Write(Frame("checkpoint",0,new {}),Frame("update",1000,new {}));
  var manifest=Path.Combine(Folder,"manifest.json");var m=JsonNode.Parse(File.ReadAllText(manifest))!;m["state"]="interrupted";File.WriteAllText(manifest,m.ToJsonString());
  // Corrupt an index in a complete gzip member, rather than a truncated final member.
  var path=Path.Combine(Folder,"events-000001.ndjson.gz");File.Delete(path);using(var f=File.Create(path)){using var gz=new GZipStream(f,CompressionLevel.Fastest);var row=JsonSerializer.SerializeToNode(Frame("checkpoint",0,new {}))!;row["index"]=4;JsonSerializer.Serialize(gz,row);gz.WriteByte(10);}
  await Assert.ThrowsAsync<InvalidDataException>(()=>Reader().Index(id));
 }
 [Fact]public void RejectsCachePathsThatOverlapOriginalRecordings(){
  Assert.Throws<ArgumentException>(()=>new RecordingReplay(Root,Root));
  Assert.Throws<ArgumentException>(()=>new RecordingReplay(Root,Path.Combine(Root,"cache")));
  Assert.Throws<ArgumentException>(()=>new RecordingReplay(Root,dir));
 }
 public void Dispose(){if(Directory.Exists(dir))Directory.Delete(dir,true);}
}
