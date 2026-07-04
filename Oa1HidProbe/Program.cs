using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

Console.OutputEncoding = Encoding.UTF8;

var duration = args.Length > 0 && int.TryParse(args[0], out var seconds)
    ? Math.Clamp(seconds, 3, 120)
    : 20;

var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);

var logPath = Path.Combine(logDirectory, $"oa1-hid-capture-{DateTime.Now:yyyyMMdd-HHmmss}.log");
await using var log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };

var probe = new HidCaptureProbe(log);
await probe.RunAsync(TimeSpan.FromSeconds(duration));

Console.WriteLine();
Console.WriteLine($"Log saved: {logPath}");
return 0;

internal sealed class HidCaptureProbe(StreamWriter log)
{
    private const ushort TargetVendorId = 14007;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint NoDesiredAccess = 0x00000000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int HidpStatusSuccess = 0x00110000;

    private static readonly byte[] ActivationPayload = [0xAF, 0x03, 0x01, 0x01, 0x4C, 0xFF];
    private readonly ConcurrentDictionary<string, DeviceState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public async Task RunAsync(TimeSpan duration)
    {
        Log("OA1 HID capture started");
        Log($"Target VID: decimal {TargetVendorId}, hex 0x{TargetVendorId:X4}");
        Log($"Duration: {duration.TotalSeconds:F0}s");
        Log("WebHID activation: sendReport(0, Uint8Array(64)) with AF 03 01 01 4C FF at payload offset 0");
        Log("Close LineResistanceHost_WinUI/Chrome WebHID pages while capturing if they may already hold the interface.");
        Log("");

        StopProcess("LineResistanceHost_WinUI");

        var end = DateTimeOffset.Now + duration;
        while (DateTimeOffset.Now < end)
        {
            var devices = EnumerateTargetDevices();
            if (devices.Count == 0)
            {
                LogThrottled("no-device", "No VID_36B7 HID interface currently present", TimeSpan.FromMilliseconds(500));
            }

            foreach (var device in devices)
            {
                var state = _states.GetOrAdd(device.Path, _ => new DeviceState());
                if (!state.Seen)
                {
                    state.Seen = true;
                    Log("DEVICE SEEN");
                    Log($"  Path: {device.Path}");
                    Log($"  VID/PID: VID_{device.VendorId:X4} PID_{device.ProductId:X4} Version=0x{device.VersionNumber:X4}");
                }

                await CaptureDeviceAsync(device, state);
            }

            await Task.Delay(10);
        }

        Log("");
        Log("Capture finished");
    }

    private async Task CaptureDeviceAsync(TargetDevice device, DeviceState state)
    {
        state.Attempts++;
        Log($"ATTEMPT #{state.Attempts} for VID_{device.VendorId:X4} PID_{device.ProductId:X4}");

        foreach (var mode in AccessModes)
        {
            using var handle = OpenDevice(device.Path, mode.Access);
            if (handle.IsInvalid)
            {
                Log($"  Open {mode.Name}: FAIL {LastError()}");
                continue;
            }

            Log($"  Open {mode.Name}: OK");
            var caps = ReadCaps(handle);
            if (caps is not null)
            {
                Log($"    Caps: Input={caps.Value.InputReportByteLength}, Output={caps.Value.OutputReportByteLength}, Feature={caps.Value.FeatureReportByteLength}, UsagePage=0x{caps.Value.UsagePage:X4}, Usage=0x{caps.Value.Usage:X4}");
            }

            if ((mode.Access & GenericWrite) != 0)
            {
                TryActivationBurst(handle, caps);
            }

            if ((mode.Access & GenericRead) != 0)
            {
                await ReadBrieflyAsync(handle, caps?.InputReportByteLength ?? 64);
            }
        }
    }

    private void TryActivationBurst(SafeFileHandle handle, HIDP_CAPS? caps)
    {
        var reports = BuildActivationReports(caps?.OutputReportByteLength ?? 0).ToArray();
        for (var burst = 1; burst <= 3; burst++)
        {
            foreach (var report in reports)
            {
                var preview = Hex(report.AsSpan(0, Math.Min(report.Length, 12)));
                if (HidD_SetOutputReport(handle, report, report.Length))
                {
                    Log($"    Activation burst {burst}: HidD_SetOutputReport OK len={report.Length} head={preview}");
                }
                else
                {
                    Log($"    Activation burst {burst}: HidD_SetOutputReport FAIL len={report.Length} {LastError()} head={preview}");
                }

                if (WriteFile(handle, report, (uint)report.Length, out var written, IntPtr.Zero))
                {
                    Log($"    Activation burst {burst}: WriteFile OK len={report.Length} written={written} head={preview}");
                }
                else
                {
                    Log($"    Activation burst {burst}: WriteFile FAIL len={report.Length} {LastError()} head={preview}");
                }
            }
        }
    }

    private async Task ReadBrieflyAsync(SafeFileHandle handle, ushort inputReportLength)
    {
        var buffer = new byte[Math.Max(inputReportLength, (ushort)64)];
        var end = DateTimeOffset.Now + TimeSpan.FromMilliseconds(250);
        var readCount = 0;

        while (DateTimeOffset.Now < end && readCount < 5)
        {
            Array.Clear(buffer);
            if (!ReadFile(handle, buffer, (uint)buffer.Length, out var read, IntPtr.Zero))
            {
                Log($"    ReadFile FAIL {LastError()}");
                return;
            }

            if (read == 0)
            {
                await Task.Delay(5);
                continue;
            }

            readCount++;
            Log($"    ReadFile OK read={read} data={Hex(buffer.AsSpan(0, (int)Math.Min(read, 64)))}");
        }

        if (readCount == 0)
        {
            Log("    ReadFile: no report within 250ms");
        }
    }

    private List<TargetDevice> EnumerateTargetDevices()
    {
        var devices = new List<TargetDevice>();

        HidD_GetHidGuid(out var hidGuid);
        var infoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (infoSet == IntPtr.Zero || infoSet == new IntPtr(-1))
        {
            LogThrottled("setup-class-devs", $"SetupDiGetClassDevs failed: {LastError()}", TimeSpan.FromSeconds(1));
            return devices;
        }

        try
        {
            var interfaceData = new SP_DEVICE_INTERFACE_DATA
            {
                cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
            };

            for (uint index = 0; SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData); index++)
            {
                var path = GetDevicePath(infoSet, interfaceData);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                using var handle = OpenDevice(path, NoDesiredAccess);
                if (handle.IsInvalid)
                {
                    continue;
                }

                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorID != TargetVendorId)
                {
                    continue;
                }

                devices.Add(new TargetDevice(path, attributes.VendorID, attributes.ProductID, attributes.VersionNumber));
            }
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(infoSet);
        }

        return devices;
    }

    private HIDP_CAPS? ReadCaps(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var preparsedData))
        {
            Log($"    HidD_GetPreparsedData FAIL {LastError()}");
            return null;
        }

        try
        {
            var status = HidP_GetCaps(preparsedData, out var caps);
            if (status != HidpStatusSuccess)
            {
                Log($"    HidP_GetCaps FAIL status=0x{status:X8}");
                return null;
            }

            return caps;
        }
        finally
        {
            _ = HidD_FreePreparsedData(preparsedData);
        }
    }

    private IEnumerable<byte[]> BuildActivationReports(ushort outputReportLength)
    {
        var webHidEquivalent = new byte[65];
        Array.Copy(ActivationPayload, 0, webHidEquivalent, 1, ActivationPayload.Length);
        yield return webHidEquivalent;

        if (outputReportLength > 0 && outputReportLength != webHidEquivalent.Length)
        {
            var capsLength = new byte[Math.Max(outputReportLength, (ushort)(ActivationPayload.Length + 1))];
            Array.Copy(ActivationPayload, 0, capsLength, 1, ActivationPayload.Length);
            yield return capsLength;
        }

        var payloadAtZero64 = new byte[64];
        Array.Copy(ActivationPayload, 0, payloadAtZero64, 0, ActivationPayload.Length);
        yield return payloadAtZero64;

        if (outputReportLength > 0 && outputReportLength != payloadAtZero64.Length)
        {
            var payloadAtZeroCaps = new byte[Math.Max(outputReportLength, (ushort)ActivationPayload.Length)];
            Array.Copy(ActivationPayload, 0, payloadAtZeroCaps, 0, ActivationPayload.Length);
            yield return payloadAtZeroCaps;
        }
    }

    private static string? GetDevicePath(IntPtr infoSet, SP_DEVICE_INTERFACE_DATA interfaceData)
    {
        _ = SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
        if (requiredSize == 0)
        {
            return null;
        }

        var detailData = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detailData, requiredSize, out _, IntPtr.Zero))
            {
                return null;
            }

            return Marshal.PtrToStringUni(IntPtr.Add(detailData, 4));
        }
        finally
        {
            Marshal.FreeHGlobal(detailData);
        }
    }

    private void StopProcess(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                Log($"Stopping {processName}.exe pid={process.Id}");
                process.Kill();
            }
            catch (Exception ex)
            {
                Log($"Could not stop {processName}.exe pid={process.Id}: {ex.Message}");
            }
        }
    }

    private void LogThrottled(string key, string message, TimeSpan interval)
    {
        var state = _states.GetOrAdd($"__log__{key}", _ => new DeviceState());
        var now = DateTimeOffset.Now;
        if (now - state.LastLog < interval)
        {
            return;
        }

        state.LastLog = now;
        Log(message);
    }

    private void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} +{_clock.Elapsed.TotalMilliseconds,8:F1}ms  {message}";
        Console.WriteLine(line);
        log.WriteLine(line);
    }

    private static string LastError()
    {
        var error = Marshal.GetLastWin32Error();
        return $"err={error} {new Win32Exception(error).Message}";
    }

    private static string Hex(ReadOnlySpan<byte> value)
    {
        return string.Join(" ", value.ToArray().Select(item => item.ToString("X2")));
    }

    private static SafeFileHandle OpenDevice(string path, uint desiredAccess)
    {
        return CreateFile(path, desiredAccess, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
    }

    private static readonly (uint Access, string Name)[] AccessModes =
    [
        (GenericRead | GenericWrite, "READ_WRITE"),
        (GenericWrite, "WRITE_ONLY"),
        (GenericRead, "READ_ONLY"),
        (NoDesiredAccess, "METADATA")
    ];

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, uint numberOfBytesToRead, out uint numberOfBytesRead, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, uint numberOfBytesToWrite, out uint numberOfBytesWritten, IntPtr overlapped);

    private sealed class DeviceState
    {
        public bool Seen { get; set; }
        public int Attempts { get; set; }
        public DateTimeOffset LastLog { get; set; } = DateTimeOffset.MinValue;
    }

    private sealed record TargetDevice(string Path, ushort VendorId, ushort ProductId, ushort VersionNumber);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }
}
