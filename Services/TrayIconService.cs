using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using OrbitOCR.Utils;

namespace OrbitOCR.Services;

public class TrayIconService : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIM_SETVERSION = 0x00000004;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int NIIF_INFO = 0x00000001;

    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 101;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    private readonly IntPtr _hwnd;
    private readonly HwndSource _hwndSource;
    private NOTIFYICONDATA _nid;
    private Icon _icon;
    private ContextMenu _contextMenu;

    public event Action? TriggerSnipRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;

    public TrayIconService(string hotkeyDisplay)
    {
        var parameters = new HwndSourceParameters("OrbitOCR_TrayReceiver")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0
        };

        _hwndSource = new HwndSource(parameters);
        _hwnd = _hwndSource.Handle;
        _hwndSource.AddHook(HwndHook);

        _icon = IconHelper.GenerateAppIcon();
        _contextMenu = BuildContextMenu(hotkeyDisplay);

        InitializeTrayIcon();
    }

    public void UpdateHotkeyDisplay(string hotkeyDisplay)
    {
        _contextMenu = BuildContextMenu(hotkeyDisplay);
        UpdateTooltip($"OrbitOCR ({hotkeyDisplay}) - Circle to Search");
    }

    private void InitializeTrayIcon()
    {
        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _icon.Handle,
            szTip = "OrbitOCR - Circle to Search"
        };

        Shell_NotifyIcon(NIM_ADD, ref _nid);
    }

    public void UpdateTooltip(string text)
    {
        _nid.szTip = text.Length > 127 ? text[..127] : text;
        _nid.uFlags = NIF_TIP;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    public void ShowNotification(string title, string message)
    {
        _nid.uFlags = NIF_INFO;
        _nid.szInfoTitle = title.Length > 63 ? title[..63] : title;
        _nid.szInfo = message.Length > 255 ? message[..255] : message;
        _nid.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int lMsg = lParam.ToInt32();
            if (lMsg == WM_LBUTTONUP || lMsg == WM_LBUTTONDBLCLK)
            {
                TriggerSnipRequested?.Invoke();
                handled = true;
            }
            else if (lMsg == WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        SetForegroundWindow(_hwnd);
        _contextMenu.Placement = PlacementMode.MousePoint;
        _contextMenu.IsOpen = true;
    }

    private ContextMenu BuildContextMenu(string hotkeyDisplay)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 28, 38)),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 245)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 65, 81)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4)
        };

        var triggerItem = CreateMenuItem($"⚡  Trigger Snip ({hotkeyDisplay})", () => TriggerSnipRequested?.Invoke(), isBold: true);
        var settingsItem = CreateMenuItem("⚙  Settings...", () => OpenSettingsRequested?.Invoke());
        var aboutItem = CreateMenuItem("ℹ  About OrbitOCR", () =>
        {
            System.Windows.MessageBox.Show(
                "OrbitOCR v1.0\n\nHigh-performance, offline screen OCR and visual search utility for Windows 10/11.\nInspired by Google Pixel's Circle to Search.",
                "About OrbitOCR",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
        var exitItem = CreateMenuItem("❌  Exit", () => ExitRequested?.Invoke());

        menu.Items.Add(triggerItem);
        menu.Items.Add(new Separator { Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 50, 65)), Margin = new Thickness(4, 2, 4, 2) });
        menu.Items.Add(settingsItem);
        menu.Items.Add(aboutItem);
        menu.Items.Add(new Separator { Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 50, 65)), Margin = new Thickness(4, 2, 4, 2) });
        menu.Items.Add(exitItem);

        return menu;
    }

    private MenuItem CreateMenuItem(string header, Action onClick, bool isBold = false)
    {
        var item = new MenuItem
        {
            Header = header,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 245)),
            Padding = new Thickness(10, 6, 12, 6),
            FontWeight = isBold ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = 13
        };
        item.Click += (s, e) => onClick();
        return item;
    }

    public void Dispose()
    {
        _nid.uFlags = 0;
        Shell_NotifyIcon(NIM_DELETE, ref _nid);

        _hwndSource.RemoveHook(HwndHook);
        _hwndSource.Dispose();
        _icon.Dispose();
        GC.SuppressFinalize(this);
    }
}
