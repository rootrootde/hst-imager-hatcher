# HST Imager for Emu68 Hatcher

This fork carries HST console fixes used by Emu68 Hatcher.

## Starting point

- Upstream: https://github.com/henrikstengaard/hst-imager
- Fork: https://github.com/rootrootde/hst-imager-hatcher
- Base: release 1.6.616, commit a91fa4ca681de082022e648799cfb58d44b39be9
- Local patch branch: hatcher/macos-raw
- origin points to the fork; upstream points to the original project.

The macOS patch is packaged as 1.6.616-hatcher.1. Bounded write benchmarks
and console direct-build checks on EMU68BOOT passed. The release workflow
builds ARM64 and Intel archives from the tagged source.

## Evidence

An Emu68 Hatcher image flash took 3264.8 seconds with verification and
zero-buffer skipping enabled. The image was 59.48 GiB logically; about
27.46 GiB of its 1 MiB buffers contained nonzero data.

A read-only test on the same card and reader measured 20.33 and 20.93 MiB/s
through /dev/disk8, and 79.73 and 88.09 MiB/s through /dev/rdisk8.
These are read measurements, not a measured improvement in flash time.
Device numbers are temporary identifiers and must be checked again before use.

Upstream MacOsPhysicalDrive.Open opens the block-device path directly.
StreamCopier writes and verifies each buffer before advancing; this fork
keeps that order.

## Implementation

MacOsPhysicalDrive opens /dev/rdiskN for data I/O. Its public path remains
/dev/diskN for enumeration, unmounting and mounting. System-drive rejection
runs before unmounting or opening a device.

MacOsRawDiskStream takes the device size and logical sector size from diskutil.
It reports that size even when the device's stat length is zero. Begin, current
and end seeks use a separate logical position. Writes cannot extend the disk.

Aligned transfers go directly to an unbuffered FileStream. Partial-sector
writes first read the sector and preserve neighboring bytes. Short aligned
reads are completed; incomplete sectors fail before a metadata write.
FileStream completes partial native writes or throws. Errors propagate so
StreamCopier can use its existing retry behavior.

MacOsDeviceStream flushes writable raw devices with DKIOCSYNCHRONIZECACHE,
retrying interrupted calls and reporting other failures. FileStream's macOS
Flush(true) uses F_FULLFSYNC, which is a filesystem request. The raw stream
closes even when flushing fails, before MacOsMediaStream attempts a remount.
Read-only streams do not issue the synchronization ioctl.

The existing SectorStream is unchanged: its partial-write padding is not
suitable for preserving adjacent metadata on raw devices. Windows and Linux
retain their existing I/O paths.

Implementation references: [.NET 8 FileStream I/O](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/OSFileStreamStrategy.cs),
[.NET Unix partial writes](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.CoreLib/src/System/IO/RandomAccess.Unix.cs),
[.NET macOS flush](https://github.com/dotnet/runtime/blob/v8.0.0/src/native/libs/System.Native/pal_io.c),
[Apple disk synchronization](https://github.com/apple-oss-distributions/IOStorageFamily/blob/main/IOMediaBSDClient.cpp).

## Validation

Run the core and console suites with .NET 8:

```sh
dotnet test src/Hst.Imager.Core.Tests --configuration Release
dotnet test src/Hst.Imager.ConsoleApp.Tests --configuration Release
```

The raw-stream regression tests cover 512- and 4096-byte sectors, unaligned
reads and writes, neighboring bytes, short reads, failed partial writes,
size limits, all seek origins, read-only access, cancellation and disposal.
They also check the write/read/write/read verification order and asynchronous
I/O through an unbuffered temporary file. A macOS test checks that a rejected
disk synchronization ioctl is reported as an error.

These automated tests use memory and temporary files. SD-card checks are
recorded separately below.

Local validation on 2026-09-25, macOS ARM64, .NET SDK 8.0.425: 960 core
tests passed, one existing test skipped; both console tests passed. Both
self-contained archives were built. The ARM64 console starts; the Intel
binary could not run on this host without an Intel runtime environment.
The release workflow still needs a run on GitHub.

### SD-card measurements, 2026-09-25

The approved EMU68BOOT card is an external USB device with 63,864,569,856
bytes and 512-byte sectors. The ARM64 fork required Full Disk Access before
macOS permitted raw-device access. Tests used the console, without the GUI.

The sample was 256 MiB from offset 1,074,790,400 in **amiga.img**. Its SHA-256
was 0b195ba1e83220c2a9f9f276ef105c2cbac9beb9e0b7a442ea4572d61a366b7e;
104 of its 256 one-MiB buffers contained nonzero bytes. Both binaries used
the same card, reader, sample, default one-MiB buffers and per-buffer verify.
Each run started from a zeroed target range. The order was upstream, fork,
fork, upstream for each skip setting. Every run passed a separate comparison
of all 256 MiB after reopening, with skipping disabled.

| Skip zero buffers | Upstream block, seconds | Fork raw, seconds | Mean time ratio |
| --- | --- | --- | --- |
| Enabled | 18.247, 18.959 | 8.278, 8.869 | 2.17 |
| Disabled | 35.538, 36.890 | 11.269, 14.304 | 2.83 |

Times cover the complete write command, including startup, enumeration,
unmounting, verification, flush and cleanup. The full 59-GiB image was not
flashed; these ratios must not be treated as full-image timing results.

Five aligned and unaligned writes also passed: offset/length pairs 0/512,
1/1, 511/1026, 513/4097 and 4095/2. Each was checked against an 8192-byte
readback to verify the payload and unchanged neighboring bytes.

Local logs, results and recovery backups are under
**src/artifacts/hardware-validation/**, which is excluded from Git.
**cli-results.json** contains all eight benchmark measurements. The older
**cli-status.json** records a failed SIGINT assertion: the background command
completed instead of stopping. The later direct-test runner uses SIGKILL,
matching Hatcher's elevated helper, and successfully reopens the device.
A write beyond the disk boundary is rejected before writing. Neither check
simulates a reader disconnect or a physical media fault.

The direct-build check created a 64-MiB FAT32 partition and a 128-MiB
RDB container with a bootable PFS3 partition and pfs3aio. It exercised MBR
initialization, partition entries, RDB filesystem installation, formatting
and a 27-byte file in each filesystem. Both files were extracted and compared.
The same layout was first prepared in an image, written to the card and
compared byte for byte after reopening. The direct build was read back and
compared again over the entire test range.

The two independently created filesystems differed in 66 bytes across 11 sectors. Every
changed byte belongs to a FAT32 volume serial or FAT32/PFS3 date field;
excluding those fields makes the full 256-MiB ranges identical. PFS3 offsets
were checked against Hst.Amiga 0.6.222, source commit
[eb84b3f](https://github.com/henrikstengaard/hst-amiga/tree/eb84b3ff4fd40d84f5123e8f4a8de38501d13990/src/Hst.Amiga/FileSystems/Pfs3).
The field ranges and normalized checksum are in
**direct/metadata-comparison.json**.

The test runner denied mounts of this card through a temporary Disk
Arbitration approval callback. Without that callback, HST's per-command
remount let macOS change FAT32 free-cluster metadata and the exact comparison
failed. A disk claim alone did not prevent this. The callback was removed
after restoration; it is test infrastructure, not a change to Hatcher's
existing mount behavior. This console test does not establish acceptance
of Hatcher's complete direct-build pipeline or an Amiga boot.

The write area started at byte 63,594,037,760. Both 512-byte guard sectors
remained unchanged, as did bytes 512 through 1,048,575 after the MBR updates.
The original first MiB and the test area including both guards were restored
from backups and compared in full after reopening. EMU68BOOT was remounted
with its original volume UUID. **direct/status.json** and
**run-direct-3.log** record the successful final run and restoration.

### Hardware acceptance

Hardware measurements require an explicitly approved target. Recheck its
current diskutil identity, capacity and block size before access; device
numbers are temporary. Do not launch the GUI for these checks.

1. Use the same image, card, reader, buffer size, verification and zero-buffer
   settings for upstream and fork runs. Record binary version, image checksum,
   wall time, transferred bytes and verification result. Alternate run order
   and repeat to distinguish the access-path effect from card variation.
2. For the access-path timing comparison, retain the existing per-buffer
   verification order and identical initial target contents. A run with
   --skip-unused-sectors preserves destination bytes for zero source buffers;
   it is not evidence that those bytes represent unused filesystem space.
3. Separately check a complete write with skipping disabled and compare the
   full image range after reopening. Exercise an equivalent direct build
   through console commands, including MBR/RDB updates, FAT and Amiga
   filesystem metadata and small file writes. Compare the same written ranges
   in both paths, including neighboring bytes around metadata changes.
4. Check cancellation and an I/O failure, closure of the device and remount
   behavior. Report these checks separately from successful write timing.

Sequential writing followed by sequential verification is a separate change
and is not implemented here.

## Builds and releases

The **Hatcher macOS console release** workflow runs for 1.6.616-hatcher.*
tags or manual dispatch. It runs the core and console tests on native ARM64 and Intel macOS runners,
builds with .NET 8 and creates a draft prerelease after both jobs succeed.
An existing release version is rejected. The workflow does not publish it.

Versions use 1.6.616-hatcher.N, with N starting at 1. The packaging script
retains upstream's self-contained single-file and ReadyToRun settings,
the hst.imager executable name, scripts and license notices. Each archive has
SHA-256 and MD5 checksum files; MD5 matches Hatcher's current tool downloader.
Local archives record the source commit and whether the checkout was modified.

```sh
bash src/publish-hatcher-macos.sh osx-arm64 1.6.616-hatcher.1 /tmp/hatcher-release
bash src/publish-hatcher-macos.sh osx-x64 1.6.616-hatcher.1 /tmp/hatcher-release
```

The output directory must not already contain an archive with the same name.
Windows and Linux retain upstream binaries for this macOS-only patch.

After validation and release publication, update the two macOS URLs and
archive checksums in Emu68 Hatcher's
**src/main/python/emu68hatcher/data/reference/tools.yaml** (under
**~/Coding/emu68hatcher/**). Only the darwin-arm64 and darwin-x64 entries change.
The release URL pattern is:

```text
https://github.com/rootrootde/hst-imager-hatcher/releases/download/1.6.616-hatcher.N/hst-imager_v1.6.616-hatcher.N_console_macos_ARCH.zip
```

Use the published archive's MD5 for the hash field. The downloader uses its
URL stamp to replace the installed tool. The replacement binary may need
Full Disk Access again before macOS permits physical-device access.

Retain upstream license notices. Keep the patch small enough to submit
upstream and return to official binaries when a suitable release includes it.
