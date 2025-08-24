using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GameBuddyBrain.Services;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;

namespace GameBuddyBrain.UI
{
    public partial class MainWindow : Window
    {
    private BrainService? _brain;
    private SystemMonitor? _monitor;
    // Usuwamy nakładkę – UI będzie bez overlay
        private DispatcherTimer _updateTimer;
    private Services.AppSettings? _settings;
    private Services.SettingsService? _settingsService;
    private readonly GameDetector _detector = new GameDetector();
    private readonly ObservableCollection<TemplateItemVm> _templates = new();
        private OverlayWindow? _overlay;
        private bool _overlayVisible = false;

        public MainWindow()
        {
            InitializeComponent();
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Title = "GameBuddyBrain - Panel sterowania";
            PopulateTrybyGry();
            this.Loaded += MainWindow_Loaded;
            
            // Timer do aktualizacji interfejsu
            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromSeconds(1);
            _updateTimer.Tick += UpdateUI;
            _updateTimer.Start();

            try
            {
                if (TemplatesItems != null)
                {
                    TemplatesItems.ItemsSource = _templates;
                    LoadTemplatesToUi();
                }
            }
            catch { }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Preload candidate list for the Proces tab
            try { RefreshCandidates(); } catch { }
        }

        // Ustaw referencje do serwisów (wywoływane z App.xaml.cs)
        public void SetServices(BrainService brain, SystemMonitor monitor)
        {
            _brain = brain;
            _monitor = monitor;
            // Subskrybuj zdarzenia strategii, by odświeżać wskaźniki UI
            try
            {
                _brain.DetectionProgressChanged += v => Dispatcher.Invoke(() => { if (DetectProgress != null) DetectProgress.Value = v; });
                _brain.StateChanged += s => Dispatcher.Invoke(() => { if (DetectLabel != null) DetectLabel.Text = s; });
                _brain.CurrentActionChanged += s => Dispatcher.Invoke(() => { /* opcjonalnie log */ });
            }
            catch { }
            // Apply current settings if already provided
            if (_settings != null)
            {
                ApplySettingsToUI(_settings);
                ApplySettingsToBrain(_settings);
            }
        }

    // Overlay removed

        // Allow App to pass settings service and current settings
        public void SetSettings(Services.SettingsService svc, Services.AppSettings cfg)
        {
            _settingsService = svc;
            _settings = cfg;
            ApplySettingsToUI(cfg);
            ApplySettingsToBrain(cfg);
            // Ustaw dolny master i tryb
            try
            {
                if (BottomMasterToggle != null) BottomMasterToggle.IsChecked = cfg.BotEnabled;
                if (ModeTabs != null)
                {
                    ModeTabs.SelectionChanged -= ModeTabs_SelectionChanged; // uniknij podwójnego
                    ModeTabs.SelectedIndex = cfg.Mode?.ToLowerInvariant() switch { "podstawowy" => 0, "zaawansowany" => 1, "niesamowity" => 2, _ => 0 };
                    ModeTabs.SelectionChanged += ModeTabs_SelectionChanged;
                }
            }
            catch { }
        }

        private void ApplySettingsToUI(Services.AppSettings s)
        {
            // Basic automation toggles
            AutoFarmCheck.IsChecked = s.AutoFarm;
            AutoPrestigeCheck.IsChecked = s.AutoPrestige;
            SafeModeCheck.IsChecked = s.SafeMode;
            // Mirror tabs (jeśli istnieją)
            if (AutoFarmCheck2 != null) AutoFarmCheck2.IsChecked = s.AutoFarm;
            if (AutoPrestigeCheck2 != null) AutoPrestigeCheck2.IsChecked = s.AutoPrestige;
            if (SafeModeCheck2 != null) SafeModeCheck2.IsChecked = s.SafeMode;
            // Overlay removed – ignore related settings in UI

            // IZWO
            // Zmieniono nazwę kontrolki na ToggleButton w XAML: AutoChestsToggle
            if (AutoChestsToggle != null) AutoChestsToggle.IsChecked = s.AutoChests;
            AutoSkillsCheck.IsChecked = s.AutoSkills;
            SkillIntervalBox.Text = Math.Max(5, s.SkillIntervalSec).ToString();
            if (SkillIntervalBox2 != null) SkillIntervalBox2.Text = SkillIntervalBox.Text;
            Skill1Check.IsChecked = s.Skill1; Skill2Check.IsChecked = s.Skill2; Skill3Check.IsChecked = s.Skill3; Skill4Check.IsChecked = s.Skill4;
            UseSpeedBoostCheck.IsChecked = s.UseSpeedBoost;
            SpeedBoostEveryBox.Text = Math.Max(5, s.SpeedBoostEverySec).ToString();
            if (SpeedBoostEveryBox2 != null) SpeedBoostEveryBox2.Text = SpeedBoostEveryBox.Text;

            // Shop preferences
            PreferElementalCheck.IsChecked = s.PreferElemental;
            PreferBallisticCheck.IsChecked = s.PreferBallistic;
            PreferExplosiveCheck.IsChecked = s.PreferExplosive;
            PreferEnergyCheck.IsChecked = s.PreferEnergy;
            BuyOnlyPreferredCheck.IsChecked = s.BuyOnlyPreferred;
            // Obsługa polskich etykiet w ComboBox
            MinRarityCombo.SelectedIndex = s.MinRarityToBuy.ToLowerInvariant() switch { "gray" => 0, "szary" => 0, "blue" => 1, "niebieski" => 1, "pink" => 2, "różowy" => 2, "rozowy" => 2, "red" => 3, "czerwony" => 3, _ => 0 };

            // Default routines enabled (controls removed in compact UI)
            if (AutoCollectToggle != null) AutoCollectToggle.IsChecked = s.AutoCollect;
            if (AutoBuyBestToggle != null) AutoBuyBestToggle.IsChecked = s.AutoBuyBest;
            if (AutoRestartToggle != null) AutoRestartToggle.IsChecked = s.AutoRestart;

            // Tryby gry – odtwórz listę i zaznaczenia
            try
            {
                if (TrybyGryList != null && TrybyGryList.Items.Count == 0)
                {
                    PopulateTrybyGry();
                }
                if (TrybyGryList != null && s.TrybyGry != null)
                {
                    TrybyGryList.UnselectAll();
                    foreach (var item in TrybyGryList.Items)
                    {
                        var name = item?.ToString() ?? string.Empty;
                        foreach (var pick in s.TrybyGry)
                        {
                            if (string.Equals(name, pick, StringComparison.OrdinalIgnoreCase))
                            {
                                TrybyGryList.SelectedItems.Add(item);
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
            // Dolny master i tryb
            try
            {
                if (BottomMasterToggle != null) BottomMasterToggle.IsChecked = s.BotEnabled;
                if (ModeTabs != null)
                {
                    ModeTabs.SelectionChanged -= ModeTabs_SelectionChanged;
                    ModeTabs.SelectedIndex = s.Mode?.ToLowerInvariant() switch { "podstawowy" => 0, "zaawansowany" => 1, "niesamowity" => 2, _ => 0 };
                    ModeTabs.SelectionChanged += ModeTabs_SelectionChanged;
                }
            }
            catch { }
        }

        private void ApplySettingsToBrain(Services.AppSettings s)
        {
            _brain?.SetAutoFarm(s.AutoFarm);
            _brain?.SetAutoPrestige(s.AutoPrestige);
            _brain?.SetSafeMode(s.SafeMode);
            _brain?.SetClickDelay(s.ClickAccuracyMs);
            _brain?.ConfigureStrategy(
                buyCooldown: TimeSpan.FromSeconds(Math.Max(1, s.BuyCooldownSec)),
                prestigeCooldown: TimeSpan.FromMinutes(Math.Max(1, s.PrestigeCooldownMin)),
                nextTimeout: TimeSpan.FromSeconds(Math.Max(3, s.NextWaveIntervalSec)),
                noProgressPrestige: TimeSpan.FromMinutes(Math.Max(1, s.NoProgressPrestigeMin)),
                buyScanSlots: Math.Max(1, s.BuyScanSlots)
            );
            _brain?.ConfigureIdleZombieWave(s);
            // Shop preferences to brain
            _brain?.ConfigureShopPreferences(s);
            // High-level switches & tryby gry
            _brain?.ConfigureHighLevel(s);
            // Zastosuj tryb
            _brain?.ApplyMode(s.Mode ?? "Podstawowy");
            if (s.BotEnabled) _brain?.EnableBot(); else _brain?.DisableBot();
        }

    private void UpdateUI(object? sender, EventArgs e)
        {
            try
            {
                // Aktualizuj informacje systemowe
                if (_monitor != null)
                {
                    var cpu = _monitor.GetCurrentCpuUsage();
                    var memory = GC.GetTotalMemory(false) / (1024 * 1024); // MB
                    var attached = _brain?.IsAttached ?? false;
                    
                    SystemInfoText.Text = $"CPU: {cpu:F1}%\n" +
                                         $"Pamięć: {memory} MB\n" +
                                         $"Gra podłączona: {(attached ? "Tak" : "Nie")}\n" +
                                         $"Bot aktywny: {(_brain?.IsRunning ?? false ? "Tak" : "Nie")}";
                    // Update detection bar using BrainService state
                    try
                    {
                        var prog = _brain?.GetDetectProgress() ?? (attached ? 100 : 0);
                        if (DetectProgress != null) DetectProgress.Value = Math.Max(0, Math.Min(100, prog));
                        if (DetectLabel != null) DetectLabel.Text = _brain?.GetState() ?? (attached ? "Gra wykryta" : "Skanowanie...");
                        // Strategia i performance
                        if (StrategyText != null) StrategyText.Text = _brain?.CurrentStrategy ?? "-";
                        if (PerfText != null) PerfText.Text = (_brain?.PerformanceScore ?? 0).ToString("F0");
                        // Statystyki (stub z ustawień)
                        if (_settings != null)
                        {
                            if (StatsTotalActions != null) StatsTotalActions.Text = _settings.TotalActions.ToString();
                            if (StatsSuccess != null) StatsSuccess.Text = ($"{_settings.SuccessRate:P0}");
                            if (StatsUptime != null) StatsUptime.Text = TimeSpan.FromMilliseconds(_settings.UptimeMs).ToString();
                        }
                    }
                    catch { }
                }

                // Synchronizuj overlay: pozycja i ostatnie prostokąty
                try
                {
                    if (_overlayVisible && _overlay != null && _brain != null)
                    {
                        if (_brain.TryGetAttachedRect(out var l, out var t, out var r, out var b))
                        {
                            _overlay.Left = l; _overlay.Top = t; _overlay.Width = Math.Max(50, r - l); _overlay.Height = Math.Max(50, b - t);
                            _overlay.ShowRects(_brain);
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        private void EventModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.EventModeEnabled = true;
                _settingsService?.Save(_settings);
                // opcjonalnie powiadom Brain o trybie eventowym (placeholder)
                ShowToast("Event Mode: WŁĄCZONY");
            }
        }
        private void EventModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.EventModeEnabled = false;
                _settingsService?.Save(_settings);
                ShowToast("Event Mode: WYŁĄCZONY");
            }
        }

        // Event handlers dla przycisków
        private void StartBot_Click(object sender, RoutedEventArgs e)
        {
            _brain?.EnableBot();
            ShowNotification("Bot został uruchomiony!");
        }

        private void StopBot_Click(object sender, RoutedEventArgs e)
        {
            _brain?.DisableBot();
            ShowNotification("Bot został zatrzymany!");
        }

        private void CollectAll_Click(object sender, RoutedEventArgs e)
        {
            _brain?.CollectAll();
            ShowNotification("Włączono zbieranie wszystkich zasobów (auto)!");
        }

        private void ForcePrestige_Click(object sender, RoutedEventArgs e)
        {
            _brain?.ForcePrestige();
            ShowNotification("Wykonano wymuszony prestiż!");
        }

        private void OpenChests_Click(object sender, RoutedEventArgs e)
        {
            _brain?.CollectAll(); // Używamy tej samej funkcji
            ShowNotification("Otwieranie skrzyń!");
        }

        private void BuyBest_Click(object sender, RoutedEventArgs e)
        {
            // Bot ma już logikę zakupów w głównej pętli
            ShowNotification("Rozpoczęto automatyczne zakupy!");
        }

    // Overlay removed – no handler

        // Event handlers dla checkboxów
        private void AutoFarm_Checked(object sender, RoutedEventArgs e)
        {
            // Logika auto farm - bot już ma to zaimplementowane
            _brain?.SetAutoFarm(true);
            if (_settings != null) { _settings.AutoFarm = true; _settingsService?.Save(_settings); }
        }

        private void AutoFarm_Unchecked(object sender, RoutedEventArgs e)
        {
            _brain?.SetAutoFarm(false);
            if (_settings != null) { _settings.AutoFarm = false; _settingsService?.Save(_settings); }
        }

        private void AutoPrestige_Checked(object sender, RoutedEventArgs e)
        {
            _brain?.SetAutoPrestige(true);
            if (_settings != null) { _settings.AutoPrestige = true; _settingsService?.Save(_settings); }
        }

        private void AutoPrestige_Unchecked(object sender, RoutedEventArgs e)
        {
            _brain?.SetAutoPrestige(false);
            if (_settings != null) { _settings.AutoPrestige = false; _settingsService?.Save(_settings); }
        }

        private void SafeMode_Checked(object sender, RoutedEventArgs e)
        {
            _brain?.SetSafeMode(true);
            if (_settings != null) { _settings.SafeMode = true; _settingsService?.Save(_settings); }
        }

        private void SafeMode_Unchecked(object sender, RoutedEventArgs e)
        {
            _brain?.SetSafeMode(false);
            if (_settings != null) { _settings.SafeMode = false; _settingsService?.Save(_settings); }
        }

        private void AlwaysShowOverlayCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.ShowOverlayWhenDetached = true; _settingsService?.Save(_settings); }
        }

        private void AlwaysShowOverlayCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.ShowOverlayWhenDetached = false; _settingsService?.Save(_settings); }
        }

        private void TransparencyFallbackCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.OverlayNoTransparency = true; _settingsService?.Save(_settings); }
        }

        private void TransparencyFallbackCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.OverlayNoTransparency = false; _settingsService?.Save(_settings); }
        }

        // IZWO handlers
        private void AutoChests_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.AutoChests = true; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void AutoChests_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.AutoChests = false; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void AutoSkills_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.AutoSkills = true; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void AutoSkills_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.AutoSkills = false; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void ApplySkillInterval_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;
            if (int.TryParse(SkillIntervalBox.Text, out var val))
            {
                _settings.SkillIntervalSec = Math.Max(5, Math.Min(600, val));
                _settingsService?.Save(_settings);
                _brain?.ConfigureIdleZombieWave(_settings);
            }
        }
        private void UseSpeedBoost_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.UseSpeedBoost = true; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void UseSpeedBoost_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.UseSpeedBoost = false; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void ApplySpeedBoostEvery_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;
            if (int.TryParse(SpeedBoostEveryBox.Text, out var val))
            {
                _settings.SpeedBoostEverySec = Math.Max(5, Math.Min(600, val));
                _settingsService?.Save(_settings);
                _brain?.ConfigureIdleZombieWave(_settings);
            }
        }

        private void Skill1_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.Skill1 = true; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void Skill1_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.Skill1 = false; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void Skill2_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.Skill2 = true; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void Skill2_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.Skill2 = false; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void Skill3_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.Skill3 = true; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void Skill3_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.Skill3 = false; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void Skill4_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.Skill4 = true; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }
        private void Skill4_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.Skill4 = false; _settingsService?.Save(_settings); _brain?.ConfigureIdleZombieWave(_settings); }
        }

        // Shop preferences handlers
        private void PreferElemental_Checked(object sender, RoutedEventArgs e) { if (_settings != null) { _settings.PreferElemental = true; _settingsService?.Save(_settings); _brain?.ConfigureShopPreferences(_settings); } }
        private void PreferElemental_Unchecked(object sender, RoutedEventArgs e) { if (_settings != null) { _settings.PreferElemental = false; _settingsService?.Save(_settings); _brain?.ConfigureShopPreferences(_settings); } }
        private void PreferBallistic_Checked(object sender, RoutedEventArgs e) { if (_settings != null) { _settings.PreferBallistic = true; _settingsService?.Save(_settings); _brain?.ConfigureShopPreferences(_settings); } }
        private void PreferBallistic_Unchecked(object sender, RoutedEventArgs e) { if (_settings != null) { _settings.PreferBallistic = false; _settingsService?.Save(_settings); _brain?.ConfigureShopPreferences(_settings); } }
        private void PreferExplosive_Checked(object sender, RoutedEventArgs e) { if (_settings != null) { _settings.PreferExplosive = true; _settingsService?.Save(_settings); _brain?.ConfigureShopPreferences(_settings); } }
        private void PreferExplosive_Unchecked(object sender, RoutedEventArgs e) { if (_settings != null) { _settings.PreferExplosive = false; _settingsService?.Save(_settings); _brain?.ConfigureShopPreferences(_settings); } }
        private void PreferEnergy_Checked(object sender, RoutedEventArgs e) { if (_settings != null) { _settings.PreferEnergy = true; _settingsService?.Save(_settings); _brain?.ConfigureShopPreferences(_settings); } }
        private void PreferEnergy_Unchecked(object sender, RoutedEventArgs e) { if (_settings != null) { _settings.PreferEnergy = false; _settingsService?.Save(_settings); _brain?.ConfigureShopPreferences(_settings); } }
        private void BuyOnlyPreferred_Checked(object sender, RoutedEventArgs e) { if (_settings != null) { _settings.BuyOnlyPreferred = true; _settingsService?.Save(_settings); _brain?.ConfigureShopPreferences(_settings); } }
        private void BuyOnlyPreferred_Unchecked(object sender, RoutedEventArgs e) { if (_settings != null) { _settings.BuyOnlyPreferred = false; _settingsService?.Save(_settings); _brain?.ConfigureShopPreferences(_settings); } }
        private void MinRarityCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_settings == null) return;
            var sel = (MinRarityCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Szary";
            _settings.MinRarityToBuy = sel.ToLowerInvariant() switch
            {
                "szary" => "Gray",
                "niebieski" => "Blue",
                "różowy" => "Pink",
                "rozowy" => "Pink",
                "czerwony" => "Red",
                _ => "Gray"
            };
            _settingsService?.Save(_settings);
            _brain?.ConfigureShopPreferences(_settings);
        }

        // Broń: katalog szablonów – UI helpers
        private void LoadTemplatesToUi()
        {
            try
            {
                _templates.Clear();
                var svc = new TemplateCatalogService();
                var list = svc.List();
                foreach (var it in list)
                {
                    _templates.Add(new TemplateItemVm
                    {
                        Key = it.Key,
                        Type = it.Type.ToString(),
                        ThumbPath = it.ThumbPath
                    });
                }
            }
            catch { }
        }
        private void RefreshTemplates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var srcDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ww11", "ww11");
                var svc = new TemplateCatalogService();
                svc.BuildOrUpdateFrom(srcDir);
                LoadTemplatesToUi();
                ShowToast("Zaktualizowano katalog broni.");
            }
            catch (Exception ex)
            {
                ShowToast($"Błąd odświeżania katalogu: {ex.Message}");
            }
        }

        // Utility buttons
        private void FocusWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Włącz tryb auto-focus w mózgu bota
                _brain?.SetAutoFocus(true);
                // Lokalnie przywróć okno
                this.WindowState = WindowState.Normal;
                this.Activate();
                this.Topmost = true; // chwilowe Topmost, żeby wymusić przód
                this.Topmost = false;
                this.Focus();
                ShowNotification("Tryb focus: WŁĄCZONY. Wyjście: naciśnij Strzałkę w górę.");
            }
            catch { }

            try
            {
                // Dodatkowo wyślij sygnał do globalnego handlera BringToFront (obsługiwany w App.xaml.cs)
                var bringEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "GBB_BringToFront");
                bringEvent.Set();
            }
            catch { }
        }
        private void Reconnect_Click(object sender, RoutedEventArgs e) { _brain?.Reconnect(); ShowNotification("Ponowne podłączenie próbne."); }
        private void TestVision_Click(object sender, RoutedEventArgs e) { ShowNotification("Test wizji: spróbuj kupna w sklepie i sprawdź log."); }
        private void ResetState_Click(object sender, RoutedEventArgs e) { ShowNotification("Zresetowano stan bota (tymczasowo tylko komunikat)."); }

    // Routine toggles
    private void EnableAttack_Checked(object sender, RoutedEventArgs e) { _brain?.SetEnableAttack(true); }
    private void EnableAttack_Unchecked(object sender, RoutedEventArgs e) { _brain?.SetEnableAttack(false); }
    private void EnableUpgrade_Checked(object sender, RoutedEventArgs e) { _brain?.SetEnableUpgrade(true); }
    private void EnableUpgrade_Unchecked(object sender, RoutedEventArgs e) { _brain?.SetEnableUpgrade(false); }
    private void EnableNextWave_Checked(object sender, RoutedEventArgs e) { _brain?.SetEnableNextWave(true); }
    private void EnableNextWave_Unchecked(object sender, RoutedEventArgs e) { _brain?.SetEnableNextWave(false); }
    private void EnableRewards_Checked(object sender, RoutedEventArgs e) { _brain?.SetEnableRewards(true); }
    private void EnableRewards_Unchecked(object sender, RoutedEventArgs e) { _brain?.SetEnableRewards(false); }

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowNotification("Ustawienia będą dostępne w przyszłej wersji!");

        private void About_Click(object sender, RoutedEventArgs e)
        {
            ShowNotification("GameBuddyBrain v1.0 — inteligentna automatyzacja rozgrywki.");
        }

        private void ShowNotification(string message) => ShowToast(message);

        // Minimalny mechanizm toastów w ramach okna
        public void ShowToast(string text)
        {
            try
            {
                var item = $"[{DateTime.Now:HH:mm:ss}] {text}";
                SeqLog?.Items.Add(item);
                if (SeqLog != null) SeqLog.SelectedIndex = SeqLog.Items.Count - 1;
            }
            catch { }
        }

        // Custom chrome handlers
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => this.Close();
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { if (e.ButtonState == MouseButtonState.Pressed) this.DragMove(); } catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _updateTimer?.Stop();
            try { _overlay?.Close(); } catch { }
            base.OnClosed(e);
        }

        // Proces: attach helpers
        private void RefreshCandidates_Click(object sender, RoutedEventArgs e) => RefreshCandidates();
        private void RefreshCandidates()
        {
            try
            {
                if (ProcessList == null) return;
                ProcessList.Items.Clear();
                var items = _brain?.ListCandidates() ?? _detector.EnumerateCandidates();
                foreach (var it in items)
                {
                    ProcessList.Items.Add(new ListBoxItemWrap(it));
                }
            }
            catch { }
        }
        private void AttachSelected_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ProcessList?.SelectedItem is ListBoxItemWrap wrap)
                {
                    // Prefer Brain's internal detector via simple title attach
                    if (!_detector.AttachByProcessId(wrap.Value.Pid))
                    {
                        // fallback to name or title
                        if (!string.IsNullOrWhiteSpace(wrap.Value.ProcessName)) _detector.AttachByProcessName(wrap.Value.ProcessName);
                        else if (!string.IsNullOrWhiteSpace(wrap.Value.Title)) _detector.AttachByWindowTitle(wrap.Value.Title);
                    }
                    ShowNotification($"Podłączono: {wrap}");
                    // Ustaw pasek postępu na 100%
                    try { if (DetectProgress != null) DetectProgress.Value = 100; if (DetectLabel != null) DetectLabel.Text = "Gra wykryta"; } catch { }
                }
            }
            catch { ShowNotification("Nie udało się podłączyć."); }
        }
        private void AttachByName_Click(object sender, RoutedEventArgs e)
        {
            var name = AttachByNameBox?.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            var ok = _detector.AttachByProcessName(name);
            ShowNotification(ok ? $"Podłączono po nazwie: {name}" : $"Brak procesu: {name}");
        }
        private void AttachByTitle_Click(object sender, RoutedEventArgs e)
        {
            var part = AttachByTitleBox?.Text?.Trim();
            if (string.IsNullOrEmpty(part)) return;
            var ok = _detector.AttachByWindowTitle(part);
            ShowNotification(ok ? $"Podłączono po tytule: {part}" : $"Nie znaleziono tytułu zawierającego: {part}");
        }

        private sealed class ListBoxItemWrap
        {
            public GameDetector.WindowInfo Value { get; }
            public ListBoxItemWrap(GameDetector.WindowInfo v) { Value = v; }
            public override string ToString()
            {
                var cls = string.IsNullOrWhiteSpace(Value.Class) ? "?" : Value.Class;
                return $"[{Value.Pid}] {Value.ProcessName} — '{Value.Title}' ({Value.Width}x{Value.Height}, {cls})";
            }
        }

        // Nowe przełączniki (switch) z zakładki Menu
        private void AutoCollect_Checked(object sender, RoutedEventArgs e)
        {
            // Mapuj na CollectAll jako akcję cykliczną przez rutynę nagród
            _brain?.SetEnableRewards(true);
            if (_settings != null) { _settings.AutoCollect = true; _settingsService?.Save(_settings); _brain?.ConfigureHighLevel(_settings); }
            ShowNotification("Zbieranie: WŁĄCZONE");
        }
        private void AutoCollect_Unchecked(object sender, RoutedEventArgs e)
        {
            _brain?.SetEnableRewards(false);
            if (_settings != null) { _settings.AutoCollect = false; _settingsService?.Save(_settings); _brain?.ConfigureHighLevel(_settings); }
            ShowNotification("Zbieranie: WYŁĄCZONE");
        }
        private void AutoBuyBest_Checked(object sender, RoutedEventArgs e)
        {
            // Zakupy są częścią pętli – wystarczy upewnić się, że AutoFarm włączony
            _brain?.SetAutoFarm(true);
            if (_settings != null) { _settings.AutoFarm = true; _settings.AutoBuyBest = true; _settingsService?.Save(_settings); _brain?.ConfigureHighLevel(_settings); }
            ShowNotification("Kupuj najlepsze: WŁĄCZONE");
        }
        private void AutoBuyBest_Unchecked(object sender, RoutedEventArgs e)
        {
            // Bezpieczny tryb – nie wyłączamy całej farmy, tylko pozwalamy pominąć zakupy (poprzez Tryb bezpieczny)
            _brain?.SetSafeMode(true);
            if (_settings != null) { _settings.SafeMode = true; _settings.AutoBuyBest = false; _settingsService?.Save(_settings); _brain?.ConfigureHighLevel(_settings); }
            ShowNotification("Kupuj najlepsze: WYŁĄCZONE (tryb bezpieczny)");
        }
        private void AutoRestart_Checked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.AutoRestart = true; _settingsService?.Save(_settings); _brain?.ConfigureHighLevel(_settings); }
            ShowNotification("Auto restart: WŁĄCZONY");
        }
        private void AutoRestart_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_settings != null) { _settings.AutoRestart = false; _settingsService?.Save(_settings); _brain?.ConfigureHighLevel(_settings); }
            ShowNotification("Auto restart: WYŁĄCZONY");
        }

        // Master: Farma + XP (zbieranie)
        private void MasterFarm_Checked(object sender, RoutedEventArgs e)
        {
            // Enable both AutoFarm and AutoCollect
            _brain?.SetAutoFarm(true);
            _brain?.SetEnableRewards(true);
            if (_settings != null)
            {
                _settings.AutoFarm = true;
                _settings.AutoCollect = true;
                _settingsService?.Save(_settings);
                _brain?.ConfigureHighLevel(_settings);
            }
            // Reflect in UI
            if (AutoFarmCheck != null) AutoFarmCheck.IsChecked = true;
            if (AutoFarmCheck2 != null) AutoFarmCheck2.IsChecked = true;
            if (AutoCollectToggle != null) AutoCollectToggle.IsChecked = true;
            ShowNotification("Master: Farma + XP — WŁĄCZONE");
        }
        private void MasterFarm_Unchecked(object sender, RoutedEventArgs e)
        {
            // Disable both AutoFarm and AutoCollect
            _brain?.SetAutoFarm(false);
            _brain?.SetEnableRewards(false);
            if (_settings != null)
            {
                _settings.AutoFarm = false;
                _settings.AutoCollect = false;
                _settingsService?.Save(_settings);
                _brain?.ConfigureHighLevel(_settings);
            }
            // Reflect in UI
            if (AutoFarmCheck != null) AutoFarmCheck.IsChecked = false;
            if (AutoFarmCheck2 != null) AutoFarmCheck2.IsChecked = false;
            if (AutoCollectToggle != null) AutoCollectToggle.IsChecked = false;
            ShowNotification("Master: Farma + XP — WYŁĄCZONE");
        }

        // Tryby gry – inicjalizacja i zapis wyboru
        private void PopulateTrybyGry()
        {
            try
            {
                if (TrybyGryList == null) return;
                if (TrybyGryList.Items.Count > 0) return;
                var tryby = new string[] { "Kampania", "Fale Zombie", "Boss Rush", "Survival", "Arena", "Wyzwania", "Tryb Codzienny", "Wieczna Fala", "Obrona Bazy", "Łowy", "Wyprawa", "Specjalne" };
                foreach (var t in tryby) TrybyGryList.Items.Add(t);
                TrybyGryList.SelectionChanged += TrybyGryList_SelectionChanged;
            }
            catch { }
        }

        private void TrybyGryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_settings == null) return;
            try
            {
                var list = new System.Collections.Generic.List<string>();
                foreach (var it in TrybyGryList.SelectedItems) list.Add(it?.ToString() ?? string.Empty);
                _settings.TrybyGry = list.ToArray();
                _settingsService?.Save(_settings);
                _brain?.ConfigureHighLevel(_settings);
            }
            catch { }
        }

        // Sekwencje – proste sterowanie
        private void RunSeq_FullFarm_Click(object sender, RoutedEventArgs e)
        {
            ShowToast("Sekwencja: start pełnej farmy");
            _brain?.SetAutoFarm(true);
            _brain?.SetEnableRewards(true);
            _brain?.SetEnableAttack(true);
            _brain?.SetEnableUpgrade(true);
            _brain?.SetEnableNextWave(true);
        }
        private void StopAllSequences_Click(object sender, RoutedEventArgs e)
        {
            ShowToast("Sekwencje: zatrzymane");
            _brain?.DisableBot();
        }

        // Idealny start + overlay
        private void StartIdealBtn_Click(object sender, RoutedEventArgs e)
        {
            // Włącz zestaw opcji zalecanych: farma, nagrody, atak/upgrade/next, focus i anti-AFK
            _brain?.SetAutoFarm(true);
            _brain?.SetEnableRewards(true);
            _brain?.SetEnableAttack(true);
            _brain?.SetEnableUpgrade(true);
            _brain?.SetEnableNextWave(true);
            _brain?.SetAutoFocus(true);
            _brain?.SetAntiAfk(true);
            _brain?.EnableBot();
            try { if (_settings != null) { _settings.BotEnabled = true; _settings.Mode = "Niesamowity"; _settingsService?.Save(_settings); } } catch { }
            try { if (BottomMasterToggle != null) BottomMasterToggle.IsChecked = true; } catch { }
            try { if (ModeTabs != null) ModeTabs.SelectedIndex = 2; } catch { }
            if (MasterFarmToggle != null) MasterFarmToggle.IsChecked = true;
            ShowToast("Idealny farm bot: uruchomiony");
        }

        private void ToggleOverlayBtn_Click(object sender, RoutedEventArgs e)
        {
            _overlayVisible = !_overlayVisible;
            if (_overlayVisible)
            {
                if (_overlay == null)
                {
                    _overlay = new OverlayWindow();
                    _overlay.Show();
                    _overlay.IsHitTestVisible = false;
                }
                else { _overlay.Show(); }
                ShowToast("Overlay: WŁĄCZONY");
            }
            else
            {
                try { _overlay?.Hide(); } catch { }
                ShowToast("Overlay: WYŁĄCZONY");
            }
        }

        // Dolny master ON/OFF
        private void BottomMasterToggle_Checked(object sender, RoutedEventArgs e)
        {
            _brain?.EnableBot();
            if (_settings != null) { _settings.BotEnabled = true; _settingsService?.Save(_settings); }
            ShowNotification("Bot: WŁĄCZONY (master)");
        }
        private void BottomMasterToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _brain?.DisableBot();
            if (_settings != null) { _settings.BotEnabled = false; _settingsService?.Save(_settings); }
            ShowNotification("Bot: WYŁĄCZONY (master)");
        }

        // Zmiana trybu (Podstawowy/Zaawansowany/Niesamowity)
        private void ModeTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_settings == null) return;
            try
            {
                var idx = ModeTabs.SelectedIndex;
                var mode = idx switch { 0 => "Podstawowy", 1 => "Zaawansowany", 2 => "Niesamowity", _ => "Podstawowy" };
                _settings.Mode = mode;
                _settingsService?.Save(_settings);
                _brain?.ApplyMode(mode);
                ShowNotification($"Tryb: {mode}");
            }
            catch { }
        }
    }

    public sealed class TemplateItemVm
    {
        public string Key { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string ThumbPath { get; set; } = string.Empty;
    }
}
