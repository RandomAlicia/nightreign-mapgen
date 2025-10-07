using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    public static class LabelerFieldBoss
    {
        private const string PFX_FIELD  = "Field_Boss - ";
        private const string PFX_STRONG = "Strong_Field_Boss - ";
        private const string PFX_ARENA  = "Arena_Boss - ";
        private const string PFX_CASTLE = "Castle - ";

        public static void Label<T>(
            MagickImage background,
            IEnumerable<T> pois,
            Func<T, (string name, double x, double z)> select,
            IDictionary<string, object> indexLookup,
            string appsettingsPath,
            string cwd,
            Func<double, double, (int px, int py)> mapXZtoPxPy,
            string defaultStyle = "poiStandard"
        )
        {
            if (background is null || pois is null || select is null || mapXZtoPxPy is null) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
            var root = doc.RootElement;

            // i18n
            var i18nFolder = root.TryGetProperty("I18nFolder", out var i18nF) && i18nF.ValueKind == JsonValueKind.String
                ? i18nF.GetString()! : "../i18n";
            var i18nLang = root.TryGetProperty("I18nLang", out var i18nL) && i18nL.ValueKind == JsonValueKind.String
                ? i18nL.GetString()! : "en";

            var typeOffsets    = ReadTypeOffsets(root);
            var typeStyles     = ReadTypeStyles(root);
            var coordOverrides = ReadCoordOverrides(root, out double epsilon);
            var castleReanchor = ReadCastleReanchor(root, out bool hasCastleReanchor);

            int total = 0, drawn = 0, missingI18n = 0;

            foreach (var poi in pois)
            {
                var (rawName, x0, z0) = select(poi);
                if (string.IsNullOrWhiteSpace(rawName)) continue;

                string kind;
                if      (rawName.StartsWith(PFX_FIELD,  StringComparison.Ordinal)) kind = "Field_Boss";
                else if (rawName.StartsWith(PFX_STRONG, StringComparison.Ordinal)) kind = "Strong_Field_Boss";
                else if (rawName.StartsWith(PFX_ARENA,  StringComparison.Ordinal)) kind = "Arena_Boss";
                else if (rawName.StartsWith(PFX_CASTLE, StringComparison.Ordinal)) kind = "Castle";
                else continue;

                total++;

                var label = LabelTextResolver.Resolve(rawName, indexLookup, i18nFolder, i18nLang, cwd);
                if (string.IsNullOrWhiteSpace(label)) { missingI18n++; continue; }
                label = label.Replace("\\n", "\n");

                // Effective anchor coords
                double useX = x0, useZ = z0;
                if (kind == "Castle" && hasCastleReanchor)
                {
                    useX = castleReanchor.x;
                    useZ = castleReanchor.z;
                }

                var (px, py) = mapXZtoPxPy(useX, useZ);

                // Default per-type offset
                var (dx, dy) = typeOffsets.TryGetValue(kind, out var od) ? od : (0, 0);

                // Coordinate override?
                if (TryFindCoordOverride(coordOverrides, x0, z0, epsilon, out var co))
                {
                    dx = co.dx;
                    dy = co.dy;
                }

                // Style selection
                string styleName = defaultStyle;
                if (typeStyles.TryGetValue(kind, out var styleOverride) && !string.IsNullOrWhiteSpace(styleOverride))
                    styleName = styleOverride; // e.g., Castle -> poiCastle
                if (co.style != null) styleName = co.style;

                var (fontPath, fontSize, fill, glow) = ReadTextStyle(root, styleName, cwd);

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

            Console.WriteLine($"[FieldBoss Labels] total={total} drawn={drawn} missingI18n={missingI18n}");
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

        private static Dictionary<string, (int dx, int dy)> ReadTypeOffsets(JsonElement root)
        {
            var map = new Dictionary<string, (int dx, int dy)>(StringComparer.OrdinalIgnoreCase)
            {
                { "Field_Boss",        (0, 0) },
                { "Strong_Field_Boss", (0, 0) },
                { "Arena_Boss",        (0, 0) },
                { "Castle",            (0, 0) }
            };

            if (root.TryGetProperty("LabelOffsets", out var lo) && lo.ValueKind == JsonValueKind.Object &&
                lo.TryGetProperty("FieldBoss", out var fb) && fb.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in fb.EnumerateObject())
                {
                    var name = kv.Name;
                    var val = kv.Value;
                    try
                    {
                        int dx = 0, dy = 0;
                        if (val.ValueKind == JsonValueKind.Array && val.GetArrayLength() == 2)
                        {
                            dx = val[0].GetInt32();
                            dy = val[1].GetInt32();
                        }
                        else if (val.ValueKind == JsonValueKind.Object)
                        {
                            if (val.TryGetProperty("dx", out var dxEl) && dxEl.TryGetInt32(out var dxVal)) dx = dxVal;
                            if (val.TryGetProperty("dy", out var dyEl) && dyEl.TryGetInt32(out var dyVal)) dy = dyVal;
                        }
                        map[name] = (dx, dy);
                    }
                    catch { }
                }
            }
            return map;
        }

        private static Dictionary<string, string> ReadTypeStyles(JsonElement root)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("LabelStyles", out var ls) && ls.ValueKind == JsonValueKind.Object &&
                ls.TryGetProperty("FieldBoss", out var fb) && fb.ValueKind == JsonValueKind.Object)
            {
                foreach (var kv in fb.EnumerateObject())
                {
                    if (kv.Value.ValueKind == JsonValueKind.String)
                        map[kv.Name] = kv.Value.GetString()!;
                }
            }
            return map;
        }

        private static List<(double x, double z, int dx, int dy, string? style)> ReadCoordOverrides(JsonElement root, out double epsilon)
        {
            var list = new List<(double x, double z, int dx, int dy, string? style)>();
            epsilon = 0.25; // default tolerance

            if (root.TryGetProperty("LabelOverrides", out var lo) && lo.ValueKind == JsonValueKind.Object &&
                lo.TryGetProperty("FieldBoss", out var fb) && fb.ValueKind == JsonValueKind.Object)
            {
                if (fb.TryGetProperty("Epsilon", out var epsEl) && epsEl.ValueKind == JsonValueKind.Number)
                    epsilon = epsEl.GetDouble();

                if (fb.TryGetProperty("ByCoord", out var bc) && bc.ValueKind == JsonValueKind.Object)
                {
                    foreach (var kv in bc.EnumerateObject())
                    {
                        // key: "x,z"
                        var key = kv.Name;
                        var parts = key.Split(',');
                        if (parts.Length != 2) continue;
                        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x))
                            continue;
                        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double z))
                            continue;

                        int dx = 0, dy = 0;
                        string? style = null;
                        var val = kv.Value;
                        if (val.ValueKind == JsonValueKind.Object)
                        {
                            if (val.TryGetProperty("dx", out var dxEl) && dxEl.TryGetInt32(out var dxVal)) dx = dxVal;
                            if (val.TryGetProperty("dy", out var dyEl) && dyEl.TryGetInt32(out var dyVal)) dy = dyVal;
                            if (val.TryGetProperty("Style", out var stEl) && stEl.ValueKind == JsonValueKind.String) style = stEl.GetString();
                        }
                        list.Add((x, z, dx, dy, style));
                    }
                }
            }

            return list;
        }

        private static (double x, double z) ReadCastleReanchor(JsonElement root, out bool has)
        {
            has = false;
            double x = 0, z = 0;
            if (root.TryGetProperty("LabelReanchors", out var lr) && lr.ValueKind == JsonValueKind.Object &&
                lr.TryGetProperty("FieldBoss", out var fb) && fb.ValueKind == JsonValueKind.Object &&
                fb.TryGetProperty("Castle", out var cast) && cast.ValueKind == JsonValueKind.Object)
            {
                if (cast.TryGetProperty("x", out var xEl) && xEl.ValueKind == JsonValueKind.Number &&
                    cast.TryGetProperty("z", out var zEl) && zEl.ValueKind == JsonValueKind.Number)
                {
                    x = xEl.GetDouble();
                    z = zEl.GetDouble();
                    has = true;
                }
            }
            return (x, z);
        }

        private static bool TryFindCoordOverride(List<(double x, double z, int dx, int dy, string? style)> list,
            double x, double z, double eps, out (int dx, int dy, string? style) match)
        {
            foreach (var item in list)
            {
                if (Math.Abs(item.x - x) <= eps && Math.Abs(item.z - z) <= eps)
                {
                    match = (item.dx, item.dy, item.style);
                    return true;
                }
            }
            match = (0, 0, null);
            return false;
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
