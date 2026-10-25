using StarfrontControl.Core;
using Xunit;

public sealed class SequenceLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "starfront-library-" + Guid.NewGuid());
    private string Targets => Path.Combine(root, "targets");
    public SequenceLibraryTests() { Directory.CreateDirectory(Targets); File.WriteAllText(Path.Combine(root, "Night.json"), "{}"); File.WriteAllText(Path.Combine(Targets, "M31.json"), "{}"); }
    public void Dispose() => Directory.Delete(root, true);
    [Fact] public void LibraryKeepsRootsDistinctAndResolvesFiles()
    {
        Assert.Equal(new[] { "Night.json", "Targets/M31.json" }, SequenceLibrary.List(root, Targets).Files);
        Assert.Equal(Path.Combine(Targets,"M31.json"),SequenceLibrary.Resolve(root,Targets,"Targets/M31.json"));
        Assert.Equal(Path.Combine(root,"Night.json"),SequenceLibrary.Resolve(root,Targets,"Night.json"));
    }
    [Theory] [InlineData("../Night.json")] [InlineData("Targets/../Night.json")] [InlineData("/Night.json")] [InlineData("C:\\Night.json")] [InlineData("Targets/a/b.json")] [InlineData("Night.txt")] [InlineData("missing.json")]
    public void PathsCannotEscapeConfiguredFolders(string name) => Assert.Throws<ArgumentException>(()=>SequenceLibrary.Resolve(root,Targets,name));
    [Fact] public void LinkedFilesAreNotCataloguedOrLoaded()
    {
        File.CreateSymbolicLink(Path.Combine(Targets,"Link.json"),Path.Combine(root,"Night.json"));
        Assert.DoesNotContain("Targets/Link.json",SequenceLibrary.List(root,Targets).Files);
        Assert.Throws<ArgumentException>(()=>SequenceLibrary.Resolve(root,Targets,"Targets/Link.json"));
    }
    [Fact] public void CatalogBudgetIsExplicit()
    {
        for(var i=0;i<140;i++)File.WriteAllText(Path.Combine(Targets,$"{i}.json"),"{}");
        var listing=SequenceLibrary.List(root,Targets);Assert.Equal(128,listing.Files.Length);Assert.True(listing.Truncated);
    }

    [Fact] public void TargetSavesAreAtomicAndReplacementIsExplicit() {
        var path=SequenceLibrary.SaveTarget(Targets,"New.json","{\"Name\":\"new\"}",false);
        Assert.Equal("{\"Name\":\"new\"}",File.ReadAllText(path));
        Assert.Throws<CommandConflictException>(()=>SequenceLibrary.SaveTarget(Targets,"New.json","replacement",false));
        Assert.Equal("{\"Name\":\"new\"}",File.ReadAllText(path));
        SequenceLibrary.SaveTarget(Targets,"New.json","replacement",true);
        Assert.Equal("replacement",File.ReadAllText(path));Assert.Empty(Directory.GetFiles(Targets,"*.tmp"));
    }
    [Theory] [InlineData("../escape.json")] [InlineData("Targets/a.json")] [InlineData("CON.json")] [InlineData("x:stream.json")] [InlineData(".json")] [InlineData("bad?.json")]
    public void SaveNamesCannotEscapeOrUseWindowsSpecialNames(string name) {
        Assert.Throws<ArgumentException>(()=>SequenceLibrary.SaveTarget(Targets,name,"{}",false));
    }
    [Fact] public void SaveNeverReplacesALink() {
        File.CreateSymbolicLink(Path.Combine(Targets,"link.json"),Path.Combine(root,"Night.json"));
        Assert.Throws<ArgumentException>(()=>SequenceLibrary.SaveTarget(Targets,"link.json","replacement",true));
        Assert.Equal("{}",File.ReadAllText(Path.Combine(root,"Night.json")));
    }
}
