using System;
using System.Runtime.InteropServices;

namespace GameBuddyBrain.Services
{
    // Clicks relative to a target window rectangle (0..1 normalized positions)
    public class Clicker
    {
    public enum KeyCode : byte { SPACE = 0x20, W = 0x57, A = 0x41, S = 0x53, D = 0x44, UP = 0x26, DOWN = 0x28, LEFT = 0x25, RIGHT = 0x27 }

        // Send mouse click to a window via messages without moving system cursor
        public void ClickNormalized(IntPtr hwnd, double nx, double ny, int extraDelayMs = 0)
        {
            if (!GetWindowRect(hwnd, out var r)) return;
            int absX = r.Left + (int)((r.Right - r.Left) * nx);
            int absY = r.Top + (int)((r.Bottom - r.Top) * ny);
            ClickAbsolute(absX, absY, extraDelayMs);
        }

        // Global-absolute screen click delivered to the window under the point (no cursor move)
        public void ClickAbsolute(int xScreen, int yScreen, int extraDelayMs = 0)
        {
            // Find target window under the point
            var pt = new POINT { X = xScreen, Y = yScreen };
            IntPtr target = WindowFromPoint(pt);
            if (target == IntPtr.Zero) return;
            // Use root window to avoid child-only focus issues
            IntPtr root = GetAncestor(target, GA_ROOT);
            if (root != IntPtr.Zero) target = root;

            // Convert to client coordinates for the target window
            var client = new POINT { X = xScreen, Y = yScreen };
            ScreenToClient(target, ref client);
            int lParam = (client.Y << 16) | (client.X & 0xFFFF);

            // Send button down/up
            PostMessage(target, WM_LBUTTONDOWN, (IntPtr)1, (IntPtr)lParam);
            PostMessage(target, WM_LBUTTONUP, IntPtr.Zero, (IntPtr)lParam);

            if (extraDelayMs > 0) System.Threading.Thread.Sleep(extraDelayMs);
        }

        // Send a key press to a specific window (fallback sends to foreground if hwnd==IntPtr.Zero)
        public void KeyPressTo(IntPtr hwnd, KeyCode key)
        {
            var h = hwnd;
            if (h == IntPtr.Zero) h = GetForegroundWindow();
            if (h == IntPtr.Zero) return;
            PostMessage(h, WM_KEYDOWN, (IntPtr)(byte)key, IntPtr.Zero);
            PostMessage(h, WM_KEYUP, (IntPtr)(byte)key, IntPtr.Zero);
        }

        // Key down/up for more natural movement
        public void KeyDownTo(IntPtr hwnd, KeyCode key)
        {
            var h = hwnd; if (h == IntPtr.Zero) h = GetForegroundWindow(); if (h == IntPtr.Zero) return;
            PostMessage(h, WM_KEYDOWN, (IntPtr)(byte)key, IntPtr.Zero);
        }
        public void KeyUpTo(IntPtr hwnd, KeyCode key)
        {
            var h = hwnd; if (h == IntPtr.Zero) h = GetForegroundWindow(); if (h == IntPtr.Zero) return;
            PostMessage(h, WM_KEYUP, (IntPtr)(byte)key, IntPtr.Zero);
        }

        // Backward-compatible global key (kept for callers) -> forwards to foreground window
        public void KeyPress(KeyCode key) => KeyPressTo(IntPtr.Zero, key);

        public static (int x, int y) CenterOfRect(RECT r)
        {
            return (r.Left + (r.Right - r.Left) / 2, r.Top + (r.Bottom - r.Top) / 2);
        }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP   = 0x0202;
    private const uint WM_KEYDOWN     = 0x0100;
    private const uint WM_KEYUP       = 0x0101;
    private const uint GA_ROOT        = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
    }
}
