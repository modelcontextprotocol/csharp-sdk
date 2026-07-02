// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Text;
using ModelContextProtocol.AspNetCore.Distributed.Abstractions;
using Xunit;

namespace ModelContextProtocol.AspNetCore.Distributed.Tests;

public class SessionOwnerInfoSerializerTests
{
    private readonly SessionOwnerInfoSerializer _serializer;

    public SessionOwnerInfoSerializerTests()
    {
        _serializer = new SessionOwnerInfoSerializer();
    }

    [Fact]
    public void Serialize_ValidSessionOwnerInfo_WritesToBuffer()
    {
        // Arrange
        var sessionOwner = new SessionOwnerInfo
        {
            OwnerId = "test-owner-123",
            Address = "http://localhost:5000",
            ClaimedAt = new DateTimeOffset(2025, 10, 24, 12, 0, 0, TimeSpan.Zero),
        };
        var buffer = new ArrayBufferWriter<byte>();

        // Act
        _serializer.Serialize(sessionOwner, buffer);

        // Assert
        var json = Encoding.UTF8.GetString(buffer.WrittenSpan);
        Assert.True(json.Contains("test-owner-123", StringComparison.Ordinal));
        Assert.True(json.Contains("http://localhost:5000", StringComparison.Ordinal));
        Assert.True(json.Contains("2025-10-24", StringComparison.Ordinal));
    }

    [Fact]
    public void Serialize_SessionOwnerInfoWithNullClaimedAt_WritesToBuffer()
    {
        // Arrange
        var sessionOwner = new SessionOwnerInfo
        {
            OwnerId = "owner-456",
            Address = "https://example.com:8080",
            ClaimedAt = null,
        };
        var buffer = new ArrayBufferWriter<byte>();

        // Act
        _serializer.Serialize(sessionOwner, buffer);

        // Assert
        var json = Encoding.UTF8.GetString(buffer.WrittenSpan);
        Assert.True(json.Contains("owner-456", StringComparison.Ordinal));
        Assert.True(json.Contains("https://example.com:8080", StringComparison.Ordinal));
        // ClaimedAt should not be present due to JsonIgnoreCondition.WhenWritingNull
        Assert.False(json.Contains("claimedAt", StringComparison.Ordinal));
    }

    [Fact]
    public void Deserialize_ValidJson_ReturnsSessionOwnerInfo()
    {
        // Arrange
        var json = """
            {
                "ownerId": "deserialized-owner",
                "address": "http://server:3000",
                "claimedAt": "2025-10-24T15:30:00Z"
            }
            """;
        var bytes = Encoding.UTF8.GetBytes(json);
        var sequence = new ReadOnlySequence<byte>(bytes);

        // Act
        var result = _serializer.Deserialize(sequence);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("deserialized-owner", result.OwnerId);
        Assert.Equal("http://server:3000", result.Address);
        Assert.NotNull(result.ClaimedAt);
        Assert.Equal(
            new DateTimeOffset(2025, 10, 24, 15, 30, 0, TimeSpan.Zero),
            result.ClaimedAt
        );
    }

    [Fact]
    public void Deserialize_JsonWithoutClaimedAt_ReturnsSessionOwnerInfoWithNullClaimedAt()
    {
        // Arrange
        var json = """
            {
                "ownerId": "owner-without-timestamp",
                "address": "http://localhost:9000"
            }
            """;
        var bytes = Encoding.UTF8.GetBytes(json);
        var sequence = new ReadOnlySequence<byte>(bytes);

        // Act
        var result = _serializer.Deserialize(sequence);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("owner-without-timestamp", result.OwnerId);
        Assert.Equal("http://localhost:9000", result.Address);
        Assert.Null(result.ClaimedAt);
    }

    [Fact]
    public void RoundTrip_SerializeAndDeserialize_PreservesData()
    {
        // Arrange
        var original = new SessionOwnerInfo
        {
            OwnerId = "roundtrip-test",
            Address = "https://roundtrip.example.com:443",
            ClaimedAt = DateTimeOffset.UtcNow,
        };
        var buffer = new ArrayBufferWriter<byte>();

        // Act - Serialize
        _serializer.Serialize(original, buffer);

        // Act - Deserialize
        var sequence = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var deserialized = _serializer.Deserialize(sequence);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.OwnerId, deserialized.OwnerId);
        Assert.Equal(original.Address, deserialized.Address);
        Assert.Equal(original.ClaimedAt, deserialized.ClaimedAt);
    }

    [Fact]
    public void RoundTrip_WithNullClaimedAt_PreservesData()
    {
        // Arrange
        var original = new SessionOwnerInfo
        {
            OwnerId = "null-timestamp-test",
            Address = "http://test.local",
            ClaimedAt = null,
        };
        var buffer = new ArrayBufferWriter<byte>();

        // Act - Serialize
        _serializer.Serialize(original, buffer);

        // Act - Deserialize
        var sequence = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var deserialized = _serializer.Deserialize(sequence);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.OwnerId, deserialized.OwnerId);
        Assert.Equal(original.Address, deserialized.Address);
        Assert.Null(deserialized.ClaimedAt);
    }

    [Fact]
    public void Serialize_UsesCamelCaseNaming()
    {
        // Arrange
        var sessionOwner = new SessionOwnerInfo
        {
            OwnerId = "case-test",
            Address = "http://localhost",
            ClaimedAt = DateTimeOffset.UtcNow,
        };
        var buffer = new ArrayBufferWriter<byte>();

        // Act
        _serializer.Serialize(sessionOwner, buffer);

        // Assert
        var json = Encoding.UTF8.GetString(buffer.WrittenSpan);
        // Verify camelCase naming policy is applied
        Assert.True(json.Contains("\"ownerId\"", StringComparison.Ordinal));
        Assert.True(json.Contains("\"address\"", StringComparison.Ordinal));
        Assert.True(json.Contains("\"claimedAt\"", StringComparison.Ordinal));
        // Should not contain PascalCase
        Assert.False(json.Contains("\"OwnerId\"", StringComparison.Ordinal));
        Assert.False(json.Contains("\"Address\"", StringComparison.Ordinal));
        Assert.False(json.Contains("\"ClaimedAt\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Deserialize_NullJson_ThrowsInvalidOperationException()
    {
        // Arrange
        var json = "null";
        var bytes = Encoding.UTF8.GetBytes(json);
        var sequence = new ReadOnlySequence<byte>(bytes);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _serializer.Deserialize(sequence));
    }

    [Fact]
    public void Deserialize_MultiSegmentBuffer_ReturnsSessionOwnerInfo()
    {
        // Arrange
        var json = """
            {
                "ownerId": "multi-segment-test",
                "address": "http://multi.example.com",
                "claimedAt": "2025-10-24T10:00:00Z"
            }
            """;
        var bytes = Encoding.UTF8.GetBytes(json);

        // Create a multi-segment buffer
        var segment1 = new ReadOnlyMemory<byte>(bytes, 0, bytes.Length / 2);
        var segment2 = new ReadOnlyMemory<byte>(
            bytes,
            bytes.Length / 2,
            bytes.Length - bytes.Length / 2
        );
        var sequence = CreateMultiSegmentSequence(segment1, segment2);

        // Act
        var result = _serializer.Deserialize(sequence);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("multi-segment-test", result.OwnerId);
        Assert.Equal("http://multi.example.com", result.Address);
    }

    [Fact]
    public void Serialize_SpecialCharactersInAddress_EncodesCorrectly()
    {
        // Arrange
        var sessionOwner = new SessionOwnerInfo
        {
            OwnerId = "special-chars",
            Address = "http://server/path?query=value&foo=bar",
            ClaimedAt = null,
        };
        var buffer = new ArrayBufferWriter<byte>();

        // Act
        _serializer.Serialize(sessionOwner, buffer);
        var sequence = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var deserialized = _serializer.Deserialize(sequence);

        // Assert
        Assert.Equal(sessionOwner.Address, deserialized.Address);
    }

    private static ReadOnlySequence<byte> CreateMultiSegmentSequence(
        ReadOnlyMemory<byte> segment1,
        ReadOnlyMemory<byte> segment2
    )
    {
        var first = new BufferSegment(segment1);
        var second = first.Append(segment2);
        return new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new BufferSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }
}
