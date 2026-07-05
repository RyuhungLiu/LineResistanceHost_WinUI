using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using LineResistanceHost.Models;

namespace LineResistanceHost.Services;

public sealed class Oa1HidService : IDisposable
{
    public const ushort TargetVendorId = 14007;
    public const ushort WitrnK2VendorId = 0x0716;
    public const ushort WitrnK2ProductId = 0x5060;

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint NoDesiredAccess = 0x00000000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int HidpStatusSuccess = 0x00110000;
    private const int ErrorIoPending = 997;

    private static readonly byte[] ActivationPayload = [0xAF, 0x03, 0x01, 0x01, 0x4C, 0xFF];

    private readonly object _sync = new();
    private SafeFileHandle? _handle;
    private CancellationTokenSource? _readCancellation;
    private string? _connectedPath;
    private Oa1DeviceKind _connectedKind = Oa1DeviceKind.Oa1;
    private ushort _inputReportLength = 64;
    private ushort _outputReportLength = 65;
    private int _loggedFrameCount;
    private DateTimeOffset _lastWitrnK2Frame = DateTimeOffset.MinValue;

    public event EventHandler<Oa1Frame>? FrameReceived;
    public event EventHandler<string>? LogReceived;
    public event EventHandler? Disconnected;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _handle is { IsInvalid: false, IsClosed: false };
            }
        }
    }

    public IReadOnlyList<Oa1DeviceInfo> FindDevices()
    {
        var devices = new List<Oa1DeviceInfo>();

        HidD_GetHidGuid(out var hidGuid);
        var infoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (infoSet == IntPtr.Zero || infoSet == new IntPtr(-1))
        {
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

                var attributes = ReadAttributes(path);
                if (attributes is not { } value || !TryGetDeviceKind(value.VendorID, value.ProductID, out var kind))
                {
                    continue;
                }

                devices.Add(new Oa1DeviceInfo(
                    path,
                    string.Empty,
                    value.VendorID,
                    value.ProductID,
                    kind));
            }
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(infoSet);
        }

        return devices;
    }

    public async Task ConnectAsync(Oa1DeviceInfo device)
    {
        Disconnect();

        SafeFileHandle handle;
        try
        {
            handle = OpenForConnection(device);
        }
        catch (Exception ex)
        {
            if (device.Kind == Oa1DeviceKind.Oa1 && await TryPrimeActivationOnlyAsync(device.Path))
            {
                throw new InvalidOperationException(AppText.Get("PrimeActivationSent"), ex);
            }

            throw;
        }

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, AppText.Get("OpenHidFailed"));
        }

        var readCancellation = new CancellationTokenSource();
        lock (_sync)
        {
            _handle = handle;
            _connectedPath = device.Path;
            _connectedKind = device.Kind;
            _readCancellation = readCancellation;
            _loggedFrameCount = 0;
            _lastWitrnK2Frame = DateTimeOffset.MinValue;
        }

        ReadCapabilities(handle);
        Log(AppText.Format("ConnectedLog", device.DeviceLabel, device.DisplayName));

        if (device.Kind == Oa1DeviceKind.Oa1)
        {
            SendActivationBurstBestEffort(handle, AppText.Get("ConnectPreActivation"), 2);
            _ = Task.Run(() => SendActivationBestEffortAsync(handle, TimeSpan.FromSeconds(2.5)));
        }
        else
        {
            Log(AppText.Get("K2NoActivation"));
        }

        _ = Task.Run(() => ReadLoop(handle, readCancellation.Token));
    }

    public Task SendActivationAsync()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException(AppText.Get("DeviceNotConnected"));
        }

        if (GetCurrentKind() != Oa1DeviceKind.Oa1)
        {
            throw new InvalidOperationException(AppText.Get("ActivationNotRequired"));
        }

        return SendActivationWithRetryAsync(TimeSpan.Zero);
    }

    public void Disconnect()
    {
        CancellationTokenSource? readCancellation;
        SafeFileHandle? handle;

        lock (_sync)
        {
            readCancellation = _readCancellation;
            handle = _handle;
            _readCancellation = null;
            _handle = null;
            _connectedPath = null;
            _connectedKind = Oa1DeviceKind.Oa1;
        }

        readCancellation?.Cancel();
        if (handle is { IsInvalid: false, IsClosed: false })
        {
            _ = CancelIoEx(handle, IntPtr.Zero);
        }

        readCancellation?.Dispose();
        handle?.Dispose();
    }

    public void Dispose()
    {
        Disconnect();
    }

    private void ReadLoop(SafeFileHandle handle, CancellationToken token)
    {
        var buffer = new byte[Math.Max(_inputReportLength, (ushort)64)];

        while (!token.IsCancellationRequested && !handle.IsInvalid && !handle.IsClosed)
        {
            Array.Clear(buffer);

            if (!TryReadInputReport(handle, buffer, token, out var read, out var errorMessage))
            {
                if (!token.IsCancellationRequested)
                {
                    if (DisconnectCurrentHandle(handle))
                    {
                        Log(AppText.Format("ReadInterruptedLog", errorMessage ?? AppText.Get("ReadInterruptedMessage")));
                        Disconnected?.Invoke(this, EventArgs.Empty);
                    }
                }
                return;
            }

            if (read == 0)
            {
                continue;
            }

            var kind = GetCurrentKind(handle);
            if (Oa1Frame.TryParse(kind, buffer.AsSpan(0, (int)read), out var frame) && frame is not null)
            {
                if (kind == Oa1DeviceKind.WitrnK2 && !ShouldPublishWitrnK2Frame())
                {
                    continue;
                }

                if (_loggedFrameCount < 5)
                {
                    _loggedFrameCount++;
                    Log(AppText.Format("FrameReceivedLog", _loggedFrameCount, frame.RawHex));
                }

                FrameReceived?.Invoke(this, frame);
            }
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

    private static HIDD_ATTRIBUTES? ReadAttributes(string path)
    {
        using var handle = OpenDevice(path, NoDesiredAccess);
        if (handle.IsInvalid)
        {
            return null;
        }

        var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        return HidD_GetAttributes(handle, ref attributes) ? attributes : null;
    }

    private static string ReadProductName(string path)
    {
        using var handle = OpenDevice(path, NoDesiredAccess);
        if (handle.IsInvalid)
        {
            return string.Empty;
        }

        var buffer = new byte[256];
        return HidD_GetProductString(handle, buffer, buffer.Length)
            ? Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : string.Empty;
    }

    private void ReadCapabilities(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var preparsedData))
        {
            return;
        }

        try
        {
            var status = HidP_GetCaps(preparsedData, out var caps);
            if (status == HidpStatusSuccess)
            {
                _inputReportLength = caps.InputReportByteLength;
                _outputReportLength = caps.OutputReportByteLength;
                Log(AppText.Format("ReportLengthLog", _inputReportLength, _outputReportLength, caps.FeatureReportByteLength));
            }
            else
            {
                Log(AppText.Format("CapsFailedLog", status));
            }
        }
        finally
        {
            _ = HidD_FreePreparsedData(preparsedData);
        }
    }

    private static SafeFileHandle OpenForConnection(Oa1DeviceInfo device)
    {
        (uint Access, uint ShareMode, string Name)[] attempts = device.Kind == Oa1DeviceKind.WitrnK2
            ? [
                (GenericRead, FileShareRead | FileShareWrite, AppText.Get("SharedRead")),
                (GenericRead | GenericWrite, FileShareRead | FileShareWrite, AppText.Get("SharedReadWrite")),
                (GenericRead, 0, AppText.Get("ExclusiveRead"))
            ]
            : [
                (GenericRead | GenericWrite, 0, AppText.Get("ExclusiveReadWrite")),
                (GenericRead, 0, AppText.Get("ExclusiveRead")),
                (GenericRead | GenericWrite, FileShareRead | FileShareWrite, AppText.Get("SharedReadWrite")),
                (GenericRead, FileShareRead | FileShareWrite, AppText.Get("SharedRead"))
            ];

        var errors = new List<string>();
        foreach (var (access, shareMode, name) in attempts)
        {
            var handle = OpenDevice(device.Path, access, shareMode, FileFlagOverlapped);
            if (!handle.IsInvalid)
            {
                return handle;
            }

            errors.Add($"{name}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            handle.Dispose();
        }

        throw new InvalidOperationException(AppText.Format("OpenHidFailedWithDetails", string.Join("; ", errors)));
    }

    private async Task<bool> TryPrimeActivationOnlyAsync(string path)
    {
        var started = DateTimeOffset.Now;
        List<string> failures = [];

        do
        {
            failures.Clear();
            using var writeHandle = OpenDevice(path, GenericWrite);
            if (!writeHandle.IsInvalid && TrySendActivation(writeHandle, failures, out var method))
            {
                Log(AppText.Format("PrimeActivationWriteOnly", method));
                return true;
            }

            await Task.Delay(25);
        }
        while (DateTimeOffset.Now - started < TimeSpan.FromMilliseconds(450));

        if (failures.Count > 0)
        {
            Log(AppText.Format("PrimeActivationFailed", string.Join("; ", failures.Distinct())));
        }

        return false;
    }

    private async Task SendActivationWithRetryAsync(TimeSpan retryWindow)
    {
        var handle = GetCurrentHandle();
        if (handle is not { IsInvalid: false, IsClosed: false })
        {
            throw new InvalidOperationException(AppText.Get("DeviceNotConnected"));
        }

        await SendActivationWithRetryAsync(handle, retryWindow);
    }

    private async Task SendActivationBestEffortAsync(SafeFileHandle handle, TimeSpan retryWindow)
    {
        try
        {
            await SendActivationWithRetryAsync(handle, retryWindow);
        }
        catch (Exception ex)
        {
            if (IsCurrentHandle(handle))
            {
                Log(AppText.Format("ActivationSendFailedKeep", ex.Message));
            }
        }
    }

    private void SendActivationBurstBestEffort(SafeFileHandle handle, string source, int bursts)
    {
        var anySuccess = false;
        var failures = new List<string>();

        for (var index = 0; index < bursts && IsCurrentHandle(handle); index++)
        {
            failures.Clear();
            if (TrySendActivation(handle, failures, out var method))
            {
                anySuccess = true;
                Log(AppText.Format("SourceSent", source, method));
            }
        }

        if (!anySuccess && failures.Count > 0)
        {
            Log(AppText.Format("SourceFailedKeep", source, string.Join("; ", failures.Distinct())));
        }
    }

    private async Task SendActivationWithRetryAsync(SafeFileHandle handle, TimeSpan retryWindow)
    {
        var started = DateTimeOffset.Now;
        List<string> failures = [];

        do
        {
            failures.Clear();
            if (TrySendActivation(handle, failures, out var method))
            {
                Log(AppText.Format("ActivationSentLog", method));
                return;
            }

            var connectedPath = GetConnectedPath(handle);
            if (!string.IsNullOrWhiteSpace(connectedPath))
            {
                using var writeHandle = OpenDevice(connectedPath, GenericWrite);
                if (!writeHandle.IsInvalid && TrySendActivation(writeHandle, failures, out method))
                {
                    Log(AppText.Format("ActivationSentHelper", method));
                    return;
                }

                if (writeHandle.IsInvalid)
                {
                    failures.Add(AppText.Format("HelperWriteHandle", new Win32Exception(Marshal.GetLastWin32Error()).Message));
                }
            }

            if (retryWindow <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(35);
        }
        while (DateTimeOffset.Now - started < retryWindow && IsCurrentHandle(handle));

        throw new InvalidOperationException(AppText.Format("ActivationSendFailed", string.Join("; ", failures.Distinct())));
    }

    private SafeFileHandle? GetCurrentHandle()
    {
        lock (_sync)
        {
            return _handle;
        }
    }

    private Oa1DeviceKind GetCurrentKind()
    {
        lock (_sync)
        {
            return _connectedKind;
        }
    }

    private Oa1DeviceKind GetCurrentKind(SafeFileHandle handle)
    {
        lock (_sync)
        {
            return ReferenceEquals(_handle, handle) ? _connectedKind : Oa1DeviceKind.Oa1;
        }
    }

    private string? GetConnectedPath(SafeFileHandle handle)
    {
        lock (_sync)
        {
            return ReferenceEquals(_handle, handle) ? _connectedPath : null;
        }
    }

    private bool IsCurrentHandle(SafeFileHandle handle)
    {
        lock (_sync)
        {
            return ReferenceEquals(_handle, handle);
        }
    }

    private bool DisconnectCurrentHandle(SafeFileHandle handle)
    {
        CancellationTokenSource? readCancellation;
        SafeFileHandle? currentHandle;

        lock (_sync)
        {
            if (!ReferenceEquals(_handle, handle))
            {
                return false;
            }

            readCancellation = _readCancellation;
            currentHandle = _handle;
            _readCancellation = null;
            _handle = null;
            _connectedPath = null;
            _connectedKind = Oa1DeviceKind.Oa1;
        }

        readCancellation?.Cancel();
        readCancellation?.Dispose();
        if (currentHandle is { IsInvalid: false, IsClosed: false })
        {
            _ = CancelIoEx(currentHandle, IntPtr.Zero);
        }

        currentHandle?.Dispose();
        return true;
    }

    private bool ShouldPublishWitrnK2Frame()
    {
        var now = DateTimeOffset.Now;
        lock (_sync)
        {
            if (now - _lastWitrnK2Frame < TimeSpan.FromMilliseconds(500))
            {
                return false;
            }

            _lastWitrnK2Frame = now;
            return true;
        }
    }

    private bool TrySendActivation(SafeFileHandle? handle, List<string> failures, out string method)
    {
        method = string.Empty;
        if (handle is not { IsInvalid: false, IsClosed: false })
        {
            failures.Add(AppText.Get("InvalidHandle"));
            return false;
        }

        var fallbackSetOutputMethod = string.Empty;
        foreach (var report in BuildActivationReports())
        {
            var setOutputMethod = string.Empty;
            if (HidD_SetOutputReport(handle, report, report.Length))
            {
                setOutputMethod = $"SetOutputReport ({report.Length} bytes)";
                fallbackSetOutputMethod = setOutputMethod;
            }
            else
            {
                failures.Add($"SetOutputReport/{report.Length}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }

            if (TryWriteReport(handle, report, out var written, out var writeError))
            {
                method = string.IsNullOrWhiteSpace(setOutputMethod)
                    ? $"WriteFile ({written} bytes)"
                    : $"{setOutputMethod} + WriteFile ({written} bytes)";
                return true;
            }

            failures.Add($"WriteFile/{report.Length}: {writeError}");
        }

        if (!string.IsNullOrWhiteSpace(fallbackSetOutputMethod))
        {
            method = fallbackSetOutputMethod;
            return true;
        }

        return false;
    }

    private IEnumerable<byte[]> BuildActivationReports()
    {
        // WebHID sendReport(0, Uint8Array(64)) is report-id 0 plus a 64-byte payload.
        var webHidEquivalent = new byte[65];
        Array.Copy(ActivationPayload, 0, webHidEquivalent, 1, ActivationPayload.Length);
        yield return webHidEquivalent;

        if (_outputReportLength > 0 && _outputReportLength != webHidEquivalent.Length)
        {
            var capsLengthReport = new byte[Math.Max(_outputReportLength, (ushort)(ActivationPayload.Length + 1))];
            Array.Copy(ActivationPayload, 0, capsLengthReport, 1, ActivationPayload.Length);
            yield return capsLengthReport;
        }

        var payloadAtZero = new byte[Math.Max(_outputReportLength, (ushort)64)];
        Array.Copy(ActivationPayload, 0, payloadAtZero, 0, ActivationPayload.Length);
        yield return payloadAtZero;
    }

    private static unsafe bool TryReadInputReport(
        SafeFileHandle handle,
        byte[] buffer,
        CancellationToken token,
        out uint bytesRead,
        out string? errorMessage)
    {
        return TryOverlappedIo(
            handle,
            buffer,
            (SafeFileHandle file, IntPtr pinnedBuffer, uint length, NativeOverlapped* overlapped, out uint transferred) =>
                ReadFile(file, pinnedBuffer, length, out transferred, overlapped),
            token,
            Timeout.InfiniteTimeSpan,
            out bytesRead,
            out errorMessage);
    }

    private static unsafe bool TryWriteReport(
        SafeFileHandle handle,
        byte[] report,
        out uint bytesWritten,
        out string? errorMessage)
    {
        return TryOverlappedIo(
            handle,
            report,
            (SafeFileHandle file, IntPtr pinnedBuffer, uint length, NativeOverlapped* overlapped, out uint transferred) =>
                WriteFile(file, pinnedBuffer, length, out transferred, overlapped),
            CancellationToken.None,
            TimeSpan.FromMilliseconds(250),
            out bytesWritten,
            out errorMessage);
    }

    private static unsafe bool TryOverlappedIo(
        SafeFileHandle handle,
        byte[] buffer,
        OverlappedIoOperation operation,
        CancellationToken token,
        TimeSpan timeout,
        out uint transferred,
        out string? errorMessage)
    {
        transferred = 0;
        errorMessage = null;
        if (handle.IsInvalid || handle.IsClosed)
        {
            errorMessage = AppText.Get("InvalidHandle");
            return false;
        }

        using var completed = new ManualResetEvent(false);
        var nativeOverlapped = new NativeOverlapped
        {
            EventHandle = completed.SafeWaitHandle.DangerousGetHandle()
        };
        var pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            var overlappedPointer = &nativeOverlapped;
            if (operation(handle, pinnedBuffer.AddrOfPinnedObject(), (uint)buffer.Length, overlappedPointer, out transferred))
            {
                return true;
            }

            var error = Marshal.GetLastWin32Error();
            if (error != ErrorIoPending)
            {
                errorMessage = new Win32Exception(error).Message;
                return false;
            }

            var completedIndex = timeout == Timeout.InfiniteTimeSpan
                ? WaitHandle.WaitAny([completed, token.WaitHandle])
                : WaitHandle.WaitAny([completed, token.WaitHandle], timeout);

            if (completedIndex != 0)
            {
                _ = CancelIoEx(handle, overlappedPointer);
                try
                {
                    _ = GetOverlappedResult(handle, overlappedPointer, out _, true);
                }
                catch (ObjectDisposedException)
                {
                    // Closing the HID handle is also a valid cancellation path.
                }

                errorMessage = completedIndex == WaitHandle.WaitTimeout
                    ? AppText.Get("IoTimeout")
                    : new OperationCanceledException().Message;
                return false;
            }

            if (GetOverlappedResult(handle, overlappedPointer, out transferred, false))
            {
                return true;
            }

            errorMessage = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }
        finally
        {
            pinnedBuffer.Free();
        }
    }

    private unsafe delegate bool OverlappedIoOperation(
        SafeFileHandle handle,
        IntPtr buffer,
        uint length,
        NativeOverlapped* overlapped,
        out uint transferred);

    private static SafeFileHandle OpenDevice(string path, uint desiredAccess)
    {
        return CreateFile(path, desiredAccess, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
    }

    private static SafeFileHandle OpenDevice(string path, uint desiredAccess, uint shareMode)
    {
        return OpenDevice(path, desiredAccess, shareMode, FileFlagOverlapped);
    }

    private static SafeFileHandle OpenDevice(string path, uint desiredAccess, uint shareMode, uint flagsAndAttributes)
    {
        return CreateFile(path, desiredAccess, shareMode, IntPtr.Zero, OpenExisting, flagsAndAttributes, IntPtr.Zero);
    }

    private static bool TryGetDeviceKind(ushort vendorId, ushort productId, out Oa1DeviceKind kind)
    {
        if (vendorId == TargetVendorId)
        {
            kind = Oa1DeviceKind.Oa1;
            return true;
        }

        if (vendorId == WitrnK2VendorId && productId == WitrnK2ProductId)
        {
            kind = Oa1DeviceKind.WitrnK2;
            return true;
        }

        kind = Oa1DeviceKind.Oa1;
        return false;
    }

    private void Log(string message)
    {
        LogReceived?.Invoke(this, $"{DateTime.Now:HH:mm:ss}  {message}");
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetProductString(SafeFileHandle hidDeviceObject, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

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
    private static extern unsafe bool ReadFile(SafeFileHandle file, IntPtr buffer, uint numberOfBytesToRead, out uint numberOfBytesRead, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool WriteFile(SafeFileHandle file, IntPtr buffer, uint numberOfBytesToWrite, out uint numberOfBytesWritten, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool CancelIoEx(SafeFileHandle file, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool GetOverlappedResult(SafeFileHandle file, NativeOverlapped* overlapped, out uint numberOfBytesTransferred, bool wait);

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

