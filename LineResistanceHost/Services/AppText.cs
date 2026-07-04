using System.Globalization;

namespace LineResistanceHost.Services;

public enum AppLanguage
{
    SimplifiedChinese,
    TraditionalChinese,
    English
}

public sealed record AppLanguageOption(AppLanguage Language, string NativeName);

public static class AppText
{
    private static readonly string LanguageSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LineResistanceHost",
        "language.txt");

    private static readonly IReadOnlyDictionary<AppLanguage, IReadOnlyDictionary<string, string>> Resources =
        new Dictionary<AppLanguage, IReadOnlyDictionary<string, string>>
        {
            [AppLanguage.SimplifiedChinese] = new Dictionary<string, string>
            {
                ["AppTitleDisconnected"] = "未连接",
                ["AppTitleOa1Connected"] = "ATK-OA1已连接",
                ["AppTitleK2Connected"] = "K2已连接",
                ["WindowTitle"] = "Line Resistance Host",
                ["BusResistance"] = "总线阻",
                ["CableLength"] = "线材长度",
                ["CableSection"] = "线材",
                ["CableType"] = "类型",
                ["PinContinuity"] = "引脚通断",
                ["RawFrame"] = "原始帧",
                ["DeviceCapture"] = "设备与采集",
                ["TargetHidDevice"] = "目标 HID 设备",
                ["NoDeviceFound"] = "未发现设备",
                ["Preempt"] = "抢占",
                ["FrameCount"] = "帧数",
                ["Checksum"] = "校验",
                ["Refresh"] = "刷新",
                ["Connect"] = "连接",
                ["Activate"] = "激活",
                ["Disconnect"] = "断开",
                ["SampleFrame"] = "模拟帧",
                ["Appearance"] = "外观",
                ["BackdropMaterial"] = "背景材质",
                ["DarkMode"] = "深色模式",
                ["Off"] = "关",
                ["On"] = "开",
                ["Language"] = "语言",
                ["Waiting"] = "等待",
                ["WaitingData"] = "等待数据",
                ["Correct"] = "正确",
                ["Wrong"] = "错误",
                ["Connected"] = "已连接",
                ["Disconnected"] = "未连接",
                ["PinConnected"] = "通",
                ["PinDisconnected"] = "断",
                ["AppStarted"] = "本地上位机已启动，目标 VID_36B7 / VID_0716 PID_5060。",
                ["DisconnectedMessage"] = "已断开连接。",
                ["ActivationSentTitle"] = "激活命令已发送",
                ["ActivationSentMessage"] = "设备已切换到持续输出模式。",
                ["ActivationFailedTitle"] = "激活失败",
                ["HoldOn"] = "Hold 已开启，暂停刷新帧显示。",
                ["HoldOff"] = "Hold 已关闭，恢复实时刷新。",
                ["SampleLoaded"] = "已载入模拟帧。",
                ["DeviceConnectedMessage"] = "已连接 {0}",
                ["AutoConnectFailed"] = "自动连接 {0} 失败：{1}",
                ["AutoAttempt"] = "自动抢占发现 {0}，准备连接。",
                ["NoDeviceTitle"] = "未发现设备",
                ["DevicesRefreshedTitle"] = "已刷新设备",
                ["InsertDeviceMessage"] = "请插入 ATK-OA1 或 POWER-Z K2。",
                ["DeviceCountMessage"] = "发现 {0} 个目标设备。",
                ["ReadInterruptedMessage"] = "设备读取中断或已拔出。",
                ["DeviceConnectedTitle"] = "设备已连接",
                ["DeviceDisconnectedTitle"] = "设备未连接",
                ["PrimeActivationSent"] = "已发送抢占激活，等待设备重新枚举为数据接口。",
                ["OpenHidFailed"] = "无法打开 HID 设备",
                ["ConnectedLog"] = "已连接 {0} {1}",
                ["ConnectPreActivation"] = "连接预激活",
                ["K2NoActivation"] = "POWER-Z K2 无需激活，开始读取 Metrics Report。",
                ["DeviceNotConnected"] = "设备未连接",
                ["ActivationNotRequired"] = "当前设备无需激活",
                ["ReadInterruptedLog"] = "读取中断：{0}",
                ["FrameReceivedLog"] = "收到数据帧 #{0}: {1}",
                ["ReportLengthLog"] = "HID 报告长度 Input={0}, Output={1}, Feature={2}",
                ["CapsFailedLog"] = "HidP_GetCaps 失败，status=0x{0:X8}",
                ["ExclusiveReadWrite"] = "独占读写",
                ["ExclusiveRead"] = "独占只读",
                ["OpenHidFailedWithDetails"] = "无法打开 HID 设备：{0}",
                ["PrimeActivationWriteOnly"] = "已发送抢占激活 {0} (只写句柄)",
                ["PrimeActivationFailed"] = "只写抢占失败：{0}",
                ["ActivationSendFailedKeep"] = "激活命令发送失败，保持连接等待数据：{0}",
                ["SourceSent"] = "{0}已发送 {1}",
                ["SourceFailedKeep"] = "{0}失败，继续保持连接：{1}",
                ["ActivationSentLog"] = "已发送激活命令 {0}",
                ["ActivationSentHelper"] = "已发送激活命令 {0} (辅助写句柄)",
                ["HelperWriteHandle"] = "辅助写句柄: {0}",
                ["ActivationSendFailed"] = "激活命令发送失败：{0}",
                ["InvalidHandle"] = "句柄无效",
                ["K2EstimateNote"] = "经验估算，非 K2 HID 直接上报的原始分离值",
                ["K2EstimateLowConfidence"] = "外推，可信度下降"
            },
            [AppLanguage.TraditionalChinese] = new Dictionary<string, string>
            {
                ["AppTitleDisconnected"] = "未連接",
                ["AppTitleOa1Connected"] = "ATK-OA1已連接",
                ["AppTitleK2Connected"] = "K2已連接",
                ["WindowTitle"] = "Line Resistance Host",
                ["BusResistance"] = "總線阻",
                ["CableLength"] = "線材長度",
                ["CableSection"] = "線材",
                ["CableType"] = "類型",
                ["PinContinuity"] = "引腳通斷",
                ["RawFrame"] = "原始幀",
                ["DeviceCapture"] = "設備與擷取",
                ["TargetHidDevice"] = "目標 HID 設備",
                ["NoDeviceFound"] = "未發現設備",
                ["Preempt"] = "搶占",
                ["FrameCount"] = "幀數",
                ["Checksum"] = "校驗",
                ["Refresh"] = "重新整理",
                ["Connect"] = "連接",
                ["Activate"] = "激活",
                ["Disconnect"] = "斷開",
                ["SampleFrame"] = "模擬幀",
                ["Appearance"] = "外觀",
                ["BackdropMaterial"] = "背景材質",
                ["DarkMode"] = "深色模式",
                ["Off"] = "關",
                ["On"] = "開",
                ["Language"] = "語言",
                ["Waiting"] = "等待",
                ["WaitingData"] = "等待資料",
                ["Correct"] = "正確",
                ["Wrong"] = "錯誤",
                ["Connected"] = "已連接",
                ["Disconnected"] = "未連接",
                ["PinConnected"] = "通",
                ["PinDisconnected"] = "斷",
                ["AppStarted"] = "本地上位機已啟動，目標 VID_36B7 / VID_0716 PID_5060。",
                ["DisconnectedMessage"] = "已斷開連接。",
                ["ActivationSentTitle"] = "激活命令已發送",
                ["ActivationSentMessage"] = "設備已切換到持續輸出模式。",
                ["ActivationFailedTitle"] = "激活失敗",
                ["HoldOn"] = "Hold 已開啟，暫停刷新幀顯示。",
                ["HoldOff"] = "Hold 已關閉，恢復即時刷新。",
                ["SampleLoaded"] = "已載入模擬幀。",
                ["DeviceConnectedMessage"] = "已連接 {0}",
                ["AutoConnectFailed"] = "自動連接 {0} 失敗：{1}",
                ["AutoAttempt"] = "自動搶占發現 {0}，準備連接。",
                ["NoDeviceTitle"] = "未發現設備",
                ["DevicesRefreshedTitle"] = "已重新整理設備",
                ["InsertDeviceMessage"] = "請插入 ATK-OA1 或 POWER-Z K2。",
                ["DeviceCountMessage"] = "發現 {0} 個目標設備。",
                ["ReadInterruptedMessage"] = "設備讀取中斷或已拔出。",
                ["DeviceConnectedTitle"] = "設備已連接",
                ["DeviceDisconnectedTitle"] = "設備未連接",
                ["PrimeActivationSent"] = "已發送搶占激活，等待設備重新枚舉為資料介面。",
                ["OpenHidFailed"] = "無法打開 HID 設備",
                ["ConnectedLog"] = "已連接 {0} {1}",
                ["ConnectPreActivation"] = "連接預激活",
                ["K2NoActivation"] = "POWER-Z K2 無需激活，開始讀取 Metrics Report。",
                ["DeviceNotConnected"] = "設備未連接",
                ["ActivationNotRequired"] = "目前設備無需激活",
                ["ReadInterruptedLog"] = "讀取中斷：{0}",
                ["FrameReceivedLog"] = "收到資料幀 #{0}: {1}",
                ["ReportLengthLog"] = "HID 報告長度 Input={0}, Output={1}, Feature={2}",
                ["CapsFailedLog"] = "HidP_GetCaps 失敗，status=0x{0:X8}",
                ["ExclusiveReadWrite"] = "獨占讀寫",
                ["ExclusiveRead"] = "獨占只讀",
                ["OpenHidFailedWithDetails"] = "無法打開 HID 設備：{0}",
                ["PrimeActivationWriteOnly"] = "已發送搶占激活 {0} (只寫句柄)",
                ["PrimeActivationFailed"] = "只寫搶占失敗：{0}",
                ["ActivationSendFailedKeep"] = "激活命令發送失敗，保持連接等待資料：{0}",
                ["SourceSent"] = "{0}已發送 {1}",
                ["SourceFailedKeep"] = "{0}失敗，繼續保持連接：{1}",
                ["ActivationSentLog"] = "已發送激活命令 {0}",
                ["ActivationSentHelper"] = "已發送激活命令 {0} (輔助寫句柄)",
                ["HelperWriteHandle"] = "輔助寫句柄: {0}",
                ["ActivationSendFailed"] = "激活命令發送失敗：{0}",
                ["InvalidHandle"] = "句柄無效",
                ["K2EstimateNote"] = "經驗估算，非 K2 HID 直接上報的原始分離值",
                ["K2EstimateLowConfidence"] = "外推，可信度下降"
            },
            [AppLanguage.English] = new Dictionary<string, string>
            {
                ["AppTitleDisconnected"] = "Disconnected",
                ["AppTitleOa1Connected"] = "ATK-OA1 Connected",
                ["AppTitleK2Connected"] = "K2 Connected",
                ["WindowTitle"] = "ATK-OA1 Cable Tester",
                ["BusResistance"] = "Bus R",
                ["CableLength"] = "Cable length",
                ["CableSection"] = "Cable",
                ["CableType"] = "Type",
                ["PinContinuity"] = "Pin continuity",
                ["RawFrame"] = "Raw frame",
                ["DeviceCapture"] = "Device and capture",
                ["TargetHidDevice"] = "Target HID device",
                ["NoDeviceFound"] = "No device found",
                ["Preempt"] = "Preempt",
                ["FrameCount"] = "Frames",
                ["Checksum"] = "Checksum",
                ["Refresh"] = "Refresh",
                ["Connect"] = "Connect",
                ["Activate"] = "Activate",
                ["Disconnect"] = "Disconnect",
                ["SampleFrame"] = "Sample frame",
                ["Appearance"] = "Appearance",
                ["BackdropMaterial"] = "Backdrop",
                ["DarkMode"] = "Dark mode",
                ["Off"] = "Off",
                ["On"] = "On",
                ["Language"] = "Language",
                ["Waiting"] = "Waiting",
                ["WaitingData"] = "Waiting for data",
                ["Correct"] = "OK",
                ["Wrong"] = "Error",
                ["Connected"] = "Connected",
                ["Disconnected"] = "Disconnected",
                ["PinConnected"] = "Closed",
                ["PinDisconnected"] = "Open",
                ["AppStarted"] = "Local host started. Targets: VID_36B7 / VID_0716 PID_5060.",
                ["DisconnectedMessage"] = "Disconnected.",
                ["ActivationSentTitle"] = "Activation sent",
                ["ActivationSentMessage"] = "Device switched to continuous output mode.",
                ["ActivationFailedTitle"] = "Activation failed",
                ["HoldOn"] = "Hold enabled. Frame display is paused.",
                ["HoldOff"] = "Hold disabled. Live refresh resumed.",
                ["SampleLoaded"] = "Sample frame loaded.",
                ["DeviceConnectedMessage"] = "Connected {0}",
                ["AutoConnectFailed"] = "Auto-connect to {0} failed: {1}",
                ["AutoAttempt"] = "Auto-preempt found {0}; connecting.",
                ["NoDeviceTitle"] = "No device found",
                ["DevicesRefreshedTitle"] = "Devices refreshed",
                ["InsertDeviceMessage"] = "Insert an ATK-OA1 or POWER-Z K2.",
                ["DeviceCountMessage"] = "Found {0} target device(s).",
                ["ReadInterruptedMessage"] = "Device read was interrupted or the device was removed.",
                ["DeviceConnectedTitle"] = "Device connected",
                ["DeviceDisconnectedTitle"] = "Device disconnected",
                ["PrimeActivationSent"] = "Preempt activation sent. Waiting for the device to re-enumerate as a data interface.",
                ["OpenHidFailed"] = "Unable to open HID device",
                ["ConnectedLog"] = "Connected {0} {1}",
                ["ConnectPreActivation"] = "Connect pre-activation",
                ["K2NoActivation"] = "POWER-Z K2 does not require activation. Reading Metrics Report.",
                ["DeviceNotConnected"] = "Device is not connected",
                ["ActivationNotRequired"] = "The current device does not require activation",
                ["ReadInterruptedLog"] = "Read interrupted: {0}",
                ["FrameReceivedLog"] = "Received frame #{0}: {1}",
                ["ReportLengthLog"] = "HID report length Input={0}, Output={1}, Feature={2}",
                ["CapsFailedLog"] = "HidP_GetCaps failed, status=0x{0:X8}",
                ["ExclusiveReadWrite"] = "exclusive read/write",
                ["ExclusiveRead"] = "exclusive read",
                ["OpenHidFailedWithDetails"] = "Unable to open HID device: {0}",
                ["PrimeActivationWriteOnly"] = "Preempt activation sent via {0} (write-only handle)",
                ["PrimeActivationFailed"] = "Write-only preempt failed: {0}",
                ["ActivationSendFailedKeep"] = "Activation command failed; keeping connection and waiting for data: {0}",
                ["SourceSent"] = "{0} sent via {1}",
                ["SourceFailedKeep"] = "{0} failed; keeping connection: {1}",
                ["ActivationSentLog"] = "Activation command sent via {0}",
                ["ActivationSentHelper"] = "Activation command sent via {0} (helper write handle)",
                ["HelperWriteHandle"] = "helper write handle: {0}",
                ["ActivationSendFailed"] = "Activation command failed: {0}",
                ["InvalidHandle"] = "Invalid handle",
                ["K2EstimateNote"] = "empirical estimate, not raw split values reported directly by K2 HID",
                ["K2EstimateLowConfidence"] = "extrapolated; lower confidence"
            }
        };

    public static IReadOnlyList<AppLanguageOption> Languages { get; } =
    [
        new(AppLanguage.SimplifiedChinese, "简体中文"),
        new(AppLanguage.TraditionalChinese, "繁體中文"),
        new(AppLanguage.English, "English")
    ];

    public static event EventHandler? LanguageChanged;

    public static AppLanguage CurrentLanguage { get; private set; } = LoadLanguage();

    static AppText()
    {
        ApplyCulture(CurrentLanguage);
    }

    public static string Get(string key)
    {
        if (Resources[CurrentLanguage].TryGetValue(key, out var value))
        {
            return value;
        }

        return Resources[AppLanguage.English].TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string Format(string key, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    public static void SetLanguage(AppLanguage language)
    {
        if (CurrentLanguage == language)
        {
            return;
        }

        CurrentLanguage = language;
        ApplyCulture(language);
        SaveLanguage(language);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyCulture(AppLanguage language)
    {
        var cultureName = language switch
        {
            AppLanguage.TraditionalChinese => "zh-Hant",
            AppLanguage.English => "en-US",
            _ => "zh-Hans"
        };
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private static AppLanguage LoadLanguage()
    {
        try
        {
            if (File.Exists(LanguageSettingsPath)
                && Enum.TryParse<AppLanguage>(File.ReadAllText(LanguageSettingsPath).Trim(), out var language))
            {
                return language;
            }
        }
        catch
        {
            // Localization should never prevent app startup.
        }

        return AppLanguage.SimplifiedChinese;
    }

    private static void SaveLanguage(AppLanguage language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LanguageSettingsPath)!);
            File.WriteAllText(LanguageSettingsPath, language.ToString());
        }
        catch
        {
            // Localization should keep working even if settings cannot be saved.
        }
    }
}
