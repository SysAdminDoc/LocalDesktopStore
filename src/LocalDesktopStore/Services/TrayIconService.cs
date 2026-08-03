using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalDesktopStore.Services;

/// <summary>
/// Small native notification-area icon used by the scheduled update worker. It deliberately
/// uses Shell_NotifyIcon instead of a UI toolkit or a third-party notification package.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;
    private const int WmApp = 0x8000;
    private const int WmLButtonDblClk = 0x0203;
    private const uint IconId = 1;
    private const uint NotifyIconVersion4 = 4;

    private readonly uint _callbackMessage = WmApp + 0x4D;
    private HwndSource? _source;
    private NotifyIconData _data;
    private bool _iconAdded;

    public event EventHandler? Activated;

    public TrayIconService()
    {
        var parameters = new HwndSourceParameters("LocalDesktopStore.NotificationArea")
        {
            Width = 1,
            Height = 1,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _source.Handle,
            uID = IconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = _callbackMessage,
            hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)),
            szTip = "LocalDesktopStore",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };

        _iconAdded = Shell_NotifyIcon(NimAdd, ref _data);
        if (_iconAdded)
        {
            _data.uVersion = NotifyIconVersion4;
            _ = Shell_NotifyIcon(NimSetVersion, ref _data);
        }
    }

    public void ShowUpdateNotification(int updateCount)
    {
        if (updateCount < 1 || !_iconAdded)
            return;

        void Show()
        {
            if (!_iconAdded) return;
            _data.uFlags = NifInfo;
            _data.szInfoTitle = "LocalDesktopStore updates";
            _data.szInfo = updateCount == 1
                ? "1 installed app has an update available."
                : $"{updateCount} installed apps have updates available.";
            _data.dwInfoFlags = NiifInfo;
            _ = Shell_NotifyIcon(NimModify, ref _data);
        }

        InvokeOnDispatcher(Show);
    }

    public void Dispose()
    {
        var source = _source;
        _source = null;
        if (source is null) return;

        void Remove()
        {
            if (_iconAdded)
            {
                _ = Shell_NotifyIcon(NimDelete, ref _data);
                _iconAdded = false;
            }
            source.RemoveHook(WndProc);
            source.Dispose();
        }

        try
        {
            if (source.Dispatcher.CheckAccess()) Remove();
            else source.Dispatcher.Invoke(Remove);
        }
        catch { /* shutdown cleanup should not mask the app exit */ }
    }

    private void InvokeOnDispatcher(Action action)
    {
        var source = _source;
        if (source is null) return;
        try
        {
            if (source.Dispatcher.CheckAccess()) action();
            else source.Dispatcher.BeginInvoke(action);
        }
        catch { /* notification is best effort */ }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _callbackMessage
            && unchecked((uint)wParam.ToInt64()) == IconId
            && lParam.ToInt64() == WmLButtonDblClk)
        {
            Activated?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr resource);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
}
