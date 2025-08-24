using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace GameBuddyBrain.Services
{
    public class GameDetector
    {
        public IntPtr? AttachedHwnd { get; private set; }
        public string? AttachedTitle { get; private set; }
        public int ProcessCount { get; private set; } = 0;

        // Common emulator process names and possible window title fragments
        private static readonly string[] EmulatorProcesses =
        {
            "HD-Player",    // BlueStacks 4
            "HD-Player.exe",
            "Bluestacks", "Bluestacks.exe",
            "LdVBoxHeadless", "ldplayer", "ldplayer.exe",
            "Nox", "Nox.exe",
            "MEmu", "MEmu.exe",
            "AndroidEmulator", "AndroidEmulatorEn.exe"
        };

        // Common window class names used by emulators/browsers hosting games
        private static readonly string[] EmulatorWindowClasses =
        {
            "Qt5QWindowIcon",     // BlueStacks/Qt apps
            "Qt5154QWindowIcon",
            "LDPlayerMainFrame",  // LDPlayer
            "LDPlayerMainFrameWindow",
            "TXGuiFoundation",    // Tencent
            "Chrome_WidgetWin_1", // Chromium/Electron hosts
            "Chrome_WidgetWin_0",
            "BlueStacksApp", "BlueStacksAppXP"
        };

        private static readonly string[] GameTitleHints =
        {
            "Idle Zombie Wave", "IdleZombieWave", "Ryki",
            "Zombie", "Wave", "Idle"
        };

        public bool TryAutoAttach()
        {
            // If already attached and still valid, keep it
            if (IsAttached) return true;

            // Score windows and pick best candidate
            IntPtr best = IntPtr.Zero;
            string bestTitle = string.Empty;
            int bestScore = 0;

            foreach (var hwnd in EnumTopLevelWindows())
            {
                var title = GetWindowText(hwnd);
                if (string.IsNullOrWhiteSpace(title)) continue;

                // Avoid attaching to known non-game tool windows
                if (title.IndexOf("Visual Studio", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (title.IndexOf("Code", StringComparison.OrdinalIgnoreCase) >= 0 && GetClassName(hwnd).IndexOf("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                int score = 0;

                // 1) Title hints
                if (GameTitleHints.Any(h => title.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)) score += 3;

                // 2) ClassName hints
                var cls = GetClassName(hwnd);
                if (EmulatorWindowClasses.Any(c => cls.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0)) score += 2;

                // 3) Process name hints
                var procName = TryGetProcessName(hwnd);
                if (!string.IsNullOrEmpty(procName) && EmulatorProcesses.Any(p => procName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)) score += 2;

                // 4) Reasonable client size (likely a game viewport)
                if (GetClientRect(hwnd, out var rc))
                {
                    int cw = rc.Right - rc.Left;
                    int ch = rc.Bottom - rc.Top;
                    if (cw >= 800 && ch >= 450) score += 1; // ~16:9 min
                    if (cw >= 1024 && ch >= 576) score += 1; // larger viewport
                }

                // Prefer stronger score threshold to avoid first-found attachments
                if (score > bestScore)
                {
                    bestScore = score;
                    best = hwnd;
                    bestTitle = title;
                }
            }

            // Require slightly higher confidence or exact process match
            if (best != IntPtr.Zero && (bestScore >= 3))
            {
                AttachedHwnd = best;
                AttachedTitle = bestTitle;
                return true;
            }

            // Fallback by process list
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (EmulatorProcesses.Any(n => p.ProcessName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        var hwnd = p.MainWindowHandle;
                        if (hwnd != IntPtr.Zero)
                        {
                            AttachedHwnd = hwnd;
                            AttachedTitle = GetWindowText(hwnd);
                            return true;
                        }
                    }
                }
                catch { /* ignore */ }
            }

            return false;
        }

        // Manual attach helpers
        public bool AttachByProcessId(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                var hwnd = p.MainWindowHandle;
                if (hwnd == IntPtr.Zero)
                {
                    // Try to find any visible top-level window for the process
                    foreach (var h in EnumTopLevelWindows())
                    {
                        GetWindowThreadProcessId(h, out uint ownerPid);
                        if (ownerPid == (uint)pid && IsWindowVisible(h)) { hwnd = h; break; }
                    }
                }
                if (hwnd != IntPtr.Zero)
                {
                    AttachedHwnd = hwnd;
                    AttachedTitle = GetWindowText(hwnd);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public bool AttachByProcessName(string nameContains)
        {
            try
            {
                var procs = Process.GetProcesses()
                    .Where(p =>
                        {
                            try { return p.ProcessName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0; }
                            catch { return false; }
                        })
                    .OrderByDescending(p => p.MainWindowHandle != IntPtr.Zero)
                    .ThenByDescending(p => p.WorkingSet64)
                    .ToList();
                foreach (var p in procs)
                {
                    if (AttachByProcessId(p.Id)) return true;
                }
            }
            catch { }
            return false;
        }

        public bool AttachByWindowTitle(string titleContains)
        {
            foreach (var h in EnumTopLevelWindows())
            {
                var t = GetWindowText(h);
                if (!string.IsNullOrWhiteSpace(t) && t.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AttachedHwnd = h;
                    AttachedTitle = t;
                    return true;
                }
            }
            return false;
        }

        public IEnumerable<WindowInfo> EnumerateCandidates()
        {
            foreach (var h in EnumTopLevelWindows())
            {
                if (string.IsNullOrWhiteSpace(GetWindowText(h))) continue;
                GetWindowThreadProcessId(h, out uint pid);
                var title = GetWindowText(h);
                var cls = GetClassName(h);
                var proc = TryGetProcessName(h);
                GetWindowRect(h, out var rc);
                yield return new WindowInfo
                {
                    Hwnd = h,
                    Title = title,
                    Class = cls,
                    ProcessName = proc,
                    Pid = (int)pid,
                    Width = rc.Right - rc.Left,
                    Height = rc.Bottom - rc.Top
                };
            }
        }

        public bool IsAttached => AttachedHwnd.HasValue && AttachedHwnd.Value != IntPtr.Zero && IsWindow(AttachedHwnd.Value);

        public bool TryGetWindowRect(out RECT rect)
        {
            rect = default;
            if (!IsAttached) return false;
            return GetWindowRect(AttachedHwnd!.Value, out rect);
        }

        public void EnsureForeground()
        {
            if (IsAttached)
            {
                var h = AttachedHwnd!.Value;
                if (IsIconic(h))
                {
                    ShowWindow(h, SW_RESTORE);
                }
                SetForegroundWindow(h);
            }
        }

        private static IEnumerable<IntPtr> EnumTopLevelWindows()
        {
            var list = new List<IntPtr>();
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd)) list.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        private static string GetWindowText(IntPtr hWnd)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetClassName(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string TryGetProcessName(IntPtr hWnd)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return string.Empty;
                using var p = Process.GetProcessById((int)pid);
                return p.ProcessName;
            }
            catch { return string.Empty; }
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public sealed class WindowInfo
        {
            public IntPtr Hwnd { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Class { get; set; } = string.Empty;
            public string ProcessName { get; set; } = string.Empty;
            public int Pid { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }
        
        public bool IsGameRunning()
        {
            return IsAttached;
        }
        
        public string GetActiveGameTitle()
        {
            return AttachedTitle ?? "Nieznana gra";
        }
        
        public List<GameProcess> GetRunningGames()
        {
            var games = new List<GameProcess>();
            Process[] processes = Process.GetProcesses();
            ProcessCount = processes.Length;
            
            foreach (var process in processes)
            {
                try
                {
                    if (!string.IsNullOrEmpty(process.MainWindowTitle))
                    {
                        games.Add(new GameProcess
                        {
                            Hwnd = process.MainWindowHandle,
                            Title = process.MainWindowTitle,
                            ProcessName = process.ProcessName,
                            Pid = process.Id
                        });
                    }
                }
                catch { }
            }
            
            return games;
        }

        public class GameProcess
        {
            public IntPtr Hwnd { get; set; }
            public string Title { get; set; } = string.Empty;
            public string ProcessName { get; set; } = string.Empty;
            public int Pid { get; set; }
            public string MainWindowTitle => Title;
        }
    }
}
