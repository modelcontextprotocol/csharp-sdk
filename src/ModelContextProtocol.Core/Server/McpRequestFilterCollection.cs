using System.Collections;

namespace ModelContextProtocol.Server;

internal sealed class McpRequestFilterCollection<T>(IList<T> filters, Action validateInsertion) : IList<T>
{
    public T this[int index]
    {
        get => filters[index];
        set => filters[index] = value;
    }

    public int Count => filters.Count;

    public bool IsReadOnly => filters.IsReadOnly;

    public void Add(T item)
    {
        validateInsertion();
        filters.Add(item);
    }

    public void Clear() => filters.Clear();

    public bool Contains(T item) => filters.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => filters.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => filters.GetEnumerator();

    public int IndexOf(T item) => filters.IndexOf(item);

    public void Insert(int index, T item) => filters.Insert(index, item);

    public bool Remove(T item) => filters.Remove(item);

    public void RemoveAt(int index) => filters.RemoveAt(index);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
