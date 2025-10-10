using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Labels for Evergaol points.
    /// </summary>
    public static class LabelerEvergaol
    {
        private const string Prefix = "Evergaol - ";
        private const string TypeKey = "Evergaol";

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

            var root = NightReign.MapGen.Rendering.ConfigCache.GetRoot(appsettingsPath);

            var (fontPath, fontSize, fill, glow) = StyleResolver.ReadTextStyle(root, styleName, cwd, 17);

            var i18nFolder = root.TryGetProperty("I18nFolder", out var i18nF) && i18nF.ValueKind == JsonValueKind.String
                ? i18nF.GetString()! : "../i18n";
            var i18nLang = root.TryGetProperty("I18nLang", out var i18nL) && i18nL.ValueKind == JsonValueKind.String
                ? i18nL.GetString()! : "en";

            var offsets = ReadOffsets(root);

            int total = 0, drawn = 0, missingI18n = 0;

            foreach (var poi in pois)
            {
                var (rawName, x, z) = select(poi);
                if (string.IsNullOrWhiteSpace(rawName)) continue;
                if (!rawName.StartsWith(Prefix, StringComparison.Ordinal)) continue;

                total++;

                var label = LabelTextResolver.Resolve(rawName, indexLookup, i18nFolder, i18nLang, cwd);
                if (string.IsNullOrWhiteSpace(label)) { missingI18n++; continue; }

                // normalize literal "\n" to real newlines
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
                        textStroke: null,
                        textStrokeWidth: 0.0,
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
                        textStroke: null,
                        textStrokeWidth: 0.0,
                        centerX: true,
                        centerY: true
                    );
                }
                drawn++;
            }

            Console.WriteLine($"[Evergaol Labels] total={total} drawn={drawn} missingI18n={missingI18n}");
        }

        // ---------- helpers ----------
        private static Dictionary<string, (int dx, int dy)> ReadOffsets(JsonElement root)
        {
            var map = new Dictionary<string, (int dx, int dy)>(StringComparer.OrdinalIgnoreCase)
            {
                { TypeKey, (0, 0) }
            };

            if (root.TryGetProperty("LabelOffsets", out var lo) && lo.ValueKind == JsonValueKind.Object &&
                lo.TryGetProperty("Evergaol", out var ok) && ok.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in ok.EnumerateObject())
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
    }
}
