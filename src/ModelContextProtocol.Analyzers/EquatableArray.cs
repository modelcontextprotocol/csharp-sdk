// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Immutable;

namespace ModelContextProtocol.Analyzers;

/// <summary>An immutable, equatable array.</summary>
/// <typeparam name="T">The type of values in the array.</typeparam>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    /// <summary>
    /// The underlying <typeparamref name="T"/> array.
    /// </summary>
    private readonly ImmutableArray<T> _array;

    /// <param name="source">The source to enumerate and wrap.</param>
    public EquatableArray(IEnumerable<T> source) => _array = source.ToImmutableArray();

    /// <param name="source">The source to wrap.</param>
    public EquatableArray(ImmutableArray<T> array) => _array = array;

    /// <summary>An empty <see cref="EquatableArray{T}"/>.</summary>
    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    /// <summary>
    /// Gets a reference to an item at a specified position within the array.
    /// </summary>
    /// <param name="index">The index of the item to retrieve a reference to.</param>
    /// <returns>A reference to an item at a specified position within the array.</returns>
    public ref readonly T this[int index] => ref _array.ItemRef(index);

    /// <summary>
    /// Gets a value indicating whether the current array is empty.
    /// </summary>
    public bool IsEmpty => _array.IsEmpty;

    /// <summary>
    /// Gets a value indicating whether the current array is default or empty.
    /// </summary>
    public bool IsDefaultOrEmpty => _array.IsDefaultOrEmpty;

    /// <summary>
    /// Gets the length of the current array.
    /// </summary>
    public int Length => _array.Length;

    /// <inheritdoc/>
    public bool Equals(EquatableArray<T> other) => _array.SequenceEqual(other._array);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EquatableArray<T> array && Equals(array);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (_array.IsDefault)
        {
            return 0;
        }

        int hash = 17;
        foreach (T item in _array)
        {
            hash = hash * 31 + (item?.GetHashCode() ?? 0);
        }

        return hash;
    }

    /// <summary>
    /// Gets an <see cref="ImmutableArray{T}.Enumerator"/> value to traverse items in the current array.
    /// </summary>
    /// <returns>An <see cref="ImmutableArray{T}.Enumerator"/> value to traverse items in the current array.</returns>
    public ImmutableArray<T>.Enumerator GetEnumerator() => _array.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)_array).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_array).GetEnumerator();

    /// <summary>
    /// Implicitly converts an <see cref="ImmutableArray{T}"/> to <see cref="EquatableArray{T}"/>.
    /// </summary>
    /// <returns>An <see cref="EquatableArray{T}"/> instance from a given <see cref="ImmutableArray{T}"/>.</returns>
    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);

    /// <summary>
    /// Implicitly converts an <see cref="EquatableArray{T}"/> to <see cref="ImmutableArray{T}"/>.
    /// </summary>
    /// <returns>An <see cref="ImmutableArray{T}"/> instance from a given <see cref="EquatableArray{T}"/>.</returns>
    public static implicit operator ImmutableArray<T>(EquatableArray<T> array) => array._array;

    /// <summary>
    /// Checks whether two <see cref="EquatableArray{T}"/> values are the same.
    /// </summary>
    /// <param name="left">The first <see cref="EquatableArray{T}"/> value.</param>
    /// <param name="right">The second <see cref="EquatableArray{T}"/> value.</param>
    /// <returns>Whether <paramref name="left"/> and <paramref name="right"/> are equal.</returns>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    /// <summary>
    /// Checks whether two <see cref="EquatableArray{T}"/> values are not the same.
    /// </summary>
    /// <param name="left">The first <see cref="EquatableArray{T}"/> value.</param>
    /// <param name="right">The second <see cref="EquatableArray{T}"/> value.</param>
    /// <returns>Whether <paramref name="left"/> and <paramref name="right"/> are not equal.</returns>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}
