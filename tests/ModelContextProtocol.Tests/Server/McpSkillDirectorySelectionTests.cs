using ModelContextProtocol.Extensions.Skills;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests.Server;

public class McpSkillDirectorySelectionTests : IDisposable
{
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

    [Fact]
    public void DirectoryAuthoringExcludesDotEntriesByDefault()
    {
        using var held = new FileStream(Path.Combine(_root, ".git", "config"), FileMode.Open, FileAccess.Read, FileShare.None);

        var skill = Create(includeHidden: false);

        Assert.Equal(2, skill.Resources.Count);
        Assert.DoesNotContain(skill.Skill.ProtocolSkill!.Resources.Entries!, resource => resource.Uri.Contains("/."));
        Assert.Equal(
            skill.Skill.ProtocolSkill!.Resources.Entries!.Select(resource => resource.Uri),
            skill.Resources.Select(resource => resource.ProtocolResource!.Uri));
    }

    [Fact]
    public void HiddenEntriesCanBeIncludedExplicitly()
    {
        var skill = Create(includeHidden: true);

        Assert.Equal(4, skill.Resources.Count);
        Assert.Contains(skill.Skill.ProtocolSkill!.Resources.Entries!, resource => resource.Uri.EndsWith("/.env", StringComparison.Ordinal));
        Assert.Contains(skill.Skill.ProtocolSkill!.Resources.Entries!, resource => resource.Uri.EndsWith("/.git/config", StringComparison.Ordinal));
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

        Assert.DoesNotContain(Create(false).Resources, resource => resource.ProtocolResource!.Uri.EndsWith("/marked.txt", StringComparison.Ordinal));
        Assert.Contains(Create(true).Resources, resource => resource.ProtocolResource!.Uri.EndsWith("/marked.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void ExcludedFilesAreNotReadEvenWhenTheyAreLargeAndLocked()
    {
        using var file = new FileStream(Path.Combine(_root, ".large-cache"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        file.SetLength(16 * 1024 * 1024 + 1);

        Assert.Equal(2, Create(false).Resources.Count);
        Assert.Throws<IOException>(() => Create(true));
    }

    [Fact]
    public void ExplicitFileAuthoringRetainsTheCallersFileSelection()
    {
        var skill = McpServerSkillPackage.Create(
        [
            McpServerSkillFile.FromText("SKILL.md", Markdown),
            McpServerSkillFile.FromText(".env", "synthetic-explicit-value"),
        ]);

        Assert.Equal(2, skill.Resources.Count);
        Assert.Contains(skill.Skill.ProtocolSkill!.Resources.Entries!, resource => resource.Uri.EndsWith("/.env", StringComparison.Ordinal));
    }

    private McpServerSkillPackage Create(bool includeHidden) =>
        McpServerSkillPackage.CreateFromDirectory(_root, new McpServerSkillPackageCreateOptions { IncludeHiddenAndSystemFiles = includeHidden });

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
