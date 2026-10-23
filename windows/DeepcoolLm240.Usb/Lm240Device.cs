using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DeepcoolLm240.Usb;

public readonly record struct Lm240Found(string Path, Guid InterfaceGuid);

public sealed class Lm240Device : IDisposable
{
    static readonly Guid ZadigWinUsbGuid = new("DEE824EF-729B-4A0E-9C14-B7117D33A817");
    static readonly Guid WinUsbDeviceGuid = new("88BAE032-5A81-49F0-BC3D-A4FF138216D6");

    readonly SafeHandle _file;
    IntPtr _winusb = IntPtr.Zero;
    bool _inited;
    bool _disposed;

    Lm240Device(SafeHandle file, IntPtr winusb)
    {
        _file = file;
        _winusb = winusb;
    }

    public static IReadOnlyList<Lm240Found> Find(Guid? guidOverride = null)
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var devices = new List<Lm240Found>();
        foreach (var guid in InterfaceGuids(guidOverride))
        {
            foreach (var path in SetupApi.DevicePaths(guid))
            {
                if (!path.Contains("vid_3633", StringComparison.OrdinalIgnoreCase) ||
                    !path.Contains("pid_0026", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!seen.Add(path))
                    continue;
                devices.Add(new Lm240Found(path, guid));
            }
        }

        return devices;
    }

    public static Lm240Device Open(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WinUSB is only available on Windows.");

        var file = Native.CreateFile(
            path,
            Native.GenericRead | Native.GenericWrite,
            Native.FileShareRead | Native.FileShareWrite,
            IntPtr.Zero,
            Native.OpenExisting,
            Native.FileFlagOverlapped,
            IntPtr.Zero);

        if (file.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFile failed");

        if (!Native.WinUsb_Initialize(file, out var winusb))
        {
            int error = Marshal.GetLastWin32Error();
            file.Dispose();
            throw new Win32Exception(error, "WinUsb_Initialize failed");
        }

        uint timeout = Lm240Protocol.WriteTimeoutMs;
        Native.WinUsb_SetPipePolicy(winusb, Lm240Protocol.BulkOut, Native.PipeTransferTimeout, sizeof(uint), ref timeout);
        return new Lm240Device(file, winusb);
    }

    public void SendFrame(byte[] framebuffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (framebuffer.Length != Lm240Protocol.Width * Lm240Protocol.Height * 2)
            throw new ArgumentException("Framebuffer must be 320x240 RGB565.", nameof(framebuffer));

        if (!_inited)
        {
            Write(Lm240Protocol.InitQuery);
            Thread.Sleep(500);
            _inited = true;
        }

        Write(Lm240Protocol.FrameHeader);
        Write(framebuffer);
    }

    void Write(byte[] data)
    {
        if (!Native.WinUsb_WritePipe(_winusb, Lm240Protocol.BulkOut, data, (uint)data.Length, out uint written, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_WritePipe failed");
        if (written != data.Length)
            throw new IOException($"Short write: {written}/{data.Length}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_winusb != IntPtr.Zero)
        {
            Native.WinUsb_Free(_winusb);
            _winusb = IntPtr.Zero;
        }

        _file.Dispose();
    }

    static IEnumerable<Guid> InterfaceGuids(Guid? guidOverride)
    {
        if (guidOverride.HasValue)
        {
            yield return guidOverride.Value;
            yield break;
        }

        var found = new HashSet<Guid>();
        foreach (var guid in RegistryInterfaceGuids())
        {
            if (found.Add(guid))
                yield return guid;
        }

        if (found.Add(ZadigWinUsbGuid))
            yield return ZadigWinUsbGuid;
        if (found.Add(WinUsbDeviceGuid))
            yield return WinUsbDeviceGuid;
    }

    static IEnumerable<Guid> RegistryInterfaceGuids()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB\VID_3633&PID_0026");
        if (usb is null)
            yield break;

        foreach (var instance in usb.GetSubKeyNames())
        {
            using var parameters = usb.OpenSubKey(instance + @"\Device Parameters");
            var value = parameters?.GetValue("DeviceInterfaceGUIDs");
            if (value is string[] lines)
            {
                foreach (var line in lines)
                {
                    if (Guid.TryParse(line.Trim(), out var guid))
                        yield return guid;
                }
            }
            else if (value is string text && Guid.TryParse(text.Trim(), out var guid))
            {
                yield return guid;
            }
        }
    }
}

internal static class SetupApi
{
    public static IEnumerable<string> DevicePaths(Guid interfaceGuid)
    {
        var guid = interfaceGuid;
        IntPtr set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, Native.DigcfPresent | Native.DigcfDeviceInterface);
        if (set == Native.InvalidHandle)
            yield break;

        try
        {
            for (uint index = 0; ; index++)
            {
                var iface = new Native.SpDeviceInterfaceData { CbSize = Marshal.SizeOf<Native.SpDeviceInterfaceData>() };
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref iface))
                {
                    if (Marshal.GetLastWin32Error() == Native.ErrorNoMoreItems)
                        yield break;
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiEnumDeviceInterfaces failed");
                }

                int required = 0;
                Native.SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                IntPtr buffer = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
                    if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref iface, buffer, required, ref required, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetDeviceInterfaceDetail failed");
                    string? path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
                    if (!string.IsNullOrEmpty(path))
                        yield return path;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }
    }
}

internal static class Native
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x00000001;
    public const uint FileShareWrite = 0x00000002;
    public const uint OpenExisting = 3;
    public const uint FileFlagOverlapped = 0x40000000;
    public const uint DigcfPresent = 0x00000002;
    public const uint DigcfDeviceInterface = 0x00000010;
    public const int ErrorNoMoreItems = 259;
    public const uint PipeTransferTimeout = 0x03;
    public static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, ref int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_Initialize(SafeHandle deviceHandle, out IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_Free(IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_SetPipePolicy(IntPtr interfaceHandle, byte pipeId, uint policyType, int valueLength, ref uint value);

    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_WritePipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);
}
