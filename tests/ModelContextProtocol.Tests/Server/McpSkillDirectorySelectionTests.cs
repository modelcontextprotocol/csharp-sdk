using ModelContextProtocol.Extensions.Skills;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class McpSkillDirectorySelectionTests : IDisposable
{
    private const string SkillUri = "skill://selected/SKILL.md";
    private const string Markdown = "---\nname: selected\ndescription: d\n---\n";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-selection-" + Guid.NewGuid().ToString("N"));

    public McpSkillDirectorySelectionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, "SKILL.md"), Markdown);
        File.WriteAllText(Path.Combine(_root, "README.md"), "visible");
        File.WriteAllText(Path.Combine(_root, ".env"), "synthetic-private-value");
        File.WriteAllText(Path.Combine(_root, ".git", "config"), "synthetic-config");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExistingDirectoryOverloadsExcludeDotEntries(int overload)
    {
        using var held = new FileStream(Path.Combine(_root, ".git", "config"), FileMode.Open, FileAccess.Read, FileShare.None);

        var skill = Create(overload, includeHidden: false);

        Assert.Equal(2, skill.Resources.Count);
        Assert.DoesNotContain(skill.ProtocolSkill.Resources.Resources!, resource => resource.Uri.Contains("/."));
        Assert.Equal(
            skill.ProtocolSkill.Resources.Resources!.Select(resource => resource.Uri),
            skill.Resources.Select(resource => resource.ProtocolResource!.Uri));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HiddenEntriesCanBeIncludedExplicitly(int overload)
    {
        var skill = Create(overload, includeHidden: true);

        Assert.Equal(4, skill.Resources.Count);
        Assert.Contains(skill.ProtocolSkill.Resources.Resources!, resource => resource.Uri.EndsWith("/.env", StringComparison.Ordinal));
        Assert.Contains(skill.ProtocolSkill.Resources.Resources!, resource => resource.Uri.EndsWith("/.git/config", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    public void AttributeMarkedFilesAreExcludedUnlessExplicitlyIncluded(FileAttributes attribute)
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows file attribute semantics.");
        var path = Path.Combine(_root, "marked.txt");
        File.WriteAllText(path, "synthetic");
        File.SetAttributes(path, File.GetAttributes(path) | attribute);

        Assert.DoesNotContain(Create(0, false).Resources, resource => resource.ProtocolResource!.Uri.EndsWith("/marked.txt", StringComparison.Ordinal));
        Assert.Contains(Create(0, true).Resources, resource => resource.ProtocolResource!.Uri.EndsWith("/marked.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void ExcludedFilesAreNotReadOrCountedAgainstThePublishedSizeLimit()
    {
        using (var file = new FileStream(Path.Combine(_root, ".large-cache"), FileMode.Create))
        {
            file.SetLength(SkillsProtocol.MaxTotalSizeBytes + 1);
        }

        Assert.Equal(2, Create(0, false).Resources.Count);
        Assert.Throws<ArgumentException>(() => Create(0, true));
    }

    [Fact]
    public void ExplicitFileAuthoringRetainsTheCallersFileSelection()
    {
        var skill = McpServerSkill.Create(
        [
            McpServerSkillFile.FromText("SKILL.md", Markdown),
            McpServerSkillFile.FromText(".env", "synthetic-explicit-value"),
        ]);

        Assert.Equal(2, skill.Resources.Count);
        Assert.Contains(skill.ProtocolSkill.Resources.Resources!, resource => resource.Uri.EndsWith("/.env", StringComparison.Ordinal));
    }

    private McpServerSkill Create(int overload, bool includeHidden)
    {
        var metadata = new JsonObject { ["name"] = "selected", ["description"] = "d" };
        return (overload, includeHidden) switch
        {
            (0, false) => McpServerSkill.CreateFromDirectory(_root),
            (1, false) => McpServerSkill.CreateFromDirectory(SkillUri, _root),
            (2, false) => McpServerSkill.CreateFromDirectory(SkillUri, metadata, _root),
            (0, true) => McpServerSkill.CreateFromDirectory(_root, includeHiddenAndSystemFiles: true),
            (1, true) => McpServerSkill.CreateFromDirectory(SkillUri, _root, includeHiddenAndSystemFiles: true),
            (2, true) => McpServerSkill.CreateFromDirectory(SkillUri, metadata, _root, includeHiddenAndSystemFiles: true),
            _ => throw new ArgumentOutOfRangeException(nameof(overload)),
        };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
