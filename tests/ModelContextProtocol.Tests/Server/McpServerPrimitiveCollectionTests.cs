using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public class McpServerPrimitiveCollectionTests
{
    private static McpServerTool CreateTool(string name) =>
        McpServerTool.Create(() => name, new() { Name = name });

    private static McpServerPrompt CreatePrompt(string name) =>
        McpServerPrompt.Create(() => new ChatMessage(ChatRole.User, name), new() { Name = name });

    // -------------------------------------------------------------------------
    // Changed event without DeferChangedEvents
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
    // DeferChangedEvents -- basic deferral behavior
    // -------------------------------------------------------------------------

    [Fact]
    public void DeferChangedEvents_NoMutation_DoesNotFireChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
        {
            // no mutations
        }

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_SingleMutation_FiresOneChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
        {
            collection.TryAdd(CreateTool("tool1"));
            Assert.Equal(0, changeCount); // not fired yet
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_MultipleMutations_FiresOneChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
        {
            collection.TryAdd(CreateTool("tool1"));
            collection.TryAdd(CreateTool("tool2"));
            collection.TryAdd(CreateTool("tool3"));
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_MixedAddAndRemove_FiresOneChanged()
    {
        var tool = CreateTool("tool1");
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.TryAdd(tool);

        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
        {
            collection.TryAdd(CreateTool("tool2"));
            collection.Remove(tool);
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_AddThenRemoveSameTool_FiresOneChanged()
    {
        // Net effect is no change in contents, but a Changed notification still fires
        // because mutations occurred during the scope.
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
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
    public void DeferChangedEvents_DuplicateTryAdd_OnlySuccessfulMutationMarksChange()
    {
        // The first TryAdd succeeds (mutation), the second fails (no mutation).
        // Exactly one Changed fires on dispose.
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
        {
            collection.TryAdd(CreateTool("tool1")); // succeeds
            collection.TryAdd(CreateTool("tool1")); // fails -- duplicate name
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_OnlyFailedTryAdds_DoesNotFireChanged()
    {
        var tool = CreateTool("tool1");
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.TryAdd(tool);

        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
        {
            collection.TryAdd(CreateTool("tool1")); // fails -- already present
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_WithClear_FiresOneChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.TryAdd(CreateTool("tool1"));
        collection.TryAdd(CreateTool("tool2"));

        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
        {
            collection.Clear();
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_Nested_FiresOnceWhenAllScopesDisposed()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
        {
            collection.TryAdd(CreateTool("tool1"));

            using (collection.DeferChangedEvents())
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
    public void DeferChangedEvents_OutOfOrderDisposal_FiresOnceWhenAllScopesDisposed()
    {
        // Scopes created in order 1, 2 but disposed in reverse order 2, 1.
        // Changed should NOT fire when scope 2 is disposed (scope 1 still active).
        // Changed SHOULD fire when scope 1 is disposed (last active scope gone).
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        var scope1 = collection.DeferChangedEvents();
        var scope2 = collection.DeferChangedEvents();
        collection.TryAdd(CreateTool("tool1"));

        Assert.Equal(0, changeCount);
        scope2.Dispose(); // out-of-order dispose; scope1 still active
        Assert.Equal(0, changeCount);

        scope1.Dispose(); // last scope disposed; Changed fires now
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_DoubleDisposeSingleScope_DoesNotDecrementCountTwice()
    {
        // Double-disposing scope1 must not decrement _activeDeferralScopes more than once,
        // which would cause Changed to fire while scope2 is still active.
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        var scope1 = collection.DeferChangedEvents();
        var scope2 = collection.DeferChangedEvents();
        collection.TryAdd(CreateTool("tool1"));

        scope1.Dispose();
        Assert.Equal(0, changeCount); // scope2 still active

        scope1.Dispose(); // second dispose of scope1 -- must be a no-op
        Assert.Equal(0, changeCount); // scope2 is still active; Changed must NOT fire yet

        scope2.Dispose(); // now all scopes are disposed; Changed fires
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_OutOfOrderDisposalNoMutation_DoesNotFireChanged()
    {
        // Same out-of-order pattern but with no mutations -- verifies no spurious Changed.
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        var scope1 = collection.DeferChangedEvents();
        var scope2 = collection.DeferChangedEvents();

        scope2.Dispose();
        scope1.Dispose();

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_AfterScope_ResumesImmediateNotifications()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
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
    public void DeferChangedEvents_DisposeIdempotent_DoesNotFireTwice()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        var scope = collection.DeferChangedEvents();
        collection.TryAdd(CreateTool("tool1"));

        scope.Dispose();
        Assert.Equal(1, changeCount);

        scope.Dispose(); // second dispose should be a no-op
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_ScopeWithNoHandlers_DoesNotThrow()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        // no Changed handler registered

        using (collection.DeferChangedEvents())
        {
            collection.TryAdd(CreateTool("tool1"));
        }

        Assert.Single(collection);
    }

    [Fact]
    public void WithoutDeferChangedEvents_EachMutationFiresImmediately()
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

    // -------------------------------------------------------------------------
    // DeferChangedEvents -- concurrency
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeferChangedEvents_ConcurrentMutations_FiresExactlyOneChanged()
    {
        const int threadCount = 10;
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => Interlocked.Increment(ref changeCount);

        using (collection.DeferChangedEvents())
        {
            await Task.WhenAll(Enumerable.Range(0, threadCount).Select(i =>
                Task.Run(() => collection.TryAdd(CreateTool($"tool{i}")), TestContext.Current.CancellationToken)));
        }

        Assert.Equal(1, changeCount);
        Assert.Equal(threadCount, collection.Count);
    }

    [Fact]
    public async Task DeferChangedEvents_MutationRacingWithDispose_NotificationNotLost()
    {
        // Run many iterations to reliably exercise the race between a mutation
        // and disposal of the outermost scope. With the lock-free implementation
        // the race could cause the notification to be lost; the lock-based
        // implementation must always fire exactly one notification.
        for (int iteration = 0; iteration < 200; iteration++)
        {
            var collection = new McpServerPrimitiveCollection<McpServerTool>();
            int changeCount = 0;
            collection.Changed += (_, _) => Interlocked.Increment(ref changeCount);

            var scope = collection.DeferChangedEvents();

            // Run the mutation and the dispose concurrently.
            var addTask = Task.Run(() => collection.TryAdd(CreateTool("tool1")), TestContext.Current.CancellationToken);
            var disposeTask = Task.Run(() => scope.Dispose(), TestContext.Current.CancellationToken);

            await Task.WhenAll(addTask, disposeTask);

            // Regardless of ordering: exactly one notification must have fired.
            // - If TryAdd runs before Dispose: the mutation marks _pendingChange;
            //   Dispose sees depth -> 0 with a pending change and fires.
            // - If Dispose runs before TryAdd: depth is already 0 when TryAdd
            //   calls RaiseChanged, so it fires immediately.
            // The lock prevents the third (buggy) interleaving where Dispose
            // sees no pending change and TryAdd sees depth > 0, dropping the event.
            Assert.Equal(1, changeCount);
        }
    }

    // -------------------------------------------------------------------------
    // DeferChangedEvents -- derived-type coalescing
    // -------------------------------------------------------------------------

    private sealed class TrackingCollection : McpServerPrimitiveCollection<McpServerTool>
    {
        public void RaiseChangedDirectly() => RaiseChanged();
    }

    [Fact]
    public void DeferChangedEvents_DerivedTypeCallsRaiseChanged_Coalesces()
    {
        // Verify that derived types calling RaiseChanged() directly (the path
        // McpServerResourceCollection and other subclasses rely on) are gated
        // by the same deferral check.
        var collection = new TrackingCollection();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
        {
            collection.RaiseChangedDirectly();
            collection.RaiseChangedDirectly();
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_DerivedTypeRaisesChanged_OutsideScope_FiresImmediately()
    {
        var collection = new TrackingCollection();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        collection.RaiseChangedDirectly();
        Assert.Equal(1, changeCount);

        collection.RaiseChangedDirectly();
        Assert.Equal(2, changeCount);
    }

    // -------------------------------------------------------------------------
    // DeferChangedEvents -- exception safety
    // -------------------------------------------------------------------------

    [Fact]
    public void DeferChangedEvents_ExceptionDuringScope_StillFiresChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        try
        {
            using (collection.DeferChangedEvents())
            {
                collection.TryAdd(CreateTool("tool1"));
                throw new InvalidOperationException("test");
            }
        }
        catch (InvalidOperationException) { }

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void DeferChangedEvents_ExceptionDuringScope_ResumesImmediateNotificationsAfterward()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        try
        {
            using (collection.DeferChangedEvents())
            {
                collection.TryAdd(CreateTool("tool1"));
                throw new InvalidOperationException("test");
            }
        }
        catch (InvalidOperationException) { }

        Assert.Equal(1, changeCount);

        // Deferral must be fully reset: mutations outside the scope fire immediately.
        collection.TryAdd(CreateTool("tool2"));
        Assert.Equal(2, changeCount);

        collection.TryAdd(CreateTool("tool3"));
        Assert.Equal(3, changeCount);
    }

    // -------------------------------------------------------------------------
    // DeferChangedEvents -- prompt collection coverage
    // -------------------------------------------------------------------------

    [Fact]
    public void DeferChangedEvents_PromptCollection_MultipleMutations_FiresOneChanged()
    {
        var collection = new McpServerPrimitiveCollection<McpServerPrompt>();
        int changeCount = 0;
        collection.Changed += (_, _) => changeCount++;

        using (collection.DeferChangedEvents())
        {
            collection.TryAdd(CreatePrompt("prompt1"));
            collection.TryAdd(CreatePrompt("prompt2"));
            collection.TryAdd(CreatePrompt("prompt3"));
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
        Assert.Equal(3, collection.Count);
    }
}
