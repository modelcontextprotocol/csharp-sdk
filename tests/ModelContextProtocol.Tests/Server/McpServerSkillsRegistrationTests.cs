using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests that a server has one Skills registration, rejected early when repeated through any public method, and
/// that each Skills file is the only resource at its URI.
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
    public void SecondRegistrationOnTheSameBuilder_ThrowsBeforeRegisteringAnything(string method)
    {
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        builder.WithSkills([CreateGitWorkflow()]);
        int registrationCount = services.Count;

        var exception = Assert.Throws<InvalidOperationException>(() => Register(builder, method));

        Assert.Contains("Skills are already registered", exception.Message);
        Assert.Equal(registrationCount, services.Count);
    }

    [Theory]
    [InlineData("skills")]
    [InlineData("directory")]
    [InlineData("directory-with-hidden-files")]
    [InlineData("collection")]
    [InlineData("handlers")]
    public void SecondRegistrationThroughAnotherBuilderForTheSameServices_Throws(string method)
    {
        // For example a library and the application each enabling Skills on the same service collection.
        var services = new ServiceCollection();
        Register(services.AddMcpServer(), method);
        var secondBuilder = services.AddMcpServer();
        int registrationCount = services.Count;

        var exception = Assert.Throws<InvalidOperationException>(() => secondBuilder.WithSkillsFromDirectory(_root));

        Assert.Contains("Skills are already registered", exception.Message);
        Assert.Equal(registrationCount, services.Count);
    }

    [Theory]
    [InlineData("skills")]
    [InlineData("directory")]
    [InlineData("directory-with-hidden-files")]
    [InlineData("collection")]
    [InlineData("handlers")]
    public async Task SingleRegistration_InstallsEachSkillsMethodOnce(string method)
    {
        var services = new ServiceCollection();
        Register(services.AddMcpServer(), method);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

#pragma warning disable MCPEXP002 // RequestHandlers is experimental.
        Assert.Single(options.RequestHandlers!, handler => handler.Method == "skills/list");
        Assert.Single(options.RequestHandlers!, handler => handler.Method == "skills/get");
#pragma warning restore MCPEXP002
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
    public void FailedRegistration_LeavesNoResources_AndARetryWithNewSkillsSucceeds(string failure, Type expectedException)
    {
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();
        int registrationCount = services.Count;

        Assert.Throws(expectedException, () => RegisterFailing(builder, failure));
        Assert.Equal(registrationCount, services.Count);

        // New skill instances: a resource left behind by the failed call would conflict with them.
        builder.WithSkills([CreateGitWorkflow()]);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal([GitWorkflowUri], options.ResourceCollection!.PrimitiveNames);
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
            .WithSkills([outer, inner])
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
                // Identical files are shared, so this fails later, when the catalog sees the same skill URI twice.
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
    public void LocalMetadataIsAShallowReadOnlySnapshot_AndDoesNotMutateTheOriginalPrimitive()
    {
        var original = CreateGitWorkflow().WithMetadata("server-local-policy");
        object[] source = ["initial"];
        var copy = original.WithMetadata(source);
        source[0] = "changed";

        Assert.Equal(original.Id, copy.Id);
        Assert.Equal("initial", Assert.Single(copy.Metadata));
        Assert.Equal("server-local-policy", Assert.Single(original.Metadata));
        Assert.Throws<NotSupportedException>(() => ((IList<object>)copy.Metadata)[0] = "changed");
        Assert.Same(original.Resources[0], copy.Resources[0]);
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
        var skill = CreateGitWorkflow().WithMetadata(metadata);
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
