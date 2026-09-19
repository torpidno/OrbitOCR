using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using OrbitOCR.Models;

namespace OrbitOCR.Services;

public class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private const int HotkeyId = 9001;

    private HwndSource? _hwndSource;
    private bool _isRegistered;
    private readonly SettingsService _settingsService;

    public event Action? HotkeyTriggered;

    public HotkeyService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeHwndSource();
        RegisterFromSettings(_settingsService.Settings);

        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    private void InitializeHwndSource()
    {
        var parameters = new HwndSourceParameters("OrbitOCR_HotkeyReceiver")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(HwndHook);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyTriggered?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        RegisterFromSettings(settings);
    }

    public bool RegisterFromSettings(AppSettings settings)
    {
        Unregister();

        if (_hwndSource == null) return false;

        uint modifiers = MOD_NOREPEAT;
        if (settings.HotkeyCtrl) modifiers |= MOD_CONTROL;
        if (settings.HotkeyShift) modifiers |= MOD_SHIFT;
        if (settings.HotkeyAlt) modifiers |= MOD_ALT;
        if (settings.HotkeyWin) modifiers |= MOD_WIN;

        uint vk = ParseVirtualKey(settings.HotkeyKey);
        if (vk == 0)
        {
            vk = 0x53; // 'S' key
        }

        _isRegistered = RegisterHotKey(_hwndSource.Handle, HotkeyId, modifiers, vk);
        return _isRegistered;
    }

    public void Unregister()
    {
        if (_isRegistered && _hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            _isRegistered = false;
        }
    }

    private static uint ParseVirtualKey(string keyName)
    {
        if (Enum.TryParse<Key>(keyName, true, out var key))
        {
            return (uint)KeyInterop.VirtualKeyFromKey(key);
        }

        if (keyName.Length == 1)
        {
            char c = char.ToUpperInvariant(keyName[0]);
            if (c >= 'A' && c <= 'Z') return (uint)c;
            if (c >= '0' && c <= '9') return (uint)c;
        }

        return 0;
    }

    public void Dispose()
    {
        Unregister();
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(HwndHook);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
        GC.SuppressFinalize(this);
    }
}
