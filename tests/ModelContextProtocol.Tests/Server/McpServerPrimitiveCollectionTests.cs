using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public class McpServerPrimitiveCollectionTests
{
    private static McpServerTool CreateTool(string name) =>
        McpServerTool.Create(() => name, new() { Name = name });

    [Fact]
    public void DeferChanges_NoMutation_DoesNotFireChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChanges())
        {
            // no mutations
        }

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void DeferChanges_SingleMutation_FiresOneChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChanges())
        {
            collection.TryAdd(CreateTool("tool1"));
            Assert.Equal(0, changeCount); // not fired yet
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChanges_MultipleMutations_FiresOneChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChanges())
        {
            collection.TryAdd(CreateTool("tool1"));
            collection.TryAdd(CreateTool("tool2"));
            collection.TryAdd(CreateTool("tool3"));
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChanges_MixedAddAndRemove_FiresOneChanged()
    {
        var tool = CreateTool("tool1");
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.TryAdd(tool);

        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChanges())
        {
            collection.TryAdd(CreateTool("tool2"));
            collection.Remove(tool);
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChanges_WithClear_FiresOneChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.TryAdd(CreateTool("tool1"));
        collection.TryAdd(CreateTool("tool2"));

        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChanges())
        {
            collection.Clear();
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChanges_Nested_FiresOnceOnOutermostDispose()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChanges())
        {
            collection.TryAdd(CreateTool("tool1"));

            using (collection.DeferChanges())
            {
                collection.TryAdd(CreateTool("tool2"));
                Assert.Equal(0, changeCount);
            }

            Assert.Equal(0, changeCount); // inner scope disposed, but outer still active
            collection.TryAdd(CreateTool("tool3"));
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChanges_AfterScope_ResumesImmediateNotifications()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChanges())
        {
            collection.TryAdd(CreateTool("tool1"));
        }

        Assert.Equal(1, changeCount);

        // After the scope, each mutation fires immediately
        collection.TryAdd(CreateTool("tool2"));
        Assert.Equal(2, changeCount);

        collection.TryAdd(CreateTool("tool3"));
        Assert.Equal(3, changeCount);
    }

    [Fact]
    public void DeferChanges_DisposeIdempotent_DoesNotFireTwice()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        var scope = collection.DeferChanges();
        collection.TryAdd(CreateTool("tool1"));

        scope.Dispose();
        Assert.Equal(1, changeCount);

        scope.Dispose(); // second dispose should be a no-op
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChanges_ScopeWithNoHandlers_DoesNotThrow()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        // no Changed handler registered

        using (collection.DeferChanges())
        {
            collection.TryAdd(CreateTool("tool1"));
        }

        Assert.Single(collection);
    }

    [Fact]
    public void WithoutDeferChanges_EachMutationFiresImmediately()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        collection.TryAdd(CreateTool("tool1"));
        Assert.Equal(1, changeCount);

        collection.TryAdd(CreateTool("tool2"));
        Assert.Equal(2, changeCount);

        collection.TryAdd(CreateTool("tool3"));
        Assert.Equal(3, changeCount);
    }
}
