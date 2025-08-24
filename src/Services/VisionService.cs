using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace GameBuddyBrain.Services
{
    // Minimal vision helper: captures window area and tries to find buttons by template or color in ROIs
    public class VisionService
    {
    public enum ButtonType { Next, Claim, Prestige, Chest, Buy, Speed, Start, Sell }

        public enum ShopErrorType { None, InsufficientMaterials, TooManyMercenaries }
        public enum WeaponType { Unknown, Elemental, Ballistic, Explosive, Energy }
        public enum Rarity { Unknown, Gray, Blue, Pink, Red }

        public struct PerkChoice
        {
            public GameDetector.RECT Rect;
            public Rarity ItemRarity;
            public int Index; // 0..2 left->right
        }

        public struct ShopItemInfo
        {
            public GameDetector.RECT Rect;
            public string TemplateKey; // template filename matched, if any
            public WeaponType Type;
            public Rarity ItemRarity;
            public int RowIndex;
        }

    public bool TryDetectButton(IntPtr hwnd, ButtonType type, out GameDetector.RECT rect)
        {
            rect = default;
            if (!GetWindowRect(hwnd, out var win)) return false;

            // Define ROI around expected normalized location
            double cx = 0.5, cy = 0.5;
            if (type == ButtonType.Next) { cx = 0.80; cy = 0.15; }
            else if (type == ButtonType.Claim) { cx = 0.50; cy = 0.60; }
            else if (type == ButtonType.Prestige) { cx = 0.50; cy = 0.30; }
            else if (type == ButtonType.Buy) { cx = 0.90; cy = 0.45; }
            else if (type == ButtonType.Speed) { cx = 0.92; cy = 0.06; }
            else if (type == ButtonType.Start) { cx = 0.50; cy = 0.65; }
            else if (type == ButtonType.Sell) { cx = 0.84; cy = 0.82; }
            int w = win.Right - win.Left;
            int h = win.Bottom - win.Top;
            int roiW = Math.Max(60, (int)(w * 0.2));
            int roiH = Math.Max(40, (int)(h * 0.15));
            int centerX = win.Left + (int)(w * cx);
            int centerY = win.Top + (int)(h * cy);
            var roi = new System.Drawing.Rectangle(centerX - roiW / 2, centerY - roiH / 2, roiW, roiH);

            // 1) Template match if a template exists under AppData\GameBuddyBrain\templates
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "GameBuddyBrain", "templates");
            string templatePath = type switch
            {
                ButtonType.Next => Path.Combine(dir, "next.png"),
                ButtonType.Claim => Path.Combine(dir, "claim.png"),
                ButtonType.Prestige => Path.Combine(dir, "prestige.png"),
                ButtonType.Buy => Path.Combine(dir, "buy.png"),
                ButtonType.Speed => Path.Combine(dir, "speed.png"),
                ButtonType.Chest => Path.Combine(dir, "chest.png"),
                ButtonType.Sell => Path.Combine(dir, "sell.png"),
                _ => Path.Combine(dir, "template.png"),
            };
            // allow jpg alternative
            var templatePathJpg = Path.ChangeExtension(templatePath, ".jpg");
            try
            {
                using var screenBmp = CaptureRect(roi);
                string? used = null;
                if (File.Exists(templatePath)) used = templatePath;
                else if (File.Exists(templatePathJpg)) used = templatePathJpg;
                if (!string.IsNullOrEmpty(used))
                {
                    using var templ = (Bitmap)Image.FromFile(used);
                    if (NaiveTemplateSearch(screenBmp, templ, 12, out var found))
                    {
                        rect.Left = roi.Left + found.X;
                        rect.Top = roi.Top + found.Y;
                        rect.Right = rect.Left + templ.Width;
                        rect.Bottom = rect.Top + templ.Height;
                        return true;
                    }
                }

                // 2) Color-based heuristic
                var kind = (type == ButtonType.Start) ? ButtonType.Next : type; // treat Start as green button like Next
                if (FindColoredBlob(screenBmp, kind, out var blob))
                {
                    rect.Left = roi.Left + blob.Left;
                    rect.Top = roi.Top + blob.Top;
                    rect.Right = roi.Left + blob.Right;
                    rect.Bottom = roi.Top + blob.Bottom;
                    return true;
                }
            }
            catch { /* ignore */ }

            return false;
        }

        // Very rough scan on the left side to find weakest (non-red) inventory slot by rarity strip color
        // Returns the rectangle of the chosen slot and its rarity. Scans up to 'slots' rows.
        public bool TryFindInventoryWeakestSlot(IntPtr hwnd, int slots, out GameDetector.RECT rect, out Rarity rarity)
        {
            rect = default; rarity = Rarity.Unknown;
            if (!GetWindowRect(hwnd, out var win)) return false;
            int w = win.Right - win.Left; int h = win.Bottom - win.Top; if (w <= 0 || h <= 0) return false;
            slots = Math.Max(1, Math.Min(6, slots));

            Rarity best = Rarity.Unknown; System.Drawing.Rectangle bestRoi = System.Drawing.Rectangle.Empty; int bestRow = -1;
            for (int i = 0; i < slots; i++)
            {
                double rowNy = 0.30 + i * 0.15; // left panel approximate rows
                int cx = win.Left + (int)(w * 0.14);
                int cy = win.Top + (int)(h * rowNy);
                var roi = new System.Drawing.Rectangle(cx - Math.Max(140, w / 6), cy - Math.Max(40, h / 25), Math.Max(260, w / 4), Math.Max(80, h / 12));
                try
                {
                    using var bmp = CaptureRect(roi);
                    var r = GuessRarity(bmp);
                    // weaker is lower rarity: Gray < Blue < Pink < Red; we want the weakest non-Red
                    if (r == Rarity.Red) continue;
                    if (best == Rarity.Unknown || Rank(r) < Rank(best)) { best = r; bestRoi = roi; bestRow = i; }
                }
                catch { }
            }

            if (bestRow >= 0)
            {
                rect = new GameDetector.RECT { Left = bestRoi.Left, Top = bestRoi.Top, Right = bestRoi.Right, Bottom = bestRoi.Bottom };
                rarity = best;
                return true;
            }
            return false;

            static int Rank(Rarity r) => r switch { Rarity.Gray => 0, Rarity.Blue => 1, Rarity.Pink => 2, Rarity.Red => 3, _ => 4 };
        }

        // Detect common shop error popups using templates in the center area
        public bool TryDetectShopError(IntPtr hwnd, out ShopErrorType errorType)
        {
            errorType = ShopErrorType.None;
            if (!GetWindowRect(hwnd, out var win)) return false;
            int w = win.Right - win.Left; int h = win.Bottom - win.Top;
            var center = new System.Drawing.Rectangle(win.Left + (int)(w * 0.2), win.Top + (int)(h * 0.2), (int)(w * 0.6), (int)(h * 0.5));
            try
            {
                using var bmp = CaptureRect(center);
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameBuddyBrain", "templates");
                var insufficient = Path.Combine(dir, "shop_insufficient.png");
                var tooMany = Path.Combine(dir, "shop_too_many.png");
                if (File.Exists(insufficient))
                {
                    using var t1 = (Bitmap)Image.FromFile(insufficient);
                    if (NaiveTemplateSearch(bmp, t1, 14, out _)) { errorType = ShopErrorType.InsufficientMaterials; return true; }
                }
                if (File.Exists(tooMany))
                {
                    using var t2 = (Bitmap)Image.FromFile(tooMany);
                    if (NaiveTemplateSearch(bmp, t2, 14, out _)) { errorType = ShopErrorType.TooManyMercenaries; return true; }
                }
            }
            catch { }
            return false;
        }

        // Scan shop rows and extract item info for a few slots
        public bool TryScanShop(IntPtr hwnd, int slots, out List<ShopItemInfo> items)
        {
            items = new List<ShopItemInfo>();
            if (!GetWindowRect(hwnd, out var win)) return false;
            int w = win.Right - win.Left; int h = win.Bottom - win.Top; if (w <= 0 || h <= 0) return false;
            slots = Math.Max(1, Math.Min(6, slots));

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameBuddyBrain", "templates");
            var weaponTemplates = new List<(string path, WeaponType type)>();
            try
            {
                if (Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, "weapon_*.png"))
                    {
                        var name = Path.GetFileName(file).ToLowerInvariant();
                        var wt = WeaponType.Unknown;
                        if (name.Contains("element")) wt = WeaponType.Elemental;
                        else if (name.Contains("ballist")) wt = WeaponType.Ballistic;
                        else if (name.Contains("explos")) wt = WeaponType.Explosive;
                        else if (name.Contains("energy")) wt = WeaponType.Energy;
                        weaponTemplates.Add((file, wt));
                    }
                }
            }
            catch { }

            for (int i = 0; i < slots; i++)
            {
                double rowNy = 0.25 + i * 0.18; // approximate rows on right side
                int cx = win.Left + (int)(w * 0.90);
                int cy = win.Top + (int)(h * rowNy);
                var roi = new System.Drawing.Rectangle(cx - Math.Max(120, w / 10), cy - Math.Max(40, h / 25), Math.Max(220, w / 6), Math.Max(80, h / 12));
                try
                {
                    using var bmp = CaptureRect(roi);
                    // Determine rarity by sampling a small left-edge strip color
                    var rarity = GuessRarity(bmp);
                    string key = $"row_{i}"; WeaponType wt = WeaponType.Unknown;
                    // Try to match any weapon template within ROI
                    foreach (var (path, t) in weaponTemplates)
                    {
                        try
                        {
                            using var templ = (Bitmap)Image.FromFile(path);
                            if (NaiveTemplateSearch(bmp, templ, 12, out _)) { key = Path.GetFileNameWithoutExtension(path); wt = t; break; }
                        }
                        catch { }
                    }
                    if (wt == WeaponType.Unknown)
                    {
                        wt = GuessWeaponType(bmp);
                    }
                    var item = new ShopItemInfo
                    {
                        Rect = new GameDetector.RECT { Left = roi.Left, Top = roi.Top, Right = roi.Right, Bottom = roi.Bottom },
                        TemplateKey = key,
                        Type = wt,
                        ItemRarity = rarity,
                        RowIndex = i
                    };
                    items.Add(item);
                }
                catch { }
            }

            return items.Count > 0;
        }

        private static WeaponType GuessWeaponType(Bitmap bmp)
        {
            int blueish = 0, magentaish = 0, cyangreen = 0, orangered = 0, graybrown = 0, total = 0;
            for (int y = bmp.Height / 5; y < bmp.Height * 4 / 5; y += 3)
            {
                for (int x = bmp.Width / 5; x < bmp.Width * 4 / 5; x += 3)
                {
                    var c = bmp.GetPixel(x, y); total++;
                    int max = Math.Max(c.R, Math.Max(c.G, c.B));
                    int min = Math.Min(c.R, Math.Min(c.G, c.B));
                    int sat = max - min;
                    if (c.B > 160 && c.R < 130) blueish++; // blue accents
                    if (c.R > 170 && c.B > 150 && c.G < 160 && Math.Abs(c.R - c.B) < 60) magentaish++; // purple/magenta
                    if (c.G > 160 && c.B > 140 && c.R < 170) cyangreen++; // energy-like cyan/green
                    if (c.R > 170 && c.G >= 90 && c.G <= 150 && c.B < 110) orangered++; // explosive orange/red
                    if (sat < 25 && max > 80) graybrown++; // low saturation -> metallic/brown
                }
            }
            // Heuristic mapping to weapon types
            // Prefer strong distinct signals first
            if (orangered > blueish && orangered > cyangreen && orangered > graybrown && orangered > magentaish) return WeaponType.Explosive;
            if (cyangreen > orangered && cyangreen > graybrown && cyangreen > magentaish) return WeaponType.Energy;
            if (blueish + magentaish > cyangreen && blueish + magentaish > orangered && blueish + magentaish > graybrown) return WeaponType.Elemental;
            if (graybrown > 0) return WeaponType.Ballistic;
            return WeaponType.Unknown;
        }

    private static Rarity GuessRarity(Bitmap bmp)
        {
            // Sample a 8px-wide vertical strip at 10% width to infer border color
            int x0 = Math.Max(0, bmp.Width / 10);
            int width = Math.Max(6, Math.Min(12, bmp.Width / 20));
        int goodGray = 0, goodBlue = 0, goodPink = 0, goodRed = 0, total = 0;
            for (int y = 0; y < bmp.Height; y += 2)
            {
                for (int x = x0; x < Math.Min(bmp.Width, x0 + width); x += 2)
                {
            var c = bmp.GetPixel(x, y); total++;
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            int sat = max - min;
            // Red: high R, low B/G
            if (c.R > 170 && c.G < 110 && c.B < 110 && sat > 40) goodRed++;
            // Blue: high B, lower R
            else if (c.B > 160 && c.R < 130 && sat > 30) goodBlue++;
            // Pink/Magenta: high R and B, moderate G
            else if (c.R > 180 && c.B > 150 && c.G < 160 && Math.Abs(c.R - c.B) < 60) goodPink++;
            // Gray: high brightness but low saturation
            else if (max > 150 && sat < 25) goodGray++;
                }
            }
        int m = Math.Max(Math.Max(goodGray, goodBlue), Math.Max(goodPink, goodRed));
            if (m == 0) return Rarity.Unknown;
            if (m == goodRed) return Rarity.Red;
            if (m == goodPink) return Rarity.Pink;
            if (m == goodBlue) return Rarity.Blue;
            return Rarity.Gray;
        }

        // Heuristic: scan the right-side upgrade/shop rows and return the most promising slot (green intensity)
        // Returns normalized coordinates (nx, ny) where to click
        public bool TryFindBestBuySlot(IntPtr hwnd, int slots, out double nx, out double ny)
        {
            nx = 0.92; ny = 0.45;
            if (!GetWindowRect(hwnd, out var win)) return false;
            int w = win.Right - win.Left;
            int h = win.Bottom - win.Top;
            if (w <= 0 || h <= 0) return false;

            slots = Math.Max(1, Math.Min(6, slots));
            int bestScore = 0; double bestNy = ny;
            for (int i = 0; i < slots; i++)
            {
                double rowNy = 0.25 + i * 0.18; // approximate rows
                int cx = win.Left + (int)(w * 0.90);
                int cy = win.Top + (int)(h * rowNy);
                var roi = new System.Drawing.Rectangle(cx - Math.Max(40, w/15), cy - Math.Max(20, h/25), Math.Max(80, w/12), Math.Max(40, h/18));
                try
                {
                    using var bmp = CaptureRect(roi);
                    int score = 0; int step = Math.Max(2, Math.Min(5, w/500));
                    for (int y = 0; y < bmp.Height; y += step)
                    {
                        for (int x = 0; x < bmp.Width; x += step)
                        {
                            var c = bmp.GetPixel(x, y);
                            // favor greenish/bright accents typical for active buy/upgrade buttons
                            if (c.G > 160 && c.G > c.R + 20 && c.G > c.B + 20) score++;
                            else if (c.R > 200 && c.G > 200 && c.B > 200) score++; // white highlight
                        }
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestNy = rowNy;
                    }
                }
                catch { }
            }

            if (bestScore > 0)
            {
                ny = bestNy;
                return true;
            }
            return false;
        }

        // Quick chest detector: returns count of likely chest/gold icons in the viewport
        public bool TryDetectChests(IntPtr hwnd, out int count)
        {
            count = 0;
            if (!GetWindowRect(hwnd, out var win)) return false;
            int w = win.Right - win.Left;
            int h = win.Bottom - win.Top;
            if (w <= 0 || h <= 0) return false;

            try
            {
                using var screen = CaptureRect(new System.Drawing.Rectangle(win.Left, win.Top, w, h));
                // divide into a small grid and score each cell for 'gold' color
                int cols = Math.Max(4, w / 200); // adapt to window size
                int rows = Math.Max(3, h / 160);
                var hits = 0;
                var visited = new bool[rows, cols];
                for (int ry = 0; ry < rows; ry++)
                {
                    for (int rx = 0; rx < cols; rx++)
                    {
                        int sx = rx * w / cols;
                        int sy = ry * h / rows;
                        int sw = Math.Max(24, w / cols);
                        int sh = Math.Max(16, h / rows);
                        var rect = new System.Drawing.Rectangle(sx, sy, Math.Min(sw, w - sx), Math.Min(sh, h - sy));
                        int score = 0; int step = 3;
                        for (int y = rect.Top; y < rect.Bottom; y += step)
                        {
                            for (int x = rect.Left; x < rect.Right; x += step)
                            {
                                var c = screen.GetPixel(x, y);
                                // gold/yellow-ish heuristic
                                if (c.R > 200 && c.G > 140 && c.B < 140) score++;
                            }
                        }
                        if (score > (rect.Width * rect.Height) / (step * step) / 6)
                        {
                            hits++;
                            visited[ry, rx] = true;
                        }
                    }
                }
                count = hits;
                return count > 0;
            }
            catch { return false; }
        }

        // Detect level-up perk selection dialog with 3 choices; classify by rarity and return their rects
        public bool TryDetectPerkChoices(IntPtr hwnd, out List<PerkChoice> choices)
        {
            choices = new List<PerkChoice>();
            if (!GetWindowRect(hwnd, out var win)) return false;
            int w = win.Right - win.Left; int h = win.Bottom - win.Top; if (w <= 0 || h <= 0) return false;

            // Assume dialog appears centered; three columns across mid-height
            // Each choice ROI: roughly a card area; tune widths/heights heuristically
            int dlgW = (int)(w * 0.80);
            int dlgH = (int)(h * 0.55);
            int dlgX = win.Left + (w - dlgW) / 2;
            int dlgY = win.Top + (int)(h * 0.20);
            var dialogRect = new System.Drawing.Rectangle(dlgX, dlgY, dlgW, dlgH);

            // Split into 3 approximate columns with gaps
            int gap = Math.Max(12, dlgW / 50);
            int cardW = (dlgW - 2 * gap) / 3;
            int cardH = dlgH - 2 * gap;
            int cardY = dlgY + gap;

            int signals = 0;
            for (int i = 0; i < 3; i++)
            {
                int cardX = dlgX + gap + i * (cardW + gap);
                var roi = new System.Drawing.Rectangle(cardX, cardY, cardW, cardH);
                try
                {
                    using var bmp = CaptureRect(roi);
                    var r = GuessRarity(bmp);
                    if (r != Rarity.Unknown)
                    {
                        choices.Add(new PerkChoice
                        {
                            Rect = new GameDetector.RECT { Left = roi.Left, Top = roi.Top, Right = roi.Right, Bottom = roi.Bottom },
                            ItemRarity = r,
                            Index = i
                        });
                        signals++;
                    }
                }
                catch { }
            }

            // Require at least one card with a recognizable rarity to avoid false positives
            return choices.Count > 0 && signals > 0;
        }

        private static Bitmap CaptureRect(System.Drawing.Rectangle rect)
        {
            var bmp = new Bitmap(rect.Width, rect.Height);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        // Very naive template search with absolute difference threshold per pixel (RGB)
        private static bool NaiveTemplateSearch(Bitmap haystack, Bitmap needle, int tol, out System.Drawing.Point found)
        {
            found = default;
            if (needle.Width > haystack.Width || needle.Height > haystack.Height) return false;
            for (int y = 0; y <= haystack.Height - needle.Height; y += 2) // step 2px to be cheaper
            {
                for (int x = 0; x <= haystack.Width - needle.Width; x += 2)
                {
                    if (PatchMatches(haystack, needle, x, y, tol))
                    {
                        found = new System.Drawing.Point(x, y);
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool PatchMatches(Bitmap hay, Bitmap nee, int ox, int oy, int tol)
        {
            for (int j = 0; j < nee.Height; j += 3) // sample every 3px for speed
            {
                for (int i = 0; i < nee.Width; i += 3)
                {
                    var c1 = hay.GetPixel(ox + i, oy + j);
                    var c2 = nee.GetPixel(i, j);
                    if (ColorDiff(c1, c2) > tol) return false;
                }
            }
            return true;
        }

        private static int ColorDiff(Color a, Color b)
        {
            int dr = a.R - b.R; int dg = a.G - b.G; int db = a.B - b.B;
            return Math.Abs(dr) + Math.Abs(dg) + Math.Abs(db);
        }

        // Crude color blob finder for green-ish (Next) or yellow/orange-ish (Claim)
        private static bool FindColoredBlob(Bitmap bmp, ButtonType type, out System.Drawing.Rectangle blob)
        {
            blob = System.Drawing.Rectangle.Empty;
            int minW = Math.Max(30, bmp.Width / 8);
            int minH = Math.Max(18, bmp.Height / 8);

            // Scan with block sampling to find a dense region of target color
            int bestScore = 0; System.Drawing.Rectangle best = System.Drawing.Rectangle.Empty;
            for (int y = 0; y < bmp.Height; y += 4)
            {
                for (int x = 0; x < bmp.Width; x += 4)
                {
                    var score = SampleScore(bmp, x, y, 16, 10, type);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new System.Drawing.Rectangle(x, y, 16, 10);
                    }
                }
            }

            if (bestScore < 20) return false; // no strong signal

            // Expand best block to approximate a blob by growing rectangle while color ratio remains high
            var rect = GrowBlob(bmp, best, type);
            if (rect.Width >= minW && rect.Height >= minH)
            {
                blob = rect;
                return true;
            }
            return false;
        }

        private static int SampleScore(Bitmap bmp, int sx, int sy, int w, int h, ButtonType type)
        {
            int score = 0; int step = 2;
            for (int y = sy; y < Math.Min(bmp.Height, sy + h); y += step)
            {
                for (int x = sx; x < Math.Min(bmp.Width, sx + w); x += step)
                {
                    var c = bmp.GetPixel(x, y);
                    if (type == ButtonType.Next)
                    {
                        // green-ish
                        if (c.G > 160 && c.R < 140 && c.B < 140) score++;
                    }
                    else
                    {
                        // yellow/orange-ish
                        if (c.R > 170 && c.G > 120 && c.B < 120) score++;
                    }
                }
            }
            return score;
        }

        private static System.Drawing.Rectangle GrowBlob(Bitmap bmp, System.Drawing.Rectangle seed, ButtonType type)
        {
            var rect = seed;
            bool changed;
            int iter = 0;
            do
            {
                changed = false; iter++;
                var expanded = System.Drawing.Rectangle.Inflate(rect, 6, 6);
                expanded.Intersect(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                double ratio = ColorRatio(bmp, expanded, type);
                if (ratio > 0.22) { rect = expanded; changed = true; }
            } while (changed && iter < 8);
            return rect;
        }

        private static double ColorRatio(Bitmap bmp, System.Drawing.Rectangle area, ButtonType type)
        {
            int good = 0, total = 0; int step = 3;
            for (int y = area.Top; y < area.Bottom; y += step)
            {
                for (int x = area.Left; x < area.Right; x += step)
                {
                    var c = bmp.GetPixel(x, y); total++;
                    if (type == ButtonType.Next)
                        good += (c.G > 160 && c.R < 140 && c.B < 140) ? 1 : 0;
                    else
                        good += (c.R > 170 && c.G > 120 && c.B < 120) ? 1 : 0;
                }
            }
            return total == 0 ? 0 : (double)good / total;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out GameDetector.RECT lpRect);
    }
}
