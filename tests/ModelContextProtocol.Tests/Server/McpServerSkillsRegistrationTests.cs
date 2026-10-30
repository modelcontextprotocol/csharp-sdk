using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests additive Skills contributions, one-time wire-handler installation, and static file-resource integrity.
/// </summary>
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
    [InlineData("skills")]
    [InlineData("directory")]
    [InlineData("directory-with-hidden-files")]
    [InlineData("collection")]
    [InlineData("handlers")]
    public async Task ContributionsThroughDifferentBuildersAreAdditive_AndInstallEachWireHandlerOnce(string method)
    {
        var services = new ServiceCollection();
        var alpha = McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", "---\nname: alpha\ndescription: Alpha\n---\n")]);
        services.AddMcpServer().WithSkills([alpha]);
        Register(services.AddMcpServer(), method);
        services.AddMcpServer().WithSkills();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var skills = provider.GetRequiredService<IOptions<McpSkillsOptions>>().Value.SkillCollection;

#pragma warning disable MCPEXP002 // RequestHandlers is experimental.
        Assert.Single(options.RequestHandlers!, handler => handler.Method == "skills/list");
        Assert.Single(options.RequestHandlers!, handler => handler.Method == "skills/get");
#pragma warning restore MCPEXP002
        Assert.Same(alpha, skills[alpha.Id]);
        Assert.Equal(method == "handlers" ? 1 : 2, skills.Count);
        Assert.Equal(skills.Count, options.ResourceCollection!.Count);
    }

    [Fact]
    public void FailedRegistration_DoesNotBlockALaterOne()
    {
        var empty = Path.Combine(_root, "not-a-skill");
        Directory.CreateDirectory(empty);
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddMcpServer().WithSkillsFromDirectory(empty));
        services.AddMcpServer().WithSkillsFromDirectory(_root);
    }

    [Theory]
    [InlineData("conflicting-file", typeof(ArgumentException))]
    [InlineData("duplicate-skill", typeof(ArgumentException))]
    [InlineData("configure", typeof(InvalidOperationException))]
    [InlineData("handlers-configure", typeof(InvalidOperationException))]
    public async Task InvalidContributionsFailOptionsConstruction_InsteadOfBeingSilentlyLost(string failure, Type expectedException)
    {
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        RegisterFailing(builder, failure);
        await using var provider = services.BuildServiceProvider();
        Assert.Throws(expectedException, () => provider.GetRequiredService<IOptions<McpServerOptions>>().Value);
    }

    [Theory]
    [InlineData(true, GitWorkflowUri)]
    [InlineData(false, GitWorkflowUri)]
    [InlineData(true, "skill://GIT-WORKFLOW/SKILL.md")]
    [InlineData(false, "SKILL://git-workflow/SKILL.md")]
    public void ApplicationResourceAtASkillFileUri_FailsWhenTheOptionsAreBuilt(bool applicationResourceFirst, string applicationUri)
    {
        // The reported shape: an application resource whose parameterless template is a Skills file's URI.
        var applicationResource = McpServerResource.Create(
            () => "application content",
            new McpServerResourceCreateOptions { UriTemplate = applicationUri });
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        if (applicationResourceFirst)
        {
            builder.WithResources([applicationResource]).WithSkillsFromDirectory(_root);
        }
        else
        {
            builder.WithSkillsFromDirectory(_root).WithResources([applicationResource]);
        }

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptions<McpServerOptions>>().Value);
        Assert.Contains(GitWorkflowUri, exception.Message);
        Assert.Contains(applicationUri, exception.Message);
    }

    [Fact]
    public void ResourceAddedDirectlyToTheOptions_ConflictsLikeARegisteredOne()
    {
        var applicationResource = McpServerResource.Create(
            () => "application content",
            new McpServerResourceCreateOptions { UriTemplate = GitWorkflowUri });
        var services = new ServiceCollection();
        services.AddMcpServer(options => options.ResourceCollection = [applicationResource])
            .WithSkills([CreateGitWorkflow()]);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptions<McpServerOptions>>().Value);
        Assert.Contains(GitWorkflowUri, exception.Message);
    }

    [Fact]
    public void SharedNestedFiles_TheSkillsOwnResources_AndOtherUris_DoNotConflict()
    {
        const string InnerMarkdown = "---\nname: inner\ndescription: nested\n---\n";
        var outer = McpServerSkill.Create("skill://acme/SKILL.md",
        [
            McpServerSkillFile.FromText("SKILL.md", "---\nname: acme\ndescription: outer\n---\n"),
            McpServerSkillFile.FromText("inner/SKILL.md", InnerMarkdown),
        ]);
        var inner = McpServerSkill.Create("skill://acme/inner/SKILL.md", [McpServerSkillFile.FromText("SKILL.md", InnerMarkdown)]);
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithSkills([outer])
            .WithSkills([inner])
            .WithResources(outer.Resources)
            .WithResources(
            [
                McpServerResource.Create(() => "a different path", new McpServerResourceCreateOptions { UriTemplate = "skill://acme/skill.md" }),
                McpServerResource.Create((string name) => name, new McpServerResourceCreateOptions { UriTemplate = "skill://{name}/SKILL.md" }),
            ]);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(
            ["skill://acme/SKILL.md", "skill://acme/inner/SKILL.md", "skill://acme/skill.md", "skill://{name}/SKILL.md"],
            options.ResourceCollection!.PrimitiveNames);
        Assert.Same(outer.Resources[1], options.ResourceCollection["skill://acme/inner/SKILL.md"]);
    }

    private void Register(IMcpServerBuilder builder, string method)
    {
        switch (method)
        {
            case "skills":
                builder.WithSkills([CreateGitWorkflow()]);
                break;
            case "directory":
                builder.WithSkillsFromDirectory(_root);
                break;
            case "directory-with-hidden-files":
                builder.WithSkillsFromDirectory(_root, includeHiddenAndSystemFiles: true);
                break;
            case "collection":
                builder.WithSkills(options => options.SkillCollection.Add(CreateGitWorkflow()));
                break;
            case "handlers":
                builder.WithSkills(options =>
                {
                    options.ListSkillsHandler = (_, _) => new(new ListSkillsResult());
                    options.GetSkillHandler = (_, _) => throw new InvalidOperationException("No skills.");
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown registration method.");
        }
    }

    private void RegisterFailing(IMcpServerBuilder builder, string failure)
    {
        switch (failure)
        {
            case "conflicting-file":
                // The second skill lists the first one's file with different content.
                var changed = McpServerSkill.Create(GitWorkflowUri, [McpServerSkillFile.FromText("SKILL.md", GitWorkflowMarkdown + "Changed.\n")]);
                builder.WithSkills([CreateGitWorkflow(), changed]);
                break;
            case "duplicate-skill":
                // Identical files do not permit duplicate skill identities.
                builder.WithSkills([CreateGitWorkflow(), CreateGitWorkflow()]);
                break;
            case "configure":
                builder.WithSkills([CreateGitWorkflow()], _ => throw new InvalidOperationException("The configuration failed."));
                break;
            case "handlers-configure":
                builder.WithSkills(_ => throw new InvalidOperationException("The configuration failed."));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown failure.");
        }
    }

    private McpServerSkill CreateGitWorkflow() =>
        McpServerSkill.Create(GitWorkflowUri, [McpServerSkillFile.FromText("SKILL.md", GitWorkflowMarkdown)]);

    [Fact]
    public void CreationOptionsMetadataIsAShallowReadOnlySnapshot_NotSharedWithFileResources()
    {
        object[] source = ["initial"];
        var options = new McpServerSkillCreateOptions { Metadata = source };
        var skill = McpServerSkill.Create(GitWorkflowUri,
            [McpServerSkillFile.FromText("SKILL.md", GitWorkflowMarkdown)], options);
        source[0] = "changed";
        options.Metadata = ["replaced"];

        Assert.Equal("initial", Assert.Single(skill.Metadata));
        Assert.Throws<NotSupportedException>(() => ((IList<object>)skill.Metadata)[0] = "changed");
        Assert.DoesNotContain("initial", skill.Resources[0].Metadata);
    }

    [Fact]
    public void LiveCollectionRejectsConflictingSharedFilesBeforeMutation_AndClearReleasesTheConstraint()
    {
        const string InnerMarkdown = "---\nname: inner\ndescription: Inner\n---\n";
        var outer = McpServerSkill.Create("skill://catalog/SKILL.md",
        [
            McpServerSkillFile.FromText("SKILL.md", "---\nname: catalog\ndescription: Outer\n---\n"),
            McpServerSkillFile.FromText("inner/SKILL.md", InnerMarkdown),
        ]);
        var inner = McpServerSkill.Create("skill://catalog/inner/SKILL.md", [McpServerSkillFile.FromText("SKILL.md", InnerMarkdown)]);
        var conflicting = McpServerSkill.Create("skill://catalog/inner/SKILL.md", [McpServerSkillFile.FromText("SKILL.md", InnerMarkdown + "changed\n")]);
        var collection = new McpSkillsOptions().SkillCollection;
        collection.Add(outer);
        int changes = 0;
        collection.Changed += (_, _) => changes++;

        Assert.Throws<ArgumentException>(() => collection.Add(conflicting));
        Assert.Equal(0, changes);
        Assert.Same(outer, Assert.Single(collection));
        collection.Add(inner);
        Assert.Equal(2, collection.Count);
        Assert.True(collection.Remove(inner));
        Assert.Throws<ArgumentException>(() => collection.Add(conflicting));
        collection.Clear();
        collection.Add(conflicting);
        Assert.Same(conflicting, Assert.Single(collection));
    }

    [Fact]
    public async Task ConcurrentAdditionsCannotBothPublishConflictingSharedFiles()
    {
        var outer = McpServerSkill.Create("skill://catalog/SKILL.md",
        [
            McpServerSkillFile.FromText("SKILL.md", "---\nname: catalog\ndescription: Outer\n---\n"),
            McpServerSkillFile.FromText("inner/SKILL.md", "---\nname: inner\ndescription: Inner\n---\n"),
        ]);
        var conflicting = McpServerSkill.Create("skill://catalog/inner/SKILL.md",
            [McpServerSkillFile.FromText("SKILL.md", "---\nname: inner\ndescription: Changed\n---\n")]);
        var collection = new McpSkillsOptions().SkillCollection;
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int waiting = 0;

        async Task<bool> AddAsync(McpServerSkill skill)
        {
            if (Interlocked.Increment(ref waiting) == 2)
            {
                entered.SetResult(true);
            }
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                collection.Add(skill);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        var first = Task.Run(() => AddAsync(outer), TestContext.Current.CancellationToken);
        var second = Task.Run(() => AddAsync(conflicting), TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            release.TrySetResult(true);
        }
        Assert.Single(await Task.WhenAll(first, second), added => added);
        Assert.Single(collection);
    }

    [Fact]
    public async Task RegisteredPrimitive_RetainsIdentityAndCallerOwnedLocalMetadata()
    {
        await using var metadata = new OwnedMetadata();
        var skill = McpServerSkill.Create(
            [McpServerSkillFile.FromText("SKILL.md", GitWorkflowMarkdown)], new() { Metadata = [metadata] });
        var services = new ServiceCollection();
        McpSkillsOptions? skillsOptions = null;
        services.AddMcpServer().WithSkills([skill], options => skillsOptions = options);

        await using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        {
            _ = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
            Assert.Same(skill, skillsOptions!.SkillCollection[GitWorkflowUri]);
            Assert.Same(metadata, Assert.Single(skill.Metadata));
        }

        Assert.Equal(0, metadata.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateIdentitiesAcrossSeparateContributionsFailOptionsConstruction(bool sameInstance)
    {
        var skill = CreateGitWorkflow();
        var services = new ServiceCollection();
        services.AddMcpServer().WithSkills([skill]).WithSkills([sameInstance ? skill : CreateGitWorkflow()]);
        await using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<ArgumentException>(() => provider.GetRequiredService<IOptions<McpServerOptions>>().Value);
        Assert.Contains(GitWorkflowUri, error.Message);
    }

    [Fact]
    public void SortedSnapshotIsCachedAndInvalidatedBySuccessfulMutationsOnly()
    {
        var options = new McpSkillsOptions();
        var getSnapshot = typeof(McpSkillsOptions).GetMethod("GetSortedSkills", BindingFlags.Instance | BindingFlags.NonPublic)!;
        McpServerSkill[] Snapshot() => (McpServerSkill[])getSnapshot.Invoke(options, null)!;
        var empty = Snapshot();
        Assert.Same(empty, Snapshot());
        var skill = CreateGitWorkflow();
        options.SkillCollection.Add(skill);
        var added = Snapshot();
        Assert.NotSame(empty, added);
        Assert.Same(skill, Assert.Single(added));
        Assert.False(options.SkillCollection.TryAdd(skill));
        Assert.Same(added, Snapshot());
        Assert.True(options.SkillCollection.Remove(skill));
        var removed = Snapshot();
        Assert.NotSame(added, removed);
        Assert.Empty(removed);
        Assert.False(options.SkillCollection.Remove(skill));
        Assert.Same(removed, Snapshot());
        options.SkillCollection.Add(skill);
        var beforeClear = Snapshot();
        options.SkillCollection.Clear();
        Assert.NotSame(beforeClear, Snapshot());
        Assert.Empty(Snapshot());
    }

    [Theory]
    [InlineData("entry")]
    [InlineData("files")]
    [InlineData("files-uri")]
    [InlineData("files-frontmatter")]
    [InlineData("directory")]
    [InlineData("directory-hidden")]
    [InlineData("directory-uri")]
    [InlineData("directory-uri-hidden")]
    [InlineData("directory-frontmatter")]
    [InlineData("directory-frontmatter-hidden")]
    public void EveryCreationOverloadSnapshotsCreationOptionsMetadata(string creation)
    {
        object[] metadata = ["creation-metadata"];
        var options = new McpServerSkillCreateOptions { Metadata = metadata };
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", GitWorkflowMarkdown) };
        var frontmatter = CreateGitWorkflow().ProtocolSkill.Frontmatter;
        string directory = Path.Combine(_root, "git-workflow");
        var skill = creation switch
        {
            "entry" => new McpServerSkill(CreateGitWorkflow().ProtocolSkill, options),
            "files" => McpServerSkill.Create(files, options),
            "files-uri" => McpServerSkill.Create(GitWorkflowUri, files, options),
            "files-frontmatter" => McpServerSkill.Create(GitWorkflowUri, frontmatter, files, options),
            "directory" => McpServerSkill.CreateFromDirectory(directory, options),
            "directory-hidden" => McpServerSkill.CreateFromDirectory(directory, includeHiddenAndSystemFiles: true, options),
            "directory-uri" => McpServerSkill.CreateFromDirectory(GitWorkflowUri, directory, options),
            "directory-uri-hidden" => McpServerSkill.CreateFromDirectory(GitWorkflowUri, directory, includeHiddenAndSystemFiles: true, options),
            "directory-frontmatter" => McpServerSkill.CreateFromDirectory(GitWorkflowUri, frontmatter, directory, options),
            "directory-frontmatter-hidden" => McpServerSkill.CreateFromDirectory(GitWorkflowUri, frontmatter, directory, includeHiddenAndSystemFiles: true, options),
            _ => throw new ArgumentOutOfRangeException(nameof(creation)),
        };
        metadata[0] = "changed";
        Assert.Equal("creation-metadata", Assert.Single(skill.Metadata));
        Assert.Equal(GitWorkflowUri, skill.Id);
    }

    private sealed class OwnedMetadata : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return default;
        }
    }
}
