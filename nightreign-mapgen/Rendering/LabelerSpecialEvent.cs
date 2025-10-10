using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Renders a bottom-centered "Special Event" banner and optional icon for a given pattern id.
    /// </summary>
    public static class LabelerSpecialEvent
    {
        public static void Label(
        MagickImage background,
        string patternId,
        string appsettingsPath,
        string cwd,
        int bottomMarginPx = 24)
        {
            try
            {
                bool logged = false;
                // Make sure punctuation like ':' renders verbatim
                background.Settings.TextEncoding = Encoding.UTF8;
                
                // Load appsettings
                var appRoot = ReadJson(appsettingsPath);
                
                // Locate and parse summary.json
                var summaryPath = ResolvePath(GetString(appRoot, "SummaryPath", "../data/summary.json"), cwd);
                if (!File.Exists(summaryPath))
                {
                    // Some repos keep params under ../data/param
                    var alt = ResolvePath("../data/param/summary.json", cwd);
                    if (!File.Exists(alt))
                    {
                        Console.WriteLine($"[SpecialEvent] summary.json not found at {summaryPath} or {alt}");
                        return;
                    }
                    summaryPath = alt;
                }
                
                using var fs = File.OpenRead(summaryPath);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("patterns", out var pats) || pats.ValueKind != JsonValueKind.Array)
                {
                    if (NightReign.MapGen.Program.Verbose) Console.WriteLine("[SpecialEvent] invalid summary.json structure");
                    return;
                }
                
                JsonElement? matched = null;
                foreach (var pat in pats.EnumerateArray())
                {
                    if (pat.ValueKind == JsonValueKind.Object &&
                    pat.TryGetProperty("id", out var idEl) &&
                    idEl.ValueKind == JsonValueKind.String &&
                    string.Equals(idEl.GetString(), patternId, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = pat;
                        break;
                    }
                }
                
                if (matched is null)
                {
                    Console.WriteLine($"[SpecialEvent] pattern id '{patternId}' not found in summary.json");
                    return;
                }
                
                var patObj = matched.Value;
                
                // Resolve i18n
                var (i18nFolder, lang) = ReadI18nRoot(appRoot, cwd);
                var sePath = Path.Combine(i18nFolder, lang, "special_event.json");
                if (!File.Exists(sePath))
                {
                    Console.WriteLine($"[SpecialEvent] missing i18n special_event: {sePath}");
                    return;
                }
                var specialEventMap = ReadStringMap(sePath);
                
                // Determine key and display text
                string? key = GetString(patObj, "special_event_key", null);
                if (string.IsNullOrWhiteSpace(key))
                {
                    // Fallback: try to match summary.special_event value to i18n values
                    var seName = GetString(patObj, "special_event", null);
                    if (!string.IsNullOrWhiteSpace(seName))
                    {
                        foreach (var kv in specialEventMap)
                        {
                            if (string.Equals(kv.Value, seName, StringComparison.OrdinalIgnoreCase))
                            {
                                key = kv.Key;
                                break;
                            }
                        }
                    }
                }
                
                if (string.IsNullOrWhiteSpace(key))
                {
                    if (NightReign.MapGen.Program.Verbose) Console.WriteLine("[SpecialEvent] no key could be resolved; skipping banner.");
                    return;
                }
                
                if (!specialEventMap.TryGetValue(key!, out var bannerText) || string.IsNullOrWhiteSpace(bannerText))
                {
                    Console.WriteLine($"[SpecialEvent] i18n missing/blank for key={key}");
                    return;
                }
                
                // Extra boss name append
                bool isExtraNightBoss =
                string.Equals(key, "day1_extra_night_boss", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "day2_extra_night_boss", StringComparison.OrdinalIgnoreCase);
                
                if (isExtraNightBoss)
                {
                    var bossKey = GetString(patObj, "extra_boss_key", null);
                    if (!string.IsNullOrWhiteSpace(bossKey))
                    {
                        var poiPath = Path.Combine(i18nFolder, lang, "poi.json");
                        if (File.Exists(poiPath))
                        {
                            var poiMap = ReadStringMap(poiPath);
                            if (poiMap.TryGetValue(bossKey!, out var bossName) && !string.IsNullOrWhiteSpace(bossName))
                            {
                                bannerText = $"{bannerText} - {bossName}";
                            }
                        }
                    }
                }
                
                // Styles
                var (baseFont, baseSize, baseFill, baseGlow) = ReadTextStyle(appRoot, "poiBanner", cwd);
                var (bossFont, bossSize, bossFill, bossGlow) = ReadTextStyleOrFallback(appRoot, "poiBannerBoss", cwd, "poiBanner");
                
                // Resolve bottom margin from appsettings + pattern trigger
                int cfgBannerOnly = bottomMarginPx, cfgIconAndBanner = bottomMarginPx;
                if (appRoot.ValueKind == JsonValueKind.Object &&
                appRoot.TryGetProperty("SpecialEvent", out var se) && se.ValueKind == JsonValueKind.Object &&
                se.TryGetProperty("BottomMargins", out var bm) && bm.ValueKind == JsonValueKind.Object)
                {
                    if (bm.TryGetProperty("BannerOnly", out var bo) && bo.ValueKind == JsonValueKind.Number)
                    cfgBannerOnly = bo.GetInt32();
                    if (bm.TryGetProperty("IconAndBanner", out var iab) && iab.ValueKind == JsonValueKind.Number)
                    cfgIconAndBanner = iab.GetInt32();
                }
                
                bool hasTrigger = false;
                try
                {
                    var patternPath = TryResolvePatternPath(cwd, patternId);
                    if (File.Exists(patternPath))
                    {
                        using var pf = File.OpenRead(patternPath);
                        using var pdoc = JsonDocument.Parse(pf);
                        var proot = pdoc.RootElement;
                        hasTrigger = PatternContainsName(proot, "Township - Township") ||
                        PatternContainsName(proot, "Event - Scale-Bearing Merchant");
                    }
                    else
                    {
                        hasTrigger = PatternContainsName(patObj, "Township - Township") ||
                        PatternContainsName(patObj, "Event - Scale-Bearing Merchant");
                    }
                }
                catch { /* ignore */ }
                
                int effectiveBottomMargin = hasTrigger ? cfgIconAndBanner : cfgBannerOnly;
                
                // Compute baseline at bottom-center
                int centerX = ToInt32(background.Width) / 2;
                int baseY   = ToInt32(background.Height) - effectiveBottomMargin - (baseSize / 2);
                
                // Try to find an icon for this special event (best-effort)
                var icon = TryLoadSpecialEventIcon(key!, cwd);
                int iconW = 0, iconH = 0;
                if (icon is not null)
                {
                    // Scale icon to 64x64 box by default (preserve aspect)
                    const int box = 64;
                    var scale = Math.Min((double)box / icon.Width, (double)box / icon.Height);
                    iconW = (int)Math.Round(icon.Width * scale);
                    iconH = (int)Math.Round(icon.Height * scale);
                }
                
                // Draw text, with optional BOSS span styling
                // Also measure total text width to place icon to the left of the text block
                if (isExtraNightBoss)
                {
                    int idx = bannerText.IndexOf("BOSS", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        string left = bannerText.Substring(0, idx);
                        string bossToken = bannerText.Substring(idx, 4); // preserves original casing
                        string right = bannerText.Substring(idx + 4);
                        
                        double wLeft = MeasureTextWidth(ProtectColons(left), baseFont, baseSize);
                        double wBoss = MeasureTextWidth(ProtectColons(bossToken), bossFont, bossSize);
                        double wRight = MeasureTextWidth(ProtectColons(right), baseFont, baseSize);
                        double total = wLeft + wBoss + wRight;
                        
                        // If we have an icon, put it to the left with a gap
                        int gap = icon != null ? 10 : 0;
                        int blockWidth = (int)Math.Round(total) + (icon != null ? (iconW + gap) : 0);
                        
                        double leftX = centerX - (blockWidth / 2.0);
                        
                        // Composite icon if present
                        if (icon != null)
                        {
                            int iconX = (int)Math.Round(leftX + (iconW / 2.0));
                            // align icon vertical center to text baseline minus little offset
                            int iconY = baseY - (baseSize / 3);
                            CompositeCenter(background, icon, iconX, iconY);
                            leftX += iconW + gap;
                        }
                        
                        // LEFT
                        if (!string.IsNullOrEmpty(left))
                        {
                            int leftCenter = (int)(leftX + (wLeft / 2.0));
                            using (UseCanvasFont(background, baseFont))
                            {
                                TextRenderer.DrawMultilineWithGlow(
                                background,
                                ProtectColons(left),
                                baseFont, baseSize,
                                leftCenter, baseY,
                                baseGlow, baseFill,
                                null, 0.0, 4);
                            }
                            leftX += wLeft;
                        }
                        
                        // BOSS (alternate style enforced)
                        {
                            int bossCenter = (int)(leftX + (wBoss / 2.0));
                            using (UseCanvasFont(background, bossFont))
                            {
                                TextRenderer.DrawMultilineWithGlow(
                                background,
                                ProtectColons(bossToken),
                                bossFont, bossSize,
                                bossCenter, baseY,
                                bossGlow, bossFill,
                                null, 0.0, 4);
                            }
                            leftX += wBoss;
                        }
                        
                        // RIGHT
                        if (!string.IsNullOrEmpty(right))
                        {
                            int rightCenter = (int)(leftX + (wRight / 2.0));
                            using (UseCanvasFont(background, baseFont))
                            {
                                TextRenderer.DrawMultilineWithGlow(
                                background,
                                ProtectColons(right),
                                baseFont, baseSize,
                                rightCenter, baseY,
                                baseGlow, baseFill,
                                null, 0.0, 4);
                            }
                        }
                        
                        LogSpecialEventDrawnOnce(patternId, key!, icon!=null);
                        icon?.Dispose(); return;
                    }
                }
                
                // Default single-span render (with optional icon to the left)
                double wText = MeasureTextWidth(ProtectColons(bannerText), baseFont, baseSize);
                int gapDefault = icon != null ? 10 : 0;
                int totalWidth = (int)Math.Round(wText) + (icon != null ? (iconW + gapDefault) : 0);
                double textLeftX = centerX - (totalWidth / 2.0);
                if (icon != null)
                {
                    int iconX = (int)Math.Round(textLeftX + (iconW / 2.0));
                    int iconY = baseY - (baseSize / 3);
                    CompositeCenter(background, icon, iconX, iconY);
                    textLeftX += iconW + gapDefault;
                }
                
                int textCenter = (int)(textLeftX + (wText / 2.0));
                using (UseCanvasFont(background, baseFont))
                {
                    TextRenderer.DrawMultilineWithGlow(
                    background,
                    ProtectColons(bannerText),
                    baseFont, baseSize,
                    textCenter, baseY,
                    baseGlow, baseFill,
                    null, 0.0, 4);
                }
                
                LogSpecialEventDrawnOnce(patternId, key!, icon!=null);
                icon?.Dispose(); return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpecialEvent] skipped: {ex.Message}");
            }
        }
        
        // ---- Icon helpers --------------------------------------------------
        
        private static MagickImage? TryLoadSpecialEventIcon(string key, string cwd)
        {
            // Try several likely folders and file names
            var candidates = new List<string>();
            void add(string rel) { candidates.Add(ResolvePath(rel, cwd)); }
            
            // Common guesses
            add($"../assets/icon/special_event/{key}.png");
            add($"../assets/icon/special_event/{key}.webp");
            add($"../assets/icon/event/{key}.png");
            add($"../assets/icon/event/{key}.webp");
            
            // Generic icon for extra night boss
            if (key.Contains("night_boss", StringComparison.OrdinalIgnoreCase))
            {
                add("../assets/icon/special_event/boss.png");
                add("../assets/icon/event/boss.png");
            }
            
            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    try { return new MagickImage(path); }
                    catch { /* next */ }
                }
            }
            return null;
        }
        
        private static void CompositeCenter(MagickImage canvas, MagickImage icon, int centerX, int centerY)
        {
            int x = centerX - (int)Math.Round(icon.Width / 2.0);
            int y = centerY - (int)Math.Round(icon.Height / 2.0);
            canvas.Composite(icon, x, y, CompositeOperator.SrcOver);
        }
        
        // ---- JSON / style helpers -----------------------------------------
        
        private static JsonElement ReadJson(string path)
        {
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            return doc.RootElement.Clone();
        }
        
        private static (string i18nFolder, string lang) ReadI18nRoot(JsonElement appRoot, string cwd)
        {
            string i18nFolder = GetString(appRoot, "I18nFolder", "i18n")!;
            string lang = GetString(appRoot, "I18nLang", "en")!;
            if (!Path.IsPathRooted(i18nFolder))
            i18nFolder = Path.Combine(cwd, i18nFolder);
            return (i18nFolder, lang);
        }
        
        private static string? GetString(JsonElement obj, string prop, string? def)
        {
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString();
            return def;
        }
        
        private static (string fontPath, int fontSize, MagickColor fill, TextRenderer.GlowStyle? glow)
        ReadTextStyle(JsonElement root, string styleName, string cwd)
        {
            // Defaults
            string fontPath = GetString(root.GetProperty("Text"), "FontPath", "") ?? "";
            int fontSize = 28;
            var fill = new MagickColor("#FFFFFFFF");
            TextRenderer.GlowStyle? glow = null;
            
            if (root.TryGetProperty("Text", out var textEl) && textEl.ValueKind == JsonValueKind.Object)
            {
                if (textEl.TryGetProperty("Styles", out var styles) &&
                styles.ValueKind == JsonValueKind.Object &&
                styles.TryGetProperty(styleName, out var style) &&
                style.ValueKind == JsonValueKind.Object)
                {
                    if (style.TryGetProperty("FontPath", out var sf) && sf.ValueKind == JsonValueKind.String)
                    fontPath = sf.GetString() ?? fontPath;
                    if (style.TryGetProperty("FontSizePx", out var fs) && fs.ValueKind == JsonValueKind.Number)
                    fontSize = fs.GetInt32();
                    if (style.TryGetProperty("Fill", out var fillEl) && fillEl.ValueKind == JsonValueKind.String)
                    fill = new MagickColor(fillEl.GetString());
                    if (style.TryGetProperty("Glow", out var glowEl) && glowEl.ValueKind == JsonValueKind.Object)
                    {
                        string colorHex = glowEl.TryGetProperty("Color", out var col) && col.ValueKind == JsonValueKind.String
                        ? col.GetString()! : "#000000FF";
                        int opacity = glowEl.TryGetProperty("OpacityPercent", out var op) && op.ValueKind == JsonValueKind.Number
                        ? op.GetInt32() : 100;
                        int widen = glowEl.TryGetProperty("WideningRadius", out var wr) && wr.ValueKind == JsonValueKind.Number ? wr.GetInt32() : 2;
                        double blur = glowEl.TryGetProperty("BlurRadius", out var br) && br.ValueKind == JsonValueKind.Number ? br.GetDouble() : 2.0;
                        int offX = glowEl.TryGetProperty("OffsetX", out var ox) && ox.ValueKind == JsonValueKind.Number ? ox.GetInt32() : 0;
                        int offY = glowEl.TryGetProperty("OffsetY", out var oy) && oy.ValueKind == JsonValueKind.Number ? oy.GetInt32() : 0;
                        glow = new TextRenderer.GlowStyle
                        {
                            OffsetX = offX,
                            OffsetY = offY,
                            WideningRadius = widen,
                            BlurRadius = blur,
                            Color = new MagickColor(colorHex),
                            OpacityPercent = opacity
                        };
                    }
                }
            }
            
            // Resolve and fallback font to NotoSans if missing
            fontPath = EnsureFontOrFallback(ResolveFontPath(fontPath, cwd), cwd);
            return (fontPath, fontSize, fill, glow);
        }
        
        private static (string fontPath, int fontSize, MagickColor fill, TextRenderer.GlowStyle? glow)
        ReadTextStyleOrFallback(JsonElement root, string styleName, string cwd, string fallbackStyleName)
        {
            var res = ReadTextStyle(root, fallbackStyleName, cwd);
            if (root.TryGetProperty("Text", out var textEl) && textEl.ValueKind == JsonValueKind.Object)
            {
                if (textEl.TryGetProperty("Styles", out var styles) &&
                styles.ValueKind == JsonValueKind.Object &&
                styles.TryGetProperty(styleName, out var style) &&
                style.ValueKind == JsonValueKind.Object)
                {
                    if (style.TryGetProperty("FontPath", out var sf) && sf.ValueKind == JsonValueKind.String)
                    {
                        var fp = sf.GetString();
                        if (!string.IsNullOrWhiteSpace(fp))
                        res.fontPath = EnsureFontOrFallback(ResolveFontPath(fp, cwd), cwd);
                    }
                    if (style.TryGetProperty("FontSizePx", out var fs) && fs.ValueKind == JsonValueKind.Number)
                    res.fontSize = fs.GetInt32();
                    if (style.TryGetProperty("Fill", out var fillEl) && fillEl.ValueKind == JsonValueKind.String)
                    res.fill = new MagickColor(fillEl.GetString());
                    if (style.TryGetProperty("Glow", out var glowEl) && glowEl.ValueKind == JsonValueKind.Object)
                    {
                        string colorHex = glowEl.TryGetProperty("Color", out var col) && col.ValueKind == JsonValueKind.String
                        ? col.GetString()! : "#000000FF";
                        int opacity = glowEl.TryGetProperty("OpacityPercent", out var op) && op.ValueKind == JsonValueKind.Number
                        ? op.GetInt32() : 100;
                        int widen = glowEl.TryGetProperty("WideningRadius", out var wr) && wr.ValueKind == JsonValueKind.Number ? wr.GetInt32() : 2;
                        double blur = glowEl.TryGetProperty("BlurRadius", out var br) && br.ValueKind == JsonValueKind.Number ? br.GetDouble() : 2.0;
                        int offX = glowEl.TryGetProperty("OffsetX", out var ox) && ox.ValueKind == JsonValueKind.Number ? ox.GetInt32() : 0;
                        int offY = glowEl.TryGetProperty("OffsetY", out var oy) && oy.ValueKind == JsonValueKind.Number ? oy.GetInt32() : 0;
                        res.glow = new TextRenderer.GlowStyle
                        {
                            OffsetX = offX,
                            OffsetY = offY,
                            WideningRadius = widen,
                            BlurRadius = blur,
                            Color = new MagickColor(colorHex),
                            OpacityPercent = opacity
                        };
                    }
                }
            }
            res.fontPath = EnsureFontOrFallback(ResolveFontPath(res.fontPath, cwd), cwd);
            return res;
        }
        
        private const string FallbackFontName = "NotoSans-Regular.ttf";
        private static string ResolveFontPath(string? path, string cwd)
        {
            if (string.IsNullOrWhiteSpace(path))
            return Path.Combine(cwd, FallbackFontName);
            return Path.IsPathRooted(path!) ? path! : Path.Combine(cwd, path!);
        }
        
        private static string EnsureFontOrFallback(string resolvedPath, string cwd)
        {
            try
            {
                bool logged = false;
                if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                return resolvedPath;
            }
            catch { }
            var try1 = Path.Combine(cwd, FallbackFontName);
            if (File.Exists(try1)) return try1;
            return FallbackFontName; // let fontconfig find it
        }
        
        private sealed class DisposableAction : IDisposable
        {
            private readonly Action _onDispose;
            private bool _done;
            public DisposableAction(Action onDispose) { _onDispose = onDispose ?? (() => { }); }
            public void Dispose() { if (_done) return; _done = true; _onDispose(); }
        }
        
        private static IDisposable UseCanvasFont(MagickImage img, string fontPath)
        {
            var prev = img.Settings.Font;
            img.Settings.Font = fontPath;
            return new DisposableAction(() => img.Settings.Font = prev);
        }
        
        private static string ProtectColons(string text)
        {
            // Insert a zero-width word-joiner after ':' so naive split(':') won't drop it.
            return string.IsNullOrEmpty(text) ? text : text.Replace(":", ":\u2060");
        }
        
        private static double MeasureTextWidth(string text, string fontPath, int fontSize)
        {
            using var tmp = new MagickImage(MagickColors.Transparent, 1, 1);
            tmp.Settings.TextEncoding = Encoding.UTF8;
            tmp.Settings.Font = fontPath;
            tmp.Settings.FontPointsize = fontSize;
            var metrics = tmp.FontTypeMetrics(text, false);
            return metrics?.TextWidth ?? 0.0;
        }
        
        private static int ToInt32(long v)
        {
            if (v > int.MaxValue) return int.MaxValue;
            if (v < int.MinValue) return int.MinValue;
            return (int)v;
        }
        
        private static string ResolvePath(string relativeOrAbsolute, string cwd)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolute)) return cwd;
            return Path.IsPathRooted(relativeOrAbsolute) ? relativeOrAbsolute : Path.Combine(cwd, relativeOrAbsolute);
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
            foreach (var pth in candidates)
            {
                try { if (File.Exists(pth)) return pth; } catch {}
            }
            return candidates[0];
        }
        private static Dictionary<string, string> ReadStringMap(string path)
        {
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(v))
                dict[p.Name] = v!;
            }
            return dict;
        }
        private static bool PatternContainsName(JsonElement el, string target)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    foreach (var prop in el.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "name", StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind == JsonValueKind.String &&
                        string.Equals(prop.Value.GetString(), target, StringComparison.Ordinal))
                        {
                            return true;
                        }
                        if (PatternContainsName(prop.Value, target))
                        return true;
                    }
                    return false;
                }
                case JsonValueKind.Array:
                {
                    foreach (var item in el.EnumerateArray())
                    {
                        if (PatternContainsName(item, target))
                        return true;
                    }
                    return false;
                }
                default:
                return false;
            }
        }
        
        // ---- Logging de-dup (process-wide) ---------------------------------
        private static readonly object _seLogLock = new object();
        private static readonly System.Collections.Generic.HashSet<string> _seLogged =
        new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        private static void LogSpecialEventDrawnOnce(string patternId, string key, bool hasIcon)
        {
            var token = $"{patternId}|{key}|{hasIcon}";
            lock (_seLogLock)
            {
                if (_seLogged.Contains(token)) return;
                _seLogged.Add(token);
            }
            Console.WriteLine($"[SpecialEvent] drawn key={key} (icon={hasIcon.ToString().ToLower()})");
        }
        
    }
}