using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Hst.Imager.Core.PhysicalDrives;

public sealed class MacOsDeviceStream : FileStream
{
    // _IO('d', 22) from macOS sys/disk.h.
    private const uint DkIoSynchronizeCache = 0x20006416;
    private const int Interrupted = 4;

    public MacOsDeviceStream(string path, bool writable)
        : base(path, FileMode.Open, writable ? FileAccess.ReadWrite : FileAccess.Read,
            FileShare.None, bufferSize: 1)
    {
    }

    public override void Flush() => Flush(flushToDisk: true);

    public override void Flush(bool flushToDisk)
    {
        base.Flush(flushToDisk: false);
        if (!flushToDisk || !CanWrite)
        {
            return;
        }

        // F_FULLFSYNC is a filesystem request; raw disks need this ioctl.
        while (Ioctl(SafeFileHandle, DkIoSynchronizeCache) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != Interrupted)
            {
                throw new IOException($"Failed to synchronize raw disk '{Name}' (errno {error})",
                    new Win32Exception(error));
            }
        }
    }

    [DllImport("libSystem.B.dylib", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int Ioctl(SafeFileHandle handle, nuint request);
}
