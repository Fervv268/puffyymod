using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace GameBuddyBrain.Services
{
    // Scans a source folder for weapon images, classifies into 4 types, creates thumbnails,
    // de-duplicates by content hash, and maintains a catalog under %AppData%/GameBuddyBrain/templates
    public class TemplateCatalogService
    {
        public sealed class TemplateInfo
        {
            public string Key { get; set; } = string.Empty;     // weapon_elemental_ABC123
            public string FileName { get; set; } = string.Empty; // full path to template png
            public string ThumbPath { get; set; } = string.Empty; // thumbnail png path
            public VisionService.WeaponType Type { get; set; }
            public string Hash { get; set; } = string.Empty;     // SHA1
            public DateTime AddedUtc { get; set; }
        }

        private readonly string _rootDir;
        private readonly string _thumbsDir;
        private readonly string _catalogFile;
        private readonly List<TemplateInfo> _catalog = new List<TemplateInfo>();

        public TemplateCatalogService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _rootDir = Path.Combine(appData, "GameBuddyBrain", "templates");
            _thumbsDir = Path.Combine(_rootDir, "thumbs");
            _catalogFile = Path.Combine(_rootDir, "catalog.json");
            try { Directory.CreateDirectory(_rootDir); Directory.CreateDirectory(_thumbsDir); } catch { }
            LoadCatalog();
        }

        public IReadOnlyList<TemplateInfo> List() => _catalog;

        public void BuildOrUpdateFrom(string sourceDir)
        {
            if (!Directory.Exists(sourceDir)) return;
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                try
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp") continue;
                    using var fs = File.OpenRead(file);
                    string hash = ComputeSha1(fs);
                    if (_catalog.Any(c => string.Equals(c.Hash, hash, StringComparison.OrdinalIgnoreCase)))
                    {
                        // already added
                        continue;
                    }
                    // Load as bitmap for type guess and thumbnail
                    using var bmp = (Bitmap)Image.FromFile(file);
                    var type = GuessTypeFromNameOrPixels(file, bmp);
                    // Normalize filename pattern for VisionService to pick up: weapon_<type>_<hash>.png
                    string typeName = type.ToString().ToLowerInvariant();
                    string outName = $"weapon_{typeName}_{hash.Substring(0, 8)}.png";
                    string outPath = Path.Combine(_rootDir, outName);
                    bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                    // Create thumbnail 128px max side
                    string thumbPath = Path.Combine(_thumbsDir, Path.ChangeExtension(outName, ".thumb.png"));
                    CreateThumbnail(bmp, thumbPath, 128);
                    var info = new TemplateInfo
                    {
                        Key = Path.GetFileNameWithoutExtension(outName),
                        FileName = outPath,
                        ThumbPath = thumbPath,
                        Type = type,
                        Hash = hash,
                        AddedUtc = DateTime.UtcNow
                    };
                    _catalog.Add(info);
                }
                catch { }
            }
            SaveCatalog();
        }

        private static string ComputeSha1(Stream s)
        {
            using var sha = SHA1.Create();
            s.Position = 0;
            var hash = sha.ComputeHash(s);
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }

        private static VisionService.WeaponType GuessTypeFromNameOrPixels(string path, Bitmap bmp)
        {
            var name = Path.GetFileName(path).ToLowerInvariant();
            if (name.Contains("element")) return VisionService.WeaponType.Elemental;
            if (name.Contains("ballist")) return VisionService.WeaponType.Ballistic;
            if (name.Contains("explos")) return VisionService.WeaponType.Explosive;
            if (name.Contains("energy")) return VisionService.WeaponType.Energy;
            // fallback: reuse VisionService heuristic
            try { return (VisionService.WeaponType)typeof(VisionService)
                        .GetMethod("GuessWeaponType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                        .Invoke(null, new object[] { bmp })!; }
            catch { return VisionService.WeaponType.Unknown; }
        }

        private static void CreateThumbnail(Bitmap src, string outPath, int maxSide)
        {
            try
            {
                int w = src.Width, h = src.Height;
                double scale = (double)maxSide / Math.Max(1, Math.Max(w, h));
                if (scale > 1) scale = 1; // do not upscale
                int tw = Math.Max(1, (int)(w * scale));
                int th = Math.Max(1, (int)(h * scale));
                using var thumb = new Bitmap(tw, th);
                using (var g = Graphics.FromImage(thumb))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, tw, th);
                }
                thumb.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch { }
        }

        private void LoadCatalog()
        {
            try
            {
                if (File.Exists(_catalogFile))
                {
                    var json = File.ReadAllText(_catalogFile);
                    var list = JsonSerializer.Deserialize<List<TemplateInfo>>(json);
                    if (list != null) { _catalog.Clear(); _catalog.AddRange(list); }
                }
            }
            catch { }
        }

        private void SaveCatalog()
        {
            try
            {
                var json = JsonSerializer.Serialize(_catalog, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_catalogFile, json);
            }
            catch { }
        }
    }
}
