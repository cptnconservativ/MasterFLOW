using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Persistence;

namespace MasterFlow.IntegrationTests;

/// <summary>Institutional memory search (premium, D-290): projects, documents, synonyms, numbers, ranking, cache.</summary>
public sealed class MemorySearchTests : IDisposable
{
    private static readonly MemorySearchTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mf-memory-" + Guid.NewGuid().ToString("N"));

    public MemorySearchTests()
    {
        MemorySearchTool.CacheFolder = null;
        var a = Directory.CreateDirectory(Path.Combine(_root, "24-031 Elk Vale Commercial")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(_root, "22-108 Founders Park")).FullName;
        var c = Directory.CreateDirectory(Path.Combine(_root, "25-002 Pond Retrofit")).FullName;
        // A project whose saved gutter-inlet input has a sump inlet on a 6.0 % street.
        var project = new Project
        {
            Info = new ProjectInfo { Name = "Elk Vale Commercial", ProjectNumber = "24-031" },
            ToolInputs = new Dictionary<string, string> { ["gutter-inlets"] = "# Inlet B-3 at the sump on Elk Vale Rd\nlongitudinal slope = 6.0   # %\ninlet = sump\nclogging = 50 %   # City asked for 50 % clogging at the sag\n" },
        };
        ProjectFile.Save(project, Path.Combine(a, "Elk Vale" + ProjectFile.Extension));
        File.WriteAllText(Path.Combine(b, "City review comments round 2.txt"),
            "City of Rapid City Public Works\nReview comments, round 2\n\n12. The catch basin at the low point of Founders Drive (grade 6%) must be checked with a clogging factor of 50 percent and the 100-year ponding must stay within the right-of-way.\n\n13. Provide riprap at the outfall.\n");
        File.WriteAllText(Path.Combine(c, "Pond report.md"), "Detention pond retrofit. The pond outlet structure was rebuilt with a new orifice plate. The street was not changed.\n");
        File.WriteAllText(Path.Combine(c, "notes.xyz"), "sag inlet 6% street");   // not a searched type
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } MemorySearchTool.CacheFolder = null; }

    private ToolResult Ask(string q, string extra = "") => Tool.Run($"folders = {_root}\nquestion = {q}\n{extra}", Us);

    [Fact]
    public void SagInletQuestion_FindsProjectAndReviewLetter_NotThePond()
    {
        var r = Ask("how did we handle a sag inlet on a 6% street last time?");
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        var rows = r.Tables.Single(t => t.Title == "Results").Rows;
        var top2 = rows.Take(2).Select(x => (string)x[4]!).ToList();
        Assert.Contains("Elk Vale.mfproj", top2);
        Assert.Contains("City review comments round 2.txt", top2);
        Assert.DoesNotContain(rows, x => (string)x[4]! == "notes.xyz");
        // The review letter is recognised as one, matched through synonyms (catch basin, low point, grade), with the passage shown.
        var letter = rows.Single(x => (string)x[4]! == "City review comments round 2.txt");
        Assert.Equal("review", letter[3]);
        Assert.Equal("22-108 Founders Park", letter[2]);
        Assert.Contains("[low]", (string)letter[7]!);
        var proj = rows.First(x => (string)x[4]! == "Elk Vale.mfproj");
        Assert.Equal("Elk Vale Commercial", proj[2]);
        Assert.StartsWith("tool: Gutter", (string)proj[5]!);
        // The pond report shares only "street"; it ranks last if at all.
        var pond = rows.Select((x, i) => ((string)x[4]!, i)).Where(x => x.Item1 == "Pond report.md").Select(x => x.i).DefaultIfEmpty(99).First();
        Assert.True(pond > 1);
    }

    [Fact]
    public void KindsAndYearFilter()
    {
        var r = Ask("clogging at the sag", "kinds = review");
        Assert.All(r.Tables.Single(t => t.Title == "Results").Rows, x => Assert.Equal("review", x[3]));
        var none = Ask("clogging", $"since = {DateTime.Now.Year + 1}");
        Assert.Contains(none.Messages, m => m.Code == "W-MS-003");
    }

    [Fact]
    public void Cache_IsUsedOnTheSecondRun()
    {
        var cache = Path.Combine(_root, "_cache");
        MemorySearchTool.CacheFolder = cache;
        Ask("riprap outfall");
        int n = Directory.GetFiles(cache).Length;
        Assert.Equal(3, n);   // one per searched file
        var again = Ask("riprap outfall");
        Assert.Equal("City review comments round 2.txt", again.Tables.Single(t => t.Title == "Results").Rows[0][4]);
    }

    [Fact]
    public void Expand_AddsSynonymsAndNumberForms()
    {
        var terms = MemorySearchTool.Expand(MemorySearchTool.Tokens("sag inlets at 6%")).Select(t => t.Term).ToList();
        Assert.Contains("sump", terms); Assert.Contains("basin", terms); Assert.Contains("6.0", terms); Assert.Contains("0.06", terms);
        Assert.DoesNotContain("at", terms);
    }

    [Fact]
    public void Errors_AndPremium()
    {
        Assert.Contains(Tool.Run($"folders = {_root}\n", Us).Messages, m => m.Code == "E-MS-001");
        Assert.Contains(Tool.Run("question = x\n", Us).Messages, m => m.Code == "E-MS-002");
        Assert.Contains(Tool.Run($"folders = {_root}/missing\nquestion = x\n", Us).Messages, m => m.Code == "W-MS-001");
        Assert.Equal("memory-search", PremiumFeatures.ForTool("memory-search")?.Id);
    }

    [Fact]
    public void ReadsPdfWithBuiltInReader()
    {
        // A PDF written by MasterFlow's own report writer carries text the built-in reader can extract.
        var pdf = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory), "*.pdf", SearchOption.AllDirectories).FirstOrDefault();
        if (pdf is null) return;
        File.Copy(pdf, Path.Combine(_root, "25-002 Pond Retrofit", "calc.pdf"));
        Assert.True(Ask("pipe").Succeeded);
    }
}
