using ModelContextProtocol.Protocol;
using System.Diagnostics;

namespace ModelContextProtocol;

/// <summary>Provides thread-safe storage for notification handlers.</summary>
internal sealed class NotificationHandlers
{
    /// <summary>A dictionary of linked lists of registrations, indexed by the notification method.</summary>
    private readonly Dictionary<string, Registration> _handlers = [];

    /// <summary>Gets the object to be used for all synchronization.</summary>
    private object SyncObj => _handlers;

    /// <summary>
    /// Registers a collection of notification handlers at once.
    /// </summary>
    /// <param name="handlers">
    /// A collection of notification method names paired with their corresponding handler functions.
    /// Each key in the collection is a notification method name, and each value is a handler function
    /// that will be invoked when a notification with that method name is received.
    /// </param>
    /// <remarks>
    /// <para>
    /// This method is typically used during client or server initialization to register
    /// all notification handlers provided in capabilities.
    /// </para>
    /// <para>
    /// Registrations completed with this method are permanent and non-removable.
    /// This differs from handlers registered with <see cref="Register"/> which can be temporary.
    /// </para>
    /// <para>
    /// When multiple handlers are registered for the same method, all handlers will be invoked
    /// in reverse order of registration (newest first) when a notification is received.
    /// </para>
    /// <para>
    /// The registered handlers will be invoked by <see cref="InvokeHandlers"/> when a notification
    /// with the corresponding method name is received.
    /// </para>
    /// </remarks>
    public void RegisterRange(IEnumerable<KeyValuePair<string, Func<JsonRpcNotification, CancellationToken, ValueTask>>> handlers)
    {
        foreach (var entry in handlers)
        {
            _ = Register(entry.Key, entry.Value, temporary: false);
        }
    }

    /// <summary>
    /// Adds a notification handler as part of configuring the endpoint.
    /// </summary>
    /// <param name="method">The notification method for which the handler is being registered.</param>
    /// <param name="handler">The handler being registered.</param>
    /// <param name="temporary">
    /// <see langword="true"/> if the registration can be removed later; <see langword="false"/> if it cannot.
    /// If <see langword="false"/>, the registration will be permanent: calling <see cref="IAsyncDisposable.DisposeAsync"/>
    /// on the returned instance will not unregister the handler.
    /// </param>
    /// <returns>
    /// An <see cref="IAsyncDisposable"/> that when disposed will unregister the handler if <paramref name="temporary"/> is <see langword="true"/>.
    /// </returns>
    /// <remarks>
    /// Multiple handlers can be registered for the same method. When a notification for that method is received,
    /// all registered handlers will be invoked in reverse order of registration (newest first).
    /// </remarks>
    public IAsyncDisposable Register(
        string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler, bool temporary = true)
    {
        // Create the new registration instance.
        Registration reg = new(this, method, handler, temporary);

        // Store the registration into the dictionary. If there's not currently a registration for the method,
        // then this registration instance just becomes the single value. If there is currently a registration,
        // then this new registration becomes the new head of the linked list, and the old head becomes the next
        // item in the list.
        lock (SyncObj)
        {
            if (_handlers.TryGetValue(method, out var existingHandlerHead))
            {
                reg.Next = existingHandlerHead;
                existingHandlerHead.Prev = reg;
            }

            _handlers[method] = reg;
        }

        // Return the new registration. It must be disposed of when no longer used, or it will end up being
        // leaked into the list. This is the same as with CancellationToken.Register.
        return reg;
    }

    /// <summary>
    /// Invokes all registered handlers for the specified notification method.
    /// </summary>
    /// <param name="method">The notification method name to invoke handlers for.</param>
    /// <param name="notification">The notification object to pass to each handler.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <remarks>
    /// Handlers are invoked in reverse order of registration (newest first).
    /// If any handler throws an exception, all handlers will still be invoked, and an <see cref="AggregateException"/> 
    /// containing all exceptions will be thrown after all handlers have been invoked.
    /// </remarks>
    public async Task InvokeHandlers(string method, JsonRpcNotification notification, CancellationToken cancellationToken)
    {
        // If there are no handlers registered for this method, we're done.
        Registration? reg;
        lock (SyncObj)
        {
            if (!_handlers.TryGetValue(method, out reg))
            {
                return;
            }
        }

        // Invoke each handler in the list. We guarantee that we'll try to invoke
        // any handlers that were in the list when the list was fetched from the dictionary,
        // which is why DisposeAsync doesn't modify the Prev/Next of the registration being
        // disposed; if those were nulled out, we'd be unable to walk around it in the list
        // if we happened to be on that item when it was disposed.
        List<Exception>? exceptions = null;
        while (reg is not null)
        {
            try
            {
                await reg.InvokeAsync(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                (exceptions ??= []).Add(e);
            }

            lock (SyncObj)
            {
                reg = reg.Next;
            }
        }

        if (exceptions is not null)
        {
            throw new AggregateException(exceptions);
        }
    }

    /// <summary>Provides storage for a handler registration.</summary>
    private sealed class Registration(
        NotificationHandlers handlers, string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler, bool unregisterable) : IAsyncDisposable
    {
        /// <summary>Used to prevent deadlocks during disposal.</summary>
        /// <remarks>
        /// The task returned from <see cref="DisposeAsync"/> does not complete until all invocations of the handler
        /// have completed and no more will be performed, so that the consumer can then trust that any resources accessed
        /// by that handler are no longer in use and may be cleaned up. If <see cref="DisposeAsync"/> were to be invoked
        /// and its task awaited from within the invocation of the handler, however, that would result in deadlock, since
        /// the task wouldn't complete until the invocation completed, and the invocation wouldn't complete until the task
        /// completed. To circumvent that, we track via an <see cref="AsyncLocal{Int32}"/> in-flight invocations. If
        /// <see cref="DisposeAsync"/> detects it's being invoked from within an invocation, it will avoid waiting. For
        /// simplicity, we don't require that it's the same handler.
        /// </remarks>
        private static readonly AsyncLocal<int> s_invokingAncestor = new();

        /// <summary>The parent <see cref="NotificationHandlers"/> to which this registration belongs.</summary>
        private readonly NotificationHandlers _handlers = handlers;

        /// <summary>The method with which this registration is associated.</summary>
        private readonly string _method = method;
        
        /// <summary>The handler this registration represents.</summary>
        private readonly Func<JsonRpcNotification, CancellationToken, ValueTask> _handler = handler;

        /// <summary>true if this instance is temporary; false if it's permanent</summary>
        private readonly bool _temporary = unregisterable;

        /// <summary>Provides a task that <see cref="DisposeAsync"/> can await to know when all in-flight invocations have completed.</summary>
        /// <remarks>
        /// This will only be initialized if <see cref="DisposeAsync"/> sees in-flight invocations, in which case it'll initialize
        /// this <see cref="TaskCompletionSource{TResult}"/> and then await its task. The task will be completed when the last
        /// in-flight notification completes.
        /// </remarks>
        private TaskCompletionSource<bool>? _disposeTcs;
        
        /// <summary>The number of remaining references to this registration.</summary>
        /// <remarks>
        /// The ref count starts life at 1 to represent the whole registration; that ref count will be subtracted when
        /// the instance is disposed. Every invocation then temporarily increases the ref count before invocation and
        /// decrements it after. When <see cref="DisposeAsync"/> is called, it decrements the ref count. In the common
        /// case, that'll bring the count down to 0, in which case the instance will never be subsequently invoked.
        /// If, however, after that decrement the count is still positive, then there are in-flight invocations; the last
        /// one of those to complete will end up decrementing the ref count to 0.
        /// </remarks>
        private int _refCount = 1;

        /// <summary>Tracks whether <see cref="DisposeAsync"/> has ever been invoked.</summary>
        /// <remarks>
        /// It's rare but possible <see cref="DisposeAsync"/> is called multiple times. Only the first
        /// should decrement the initial ref count, but they all must wait until all invocations have quiesced.
        /// </remarks>
        private bool _disposedCalled = false;

        /// <summary>The next registration in the linked list.</summary>
        public Registration? Next;
        /// <summary>
        /// The previous registration in the linked list of handlers for a specific notification method.
        /// Used to maintain the bidirectional linked list when handlers are added or removed.
        /// </summary>
        public Registration? Prev;

        /// <summary>Removes the registration.</summary>
        public async ValueTask DisposeAsync()
        {
            if (!_temporary)
            {
                return;
            }

            lock (_handlers.SyncObj)
            {
                // If DisposeAsync was previously called, we don't want to do all of the work again
                // to remove the registration from the list, and we must not do the work again to
                // decrement the ref count and possibly initialize the _disposeTcs.
                if (!_disposedCalled)
                {
                    _disposedCalled = true;

                    // If this handler is the head of the list for this method, we need to update
                    // the dictionary, either to point to a different head, or if this is the only
                    // item in the list, to remove the entry from the dictionary entirely.
                    if (_handlers._handlers.TryGetValue(_method, out var handlers) && handlers == this)
                    {
                        if (Next is not null)
                        {
                            _handlers._handlers[_method] = Next;
                        }
                        else
                        {
                            _handlers._handlers.Remove(_method);
                        }
                    }

                    // Remove the registration from the linked list by routing the nodes around it
                    // to point past this one. Importantly, we do not modify this node's Next or Prev.
                    // We want to ensure that an enumeration through all of the registrations can still
                    // progress through this one.
                    Prev?.Next = Next;
                    Next?.Prev = Prev;

                    // Decrement the ref count. In the common case, there's no in-flight invocation for
                    // this handler. However, in the uncommon case that there is, we need to wait for
                    // that invocation to complete. To do that, initialize the _disposeTcs. It's created
                    // with RunContinuationsAsynchronously so that completing it doesn't run the continuation
                    // under any held locks.
                    if (--_refCount != 0)
                    {
                        Debug.Assert(_disposeTcs is null);
                        _disposeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                }
            }

            // Ensure that DisposeAsync doesn't complete until all in-flight invocations have completed,
            // unless our call chain includes one of those in-flight invocations, in which case waiting
            // would deadlock.
            if (_disposeTcs is not null && s_invokingAncestor.Value == 0)
            {
                await _disposeTcs.Task.ConfigureAwait(false);
            }
        }

        /// <summary>Invoke the handler associated with the registration.</summary>
        public ValueTask InvokeAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
        {
            // For permanent registrations, skip all the tracking overhead and just invoke the handler.
            if (!_temporary)
            {
                return _handler(notification, cancellationToken);
            }

            // For temporary registrations, track the invocation and coordinate with disposal.
            return InvokeTemporaryAsync(notification, cancellationToken);
        }

        /// <summary>Invoke the handler associated with the temporary registration.</summary>
        private async ValueTask InvokeTemporaryAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
        {
            // Check whether we need to handle this registration. If DisposeAsync has been called,
            // then even if there are in-flight invocations for it, we avoid adding more.
            // If DisposeAsync has not been called, then we need to increment the ref count to
            // signal that there's another in-flight invocation.
            lock (_handlers.SyncObj)
            {
                Debug.Assert(_refCount != 0 || _disposedCalled, $"Expected {nameof(_disposedCalled)} == true when {nameof(_refCount)} == 0");
                if (_disposedCalled)
                {
                    return;
                }

                Debug.Assert(_refCount > 0);
                _refCount++;
            }

            // Ensure that if DisposeAsync is called from within the handler, it won't deadlock by waiting
            // for the in-flight invocation to complete.
            s_invokingAncestor.Value++;

            try
            {
                // Invoke the handler.
                await _handler(notification, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Undo the in-flight tracking.
                s_invokingAncestor.Value--;

                // Now decrement the ref count we previously incremented. If that brings the ref count to 0,
                // DisposeAsync must have been called while this was in-flight, which also means it's now
                // waiting on _disposeTcs; unblock it.
                lock (_handlers.SyncObj)
                {
                    _refCount--;
                    if (_refCount == 0)
                    {
                        Debug.Assert(_disposedCalled);
                        Debug.Assert(_disposeTcs is not null);
                        bool completed = _disposeTcs!.TrySetResult(true);
                        Debug.Assert(completed);
                    }
                }
            }
        }
    }
}
