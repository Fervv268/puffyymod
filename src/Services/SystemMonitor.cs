using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Timers;

namespace GameBuddyBrain.Services
{
    public class SystemMonitor : IDisposable
    {
        public event EventHandler Tick = delegate { };
        public event EventHandler<double> CpuUsageHigh = delegate { };
        public event Action<string> WindowChanged = delegate { };

        private readonly PerformanceCounter? _cpuCounter;
        private readonly System.Timers.Timer _timer;
        private string _lastWindow = "";
        private double _lastCpuValue = 0;

        public SystemMonitor()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            }
            catch
            {
                // Some environments don't have PerformanceCounter available/accessible.
                _cpuCounter = null;
            }
            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += (_, __) => OnTick();
        }

        public void Start() => _timer.Start();

        public double GetCurrentCpuUsage()
        {
            if (_cpuCounter != null)
            {
                try
                {
                    return _cpuCounter.NextValue();
                }
                catch
                {
                    return _lastCpuValue;
                }
            }
            return 0;
        }

        public void Dispose()
        {
            _timer.Stop();
            try { _cpuCounter?.Dispose(); } catch { }
        }

        private void OnTick()
        {
            Tick(this, EventArgs.Empty);

            if (_cpuCounter != null)
            {
                try
                {
                    double cpu = _cpuCounter.NextValue();
                    _lastCpuValue = cpu;
                    if (cpu > 80) // 80% threshold
                        CpuUsageHigh(this, cpu);
                }
                catch
                {
                    // Ignore if counter faults.
                }
            }

            string title = GetActiveWindowTitle();
            if (title != _lastWindow)
            {
                _lastWindow = title;
                WindowChanged(title);
            }
        }

        private string GetActiveWindowTitle()
        {
            const int nChars = 256;
            var buff = new System.Text.StringBuilder(nChars);
            IntPtr handle = GetForegroundWindow();
            if (GetWindowText(handle, buff, nChars) > 0)
                return buff.ToString();
            return "";
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern int GetWindowText(IntPtr hWnd,
            System.Text.StringBuilder text, int count);
    }
}
