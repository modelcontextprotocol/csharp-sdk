// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using ModelContextProtocol.AspNetCore.Distributed.Abstractions;

namespace ModelContextProtocol.AspNetCore.Distributed;

/// <summary>
/// Source-generated JSON serializer for <see cref="SessionOwnerInfo"/>.
/// Uses the generated <see cref="SerializerContext"/> for AOT-compatible,
/// high-performance serialization without reflection.
/// </summary>
internal sealed class SessionOwnerInfoSerializer : IHybridCacheSerializer<SessionOwnerInfo>
{
    /// <summary>
    /// Deserializes a <see cref="SessionOwnerInfo"/> from a buffer using source-generated JSON.
    /// </summary>
    public SessionOwnerInfo Deserialize(ReadOnlySequence<byte> source)
    {
        var reader = new Utf8JsonReader(source);
        return JsonSerializer.Deserialize(ref reader, SerializerContext.Default.SessionOwnerInfo)
            ?? throw new InvalidOperationException("Failed to deserialize SessionOwnerInfo");
    }

    /// <summary>
    /// Serializes a <see cref="SessionOwnerInfo"/> to a buffer using source-generated JSON.
    /// </summary>
    public void Serialize(SessionOwnerInfo value, IBufferWriter<byte> target)
    {
        using Utf8JsonWriter writer = new(target);
        JsonSerializer.Serialize(writer, value, SerializerContext.Default.SessionOwnerInfo);
    }
}
