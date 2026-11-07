using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Runtime.CompilerServices;

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

    // -------------------------------------------------------------------------
    // Enumeration order
    // -------------------------------------------------------------------------

    private static readonly string[] s_unorderedNames =
    [
        "zulu", "alpha", "mike", "echo", "yankee", "bravo", "kilo", "xray", "charlie", "lima",
        "whiskey", "delta", "november", "victor", "foxtrot", "oscar", "uniform", "golf", "papa", "tango",
    ];

    private static McpServerResource CreateResource(string uri) =>
        McpServerResource.Create(() => uri, new() { UriTemplate = uri, Name = uri });

    [Fact]
    public void Enumeration_YieldsPrimitivesInInsertionOrder()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var name in s_unorderedNames)
        {
            collection.Add(CreateTool(name));
        }

        Assert.Equal(s_unorderedNames, collection.Select(t => t.ProtocolTool.Name));
        Assert.Equal(s_unorderedNames, collection.ToArray().Select(t => t.ProtocolTool.Name));
        Assert.Equal(s_unorderedNames, collection.PrimitiveNames);

        var copy = new McpServerTool[collection.Count + 1];
        collection.CopyTo(copy, 1);
        Assert.Null(copy[0]);
        Assert.Equal(s_unorderedNames, copy.Skip(1).Select(t => t.ProtocolTool.Name));
    }

    [Fact]
    public void Enumeration_PromptCollection_YieldsPromptsInInsertionOrder()
    {
        var collection = new McpServerPrimitiveCollection<McpServerPrompt>();
        foreach (var name in s_unorderedNames)
        {
            collection.Add(CreatePrompt(name));
        }

        Assert.Equal(s_unorderedNames, collection.Select(p => p.ProtocolPrompt.Name));
    }

    [Fact]
    public void Enumeration_ResourceCollection_YieldsResourcesInInsertionOrder()
    {
        string[] uris = [.. s_unorderedNames.Select(name => $"test://{name}")];

        var collection = new McpServerResourceCollection();
        foreach (var uri in uris)
        {
            collection.Add(CreateResource(uri));
        }

        Assert.Equal(uris, collection.Select(r => r.ProtocolResourceTemplate.UriTemplate));
        Assert.Equal(uris, collection.PrimitiveNames);
    }

    [Fact]
    public void Enumeration_RemovedAndReaddedPrimitive_MovesToEnd()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        var first = CreateTool("first");
        collection.Add(first);
        collection.Add(CreateTool("second"));
        collection.Add(CreateTool("third"));

        Assert.True(collection.Remove(first));
        collection.Add(CreateTool("first"));

        Assert.Equal(["second", "third", "first"], collection.Select(t => t.ProtocolTool.Name));
    }

    [Fact]
    public void Enumeration_AfterClear_UsesNewInsertionOrder()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.Add(CreateTool("alpha"));
        collection.Add(CreateTool("bravo"));

        collection.Clear();
        collection.Add(CreateTool("bravo"));
        collection.Add(CreateTool("alpha"));

        Assert.Equal(["bravo", "alpha"], collection.Select(t => t.ProtocolTool.Name));
    }

    [Fact]
    public void Remove_DifferentInstanceWithSameName_ReturnsFalse()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        var stored = CreateTool("tool1");
        collection.Add(stored);

        var other = CreateTool("tool1");

        Assert.DoesNotContain(other, collection);
        Assert.False(collection.Remove(other));
        Assert.Contains(stored, collection);
        Assert.Same(stored, collection["tool1"]);
    }

    [Fact]
    public void PrimitiveNames_PrimitiveRenamedAfterAdd_ReturnsNameItWasAddedUnder()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        var tool = CreateTool("original");
        collection.Add(tool);

        tool.ProtocolTool.Name = "renamed";

        Assert.Equal(["original"], collection.PrimitiveNames);
        Assert.True(collection.TryGetPrimitive("original", out var found));
        Assert.Same(tool, found);
    }

    [Fact]
    public void ToArrayAndPrimitiveNames_ReturnCopiesTheCallerOwns()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.Add(CreateTool("alpha"));
        collection.Add(CreateTool("bravo"));

        var array = collection.ToArray();
        array[0] = array[1];
        var names = collection.PrimitiveNames;
        ((string[])names)[0] = "changed";

        Assert.Equal(["alpha", "bravo"], collection.Select(t => t.ProtocolTool.Name));
        Assert.Equal(["alpha", "bravo"], collection.PrimitiveNames);
        Assert.NotSame(collection.ToArray(), collection.ToArray());
    }

    [Fact]
    public void CopyTo_DestinationTooSmall_Throws()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();
        collection.Add(CreateTool("alpha"));
        collection.Add(CreateTool("bravo"));

        Assert.Throws<ArgumentException>(() => collection.CopyTo(new McpServerTool[2], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => collection.CopyTo(new McpServerTool[2], -1));
    }

    [Fact]
    public void Remove_EqualsReplacesEntryUnderSameKey_LeavesReplacementInPlace()
    {
        var collection = new McpServerPrimitiveCollection<ReentrantEqualityPrimitive>();
        var original = new ReentrantEqualityPrimitive("tool1");
        var replacement = new ReentrantEqualityPrimitive("tool1");
        collection.Add(original);

        original.OnEquals = () =>
        {
            collection.Clear();
            collection.Add(replacement);
        };

        Assert.False(collection.Remove(original));
        Assert.False(collection.IsEmpty);
        Assert.Same(replacement, collection["tool1"]);
        Assert.Same(replacement, Assert.Single(collection.ToArray()));
    }

    [Fact]
    public void Enumeration_AlreadyVisiblePrimitive_KeepsItsPosition()
    {
        // The comparer reenters the collection on the same thread before "first" is inserted, which deterministically
        // forces the interleaving where a later add becomes visible while an earlier add is still in progress.
        var comparer = new InsertionInterleavingComparer();
        var collection = new McpServerPrimitiveCollection<McpServerTool>(comparer);
        string[] whileFirstPending = [];

        comparer.BeforeFirstInsertion = () =>
        {
            collection.Add(CreateTool("second"));
            whileFirstPending = [.. collection.Select(t => t.ProtocolTool.Name)];
        };

        collection.Add(CreateTool("first"));
        string[] afterward = [.. collection.Select(t => t.ProtocolTool.Name)];

        Assert.Equal(["second"], whileFirstPending);
        Assert.Equal(["second", "first"], afterward);
    }

    [Fact]
    public async Task Enumeration_AddStalledMidway_LaterAddDoesNotOvertakeIt()
    {
        // Best-effort threaded coverage: the second add is given a window to complete while the first is stalled, but
        // isn't guaranteed to use it. Enumeration_AlreadyVisiblePrimitive_KeepsItsPosition covers the interleaving
        // deterministically.
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var comparer = new StallingComparer("first", cancellationToken);
        var collection = new McpServerPrimitiveCollection<McpServerTool>(comparer);

        // Enumerate once so listing while an add is stalled reads the cached snapshot rather than waiting for the add.
        Assert.Empty(collection);

        Task addFirst = Task.Run(() => collection.Add(CreateTool("first")), cancellationToken);
        Task addSecond = Task.CompletedTask;
        string[] whileStalled;
        try
        {
            Assert.True(comparer.Stalled.Wait(TestConstants.DefaultTimeout, cancellationToken));

            addSecond = Task.Run(() => collection.Add(CreateTool("second")), cancellationToken);
            await Task.WhenAny(addSecond, Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken));
            whileStalled = [.. collection.Select(t => t.ProtocolTool.Name)];
        }
        finally
        {
            comparer.Release.Set();
            await Task.WhenAll(addFirst, addSecond);
        }

        string[] afterward = [.. collection.Select(t => t.ProtocolTool.Name)];

        // A primitive that was already listed must not be reordered by an add that started before it.
        Assert.Equal(whileStalled, afterward.Take(whileStalled.Length));
    }

    // -------------------------------------------------------------------------
    // Copying
    // -------------------------------------------------------------------------

    [Fact]
    public void Copy_NullCollection_Throws()
    {
        Assert.Throws<ArgumentNullException>("collection", () => new McpServerPrimitiveCollection<McpServerTool>((McpServerPrimitiveCollection<McpServerTool>)null!));
        Assert.Throws<ArgumentNullException>("collection", () => new McpServerResourceCollection(null!));
    }

    [Fact]
    public void Copy_ContainsSamePrimitivesInSameOrder()
    {
        var source = new McpServerPrimitiveCollection<McpServerTool>();
        McpServerTool[] tools = [CreateTool("c"), CreateTool("a"), CreateTool("b")];
        foreach (var tool in tools)
        {
            source.Add(tool);
        }

        var copy = new McpServerPrimitiveCollection<McpServerTool>(source);

        Assert.Equal(3, copy.Count);
        Assert.False(copy.IsEmpty);
        Assert.Equal(tools, copy);
        Assert.Equal(tools, copy.ToArray());
        Assert.Equal(["c", "a", "b"], copy.PrimitiveNames);
        Assert.Same(tools[1], copy["a"]);
        Assert.True(copy.TryGetPrimitive("b", out var b));
        Assert.Same(tools[2], b);
        Assert.Contains(tools[0], copy);
        Assert.False(copy.TryGetPrimitive("missing", out _));
        Assert.Throws<KeyNotFoundException>(() => copy["missing"]);

        var array = new McpServerTool[4];
        copy.CopyTo(array, 1);
        Assert.Null(array[0]);
        Assert.Equal(tools, array.Skip(1));
    }

    [Fact]
    public void Copy_OfEmptyCollection_IsEmpty()
    {
        var copy = new McpServerPrimitiveCollection<McpServerTool>(new McpServerPrimitiveCollection<McpServerTool>());

        Assert.True(copy.IsEmpty);
        Assert.Empty(copy);
        Assert.True(copy.TryAdd(CreateTool("tool1")));
        Assert.Single(copy);
    }

    [Fact]
    public void Copy_MutatingCopy_DoesNotAffectSourceOrOtherCopies()
    {
        var source = new McpServerPrimitiveCollection<McpServerTool>();
        var shared = CreateTool("shared");
        source.Add(shared);
        source.Add(CreateTool("other"));

        int sourceChanges = 0;
        source.Changed += (_, _) => sourceChanges++;

        var copy1 = new McpServerPrimitiveCollection<McpServerTool>(source);
        var copy2 = new McpServerPrimitiveCollection<McpServerTool>(source);
        int copy1Changes = 0;
        copy1.Changed += (_, _) => copy1Changes++;

        Assert.True(copy1.Remove(shared));
        Assert.True(copy1.TryAdd(CreateTool("added")));

        Assert.Equal(["other", "added"], copy1.PrimitiveNames);
        Assert.Equal(2, copy1Changes);
        Assert.Equal(["shared", "other"], source.PrimitiveNames);
        Assert.Equal(["shared", "other"], copy2.PrimitiveNames);
        Assert.Same(shared, copy2["shared"]);
        Assert.Equal(0, sourceChanges);
    }

    [Fact]
    public void Copy_MutatingSource_DoesNotAffectExistingCopies_AndLaterCopiesSeeTheMutation()
    {
        var source = new McpServerPrimitiveCollection<McpServerTool>();
        source.Add(CreateTool("tool1"));

        var before = new McpServerPrimitiveCollection<McpServerTool>(source);
        source.Add(CreateTool("tool2"));
        var after = new McpServerPrimitiveCollection<McpServerTool>(source);
        source.Clear();

        Assert.Equal(["tool1"], before.PrimitiveNames);
        Assert.Equal(["tool1", "tool2"], after.PrimitiveNames);
        Assert.Empty(source);
    }

    [Fact]
    public void Copy_ClearCopy_FiresChangedAndLeavesSourceIntact()
    {
        var source = new McpServerPrimitiveCollection<McpServerTool>();
        source.Add(CreateTool("tool1"));
        var copy = new McpServerPrimitiveCollection<McpServerTool>(source);
        int changeCount = 0;
        copy.Changed += (_, _) => changeCount++;

        copy.Clear();

        Assert.Equal(1, changeCount);
        Assert.Empty(copy);
        Assert.Single(source);
        Assert.True(copy.TryAdd(CreateTool("tool1")));
        Assert.Single(copy);
    }

    [Fact]
    public void Copy_DuplicateTryAddAndFailedRemove_DoNotFireChanged()
    {
        var source = new McpServerPrimitiveCollection<McpServerTool>();
        source.Add(CreateTool("tool1"));
        var copy = new McpServerPrimitiveCollection<McpServerTool>(source);
        int changeCount = 0;
        copy.Changed += (_, _) => changeCount++;

        Assert.False(copy.TryAdd(CreateTool("tool1")));
        Assert.False(copy.Remove(CreateTool("tool1")));
        Assert.False(copy.Remove(CreateTool("missing")));

        Assert.Equal(0, changeCount);
        Assert.Single(copy);
    }

    [Fact]
    public void Copy_DeferChangedEvents_CoalescesMutationsOfCopy()
    {
        var source = new McpServerPrimitiveCollection<McpServerTool>();
        source.Add(CreateTool("tool1"));
        var copy = new McpServerPrimitiveCollection<McpServerTool>(source);
        int changeCount = 0;
        copy.Changed += (_, _) => changeCount++;

        using (copy.DeferChangedEvents())
        {
            copy.Add(CreateTool("tool2"));
            copy.Remove(copy["tool1"]);
            Assert.Equal(0, changeCount);
        }

        Assert.Equal(1, changeCount);
        Assert.Equal(["tool2"], copy.PrimitiveNames);
    }

    [Fact]
    public void Copy_OfCopy_IsIndependent()
    {
        var source = new McpServerPrimitiveCollection<McpServerTool>();
        source.Add(CreateTool("tool1"));
        var copy = new McpServerPrimitiveCollection<McpServerTool>(source);
        var copyOfCopy = new McpServerPrimitiveCollection<McpServerTool>(copy);

        copy.Add(CreateTool("tool2"));
        copyOfCopy.Add(CreateTool("tool3"));

        Assert.Equal(["tool1"], source.PrimitiveNames);
        Assert.Equal(["tool1", "tool2"], copy.PrimitiveNames);
        Assert.Equal(["tool1", "tool3"], copyOfCopy.PrimitiveNames);
    }

    [Fact]
    public void Copy_PreservesNamesPrimitivesWereAddedUnder()
    {
        var source = new McpServerPrimitiveCollection<McpServerTool>();
        var tool = CreateTool("original");
        source.Add(tool);
        tool.ProtocolTool.Name = "renamed";

        var copy = new McpServerPrimitiveCollection<McpServerTool>(source);

        Assert.Equal(["original"], copy.PrimitiveNames);
        Assert.Same(tool, copy["original"]);
    }

    [Fact]
    public void Copy_UsesSourceKeyComparer()
    {
        var source = new McpServerPrimitiveCollection<McpServerTool>(StringComparer.OrdinalIgnoreCase);
        var tool = CreateTool("Tool");
        source.Add(tool);

        var copy = new McpServerPrimitiveCollection<McpServerTool>(source);

        Assert.Same(tool, copy["TOOL"]);
        Assert.False(copy.TryAdd(CreateTool("tool")));
        Assert.True(copy.Remove(tool));
        Assert.True(copy.TryAdd(CreateTool("tool")));
    }

    [Fact]
    public void Copy_ResourceCollection_PreservesUriEquality()
    {
        var source = new McpServerResourceCollection();
        var resource = McpServerResource.Create(() => "content", new() { UriTemplate = "https://example.com/resource", Name = "resource" });
        var template = McpServerResource.Create((string id) => id, new() { UriTemplate = "test://items/{id}", Name = "template" });
        source.Add(resource);
        source.Add(template);

        var copy = new McpServerResourceCollection(source);

        Assert.True(copy.TryGetPrimitive("HTTPS://EXAMPLE.COM/resource", out var found));
        Assert.Same(resource, found);
        Assert.Same(template, copy["test://items/{id}"]);
        Assert.False(copy.TryGetPrimitive("test://ITEMS/{id}", out _));
        Assert.False(copy.TryAdd(McpServerResource.Create(() => "other", new() { UriTemplate = "https://EXAMPLE.com/resource", Name = "dup" })));

        Assert.True(copy.Remove(resource));
        Assert.False(copy.TryGetPrimitive("https://example.com/resource", out _));
        Assert.Same(resource, source["https://example.com/resource"]);
    }

    [Fact]
    public async Task Copy_ConcurrentMutationOfCopies_IsIsolated()
    {
        var source = new McpServerPrimitiveCollection<McpServerTool>();
        for (int i = 0; i < 20; i++)
        {
            source.Add(CreateTool($"tool{i}"));
        }

        var tasks = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (int iteration = 0; iteration < 50; iteration++)
            {
                var copy = new McpServerPrimitiveCollection<McpServerTool>(source);
                Assert.True(copy.Remove(copy[$"tool{iteration % 20}"]));
                Assert.True(copy.TryAdd(CreateTool($"worker{worker}")));
                Assert.Equal(20, copy.Count);
                Assert.Equal(20, copy.ToArray().Length);
                Assert.False(copy.TryGetPrimitive($"tool{iteration % 20}", out _));
            }
        }, TestContext.Current.CancellationToken));

        await Task.WhenAll(tasks);

        Assert.Equal(Enumerable.Range(0, 20).Select(i => $"tool{i}"), source.PrimitiveNames);
    }

    [Fact]
    public async Task Copy_ConcurrentReadsDuringFirstMutation_SeeConsistentContents()
    {
        var source = new McpServerPrimitiveCollection<McpServerTool>();
        for (int i = 0; i < 50; i++)
        {
            source.Add(CreateTool($"tool{i}"));
        }

        for (int iteration = 0; iteration < 20; iteration++)
        {
            var copy = new McpServerPrimitiveCollection<McpServerTool>(source);
            using var start = new ManualResetEventSlim();
            var readers = Enumerable.Range(0, 4).Select(reader => Task.Run(() =>
            {
                start.Wait(TestContext.Current.CancellationToken);
                for (int i = 0; i < 200; i++)
                {
                    Assert.True(copy.TryGetPrimitive("tool0", out _));
                    int count = copy.ToArray().Length;
                    Assert.InRange(count, 50, 51);
                }
            }, TestContext.Current.CancellationToken)).ToArray();

            start.Set();
            copy.Add(CreateTool("added"));
            await Task.WhenAll(readers);

            Assert.Equal(51, copy.Count);
        }
    }

    /// <summary>A primitive whose <see cref="Equals(object?)"/> runs <see cref="OnEquals"/> once before comparing by reference.</summary>
    private sealed class ReentrantEqualityPrimitive(string id) : IMcpServerPrimitive
    {
        public string Id => id;

        public IReadOnlyList<object> Metadata => [];

        public Action? OnEquals { get; set; }

        public override bool Equals(object? obj)
        {
            Action? onEquals = OnEquals;
            OnEquals = null;
            onEquals?.Invoke();
            return ReferenceEquals(this, obj);
        }

        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    /// <summary>A comparer that runs <see cref="BeforeFirstInsertion"/> once, the first time it hashes <c>"first"</c>.</summary>
    private sealed class InsertionInterleavingComparer : IEqualityComparer<string>
    {
        private bool _callbackInvoked;

        public Action? BeforeFirstInsertion { get; set; }

        public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y);

        public int GetHashCode(string value)
        {
            if (value == "first" && !_callbackInvoked)
            {
                _callbackInvoked = true;
                BeforeFirstInsertion?.Invoke();
            }

            return StringComparer.Ordinal.GetHashCode(value);
        }
    }

    /// <summary>A comparer that blocks the first time it hashes <c>stallKey</c>, until <see cref="Release"/> is set.</summary>
    private sealed class StallingComparer(string stallKey, CancellationToken cancellationToken) : IEqualityComparer<string>, IDisposable
    {
        private int _hasStalled;

        public ManualResetEventSlim Stalled { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y);

        public int GetHashCode(string obj)
        {
            if (obj == stallKey && Interlocked.Exchange(ref _hasStalled, 1) == 0)
            {
                Stalled.Set();
                if (!Release.Wait(TestConstants.DefaultTimeout, cancellationToken))
                {
                    throw new TimeoutException($"The stalled add of '{stallKey}' was never released.");
                }
            }

            return StringComparer.Ordinal.GetHashCode(obj);
        }

        public void Dispose()
        {
            Stalled.Dispose();
            Release.Dispose();
        }
    }
}
