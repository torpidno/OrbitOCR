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

    public event Action? HotkeyTriggered;

    public HotkeyService(SettingsService settingsService)
    {
        InitializeHwndSource();
        RegisterFromSettings(settingsService.Settings);
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

    /// <summary>The shortcut Windows has actually accepted, or null when none is registered.</summary>
    public AppSettings? ActiveSettings { get; private set; }

    public bool RegisterFromSettings(AppSettings settings)
    {
        // Remember what is live so a rejected change can be rolled back.
        var previous = ActiveSettings;

        Unregister();

        if (_hwndSource == null) return false;

        if (TryRegister(settings))
        {
            ActiveSettings = HotkeySnapshot(settings);
            return true;
        }

        // The requested combination is unavailable. Restore the last working one so the app
        // is never left without a shortcut, and report the requested change as failed.
        if (previous != null && TryRegister(previous))
        {
            ActiveSettings = previous;
        }
        else
        {
            ActiveSettings = null;
        }

        return false;
    }

    private bool TryRegister(AppSettings settings)
    {
        uint vk = ParseVirtualKey(settings.HotkeyKey);
        if (vk == 0)
        {
            vk = 0x53; // 'S' key
        }

        _isRegistered = RegisterHotKey(_hwndSource!.Handle, HotkeyId, BuildModifiers(settings), vk);
        return _isRegistered;
    }

    private static uint BuildModifiers(AppSettings settings)
    {
        uint modifiers = MOD_NOREPEAT;
        if (settings.HotkeyCtrl) modifiers |= MOD_CONTROL;
        if (settings.HotkeyShift) modifiers |= MOD_SHIFT;
        if (settings.HotkeyAlt) modifiers |= MOD_ALT;
        if (settings.HotkeyWin) modifiers |= MOD_WIN;
        return modifiers;
    }

    /// <summary>Snapshot of just the shortcut fields, so later edits to the caller's object can't mutate it.</summary>
    private static AppSettings HotkeySnapshot(AppSettings settings) => new()
    {
        HotkeyCtrl = settings.HotkeyCtrl,
        HotkeyShift = settings.HotkeyShift,
        HotkeyAlt = settings.HotkeyAlt,
        HotkeyWin = settings.HotkeyWin,
        HotkeyKey = settings.HotkeyKey
    };

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
