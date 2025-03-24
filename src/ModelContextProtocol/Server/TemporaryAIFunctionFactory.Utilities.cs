// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ModelContextProtocol.Utils;
using System.Buffers;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Microsoft.Extensions.AI;

internal static partial class TemporaryAIFunctionFactory
{
    /// <summary>
    /// Removes characters from a .NET member name that shouldn't be used in an AI function name.
    /// </summary>
    /// <param name="memberName">The .NET member name that should be sanitized.</param>
    /// <returns>
    /// Replaces non-alphanumeric characters in the identifier with the underscore character.
    /// Primarily intended to remove characters produced by compiler-generated method name mangling.
    /// </returns>
    internal static string SanitizeMemberName(string memberName)
    {
        Throw.IfNull(memberName);
        return InvalidNameCharsRegex().Replace(memberName, "_");
    }

    /// <summary>Regex that flags any character other than ASCII digits or letters or the underscore.</summary>
#if NET
    [GeneratedRegex("[^0-9A-Za-z_]")]
    private static partial Regex InvalidNameCharsRegex();
#else
    private static Regex InvalidNameCharsRegex() => _invalidNameCharsRegex;
    private static readonly Regex _invalidNameCharsRegex = new("[^0-9A-Za-z_]", RegexOptions.Compiled);
#endif

    /// <summary>Invokes the MethodInfo with the specified target object and arguments.</summary>
    private static object? ReflectionInvoke(MethodInfo method, object? target, object?[]? arguments)
    {
#if NET
        return method.Invoke(target, BindingFlags.DoNotWrapExceptions, binder: null, arguments, culture: null);
#else
        try
        {
            return method.Invoke(target, BindingFlags.Default, binder: null, arguments, culture: null);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            // If we're targeting .NET Framework, such that BindingFlags.DoNotWrapExceptions
            // is ignored, the original exception will be wrapped in a TargetInvocationException.
            // Unwrap it and throw that original exception, maintaining its stack information.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
#endif
    }

    /// <summary>
    /// Implements a simple write-only memory stream that uses pooled buffers.
    /// </summary>
    private sealed class PooledMemoryStream : Stream
    {
        private const int DefaultBufferSize = 4096;
        private byte[] _buffer;
        private int _position;

        public PooledMemoryStream(int initialCapacity = DefaultBufferSize)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            _position = 0;
        }

        public ReadOnlySpan<byte> GetBuffer() => _buffer.AsSpan(0, _position);
        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override long Length => _position;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureNotDisposed();
            EnsureCapacity(_position + count);

            Buffer.BlockCopy(buffer, offset, _buffer, _position, count);
            _position += count;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (_buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null!;
            }

            base.Dispose(disposing);
        }

        private void EnsureCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= _buffer.Length)
            {
                return;
            }

            int newCapacity = Math.Max(requiredCapacity, _buffer.Length * 2);
            byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
            Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _position);

            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
        }

        private void EnsureNotDisposed()
        {
            if (_buffer is null)
            {
                Throw();
                static void Throw() => throw new ObjectDisposedException(nameof(PooledMemoryStream));
            }
        }
    }
}
