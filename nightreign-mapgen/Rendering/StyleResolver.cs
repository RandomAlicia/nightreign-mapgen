using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Resolved text style that the renderers consume.
    /// Supports tuple-like deconstruction into (fontPath, fontSizePx, fill, glow).
    /// </summary>
    public sealed class ResolvedTextStyle
    {
        public string FontPath { get; set; } = "";
        public int FontSizePx { get; set; } = 24;
        public MagickColor Fill { get; set; } = new MagickColor("white");
        public TextRenderer.GlowStyle? Glow { get; set; } = null;

        public void Deconstruct(out string fontPath, out int fontSizePx, out MagickColor fill, out TextRenderer.GlowStyle? glow)
        {
            fontPath = FontPath;
            fontSizePx = FontSizePx;
            fill = Fill;
            glow = Glow;
        }
    }

    public static class StyleResolver
    {
        // ---------------- Public API (back-compat) ----------------

        /// <summary>
        /// Read a style by name (e.g., "poiStandard") from config, merging with Text-level defaults.
        /// </summary>
        public static ResolvedTextStyle ReadTextStyle(object config, string styleName, string? fallbackStyleName = null)
        {
            return ReadTextStyle(config, styleName, fallbackStyleName is null ? Array.Empty<string?>() : new[] { fallbackStyleName });
        }

        /// <summary>
        /// Back-compat overload: accepts multiple fallback style names.
        /// This satisfies earlier call-sites that passed four arguments (all strings).
        /// </summary>
        public static ResolvedTextStyle ReadTextStyle(object config, string styleName, params string?[] fallbackStyleNames)
        {
            var root = ToJsonElement(config);
            if (root.ValueKind == JsonValueKind.Undefined || root.ValueKind == JsonValueKind.Null)
                return new ResolvedTextStyle();

            var textRoot = GetObject(root, "Text");
            var styles = GetObject(textRoot, "Styles");

            // Build merge result starting from Text defaults
            var result = new ResolvedTextStyle
            {
                FontPath = GetString(textRoot, "FontPath") ?? "",
                FontSizePx = GetInt(textRoot, "FontSizePx") ?? 24,
                Fill = TryParseColor(GetString(textRoot, "Fill"), defaultWhite: true),
                Glow = TryParseGlow(GetObject(textRoot, "Glow"))
            };

            // Try the requested style first, then fallbacks, finally poiStandard if still not found.
            if (!TryMergeStyle(styles, styleName, ref result))
            {
                bool merged = false;
                if (fallbackStyleNames != null)
                {
                    foreach (var fb in fallbackStyleNames)
                    {
                        if (string.IsNullOrWhiteSpace(fb)) continue;
                        if (TryMergeStyle(styles, fb!, ref result)) { merged = true; break; }
                    }
                }
                if (!merged)
                {
                    TryMergeStyle(styles, "poiStandard", ref result);
                }
            }

            return result;
        }

        /// <summary>
        /// Back-compat overload used by older labelers:
        /// (config, styleName, fallbackStyleName, defaultFontSizePx)
        /// The provided defaultFontSizePx is applied only if the chosen style does not specify FontSizePx.
        /// </summary>
        public static ResolvedTextStyle ReadTextStyle(object config, string styleName, string? fallbackStyleName, int defaultFontSizePx)
        {
            var root = ToJsonElement(config);
            if (root.ValueKind == JsonValueKind.Undefined || root.ValueKind == JsonValueKind.Null)
                return new ResolvedTextStyle { FontSizePx = defaultFontSizePx > 0 ? defaultFontSizePx : 24 };

            var textRoot = GetObject(root, "Text");
            var styles = GetObject(textRoot, "Styles");

            var result = new ResolvedTextStyle
            {
                FontPath = GetString(textRoot, "FontPath") ?? "",
                FontSizePx = GetInt(textRoot, "FontSizePx") ?? 24,
                Fill = TryParseColor(GetString(textRoot, "Fill"), defaultWhite: true),
                Glow = TryParseGlow(GetObject(textRoot, "Glow"))
            };

            bool fontExplicit = false;
            if (TryMergeStyle(styles, styleName, ref result, ref fontExplicit) == false && !string.IsNullOrWhiteSpace(fallbackStyleName))
            {
                TryMergeStyle(styles, fallbackStyleName!, ref result, ref fontExplicit);
            }

            // If neither style provided FontSizePx, honor the explicit default provided by caller
            if (!fontExplicit && defaultFontSizePx > 0)
            {
                result.FontSizePx = defaultFontSizePx;
            }

            // Still ensure there is some style applied
            if (string.IsNullOrWhiteSpace(result.FontPath))
            {
                // Try poiStandard as a final merge (without changing fontExplicit)
                TryMergeStyle(styles, "poiStandard", ref result);
            }

            return result;
        }

        /// <summary>
        /// Resolve style via LabelStyles mapping (e.g., category="FieldBoss", subtype="Castle").
        /// Falls back to provided fallbacks and then to "poiStandard".
        /// </summary>
        public static ResolvedTextStyle ReadTextStyleForLabel(object config, string category, string subtype, params string?[] fallbacks)
        {
            var root = ToJsonElement(config);
            string? styleName = ResolveLabelStyleName(root, category, subtype);
            if (string.IsNullOrWhiteSpace(styleName))
                return ReadTextStyle(config, "poiStandard", fallbacks);
            return ReadTextStyle(config, styleName!, fallbacks);
        }

        // ---------------- Internals ----------------

        private static bool TryMergeStyle(JsonElement stylesRoot, string name, ref ResolvedTextStyle result)
            => TryMergeStyle(stylesRoot, name, ref result, ref UnsafeFalse);

        private static bool UnsafeFalse = false; // dummy ref for callsite above

        private static bool TryMergeStyle(JsonElement stylesRoot, string name, ref ResolvedTextStyle result, ref bool fontExplicit)
        {
            var styleObj = GetObject(stylesRoot, name);
            if (styleObj.ValueKind == JsonValueKind.Undefined) return false;

            // Only override fields present in the style
            var fontPath = GetString(styleObj, "FontPath");
            if (!string.IsNullOrWhiteSpace(fontPath))
                result.FontPath = fontPath;

            var fs = GetInt(styleObj, "FontSizePx");
            if (fs.HasValue)
            {
                result.FontSizePx = fs.Value;
                fontExplicit = true;
            }

            var fillStr = GetString(styleObj, "Fill");
            if (!string.IsNullOrWhiteSpace(fillStr))
                result.Fill = ImageHelpers.ParseHexToMagickColor(fillStr);

            var glowObj = GetObject(styleObj, "Glow");
            if (glowObj.ValueKind != JsonValueKind.Undefined)
                result.Glow = TryParseGlow(glowObj) ?? result.Glow;

            return true;
        }

        private static string? ResolveLabelStyleName(JsonElement root, string category, string subtype)
        {
            var labelStyles = GetObject(root, "LabelStyles");
            if (labelStyles.ValueKind == JsonValueKind.Undefined) return null;

            var catObj = GetObject(labelStyles, category);
            if (catObj.ValueKind == JsonValueKind.Undefined) return null;

            var styleName = GetString(catObj, subtype);
            return styleName;
        }

        private static TextRenderer.GlowStyle? TryParseGlow(JsonElement glow)
        {
            if (glow.ValueKind == JsonValueKind.Undefined || glow.ValueKind == JsonValueKind.Null)
                return null;

            // Supported fields: Color, OpacityPercent, WideningRadius, BlurRadius, OffsetX, OffsetY
            var colorStr = GetString(glow, "Color");
            var color = ImageHelpers.ParseHexToMagickColor(colorStr);

            int opacityPct = GetInt(glow, "OpacityPercent") ?? 100;
            int widen = GetInt(glow, "WideningRadius") ?? 0;
            int blur = GetInt(glow, "BlurRadius") ?? 0;
            int ox = GetInt(glow, "OffsetX") ?? 0;
            int oy = GetInt(glow, "OffsetY") ?? 0;

            return new TextRenderer.GlowStyle
            {
                Color = color,
                OpacityPercent = opacityPct,
                WideningRadius = widen,
                BlurRadius = blur,
                OffsetX = ox,
                OffsetY = oy
            };
        }

        private static MagickColor TryParseColor(string? s, bool defaultWhite = false)
        {
            if (!string.IsNullOrWhiteSpace(s))
                return ImageHelpers.ParseHexToMagickColor(s);
            return defaultWhite ? new MagickColor("white") : new MagickColor(0, 0, 0);
        }

        // ---- Json helpers ----

        private static JsonElement ToJsonElement(object config)
        {
            // Already a JsonElement
            if (config is JsonElement je) return je;

            // IDictionary -> convert to JsonElement
            if (config is IDictionary dict)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(dict);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }

            // Typed object -> serialize to JsonElement via reflection
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(config);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch
            {
                return default;
            }
        }

        private static JsonElement GetObject(JsonElement obj, string name)
        {
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Object)
                return el;
            return default;
        }

        private static string? GetString(JsonElement obj, string name)
        {
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString();
            return null;
        }

        private static int? GetInt(JsonElement obj, string name)
        {
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el) && el.TryGetInt32(out var v))
                return v;
            return null;
        }
    }
}