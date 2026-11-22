using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public sealed class McpServerSkillsRegistrationTests : IDisposable
{
    private const string GitWorkflowUri = "skill://git-workflow/SKILL.md";
    private const string GitWorkflowMarkdown = "---\nname: git-workflow\ndescription: Git conventions\n---\n# Git\n";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-skills-registration-" + Guid.NewGuid().ToString("N"));

    public McpServerSkillsRegistrationTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "git-workflow"));
        File.WriteAllText(Path.Combine(_root, "git-workflow", "SKILL.md"), GitWorkflowMarkdown);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("bad scheme://git-workflow/SKILL.md")]
    [InlineData("skill://other/SKILL.md")]
    [InlineData("skill://git-workflow/missing.md")]
    public void ExplicitUriErrorsNameOptionsAcrossAuthoringEntrypoints(string uri)
    {
        var options = new McpServerSkillPackageCreateOptions { Uri = uri };
        Assert.Throws<ArgumentException>("options", () => McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", GitWorkflowMarkdown)], options));
        string directory = Path.Combine(_root, "git-workflow");
        Assert.Throws<ArgumentException>("options", () => McpServerSkillPackage.CreateFromDirectory(directory, options));
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        var before = services.ToArray();

        Assert.Throws<ArgumentException>("options", () => builder.WithSkillFromDirectory(directory, options));

        Assert.Equal(before, services);
    }

    [Theory]
    [InlineData("bad scheme://")]
    [InlineData("skill://catalog?query/")]
    [InlineData("skill://catalog#fragment/")]
    public void InvalidDiscoveryPrefixesNameOptionsBeforeRegisteringServices(string prefix)
    {
        var options = new McpServerSkillPackageDiscoveryOptions { UriPrefix = prefix };
        Assert.Throws<ArgumentException>("options", () => McpServerSkillPackage.CreateFromSubdirectories(_root, options));
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        var before = services.ToArray();

        Assert.Throws<ArgumentException>("options", () => builder.WithSkillsFromSubdirectories(_root, options));

        Assert.Equal(before, services);
    }

    [Theory]
    [InlineData("# no frontmatter\n")]
    [InlineData("---\nname: git-workflow\n---\n")]
    [InlineData("---\nname: 42\ndescription: Git conventions\n---\n")]
    public void InvalidFileFrontmatterKeepsFileAttributionWithMetadataOrAValidUriOption(string markdown)
    {
        string directory = Path.Combine(_root, "git-workflow");
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), markdown);
        McpServerSkillPackageCreateOptions[] optionSets =
        [
            new() { Metadata = ["application-tag"] },
            new() { Uri = GitWorkflowUri },
        ];
        foreach (var options in optionSets)
        {
            Assert.Throws<ArgumentException>("files", () => McpServerSkillPackage.Create(
                [McpServerSkillFile.FromText("SKILL.md", markdown)], options));
            Assert.Throws<ArgumentException>("directoryPath", () => McpServerSkillPackage.CreateFromDirectory(directory, options));
            var services = new ServiceCollection();
            var builder = services.AddMcpServer();
            var before = services.ToArray();

            Assert.Throws<ArgumentException>("directoryPath", () => builder.WithSkillFromDirectory(directory, options));

            Assert.Equal(before, services);
        }
    }

    [Fact]
    public async Task ContributionsAreAdditive_AndInstallEachWireHandlerOnce()
    {
        var alpha = McpServerSkillPackage.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\n")]);
        var package = CreateGitWorkflow();
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithSkills([alpha.Skill])
            .WithSkillPackages([package])
            .WithSkills([alpha.Skill])
            .WithSkills();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var skills = provider.GetRequiredService<IOptions<McpSkillsOptions>>().Value.SkillCollection;
#pragma warning disable MCPEXP002
        Assert.Single(options.RequestHandlers!, handler => handler.Method == SkillsProtocol.MethodSkillsList);
        Assert.Single(options.RequestHandlers!, handler => handler.Method == SkillsProtocol.MethodSkillsGet);
#pragma warning restore MCPEXP002
        Assert.Same(alpha.Skill, skills[alpha.Skill.Id]);
        Assert.Same(package.Skill, skills[package.Skill.Id]);
        Assert.Equal(2, skills.Count);
        Assert.Same(package.Resources[0], Assert.Single(options.ResourceCollection!));
    }

    [Theory]
    [InlineData("enumeration")]
    [InlineData("null")]
    [InlineData("conflict")]
    [InlineData("null-manifest")]
    [InlineData("null-manifest-entry")]
    [InlineData("identity")]
    public async Task PackageFailuresLeaveTheCallsServicesUntouched(string failure)
    {
        var first = CreateGitWorkflow();
        var changed = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", GitWorkflowMarkdown + "changed")]);
        var entry = changed.Skill.ProtocolSkill!;
        switch (failure)
        {
            case "null-manifest": entry.Resources = null!; break;
            case "null-manifest-entry":
                Assert.IsAssignableFrom<IList<SkillResource>>(entry.Resources.Entries)[0] = null!;
                break;
            case "identity":
                entry.Uri = "skill://other/git-workflow/SKILL.md";
                entry.Resources = SkillResources.Dynamic;
                break;
        }

        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        var before = services.ToArray();
        int enumerations = 0;
        var exception = Record.Exception(() => builder.WithSkillPackages(Packages()));

        Assert.IsType(failure == "enumeration" ? typeof(InvalidOperationException) : typeof(ArgumentException), exception);
        if (failure is "null" or "null-manifest" or "null-manifest-entry" or "identity")
        {
            Assert.Equal("packages", Assert.IsType<ArgumentException>(exception).ParamName);
        }
        Assert.Equal(before, services);
        Assert.Equal(1, enumerations);

        enumerations = 0;
        builder.WithSkillPackages(ValidPackages());
        Assert.Equal(1, enumerations);
        await using var provider = services.BuildServiceProvider();
        Assert.Same(first.Skill, Assert.Single(provider.GetRequiredService<IOptions<McpSkillsOptions>>().Value.SkillCollection));
        Assert.Same(first.Resources[0], Assert.Single(provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ResourceCollection!));

        IEnumerable<McpServerSkillPackage> Packages()
        {
            enumerations++;
            yield return first;
            if (failure == "enumeration") throw new InvalidOperationException("Enumeration failed.");
            yield return failure == "null" ? null! : changed;
        }

        IEnumerable<McpServerSkillPackage> ValidPackages()
        {
            enumerations++;
            yield return first;
        }
    }

    [Fact]
    public async Task EmptyPackagesEnableSkills_AndPackageRegistrationRejectsNullArguments()
    {
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        Assert.Throws<ArgumentNullException>("builder", () => McpSkillsBuilderExtensions.WithSkillPackages(null!, []));
        Assert.Throws<ArgumentNullException>("packages", () => builder.WithSkillPackages(null!));
        Assert.Same(builder, builder.WithSkillPackages([]));
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        Assert.True(options.Capabilities!.Extensions!.ContainsKey(SkillsProtocol.ExtensionId));
        Assert.NotNull(options.Capabilities.Resources);
        Assert.Empty(provider.GetRequiredService<IOptions<McpSkillsOptions>>().Value.SkillCollection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigureCanRemoveSkills_WithoutSuppressingTheirResources(bool laterCallback)
    {
        var package = CreateGitWorkflow();
        var services = new ServiceCollection();
        var builder = services.AddMcpServer().WithSkillPackages([package]);
        if (laterCallback) services.Configure<McpSkillsOptions>(RemoveSkill);
        else builder.WithSkills(RemoveSkill);
        await using var provider = services.BuildServiceProvider();

        Assert.Empty(provider.GetRequiredService<IOptions<McpSkillsOptions>>().Value.SkillCollection);
        Assert.Same(package.Resources[0], Assert.Single(provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ResourceCollection!));

        void RemoveSkill(McpSkillsOptions options)
        {
            Assert.Same(package.Skill, Assert.Single(options.SkillCollection));
            options.SkillCollection.Clear();
        }
    }

    [Fact]
    public async Task SeparatePackageCallsDoNotHideResourcesFromAnIgnoredSkillIdentity()
    {
        var first = CreateGitWorkflow();
        var duplicate = McpServerSkillPackage.Create(
        [
            McpServerSkillFile.FromText("SKILL.md", GitWorkflowMarkdown + "changed"),
            McpServerSkillFile.FromText("extra.md", "extra"),
        ]);
        var services = new ServiceCollection();
        services.AddMcpServer().WithSkillPackages([first]).WithSkillPackages([duplicate]);
        await using var provider = services.BuildServiceProvider();

        var skills = provider.GetRequiredService<IOptions<McpSkillsOptions>>().Value.SkillCollection;
        var resources = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ResourceCollection!;
        Assert.Same(first.Skill, Assert.Single(skills));
        Assert.Equal(2, resources.Count);
        Assert.Same(first.Resources[0], resources[first.Skill.Id]);
        Assert.Same(duplicate.Resources[1], resources["skill://git-workflow/extra.md"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentRegistrationsCanDescribeDifferentSharedFileSnapshots(bool packages)
    {
        var outer = CreateOuter();
        var conflicting = McpServerSkillPackage.Create(
            [McpServerSkillFile.FromText("SKILL.md", InnerMarkdown + "changed")], new() { Uri = "skill://catalog/inner/SKILL.md" });
        var collection = new McpServerSkillCollection { outer.Skill, conflicting.Skill };
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        if (packages)
        {
            builder.WithSkillPackages([outer]).WithSkillPackages([conflicting]);
        }
        else
        {
            builder.WithSkills(options => options.SkillCollection = collection);
        }

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var registered = provider.GetRequiredService<IOptions<McpSkillsOptions>>().Value.SkillCollection;
        Assert.Same(outer.Skill, registered[outer.Skill.Id]);
        Assert.Same(conflicting.Skill, registered[conflicting.Skill.Id]);
        if (packages)
        {
            Assert.Same(outer.Resources[1], options.ResourceCollection![conflicting.Skill.Id]);
        }
        else
        {
            Assert.Same(collection, registered);
        }

        Assert.Equal(2, registered.Count);
    }

    private const string InnerMarkdown = "---\nname: inner\ndescription: Inner\n---\n";

    private static McpServerSkillPackage CreateOuter() => McpServerSkillPackage.Create(
    [
        McpServerSkillFile.FromText("SKILL.md", "---\nname: catalog\ndescription: Outer\n---\n"),
        McpServerSkillFile.FromText("inner/SKILL.md", InnerMarkdown),
    ], new() { Uri = "skill://catalog/SKILL.md" });

    private static McpServerSkillPackage CreateGitWorkflow() => McpServerSkillPackage.Create(
        [McpServerSkillFile.FromText("SKILL.md", GitWorkflowMarkdown)], new() { Uri = GitWorkflowUri });
}
