using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    public static class LabelerSpecialEvent
    {
        // === Public API =====================================================
        public static void Label(
            MagickImage background,
            string patternId,
            string appsettingsPath,
            string cwd,
            int bottomMarginPx = 24)
        {
            try
            {
                // Ensure punctuation and non-ASCII render as-is (helps with ":" and other symbols)
                background.Settings.TextEncoding = Encoding.UTF8;

                // 1) Load appsettings (root for styles and i18n)
                var appRoot = ReadJson(appsettingsPath);

                // 2) Load summary.json (to get special_event_key / special_event / extra_boss_key)

                // Locate summary.json robustly
                string? configuredSummary = null;
                if (appRoot.TryGetProperty("SummaryPath", out var sPathEl) && sPathEl.ValueKind == JsonValueKind.String)
                    configuredSummary = sPathEl.GetString();

                string rootOfAppsettings = Path.GetDirectoryName(appsettingsPath) ?? cwd;
                var candidates = new[]
                {
                    configuredSummary,
                    Path.Combine(cwd, "summary.json"),
                    Path.Combine(rootOfAppsettings, "summary.json"),
                    Path.Combine(cwd, "data", "summary.json"),
                    Path.Combine(cwd, "config", "summary.json")
                };

                string? summaryPath = null;
                foreach (var cand in candidates)
                {
                    if (string.IsNullOrWhiteSpace(cand)) continue;
                    var p = Path.IsPathRooted(cand) ? cand : Path.Combine(cwd, cand);
                    if (File.Exists(p)) { summaryPath = p; break; }
                }

                if (summaryPath is null)
                {
                    Console.WriteLine($"[SpecialEvent] summary.json not found. Tried: {string.Join(" | ", candidates)} (cwd='{cwd}')");
                    return;
                }

                using var fs = File.OpenRead(summaryPath);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("patterns", out var pats) || pats.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("[SpecialEvent] invalid summary.json structure");
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

                // 3) Resolve special_event key (or fallback by value)
                string? key = null;
                if (patObj.TryGetProperty("special_event_key", out var keyEl) && keyEl.ValueKind == JsonValueKind.String)
                {
                    key = keyEl.GetString();
                    if (string.IsNullOrWhiteSpace(key))
                        Console.WriteLine($"[SpecialEvent] special_event_key empty for id={patternId}; trying fallback by value");
                }

                var (i18nFolder, lang) = ReadI18nRoot(appRoot, cwd);
                var sePath = Path.Combine(i18nFolder, lang, "special_event.json");
                if (!File.Exists(sePath))
                {
                    Console.WriteLine($"[SpecialEvent] missing i18n special_event: {sePath}");
                    return;
                }

                var specialEventMap = ReadStringMap(sePath);

                if (string.IsNullOrWhiteSpace(key))
                {
                    if (patObj.TryGetProperty("special_event", out var seNameEl) && seNameEl.ValueKind == JsonValueKind.String)
                    {
                        var seName = seNameEl.GetString();
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
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    Console.WriteLine("[SpecialEvent] no key could be resolved; skipping banner.");
                    return;
                }

                if (!specialEventMap.TryGetValue(key!, out var bannerText) || string.IsNullOrWhiteSpace(bannerText))
                {
                    Console.WriteLine($"[SpecialEvent] i18n missing/blank for key={key}");
                    return;
                }

                // 4) Special-case: append boss name from i18n/<lang>/poi.json if extra night boss
                bool isExtraNightBoss =
                    string.Equals(key, "day1_extra_night_boss", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "day2_extra_night_boss", StringComparison.OrdinalIgnoreCase);

                if (isExtraNightBoss &&
                    patObj.TryGetProperty("extra_boss_key", out var bossEl) &&
                    bossEl.ValueKind == JsonValueKind.String)
                {
                    var bossKey = bossEl.GetString();
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

                // 5) Styles
                var (baseFont, baseSize, baseFill, baseGlow) = ReadTextStyle(appRoot, "poiBanner", cwd);
                if (string.IsNullOrWhiteSpace(baseFont))
                    Console.WriteLine("[SpecialEvent] WARNING: poiBanner fontPath empty; using default.");

                // Optional alt style for 'BOSS'
                var (bossFont, bossSize, bossFill, bossGlow) = ReadTextStyleOrFallback(appRoot, "poiBannerBoss", cwd, "poiBanner");

                // 6) Common placement (compute AFTER we know baseSize)
                int centerX = (int)(background.Width / 2);
                int baseY   = (int)background.Height - bottomMarginPx - (baseSize / 2);

                // 7) Draw: if extra boss and text contains "BOSS", split into three spans with alternate style (case-insensitive)
                if (isExtraNightBoss)
                {
                    int idx = bannerText.IndexOf("BOSS", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        string left = bannerText.Substring(0, idx);
                        // Preserve the exact matched casing (e.g., "Boss", "BOSS")
                        string bossToken = bannerText.Substring(idx, 4);
                        string right = bannerText.Substring(idx + 4);

                        double wLeft = MeasureTextWidth(ProtectColons(left), baseFont, baseSize);
                        double wBoss = MeasureTextWidth(ProtectColons(bossToken), bossFont, bossSize);
                        double wRight = MeasureTextWidth(ProtectColons(right), baseFont, baseSize);
                        double total = wLeft + wBoss + wRight;
                        double leftX = centerX - (total / 2.0);

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
                        }

                        // BOSS (alternate style)
                        {
                            int bossCenter = (int)(leftX + wLeft + (wBoss / 2.0));
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
                        }

                        // RIGHT
                        if (!string.IsNullOrEmpty(right))
                        {
                            int rightCenter = (int)(leftX + wLeft + wBoss + (wRight / 2.0));
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

                        Console.WriteLine($"[SpecialEvent Banner] drawn (spans) id={patternId} key={key}");
                        return;
                    }
                }

                // 8) Default single-span draw
                TextRenderer.DrawMultilineWithGlow(
                    background,
                    ProtectColons(bannerText),
                    baseFont, baseSize,
                    centerX, baseY,
                    baseGlow, baseFill,
                    null, 0.0, 4);

                Console.WriteLine($"[SpecialEvent Banner] drawn id={patternId} key={key}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpecialEvent Banner] skipped: {ex.Message}");
            }
        }

        // === Helpers ========================================================

        private static JsonElement ReadJson(string path)
        {
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            return doc.RootElement.Clone();
        }

        private static (string i18nFolder, string lang) ReadI18nRoot(JsonElement appRoot, string cwd)
        {
            string i18nFolder = "i18n";
            string lang = "en";

            if (appRoot.TryGetProperty("I18nFolder", out var f) && f.ValueKind == JsonValueKind.String)
                i18nFolder = f.GetString() ?? i18nFolder;
            if (appRoot.TryGetProperty("I18nLang", out var l) && l.ValueKind == JsonValueKind.String)
                lang = l.GetString() ?? lang;

            // Resolve relative path from cwd
            if (!Path.IsPathRooted(i18nFolder))
                i18nFolder = Path.Combine(cwd, i18nFolder);

            return (i18nFolder, lang);
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

        private static (string fontPath, int fontSize, MagickColor fill, TextRenderer.GlowStyle? glow)
            ReadTextStyle(JsonElement root, string styleName, string cwd)
        {
            // Default
            string fontPath = "";
            int fontSize = 28;
            var fill = new MagickColor("#FFFFFFFF");
            TextRenderer.GlowStyle? glow = null;

            if (root.TryGetProperty("Text", out var textEl) && textEl.ValueKind == JsonValueKind.Object)
            {
                if (textEl.TryGetProperty("FontPath", out var fp) && fp.ValueKind == JsonValueKind.String)
                {
                    var font = fp.GetString();
                    if (!string.IsNullOrWhiteSpace(font))
                        fontPath = Path.IsPathRooted(font!) ? font! : Path.Combine(cwd, font!);
                }

                if (textEl.TryGetProperty("Styles", out var styles) &&
                    styles.ValueKind == JsonValueKind.Object &&
                    styles.TryGetProperty(styleName, out var style) &&
                    style.ValueKind == JsonValueKind.Object)
                {
                    if (style.TryGetProperty("FontPath", out var sf) && sf.ValueKind == JsonValueKind.String)
                    {
                        var fp2 = sf.GetString();
                        if (!string.IsNullOrWhiteSpace(fp2))
                            fontPath = Path.IsPathRooted(fp2!) ? fp2! : Path.Combine(cwd, fp2!);
                    }
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
                            OffsetX = offX, OffsetY = offY, WideningRadius = widen, BlurRadius = blur,
                            Color = new MagickColor(colorHex), OpacityPercent = opacity
                        };
                    }
                }
            }

            return (fontPath, fontSize, fill, glow);
        }

        private static (string fontPath, int fontSize, MagickColor fill, TextRenderer.GlowStyle? glow)
            ReadTextStyleOrFallback(JsonElement root, string styleName, string cwd, string fallbackStyleName)
        {
            var result = ReadTextStyle(root, fallbackStyleName, cwd);
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
                            result.fontPath = Path.IsPathRooted(fp!) ? fp! : Path.Combine(cwd, fp!);
                    }
                    if (style.TryGetProperty("FontSizePx", out var fs) && fs.ValueKind == JsonValueKind.Number)
                        result.fontSize = fs.GetInt32();
                    if (style.TryGetProperty("Fill", out var fillEl) && fillEl.ValueKind == JsonValueKind.String)
                        result.fill = new MagickColor(fillEl.GetString());
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
                        result.glow = new TextRenderer.GlowStyle
                        {
                            OffsetX = offX, OffsetY = offY, WideningRadius = widen, BlurRadius = blur,
                            Color = new MagickColor(colorHex), OpacityPercent = opacity
                        };
                    }
                }
            }
            return result;
        }

        private sealed class DisposableAction : IDisposable
        {
            private readonly Action _onDispose;
            private bool _done;
            public DisposableAction(Action onDispose) { _onDispose = onDispose ?? (() => { }); }
            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _onDispose();
            }
        }

        private static IDisposable UseCanvasFont(MagickImage img, string fontPath)
        {
            var prev = img.Settings.Font;
            img.Settings.Font = fontPath;
            return new DisposableAction(() => img.Settings.Font = prev);
        }

        private static string ProtectColons(string text)
        {
            // Insert a zero-width word-joiner after ':' so naive split(':') in downstream code
            // won't drop the delimiter. Visually identical.
            return string.IsNullOrEmpty(text) ? text : text.Replace(":", ":\u2060");
        }

        private static double MeasureTextWidth(string text, string fontPath, int fontSize)
        {
            using var tmp = new MagickImage(MagickColors.Transparent, 1, 1);
            tmp.Settings.Font = fontPath;
            tmp.Settings.FontPointsize = fontSize;
            // Ensure consistent encoding during measurement too
            tmp.Settings.TextEncoding = Encoding.UTF8;
            var metrics = tmp.FontTypeMetrics(text, false);
            return metrics?.TextWidth ?? 0.0;
        }
    }
}
