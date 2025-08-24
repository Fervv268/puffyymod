using System;
using System.Collections.Generic;
using UI = GameBuddyBrain.UI;

namespace GameBuddyBrain.Services
{
    public class BrainService
    {
        private readonly GameDetector _detector = new GameDetector();
    private readonly Clicker _clicker = new Clicker();
    private bool _isGameActive = false;
    private readonly System.Timers.Timer _botTimer;
    private readonly System.Timers.Timer _attachTimer;
    private bool _turboMode = false;
    private int _errorCount = 0;
    private readonly Random _rand = new Random();
    private bool _botEnabled = false;
    private readonly VisionService _vision = new VisionService();
    // New automation flags
    private bool _autoFarm = true;
    private bool _autoPrestige = false;
    private bool _safeMode = false;
    private int _clickDelayMs = 150;
    private bool _autoFocus = true;
    private bool _safetyPause = true;
    private bool _antiAfk = true;
    private int _detectProgress = 0; // 0..100
    private string _currentAction = "-";
    private string _state = "Szukam gry";
    // Cooldowns to avoid repeated accidental actions
    private DateTime _lastPrestige = DateTime.MinValue;
    private TimeSpan _prestigeCooldown = TimeSpan.FromMinutes(5);
    private DateTime _lastBuy = DateTime.MinValue;
    private TimeSpan _buyCooldown = TimeSpan.FromSeconds(8);
    private TimeSpan _nextTimeout = TimeSpan.FromSeconds(20);
    private TimeSpan _noProgressPrestige = TimeSpan.FromMinutes(15);
    private int _buyScanSlots = 3;
    private DateTime _lastProgress = DateTime.UtcNow; // any successful action: buy/next/claim/speed
    private DateTime _lastNext = DateTime.MinValue;
    // Shop sequencing state
    private string? _lastOwnedWeaponKey; // template key of last acquired weapon to look for duplicates
    private readonly HashSet<string> _ownedWeapons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    // Idle Zombie Wave advanced options
    private bool _autoChests = true;
    private bool _autoSkills = false;
    private int _skillIntervalSec = 30;
    private bool _skill1 = true, _skill2 = true, _skill3 = true, _skill4 = true;
    private double _skillBarY = 0.92, _skillX1 = 0.15, _skillX2 = 0.28, _skillX3 = 0.41, _skillX4 = 0.54;
    private bool _useSpeedBoost = true;
    private int _speedBoostEverySec = 40;
    private DateTime _lastSpeed = DateTime.MinValue;
    private DateTime _lastSkills = DateTime.MinValue;
    // Perk/level-up selection
    private DateTime _lastPerkPick = DateTime.MinValue;
    private TimeSpan _perkScanCooldown = TimeSpan.FromSeconds(5);
    private bool _autoPickPerks = true;
    // Walking/XP collect
    private bool _walkAround = true; // simulate movement
    private DateTime _lastWalk = DateTime.MinValue;
    private TimeSpan _walkInterval = TimeSpan.FromSeconds(2.5);
    // Shop prefs
    private bool _prefElemental = true, _prefBallistic = true, _prefExplosive = true, _prefEnergy = true, _buyOnlyPreferred = false;
    private VisionService.Rarity _minRarityToBuy = VisionService.Rarity.Gray;
    // High-level switches and game modes
    private bool _autoCollect = true;   // mapuje na _enableRewards
    private bool _autoBuyBest = true;   // gdy false, pomijamy próby zakupu
    private bool _autoRestart = true;   // heurystyka: próba startu z menu głównego
    private readonly Queue<string> _modeQueue = new Queue<string>();
    private string? _currentMode = null;
    private DateTime _lastRecoveryAttempt = DateTime.MinValue;
    private void AdvanceMode()
    {
        if (_modeQueue.Count > 0)
        {
            _currentMode = _modeQueue.Dequeue();
            Log($"Tryb gry -> następny: {_currentMode}");
        }
        else
        {
            Log("Tryby gry: kolejka pusta (pozostaję w bieżącym)");
        }
    }

    public event Action<int>? DetectionProgressChanged; // 0..100
    public event Action<string>? CurrentActionChanged;  // krótkie opisy akcji
    public event Action<string>? StateChanged;          // "Szukam gry" | "Podłączono" | "Pracuję"

    public int GetDetectProgress() => _detectProgress;
    public string GetState() => _state;

    // Debug rectangles for overlay visualization
    private GameDetector.RECT? _lastNextRect;
    private GameDetector.RECT? _lastClaimRect;

    // AI Strategy properties for modern UI
    public bool IsRunning => _botEnabled;
    public string CurrentStrategy { get; private set; } = "Adaptive";
    public double PerformanceScore { get; private set; } = 85.0;
    public int ProcessesDetected => _detector.ProcessCount;
    public bool TargetProcessActive => _detector.IsAttached;

    // Routines toggles
    private bool _enableAttack = true;
    private bool _enableUpgrade = true;
    private bool _enableNextWave = true;
    private bool _enableRewards = true;

    // Routine scheduler
    private readonly List<Routine> _routines = new List<Routine>();
    private readonly Dictionary<string, TimeSpan> _customIntervals = new();

    // Routine scheduler item
    private class Routine
    {
        public string Name { get; }
        public TimeSpan BaseInterval { get; }
        public double Jitter { get; } // 0..1
        public Action Action { get; }
        public TimeSpan? OverrideInterval { get; set; }
        private DateTime _nextRun;

        public Routine(string name, TimeSpan baseInterval, double jitter, Action action)
        {
            Name = name;
            BaseInterval = baseInterval;
            Jitter = Math.Clamp(jitter, 0, 1);
            Action = action;
            _nextRun = DateTime.UtcNow;
        }

        public bool IsDue(DateTime now) => now >= _nextRun;

        public void ScheduleNext(DateTime now, Random rand)
        {
            var baseInt = OverrideInterval ?? BaseInterval;
            double factor = 1 + (rand.NextDouble() * 2 - 1) * Jitter; // base ± jitter
            var delta = TimeSpan.FromMilliseconds(baseInt.TotalMilliseconds * Math.Max(0.3, factor));
            _nextRun = now + delta;
        }

        public void UpdateInterval(TimeSpan newInterval)
        {
            OverrideInterval = newInterval;
        }
    }

    public bool IsTurbo => _turboMode;
    public bool IsAttached => _detector.IsAttached;
    public string? AttachedTitle => _detector.AttachedTitle;
    public string CurrentMode => _currentMode ?? "Podstawowy";
    public bool TryGetAttachedRect(out int left, out int top, out int right, out int bottom)
        {
            left = top = right = bottom = 0;
            if (_detector.TryGetWindowRect(out var r))
            {
                left = r.Left; top = r.Top; right = r.Right; bottom = r.Bottom;
                return true;
            }
            return false;
        }

    public void EnableBot()
        {
            _botEnabled = true;
            Log("Bot: włączony");
            if (_detector.IsAttached) SetState("Pracuję");
        }

    // Settings from UI
    public void SetAutoFarm(bool v) { _autoFarm = v; Log("AutoFarm: " + (v ? "ON" : "OFF")); }
    public void SetAutoPrestige(bool v) { _autoPrestige = v; Log("AutoPrestige: " + (v ? "ON" : "OFF")); }
    public void SetSafeMode(bool v) { _safeMode = v; Log("SafeMode: " + (v ? "ON" : "OFF")); }
    public void SetClickDelay(int ms) { _clickDelayMs = Math.Max(10, Math.Min(5000, ms)); Log("ClickDelay set: " + _clickDelayMs + "ms"); }

        public void DisableBot()
        {
            _botEnabled = false;
            Log("Bot: wyłączony");
            if (_detector.IsAttached) SetState("Podłączono"); else SetState("Szukam gry");
        }

        // Apply built-in mode presets
        public void ApplyMode(string mode)
        {
            mode = (mode ?? string.Empty).Trim();
            _currentMode = mode;
            switch (mode.ToLowerInvariant())
            {
                case "niesamowity":
                    // Everything ON, aggressive timings
                    _safeMode = false;
                    _autoFarm = true; _autoPrestige = true; _autoCollect = true; _autoBuyBest = true; _autoChests = true; _autoSkills = true; _useSpeedBoost = true;
                    _enableAttack = true; _enableUpgrade = true; _enableNextWave = true; _enableRewards = true;
                    ConfigureStrategy(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(8), Math.Max(3, _buyScanSlots));
                    _turboMode = true;
                    CurrentStrategy = "Aggressive";
                    break;
                case "zaawansowany":
                    _safeMode = false;
                    _autoFarm = true; _autoPrestige = true; _autoCollect = true; _autoBuyBest = true; _autoChests = true; _autoSkills = true; _useSpeedBoost = true;
                    _enableAttack = true; _enableUpgrade = true; _enableNextWave = true; _enableRewards = true;
                    ConfigureStrategy(TimeSpan.FromSeconds(8), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(12), _buyScanSlots);
                    _turboMode = false;
                    CurrentStrategy = "Adaptive";
                    break;
                default: // Podstawowy
                    _safeMode = true; // cautious
                    _autoFarm = true; _autoPrestige = false; _autoCollect = true; _autoBuyBest = true; _autoChests = true; _autoSkills = false; _useSpeedBoost = true;
                    _enableAttack = true; _enableUpgrade = true; _enableNextWave = true; _enableRewards = true;
                    ConfigureStrategy(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(8), TimeSpan.FromSeconds(25), TimeSpan.FromMinutes(15), Math.Max(2, _buyScanSlots));
                    _turboMode = false;
                    CurrentStrategy = "Conservative";
                    break;
            }
            Log($"Zastosowano tryb: {mode}");
        }

        private bool _pausedByHotkey = false;
        private (bool attack, bool upgrade, bool next, bool rewards)? _prePauseRoutines;
        public void TogglePauseAll()
        {
            if (!_pausedByHotkey)
            {
                // Save current routine states and stop
                _prePauseRoutines = (_enableAttack, _enableUpgrade, _enableNextWave, _enableRewards);
                _enableAttack = _enableUpgrade = _enableNextWave = _enableRewards = false;
                _pausedByHotkey = true;
                Log("Pause: wszystkie funkcje wstrzymane (~)");
            }
            else
            {
                // Restore previous routine states
                if (_prePauseRoutines.HasValue)
                {
                    var s = _prePauseRoutines.Value;
                    _enableAttack = s.attack; _enableUpgrade = s.upgrade; _enableNextWave = s.next; _enableRewards = s.rewards;
                }
                _pausedByHotkey = false;
                Log("Pause: wznowiono funkcje (~)");
            }
        }

        public void ToggleTurbo()
        {
            _turboMode = !_turboMode;
            Log("Turbo: " + (_turboMode ? "ON" : "OFF"));
        }

        // Strategy configuration from settings
        public void ConfigureStrategy(TimeSpan buyCooldown, TimeSpan prestigeCooldown, TimeSpan nextTimeout, TimeSpan noProgressPrestige, int buyScanSlots)
        {
            if (buyCooldown > TimeSpan.Zero) _buyCooldown = buyCooldown;
            if (prestigeCooldown > TimeSpan.Zero) _prestigeCooldown = prestigeCooldown;
            if (nextTimeout > TimeSpan.Zero) _nextTimeout = nextTimeout;
            if (noProgressPrestige > TimeSpan.Zero) _noProgressPrestige = noProgressPrestige;
            if (buyScanSlots > 0) _buyScanSlots = buyScanSlots;
            Log($"Strategy configured: buyCd={_buyCooldown}, prestigeCd={_prestigeCooldown}, nextTimeout={_nextTimeout}, noProgPrestige={_noProgressPrestige}, buyScanSlots={_buyScanSlots}");
        }

    // Toggle features from UI
    public void SetAutoPickPerks(bool v) { _autoPickPerks = v; Log("AutoPerki: " + (v ? "ON" : "OFF")); }
    public void SetWalkAround(bool v) { _walkAround = v; Log("Ruch po mapie: " + (v ? "ON" : "OFF")); }

            // New small API for advanced manual actions triggered from UI
            public void CollectAll()
            {
                try
                {
                    if (!_detector.IsAttached) { Log("CollectAll: brak podpięcia"); return; }
                    if (_detector.AttachedHwnd.HasValue)
                    {
                        // Try to click common reward area a few times to ensure collection
                        var h = _detector.AttachedHwnd.Value;
                        for (int i = 0; i < 3; i++)
                        {
                            _clicker.ClickNormalized(h, 0.50 + RandOffset(0.05), 0.60 + RandOffset(0.05), 120);
                        }
                        Log("CollectAll: wykonano serie klików");
                    }
                }
                catch (Exception ex) { Log("CollectAll error: " + ex.Message); }
            }

            public void ForcePrestige()
            {
                try
                {
                    if (!_detector.IsAttached) { Log("ForcePrestige: brak podpięcia"); return; }
                    // Heuristic click near top-left where prestige buttons often appear in emulators; conservative single click
                    if (_detector.AttachedHwnd.HasValue)
                    {
                        var h = _detector.AttachedHwnd.Value;
                        _clicker.ClickNormalized(h, 0.20 + RandOffset(0.03), 0.20 + RandOffset(0.03), 220);
                    }
                    Log("ForcePrestige: kliknięto potencjalne miejsce prestiżu");
                }
                catch (Exception ex) { Log("ForcePrestige error: " + ex.Message); }
            }

    public BrainService(SystemMonitor monitor)
        {
            monitor.WindowChanged += OnWindowActivated;
            _botTimer = new System.Timers.Timer(2000); // co 2 sekundy – główna pętla
            _botTimer.Elapsed += (s, e) => BotLogic();
            _botTimer.Start();

            // Co 3 sekundy próbuj automatycznie podpiąć okno gry/emulatora
            _attachTimer = new System.Timers.Timer(3000);
            _attachTimer.Elapsed += (s, e) => EnsureAttached();
            _attachTimer.Start();

            SetupRoutines();
        }

    private void OnWindowActivated(string title)
        {
            _isGameActive = title.Contains("IdleZombieWaveOcalalali", StringComparison.OrdinalIgnoreCase)
                         || title.Contains("Idle Zombie Wave", StringComparison.OrdinalIgnoreCase);
            if (_isGameActive)
            {
                Log("Wykryto okno gry!");
                SetDetectProgress(100);
                SetState(_botEnabled ? "Pracuję" : "Podłączono");
            }
        }

        private void BotLogic()
        {
            if (!_botEnabled) return;

            // Advanced AI Strategy - Dynamic decision making
            UpdateAIStrategy();

            // Jeśli nie widzimy gry po aktywnym oknie, spróbuj i tak pracować z podpiętym uchwytem
            if (!_detector.IsAttached)
            {
                BumpDetectProgress(2);
                SetState("Szukam gry");
                EnsureAttached();
                if (!_detector.IsAttached) { TryBackgroundAttach(); return; }
            }

            try
            {
                // Sense -> Decide/Act via FSM
                SetState(_botEnabled ? "Pracuję" : "Podłączono");
                if (_safetyPause && IsUserInteracting()) { Log("Pauza: aktywność użytkownika"); return; }
                var snap = SenseGame();
                StepFSM(snap);
                // occasional turbo spike
                if (!_turboMode && _rand.NextDouble() < 0.05) { _turboMode = true; Log("Turbo: ON (auto)"); }
            }
            catch (Exception ex)
            {
                _errorCount++;
                Log("BotLogic error: " + ex.Message);
                TryRecovery();
            }
        }

        // Minimal heuristic: enable/disable skill slots to match weapon type style
        private void AdaptSkillsForWeapon(VisionService.WeaponType type)
        {
            // Default: enable all
            _autoSkills = true;
            _skill1 = _skill2 = _skill3 = _skill4 = true;
            switch (type)
            {
                case VisionService.WeaponType.Elemental:
                    // favor periodic boosts (2,4)
                    _skill1 = true; _skill2 = true; _skill3 = false; _skill4 = true;
                    break;
                case VisionService.WeaponType.Ballistic:
                    // favor single-target/crit (1,3)
                    _skill1 = true; _skill2 = false; _skill3 = true; _skill4 = false;
                    break;
                case VisionService.WeaponType.Explosive:
                    // favor AoE (2,3,4)
                    _skill1 = false; _skill2 = true; _skill3 = true; _skill4 = true;
                    break;
                case VisionService.WeaponType.Energy:
                    // sustain DPS (1,2,4)
                    _skill1 = true; _skill2 = true; _skill3 = false; _skill4 = true;
                    break;
                default:
                    break;
            }
            Log($"Dopasowano umiejętności do typu broni: {type}");
        }
        public IEnumerable<GameDetector.WindowInfo> ListCandidates() => _detector.EnumerateCandidates();

        // High-level configuration from UI settings
        public void ConfigureHighLevel(AppSettings s)
        {
            _autoCollect = s.AutoCollect; _enableRewards = _autoCollect;
            _autoBuyBest = s.AutoBuyBest;
            _autoRestart = s.AutoRestart; _autoFocus = s.AutoRestart; // powiąż z auto-focus
            // Tryby gry – ustaw kolejkę w podanej kolejności
            _modeQueue.Clear();
            if (s.TrybyGry != null)
            {
                foreach (var m in s.TrybyGry)
                {
                    if (!string.IsNullOrWhiteSpace(m)) _modeQueue.Enqueue(m);
                }
            }
            if (_currentMode == null && _modeQueue.Count > 0)
            {
                _currentMode = _modeQueue.Dequeue();
                Log($"Tryb gry: {_currentMode}");
            }
            Log($"HighLevel: collect={_autoCollect}, buyBest={_autoBuyBest}, restart={_autoRestart}, modes={_modeQueue.Count + (_currentMode!=null?1:0)}");
        }

        // Ensure we're attached to a valid game/emulator window; update state/progress accordingly
        private void EnsureAttached()
        {
            try
            {
                if (!_detector.IsAttached)
                {
                    if (_detector.TryAutoAttach())
                    {
                        Log("Podłączono do okna: " + _detector.AttachedTitle);
                        SetDetectProgress(100);
                        SetState(_botEnabled ? "Pracuję" : "Podłączono");
                    }
                    else
                    {
                        // gently decay progress while searching
                        SetDetectProgress(Math.Max(0, _detectProgress - 2));
                    }
                }
            }
            catch { /* ignore transient attach errors */ }
        }

        // Public reconnect trigger for UI
        public void Reconnect()
        {
            EnsureAttached();
            if (!_detector.IsAttached)
            {
                TryBackgroundAttach();
            }
        }

        private void TryBackgroundAttach()
        {
            try
            {
                if (_detector.TryAutoAttach())
                {
                    Log("Podłączono do okna (background): " + _detector.AttachedTitle);
                    SetDetectProgress(100);
                    SetState(_botEnabled ? "Pracuję" : "Podłączono");
                }
            }
            catch { }
        }

        // =========================
        // Advanced FSM-based bot
        // =========================

        private enum BotState
        {
            Searching,
            MainMenu,
            LevelUp,
            Playing,
            Shopping,
            Prestige,
            Rewards,
            Recovering
        }

        private BotState _botState = BotState.Searching;

    private sealed record class GameSnapshot
        {
            public IntPtr Hwnd { get; init; }
            public bool HasStart { get; init; }
            public GameDetector.RECT StartRect { get; init; }
            public bool HasNext { get; init; }
            public GameDetector.RECT NextRect { get; init; }
            public bool HasClaim { get; init; }
            public GameDetector.RECT ClaimRect { get; init; }
            public bool HasPrestige { get; init; }
            public GameDetector.RECT PrestigeRect { get; init; }
            public bool HasBuy { get; init; }
            public GameDetector.RECT BuyRect { get; init; }
            public VisionService.ShopErrorType ShopError { get; init; }
            public int ChestCount { get; init; }
            public List<VisionService.PerkChoice> PerkChoices { get; init; } = new();
        }

        private GameSnapshot SenseGame()
        {
            var snap = new GameSnapshot { Hwnd = _detector.AttachedHwnd ?? IntPtr.Zero };
            if (snap.Hwnd == IntPtr.Zero) return snap;

            try
            {
                if (_vision.TryDetectButton(snap.Hwnd, VisionService.ButtonType.Start, out var start))
                {
                    snap = snap with { HasStart = true, StartRect = start };
                }
            }
            catch { }
            try
            {
                if (_vision.TryDetectButton(snap.Hwnd, VisionService.ButtonType.Next, out var next))
                {
                    _lastNextRect = next; // overlay debug
                    snap = snap with { HasNext = true, NextRect = next };
                }
            }
            catch { }
            try
            {
                if (_vision.TryDetectButton(snap.Hwnd, VisionService.ButtonType.Claim, out var claim))
                {
                    _lastClaimRect = claim; // overlay debug
                    snap = snap with { HasClaim = true, ClaimRect = claim };
                }
            }
            catch { }
            try
            {
                if (_vision.TryDetectButton(snap.Hwnd, VisionService.ButtonType.Prestige, out var pr))
                {
                    snap = snap with { HasPrestige = true, PrestigeRect = pr };
                }
            }
            catch { }
            try
            {
                if (_vision.TryDetectButton(snap.Hwnd, VisionService.ButtonType.Buy, out var br))
                {
                    snap = snap with { HasBuy = true, BuyRect = br };
                }
            }
            catch { }
            try
            {
                if (_vision.TryDetectShopError(snap.Hwnd, out var err))
                {
                    snap = snap with { ShopError = err };
                }
            }
            catch { }
            try
            {
                if (_vision.TryDetectChests(snap.Hwnd, out var ch))
                {
                    snap = snap with { ChestCount = ch };
                }
            }
            catch { }
            try
            {
                if (_autoPickPerks && _vision.TryDetectPerkChoices(snap.Hwnd, out var choices) && choices.Count > 0)
                {
                    snap = snap with { PerkChoices = choices };
                }
            }
            catch { }

            return snap;
        }

        private void StepFSM(GameSnapshot s)
        {
            // Global interrupts
            if (s.Hwnd == IntPtr.Zero || !_detector.IsAttached)
            {
                _botState = BotState.Searching;
                SetState("Szukam gry");
                TryBackgroundAttach();
                BumpDetectProgress(2);
                return;
            }

            // Foreground focus if desired
            if (_autoFocus) _detector.EnsureForeground();

            // Perk dialog takes precedence
            if (s.PerkChoices.Count > 0)
            {
                _botState = BotState.LevelUp;
                SetState("Perk");
                PickBestPerk(s);
                return;
            }

            // Handle blocking shop errors immediately
            if (s.ShopError != VisionService.ShopErrorType.None)
            {
                _botState = BotState.Recovering;
                SetState("Odzyskiwanie");
                HandleShopError(s);
                return;
            }

            // Decide coarse state
            if (s.HasStart)
            {
                _botState = BotState.MainMenu;
            }
            else if (s.HasPrestige)
            {
                _botState = BotState.Prestige;
            }
            else if (s.HasBuy)
            {
                _botState = BotState.Shopping;
            }
            else
            {
                _botState = BotState.Playing;
            }

            switch (_botState)
            {
                case BotState.MainMenu:
                    SetState("Menu");
                    if (_autoRestart)
                    {
                        ClickRect(s.Hwnd, s.StartRect, "Start");
                        _lastProgress = DateTime.UtcNow; AdvanceMode();
                    }
                    break;

                case BotState.Prestige:
                    SetState("Prestiż");
                    if (_autoPrestige && DateTime.UtcNow - _lastPrestige > _prestigeCooldown)
                    {
                        ClickRect(s.Hwnd, s.PrestigeRect, "Prestige");
                        _lastPrestige = DateTime.UtcNow; _lastProgress = _lastPrestige; AdvanceMode();
                    }
                    else
                    {
                        // continue playing if not time yet
                        FallbackPlay(s);
                    }
                    break;

                case BotState.Shopping:
                    SetState("Sklep");
                    DoInventoryMaintenance(s);
                    DoSmartShop(s);
                    // If blocked by no mats, try Next stage
                    if (s.ShopError == VisionService.ShopErrorType.InsufficientMaterials)
                    {
                        TryClickNext(s);
                    }
                    break;

                case BotState.Playing:
                    SetState("W grze");
                    // Priority: Claim -> Next -> Chests -> Speed/Skills -> Move
                    if (s.HasClaim) { ClickRect(s.Hwnd, s.ClaimRect, "Claim"); _lastProgress = DateTime.UtcNow; break; }
                    if (s.HasNext && _autoFarm) { ClickRect(s.Hwnd, s.NextRect, "Next"); _lastNext = DateTime.UtcNow; _lastProgress = _lastNext; break; }
                    if (_autoChests && s.ChestCount > 0 && _autoFarm) { OpenChests(s); break; }
                    DoSpeedAndSkills(s);
                    DoWalkAndCollect(s);
                    // Fallback Next if idle too long
                    TryAutonomousNext(s);
                    break;
            }

            // Keep background routines ticking as a low-level fallback
            RunDueRoutines();
        }

        private void PickBestPerk(GameSnapshot s)
        {
            if ((DateTime.UtcNow - _lastPerkPick) < _perkScanCooldown) return;
            var choices = s.PerkChoices;
            if (choices.Count == 0) return;

            VisionService.Rarity best = VisionService.Rarity.Gray;
            foreach (var c in choices) if ((int)c.ItemRarity > (int)best) best = c.ItemRarity;
            int targetIdx = -1;
            foreach (var pref in new[] { 1, 2, 0 })
            {
                foreach (var c in choices) if (c.Index == pref && c.ItemRarity == best) { targetIdx = pref; break; }
                if (targetIdx >= 0) break;
            }
            var pick = choices.Find(c => c.Index == targetIdx);
            if (targetIdx < 0) pick = choices[0];
            var (px, py) = Clicker.CenterOfRect(new Clicker.RECT { Left = pick.Rect.Left, Top = pick.Rect.Top, Right = pick.Rect.Right, Bottom = pick.Rect.Bottom });
            SetCurrentAction($"Perk: wybór {best}");
            _clicker.ClickAbsolute(px, py, _clickDelayMs);
            _lastPerkPick = DateTime.UtcNow; _lastProgress = _lastPerkPick;
            Log($"Wybrano perk: {best} (idx {pick.Index})");
            BumpDetectProgress(8);
        }

        private void HandleShopError(GameSnapshot s)
        {
            switch (s.ShopError)
            {
                case VisionService.ShopErrorType.InsufficientMaterials:
                    // Try Next to continue farming
                    if (!TryClickNext(s))
                    {
                        // fallback click near top-right
                        _clicker.ClickNormalized(s.Hwnd, 0.80 + RandOffset(0.02), 0.15 + RandOffset(0.02), 120);
                    }
                    _lastProgress = DateTime.UtcNow;
                    break;
                case VisionService.ShopErrorType.TooManyMercenaries:
                    // Click Start if visible, otherwise center-bottom to close
                    if (s.HasStart) ClickRect(s.Hwnd, s.StartRect, "Start");
                    else _clicker.ClickNormalized(s.Hwnd, 0.50 + RandOffset(0.02), 0.70 + RandOffset(0.02), 120);
                    _lastProgress = DateTime.UtcNow;
                    break;
            }
        }

        private bool TryClickNext(GameSnapshot s)
        {
            if (_autoFarm)
            {
                if (s.HasNext)
                {
                    ClickRect(s.Hwnd, s.NextRect, "Next");
                    _lastNext = DateTime.UtcNow; _lastProgress = _lastNext; return true;
                }
                _clicker.ClickNormalized(s.Hwnd, 0.80 + RandOffset(0.02), 0.15 + RandOffset(0.02), _clickDelayMs);
                _lastNext = DateTime.UtcNow; _lastProgress = _lastNext; return true;
            }
            return false;
        }

        private void DoInventoryMaintenance(GameSnapshot s)
        {
            if (!_enableUpgrade) return;
            // Sell weakest (non-red) occasionally to keep inventory clean
            if (_vision.TryFindInventoryWeakestSlot(s.Hwnd, 5, out var weakRect, out var weakRarity))
            {
                if ((DateTime.UtcNow - _lastBuy) > TimeSpan.FromSeconds(5) && weakRarity != VisionService.Rarity.Red)
                {
                    var (wx, wy) = Clicker.CenterOfRect(new Clicker.RECT { Left = weakRect.Left, Top = weakRect.Top, Right = weakRect.Right, Bottom = weakRect.Bottom });
                    _clicker.ClickAbsolute(wx, wy, 80);
                    if (_vision.TryDetectButton(s.Hwnd, VisionService.ButtonType.Sell, out var sellRect))
                    {
                        var (sx, sy) = Clicker.CenterOfRect(new Clicker.RECT { Left = sellRect.Left, Top = sellRect.Top, Right = sellRect.Right, Bottom = sellRect.Bottom });
                        _clicker.ClickAbsolute(sx, sy, 120);
                    }
                    else
                    {
                        _clicker.ClickNormalized(s.Hwnd, 0.84 + RandOffset(0.02), 0.82 + RandOffset(0.02), 120);
                    }
                    Log($"Ekwipunek: sprzedano najsłabszy ({weakRarity})");
                    _lastProgress = DateTime.UtcNow; BumpDetectProgress(4);
                }
            }
        }

        private void DoSmartShop(GameSnapshot s)
        {
            if (!_autoFarm || _safeMode || !_enableUpgrade) return;
            if ((DateTime.UtcNow - _lastBuy) <= _buyCooldown) return;

            if (_vision.TryScanShop(s.Hwnd, _buyScanSlots, out var items) && items.Count > 0)
            {
                var filtered = new List<VisionService.ShopItemInfo>();
                foreach (var it in items)
                {
                    if (it.ItemRarity < _minRarityToBuy) continue;
                    if (_buyOnlyPreferred)
                    {
                        bool preferred =
                            (it.Type == VisionService.WeaponType.Elemental && _prefElemental) ||
                            (it.Type == VisionService.WeaponType.Ballistic && _prefBallistic) ||
                            (it.Type == VisionService.WeaponType.Explosive && _prefExplosive) ||
                            (it.Type == VisionService.WeaponType.Energy && _prefEnergy);
                        if (!preferred) continue;
                    }
                    filtered.Add(it);
                }
                if (filtered.Count == 0) filtered = items;

                int Score(VisionService.ShopItemInfo it)
                {
                    int s1 = it.ItemRarity switch { VisionService.Rarity.Red => 10000, VisionService.Rarity.Pink => 7000, VisionService.Rarity.Blue => 4000, VisionService.Rarity.Gray => 1000, _ => 0 };
                    if (!string.IsNullOrEmpty(_lastOwnedWeaponKey) && it.TemplateKey.Equals(_lastOwnedWeaponKey, StringComparison.OrdinalIgnoreCase)) s1 += 600;
                    if (_ownedWeapons.Contains(it.TemplateKey)) s1 += 400;
                    int typeScore = it.Type switch
                    {
                        VisionService.WeaponType.Elemental => _prefElemental ? 120 : 20,
                        VisionService.WeaponType.Energy => _prefEnergy ? 110 : 20,
                        VisionService.WeaponType.Explosive => _prefExplosive ? 90 : 20,
                        VisionService.WeaponType.Ballistic => _prefBallistic ? 80 : 20,
                        _ => 10
                    };
                    s1 += typeScore; s1 -= it.RowIndex * 3; return s1;
                }
                filtered.Sort((a, b) => Score(b).CompareTo(Score(a)));
                var target = filtered[0];
                if (s.HasBuy)
                {
                    var (bx, by) = Clicker.CenterOfRect(new Clicker.RECT { Left = s.BuyRect.Left, Top = s.BuyRect.Top, Right = s.BuyRect.Right, Bottom = s.BuyRect.Bottom });
                    _clicker.ClickAbsolute(bx + _rand.Next(-4, 5), by + _rand.Next(-4, 5), 100);
                }
                else
                {
                    double nx = 0.92; double ny = 0.25 + target.RowIndex * 0.18;
                    _clicker.ClickNormalized(s.Hwnd, nx + RandOffset(0.02), ny + RandOffset(0.02), 90);
                }
                _lastBuy = DateTime.UtcNow; _lastProgress = _lastBuy;
                if (!string.IsNullOrEmpty(target.TemplateKey)) { _ownedWeapons.Add(target.TemplateKey); _lastOwnedWeaponKey = target.TemplateKey; }
                Log($"Sklep: kupiono cel={target.TemplateKey} typ={target.Type} rzadkość={target.ItemRarity}");
                BumpDetectProgress(8);
                AdaptSkillsForWeapon(target.Type);
            }
            else
            {
                // lightweight fallback probing on right side
                for (int i = 0; i < _buyScanSlots; i++)
                {
                    double nx = 0.92; double ny = 0.25 + i * 0.18;
                    _clicker.ClickNormalized(s.Hwnd, nx + RandOffset(0.02), ny + RandOffset(0.02), 80);
                    System.Threading.Thread.Sleep(50);
                }
                Log("Sklep: heurystyczne próby zakupu");
                BumpDetectProgress(3);
            }
        }

        private void DoSpeedAndSkills(GameSnapshot s)
        {
            if (_useSpeedBoost && (DateTime.UtcNow - _lastSpeed).TotalSeconds > Math.Max(5, _speedBoostEverySec))
            {
                if (_vision.TryDetectButton(s.Hwnd, VisionService.ButtonType.Speed, out var sp))
                {
                    var (sx, sy) = Clicker.CenterOfRect(new Clicker.RECT { Left = sp.Left, Top = sp.Top, Right = sp.Right, Bottom = sp.Bottom });
                    _clicker.ClickAbsolute(sx, sy, 50);
                }
                else
                {
                    _clicker.ClickNormalized(s.Hwnd, 0.92 + RandOffset(0.01), 0.06 + RandOffset(0.01), 50);
                }
                _lastSpeed = DateTime.UtcNow; _lastProgress = _lastSpeed; Log("Speed boost");
            }

            if (_autoSkills && (DateTime.UtcNow - _lastSkills).TotalSeconds > Math.Max(5, _skillIntervalSec))
            {
                if (_skill1) _clicker.ClickNormalized(s.Hwnd, _skillX1 + RandOffset(0.01), _skillBarY, 40);
                if (_skill2) _clicker.ClickNormalized(s.Hwnd, _skillX2 + RandOffset(0.01), _skillBarY, 40);
                if (_skill3) _clicker.ClickNormalized(s.Hwnd, _skillX3 + RandOffset(0.01), _skillBarY, 40);
                if (_skill4) _clicker.ClickNormalized(s.Hwnd, _skillX4 + RandOffset(0.01), _skillBarY, 40);
                _lastSkills = DateTime.UtcNow; _lastProgress = _lastSkills; Log("Umiejętności");
            }
        }

        private void DoWalkAndCollect(GameSnapshot s)
        {
            if (!_autoFarm || !_walkAround) return;
            if (DateTime.UtcNow - _lastWalk <= _walkInterval) return;
            var seq = _rand.Next(0, 4);
            var key = seq switch { 0 => Clicker.KeyCode.W, 1 => Clicker.KeyCode.A, 2 => Clicker.KeyCode.S, _ => Clicker.KeyCode.D };
            _clicker.KeyDownTo(s.Hwnd, key);
            System.Threading.Thread.Sleep(_rand.Next(80, 160));
            _clicker.KeyUpTo(s.Hwnd, key);
            if (_rand.NextDouble() < 0.25)
            {
                var key2 = _rand.NextDouble() < 0.5 ? Clicker.KeyCode.A : Clicker.KeyCode.D;
                _clicker.KeyDownTo(s.Hwnd, key2);
                System.Threading.Thread.Sleep(_rand.Next(60, 120));
                _clicker.KeyUpTo(s.Hwnd, key2);
            }
            if (_rand.NextDouble() < 0.70) _clicker.ClickNormalized(s.Hwnd, 0.50 + RandOffset(0.06), 0.55 + RandOffset(0.08), 40);
            _lastWalk = DateTime.UtcNow; Log("Ruch+XP"); BumpDetectProgress(1);
        }

        private void OpenChests(GameSnapshot s)
        {
            int count = Math.Min(4, Math.Max(1, s.ChestCount));
            for (int i = 0; i < count; i++)
            {
                _clicker.ClickNormalized(s.Hwnd, 0.50 + RandOffset(0.15), 0.80 + RandOffset(0.08), _clickDelayMs);
                System.Threading.Thread.Sleep(100);
            }
            Log($"Otwieram skrzynki: {s.ChestCount}"); _lastProgress = DateTime.UtcNow; BumpDetectProgress(5);
        }

        private void TryAutonomousNext(GameSnapshot s)
        {
            var sinceProgress = DateTime.UtcNow - _lastProgress;
            var sinceNext = DateTime.UtcNow - _lastNext;
            if (sinceProgress > _nextTimeout && sinceNext > TimeSpan.FromSeconds(5))
            {
                _clicker.ClickNormalized(s.Hwnd, 0.80 + RandOffset(0.02), 0.15 + RandOffset(0.02), _clickDelayMs);
                Log($"Autonomicznie: Next po {sinceProgress.TotalSeconds:F0}s bez progresu");
                _lastNext = DateTime.UtcNow;
            }
        }

        private void FallbackPlay(GameSnapshot s)
        {
            // Minimal actions when unsure: speed/skills and gentle movement
            DoSpeedAndSkills(s);
            DoWalkAndCollect(s);
        }

        private void ClickRect(IntPtr hwnd, GameDetector.RECT rect, string label)
        {
            var (x, y) = Clicker.CenterOfRect(new Clicker.RECT { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom });
            SetCurrentAction($"Klik: {label}");
            _clicker.ClickAbsolute(x, y, _clickDelayMs);
            Log($"[Vision] Kliknięto {label}");
        }

        // Minimal recovery: if window attached but looks like main menu, try clicking Start
        private void TryRecovery()
        {
            if (!_autoRestart) return;
            if (!_detector.AttachedHwnd.HasValue) return;
            var now = DateTime.UtcNow;
            if ((now - _lastRecoveryAttempt) < TimeSpan.FromSeconds(10)) return;
            _lastRecoveryAttempt = now;
            try
            {
                var h = _detector.AttachedHwnd.Value;
                if (_vision.TryDetectButton(h, VisionService.ButtonType.Start, out var startRect))
                {
                    var (sx, sy) = Clicker.CenterOfRect(new Clicker.RECT { Left = startRect.Left, Top = startRect.Top, Right = startRect.Right, Bottom = startRect.Bottom });
                    _clicker.ClickAbsolute(sx, sy, 150);
                    Log("Recovery: wciśnięto Start z menu głównego");
                    _lastProgress = DateTime.UtcNow;
                    // po starcie z menu głównego uznaj poprzedni tryb za zakończony
                    AdvanceMode();
                    return;
                }
                // fallback heurystyka – kliknij w centrum dolnej części
                _clicker.ClickNormalized(h, 0.50 + RandOffset(0.02), 0.70 + RandOffset(0.03), 150);
                Log("Recovery: heurystyczny Start");
                _lastProgress = DateTime.UtcNow;
                AdvanceMode();
            }
            catch (Exception ex)
            {
                Log("Recovery error: " + ex.Message);
            }
        }

        // Debug overlay getters
        public bool TryGetLastNextRect(out GameDetector.RECT rect)
        {
            rect = default; if (_lastNextRect.HasValue) { rect = _lastNextRect.Value; return true; } return false;
        }
        public bool TryGetLastClaimRect(out GameDetector.RECT rect)
        {
            rect = default; if (_lastClaimRect.HasValue) { rect = _lastClaimRect.Value; return true; } return false;
        }

        // Public toggles for UI
        public void SetEnableAttack(bool enabled)  { _enableAttack = enabled; Log("Atak: " + (enabled ? "ON" : "OFF")); }
        public void SetEnableUpgrade(bool enabled) { _enableUpgrade = enabled; Log("Ulepszenia: " + (enabled ? "ON" : "OFF")); }
        public void SetEnableNextWave(bool enabled){ _enableNextWave = enabled; Log("Następna fala: " + (enabled ? "ON" : "OFF")); }
        public void SetEnableRewards(bool enabled) { _enableRewards = enabled; Log("Nagrody: " + (enabled ? "ON" : "OFF")); }

        public void SetAutoFocus(bool enabled) { _autoFocus = enabled; Log("Auto-focus: " + (enabled ? "ON" : "OFF")); }
        public void SetSafetyPause(bool enabled) { _safetyPause = enabled; Log("Pauza przy aktywności: " + (enabled ? "ON" : "OFF")); }
        public void SetAntiAfk(bool enabled) { _antiAfk = enabled; Log("Anti-AFK: " + (enabled ? "ON" : "OFF")); }

        public void ConfigureInterval(string routineName, TimeSpan interval)
        {
            _customIntervals[routineName] = interval;
            foreach (var r in _routines)
            {
                if (r.Name.Equals(routineName, StringComparison.OrdinalIgnoreCase))
                {
                    r.OverrideInterval = interval;
                }
            }
            Log($"Interwał {routineName}: {interval}");
        }

        // Configure Idle Zombie Wave specific automation
        public void ConfigureIdleZombieWave(AppSettings s)
        {
            _autoChests = s.AutoChests;
            _autoSkills = s.AutoSkills;
            _skillIntervalSec = s.SkillIntervalSec;
            _skill1 = s.Skill1; _skill2 = s.Skill2; _skill3 = s.Skill3; _skill4 = s.Skill4;
            _skillBarY = s.SkillBarY; _skillX1 = s.SkillX1; _skillX2 = s.SkillX2; _skillX3 = s.SkillX3; _skillX4 = s.SkillX4;
            _useSpeedBoost = s.UseSpeedBoost;
            _speedBoostEverySec = s.SpeedBoostEverySec;
            Log("IZW settings applied");
        }

        public void ConfigureShopPreferences(AppSettings s)
        {
            _prefElemental = s.PreferElemental;
            _prefBallistic = s.PreferBallistic;
            _prefExplosive = s.PreferExplosive;
            _prefEnergy = s.PreferEnergy;
            _buyOnlyPreferred = s.BuyOnlyPreferred;
            _minRarityToBuy = s.MinRarityToBuy switch
            {
                "Red" => VisionService.Rarity.Red,
                "Pink" => VisionService.Rarity.Pink,
                "Blue" => VisionService.Rarity.Blue,
                _ => VisionService.Rarity.Gray
            };
            Log($"Shop prefs: types Elt={_prefElemental} Bal={_prefBallistic} Expl={_prefExplosive} En={_prefEnergy}, onlyPreferred={_buyOnlyPreferred}, minRarity={_minRarityToBuy}");
        }
        private void SetupRoutines()
        {
            _routines.Clear();
            // Szybkie klikanie ataku – co 0.3-0.5s (sterowane przez pętlę, więc tylko co cykl oceniamy)
            _routines.Add(new Routine(
                name: "Attack",
                baseInterval: TimeSpan.FromMilliseconds(350),
                jitter: 0.3,
                action: () =>
                {
                    if (!_enableAttack) return;
                    if (!_detector.IsAttached || !_detector.AttachedHwnd.HasValue) return;
                    var h = _detector.AttachedHwnd.Value;
                    _clicker.ClickNormalized(h, 0.50 + RandOffset(0.03), 0.50 + RandOffset(0.03), 0);
                    if (_rand.NextDouble() < 0.15) { _clicker.KeyPress(Clicker.KeyCode.SPACE); }
                }));

            // Ulepszenia – co 6-10s
            _routines.Add(new Routine(
                name: "Upgrade",
                baseInterval: TimeSpan.FromSeconds(8),
                jitter: 0.3,
                action: () =>
                {
                    if (!_enableUpgrade) return;
                    if (!_detector.IsAttached || !_detector.AttachedHwnd.HasValue) return;
                    var h2 = _detector.AttachedHwnd.Value;
                    _clicker.ClickNormalized(h2, 0.90 + RandOffset(0.02), 0.85 + RandOffset(0.02), 200);
                    Log("Ulepszenie");
                }));

            // Następna fala – co 15-30s
            _routines.Add(new Routine(
                name: "NextWave",
                baseInterval: TimeSpan.FromSeconds(22),
                jitter: 0.35,
                action: () =>
                {
                    if (!_enableNextWave) return;
                    if (!_detector.IsAttached || !_detector.AttachedHwnd.HasValue) return;
                    var h3 = _detector.AttachedHwnd.Value;
                    _clicker.ClickNormalized(h3, 0.80 + RandOffset(0.03), 0.15 + RandOffset(0.03), 200);
                    Log("Następna fala");
                }));

            // Odbiór nagród – co 60-120s
            _routines.Add(new Routine(
                name: "Rewards",
                baseInterval: TimeSpan.FromSeconds(90),
                jitter: 0.5,
                action: () =>
                {
                    if (!_enableRewards) return;
                    if (!_detector.IsAttached || !_detector.AttachedHwnd.HasValue) return;
                    var h4 = _detector.AttachedHwnd.Value;
                    _clicker.ClickNormalized(h4, 0.50 + RandOffset(0.05), 0.60 + RandOffset(0.05), 300);
                    Log("Odbiór nagrody");
                }));

            var now = DateTime.UtcNow;
            foreach (var r in _routines) r.ScheduleNext(now, _rand);
        }

        private void RunDueRoutines()
        {
            var now = DateTime.UtcNow;
            foreach (var r in _routines)
            {
                if (r.IsDue(now))
                {
                    try { r.Action(); }
                    catch (Exception ex) { Log($"Routine {r.Name} error: {ex.Message}"); }
                    r.ScheduleNext(now, _rand);
                }
            }
    }

        private double RandOffset(double range) => (_rand.NextDouble() * 2 - 1) * range;

        private void Log(string msg)
        {
            System.Diagnostics.Debug.WriteLine($"[BOT] {msg} - BrainService.cs:1094");
            SafeLog(msg);
        }

        private void SetDetectProgress(int value)
        {
            var v = Math.Clamp(value, 0, 100);
            _detectProgress = v;
            DetectionProgressChanged?.Invoke(v);
        }

        private void BumpDetectProgress(int delta)
        {
            SetDetectProgress(_detectProgress + delta);
        }

        private void SetCurrentAction(string text)
        {
            _currentAction = text;
            CurrentActionChanged?.Invoke(text);
        }

        private void SetState(string text)
        {
            if (string.Equals(_state, text, StringComparison.Ordinal)) return;
            _state = text;
            StateChanged?.Invoke(text);
        }

        private static string LogFilePath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameBuddyBrain", "run.log");
        private static void SafeLog(string msg)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(LogFilePath)!;
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            }
            catch { }
        }

        private DateTime _lastUserInputCheck = DateTime.MinValue;
        private bool _wasUserActiveRecently = false;
        private bool IsUserInteracting()
        {
            // Bardzo lekki heurystyczny placeholder: co 3s generuj drobny ruch gdy Anti-AFK, i zwracaj false (brak pauzy) gdy Anti-AFK aktywny i wykonał się ruch
            // W przyszłości: sprawdzać realny stan klawiatury/myszki.
            var now = DateTime.UtcNow;
            if (_antiAfk && (now - _lastUserInputCheck).TotalSeconds > 3)
            {
                _lastUserInputCheck = now;
                // wykonaj niewielkie poruszenie kursorem w miejscu okna gry (jeśli jest)
                if (_detector.AttachedHwnd.HasValue)
                {
                    _clicker.ClickNormalized(_detector.AttachedHwnd.Value, 0.01 + _rand.NextDouble() * 0.02, 0.01 + _rand.NextDouble() * 0.02, 0);
                }
                _wasUserActiveRecently = false;
            }
            return _safetyPause && _wasUserActiveRecently; // placeholder, zawsze false póki nie wykrywanie aktywności
        }

        private void UpdateAIStrategy()
        {
            // Advanced AI Strategy Logic - Dynamic decision making
            var now = DateTime.UtcNow;
            var timeSinceLastProgress = now - _lastProgress;

            // Adaptive Strategy Selection
            if (timeSinceLastProgress.TotalMinutes > 10)
            {
                CurrentStrategy = "Recovery";
                PerformanceScore = Math.Max(30, PerformanceScore - 5);
            }
            else if (_turboMode)
            {
                CurrentStrategy = "Aggressive";
                PerformanceScore = Math.Min(95, PerformanceScore + 2);
            }
            else if (_safeMode)
            {
                CurrentStrategy = "Conservative";
                PerformanceScore = Math.Min(90, PerformanceScore + 1);
            }
            else
            {
                CurrentStrategy = "Adaptive";
                PerformanceScore = Math.Min(100, PerformanceScore + 0.5);
            }

            // Dynamic cooldown adjustment based on performance
            if (PerformanceScore > 90)
            {
                _clickDelayMs = Math.Max(100, _clickDelayMs - 5);
            }
            else if (PerformanceScore < 60)
            {
                _clickDelayMs = Math.Min(300, _clickDelayMs + 10);
            }

            // Intelligent routine prioritization
            if (CurrentStrategy == "Aggressive")
            {
                _enableAttack = true;
                _enableUpgrade = true;
                _enableNextWave = true;
                _enableRewards = true;
            }
            else if (CurrentStrategy == "Conservative")
            {
                _enableAttack = true;
                _enableUpgrade = false;
                _enableNextWave = false;
                _enableRewards = true;
            }
            
            // Update action frequency based on strategy
            UpdateRoutineIntervals();
        }

        private void UpdateRoutineIntervals()
        {
            // Dynamic interval adjustment based on current strategy
            var multiplier = CurrentStrategy switch
            {
                "Aggressive" => 0.7,
                "Conservative" => 1.5,
                "Recovery" => 2.0,
                _ => 1.0
            };

            foreach (var routine in _routines)
            {
                if (_customIntervals.ContainsKey(routine.Name))
                {
                    var baseInterval = _customIntervals[routine.Name];
                    routine.UpdateInterval(TimeSpan.FromMilliseconds(baseInterval.TotalMilliseconds * multiplier));
                }
            }
        }
    }
}
