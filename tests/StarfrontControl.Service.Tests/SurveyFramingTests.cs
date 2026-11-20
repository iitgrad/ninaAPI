using StarfrontControl.Service;
using Xunit;
namespace StarfrontControl.Service.Tests;
public class SurveyFramingTests {
    [Theory][InlineData(double.NaN,0,4,3,0)][InlineData(360,0,4,3,0)][InlineData(1,91,4,3,0)][InlineData(1,0,0,3,0)][InlineData(1,0,4,30,0)]
    public void InvalidFieldIsRejected(double ra,double dec,double w,double h,double rotation){var field=new SurveyField(ra,dec,w,h,rotation);Assert.False(field.Valid);Assert.Throws<ArgumentException>(()=>field.Wcs());}
    [Fact]public void CameraCenterAspectAndPositionAngleAreEncoded(){
        var wcs=new SurveyField(324.74,57.489,4.6,3.07,0).Wcs();Assert.Equal(512,wcs["NAXIS1"]);Assert.Equal(342,wcs["NAXIS2"]);Assert.Equal(324.74,wcs["CRVAL1"]);Assert.True((double)wcs["CD1_1"]<0);Assert.Equal(0d,wcs["CD1_2"]);Assert.True((double)wcs["CD2_2"]>0);
        var rotated=new SurveyField(324.74,57.489,4.6,3.07,90).Wcs();Assert.True((double)rotated["CD1_2"]>0);Assert.True(Math.Abs((double)rotated["CD2_2"])<1e-10);Assert.True((double)rotated["CD2_1"]>0);
    }
}

public sealed class SurveyFramingFetchTests {
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> run) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => run(request,token);
    }
    private static byte[] Jpeg(int w,int h) => [255,216,255,192,0,8,8,(byte)(h>>8),(byte)h,(byte)(w>>8),(byte)w,0,255,217];
    private static HttpResponseMessage Reply(byte[] bytes) => new(System.Net.HttpStatusCode.OK) {Content=new ByteArrayContent(bytes)};
    private static byte[] Fits() {
        var values=new Dictionary<string,string>{{"CTYPE1","'RA---TAN'"},{"CTYPE2","'DEC--TAN'"},{"NAXIS1","768"},{"NAXIS2","768"},{"CRPIX1","384.5"},{"CRPIX2","384.5"},{"CRVAL1","313.2"},{"CRVAL2","29.75"},{"CDELT1","-0.01"},{"CDELT2","0.01"}};
        return System.Text.Encoding.ASCII.GetBytes(string.Concat(values.Select(p=>(p.Key.PadRight(8)+"= "+p.Value).PadRight(80)))+"END".PadRight(80));
    }
    [Fact] public async Task FallbackIsSelfContainedAndPersistsAcrossInstances() {
        var directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        var field=new SurveyField(313.2,29.75,4.6,3.07,37);var calls=0;
        try {
            using var survey=new SurveyFraming(new Handler((r,_)=>{
                calls++; var uri=r.RequestUri!;
                if(uri.Host=="alasky.cds.unistra.fr")throw new HttpRequestException("CDS offline");
                if(uri.AbsolutePath.EndsWith("runquery.pl"))return Task.FromResult(Reply("fixture"u8.ToArray()));
                return Task.FromResult(Reply(uri.AbsolutePath.EndsWith(".fits")?Fits():Jpeg(768,768)));
            }),directory);
            var result=await survey.Image(field,CancellationToken.None);Assert.NotNull(result);Assert.Equal(4,calls);
            var svg=System.Text.Encoding.UTF8.GetString(result!);Assert.Contains("NASA SkyView",svg);Assert.Contains("data:image/jpeg;base64,",svg);Assert.DoesNotContain("href=\"https:",svg);
            Assert.Equal(result,await survey.Image(field,CancellationToken.None));Assert.Equal(4,calls);
            using var reopened=new SurveyFraming(new Handler((_,_)=>throw new Exception("Disk cache must avoid network")),directory);
            Assert.Equal(result,await reopened.Image(field,CancellationToken.None));
            var files=Directory.GetFiles(directory);Assert.Single(files);
            await File.WriteAllTextAsync(files[0],"corrupt");
            using var recovered=new SurveyFraming(new Handler((_,_)=>Task.FromResult(Reply(Jpeg(512,342)))),directory);
            Assert.Contains("DSS2 / CDS",System.Text.Encoding.UTF8.GetString((await recovered.Image(field,CancellationToken.None))!));
        } finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [Fact] public async Task TotalDeadlineAndCancellationEndTheRequest() {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        using var survey=new SurveyFraming(new Handler(async(_,token)=>{await Task.Delay(TimeSpan.FromSeconds(5),token);return Reply([]);}),dir,TimeSpan.FromMilliseconds(40));
        var start=System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>survey.Image(new(313.2,29.75,4.6,3.07,0),CancellationToken.None));
        Assert.True(start.Elapsed<TimeSpan.FromSeconds(1));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>survey.Image(new(313.2,29.75,4.6,3.07,0),cancelled.Token));
    }
    [Fact] public async Task ProviderFailurePropagatesAndDoesNotPoisonRetry() {
        var fail=true;var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try {
            using var survey=new SurveyFraming(new Handler((_,_)=>fail?throw new HttpRequestException("Offline"):Task.FromResult(Reply(Jpeg(512,342)))),dir);
            await Assert.ThrowsAsync<HttpRequestException>(()=>survey.Image(new(313.2,29.75,4.6,3.07,0),CancellationToken.None));
            fail=false;Assert.NotNull(await survey.Image(new(313.2,29.75,4.6,3.07,0),CancellationToken.None));
        }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    [Theory][InlineData(0)][InlineData(37)][InlineData(90)][InlineData(270)]
    public void RotatedSvgPixelsMatchCameraWcs(double rotation) {
        var target=new SurveyField(313.2,29.75,4.6,3.07,rotation).Wcs();var source=SurveyFramingRenderer.Header(Fits());
        var svg=System.Xml.Linq.XElement.Parse(System.Text.Encoding.UTF8.GetString(SurveyFramingRenderer.Wrap(Jpeg(768,768),target,source)));
        var transform=svg.Elements().Single(e=>e.Name.LocalName=="image").Attribute("transform")!.Value;
        var m=transform[7..^1].Split(' ').Select(x=>double.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        double T(string k)=>Convert.ToDouble(target[k]);
        foreach(var (x,y) in new[]{(200d,300d),(500d,200d),(384d,384d)}) {
            var dx=m[0]*x+m[2]*y+m[4]+.5-T("CRPIX1");
            var dy=T("NAXIS2")+.5-(m[1]*x+m[3]*y+m[5])-T("CRPIX2");
            Assert.Equal(source["CD1_1"]*(x+.5-source["CRPIX1"]),T("CD1_1")*dx+T("CD1_2")*dy,10);
            Assert.Equal(source["CD2_2"]*(768.5-y-source["CRPIX2"]),T("CD2_1")*dx+T("CD2_2")*dy,10);
        }
        var wrong=Fits();var text=System.Text.Encoding.ASCII.GetString(wrong).Replace("313.2","314.2");
        Assert.Throws<InvalidDataException>(()=>SurveyFramingRenderer.Wrap(Jpeg(768,768),target,SurveyFramingRenderer.Header(System.Text.Encoding.ASCII.GetBytes(text))));
    }
    [Fact] public async Task LiveSurveyWhenExplicitlyRequested() {
        if(Environment.GetEnvironmentVariable("STARFRONT_LIVE_FRAMING")!="1")return;
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
        using var survey=new SurveyFraming(new Handler(async(request,token)=>{
            var clock=System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("GET "+request.RequestUri);
            try {var response=await client.GetAsync(request.RequestUri,HttpCompletionOption.ResponseHeadersRead,token);Console.WriteLine($"{response.StatusCode} after {clock.Elapsed.TotalSeconds:F1}s");return response;}
            catch(Exception e){Console.WriteLine($"{e.Message} after {clock.Elapsed.TotalSeconds:F1}s");throw;}
        }),Path.Combine(Path.GetTempPath(),"starfront-live-framing-"+Guid.NewGuid().ToString("N")));
        var result=await survey.Image(new(313.2,29.75,4.6,3.07,37),CancellationToken.None);
        Assert.NotNull(result);await File.WriteAllBytesAsync("/tmp/starfront-web-framing-live.svg",result!);
    }
    [Fact]public void MalformedJpegAndFitsAreRejected(){Assert.Throws<InvalidDataException>(()=>SurveyFramingRenderer.CheckJpeg([255,216,0,0],512,342));Assert.Throws<InvalidDataException>(()=>SurveyFramingRenderer.Header([]));}
}
