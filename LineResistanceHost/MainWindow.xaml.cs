using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using LineResistanceHost.Models;
using LineResistanceHost.Services;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace LineResistanceHost;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private Oa1DeviceKind? _connectionKind;

    public MainWindow()
    {
        InitializeComponent();
        AppText.LanguageChanged += AppText_LanguageChanged;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        SetConnectionTitle(null);
        ResizeWindow(520, 940);

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    public void SetConnectionTitle(Oa1DeviceKind? kind)
    {
        _connectionKind = kind;
        var title = kind switch
        {
            Oa1DeviceKind.Oa1 => AppText.Get("AppTitleOa1Connected"),
            Oa1DeviceKind.WitrnK2 => AppText.Get("AppTitleK2Connected"),
            _ => AppText.Get("AppTitleDisconnected")
        };

        Title = title;
        ConnectionTitleText.Text = title;
    }

    private void AppText_LanguageChanged(object? sender, EventArgs e)
    {
        SetConnectionTitle(_connectionKind);
    }

    public void SetBackdropStyle(string style)
    {
        SystemBackdrop = style switch
        {
            "Mica" => new MicaBackdrop { Kind = MicaKind.Base },
            "MicaAlt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            "None" => null,
            _ => new DesktopAcrylicBackdrop()
        };
    }

    private void ResizeWindow(int widthDip, int heightDip)
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(widthDip * scale), (int)(heightDip * scale)));
    }
}

