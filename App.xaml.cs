using System;
using System.Threading;
using System.Windows;
using GameBuddyBrain.Services;

namespace GameBuddyBrain
{
	public partial class App : Application
	{
		private Mutex? _mutex;
		private SystemMonitor? _monitor;
		private BrainService? _brain;
		private SettingsService? _settingsService;
		private AppSettings? _settings;

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);

			bool created;
			_mutex = new Mutex(true, "GameBuddyBrain.SingleInstance", out created);
			if (!created)
			{
				// Already running
				Shutdown();
				return;
			}

			_monitor = new SystemMonitor();
			_monitor.Start();
			_settingsService = new SettingsService();
			_settings = _settingsService.Load();
			_brain = new BrainService(_monitor);

			var win = new UI.MainWindow();
			win.SetServices(_brain, _monitor);
			if (_settingsService != null && _settings != null)
				win.SetSettings(_settingsService, _settings);

			win.Show();
		}

		protected override void OnExit(ExitEventArgs e)
		{
			try { _monitor?.Dispose(); } catch { }
			try { _mutex?.ReleaseMutex(); } catch { }
			base.OnExit(e);
		}
	}
}
