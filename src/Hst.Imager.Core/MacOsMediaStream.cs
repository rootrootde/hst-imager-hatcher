using System;
using System.IO;
using Hst.Core.Extensions;

namespace Hst.Imager.Core;

public class MacOsMediaStream : MediaStream
{
    private readonly string path;

    private bool isDisposed;

    public MacOsMediaStream(Stream stream, string path, long size) : base(stream, size)
    {
        this.path = path;
    }

    protected virtual void MountDisk() => "diskutil".RunProcess($"mountDisk {path}");
    
    protected override void Dispose(bool disposing)
    {
        if (!disposing || isDisposed)
        {
            return;
        }

        isDisposed = true;
        try
        {
            // dispose the raw stream even when flushing fails.
            base.Dispose(true);
        }
        finally
        {
            try
            {
                Stream.Dispose();
            }
            finally
            {
                try
                {
                    MountDisk();
                }
                catch (Exception)
                {
                    // mounting can fail for disks without a supported filesystem.
                }
            }
        }
    }
}
