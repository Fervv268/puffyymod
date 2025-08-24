using System;
using System.IO;
using System.Text.Json;

namespace GameBuddyBrain.Services
{
    public class AppSettings
    {
        public bool SkipElevate { get; set; } = false;
        public string Profile { get; set; } = "Agresywny";
        public bool Attack { get; set; } = true;
        public bool Upgrade { get; set; } = true;
        public bool NextWave { get; set; } = true;
        public bool Rewards { get; set; } = true;
    public bool MinimizeToTray { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = true;
    public int AttackIntervalMs { get; set; } = 350;
    public int UpgradeIntervalSec { get; set; } = 8;
    public int NextWaveIntervalSec { get; set; } = 22;
    public int RewardsIntervalSec { get; set; } = 90;
    public bool ShowOverlayWhenDetached { get; set; } = true;
    public bool OverlayNoTransparency { get; set; } = false;
    public bool DebugOverlayEnabled { get; set; } = false;
    public bool AutoFocus { get; set; } = false;
    public bool SafetyPause { get; set; } = false;
    public bool AntiAfk { get; set; } = false;
    // New advanced options
    public bool AutoFarm { get; set; } = true; // master toggle for farming routines
    public bool AutoPrestige { get; set; } = false; // perform prestige when conditions met
    public int ClickAccuracyMs { get; set; } = 150; // default click timing used by manual actions
    public bool SafeMode { get; set; } = false; // limits aggressive actions
    public string Theme { get; set; } = "Dark"; // "Dark" or "Light"
    // Strategy thresholds
    public int BuyCooldownSec { get; set; } = 8;             // minimal delay between buys
    public int PrestigeCooldownMin { get; set; } = 5;        // minimal delay between prestiges
    public int NextTimeoutSec { get; set; } = 20;            // if we can't afford for this long, try next wave
    public int NoProgressPrestigeMin { get; set; } = 15;     // if no buy/next/claim for this long, try prestige
    public int BuyScanSlots { get; set; } = 3;               // heuristic buy scan clicks down the right side
    
    // Idle Zombie Wave specific helpers
    public bool AutoChests { get; set; } = true;             // automatically open detected chests
    public bool AutoSkills { get; set; } = false;            // automatically fire skills
    public int SkillIntervalSec { get; set; } = 30;          // interval between skill casts
    public bool Skill1 { get; set; } = true;
    public bool Skill2 { get; set; } = true;
    public bool Skill3 { get; set; } = true;
    public bool Skill4 { get; set; } = true;
    public double SkillBarY { get; set; } = 0.92;            // normalized Y for skill bar
    public double SkillX1 { get; set; } = 0.15;              // normalized X for skill slots (heuristic defaults)
    public double SkillX2 { get; set; } = 0.28;
    public double SkillX3 { get; set; } = 0.41;
    public double SkillX4 { get; set; } = 0.54;
    public bool UseSpeedBoost { get; set; } = true;          // click speed/x2 button periodically
    public int SpeedBoostEverySec { get; set; } = 40;        // minimal interval between speed boost attempts
    
    // Shop preferences
    public bool PreferElemental { get; set; } = true;
    public bool PreferBallistic { get; set; } = true;
    public bool PreferExplosive { get; set; } = true;
    public bool PreferEnergy   { get; set; } = true;
    public bool BuyOnlyPreferred { get; set; } = false;      // if true, skip non-preferred types
    public string MinRarityToBuy { get; set; } = "Gray";     // Gray/Szary, Blue/Niebieski, Pink/Różowy, Red/Czerwony
    // Menu / przełączniki
    public bool AutoCollect { get; set; } = true;
    public bool AutoBuyBest { get; set; } = true;
    public bool AutoRestart { get; set; } = true; // auto-focus i recovery po zmianie sceny
    // Kolejka trybów gry
    public string[] TrybyGry { get; set; } = new string[0];
    
    // Statistics for dashboard
    public int TotalActions { get; set; } = 0;
    public double SuccessRate { get; set; } = 0.85;
    public long UptimeMs { get; set; } = 0;
    // Bot mode and master switch
    public string Mode { get; set; } = "Podstawowy"; // Podstawowy | Zaawansowany | Niesamowity
    public bool BotEnabled { get; set; } = false;     // persisted state of master switch

    // Advanced features (roadmap stubs)
    public bool EnableAdjutants { get; set; } = false;       // Adiutanci i transformacje drużyny
    public bool EnableBossStrategy { get; set; } = false;     // Strategie walki z bossami
    public bool EnableDailyMissions { get; set; } = false;    // Codzienne misje i wydarzenia
    public bool EventModeEnabled { get; set; } = false;       // Tryb "Event Optimization"
    }

    public class SettingsService
    {
        private readonly string _dir;
        private readonly string _file;

        public SettingsService()
        {
            _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameBuddyBrain");
            _file = Path.Combine(_dir, "settings.json");
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_file)) return new AppSettings();
                var json = File.ReadAllText(_file);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch { return new AppSettings(); }
        }

        public void Save(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_file, json);
            }
            catch { /* ignore */ }
        }
    }
}
