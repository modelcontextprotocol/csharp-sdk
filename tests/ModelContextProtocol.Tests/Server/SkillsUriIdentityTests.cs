using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public class SkillsUriIdentityTests : ClientServerTestBase
{
    private const string Uri = "skill://catalog/alpha/SKILL.md";
    private readonly McpServerSkill _skill;

    public SkillsUriIdentityTests(ITestOutputHelper output) : base(output, startServer: false)
    {
        var package = McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\n")], new McpServerSkillPackageCreateOptions { Uri = Uri });
        _skill = package.Skill;
        McpServerBuilder.WithSkillPackages([package]);
        StartServer();
    }

    [Theory]
    [InlineData("SKILL://CATALOG/alpha/SKILL.md")]
    [InlineData("skill://catalog/%61lpha/SKILL.md")]
    public async Task EquivalentLookupAndVerifiedReadPreserveAuthoredText(string alias)
    {
        await using var client = await CreateMcpClientForServer();
        var entry = await client.GetSkillAsync(alias, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Uri, entry.Uri);
        Assert.Equal(Uri, Assert.Single(entry.Resources.Entries!).Uri);
        var read = await client.ReadSkillResourceAsync(entry, alias, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Uri, Assert.Single(read.Contents).Uri);
    }

    [Theory]
    [InlineData("skill://catalog/Alpha/SKILL.md")]
    [InlineData("skill://other/alpha/SKILL.md")]
    [InlineData("skill://catalog/alpha/SKILL.md#fragment")]
    [InlineData("skill://catalog/alpha/SKILL.md?query")]
    [InlineData("skill://catalog/other/../alpha/SKILL.md")]
    public async Task DifferentOrUnsupportedIdentityIsNotAccepted(string uri)
    {
        await using var client = await CreateMcpClientForServer();
        await Assert.ThrowsAsync<McpProtocolException>(async () => await client.GetSkillAsync(uri, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await client.ReadSkillResourceAsync(_skill.ProtocolSkill!, uri, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EncodedBracePackageCanBeRead_ButRawBracesAreNotAnAlias()
    {
        const string Encoded = "skill://catalog/%7Btenant%7D/alpha/SKILL.md";
        const string Raw = "skill://catalog/{tenant}/alpha/SKILL.md";
        var package = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\n")],
            new() { Uri = Encoded });
        ServiceProvider.GetRequiredService<IOptions<McpSkillsOptions>>().Value.SkillCollection.Add(package.Skill);
        foreach (var resource in package.Resources)
        {
            Server.ServerOptions.ResourceCollection!.Add(resource);
        }

        await using var client = await CreateMcpClientForServer();
        var entry = await client.GetSkillAsync(Encoded, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Encoded, entry.Uri);
        var read = await client.ReadSkillResourceAsync(entry, Encoded, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Encoded, Assert.Single(read.Contents).Uri);

        await Assert.ThrowsAsync<McpProtocolException>(
            async () => await client.GetSkillAsync(Raw, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await client.ReadSkillResourceAsync(entry, Raw, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void EquivalentManifestEntriesAreAmbiguous_AndAliasContentStillRequiresDigestAndSize()
    {
        var entry = _skill.ProtocolSkill!;
        var expected = entry.Resources.Entries![0];
        var alias = new SkillResource { Uri = "SKILL://CATALOG/alpha/SKILL.md", Digest = expected.Digest, Size = expected.Size };
        var contents = new TextResourceContents
        {
            Uri = alias.Uri, Text = "---\nname: alpha\ndescription: Alpha\n---\n",
        };

        SkillVerifier.Verify(expected, contents);
        contents.Text += "changed";
        Assert.Throws<InvalidDataException>(() => SkillVerifier.Verify(expected, contents));
        entry.Resources = SkillResources.FromResources([expected, alias]);
        Assert.Throws<InvalidDataException>(() => SkillVerifier.Verify(entry, expected.Uri, new ReadResourceResult { Contents = [contents] }));
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(entry));
    }
}
