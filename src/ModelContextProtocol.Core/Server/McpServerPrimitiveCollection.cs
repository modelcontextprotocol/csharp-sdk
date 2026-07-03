using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Server;

/// <summary>Provides a thread-safe collection of <typeparamref name="T"/> instances, indexed by their names.</summary>
/// <typeparam name="T">The type of primitive stored in the collection.</typeparam>
public class McpServerPrimitiveCollection<T> : ICollection<T>, IReadOnlyCollection<T>
    where T : IMcpServerPrimitive
{
    /// <summary>Concurrent dictionary of primitives, indexed by their names.</summary>
    private readonly ConcurrentDictionary<string, T> _primitives;

    /// <summary>Lock protecting <see cref="_activeDeferralScopes"/> and <see cref="_hasDeferredChangeEvents"/>.</summary>
    private readonly object _deferralLock = new();

    /// <summary>Depth counter for active <see cref="DeferChangedEvents"/> scopes. Positive means notifications are deferred.</summary>
    private int _activeDeferralScopes;

    /// <summary>Whether a change occurred while notifications were deferred.</summary>
    private bool _hasDeferredChangeEvents;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerPrimitiveCollection{T}"/> class.
    /// </summary>
    public McpServerPrimitiveCollection(IEqualityComparer<string>? keyComparer = null)
    {
        _primitives = new(keyComparer ?? EqualityComparer<string>.Default);
    }

    /// <summary>Occurs when the collection is changed.</summary>
    /// <remarks>
    /// By default, this event is raised when a primitive is added or removed. However, a derived implementation
    /// might raise this event for other reasons, such as when a primitive is modified.
    /// </remarks>
    public event EventHandler? Changed;

    /// <summary>Gets the number of primitives in the collection.</summary>
    public int Count => _primitives.Count;

    /// <summary>Gets a value that indicates whether there are any primitives in the collection.</summary>
    public bool IsEmpty => _primitives.IsEmpty;

    /// <summary>
    /// Begins a deferred-change scope. <see cref="Changed"/> notifications are suppressed
    /// until the returned scope is disposed, at which point a single notification is raised
    /// if any mutation occurred during the scope. Multiple scopes may be active simultaneously;
    /// the notification fires once all active scopes have been disposed.
    /// </summary>
    /// <returns>An <see cref="IDisposable"/> that ends the deferral scope when disposed.</returns>
    /// <remarks>
    /// The scope is exception-safe: even if an exception is thrown inside a <c>using</c> block,
    /// the deferral is ended on dispose. If any mutation occurred before the exception, a single
    /// <see cref="Changed"/> notification is raised.
    /// <para>
    /// Mutations from any thread during an open scope are coalesced. A single <see cref="Changed"/>
    /// notification fires on the thread that disposes the last active scope, only if at least one
    /// mutation occurred. All deferral state transitions are guarded by an internal lock, so
    /// concurrent mutations and concurrent scope disposal are both safe. Disposing the same scope
    /// instance more than once is safe and has no additional effect.
    /// </para>
    /// </remarks>
    public IDisposable DeferChangedEvents()
    {
        lock (_deferralLock)
        {
            _activeDeferralScopes++;
        }
        return new ChangeDeferralScope(this);
    }

    /// <summary>Raises <see cref="Changed"/> if there are registered handlers.</summary>
    /// <remarks>
    /// If a <see cref="DeferChangedEvents"/> scope is active, the notification is deferred until all
    /// active scopes are disposed. Derived types that override mutation methods and call
    /// <see cref="RaiseChanged"/> will automatically participate in deferral.
    /// </remarks>
    protected void RaiseChanged()
    {
        lock (_deferralLock)
        {
            if (_activeDeferralScopes > 0)
            {
                _hasDeferredChangeEvents = true;
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EndDeferral()
    {
        bool raise;
        lock (_deferralLock)
        {
            raise = --_activeDeferralScopes == 0 && _hasDeferredChangeEvents;
            if (raise)
            {
                _hasDeferredChangeEvents = false;
            }
        }

        if (raise)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class ChangeDeferralScope : IDisposable
    {
        private McpServerPrimitiveCollection<T>? _collection;

        public ChangeDeferralScope(McpServerPrimitiveCollection<T> collection) =>
            _collection = collection;

        public void Dispose()
        {
            McpServerPrimitiveCollection<T>? collection = Interlocked.Exchange(ref _collection, null);
            collection?.EndDeferral();
        }
    }

    /// <summary>Gets the <typeparamref name="T"/> with the specified <paramref name="name"/> from the collection.</summary>
    /// <param name="name">The name of the primitive to retrieve.</param>
    /// <returns>The <typeparamref name="T"/> with the specified name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">A primitive with the specified name does not exist in the collection.</exception>
    public T this[string name]
    {
        get
        {
            Throw.IfNull(name);
            return _primitives[name];
        }
    }

    /// <summary>Clears all primitives from the collection.</summary>
    public virtual void Clear()
    {
        _primitives.Clear();
        RaiseChanged();
    }

    /// <summary>Adds the specified <typeparamref name="T"/> to the collection.</summary>
    /// <param name="primitive">The primitive to be added.</param>
    /// <exception cref="ArgumentNullException"><paramref name="primitive"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A primitive with the same name as <paramref name="primitive"/> already exists in the collection.</exception>
    public void Add(T primitive)
    {
        if (!TryAdd(primitive))
        {
            throw new ArgumentException($"A primitive with the same name '{primitive.Id}' already exists in the collection.", nameof(primitive));
        }
    }

    /// <summary>Adds the specified <typeparamref name="T"/> to the collection.</summary>
    /// <param name="primitive">The primitive to be added.</param>
    /// <returns><see langword="true"/> if the primitive was added; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="primitive"/> is <see langword="null"/>.</exception>
    public virtual bool TryAdd(T primitive)
    {
        Throw.IfNull(primitive);

        bool added = _primitives.TryAdd(primitive.Id, primitive);
        if (added)
        {
            RaiseChanged();
        }

        return added;
    }

    /// <summary>Removes the specified primitive from the collection.</summary>
    /// <param name="primitive">The primitive to be removed from the collection.</param>
    /// <returns>
    /// <see langword="true"/> if the primitive was found in the collection and removed; <see langword="false"/> if it wasn't found.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="primitive"/> is <see langword="null"/>.</exception>
    public virtual bool Remove(T primitive)
    {
        Throw.IfNull(primitive);

        bool removed = ((ICollection<KeyValuePair<string, T>>)_primitives).Remove(new(primitive.Id, primitive));
        if (removed)
        {
            RaiseChanged();
        }

        return removed;
    }

    /// <summary>Attempts to get the primitive with the specified name from the collection.</summary>
    /// <param name="name">The name of the primitive to retrieve.</param>
    /// <param name="primitive">The primitive, if found; otherwise, <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the primitive was found in the collection and returned; <see langword="false"/> if it wasn't found.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public virtual bool TryGetPrimitive(string name, [NotNullWhen(true)] out T? primitive)
    {
        Throw.IfNull(name);
        return _primitives.TryGetValue(name, out primitive);
    }

    /// <summary>Checks if a specific primitive is present in the collection of primitives.</summary>
    /// <param name="primitive">The primitive to search for in the collection.</param>
    /// <returns><see langword="true"/> if the primitive was found in the collection and returned; <see langword="false"/> if it wasn't found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="primitive"/> is <see langword="null"/>.</exception>
    public virtual bool Contains(T primitive)
    {
        Throw.IfNull(primitive);
        return ((ICollection<KeyValuePair<string, T>>)_primitives).Contains(new(primitive.Id, primitive));
    }

    /// <summary>Gets the names of all of the primitives in the collection.</summary>
    public virtual ICollection<string> PrimitiveNames => _primitives.Keys;

    /// <summary>Creates an array containing all of the primitives in the collection.</summary>
    /// <returns>An array containing all of the primitives in the collection.</returns>
    public virtual T[] ToArray() => _primitives.Values.ToArray();

    /// <inheritdoc/>
    public virtual void CopyTo(T[] array, int arrayIndex)
    {
        Throw.IfNull(array);

        _primitives.Values.CopyTo(array, arrayIndex);
    }

    /// <inheritdoc/>
    public virtual IEnumerator<T> GetEnumerator()
    {
        foreach (var entry in _primitives)
        {
            yield return entry.Value;
        }
    }

    /// <inheritdoc/>
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    bool ICollection<T>.IsReadOnly => false;
}
