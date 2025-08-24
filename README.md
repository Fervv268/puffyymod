# GameBuddyBrain

WPF .NET 8 desktop assistant/bot for Idle Zombie Wave. Single-file, self-contained build with a minimal, rounded UI and a priority-based FSM brain.

Highlights:
- Sense–Decide–Act FSM: Searching, MainMenu, LevelUp, Playing, Shopping, Prestige, Rewards, Recovering
- Vision heuristics + templates, safe clicker, game window attach
- Auto farm, perks, shop (rarity prefs, duplicates/upgrades), chests, skills, speed boost
- Modes: Podstawowy, Zaawansowany, Niesamowity + Event Mode toggle
- Settings persisted to JSON; overlay and hotkeys

Build/Publish:
- Use zip_publish.ps1 (Release). Outputs to Desktop\New_v2\publish and creates a desktop shortcut

Run:
- Start the desktop shortcut or GameBuddyBrain.exe from Desktop\New_v2\publish

Notes:
- Windows-only WPF app, DPI aware (PerMonitorV2)
- System.Drawing.Common used for simple imaging; CA1416 warnings are expected
