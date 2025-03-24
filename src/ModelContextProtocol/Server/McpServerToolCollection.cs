using ModelContextProtocol.Utils;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Server;

/// <summary>Provides a thread-safe collection of <see cref="McpServerTool"/> instances, indexed by their names.</summary>
public sealed class McpServerToolCollection : ICollection<McpServerTool>, IReadOnlyCollection<McpServerTool>
{
    /// <summary>Concurrent dictionary of tools, indexed by their names.</summary>
    private readonly ConcurrentDictionary<string, McpServerTool> _tools = [];

    /// <summary>Occurs when a change is made to the contents of the collection.</summary>
    public event EventHandler? Changed;

    /// <summary>Raises <see cref="Changed"/> if there are registered handlers.</summary>
    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Gets the number of tools in the collection.</summary>
    public int Count => _tools.Count;

    /// <summary>Gets whether there are any tools in the collection.</summary>
    public bool IsEmpty => _tools.IsEmpty;

    /// <summary>Gets the <see cref="McpServerTool"/> with the specified <paramref name="name"/> from the collection.</summary>
    /// <param name="name">The name of the tool to retrieve.</param>
    /// <returns>The <see cref="McpServerTool"/> with the specified name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">A tool with the specified name does not exist in the collection.</exception>
    public McpServerTool this[string name] => _tools[name];

    /// <summary>Clears all tools from the collection.</summary>
    public void Clear()
    {
        _tools.Clear();
        RaiseChanged();
    }

    /// <summary>Adds the specified <see cref="McpServerTool"/> to the collection.</summary>
    /// <param name="tool">The tool to be added.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A tool with the same name as <paramref name="tool"/> already exists in the collection.</exception>
    public void Add(McpServerTool tool)
    {
        Throw.IfNull(tool);

        ((IDictionary<string, McpServerTool>)_tools).Add(tool.AIFunction.Name, tool);

        RaiseChanged();
    }

    /// <summary>Adds all of the tools in <paramref name="tools"/> to the collection.</summary>
    /// <param name="tools">The tools to be added.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tools"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A tool with the same name as one of the specified tools already exists in the collection, or
    /// <paramref name="tools"/> contained multiple tools with the same name.
    /// </exception>
    public void AddRange(IEnumerable<McpServerTool> tools)
    {
        Throw.IfNull(tools);

        foreach (var tool in tools)
        {
            ((IDictionary<string, McpServerTool>)_tools).Add(tool.AIFunction.Name, tool);
        }

        RaiseChanged();
    }

    /// <summary>Adds the specified <see cref="McpServerTool"/> to the collection.</summary>
    /// <param name="tool">The tool to be added.</param>
    /// <returns><see langword="true"/> if the tool was added; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> is <see langword="null"/>.</exception>
    public bool TryAdd(McpServerTool tool)
    {
        Throw.IfNull(tool);

        bool added = _tools.TryAdd(tool.AIFunction.Name, tool);
        if (added)
        {
            RaiseChanged();
        }

        return added;
    }

    /// <summary>Removes the specified toolfrom the collection.</summary>
    /// <param name="tool">The tool to be removed from the collection.</param>
    /// <returns>
    /// <see langword="true"/> if the tool was found in the collection and removed; otherwise, <see langword="false"/> if it couldn't be found.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> is <see langword="null"/>.</exception>
    public bool Remove(McpServerTool tool)
    {
        Throw.IfNull(tool);

        bool removed = ((ICollection<KeyValuePair<string, McpServerTool>>)_tools).Remove(new(tool.AIFunction.Name, tool));
        if (removed)
        {
            RaiseChanged();
        }

        return removed;
    }

    /// <summary>Attempts to get the tool with the specified name from the collection.</summary>
    /// <param name="name">The name of the tool to retrieve.</param>
    /// <param name="tool">The tool, if found; otherwise, <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the tool was found in the collection and return; otherwise, <see langword="false"/> if it couldn't be found.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGetTool(string name, [NotNullWhen(true)] out McpServerTool? tool) =>
        _tools.TryGetValue(name, out tool);

    /// <summary>Checks if a specific tool is present in the collection of tools.</summary>
    /// <param name="tool">The tool to search for in the collection.</param>
    /// <see langword="true"/> if the tool was found in the collection and return; otherwise, <see langword="false"/> if it couldn't be found.
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> is <see langword="null"/>.</exception>
    public bool Contains(McpServerTool tool)
    {
        Throw.IfNull(tool);
        return ((ICollection<KeyValuePair<string, McpServerTool>>)_tools).Contains(new(tool.AIFunction.Name, tool));
    }

    /// <summary>Gets the names of all of the tools in the collection.</summary>
    public ICollection<string> ToolNames => _tools.Keys;

    /// <summary>Creates an array containing all of the tools in the collection.</summary>
    /// <returns>An array containing all of the tools in the collection.</returns>
    public McpServerTool[] ToArray() => _tools.Values.ToArray();

    /// <inheritdoc/>
    public IEnumerator<McpServerTool> GetEnumerator()
    {
        foreach (var entry in _tools)
        {
            yield return entry.Value;
        }
    }

    /// <inheritdoc/>
    IEnumerator<McpServerTool> IEnumerable<McpServerTool>.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    bool ICollection<McpServerTool>.IsReadOnly => false;

    /// <inheritdoc/>
    void ICollection<McpServerTool>.CopyTo(McpServerTool[] array, int arrayIndex)
    {
        Throw.IfNull(array);

        foreach (var entry in _tools)
        {
            array[arrayIndex++] = entry.Value;
        }
    }
}