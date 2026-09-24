using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hst.Imager.Core.Models;
using Hst.Imager.Core.PhysicalDrives;
using Xunit;

namespace Hst.Imager.Core.Tests;

public class GivenMacOsRawDiskStream
{
    [Theory]
    [InlineData(512, 0, 1024)]
    [InlineData(512, 1, 1)]
    [InlineData(512, 511, 1026)]
    [InlineData(512, 513, 1024)]
    [InlineData(4096, 0, 8192)]
    [InlineData(4096, 511, 8194)]
    [InlineData(4096, 4095, 2)]
    public void WritesAndReadsPreserveNeighboringBytes(int sectorSize, int start, int count)
    {
        var expected = Data(sectorSize * 4);
        using var disk = new AlignedDisk((byte[])expected.Clone(), sectorSize);
        using var stream = new MacOsRawDiskStream(disk, expected.Length, sectorSize);
        var input = Data(count + 13);
        Array.Copy(input, 7, expected, start, count);

        stream.Position = start;
        stream.Write(input, 7, count);
        Assert.Equal(start + count, stream.Position);
        Assert.Equal(expected, disk.ToArray());
        stream.Position = start;
        var output = new byte[count + 11];
        Assert.Equal(count, stream.Read(output, 5, count));
        Assert.Equal(input.Skip(7).Take(count), output.Skip(5).Take(count));
        Assert.All(output.Take(5).Concat(output.Skip(count + 5)), b => Assert.Equal(0, b));
    }

    [Fact]
    public void AlignedBulkWriteDoesNotReadOrSplitTheBuffer()
    {
        using var disk = new AlignedDisk(new byte[2 * 1024 * 1024], 512);
        using var stream = new MacOsRawDiskStream(disk, disk.Capacity, 512);
        stream.Write(Data(1024 * 1024), 0, 1024 * 1024);
        Assert.Equal(new[] { "write:0:1048576" }, disk.Operations);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(4096)]
    public void ShortAlignedReadsAreCompleted(int sectorSize)
    {
        var expected = Data(sectorSize * 4);
        using var disk = new AlignedDisk(expected, sectorSize) { MaxRead = sectorSize };
        using var stream = new MacOsRawDiskStream(disk, expected.Length, sectorSize);
        var actual = new byte[expected.Length];
        Assert.Equal(expected.Length, stream.Read(actual, 0, actual.Length));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void IncompleteSectorReadFailsBeforeMetadataWrite(int returnedBytes)
    {
        var expected = Data(2048);
        using var disk = new AlignedDisk((byte[])expected.Clone(), 512) { MaxRead = returnedBytes };
        using var stream = new MacOsRawDiskStream(disk, expected.Length, 512);
        stream.Position = 42;
        Assert.Throws<IOException>(() => stream.WriteByte(99));
        Assert.Equal(expected, disk.ToArray());
        Assert.Equal(42, stream.Position);
    }

    [Fact]
    public void SeekAndEofUseReportedSizeInsteadOfDeviceStatLength()
    {
        using var disk = new AlignedDisk(Data(2048), 512);
        using var stream = new MacOsRawDiskStream(disk, 2048, 512);
        Assert.Equal(0, disk.Length);
        Assert.Equal(2048, stream.Length);
        Assert.Equal(2047, stream.Seek(-1, SeekOrigin.End));
        var buffer = new byte[4];
        Assert.Equal(1, stream.Read(buffer, 1, 3));
        Assert.Equal(0, stream.Read(buffer, 0, 4));
        Assert.Equal(2046, stream.Seek(-2, SeekOrigin.Current));
        stream.Write(buffer, 0, 2);
        var before = disk.ToArray();
        stream.Position = 2047;
        Assert.Throws<IOException>(() => stream.Write(buffer, 0, 2));
        Assert.Equal(before, disk.ToArray());
        Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        stream.Position = 4096;
        Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));
        Assert.Throws<IOException>(() => stream.WriteByte(1));
    }

    [Fact]
    public void ZeroLengthOperationsDoNotTouchTheDevice()
    {
        using var disk = new AlignedDisk(Data(1024), 512);
        using var stream = new MacOsRawDiskStream(disk, 1024, 512);
        stream.Position = 13;
        stream.Write(Array.Empty<byte>(), 0, 0);
        Assert.Equal(0, stream.Read(Array.Empty<byte>(), 0, 0));
        Assert.Empty(disk.Operations);
        Assert.Throws<ArgumentNullException>(() => stream.Read(null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Write(new byte[1], -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(new byte[1], 0, 2));
    }

    [Fact]
    public void ReadOnlyDeviceRejectsWrites()
    {
        using var disk = new MemoryStream(Data(1024), writable: false);
        using var stream = new MacOsRawDiskStream(disk, 1024, 512);
        Assert.True(stream.CanRead);
        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.WriteByte(1));
        Assert.NotEqual(-1, stream.ReadByte());
    }

    [Theory]
    [InlineData(0, 512)]
    [InlineData(513, 512)]
    [InlineData(1024, 0)]
    public void InvalidGeometryIsRejected(long size, int sectorSize)
    {
        using var disk = new MemoryStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacOsRawDiskStream(disk, size, sectorSize));
    }

    [Fact]
    public void FailedPartialWriteCanBeRetriedFromTheRequestedPosition()
    {
        var data = Data(2048);
        using var disk = new AlignedDisk(new byte[4096], 512) { FailWriteAfter = 512 };
        using var stream = new MacOsRawDiskStream(disk, 4096, 512);
        stream.Position = 512;
        Assert.Throws<IOException>(() => stream.Write(data, 0, data.Length));
        Assert.Equal(512, stream.Position);
        disk.FailWriteAfter = null;
        stream.Write(data, 0, data.Length);
        Assert.Equal(data, disk.ToArray().Skip(512).Take(data.Length));
        Assert.All(disk.ToArray().Take(512).Concat(disk.ToArray().Skip(2560)), b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposeClosesTheDeviceEvenIfFlushFails(bool failFlush)
    {
        var disk = new AlignedDisk(Data(1024), 512) { FailFlush = failFlush };
        var stream = new MacOsRawDiskStream(disk, 1024, 512);
        if (failFlush)
            Assert.Throws<IOException>(() => stream.Dispose());
        else
            stream.Dispose();
        stream.Dispose();
        Assert.Equal(1, disk.DisposeCount);
        Assert.Equal(1, disk.FlushCount);
        Assert.False(stream.CanRead);
        Assert.False(stream.CanWrite);
        Assert.False(stream.CanSeek);
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => stream.WriteByte(1));
        Assert.Throws<ObjectDisposedException>(() => stream.Flush());
    }

    [Fact]
    public void MediaClosesBeforeRemountAndPreservesFlushErrors()
    {
        var disk = new AlignedDisk(Data(1024), 512) { FailFlush = true };
        var media = new TestMedia(disk);
        Assert.Throws<IOException>(() => media.Dispose());
        media.Dispose();
        Assert.Equal(1, disk.DisposeCount);
        Assert.Equal(1, media.MountCount);
    }

    [Theory]
    [InlineData("/dev/disk8", "/dev/rdisk8")]
    [InlineData("/dev/disk12s3", "/dev/rdisk12s3")]
    public void RawPathChangesOnlyTheDevicePrefix(string path, string expected)
    {
        Assert.Equal(expected, MacOsPhysicalDrive.GetRawDevicePath(path));
    }

    [Theory]
    [InlineData("/tmp/disk8")]
    [InlineData("/dev/rdisk8")]
    [InlineData("/dev/disk8\n")]
    [InlineData("/dev/disk8;echo")]
    [InlineData(null)]
    public void InvalidDevicePathsAreRejected(string path)
    {
        Assert.Throws<ArgumentException>(() => MacOsPhysicalDrive.GetRawDevicePath(path));
    }

    [Fact]
    public void SystemDiskIsRejectedBeforeAnyDeviceAccess()
    {
        using var disk = new MacOsPhysicalDrive("/dev/disk0", "disk", "system", 4096, false, true,
            Array.Empty<string>());
        Assert.Throws<IOException>(() => disk.Open(false, CacheType.Disk, 512));
    }

    [Fact]
    public async Task CopyStillVerifiesEachBufferBeforeWritingTheNext()
    {
        var expected = Data(2048);
        using var source = new MemoryStream(expected);
        using var disk = new AlignedDisk(new byte[2048], 512);
        using var stream = new MacOsRawDiskStream(disk, 2048, 512);
        using var copier = new StreamCopier(bufferSize: 1024, verify: true);
        var result = await copier.Copy(CancellationToken.None, source, stream, expected.Length);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, disk.ToArray());
        Assert.Equal(new[] { "write:0:1024", "read:0:1024", "write:1024:1024", "read:1024:1024" },
            disk.Operations);
    }

    [Fact]
    public async Task CancelledCopyDoesNotWrite()
    {
        using var source = new MemoryStream(Data(1024));
        using var disk = new AlignedDisk(new byte[1024], 512);
        using var stream = new MacOsRawDiskStream(disk, 1024, 512);
        using var copier = new StreamCopier(verify: true);
        var result = await copier.Copy(new CancellationToken(true), source, stream, 1024);
        Assert.True(result.IsFaulted);
        Assert.Empty(disk.Operations);
    }

    [Fact]
    public async Task UnbufferedFileSupportsAsyncMetadataAndBulkWrites()
    {
        var path = Path.GetTempFileName();
        try
        {
            var expected = Data(8192);
            await File.WriteAllBytesAsync(path, expected);
            using (var stream = new MacOsRawDiskStream(new FileStream(path, FileMode.Open,
                       FileAccess.ReadWrite, FileShare.None, 1), expected.Length, 512))
            {
                stream.Position = 511;
                var data = Data(4098);
                await stream.WriteAsync(data.AsMemory());
                Array.Copy(data, 0, expected, 511, data.Length);
                stream.Position = 0;
                var actual = new byte[expected.Length];
                Assert.Equal(actual.Length, await stream.ReadAsync(actual.AsMemory()));
                Assert.Equal(expected, actual);
                await stream.FlushAsync();
            }
            Assert.Equal(expected, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] Data(int length) => Enumerable.Range(0, length).Select(i => (byte)(i % 251 + 1)).ToArray();

    [Fact]
    public void DeviceFlushReportsAnUnsupportedIoctl()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        var path = Path.GetTempFileName();
        try
        {
            using var stream = new MacOsDeviceStream(path, writable: true);
            // an ordinary file must not report success for a disk-only request.
            var error = Assert.Throws<IOException>(() => stream.Flush(flushToDisk: true));
            Assert.IsType<System.ComponentModel.Win32Exception>(error.InnerException);
            Assert.Contains("errno 25", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadOnlyDeviceFlushDoesNotIssueAnIoctl()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var stream = new MacOsDeviceStream(path, writable: false);
            stream.Flush();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class AlignedDisk(byte[] data, int sectorSize) : MemoryStream(data)
    {
        public List<string> Operations { get; } = new();
        public int MaxRead { get; set; } = int.MaxValue;
        public int? FailWriteAfter { get; set; }
        public bool FailFlush { get; set; }
        public int FlushCount { get; private set; }
        public int DisposeCount { get; private set; }
        public override long Length => 0;

        public override long Seek(long offset, SeekOrigin origin)
        {
            Assert.Equal(SeekOrigin.Begin, origin);
            Assert.Equal(0, offset % sectorSize);
            return base.Seek(offset, origin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Assert.Equal(0, Position % sectorSize);
            Assert.Equal(0, count % sectorSize);
            Operations.Add($"read:{Position}:{count}");
            return base.Read(buffer, offset, Math.Min(count, MaxRead));
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Assert.Equal(0, Position % sectorSize);
            Assert.Equal(0, count % sectorSize);
            Operations.Add($"write:{Position}:{count}");
            if (FailWriteAfter.HasValue)
            {
                base.Write(buffer, offset, FailWriteAfter.Value);
                throw new IOException("Write failed after a partial transfer");
            }
            base.Write(buffer, offset, count);
        }

        public override void Flush()
        {
            FlushCount++;
            if (FailFlush)
                throw new IOException("Flush failed");
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
        }
    }

    private sealed class TestMedia(AlignedDisk disk) : MacOsMediaStream(disk, "/dev/disk999", 1024)
    {
        public int MountCount { get; private set; }
        protected override void MountDisk()
        {
            Assert.Equal(1, disk.DisposeCount);
            MountCount++;
        }
    }
}
