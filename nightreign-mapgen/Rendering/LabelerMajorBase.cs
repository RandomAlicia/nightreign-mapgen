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
        private static readonly (string Prefix, string TypeKey)[] Types = new[]
        {
            ("Camp - ",          "Camp"),
            ("Fort - ",          "Fort"),
            ("Great_Church - ",  "Great_Church"),
            ("Ruins - ",         "Ruins"),
        };

        /// <typeparam name="T">Your POI type (from pattern_xxx.json). Provide a selector to extract (name, x, z).</typeparam>
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

            // Load config bits: Text styles, I18n, LabelOffsets.MajorBase
            var root = NightReign.MapGen.Rendering.ConfigCache.GetRoot(appsettingsPath);

            var (fontPath, fontSize, fill, glow) = StyleResolver.ReadTextStyle(root, styleName, cwd, 17);

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
                foreach (var t in Types)
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
                if (string.IsNullOrWhiteSpace(label)) { missingI18n++; continue; }

                label = label.Replace("\n", "\n").Replace("\r\n", "\n");
                label = label.Replace("\n", "\n"); // keep literal for multiline renderer
                label = label.Replace("\n", "\n");  // convert to real newlines
                label = label.Replace("\n", "\n");
                label = label.Replace("\n", "\n");

                label = label.Replace("\n", "\n");
                label = label.Replace("\n", "\n");

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

            Console.WriteLine($"[MajorBase Labels] total={{total}} drawn={{drawn}} missingI18n={{missingI18n}}");
        }

        // ---------- helpers (local, safe) ----------
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
    }
}
