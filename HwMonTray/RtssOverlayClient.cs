using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace HwMonTray
{
    internal sealed class RtssOverlayClient : IDisposable
    {
        private const string SharedMemoryName = "RTSSSharedMemoryV2";
        private const int RtssSignature = 0x52545353;
        private const int MinimumVersion = 0x00020000;
        private const int ExtendedTextVersion = 0x00020007;
        private const int ExtendedText2Version = 0x00020014;
        private const int AppEntrySizeOffset = 8;
        private const int AppArrayOffsetOffset = 12;
        private const int AppArraySizeOffset = 16;
        private const int OsdEntrySizeOffset = 20;
        private const int OsdArrayOffsetOffset = 24;
        private const int OsdArraySizeOffset = 28;
        private const int OsdFrameOffset = 32;
        private const int BusyOffset = 36;
        private const int LastForegroundAppOffset = 64;
        private const int OsdTextOffset = 0;
        private const int OsdOwnerOffset = 256;
        private const int OsdTextExOffset = 512;
        private const int OsdTextEx2Offset = 266752;
        private const int OwnerCapacity = 256;
        private const int AppNameOffset = 4;
        private const int AppNameCapacity = 260;
        private const int AppFlagsOffset = 264;
        private const int LegacyTextCapacity = 256;
        private const int ExtendedTextCapacity = 4096;
        private const int ExtendedText2Capacity = 32768;
        private const string OwnerId = "HwMonTray";
        private static readonly Encoding RtssTextEncoding = Encoding.Default;
        private static readonly object StatusLock = new();
        private static bool _lastPushSucceeded;
        private static string _lastPushStatus = "No RTSS push attempt yet.";
        private static DateTime? _lastPushLocalTime;
        private static int _lastPayloadLength;

        public bool Update(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Release();
            }

            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.ReadWrite);
                using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);

                int signature = accessor.ReadInt32(0);
                if (signature != RtssSignature)
                {
                    RecordPushResult(false, $"RTSS shared memory signature is invalid (0x{signature:X8}).", text.Length);
                    return false;
                }

                int version = accessor.ReadInt32(4);
                if (version < MinimumVersion)
                {
                    RecordPushResult(false, $"RTSS shared memory version {version:X8} is too old.", text.Length);
                    return false;
                }

                int entrySize = accessor.ReadInt32(OsdEntrySizeOffset);
                int arrayOffset = accessor.ReadInt32(OsdArrayOffsetOffset);
                int arraySize = accessor.ReadInt32(OsdArraySizeOffset);
                if (entrySize <= 0 || arrayOffset <= 0 || arraySize <= 1)
                {
                    RecordPushResult(false, "RTSS OSD slot table is unavailable.", text.Length);
                    return false;
                }

                long? slotOffset = FindOwnedSlotOffset(accessor, arrayOffset, entrySize, arraySize);
                if (slotOffset == null)
                {
                    slotOffset = AcquireEmptySlot(accessor, arrayOffset, entrySize, arraySize);
                }

                if (slotOffset == null)
                {
                    RecordPushResult(false, "No free RTSS OSD slot was available.", text.Length);
                    return false;
                }

                if (version >= 0x0002000e)
                {
                    WaitForBusy(accessor);
                }

                WriteOsdText(accessor, version, slotOffset.Value, text);
                accessor.Write(OsdFrameOffset, accessor.ReadInt32(OsdFrameOffset) + 1);

                if (version >= 0x0002000e)
                {
                    accessor.Write(BusyOffset, 0);
                }

                RecordPushResult(true, "Last RTSS push succeeded.", text.Length);
                return true;
            }
            catch (FileNotFoundException)
            {
                RecordPushResult(false, "RTSS shared memory is not available. Make sure RTSS is running.", text.Length);
                return false;
            }
            catch (Exception ex)
            {
                RecordPushResult(false, $"RTSS push failed: {ex.Message}", text.Length);
                return false;
            }
        }

        public bool Release()
        {
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.ReadWrite);
                using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);

                int signature = accessor.ReadInt32(0);
                if (signature != RtssSignature)
                {
                    RecordPushResult(false, $"RTSS shared memory signature is invalid during release (0x{signature:X8}).", 0);
                    return false;
                }

                int version = accessor.ReadInt32(4);
                if (version < MinimumVersion)
                {
                    RecordPushResult(false, $"RTSS shared memory version {version:X8} is too old during release.", 0);
                    return false;
                }

                int entrySize = accessor.ReadInt32(OsdEntrySizeOffset);
                int arrayOffset = accessor.ReadInt32(OsdArrayOffsetOffset);
                int arraySize = accessor.ReadInt32(OsdArraySizeOffset);
                if (entrySize <= 0 || arrayOffset <= 0 || arraySize <= 1)
                {
                    RecordPushResult(false, "RTSS OSD slot table is unavailable during release.", 0);
                    return false;
                }

                bool changed = false;
                for (int index = 1; index < arraySize; index++)
                {
                    long slotOffset = arrayOffset + (long)entrySize * index;
                    if (!string.Equals(ReadNullTerminatedAnsi(accessor, slotOffset + OsdOwnerOffset, OwnerCapacity), OwnerId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    accessor.WriteArray(slotOffset, new byte[entrySize], 0, entrySize);
                    changed = true;
                }

                if (changed)
                {
                    accessor.Write(OsdFrameOffset, accessor.ReadInt32(OsdFrameOffset) + 1);
                    RecordPushResult(true, "RTSS OSD slot released.", 0);
                }
                else
                {
                    RecordPushResult(false, "No HwMonTray RTSS OSD slot was owned.", 0);
                }

                return changed;
            }
            catch (FileNotFoundException)
            {
                RecordPushResult(false, "RTSS is not running, so there was no OSD slot to release.", 0);
                return false;
            }
            catch (Exception ex)
            {
                RecordPushResult(false, $"RTSS release failed: {ex.Message}", 0);
                return false;
            }
        }

        public void Dispose()
        {
            Release();
        }

        public static RtssStatusSnapshot GetStatusSnapshot()
        {
            var snapshot = new RtssStatusSnapshot
            {
                IsProcessRunning = Process.GetProcessesByName("RTSS").Length > 0
            };

            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.ReadWrite);
                using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);

                int signature = accessor.ReadInt32(0);
                if (signature != RtssSignature)
                {
                    snapshot.HasSharedMemory = true;
                    snapshot.Status = ComposeStatus(snapshot, $"RTSS shared memory signature is unexpected (0x{signature:X8}).");
                    return snapshot;
                }

                snapshot.HasSharedMemory = true;
                snapshot.Version = accessor.ReadInt32(4);
                if (snapshot.Version < MinimumVersion)
                {
                    snapshot.Status = ComposeStatus(snapshot);
                    return snapshot;
                }

                int osdEntrySize = accessor.ReadInt32(OsdEntrySizeOffset);
                int osdArrayOffset = accessor.ReadInt32(OsdArrayOffsetOffset);
                int osdArraySize = accessor.ReadInt32(OsdArraySizeOffset);
                snapshot.IsSlotOwned = FindOwnedSlotOffset(accessor, osdArrayOffset, osdEntrySize, osdArraySize) != null;

                if (snapshot.Version >= 0x00020010)
                {
                    int appEntrySize = accessor.ReadInt32(AppEntrySizeOffset);
                    int appArrayOffset = accessor.ReadInt32(AppArrayOffsetOffset);
                    int appArraySize = accessor.ReadInt32(AppArraySizeOffset);
                    int foregroundAppIndex = accessor.ReadInt32(LastForegroundAppOffset);

                    if (foregroundAppIndex >= 0 && foregroundAppIndex < appArraySize && appEntrySize > 0 && appArrayOffset > 0)
                    {
                        long appOffset = appArrayOffset + (long)appEntrySize * foregroundAppIndex;
                        snapshot.LastForegroundAppName = ReadNullTerminatedAnsi(accessor, appOffset + AppNameOffset, AppNameCapacity);
                        int flags = accessor.ReadInt32(appOffset + AppFlagsOffset);
                        snapshot.LastForegroundApi = DecodeApiName(flags);
                    }
                }

                snapshot.Status = ComposeStatus(snapshot);
                return snapshot;
            }
            catch (FileNotFoundException)
            {
                snapshot.Status = ComposeStatus(snapshot);
                return snapshot;
            }
            catch (Exception ex)
            {
                snapshot.Status = ComposeStatus(snapshot, $"RTSS status check failed: {ex.Message}");
                return snapshot;
            }
        }

        public static string BuildDebugReport()
        {
            return GetStatusSnapshot().Status;
        }

        private static long? FindOwnedSlotOffset(MemoryMappedViewAccessor accessor, int arrayOffset, int entrySize, int arraySize)
        {
            for (int index = 1; index < arraySize; index++)
            {
                long slotOffset = arrayOffset + (long)entrySize * index;
                string owner = ReadNullTerminatedAnsi(accessor, slotOffset + OsdOwnerOffset, OwnerCapacity);
                if (string.Equals(owner, OwnerId, StringComparison.Ordinal))
                {
                    return slotOffset;
                }
            }

            return null;
        }

        private static long? AcquireEmptySlot(MemoryMappedViewAccessor accessor, int arrayOffset, int entrySize, int arraySize)
        {
            for (int index = 1; index < arraySize; index++)
            {
                long slotOffset = arrayOffset + (long)entrySize * index;
                string owner = ReadNullTerminatedAnsi(accessor, slotOffset + OsdOwnerOffset, OwnerCapacity);
                if (!string.IsNullOrEmpty(owner))
                {
                    continue;
                }

                WriteAnsi(accessor, slotOffset + OsdOwnerOffset, OwnerId, OwnerCapacity);
                return slotOffset;
            }

            return null;
        }

        private static void WriteOsdText(MemoryMappedViewAccessor accessor, int version, long slotOffset, string text)
        {
            if (version >= ExtendedText2Version)
            {
                WriteAnsi(accessor, slotOffset + OsdTextEx2Offset, text, ExtendedText2Capacity);
            }
            else if (version >= ExtendedTextVersion)
            {
                WriteAnsi(accessor, slotOffset + OsdTextExOffset, text, ExtendedTextCapacity);
            }
            else
            {
                WriteAnsi(accessor, slotOffset + OsdTextOffset, text, LegacyTextCapacity);
            }
        }

        private static void WaitForBusy(MemoryMappedViewAccessor accessor)
        {
            int start = Environment.TickCount;
            while (accessor.ReadInt32(BusyOffset) != 0)
            {
                if (unchecked(Environment.TickCount - start) > 100)
                {
                    break;
                }

                System.Threading.Thread.Sleep(1);
            }

            accessor.Write(BusyOffset, 1);
        }

        private static string ReadNullTerminatedAnsi(MemoryMappedViewAccessor accessor, long offset, int capacity)
        {
            byte[] buffer = new byte[capacity];
            accessor.ReadArray(offset, buffer, 0, capacity);
            int terminator = Array.IndexOf(buffer, (byte)0);
            int length = terminator >= 0 ? terminator : capacity;
            return RtssTextEncoding.GetString(buffer, 0, length);
        }

        private static void WriteAnsi(MemoryMappedViewAccessor accessor, long offset, string text, int capacity)
        {
            byte[] buffer = new byte[capacity];
            RtssTextEncoding.GetBytes(text.AsSpan(0, Math.Min(text.Length, capacity - 1)), buffer);
            accessor.WriteArray(offset, buffer, 0, capacity);
        }

        private static string DecodeApiName(int flags)
        {
            return (flags & 0x0000FFFF) switch
            {
                0x00000001 => "OpenGL",
                0x00000002 => "DirectDraw",
                0x00000003 => "Direct3D 8",
                0x00000004 => "Direct3D 9",
                0x00000005 => "Direct3D 9Ex",
                0x00000006 => "Direct3D 10",
                0x00000007 => "Direct3D 11",
                0x00000008 => "Direct3D 12",
                0x00000009 => "Direct3D 12 AFR",
                0x0000000A => "Vulkan",
                _ => "Unknown API"
            };
        }

        private static void RecordPushResult(bool succeeded, string status, int payloadLength)
        {
            lock (StatusLock)
            {
                _lastPushSucceeded = succeeded;
                _lastPushStatus = status;
                _lastPushLocalTime = DateTime.Now;
                _lastPayloadLength = payloadLength;
            }
        }

        private static void CopyRuntimeStatus(RtssStatusSnapshot snapshot)
        {
            lock (StatusLock)
            {
                snapshot.LastPushSucceeded = _lastPushSucceeded;
                snapshot.LastPushStatus = _lastPushStatus;
                snapshot.LastPushLocalTime = _lastPushLocalTime;
                snapshot.LastPayloadLength = _lastPayloadLength;
            }
        }

        private static string ComposeStatus(RtssStatusSnapshot snapshot, string? overrideError = null)
        {
            CopyRuntimeStatus(snapshot);

            var lines = new StringBuilder();
            lines.AppendLine(snapshot.IsProcessRunning
                ? "RTSS process: running"
                : "RTSS process: not running");
            lines.AppendLine(snapshot.HasSharedMemory
                ? $"Shared memory: ready (v{snapshot.Version >> 16}.{snapshot.Version & 0xFFFF})"
                : snapshot.IsProcessRunning
                    ? "Shared memory: unavailable"
                    : "Shared memory: unavailable");
            lines.AppendLine(snapshot.IsSlotOwned
                ? "HwMonTray slot: acquired"
                : "HwMonTray slot: not acquired");

            if (!string.IsNullOrWhiteSpace(snapshot.LastForegroundAppName))
            {
                lines.AppendLine($"Last hooked app: {snapshot.LastForegroundAppName} ({snapshot.LastForegroundApi})");
            }
            else
            {
                lines.AppendLine("Last hooked app: none detected yet");
            }

            string pushPrefix = snapshot.LastPushSucceeded ? "Last push" : "Last push attempt";
            string payloadText = snapshot.LastPayloadLength > 0 ? $"{snapshot.LastPayloadLength} chars" : "no payload";
            string timeText = snapshot.LastPushLocalTime?.ToString("HH:mm:ss") ?? "never";
            lines.AppendLine($"{pushPrefix}: {snapshot.LastPushStatus} ({payloadText}, {timeText})");

            if (!string.IsNullOrWhiteSpace(overrideError))
            {
                lines.AppendLine(overrideError);
            }
            else if (!snapshot.IsProcessRunning)
            {
                lines.AppendLine("Start RTSS first, then launch the game.");
            }
            else if (!snapshot.HasSharedMemory)
            {
                lines.AppendLine("RTSS is running, but its shared memory is not ready yet.");
            }
            else if (!snapshot.IsSlotOwned)
            {
                lines.AppendLine("HwMonTray has not captured an RTSS OSD slot yet.");
            }
            else if (string.IsNullOrWhiteSpace(snapshot.LastForegroundAppName))
            {
                lines.AppendLine("RTSS is ready. Launch a supported 3D app and check whether RTSS hooks it.");
            }
            else
            {
                lines.AppendLine("If the game still shows nothing, RTSS hooked a process but the overlay may be blocked by that game's profile or renderer path.");
            }

            return lines.ToString().TrimEnd();
        }
    }

    internal sealed class RtssStatusSnapshot
    {
        public bool IsProcessRunning { get; set; }
        public bool HasSharedMemory { get; set; }
        public bool IsSlotOwned { get; set; }
        public int Version { get; set; }
        public string LastForegroundAppName { get; set; } = string.Empty;
        public string LastForegroundApi { get; set; } = string.Empty;
        public bool LastPushSucceeded { get; set; }
        public string LastPushStatus { get; set; } = string.Empty;
        public DateTime? LastPushLocalTime { get; set; }
        public int LastPayloadLength { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
