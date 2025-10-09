using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Renders bottom-center spawn icons for Township / Merchant based on pattern_{id}.json.
    /// - Township ONLY  -> assets/misc/spawn_village.png
    /// - Merchant ONLY  -> assets/misc/spawn_merchant.png
    /// - Both present   -> assets/misc/spawn_both.png
    ///
    /// Icon is fit into a configurable box and centered at the bottom.
    /// The vertical center of the icon is placed at: canvasHeight - margin.
    /// Margins are chosen by whether a special-event banner exists for this pattern:
    ///   - If a special event exists:   use SpecialEventIcon.BottomMargins.IconAndBanner
    ///   - If NO special event exists:  use SpecialEventIcon.BottomMargins.IconOnly
    ///
    /// No text is drawn here; this file is dedicated to the PNG overlay.
    /// </summary>
    public static class LabelerSpecialIcon
    {
        public static void Label(
        MagickImage background,
        string patternId,
        string appsettingsPath,
        string cwd)
        {
            try
            {
                // 1) read config
                var appRoot = ReadJson(appsettingsPath);
                
                // 2) detect Township / Merchant in pattern_{id}.json
                var patternPath = TryResolvePatternPath(cwd, patternId);
                bool hasTownship = false, hasMerchant = false;
                
                if (File.Exists(patternPath))
                {
                    using var pfs = File.OpenRead(patternPath);
                    using var pdoc = JsonDocument.Parse(pfs);
                    if (pdoc.RootElement.ValueKind == JsonValueKind.Object &&
                    pdoc.RootElement.TryGetProperty("pois", out var pois) &&
                    pois.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var po in pois.EnumerateArray())
                        {
                            if (po.ValueKind != JsonValueKind.Object) continue;
                            if (po.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                            {
                                var nv = n.GetString() ?? string.Empty;
                                if (nv == "Township - Township") hasTownship = true;
                                if (nv == "Event - Scale-Bearing Merchant") hasMerchant = true;
                            }
                        }
                    }
                }
                
                if (!hasTownship && !hasMerchant)
                {
                    Console.WriteLine("[SpecialIcon] none present; skip");
                    return;
                }
                
                // 3) decide if a special-event banner exists for this pattern
                bool hasSpecialEvent = TryHasSpecialEvent(appRoot, patternId, cwd);
                
                // 4) read margins and box from appsettings
                int iconOnlyMargin = 28;
                int iconAndBannerMargin = 36;
                int boxW = 172, boxH = 70;
                
                if (appRoot.ValueKind == JsonValueKind.Object &&
                appRoot.TryGetProperty("SpecialEventIcon", out var seIcon) &&
                seIcon.ValueKind == JsonValueKind.Object)
                {
                    if (seIcon.TryGetProperty("BottomMargins", out var bm) && bm.ValueKind == JsonValueKind.Object)
                    {
                        if (bm.TryGetProperty("IconOnly", out var io) && io.ValueKind == JsonValueKind.Number)
                        iconOnlyMargin = io.GetInt32();
                        if (bm.TryGetProperty("IconAndBanner", out var iab) && iab.ValueKind == JsonValueKind.Number)
                        iconAndBannerMargin = iab.GetInt32();
                    }
                    if (seIcon.TryGetProperty("IconBox", out var ibox) && ibox.ValueKind == JsonValueKind.Object)
                    {
                        if (ibox.TryGetProperty("Width", out var w) && w.ValueKind == JsonValueKind.Number)
                        boxW = w.GetInt32();
                        if (ibox.TryGetProperty("Height", out var h) && h.ValueKind == JsonValueKind.Number)
                        boxH = h.GetInt32();
                    }
                }
                
                int centerX = (int)background.Width / 2;
                int centerY = (int)background.Height - (hasSpecialEvent ? iconAndBannerMargin : iconOnlyMargin);
                
                // 5) choose icon path
                var iconRel = hasTownship && hasMerchant
                ? "../assets/misc/spawn_both.png"
                : hasTownship
                ? "../assets/misc/spawn_village.png"
                : "../assets/misc/spawn_merchant.png";
                
                var iconPath = ResolvePath(iconRel, cwd);
                if (!File.Exists(iconPath))
                {
                    // try cwd/assets/...
                    var alt = Path.Combine(cwd, iconRel);
                    if (File.Exists(alt)) iconPath = alt;
                }
                
                if (!File.Exists(iconPath))
                {
                    Console.WriteLine($"[SpecialIcon] icon not found: {iconPath}");
                    return;
                }
                
                using var icon = new MagickImage(iconPath);
                // 6) scale to fit into box while preserving aspect ratio
                double scale = Math.Min((double)boxW / icon.Width, (double)boxH / icon.Height);
                int newW = Math.Max(1, (int)Math.Round(icon.Width * scale));
                int newH = Math.Max(1, (int)Math.Round(icon.Height * scale));
                icon.Resize((uint)newW, (uint)newH);
                
                // 7) compute top-left such that the icon is centered at (centerX, centerY)
                int x = centerX - newW / 2;
                int y = centerY - newH / 2;
                
                background.Composite(icon, x, y, CompositeOperator.Over);
                
                Console.WriteLine($"[SpecialIcon] drawn mode={(hasSpecialEvent ? "icon+banner" : "icon-only")} township={hasTownship} merchant={hasMerchant}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpecialIcon] skipped: {ex.Message}");
            }
        }
        
        // -------- helpers --------
        
        private static JsonElement ReadJson(string path)
        {
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            return doc.RootElement.Clone();
        }
        
        private static string ResolvePath(string relativeOrAbsolute, string cwd)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolute)) return cwd;
            return Path.IsPathRooted(relativeOrAbsolute)
            ? relativeOrAbsolute
            : Path.Combine(cwd, relativeOrAbsolute);
        }
        
        private static string TryResolvePatternPath(string cwd, string id)
        {
            var candidates = new[]
            {
                Path.Combine(cwd, $"pattern_{id}.json"),
                Path.Combine(cwd, "data", "pattern", $"pattern_{id}.json"),
                Path.Combine(cwd, "../data", "pattern", $"pattern_{id}.json"),
                Path.Combine(Path.GetDirectoryName(cwd) ?? cwd, "data", "pattern", $"pattern_{id}.json")
            };
            foreach (var p in candidates)
            {
                try { if (File.Exists(p)) return p; } catch {}
            }
            return candidates[0];
        }
        
        private static bool TryHasSpecialEvent(JsonElement appRoot, string patternId, string cwd)
        {
            var summaryPath = ResolvePath(GetString(appRoot, "SummaryPath", "../data/summary.json")!, cwd);
            if (!File.Exists(summaryPath))
            {
                var alt = ResolvePath("../data/param/summary.json", cwd);
                if (!File.Exists(alt)) return false;
                summaryPath = alt;
            }
            try
            {
                using var fs = File.OpenRead(summaryPath);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("patterns", out var pats) ||
                pats.ValueKind != JsonValueKind.Array) return false;
                
                foreach (var pat in pats.EnumerateArray())
                {
                    if (pat.ValueKind == JsonValueKind.Object &&
                    pat.TryGetProperty("id", out var idEl) &&
                    idEl.ValueKind == JsonValueKind.String &&
                    string.Equals(idEl.GetString(), patternId, StringComparison.OrdinalIgnoreCase))
                    {
                        // consider any non-empty special_event_key or special_event as "exists"
                        if (GetString(pat, "special_event_key", null) is string k && !string.IsNullOrWhiteSpace(k))
                        return true;
                        if (GetString(pat, "special_event", null) is string v && !string.IsNullOrWhiteSpace(v))
                        return true;
                        return false;
                    }
                }
            }
            catch { }
            return false;
        }
        
        private static string? GetString(JsonElement obj, string prop, string? def)
        {
            if (obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(prop, out var el) &&
            el.ValueKind == JsonValueKind.String)
            return el.GetString();
            return def;
        }
    }
}