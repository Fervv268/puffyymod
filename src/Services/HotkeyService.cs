using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace GameBuddyBrain.Services
{
    public class HotkeyService : IDisposable
    {
        private readonly IntPtr _hwnd;
        private readonly Dictionary<int, Action> _map = new();
        private int _id = 1;

        public HotkeyService(WindowInteropHelper helper)
        {
            _hwnd = helper.Handle;
            var src = HwndSource.FromHwnd(_hwnd);
            src.AddHook(WndProc);
        }

        public int Register(ModifierKeys mod, Key key, Action action)
        {
            int idx = _id++;
            _map[idx] = action;
            RegisterHotKey(_hwnd, idx, (uint)mod, (uint)KeyInterop.VirtualKeyFromKey(key));
            return idx;
        }

        public void Dispose()
        {
            foreach (var idx in _map.Keys)
                UnregisterHotKey(_hwnd, idx);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && _map.TryGetValue(wParam.ToInt32(), out var act))
            {
                act();
                handled = true;
            }
            return IntPtr.Zero;
        }

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(
            IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(
            IntPtr hWnd, int id);
    }
}
