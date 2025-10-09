using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Labels for MinorBase items. For now we only handle "Sorcerers_Rise - " entries
    /// and render them using the "poiStandard" text style.
    /// Offsets are configurable in appsettings.json under:
    /// "LabelOffsets": { "MinorBase": { "Sorcerers_Rise": { "dx": 0, "dy": 0 } } }
    /// Stroke/outline is intentionally not used here (always off).
    /// </summary>
    public static class LabelerMinorBase
    {
        private const string Prefix = "Sorcerers_Rise - ";
        private const string TypeKey = "Sorcerers_Rise";
        
        /// <typeparam name="T">POI type from pattern_xxx.json. Provide a selector to extract (name, x, z).</typeparam>
        public static void Label<T>(
        MagickImage background,
        IEnumerable<T> pois,
        Func<T, (string name, double x, double z)> select,
        IDictionary<string, object> indexLookup,
        string appsettingsPath,
        string cwd,
        Func<double, double, (int px, int py)> mapXZtoPxPy,
        string styleName = "poiStandard"
        )
        {
            if (background is null || pois is null || select is null || mapXZtoPxPy is null) return;
            
            using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
            var root = doc.RootElement;
            
            // Text style (no stroke here)
            var (fontPath, fontSize, fill, glow) = ReadTextStyle(root, styleName, cwd);
            
            // I18n config
            var i18nFolder = root.TryGetProperty("I18nFolder", out var i18nF) && i18nF.ValueKind == JsonValueKind.String
            ? i18nF.GetString()! : "../i18n";
            var i18nLang = root.TryGetProperty("I18nLang", out var i18nL) && i18nL.ValueKind == JsonValueKind.String
            ? i18nL.GetString()! : "en";
            
            // Offsets
            var offsets = ReadOffsets(root);
            
            int total = 0, drawn = 0, missingI18n = 0;
            
            foreach (var poi in pois)
            {
                var (rawName, x, z) = select(poi);
                if (string.IsNullOrWhiteSpace(rawName)) continue;
                if (!rawName.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                
                total++;
                
                var label = LabelTextResolver.Resolve(rawName, indexLookup, i18nFolder, i18nLang, cwd);
                if (string.IsNullOrWhiteSpace(label))
                {
                    missingI18n++;
                    continue;
                }
                // normalize literal "\n"
                label = label.Replace("\\n", "\n");
                
                var (px, py) = mapXZtoPxPy(x, z);
                var (dx, dy) = offsets.TryGetValue(TypeKey, out var v) ? v : (0, 0);
                int cx = px + dx;
                int cy = py + dy;
                
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
                    textStroke: null,         // no stroke
                    textStrokeWidth: 0.0,     // no stroke
                    lineSpacingPx: 4
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
                    textStroke: null,         // no stroke
                    textStrokeWidth: 0.0,     // no stroke
                    centerX: true,
                    centerY: true
                    );
                }
                drawn++;
            }
            
            Console.WriteLine($"[MinorBase Labels] total={total} drawn={drawn} missingI18n={missingI18n}");
        }
        
        // ---------- helpers ----------
        
        private static (string fontPath, int fontSize, MagickColor fill, TextRenderer.GlowStyle? glow)
        ReadTextStyle(JsonElement root, string styleName, string cwd)
        {
            string fontPath = GetFontPathFromConfig(root, cwd);
            int fontSize = 17;
            MagickColor fill = MagickColors.White;
            TextRenderer.GlowStyle? glow = null;
            
            if (root.TryGetProperty("Text", out var textEl) && textEl.ValueKind == JsonValueKind.Object)
            {
                if (textEl.TryGetProperty("Styles", out var styles) &&
                styles.ValueKind == JsonValueKind.Object &&
                styles.TryGetProperty(styleName, out var style) &&
                style.ValueKind == JsonValueKind.Object)
                {
                    if (style.TryGetProperty("FontSizePx", out var fs) && fs.ValueKind == JsonValueKind.Number)
                    fontSize = fs.GetInt32();
                    
                    if (style.TryGetProperty("Fill", out var fillEl) && fillEl.ValueKind == JsonValueKind.String)
                    fill = ParseHexColor(fillEl.GetString()!);
                    
                    if (style.TryGetProperty("Glow", out var glowEl) && glowEl.ValueKind == JsonValueKind.Object)
                    {
                        string colorHex = glowEl.TryGetProperty("Color", out var col) && col.ValueKind == JsonValueKind.String
                        ? col.GetString()! : "#000000FF";
                        int opacity = glowEl.TryGetProperty("OpacityPercent", out var op) && op.ValueKind == JsonValueKind.Number
                        ? op.GetInt32() : 100;
                        int widen = glowEl.TryGetProperty("WideningRadius", out var wr) && wr.ValueKind == JsonValueKind.Number ? wr.GetInt32() : 3;
                        double blur = glowEl.TryGetProperty("BlurRadius", out var br) && br.ValueKind == JsonValueKind.Number ? br.GetDouble() : 5.0;
                        int offX = glowEl.TryGetProperty("OffsetX", out var ox) && ox.ValueKind == JsonValueKind.Number ? ox.GetInt32() : 0;
                        int offY = glowEl.TryGetProperty("OffsetY", out var oy) && oy.ValueKind == JsonValueKind.Number ? oy.GetInt32() : 0;
                        
                        glow = new TextRenderer.GlowStyle
                        {
                            OffsetX = offX,
                            OffsetY = offY,
                            WideningRadius = widen,
                            BlurRadius = blur,
                            Color = ParseHexColor(colorHex),
                            OpacityPercent = opacity
                        };
                    }
                }
            }
            
            return (fontPath, fontSize, fill, glow);
        }
        
        private static string GetFontPathFromConfig(JsonElement root, string cwd)
        {
            if (root.TryGetProperty("Text", out var textEl) &&
            textEl.ValueKind == JsonValueKind.Object &&
            textEl.TryGetProperty("FontPath", out var fontEl) &&
            fontEl.ValueKind == JsonValueKind.String)
            {
                var path = fontEl.GetString()!;
                return Path.IsPathRooted(path) ? path : Path.Combine(cwd, path);
            }
            throw new InvalidOperationException("appsettings.json missing Text.FontPath");
        }
        
        private static Dictionary<string, (int dx, int dy)> ReadOffsets(JsonElement root)
        {
            var map = new Dictionary<string, (int dx, int dy)>(StringComparer.OrdinalIgnoreCase)
            {
                { TypeKey, (0, 0) }
            };
            
            if (root.TryGetProperty("LabelOffsets", out var lo) && lo.ValueKind == JsonValueKind.Object &&
            lo.TryGetProperty("MinorBase", out var mb) && mb.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in mb.EnumerateObject())
                {
                    var name = kv.Name;
                    var val = kv.Value;
                    try
                    {
                        if (val.ValueKind == JsonValueKind.Array && val.GetArrayLength() == 2)
                        {
                            map[name] = (val[0].GetInt32(), val[1].GetInt32());
                        }
                        else if (val.ValueKind == JsonValueKind.Object)
                        {
                            int dx = 0, dy = 0;
                            if (val.TryGetProperty("dx", out var dxEl) && dxEl.TryGetInt32(out var dxVal)) dx = dxVal;
                            if (val.TryGetProperty("dy", out var dyEl) && dyEl.TryGetInt32(out var dyVal)) dy = dyVal;
                            map[name] = (dx, dy);
                        }
                    }
                    catch { /* ignore malformed */ }
                }
            }
            
            return map;
        }
        
        private static MagickColor ParseHexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return MagickColors.White;
            var s = hex.Trim();
            if (s.StartsWith("#")) s = s[1..];
            
            byte r = 255, g = 255, b = 255, a = 255;
            if (s.Length == 6)
            {
                r = Convert.ToByte(s.Substring(0, 2), 16);
                g = Convert.ToByte(s.Substring(2, 2), 16);
                b = Convert.ToByte(s.Substring(4, 2), 16);
            }
            else if (s.Length == 8)
            {
                r = Convert.ToByte(s.Substring(0, 2), 16);
                g = Convert.ToByte(s.Substring(2, 2), 16);
                b = Convert.ToByte(s.Substring(4, 2), 16);
                a = Convert.ToByte(s.Substring(6, 2), 16);
            }
            return new MagickColor(r, g, b, a);
        }
    }
}