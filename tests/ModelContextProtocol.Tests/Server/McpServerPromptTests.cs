using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using Moq;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

public class McpServerPromptTests
{
    [Fact]
    public void Create_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("function", () => McpServerPrompt.Create((AIFunction)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerPrompt.Create((MethodInfo)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerPrompt.Create((MethodInfo)null!, typeof(object)));
        Assert.Throws<ArgumentNullException>("targetType", () => McpServerPrompt.Create(typeof(McpServerPromptTests).GetMethod(nameof(Create_InvalidArgs_Throws))!, (Type)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerPrompt.Create((Delegate)null!));
    }

    [Fact]
    public async Task SupportsIMcpServer()
    {
        Mock<IMcpServer> mockServer = new();

        McpServerPrompt prompt = McpServerPrompt.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return new ChatMessage(ChatRole.User, "Hello");
        });

        Assert.DoesNotContain("server", prompt.ProtocolPrompt.Arguments?.Select(a => a.Name) ?? []);

        var result = await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.NotNull(result.Messages);
        Assert.Single(result.Messages);
        Assert.Equal("Hello", result.Messages[0].Content.Text);
    }

    [Fact]
    public async Task SupportsServiceFromDI()
    {
        MyService expectedMyService = new();

        ServiceCollection sc = new();
        sc.AddSingleton(expectedMyService);
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerPrompt prompt = McpServerPrompt.Create((MyService actualMyService, int? something = null) =>
        {
            Assert.Same(expectedMyService, actualMyService);
            return new PromptMessage() { Role = Role.Assistant, Content = new() { Text = "Hello", Type = "text" } };
        }, new() { Services = services });

        Assert.Contains("something", prompt.ProtocolPrompt.Arguments?.Select(a => a.Name) ?? []);
        Assert.DoesNotContain("actualMyService", prompt.ProtocolPrompt.Arguments?.Select(a => a.Name) ?? []);

        Mock<IMcpServer> mockServer = new();

        await Assert.ThrowsAsync<ArgumentException>(async () => await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken));

        mockServer.SetupGet(x => x.Services).Returns(services);

        var result = await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("Hello", result.Messages[0].Content.Text);
    }

    [Fact]
    public async Task SupportsOptionalServiceFromDI()
    {
        MyService expectedMyService = new();

        ServiceCollection sc = new();
        sc.AddSingleton(expectedMyService);
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerPrompt prompt = McpServerPrompt.Create((MyService? actualMyService = null) =>
        {
            Assert.Null(actualMyService);
            return new PromptMessage() { Role = Role.Assistant, Content = new() { Text = "Hello", Type = "text" } };
        }, new() { Services = services });

        var result = await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("Hello", result.Messages[0].Content.Text);
    }

    [Fact]
    public async Task SupportsDisposingInstantiatedDisposableTargets()
    {
        McpServerPrompt prompt1 = McpServerPrompt.Create(
            typeof(DisposablePromptType).GetMethod(nameof(DisposablePromptType.InstanceMethod))!,
            typeof(DisposablePromptType));

        var result = await prompt1.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("disposals:1", result.Messages[0].Content.Text);
    }

    [Fact]
    public async Task SupportsAsyncDisposingInstantiatedAsyncDisposableTargets()
    {
        McpServerPrompt prompt1 = McpServerPrompt.Create(
            typeof(AsyncDisposablePromptType).GetMethod(nameof(AsyncDisposablePromptType.InstanceMethod))!,
            typeof(AsyncDisposablePromptType));

        var result = await prompt1.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("asyncDisposals:1", result.Messages[0].Content.Text);
    }

    [Fact]
    public async Task SupportsAsyncDisposingInstantiatedAsyncDisposableAndDisposableTargets()
    {
        McpServerPrompt prompt1 = McpServerPrompt.Create(
            typeof(AsyncDisposableAndDisposablePromptType).GetMethod(nameof(AsyncDisposableAndDisposablePromptType.InstanceMethod))!,
            typeof(AsyncDisposableAndDisposablePromptType));

        var result = await prompt1.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("disposals:0, asyncDisposals:1", result.Messages[0].Content.Text);
    }

    [Fact]
    public async Task CanReturnGetPromptResult()
    {
        GetPromptResult expected = new();

        McpServerPrompt prompt = McpServerPrompt.Create(() =>
        {
            return expected;
        });

        var actual = await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken);

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task CanReturnText()
    {
        string expected = "hello";

        McpServerPrompt prompt = McpServerPrompt.Create(() =>
        {
            return expected;
        });

        var actual = await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken);

        Assert.NotNull(actual);
        Assert.NotNull(actual.Messages);
        Assert.Single(actual.Messages);
        Assert.Equal(Role.User, actual.Messages[0].Role);
        Assert.Equal("text", actual.Messages[0].Content.Type);
        Assert.Equal(expected, actual.Messages[0].Content.Text);
    }

    [Fact]
    public async Task CanReturnPromptMessage()
    {
        PromptMessage expected = new()
        {
            Role = Role.User,
            Content = new() { Text = "hello", Type = "text" }
        };

        McpServerPrompt prompt = McpServerPrompt.Create(() =>
        {
            return expected;
        });

        var actual = await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken);

        Assert.NotNull(actual);
        Assert.NotNull(actual.Messages);
        Assert.Single(actual.Messages);
        Assert.Same(expected, actual.Messages[0]);
    }

    [Fact]
    public async Task CanReturnPromptMessages()
    {
        PromptMessage[] expected = [
            new()
            {
                Role = Role.User,
                Content = new() { Text = "hello", Type = "text" }
            },
            new()
            {
                Role = Role.Assistant,
                Content = new() { Text = "hello again", Type = "text" }
            }
        ];

        McpServerPrompt prompt = McpServerPrompt.Create(() =>
        {
            return expected;
        });

        var actual = await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken);

        Assert.NotNull(actual);
        Assert.NotNull(actual.Messages);
        Assert.Equal(2, actual.Messages.Count);
        Assert.Equal(Role.User, actual.Messages[0].Role);
        Assert.Equal("text", actual.Messages[0].Content.Type);
        Assert.Equal("hello", actual.Messages[0].Content.Text);
        Assert.Equal(Role.Assistant, actual.Messages[1].Role);
        Assert.Equal("text", actual.Messages[1].Content.Type);
        Assert.Equal("hello again", actual.Messages[1].Content.Text);
    }

    [Fact]
    public async Task CanReturnChatMessage()
    {
        PromptMessage expected = new()
        {
            Role = Role.User,
            Content = new() { Text = "hello", Type = "text" }
        };

        McpServerPrompt prompt = McpServerPrompt.Create(() =>
        {
            return expected.ToChatMessage();
        });

        var actual = await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken);

        Assert.NotNull(actual);
        Assert.NotNull(actual.Messages);
        Assert.Single(actual.Messages);
        Assert.Equal(Role.User, actual.Messages[0].Role);
        Assert.Equal("text", actual.Messages[0].Content.Type);
        Assert.Equal("hello", actual.Messages[0].Content.Text);
    }

    [Fact]
    public async Task CanReturnChatMessages()
    {
        PromptMessage[] expected = [
            new()
            {
                Role = Role.User,
                Content = new() { Text = "hello", Type = "text" }
            },
            new()
            {
                Role = Role.Assistant,
                Content = new() { Text = "hello again", Type = "text" }
            }
        ];

        McpServerPrompt prompt = McpServerPrompt.Create(() =>
        {
            return expected.Select(p => p.ToChatMessage());
        });

        var actual = await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken);

        Assert.NotNull(actual);
        Assert.NotNull(actual.Messages);
        Assert.Equal(2, actual.Messages.Count);
        Assert.Equal(Role.User, actual.Messages[0].Role);
        Assert.Equal("text", actual.Messages[0].Content.Type);
        Assert.Equal("hello", actual.Messages[0].Content.Text);
        Assert.Equal(Role.Assistant, actual.Messages[1].Role);
        Assert.Equal("text", actual.Messages[1].Content.Type);
        Assert.Equal("hello again", actual.Messages[1].Content.Text);
    }

    [Fact]
    public async Task ThrowsForNullReturn()
    {
        McpServerPrompt prompt = McpServerPrompt.Create(() =>
        {
            return (string)null!;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ThrowsForUnexpectedTypeReturn()
    {
        McpServerPrompt prompt = McpServerPrompt.Create(() =>
        {
            return new object();
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await prompt.GetAsync(
            new RequestContext<GetPromptRequestParams>(null!, null),
            TestContext.Current.CancellationToken));
    }

    private sealed class MyService;

    private class DisposablePromptType : IDisposable
    {
        public int Disposals { get; private set; }
        private ChatMessage _message = new ChatMessage(ChatRole.User, "");

        public void Dispose()
        {
            Disposals++;
            ((TextContent)_message.Contents[0]).Text = $"disposals:{Disposals}";
        }

        public ChatMessage InstanceMethod()
        {
            if (Disposals != 0)
            {
                throw new InvalidOperationException("Dispose was called");
            }

            return _message;
        }
    }

    private class AsyncDisposablePromptType : IAsyncDisposable
    {
        public int AsyncDisposals { get; private set; }
        private ChatMessage _message = new ChatMessage(ChatRole.User, "");

        public ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            ((TextContent)_message.Contents[0]).Text = $"asyncDisposals:{AsyncDisposals}";
            return default;
        }

        public ChatMessage InstanceMethod()
        {
            if (AsyncDisposals != 0)
            {
                throw new InvalidOperationException("DisposeAsync was called");
            }

            return _message;
        }
    }

    private class AsyncDisposableAndDisposablePromptType : IAsyncDisposable, IDisposable
    {
        public int Disposals { get; private set; }
        public int AsyncDisposals { get; private set; }
        private ChatMessage _message = new ChatMessage(ChatRole.User, "");

        public void Dispose()
        {
            Disposals++;
            ((TextContent)_message.Contents[0]).Text = $"disposals:{Disposals}, asyncDisposals:{AsyncDisposals}";
        }

        public ValueTask DisposeAsync()
        {
            AsyncDisposals++;
            ((TextContent)_message.Contents[0]).Text = $"disposals:{Disposals}, asyncDisposals:{AsyncDisposals}";
            return default;
        }

        public ChatMessage InstanceMethod()
        {
            if (Disposals + AsyncDisposals != 0)
            {
                throw new InvalidOperationException("Dispose and/or DisposeAsync was called");
            }

            return _message;
        }
    }
}
