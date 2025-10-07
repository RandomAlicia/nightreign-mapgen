using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Renders labels for Major Base: Camp, Fort, Great_Church, Ruins.
    /// Uses poiStandard style for now. Offsets read from appsettings.json:
    /// "LabelOffsets": { "MajorBase": { "Camp": [10,0], "Fort":[0,-10], "Great_Church":[0,10], "Ruins":[-10,0] } }
    /// </summary>
    public static class LabelerMajorBase
    {
        // Type prefixes
        private static readonly (string Prefix, string TypeKey)[] _types = new[]
        {
            ("Camp - ",          "Camp"),
            ("Fort - ",          "Fort"),
            ("Great_Church - ",  "Great_Church"),
            ("Ruins - ",         "Ruins"),
        };

        /// <typeparam name="T">Your POI type (from pattern_xxx.json). Provide a selector to extract (name, x, z).</typeparam>
        /// <param name="background">canvas</param>
        /// <param name="pois">pattern POIs</param>
        /// <param name="select">selector returning (name, x, z)</param>
        /// <param name="indexLookup">index.json entries by name (values can be your IndexEntry type)</param>
        /// <param name="appsettingsPath">path to appsettings.json (for Text + LabelOffsets + I18n config)</param>
        /// <param name="cwd">current working directory, for resolving relative paths</param>
        /// <param name="mapXZtoPxPy">your mapping: (x,z) → (px,py) on the 1536×1536 canvas</param>
        /// <param name="styleName">use "poiStandard"</param>
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
            if (background is null || pois is null || select is null || mapXZtoPxPy is null)
                return;

            // Load config bits: Text styles, I18n, LabelOffsets.MajorBase
            using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
            var root = doc.RootElement;

            // Text → Styles → styleName
            var (fontPath, fontSize, fill, glow) = ReadTextStyle(root, styleName, cwd);

            // I18n config
            var i18nFolder = root.TryGetProperty("I18nFolder", out var i18nF) && i18nF.ValueKind == JsonValueKind.String
                ? i18nF.GetString()! : "../i18n";
            var i18nLang = root.TryGetProperty("I18nLang", out var i18nL) && i18nL.ValueKind == JsonValueKind.String
                ? i18nL.GetString()! : "en";

            // LabelOffsets.MajorBase
            var offsets = ReadOffsets(root);

            int total = 0, drawn = 0, missingI18n = 0;

            foreach (var poi in pois)
            {
                var (rawName, x, z) = select(poi);
                if (string.IsNullOrWhiteSpace(rawName)) continue;

                // Filter to MajorBase types
                string? typeKey = null;
                foreach (var t in _types)
                {
                    if (rawName.StartsWith(t.Prefix, StringComparison.Ordinal))
                    {
                        typeKey = t.TypeKey;
                        break;
                    }
                }
                if (typeKey == null) continue; // not a MajorBase
                total++;

                // Resolve label via i18n
                var label = LabelTextResolver.Resolve(rawName, indexLookup, i18nFolder, i18nLang, cwd);
                if (string.IsNullOrWhiteSpace(label))
                {
                    missingI18n++;
                    continue;
                }

                // Position + type-specific offset
                var (px, py) = mapXZtoPxPy(x, z);
                var (dx, dy) = offsets.TryGetValue(typeKey, out var v) ? v : (0, 0);

                // Draw
                TextRenderer.DrawTextWithGlow(
                    background,
                    text: label,
                    fontPath: fontPath,
                    fontSizePx: fontSize,
                    x: px + dx,
                    y: py + dy,
                    glow: glow,                  // null if style has no glow
                    textFill: fill,
                    textStroke: null,
                    textStrokeWidth: 0.0,
                    centerX: true,
                    centerY: true
                );
                drawn++;
            }

            Console.WriteLine($"[MajorBase Labels] total={total} drawn={drawn} missingI18n={missingI18n}");
        }

        // ---------- helpers (local, safe) ----------

        private static (string fontPath, int fontSize, MagickColor fill, TextRenderer.GlowStyle? glow)
            ReadTextStyle(JsonElement root, string styleName, string cwd)
        {
            string fontPath = Path.Combine(cwd, "../assets/font/DingLieZhuHaiTi/dingliezhuhaifont-20240831GengXinBan)-2.ttf");
            int fontSize = 17;
            MagickColor fill = MagickColors.White;
            TextRenderer.GlowStyle? glow = null;

            if (root.TryGetProperty("Text", out var textEl) && textEl.ValueKind == JsonValueKind.Object)
            {
                if (textEl.TryGetProperty("FontPath", out var f) && f.ValueKind == JsonValueKind.String)
                    fontPath = Path.Combine(cwd, f.GetString()!);

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

        private static Dictionary<string, (int dx, int dy)> ReadOffsets(JsonElement root)
        {
            var map = new Dictionary<string, (int dx, int dy)>(StringComparer.OrdinalIgnoreCase)
            {
                { "Camp", (0, 0) },
                { "Fort", (0, 0) },
                { "Great_Church", (0, 0) },
                { "Ruins", (0, 0) },
            };

            if (root.TryGetProperty("LabelOffsets", out var lo) && lo.ValueKind == JsonValueKind.Object &&
                lo.TryGetProperty("MajorBase", out var mb) && mb.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in mb.EnumerateObject())
                {
                    var name = kv.Name;
                    var val = kv.Value;

                    try
                    {
                        if (val.ValueKind == JsonValueKind.Array && val.GetArrayLength() == 2)
                        {
                            // Legacy: [dx, dy]
                            int dx = val[0].GetInt32();
                            int dy = val[1].GetInt32();
                            map[name] = (dx, dy);
                        }
                        else if (val.ValueKind == JsonValueKind.Object)
                        {
                            // Preferred: { "dx": <int>, "dy": <int> }
                            int dx = 0, dy = 0;
                            if (val.TryGetProperty("dx", out var dxEl) && dxEl.TryGetInt32(out var dxVal)) dx = dxVal;
                            if (val.TryGetProperty("dy", out var dyEl) && dyEl.TryGetInt32(out var dyVal)) dy = dyVal;
                            map[name] = (dx, dy);
                        }
                    }
                    catch
                    {
                        // ignore malformed entries, keep defaults
                    }
                }
            }

            return map;
        }

        // Strict RRGGBBAA (8-digit) parsing; 6-digit is RRGGBB with alpha=FF.
        private static MagickColor ParseHexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return MagickColors.White;
            var s = hex.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);

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
