// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;

namespace ModelContextProtocol.Analyzers;

/// <summary>An immutable, equatable array.</summary>
/// <typeparam name="T">The type of values in the array.</typeparam>
internal readonly struct EquatableArray<T> : IEnumerable<T>, IEquatable<EquatableArray<T>>
{
    /// <summary>The underlying <typeparamref name="T"/> array.</summary>
    private readonly T[]? _array;

    /// <param name="source">The source to enumerate and wrap.</param>
    public EquatableArray(IEnumerable<T> source) => _array = source.ToArray();

    /// <param name="array">The source to wrap.</param>
    public EquatableArray(T[] array) => _array = array;

    /// <summary>Gets a reference to an item at a specified position within the array.</summary>
    /// <param name="index">The index of the item to retrieve a reference to.</param>
    /// <returns>A reference to an item at a specified position within the array.</returns>
    public ref readonly T this[int index] => ref NonNullArray[index];

    /// <summary>Gets the backing array.</summary>
    private T[] NonNullArray => _array ?? [];

    /// <summary>Gets the length of the current array.</summary>
    public int Length => NonNullArray.Length;

    /// <inheritdoc/>
    public bool Equals(EquatableArray<T> other) => NonNullArray.SequenceEqual(other.NonNullArray);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>  obj is EquatableArray<T> array && Equals(array);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 17;
        foreach (T item in NonNullArray)
        {
            hash = hash * 31 + (item?.GetHashCode() ?? 0);
        }

        return hash;
    }

    /// <inheritdoc/>
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)NonNullArray).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => NonNullArray.GetEnumerator();
}
