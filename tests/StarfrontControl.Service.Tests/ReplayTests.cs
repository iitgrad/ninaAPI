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
 [Fact]public async Task BackgroundIndexAppearsAfterFinishWithoutOpeningReplay(){
  Write(Frame("checkpoint",0,new {camera=new {value=new {connected=true}}}));
  var path=Path.Combine(Folder,"manifest.json");var meta=JsonNode.Parse(File.ReadAllText(path))!;
  meta["state"]="recording";File.WriteAllText(path,meta.ToJsonString());
  var original=File.ReadAllBytes(Path.Combine(Folder,"events-000001.ndjson.gz"));
  var worker=new ReplayIndexWarmup(Reader(),Microsoft.Extensions.Logging.Abstractions.NullLogger<ReplayIndexWarmup>.Instance);
  await worker.StartAsync(default);
  try {
   await Task.Delay(600);Assert.False(Directory.Exists(Path.Combine(Cache,id)));
   meta["state"]="completed";File.WriteAllText(path+".tmp",meta.ToJsonString());File.Move(path+".tmp",path,true);
   var ready=Path.Combine(Cache,id,"index.json");var deadline=DateTime.UtcNow.AddSeconds(10);
   while(!File.Exists(ready)&&DateTime.UtcNow<deadline)await Task.Delay(50);
   Assert.True(File.Exists(ready));Assert.Equal(original,File.ReadAllBytes(Path.Combine(Folder,"events-000001.ndjson.gz")));
  } finally {await worker.StopAsync(default);worker.Dispose();}
 }
 [Fact]public async Task StartupWarmsOnlyLatestTwoAndLeavesUnchangedCacheAlone(){
  Write(Frame("checkpoint",0,new {}));
  var ids=new[]{id,Guid.NewGuid().ToString(),Guid.NewGuid().ToString()};
  for(int i=1;i<ids.Length;i++) {
   var dest=Path.Combine(Root,ids[i]);Directory.CreateDirectory(dest);
   File.Copy(Path.Combine(Folder,"events-000001.ndjson.gz"),Path.Combine(dest,"events-000001.ndjson.gz"));
   var m=JsonNode.Parse(File.ReadAllText(Path.Combine(Folder,"manifest.json")))!;m["id"]=ids[i];m["finishedAt"]=Stamp(300000+i*60000);File.WriteAllText(Path.Combine(dest,"manifest.json"),m.ToJsonString());
  }
  var worker=new ReplayIndexWarmup(Reader(),Microsoft.Extensions.Logging.Abstractions.NullLogger<ReplayIndexWarmup>.Instance);
  await worker.Refresh();Assert.False(Directory.Exists(Path.Combine(Cache,id)));
  Assert.Equal(2,Directory.GetDirectories(Cache).Length);
  var ready=Path.Combine(Cache,ids[2],"index.json");var before=File.GetLastWriteTimeUtc(ready);
  await worker.Refresh();Assert.Equal(before,File.GetLastWriteTimeUtc(ready));worker.Dispose();
 }
 [Fact]public async Task BadRecordingDoesNotPreventGoodRecordingFromBeingPrepared(){
  Write(Frame("checkpoint",0,new {}));
  var bad=Guid.NewGuid().ToString();var dest=Path.Combine(Root,bad);Directory.CreateDirectory(dest);
  var m=JsonNode.Parse(File.ReadAllText(Path.Combine(Folder,"manifest.json")))!;m["id"]=bad;m["finishedAt"]=Stamp(360000);File.WriteAllText(Path.Combine(dest,"manifest.json"),m.ToJsonString());
  File.WriteAllText(Path.Combine(dest,"events-000001.ndjson.gz"),"not gzip");
  var worker=new ReplayIndexWarmup(Reader(),Microsoft.Extensions.Logging.Abstractions.NullLogger<ReplayIndexWarmup>.Instance);
  await worker.Refresh();Assert.True(File.Exists(Path.Combine(Cache,id,"index.json")));Assert.False(Directory.Exists(Path.Combine(Cache,bad)));worker.Dispose();
 }
 [Fact]public async Task ThermalHistoryIsAvailableOnDirectSeekAndNeverIncludesFutureSamples(){
  object Camera(double temperature,double power,bool connected=true)=>new {camera=new {value=new {connected,name="Camera",temperature,coolerPower=power}}};
  Write(Frame("checkpoint",0,Camera(20,0)),Frame("update",10000,Camera(10,70)),Frame("update",70000,Camera(-5,90)),new {kind="gap-start",receivedAt=Stamp(80000),data=new {}},new {kind="gap-end",receivedAt=Stamp(100000),data=new {}},Frame("checkpoint",100000,Camera(-10,40)));
  var reader=Reader();await reader.Index(id); // Checkpoint seed must retain earlier camera samples.
  var seek=await At(reader,75000);var samples=seek["cameraThermal"]!.AsArray();
  Assert.Equal(3,samples.Count);Assert.Equal(20,samples[0]!["temperature"]!.GetValue<double>());
  Assert.Equal(-5,samples[2]!["temperature"]!.GetValue<double>());
  Assert.Equal(2,(await At(reader,15000))["cameraThermal"]!.AsArray().Count);
  Assert.Null((await At(reader,85000))["cameraThermal"]!.AsArray().Last()!["temperature"]);
  Assert.Equal(-10,(await At(reader,110000))["cameraThermal"]!.AsArray().Last()!["temperature"]!.GetValue<double>());
 }
 [Fact]public async Task ThermalHistoryIsBoundedAndCameraSessionChangeResetsIt(){
  var state=new RecordingReplay.ReplayState();
  for(int i=0;i<1000;i++) {
   var row=JsonSerializer.SerializeToNode(Frame("update",i*5000,new {camera=new {value=new {connected=true,name="Camera",temperature=10,coolerPower=30}}}))!.AsObject();state.Apply(row);
  }
  Assert.InRange(state.CameraThermal.Count,1,720);
  var next=JsonSerializer.SerializeToNode(Frame("checkpoint",5000000,new {camera=new {value=new {connected=true,name="Camera",temperature=5,coolerPower=40}}}))!.AsObject();next["data"]!["sessionId"]="new-session";state.Apply(next);Assert.Single(state.CameraThermal);
 }
 [Fact]public async Task AutofocusImagesReplayInOrderWithoutAnImageSavedEvent(){
  var a=new string('a',64);var b=new string('b',64);
  object Focus(string file,long revision)=>new {autofocus=new {value=new {running=true,points=new[]{new {position=35000,hfr=3.0}}}},preview=new {value=new {revision,recordingFile="previews/"+file+".jpg"}}};
  Write(Frame("checkpoint",0,Focus(a,1)),Frame("update",10000,Focus(b,2)));
  var folder=Path.Combine(Folder,"previews");Directory.CreateDirectory(folder);
  byte[] first=[255,216,1,255,217],second=[255,216,2,255,217];File.WriteAllBytes(Path.Combine(folder,a+".jpg"),first);File.WriteAllBytes(Path.Combine(folder,b+".jpg"),second);
  var reader=Reader();
  foreach(var (offset,bytes) in new[]{(0L,first),(5000L,first),(10000L,second),(15000L,second),(0L,first)}) {
   var frame=await At(reader,offset);var states=frame["snapshot"]!["states"]!;
   Assert.True(states["autofocus"]!["value"]!["running"]!.GetValue<bool>());
   Assert.Equal(Convert.ToBase64String(bytes),states["preview"]!["value"]!["jpeg"]!.GetValue<string>());
   Assert.Null(states["image"]);
  }
 }
 public void Dispose(){if(Directory.Exists(dir))Directory.Delete(dir,true);}
}
