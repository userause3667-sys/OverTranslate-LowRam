using System.Runtime.InteropServices;

namespace OverTranslate.Services.Performance;

/// <summary>Installed physical memory, asked of Windows once.</summary>
internal static class SystemMemory
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static readonly Lazy<long> Total = new(ReadTotal);

    /// <summary>Installed RAM Windows reports, in bytes, or 0 when it would not say.</summary>
    public static long TotalPhysicalBytes => Total.Value;

    /// <summary>Physical memory free right now, in bytes, or 0 when it would not say.</summary>
    public static long AvailablePhysicalBytes
    {
        get
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? (long)status.ullAvailPhys : 0;
        }
    }

    private static long ReadTotal()
    {
        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
