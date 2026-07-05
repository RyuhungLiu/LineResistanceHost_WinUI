using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LineResistanceHost.Models;
using LineResistanceHost.Services;

namespace LineResistanceHost;

public sealed partial class MainPage : Page
{
    private readonly Oa1HidService _hidService = new();
    private readonly DispatcherTimer _autoConnectTimer = new();
    private readonly object _logSync = new();
    private readonly string _logPath = CreateLogPath();
    private int _frameCount;
    private bool _hold;
    private bool _connectAttemptActive;
    private DateTimeOffset _lastAutoFailure = DateTimeOffset.MinValue;
    private string? _lastAutoFailureMessage;
    private DateTimeOffset _lastAutoAttemptLog = DateTimeOffset.MinValue;
    private string? _lastAutoAttemptPath;
    private Oa1Frame _currentFrame = Oa1Frame.Empty;
    private bool _applyingLocalization;
    private bool _isDisposed;

    public MainPage()
    {
        InitializeComponent();

        _hidService.FrameReceived += HidService_FrameReceived;
        _hidService.LogReceived += HidService_LogReceived;
        _hidService.Disconnected += HidService_Disconnected;
        AppText.LanguageChanged += AppText_LanguageChanged;
        Unloaded += MainPage_Unloaded;

        _autoConnectTimer.Interval = TimeSpan.FromMilliseconds(50);
        _autoConnectTimer.Tick += AutoConnectTimer_Tick;
        _autoConnectTimer.Start();

        ApplyLocalization();
        UpdateFrame(Oa1Frame.Empty);
        UpdateThreshold();
        RefreshDevices(false);
        AddLog(AppText.Get("AppStarted"));
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        DisposePageResources();
    }

    private void DisposePageResources()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _autoConnectTimer.Stop();
        _autoConnectTimer.Tick -= AutoConnectTimer_Tick;
        _hidService.FrameReceived -= HidService_FrameReceived;
        _hidService.LogReceived -= HidService_LogReceived;
        _hidService.Disconnected -= HidService_Disconnected;
        AppText.LanguageChanged -= AppText_LanguageChanged;
        Unloaded -= MainPage_Unloaded;
        _hidService.Dispose();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDevices(true);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceComboBox.SelectedItem is Oa1DeviceInfo device)
        {
            await ConnectAsync(device, false);
        }
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _hidService.Disconnect();
        App.MainWindow?.SetConnectionTitle(null);
        SetConnection(false, AppText.Get("DisconnectedMessage"), InfoBarSeverity.Informational);
    }

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _hidService.SendActivationAsync();
            ShowStatus(AppText.Get("ActivationSentTitle"), AppText.Get("ActivationSentMessage"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(AppText.Get("ActivationFailedTitle"), ex.Message, InfoBarSeverity.Error);
            AddLog(ex.Message);
        }
    }

    private void HoldButton_Toggled(object sender, RoutedEventArgs e)
    {
        _hold = HoldButton.IsChecked == true;
        AddLog(AppText.Get(_hold ? "HoldOn" : "HoldOff"));
    }

    private void SampleButton_Click(object sender, RoutedEventArgs e)
    {
        _frameCount++;
        UpdateFrame(Oa1Frame.CreateSample());
        AddLog(AppText.Get("SampleLoaded"));
    }

    private void ThemeSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        var theme = ThemeSwitch.IsOn ? ElementTheme.Dark : ElementTheme.Light;
        RootLayout.RequestedTheme = theme;
        if (XamlRoot?.Content is FrameworkElement windowRoot)
        {
            windowRoot.RequestedTheme = theme;
        }

        UpdateFrame(_currentFrame);
    }

    private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackdropComboBox.SelectedItem is ComboBoxItem item && item.Tag is string style)
        {
            App.MainWindow?.SetBackdropStyle(style);
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingLocalization || LanguageComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
        {
            return;
        }

        if (Enum.TryParse<AppLanguage>(tag, out var language))
        {
            AppText.SetLanguage(language);
        }
    }

    private void LengthSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (LengthText is null)
        {
            return;
        }

        UpdateThreshold();
        if (double.TryParse(TotalText.Text, out var total))
        {
            UpdateTotalCard(total);
        }
    }

    private async void AutoConnectTimer_Tick(object? sender, object e)
    {
        if (!AutoConnectSwitch.IsOn || _hidService.IsConnected || _connectAttemptActive)
        {
            return;
        }

        try
        {
            _connectAttemptActive = true;
            var devices = RefreshDevices(false);
            foreach (var device in devices)
            {
                LogAutoAttempt(device);
                if (await ConnectAsync(device, true))
                {
                    break;
                }
            }
        }
        finally
        {
            _connectAttemptActive = false;
        }
    }

    private async Task<bool> ConnectAsync(Oa1DeviceInfo device, bool automatic)
    {
        try
        {
            await _hidService.ConnectAsync(device);
            App.MainWindow?.SetConnectionTitle(device.Kind);
            SetConnection(true, AppText.Format("DeviceConnectedMessage", device.DisplayName), InfoBarSeverity.Success);
            return true;
        }
        catch (Exception ex)
        {
            if (automatic)
            {
                if (!ShouldSuppressAutoFailure(ex.Message))
                {
                    AddLog(AppText.Format("AutoConnectFailed", device.DisplayName, ex.Message));
                }

                return false;
            }

            SetConnection(false, ex.Message, InfoBarSeverity.Error);
            App.MainWindow?.SetConnectionTitle(null);
            AddLog(ex.Message);
            return false;
        }
    }

    private bool ShouldSuppressAutoFailure(string message)
    {
        var now = DateTimeOffset.Now;
        if (_lastAutoFailureMessage == message && now - _lastAutoFailure < TimeSpan.FromSeconds(2))
        {
            return true;
        }

        _lastAutoFailure = now;
        _lastAutoFailureMessage = message;
        return false;
    }

    private void LogAutoAttempt(Oa1DeviceInfo device)
    {
        var now = DateTimeOffset.Now;
        if (_lastAutoAttemptPath == device.Path && now - _lastAutoAttemptLog < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _lastAutoAttemptPath = device.Path;
        _lastAutoAttemptLog = now;
        AddLog(AppText.Format("AutoAttempt", device.DisplayName));
    }

    private IReadOnlyList<Oa1DeviceInfo> RefreshDevices(bool showStatus)
    {
        var selectedPath = (DeviceComboBox.SelectedItem as Oa1DeviceInfo)?.Path;
        var devices = _hidService.FindDevices();
        DeviceComboBox.ItemsSource = devices;
        DeviceComboBox.SelectedItem = devices.FirstOrDefault(device => device.Path == selectedPath) ?? devices.FirstOrDefault();

        if (showStatus)
        {
            ShowStatus(
                devices.Count == 0 ? AppText.Get("NoDeviceTitle") : AppText.Get("DevicesRefreshedTitle"),
                devices.Count == 0 ? AppText.Get("InsertDeviceMessage") : AppText.Format("DeviceCountMessage", devices.Count),
                devices.Count == 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }

        return devices;
    }

    private void HidService_FrameReceived(object? sender, Oa1Frame frame)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_hold)
            {
                return;
            }

            _frameCount++;
            UpdateFrame(frame);
        });
    }

    private void HidService_LogReceived(object? sender, string message)
    {
        _ = DispatcherQueue.TryEnqueue(() => AddLog(message));
    }

    private void HidService_Disconnected(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            App.MainWindow?.SetConnectionTitle(null);
            SetConnection(false, AppText.Get("ReadInterruptedMessage"), InfoBarSeverity.Warning);
            RefreshDevices(false);
        });
    }

    private void AppText_LanguageChanged(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(ApplyLocalization);
    }

    private void ApplyLocalization()
    {
        _applyingLocalization = true;
        try
        {
            LanguageComboBox.SelectedIndex = AppText.CurrentLanguage switch
            {
                AppLanguage.TraditionalChinese => 1,
                AppLanguage.English => 2,
                _ => 0
            };
        }
        finally
        {
            _applyingLocalization = false;
        }

        BusResistanceTitleText.Text = AppText.Get("BusResistance");
        CableLengthLabelText.Text = AppText.Get("CableLength");
        CableSectionTitleText.Text = AppText.Get("CableSection");
        CableTypeLabelText.Text = AppText.Get("CableType");
        PinContinuityTitleText.Text = AppText.Get("PinContinuity");
        RawFrameTitleText.Text = AppText.Get("RawFrame");
        DeviceCaptureTitleText.Text = AppText.Get("DeviceCapture");
        DeviceComboBox.Header = AppText.Get("TargetHidDevice");
        DeviceComboBox.PlaceholderText = AppText.Get("NoDeviceFound");
        AutoConnectSwitch.Header = AppText.Get("Preempt");
        FrameCountLabelText.Text = AppText.Get("FrameCount");
        ChecksumLabelText.Text = AppText.Get("Checksum");
        RefreshButtonText.Text = AppText.Get("Refresh");
        ConnectButtonText.Text = AppText.Get("Connect");
        ActivateButtonText.Text = AppText.Get("Activate");
        DisconnectButtonText.Text = AppText.Get("Disconnect");
        SampleFrameButtonText.Text = AppText.Get("SampleFrame");
        AppearanceTitleText.Text = AppText.Get("Appearance");
        BackdropComboBox.Header = AppText.Get("BackdropMaterial");
        ThemeSwitch.Header = AppText.Get("DarkMode");
        ThemeSwitch.OffContent = AppText.Get("Off");
        ThemeSwitch.OnContent = AppText.Get("On");
        LanguageComboBox.Header = AppText.Get("Language");
        UpdateFrame(_currentFrame);
    }

    private void UpdateFrame(Oa1Frame frame)
    {
        _currentFrame = frame;
        VbusText.Text = FormatMilliOhms(frame.VbusMilliOhms, frame.SplitValuesEstimated);
        GbusText.Text = FormatMilliOhms(frame.GbusMilliOhms, frame.SplitValuesEstimated);
        TotalText.Text = FormatMilliOhms(frame.TotalMilliOhms);
        TotalUnitText.Visibility = frame.TotalMilliOhms.HasValue ? Visibility.Visible : Visibility.Collapsed;
        VbusUnitText.Visibility = frame.VbusMilliOhms.HasValue ? Visibility.Visible : Visibility.Collapsed;
        GbusUnitText.Visibility = frame.GbusMilliOhms.HasValue ? Visibility.Visible : Visibility.Collapsed;
        CableText.Text = frame.Cable;
        FrameCountText.Text = _frameCount.ToString();
        ChecksumText.Text = frame.Raw.Length == 0
            ? AppText.Get("WaitingData")
            : !frame.ChecksumApplicable
                ? "N/A"
            : frame.ChecksumValid && frame.TailValid
                ? AppText.Get("Correct")
                : AppText.Get("Wrong");
        RawFrameText.Text = BuildRawFrameText(frame);

        SetPin("DP", frame, DpBadge, DpText);
        SetPin("DM", frame, DmBadge, DmText);
        SetPin("C1", frame, C1Badge, C1Text);
        SetPin("C2", frame, C2Badge, C2Text);
        SetPin("S1", frame, S1Badge, S1Text);
        SetPin("S2", frame, S2Badge, S2Text);
        UpdateTotalCard(frame.TotalMilliOhms);
    }

    private void SetPin(string pin, Oa1Frame frame, Border badge, TextBlock text)
    {
        if (!frame.PinsApplicable)
        {
            text.Text = $"{pin} N/A";
            badge.Background = GetBrushResource("PinDisconnectedBrush");
            text.Foreground = GetBrushResource("OnGlassPrimaryTextBrush");
            return;
        }

        var connected = frame.Pins.TryGetValue(pin, out var value) && value;
        text.Text = $"{pin} {AppText.Get(connected ? "PinConnected" : "PinDisconnected")}";
        badge.Background = GetBrushResource(connected ? "PinConnectedBrush" : "PinDisconnectedBrush");
        text.Foreground = GetBrushResource("OnGlassPrimaryTextBrush");
    }

    private void UpdateThreshold()
    {
        var length = LengthSlider.Value;
        LengthText.Text = $"{length:F1} m";
        ThresholdText.Text = string.Empty;
    }

    private void UpdateTotalCard(double? totalMilliOhms)
    {
        var (low, high) = ResistanceRange(LengthSlider.Value);
        var ratio = totalMilliOhms.HasValue
            ? Math.Clamp((totalMilliOhms.Value - low) / (high - low), 0, 1)
            : 0;
        var color = LerpColor(
            GetColorResource("ResistanceGoodColor", ColorHelper.FromArgb(255, 12, 151, 116)),
            GetColorResource("ResistanceBadColor", ColorHelper.FromArgb(255, 203, 63, 45)),
            ratio);
        var heroStart = GetColorResource("HeroGradientStartColor", ColorHelper.FromArgb(255, 49, 80, 110));
        var heroEnd = GetColorResource("HeroGradientEndColor", ColorHelper.FromArgb(255, 6, 27, 59));
        var hasFrame = _currentFrame.Raw.Length > 0;
        var start = hasFrame ? LerpColor(heroStart, color, 0.48) : heroStart;
        var end = hasFrame ? LerpColor(heroEnd, color, 0.22) : heroEnd;

        TotalCard.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = AdjustBrightness(start, 8), Offset = 0 },
                new GradientStop { Color = end, Offset = 1 }
            }
        };
    }

    private static string FormatMilliOhms(double? value, bool estimated = false)
    {
        if (!value.HasValue)
        {
            return "N/A";
        }

        var prefix = estimated ? "?" : string.Empty;
        if (value.Value > 1000)
        {
            return $"{prefix}>1000";
        }

        return prefix + value.Value.ToString(value.Value % 1 == 0 ? "0" : "0.0");
    }

    private static string BuildRawFrameText(Oa1Frame frame)
    {
        if (frame.Raw.Length == 0)
        {
            return "-";
        }

        if (frame.SourceKind != Oa1DeviceKind.WitrnK2 || !frame.SplitValuesEstimated)
        {
            return frame.RawHex;
        }

        var confidence = frame.SplitEstimateExtrapolated
            ? $" | {AppText.Get("K2EstimateLowConfidence")}"
            : string.Empty;
        return string.Join(
            " | ",
            $"D+={frame.DPlusVolts:F4}V",
            $"D-={frame.DMinusVolts:F4}V",
            $"I={frame.CurrentAmps:F4}A",
            $"R_total={frame.TotalMilliOhms:F1}mΩ",
            $"alpha={frame.EstimatedAlpha:F3}",
            $"V0={frame.EstimatedV0Volts:F4}V",
            $"?VCC/VBUS={frame.VbusMilliOhms:F1}mΩ",
            $"?GND/GBUS={frame.GbusMilliOhms:F1}mΩ",
            $"{AppText.Get("K2EstimateNote")}{confidence}",
            frame.RawHex);
    }

    private static (double Low, double High) ResistanceRange(double lengthMeters)
    {
        (double Length, double Low, double High)[] points =
        [
            (0.3, 40, 120),
            (1.0, 80, 180),
            (1.5, 100, 200),
            (2.0, 140, 220),
            (3.0, 180, 300)
        ];

        if (lengthMeters <= points[0].Length)
        {
            return (points[0].Low, points[0].High);
        }

        if (lengthMeters >= points[^1].Length)
        {
            return (points[^1].Low, points[^1].High);
        }

        for (var index = 1; index < points.Length; index++)
        {
            if (lengthMeters <= points[index].Length)
            {
                var previous = points[index - 1];
                var current = points[index];
                var amount = (lengthMeters - previous.Length) / (current.Length - previous.Length);
                return (Lerp(previous.Low, current.Low, amount), Lerp(previous.High, current.High, amount));
            }
        }

        return (points[^1].Low, points[^1].High);
    }

    private static double Lerp(double start, double end, double amount)
    {
        return start + (end - start) * amount;
    }

    private static Windows.UI.Color LerpColor(Windows.UI.Color start, Windows.UI.Color end, double amount)
    {
        return ColorHelper.FromArgb(
            255,
            (byte)Lerp(start.R, end.R, amount),
            (byte)Lerp(start.G, end.G, amount),
            (byte)Lerp(start.B, end.B, amount));
    }

    private static Windows.UI.Color AdjustBrightness(Windows.UI.Color color, int amount)
    {
        return ColorHelper.FromArgb(
            color.A,
            (byte)Math.Clamp(color.R + amount, 0, 255),
            (byte)Math.Clamp(color.G + amount, 0, 255),
            (byte)Math.Clamp(color.B + amount, 0, 255));
    }

    private SolidColorBrush GetBrushResource(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    private Windows.UI.Color GetColorResource(string key, Windows.UI.Color fallback)
    {
        return Application.Current.Resources.TryGetValue(key, out var value) && value is Windows.UI.Color color
            ? color
            : fallback;
    }

    private void SetConnection(bool connected, string message, InfoBarSeverity severity)
    {
        ConnectionText.Text = AppText.Get(connected ? "Connected" : "Disconnected");
        ShowStatus(connected ? AppText.Get("DeviceConnectedTitle") : AppText.Get("DeviceDisconnectedTitle"), message, severity);
        AddLog(message);
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private void AddLog(string message)
    {
        WriteFileLog(message);
        LogList.Items.Insert(0, message);
        while (LogList.Items.Count > 80)
        {
            LogList.Items.RemoveAt(LogList.Items.Count - 1);
        }
    }

    private void WriteFileLog(string message)
    {
        try
        {
            lock (_logSync)
            {
                File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging should never interrupt device capture.
        }
    }

    private static string CreateLogPath()
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        return Path.Combine(logDirectory, $"line-resistance-host-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }
}

