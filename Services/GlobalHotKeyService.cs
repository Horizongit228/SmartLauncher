using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmartLauncher.UI.Services
{
    public sealed class GlobalHotKeyService : IDisposable
    {
        private const int HotKeyId = 0x534C;
        private const int WmHotKey = 0x0312;
        private const uint ModifierControl = 0x0002;
        private const uint VirtualKeyL = 0x4C;

        private readonly IntPtr _windowHandle;
        private readonly HwndSource _source;
        private readonly Action _callback;
        private bool _registered;

        public GlobalHotKeyService(
            Window window,
            Action callback)
        {
            _callback = callback;
            _windowHandle =
                new WindowInteropHelper(window).Handle;
            _source =
                HwndSource.FromHwnd(_windowHandle)
                ?? throw new InvalidOperationException(
                    "Не удалось получить дескриптор окна.");

            _source.AddHook(WindowProcedure);
            _registered =
                RegisterHotKey(
                    _windowHandle,
                    HotKeyId,
                    ModifierControl,
                    VirtualKeyL);

            if (!_registered)
            {
                AppLogService.Warning(
                    "Не удалось зарегистрировать глобальную горячую клавишу Ctrl+L.");
            }
        }

        public bool IsRegistered => _registered;

        private IntPtr WindowProcedure(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == WmHotKey
                && wParam.ToInt32() == HotKeyId)
            {
                handled = true;
                _callback();
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_registered)
            {
                _ = UnregisterHotKey(
                    _windowHandle,
                    HotKeyId);
                _registered = false;
            }

            _source.RemoveHook(WindowProcedure);
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(
            IntPtr windowHandle,
            int id,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(
            IntPtr windowHandle,
            int id);
    }
}
