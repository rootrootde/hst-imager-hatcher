using System;
using System.IO;

namespace Hst.Imager.Core;

public sealed class MacOsRawDiskStream : Stream
{
    private readonly Stream stream;
    private readonly int sectorSize;
    private readonly byte[] sector;
    private readonly long size;
    private long position;
    private bool disposed;

    public MacOsRawDiskStream(Stream stream, long size, int sectorSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sectorSize);
        if (size <= 0 || size % sectorSize != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Raw disk stream must support reading and seeking", nameof(stream));
        }

        this.stream = stream;
        this.size = size;
        this.sectorSize = sectorSize;
        sector = new byte[sectorSize];
    }

    public override bool CanRead => !disposed && stream.CanRead;
    public override bool CanWrite => !disposed && stream.CanWrite;
    public override bool CanSeek => !disposed;
    public override long Length
    {
        get { ObjectDisposedException.ThrowIf(disposed, this); return size; }
    }
    public override long Position
    {
        get { ObjectDisposedException.ThrowIf(disposed, this); return position; }
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(position + offset),
            SeekOrigin.End => checked(size + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (target < 0)
        {
            throw new IOException("Cannot seek before the start of the disk");
        }
        return position = target;
    }

    private void ReadAligned(byte[] buffer, int offset, int count, long diskOffset)
    {
        stream.Seek(diskOffset, SeekOrigin.Begin);
        while (count > 0)
        {
            var read = stream.Read(buffer, offset, count);
            if (read <= 0 || read > count || read % sectorSize != 0)
            {
                throw new IOException("Incomplete sector read from raw disk");
            }
            offset += read;
            count -= read;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateBufferArguments(buffer, offset, count);
        if (position >= size)
        {
            return 0;
        }
        var remaining = (int)Math.Min(count, size - position);
        var total = remaining;
        while (remaining > 0)
        {
            var withinSector = (int)(position % sectorSize);
            int length;
            if (withinSector == 0 && remaining >= sectorSize)
            {
                length = remaining - remaining % sectorSize;
                ReadAligned(buffer, offset, length, position);
            }
            else
            {
                length = Math.Min(remaining, sectorSize - withinSector);
                ReadAligned(sector, 0, sectorSize, position - withinSector);
                Array.Copy(sector, withinSector, buffer, offset, length);
            }
            position += length;
            offset += length;
            remaining -= length;
        }
        return total;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateBufferArguments(buffer, offset, count);
        if (!CanWrite)
        {
            throw new NotSupportedException("Raw disk is read-only");
        }
        if (position > size || count > size - position)
        {
            throw new IOException("Write exceeds the disk size");
        }
        while (count > 0)
        {
            var withinSector = (int)(position % sectorSize);
            int length;
            if (withinSector == 0 && count >= sectorSize)
            {
                length = count - count % sectorSize;
                stream.Seek(position, SeekOrigin.Begin);
                stream.Write(buffer, offset, length);
            }
            else
            {
                length = Math.Min(count, sectorSize - withinSector);
                var sectorOffset = position - withinSector;
                // metadata writes must preserve both ends of a partial sector.
                ReadAligned(sector, 0, sectorSize, sectorOffset);
                Array.Copy(buffer, offset, sector, withinSector, length);
                stream.Seek(sectorOffset, SeekOrigin.Begin);
                stream.Write(sector, 0, sectorSize);
            }
            position += length;
            offset += length;
            count -= length;
        }
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (stream is FileStream fileStream)
        {
            fileStream.Flush(flushToDisk: true);
        }
        else
        {
            stream.Flush();
        }
    }

    public override void SetLength(long value) => throw new NotSupportedException("Cannot resize a raw disk");

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            try
            {
                if (stream.CanWrite)
                {
                    Flush();
                }
            }
            finally
            {
                disposed = true;
                stream.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
