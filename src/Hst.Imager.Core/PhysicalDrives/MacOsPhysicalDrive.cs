using Hst.Imager.Core.Models;

namespace Hst.Imager.Core.PhysicalDrives
{
    using System.Collections.Generic;
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using Hst.Core.Extensions;

    public class MacOsPhysicalDrive : GenericPhysicalDrive
    {
        public readonly IEnumerable<string> PartitionDevices;
        public int SectorSize { get; }

        public MacOsPhysicalDrive(string path, string type, string name, long size, bool removable,
            bool systemDrive, IEnumerable<string> partitionDevices, int sectorSize = 512)
            : base(path, type, name, size, removable: removable, systemDrive: systemDrive)
        {
            PartitionDevices = partitionDevices;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sectorSize);
            SectorSize = sectorSize;
        }

        public static string GetRawDevicePath(string path)
        {
            if (path == null || !Regex.IsMatch(path, @"\A/dev/disk[0-9]+(?:s[0-9]+)*\z"))
            {
                throw new ArgumentException("Expected a macOS block-device path", nameof(path));
            }
            return "/dev/r" + path.Substring("/dev/".Length);
        }

        public override Stream Open(bool useCache, CacheType cacheType, int blockSize)
        {
            if (SystemDrive)
            {
                throw new IOException($"Access to system drive path '{Path}' is not supported!");
            }
            
            var rawPath = GetRawDevicePath(Path);
            if (Size <= 0 || Size % SectorSize != 0)
            {
                throw new IOException("Disk size must be a positive multiple of its sector size");
            }

            // force is required when the disk has multiple mounted partitions.
            "diskutil".RunProcess($"unmountDisk force {Path}");

            // FileStream buffering would turn sector reads into unaligned device requests.
            var stream = new MacOsDeviceStream(rawPath, Writable);
            try
            {
                return new MacOsMediaStream(new MacOsRawDiskStream(stream, Size, SectorSize), Path, Size);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
    }
}
