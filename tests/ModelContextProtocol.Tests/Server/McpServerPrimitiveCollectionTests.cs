using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public class McpServerPrimitiveCollectionTests
{
    private static McpServerTool CreateTool(string name) =>
        McpServerTool.Create(() => name, new() { Name = name });

    // -------------------------------------------------------------------------
    // Preexisting behavior -- Changed event without DeferChanges
    // -------------------------------------------------------------------------

    [Fact]
    public void TryAdd_NewTool_ReturnsTrue_FiresChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        bool added = collection.TryAdd(CreateTool("tool1"));

        Assert.True(added);
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void TryAdd_DuplicateName_ReturnsFalse_DoesNotFireChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.TryAdd(CreateTool("tool1"));

        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        bool added = collection.TryAdd(CreateTool("tool1"));

        Assert.False(added);
        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void TryAdd_SameTool_TwiceInSequence_FiresOnlyOnFirst()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        bool first = collection.TryAdd(CreateTool("tool1"));
        bool second = collection.TryAdd(CreateTool("tool1"));

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void Remove_ExistingTool_ReturnsTrue_FiresChanged()
    {
        var tool = CreateTool("tool1");
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.TryAdd(tool);

        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        bool removed = collection.Remove(tool);

        Assert.True(removed);
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void Remove_NonExistentTool_ReturnsFalse_DoesNotFireChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        bool removed = collection.Remove(CreateTool("tool1"));

        Assert.False(removed);
        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void Clear_NonEmptyCollection_FiresChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.TryAdd(CreateTool("tool1"));
        collection.TryAdd(CreateTool("tool2"));

        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        collection.Clear();

        Assert.Equal(1, changeCount);
        Assert.Empty(collection);
    }

    [Fact]
    public void Clear_EmptyCollection_FiresChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        collection.Clear();

        Assert.Equal(1, changeCount);
    }

    // -------------------------------------------------------------------------
    // DeferChanges -- basic deferral behavior
    // -------------------------------------------------------------------------

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
    public void DeferChanges_AddThenRemoveSameTool_FiresOneChanged()
    {
        // Net effect is no change in contents, but a Changed notification still fires
        // because mutations occurred during the scope.
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChanges())
        {
            var tool = CreateTool("tool1");
            collection.TryAdd(tool);
            collection.Remove(tool);
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
        Assert.Empty(collection);
    }

    [Fact]
    public void DeferChanges_DuplicateTryAdd_OnlySuccessfulMutationMarksChange()
    {
        // The first TryAdd succeeds (mutation), the second fails (no mutation).
        // Exactly one Changed fires on dispose.
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChanges())
        {
            collection.TryAdd(CreateTool("tool1")); // succeeds
            collection.TryAdd(CreateTool("tool1")); // fails -- duplicate name
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChanges_OnlyFailedTryAdds_DoesNotFireChanged()
    {
        var tool = CreateTool("tool1");
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.TryAdd(tool);

        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChanges())
        {
            collection.TryAdd(CreateTool("tool1")); // fails -- already present
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(0, changeCount);
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
