using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    public static class LabelerNightBoss
    {
        private const string PFX_NIGHT_UNDERSCORE = "Night_Boss";
        private const string PFX_NIGHT_SPACE      = "Night Boss";

        public static void Label<T>(
            MagickImage background,
            IEnumerable<T> pois,
            Func<T, (string name, double x, double z)> select,
            IDictionary<string, object> indexLookup,
            string appsettingsPath,
            string cwd,
            Func<double, double, (int px, int py)> mapXZtoPxPy,
            string defaultStyle = "poiNightBoss"
        )
        {
            if (background is null || pois is null || select is null || mapXZtoPxPy is null) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
            var root = doc.RootElement;

            var i18nFolder = root.TryGetProperty("I18nFolder", out var i18nF) && i18nF.ValueKind == JsonValueKind.String
                ? i18nF.GetString()! : "../i18n";
            var i18nLang = root.TryGetProperty("I18nLang", out var i18nL) && i18nL.ValueKind == JsonValueKind.String
                ? i18nL.GetString()! : "en";

            int total = 0, drawn = 0, missingI18n = 0;

            foreach (var poi in pois)
            {
                var (rawName, x0, z0) = select(poi);
                if (string.IsNullOrWhiteSpace(rawName)) continue;

                // Accept only Night bosses
                bool isNight = rawName.StartsWith(PFX_NIGHT_UNDERSCORE, StringComparison.Ordinal)
                               || rawName.StartsWith(PFX_NIGHT_SPACE, StringComparison.Ordinal);
                if (!isNight) continue;

                total++;

                var label = LabelTextResolver.Resolve(rawName, indexLookup, i18nFolder, i18nLang, cwd);
                if (string.IsNullOrWhiteSpace(label)) { missingI18n++; continue; }
                label = label.Replace("\n", "\n").Replace("\r\n", "\n");
                label = label.Replace("\n", "\n");
                label = label.Replace("\n", "\n");

                // Split into lines; first line might be "Day 1" or "Day 2" from raw name indicator
                string? dayPrefix = null;
                if (rawName.Contains("(Day 1)", StringComparison.Ordinal)) dayPrefix = "Day 1";
                else if (rawName.Contains("(Day 2)", StringComparison.Ordinal)) dayPrefix = "Day 2";

                // Style for the main content
                var (fontPath, fontSize, fill, glow) = StyleResolver.ReadTextStyle(root, defaultStyle, cwd, 17);

                // Optional different font for the Day prefix:
                var (dayFontPath, dayFontSize) = ReadDayPrefixFont(root, cwd, defaultStyle, fontPath, fontSize);

                // Position
                var (px, py) = mapXZtoPxPy(x0, z0);
                int cx = px;
                int cy = py;

                const int lineSpacing = 4;

                if (dayPrefix != null)
                {
                    // Mixed-font rendering: measure and draw the prefix line with its own font,
                    // then draw the content lines with the main font, all centered on (cx,cy).
                    var contentLines = label.Split('\n');

                    using var meas = new MagickImage(MagickColors.Transparent, 1, 1);

                    // Measure prefix height
                    meas.Settings.Font = dayFontPath;
                    meas.Settings.FontPointsize = dayFontSize;
                    var mDay = meas.FontTypeMetrics(dayPrefix);
                    int hDay = (int)Math.Ceiling(mDay.TextHeight);

                    // Measure content lines height (sum + spacing) with main font
                    meas.Settings.Font = fontPath;
                    meas.Settings.FontPointsize = fontSize;
                    int[] hLines = new int[contentLines.Length];
                    int totalContentH = 0;
                    for (int i = 0; i < contentLines.Length; i++)
                    {
                        var m = meas.FontTypeMetrics(contentLines[i]);
                        int h = (int)Math.Ceiling(m.TextHeight);
                        hLines[i] = h;
                        totalContentH += h;
                        if (i < contentLines.Length - 1) totalContentH += lineSpacing;
                    }

                    int totalH = hDay + (contentLines.Length > 0 ? lineSpacing : 0) + totalContentH;
                    int top = cy - totalH / 2;

                    // Draw prefix centered at (cx, yCenterOfPrefix)
                    int yDayCenter = top + hDay / 2;
                    TextRenderer.DrawTextWithGlow(
                        background,
                        text: dayPrefix,
                        fontPath: dayFontPath,
                        fontSizePx: dayFontSize,
                        x: cx,
                        y: yDayCenter,
                        glow: glow,
                        textFill: fill,
                        textStroke: null,
                        textStrokeWidth: 0.0,
                        centerX: true,
                        centerY: true
                    );

                    // Draw content lines below
                    int curTop = top + hDay + lineSpacing;
                    for (int i = 0; i < contentLines.Length; i++)
                    {
                        int h = hLines[i];
                        int yCenter = curTop + h / 2;
                        TextRenderer.DrawTextWithGlow(
                            background,
                            text: contentLines[i],
                            fontPath: fontPath,
                            fontSizePx: fontSize,
                            x: cx,
                            y: yCenter,
                            glow: glow,
                            textFill: fill,
                            textStroke: null,
                            textStrokeWidth: 0.0,
                            centerX: true,
                            centerY: true
                        );
                        curTop += h + (i < contentLines.Length - 1 ? lineSpacing : 0);
                    }
                }
                else
                {
                    // No day prefix: default multiline logic
                    if (label.IndexOf('\n') >= 0) label = label.Replace("\n", "\n");
                    if (label.IndexOf('\n') >= 0)
                    {
                        TextRenderer.DrawMultilineWithGlow(
                            background,
                            text: label,
                            fontPath: fontPath,
                            fontSizePx: fontSize,
                            centerX: cx,
                            centerY: cy,
                            glow: glow ?? new TextRenderer.GlowStyle(),
                            textFill: fill,
                            textStroke: null,
                            textStrokeWidth: 0.0,
                            lineSpacingPx: lineSpacing
                        );
                    }
                    else
                    {
                        TextRenderer.DrawTextWithGlow(
                            background,
                            text: label,
                            fontPath: fontPath,
                            fontSizePx: fontSize,
                            x: cx,
                            y: cy,
                            glow: glow,
                            textFill: fill,
                            textStroke: null,
                            textStrokeWidth: 0.0,
                            centerX: true,
                            centerY: true
                        );
                    }
                }

                drawn++;
            }

            Console.WriteLine($"[NightBoss Labels] total={{total}} drawn={{drawn}} missingI18n={{missingI18n}}");
        }

        private static (string fontPath, int fontSize) ReadDayPrefixFont(
            JsonElement root, string cwd, string baseStyleName, string fallbackFontPath, int fallbackFontSize)
        {
            // Prefer a dedicated style "poiNightBossDayPrefix"; fallback to main style's font
            const string styleName = "poiNightBossDayPrefix";
            if (root.TryGetProperty("Text", out var textEl) && textEl.ValueKind == JsonValueKind.Object)
            {
                if (textEl.TryGetProperty("Styles", out var styles) && styles.ValueKind == JsonValueKind.Object)
                {
                    if (styles.TryGetProperty(styleName, out var dayStyle) && dayStyle.ValueKind == JsonValueKind.Object)
                    {
                        string fontPath = fallbackFontPath;
                        int fontSize = fallbackFontSize;
                        if (dayStyle.TryGetProperty("FontPath", out var fp) && fp.ValueKind == JsonValueKind.String)
                        {
                            var path = fp.GetString()!;
                            fontPath = Path.IsPathRooted(path) ? path : Path.Combine(cwd, path);
                        }
                        if (dayStyle.TryGetProperty("FontSizePx", out var fs) && fs.ValueKind == JsonValueKind.Number)
                        {
                            fontSize = fs.GetInt32();
                        }
                        return (fontPath, fontSize);
                    }
                }
            }
            return (fallbackFontPath, fallbackFontSize);
        }
    }
}
