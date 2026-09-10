using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Tests for <see cref="SkillVerifier"/>: digest formatting and content verification against a manifest.
/// </summary>
public class SkillVerifierTests
{
    private const string Uri = "skill://alpha/SKILL.md";

    private static SkillResource Entry(byte[] content) => new()
    {
        Uri = Uri,
        Digest = SkillVerifier.ComputeDigest(content),
        Size = content.Length,
    };

    [Theory]
    [InlineData("", "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void ComputeDigest_MatchesKnownVectors(string input, string expected)
    {
        Assert.Equal(expected, SkillVerifier.ComputeDigest(Encoding.UTF8.GetBytes(input)));
    }

    [Fact]
    public void Verify_AcceptsMatchingBytes()
    {
        byte[] content = Encoding.UTF8.GetBytes("# Hello\n");

        SkillVerifier.Verify(Entry(content), content);
    }

    [Fact]
    public void Verify_RejectsSizeMismatch()
    {
        byte[] content = Encoding.UTF8.GetBytes("# Hello\n");
        var entry = Entry(content);
        entry.Size += 1;

        var exception = Assert.Throws<SkillVerificationException>(() => SkillVerifier.Verify(entry, content));
        Assert.Contains("bytes", exception.Message);
    }

    [Fact]
    public void Verify_RejectsDigestMismatch()
    {
        var entry = Entry(Encoding.UTF8.GetBytes("# Hello\n"));

        var exception = Assert.Throws<SkillVerificationException>(() =>
            SkillVerifier.Verify(entry, Encoding.UTF8.GetBytes("# Hellp\n")));
        Assert.Contains("digest", exception.Message);
    }

    [Fact]
    public void Verify_AcceptsUppercaseDigestFromServer()
    {
        byte[] content = Encoding.UTF8.GetBytes("x");
        var entry = Entry(content);
        entry.Digest = entry.Digest.ToUpperInvariant().Replace("SHA256", "sha256");

        SkillVerifier.Verify(entry, content);
    }

    [Fact]
    public void Verify_TextContents_HashesUtf8Encoding()
    {
        const string Text = "# Héllo\n";
        var entry = Entry(Encoding.UTF8.GetBytes(Text));

        SkillVerifier.Verify(entry, new TextResourceContents { Uri = Uri, Text = Text });
    }

    [Fact]
    public void Verify_Contents_RejectsMissingTextAsVerificationFailure()
    {
        var entry = Entry(Encoding.UTF8.GetBytes("x"));

        Assert.Throws<SkillVerificationException>(() => SkillVerifier.Verify(entry, new TextResourceContents { Uri = Uri, Text = null! }));
    }

    [Fact]
    public void Verify_BlobContents_HashesDecodedBytes()
    {
        byte[] content = [0x00, 0xFF, 0x10, 0x80];
        var entry = Entry(content);

        SkillVerifier.Verify(entry, BlobResourceContents.FromBytes(content, Uri));
    }

    [Fact]
    public void Verify_Contents_RejectsUriMismatch()
    {
        var entry = Entry(Encoding.UTF8.GetBytes("x"));

        Assert.Throws<SkillVerificationException>(() =>
            SkillVerifier.Verify(entry, new TextResourceContents { Uri = "skill://alpha/other.md", Text = "x" }));
    }

    [Fact]
    public void Verify_Skill_ChecksEveryContentAgainstManifest()
    {
        byte[] content = Encoding.UTF8.GetBytes("x");
        var skill = CreateSkill(content);

        SkillVerifier.Verify(skill, new ReadResourceResult { Contents = [new TextResourceContents { Uri = Uri, Text = "x" }] });

        Assert.Throws<SkillVerificationException>(() => SkillVerifier.Verify(skill,
            new ReadResourceResult { Contents = [new TextResourceContents { Uri = Uri, Text = "y" }] }));
    }

    [Fact]
    public void Verify_Skill_RejectsUnlistedFile()
    {
        var skill = CreateSkill(Encoding.UTF8.GetBytes("x"));

        var exception = Assert.Throws<SkillVerificationException>(() => SkillVerifier.Verify(skill,
            new ReadResourceResult { Contents = [new TextResourceContents { Uri = "skill://alpha/new.md", Text = "x" }] }));
        Assert.Contains("not listed", exception.Message);
    }

    [Fact]
    public void Verify_Skill_RejectsEmptyResult()
    {
        var skill = CreateSkill(Encoding.UTF8.GetBytes("x"));

        Assert.Throws<SkillVerificationException>(() => SkillVerifier.Verify(skill, new ReadResourceResult()));
    }

    [Fact]
    public void Verify_Skill_RejectsMissingManifestWithoutCrashing()
    {
        var skill = CreateSkill(Encoding.UTF8.GetBytes("x"));
        skill.Resources = null!;

        Assert.Throws<ArgumentException>(() => SkillVerifier.Verify(skill,
            new ReadResourceResult { Contents = [new TextResourceContents { Uri = Uri, Text = "x" }] }));
        Assert.Throws<ArgumentException>(() => SkillVerifier.Verify(skill, Uri,
            new ReadResourceResult { Contents = [new TextResourceContents { Uri = Uri, Text = "x" }] }));
    }

    [Fact]
    public void Verify_Skill_ThrowsForDynamicSkill()
    {
        var skill = CreateSkill(Encoding.UTF8.GetBytes("x"));
        skill.Resources = SkillResources.Dynamic;

        Assert.Throws<InvalidOperationException>(() => SkillVerifier.Verify(skill,
            new ReadResourceResult { Contents = [new TextResourceContents { Uri = Uri, Text = "x" }] }));
    }

    [Fact]
    public void Verify_SkillAndUri_RequiresTheRequestedFileInTheResult()
    {
        byte[] skillFile = Encoding.UTF8.GetBytes("x");
        byte[] other = Encoding.UTF8.GetBytes("other");
        var skill = CreateSkill(skillFile);
        skill.Resources = SkillResources.FromResources(
        [
            Entry(skillFile),
            new SkillResource { Uri = "skill://alpha/other.md", Digest = SkillVerifier.ComputeDigest(other), Size = other.Length },
        ]);

        // A correctly digested but different file of the same skill must not satisfy a read of SKILL.md.
        var substituted = new ReadResourceResult { Contents = [new TextResourceContents { Uri = "skill://alpha/other.md", Text = "other" }] };
        SkillVerifier.Verify(skill, substituted);
        var exception = Assert.Throws<SkillVerificationException>(() => SkillVerifier.Verify(skill, Uri, substituted));
        Assert.Contains("returned no contents for that URI", exception.Message);

        SkillVerifier.Verify(skill, Uri, new ReadResourceResult { Contents = [new TextResourceContents { Uri = Uri, Text = "x" }] });

        Assert.Throws<SkillVerificationException>(() =>
            SkillVerifier.Verify(skill, "skill://alpha/unlisted.md", new ReadResourceResult { Contents = [new TextResourceContents { Uri = Uri, Text = "x" }] }));
    }

    private static Skill CreateSkill(byte[] skillFileContent) => new()
    {
        Uri = Uri,
        Frontmatter = new JsonObject { ["name"] = "alpha", ["description"] = "d" },
        Resources = SkillResources.FromResources([Entry(skillFileContent)]),
    };
}
