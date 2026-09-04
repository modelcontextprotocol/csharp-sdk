#if NET
namespace ModelContextProtocol.Client;

/// <summary>Combines separate read and write streams into one duplex stream.</summary>
internal sealed class HttpOverStdioDuplexStream(Stream readStream, Stream writeStream) : Stream
{
    private int _disposed;

    public override bool CanRead => Volatile.Read(ref _disposed) == 0 && readStream.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => Volatile.Read(ref _disposed) == 0 && writeStream.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => writeStream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        writeStream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        readStream.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => readStream.Read(buffer);

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        readStream.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        readStream.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        writeStream.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => writeStream.Write(buffer);

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        writeStream.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        writeStream.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            writeStream.Dispose();
            readStream.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await writeStream.DisposeAsync().ConfigureAwait(false);
            await readStream.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
#endif
