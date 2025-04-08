using ModelContextProtocol.Utils;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Server;

/// <summary>Provides a thread-safe collection of <typeparamref name="T"/> instances, indexed by their names.</summary>
/// <typeparam name="T">Specifies the type of primitive stored in the collection.</typeparam>
public class McpServerPrimitiveCollection<T> : ICollection<T>, IReadOnlyCollection<T>
    where T : IMcpServerPrimitive
{
    /// <summary>Concurrent dictionary of primitives, indexed by their names.</summary>
    private readonly ConcurrentDictionary<string, T> _primitives = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerPrimitiveCollection{T}"/> class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This constructor creates an empty, thread-safe collection for storing MCP server primitives, such as tools and prompts.
    /// The collection allows primitives to be looked up by their unique names and supports change notifications through
    /// the <see cref="Changed"/> event.
    /// </para>
    /// <para>
    /// The collection is internally implemented using <see cref="ConcurrentDictionary{TKey, TValue}"/> to ensure thread safety
    /// for concurrent access scenarios, which is important when primitives may be registered or unregistered dynamically
    /// during server operation.
    /// </para>
    /// <para>
    /// In the Model Context Protocol architecture, this collection is typically used to store available tools and prompts
    /// that can be invoked by AI models during inference.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create a new empty collection of tools
    /// var toolCollection = new McpServerPrimitiveCollection&lt;McpServerTool&gt;();
    /// 
    /// // Add a tool to the collection
    /// toolCollection.Add(new MyCustomTool("myTool"));
    /// 
    /// // Or with C# 12 collection expressions
    /// McpServerPrimitiveCollection&lt;McpServerPrompt&gt; promptCollection = [];
    /// promptCollection.Add(new McpServerPrompt("greeting", "Hello, world!"));
    /// 
    /// // Subscribe to changes in the collection
    /// toolCollection.Changed += (sender, args) => {
    ///     Console.WriteLine("Tool collection has changed!");
    /// };
    /// </code>
    /// </example>
    public McpServerPrimitiveCollection()
    {
    }

    /// <summary>Occurs when the collection of primitives is changed.</summary>
    /// <remarks>
    /// <para>
    /// By default, this event is raised when a primitive is added or removed. However, a derived implementation
    /// may raise this event for other reasons, such as when a primitive is modified.
    /// </para>
    /// <para>
    /// Subscribers to this event can react to changes in the collection, such as updating UI, 
    /// notifying clients of changes via protocol messages, or maintaining cache coherence.
    /// </para>
    /// <para>
    /// In the ModelContextProtocol, this event is typically used by the server to send notifications
    /// to clients when the available tools, prompts or resources have changed.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Subscribe to collection changes
    /// primitiveCollection.Changed += (sender, args) =>
    /// {
    ///     // Send a notification to clients
    ///     _ = SendMessageAsync(new JsonRpcNotification() 
    ///         { Method = NotificationMethods.ToolListChangedNotification });
    /// };
    /// </code>
    /// </example>
    public event EventHandler? Changed;

    /// <summary>Gets the number of primitives in the collection.</summary>
    /// <remarks>
    /// <para>This property returns the total count of primitives currently stored in the collection.</para>
    /// <para>Use this property to determine if the collection is empty or to size arrays or other
    /// data structures that will store copies of the primitives.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Check if the collection has any primitives
    /// if (primitiveCollection.Count > 0)
    /// {
    ///     // Create an array to store all primitives
    ///     var allPrimitives = new MyPrimitive[primitiveCollection.Count];
    ///     
    ///     // Copy all primitives to the array
    ///     primitiveCollection.CopyTo(allPrimitives, 0);
    ///     
    ///     // Process each primitive
    ///     foreach (var primitive in allPrimitives)
    ///     {
    ///         Console.WriteLine($"Processing primitive: {primitive.Name}");
    ///     }
    /// }
    /// </code>
    /// </example>
    public int Count => _primitives.Count;

    /// <summary>Gets a value indicating whether the collection contains no primitives.</summary>
    /// <remarks>
    /// <para>This property returns <see langword="true"/> when the collection is empty (contains no primitives),
    /// and <see langword="false"/> when the collection contains at least one primitive.</para>
    /// <para>This is a convenience property that is equivalent to checking if <see cref="Count"/> equals 0,
    /// but may provide better readability in conditional expressions.</para>
    /// <para>This property is typically used in conditional statements to check if a collection needs
    /// to be processed or to skip unnecessary operations on empty collections.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Check if a collection is empty before processing
    /// if (!primitiveCollection.IsEmpty)
    /// {
    ///     // Process non-empty collection
    ///     foreach (var primitive in primitiveCollection)
    ///     {
    ///         Console.WriteLine($"Processing: {primitive.Name}");
    ///     }
    /// }
    /// 
    /// // Use in conditional initialization
    /// if (toolCollection.IsEmpty)
    /// {
    ///     // Initialize collection with default items
    ///     toolCollection.Add(new DefaultTool());
    /// }
    /// </code>
    /// </example>
    public bool IsEmpty => _primitives.IsEmpty;

    /// <summary>Raises the <see cref="Changed"/> event if there are registered handlers.</summary>
    /// <remarks>
    /// <para>This method should be called whenever the collection of primitives changes in a way that should
    /// be communicated to subscribers. The base implementation calls this method when primitives are 
    /// added, removed, or when the collection is cleared.</para>
    /// <para>Derived classes can call this method when implementing custom operations that modify the collection
    /// to ensure subscribers are notified appropriately.</para>
    /// </remarks>
    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Gets the <typeparamref name="T"/> with the specified <paramref name="name"/> from the collection.</summary>
    /// <param name="name">The name of the primitive to retrieve.</param>
    /// <returns>The <typeparamref name="T"/> with the specified name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">An primitive with the specified name does not exist in the collection.</exception>
    public T this[string name]
    {
        get
        {
            Throw.IfNull(name);
            return _primitives[name];
        }
    }

    /// <summary>Clears all primitives from the collection.</summary>
    /// <remarks>
    /// <para>This method removes all primitives from the collection and raises the <see cref="Changed"/> event.</para>
    /// <para>Any subscribers to the <see cref="Changed"/> event will be notified that the collection has been modified.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Remove all primitives from the collection
    /// primitiveCollection.Clear();
    /// 
    /// // The collection is now empty
    /// Debug.Assert(primitiveCollection.Count == 0);
    /// Debug.Assert(primitiveCollection.IsEmpty);
    /// </code>
    /// </example>
    public virtual void Clear()
    {
        _primitives.Clear();
        RaiseChanged();
    }

    /// <summary>
    /// Adds the specified <typeparamref name="T"/> to the collection.
    /// </summary>
    /// <param name="primitive">The primitive to be added to the collection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="primitive"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A primitive with the same name as <paramref name="primitive"/> already exists in the collection.</exception>
    /// <remarks>
    /// If a primitive with the same name already exists, an <see cref="ArgumentException"/> is thrown.
    /// To add a primitive without throwing an exception when it already exists, use <see cref="TryAdd"/> instead.
    /// </remarks>
    public void Add(T primitive)
    {
        if (!TryAdd(primitive))
        {
            throw new ArgumentException($"A primitive with the same name '{primitive.Name}' already exists in the collection.", nameof(primitive));
        }
    }

    /// <summary>
    /// Adds the specified <typeparamref name="T"/> to the collection if it doesn't already exist.
    /// </summary>
    /// <param name="primitive">The primitive to be added to the collection.</param>
    /// <returns>
    /// <see langword="true"/> if the primitive was added successfully; 
    /// <see langword="false"/> if a primitive with the same name already exists.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="primitive"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Unlike <see cref="Add"/>, this method doesn't throw an exception if a primitive with the same name already exists.
    /// If the primitive was added successfully, the <see cref="Changed"/> event is raised.
    /// </remarks>
    public virtual bool TryAdd(T primitive)
    {
        Throw.IfNull(primitive);

        bool added = _primitives.TryAdd(primitive.Name, primitive);
        if (added)
        {
            RaiseChanged();
        }

        return added;
    }

    /// <summary>Removes the specified primitive from the collection.</summary>
    /// <param name="primitive">The primitive to be removed from the collection.</param>
    /// <returns>
    /// <see langword="true"/> if the primitive was found in the collection and removed; otherwise, <see langword="false"/> if it couldn't be found.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="primitive"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>This method removes the specified primitive from the collection if found. The primitive is matched by both name and reference equality.</para>
    /// <para>If the primitive is successfully removed, the <see cref="Changed"/> event is raised to notify subscribers that the collection has been modified.</para>
    /// <para>To remove a primitive by name without having the actual primitive instance, you would need to first retrieve it using <see cref="this[string]"/> or <see cref="TryGetPrimitive"/>.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Remove a primitive from the collection
    /// bool wasRemoved = primitiveCollection.Remove(myPrimitive);
    /// 
    /// // Check if removal was successful
    /// if (wasRemoved)
    /// {
    ///     Console.WriteLine($"Successfully removed primitive: {myPrimitive.Name}");
    /// }
    /// else
    /// {
    ///     Console.WriteLine($"Primitive was not found in the collection");
    /// }
    /// 
    /// // Remove a primitive by name (first retrieving it)
    /// if (primitiveCollection.TryGetPrimitive("myToolName", out var tool))
    /// {
    ///     primitiveCollection.Remove(tool);
    /// }
    /// </code>
    /// </example>
    public virtual bool Remove(T primitive)
    {
        Throw.IfNull(primitive);

        bool removed = ((ICollection<KeyValuePair<string, T>>)_primitives).Remove(new(primitive.Name, primitive));
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
    /// <see langword="true"/> if the primitive was found in the collection and return; otherwise, <see langword="false"/> if it couldn't be found.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public virtual bool TryGetPrimitive(string name, [NotNullWhen(true)] out T? primitive)
    {
        Throw.IfNull(name);
        return _primitives.TryGetValue(name, out primitive);
    }

    /// <summary>Checks if a specific primitive is present in the collection of primitives.</summary>
    /// <param name="primitive">The primitive to search for in the collection.</param>
    /// <returns><see langword="true"/> if the primitive was found in the collection; otherwise, <see langword="false"/> if it couldn't be found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="primitive"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The primitive is searched by both name and reference. Both must match for the method to return true.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Check if a specific primitive exists in the collection
    /// var toolExists = toolCollection.Contains(myTool);
    /// 
    /// // Conditionally perform an operation based on existence
    /// if (!promptCollection.Contains(myPrompt))
    /// {
    ///     promptCollection.Add(myPrompt);
    /// }
    /// </code>
    /// </example>
    public virtual bool Contains(T primitive)
    {
        Throw.IfNull(primitive);
        return ((ICollection<KeyValuePair<string, T>>)_primitives).Contains(new(primitive.Name, primitive));
    }

    /// <summary>Gets the names of all of the primitives in the collection.</summary>
    /// <remarks>
    /// <para>This property returns a collection of strings containing the names of all primitives currently 
    /// stored in the collection.</para>
    /// <para>This collection is useful for enumerating the available primitives by name without 
    /// accessing the actual primitive instances, or for checking if a primitive with a specific name exists.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Check if a primitive with a specific name exists
    /// if (primitiveCollection.PrimitiveNames.Contains("myTool"))
    /// {
    ///     // Get the primitive by name
    ///     var myTool = primitiveCollection["myTool"];
    ///     // Use the primitive
    /// }
    /// 
    /// // Enumerate all primitive names
    /// foreach (string name in primitiveCollection.PrimitiveNames)
    /// {
    ///     Console.WriteLine($"Found primitive: {name}");
    /// }
    /// </code>
    /// </example>
    public virtual ICollection<string> PrimitiveNames => _primitives.Keys;

    /// <summary>Creates an array containing all of the primitives in the collection.</summary>
    /// <returns>An array containing all of the primitives in the collection.</returns>
    public virtual T[] ToArray() => _primitives.Values.ToArray();

    /// <summary>
    /// Copies all primitives from this collection to a specified array, starting at a particular array index.
    /// </summary>
    /// <param name="array">The one-dimensional array that is the destination of the primitives copied from the collection. The array must have zero-based indexing.</param>
    /// <param name="arrayIndex">The zero-based index in <paramref name="array"/> at which copying begins.</param>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="arrayIndex"/> is less than 0.</exception>
    /// <exception cref="ArgumentException">The number of elements in the source collection is greater than the available space from <paramref name="arrayIndex"/> to the end of the destination <paramref name="array"/>.</exception>
    /// <remarks>
    /// <para>This method copies all primitives from the collection into the specified array. The primitives 
    /// are copied in the order they would be returned by the collection's enumerator.</para>
    /// <para>If the collection is modified during the copy operation, the behavior is undefined.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create an array large enough to hold all primitives
    /// var allPrimitives = new MyPrimitive[primitiveCollection.Count];
    /// 
    /// // Copy all primitives into the array starting at index 0
    /// primitiveCollection.CopyTo(allPrimitives, 0);
    /// 
    /// // Process all primitives
    /// foreach (var primitive in allPrimitives)
    /// {
    ///     Console.WriteLine($"Processing primitive: {primitive.Name}");
    /// }
    /// </code>
    /// </example>
    public virtual void CopyTo(T[] array, int arrayIndex)
    {
        Throw.IfNull(array);

        _primitives.Values.CopyTo(array, arrayIndex);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the primitives in the collection.
    /// </summary>
    /// <returns>An <see cref="IEnumerator{T}"/> that can be used to iterate through the primitives.</returns>
    /// <remarks>
    /// <para>The enumerator iterates through the values of the underlying dictionary, returning each primitive in an unspecified order.</para>
    /// <para>This method is used implicitly when iterating through the collection with a foreach loop.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Iterate through all primitives in the collection
    /// foreach (var primitive in primitiveCollection)
    /// {
    ///     Console.WriteLine($"Primitive name: {primitive.Name}");
    ///     // Process each primitive
    /// }
    /// </code>
    /// </example>
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