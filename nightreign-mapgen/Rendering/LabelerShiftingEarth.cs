using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Shifting Earth labeler with offsets + by-coordinate overrides (with Anchor support).
    /// AppSettings shape:
    /// "LabelOffsets": {
    ///   "Shifting_Earth": { "dx": 0, "dy": 0 }
    /// },
    /// "LabelOverrides": {
    ///   "Shifting_Earth": {
    ///     "Epsilon": 0.0,
    ///     "ByCoord": {
    ///       "752,1170": { "dx": -20, "dy": 0, "style": "poiShiftingEarth", "Anchor": "left" }
    ///     }
    ///   }
    /// }
    /// </summary>
    public static class LabelerShiftingEarth
    {
        public static void Label(
            MagickImage background,
            string specialValue,
            string appsettingsPath,
            string cwd)
        {
            var overlayFile = MapOverlayFile(specialValue);
            if (overlayFile is null) return;

            var attachDir = Path.Combine(cwd, "../data/param/attach_points");
            var overlayPath = Path.Combine(attachDir, overlayFile);
            if (!File.Exists(overlayPath)) { return; }

            using var rootDoc = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
            var root = rootDoc.RootElement;

            // Base style (prefer poiShiftingEarth; fallback poiStandard)
            var styleName = ResolveStyleName(root);
            var baseStyle = ReadTextStyle(root, styleName, cwd, 24);

            // Base offsets and coord overrides
            var (baseDx, baseDy) = ReadTypeOffsets(root);
            var (epsilonWorld, epsilonPx, coordOverrides) = ReadOverridesSE(root);

            // i18n
            var poiStrings = LoadPoiStrings(root, cwd);

            using var doc = JsonDocument.Parse(File.ReadAllText(overlayPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

            int total = 0;
            int drawn = 0;
            int missingI18n = 0;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (!el.TryGetProperty("px", out var pxEl) || !el.TryGetProperty("py", out var pyEl)) continue;

                int px = pxEl.GetInt32();
                int py = pyEl.GetInt32();
                total++;
                string? i18nKey = null;
                if (el.TryGetProperty("i18nKey", out var kEl) && kEl.ValueKind == JsonValueKind.String)
                    i18nKey = kEl.GetString();

                string label;
                if (i18nKey != null)
                {
                    if (poiStrings.TryGetValue(i18nKey, out var v) && !string.IsNullOrWhiteSpace(v))
                        label = v!;
                    else
                    {
                        missingI18n++;
                        label = (el.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String) ? (nm.GetString() ?? "") : "";
                    }
                }
                else
                {
                    label = (el.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String) ? (nm.GetString() ?? "") : "";
                }

                if (string.IsNullOrWhiteSpace(label)) continue;

                // Defaults
                int dx = baseDx, dy = baseDy;
                string? overrideStyleName = null;
                string? anchor = null;

                // Per-coordinate overrides: try world (x,z) first, then pixel (px,py)
                if ((el.TryGetProperty("x", out var xEl) && xEl.ValueKind == JsonValueKind.Number) &&
                    (el.TryGetProperty("z", out var zEl) && zEl.ValueKind == JsonValueKind.Number))
                {
                    double wx = xEl.GetDouble(), wz = zEl.GetDouble();
                    if (TryMatchWorldOverrideSE(coordOverrides, wx, wz, epsilonWorld, out var ovW))
                    { dx = ovW.dx; dy = ovW.dy; overrideStyleName = ovW.style; anchor = ovW.anchor; }
                    else if (TryMatchPixelOverrideSE(coordOverrides, px, py, epsilonPx, out var ovP))
                    { dx = ovP.dx; dy = ovP.dy; overrideStyleName = ovP.style; anchor = ovP.anchor; }
                }
                else if (TryMatchPixelOverrideSE(coordOverrides, px, py, epsilonPx, out var ov))
                { dx = ov.dx; dy = ov.dy; overrideStyleName = ov.style; anchor = ov.anchor; }

                // Pick style to render
                var renderStyle = baseStyle;
                if (!string.IsNullOrWhiteSpace(overrideStyleName))
                    renderStyle = ReadTextStyle(root, overrideStyleName!, cwd, baseStyle.FontSize);

                int cx = px + dx;
                int cy = py + dy;

                // Anchor adjustment: left/right align the text rectangle at (cx,cy)
                if (!string.IsNullOrWhiteSpace(anchor))
                {
                    int w = MeasureMaxLineWidth(label, renderStyle.FontPath, renderStyle.FontSize);
                    var anch = anchor!.Trim().ToLowerInvariant();
                    if (anch == "right")
                        cx -= w / 2; // center will shift left half-width => right edge at (cx)
                    else if (anch == "left")
                        cx += w / 2; // center will shift right half-width => left edge at (cx)
                }

                // Normalize newlines
                label = label.Replace("\\n", "\n").Replace("\r\n", "\n");
                label = label.Replace("\\n", "\n");
                label = label.Replace("\\n", "\n");

                TextRenderer.DrawMultilineWithGlow(
                    background,
                    label,
                    renderStyle.FontPath,
                    renderStyle.FontSize,
                    cx,
                    cy,
                    renderStyle.Glow,
                    renderStyle.Fill,
                    null,
                    0.0,
                    4);

                drawn++;
            }
            Console.WriteLine($"[ShiftingEarth Labels] total={{total}} drawn={{drawn}} missingI18n={{missingI18n}}");
        }

        private static (int dx, int dy) ReadTypeOffsets(JsonElement root)
        {
            int dx = 0, dy = 0;
            if (root.TryGetProperty("LabelOffsets", out var lo) && lo.ValueKind == JsonValueKind.Object &&
                lo.TryGetProperty("Shifting_Earth", out var se) && se.ValueKind == JsonValueKind.Object)
            {
                JsonElement def = se;
                if (se.TryGetProperty("Default", out var d) && d.ValueKind == JsonValueKind.Object) def = d;
                if (def.TryGetProperty("dx", out var dxEl) && dxEl.TryGetInt32(out var dxVal)) dx = dxVal;
                if (def.TryGetProperty("dy", out var dyEl) && dyEl.TryGetInt32(out var dyVal)) dy = dyVal;
            }
            return (dx, dy);
        }

        private static (double epsWorld, double epsPx, List<CoordOverrideSE> list) ReadOverridesSE(JsonElement root)
        {
            double epsWorld = 1.0; // default like FieldBoss
            double epsPx = 0.0;
            var list = new List<CoordOverrideSE>();

            if (root.TryGetProperty("LabelOverrides", out var lo) && lo.ValueKind == JsonValueKind.Object &&
                lo.TryGetProperty("Shifting_Earth", out var se) && se.ValueKind == JsonValueKind.Object)
            {
                if (se.TryGetProperty("EpsilonWorld", out var ew) && ew.ValueKind == JsonValueKind.Number)
                    epsWorld = ew.GetDouble();
                if (se.TryGetProperty("EpsilonPx", out var ep) && ep.ValueKind == JsonValueKind.Number)
                    epsPx = ep.GetDouble();
                if (se.TryGetProperty("Epsilon", out var e) && e.ValueKind == JsonValueKind.Number)
                {
                    var v = e.GetDouble();
                    if (!(se.TryGetProperty("EpsilonWorld", out _))) epsWorld = v;
                    if (!(se.TryGetProperty("EpsilonPx", out _))) epsPx = v;
                }

                if (se.TryGetProperty("ByCoord", out var bc) && bc.ValueKind == JsonValueKind.Object)
                {
                    foreach (var kv in bc.EnumerateObject())
                    {
                        var key = kv.Name.Trim();
                        key = key.Trim().TrimEnd(',');
                        var parts = key.Split(',');
                        if (parts.Length < 2) continue;

                        bool looksWorld = parts[0].Contains('.') || parts[1].Contains('.');

                        int dx = 0, dy = 0;
                        string? style = null, anchor = null;
                        var val = kv.Value;
                        if (val.ValueKind == JsonValueKind.Object)
                        {
                            if (val.TryGetProperty("dx", out var dxEl) && dxEl.TryGetInt32(out var dxVal)) dx = dxVal;
                            if (val.TryGetProperty("dy", out var dyEl) && dyEl.TryGetInt32(out var dyVal)) dy = dyVal;
                            if (val.TryGetProperty("style", out var stEl) && stEl.ValueKind == JsonValueKind.String) style = stEl.GetString();
                            if (val.TryGetProperty("Anchor", out var anEl) && anEl.ValueKind == JsonValueKind.String) anchor = anEl.GetString();
                        }

                        var item = new CoordOverrideSE { dx = dx, dy = dy, style = style, anchor = anchor };
                        if (looksWorld)
                        {
                            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                            { item.x = x; item.z = z; }
                        }
                        else
                        {
                            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var xpx) &&
                                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ypx))
                            { item.px = xpx; item.py = ypx; }
                        }
                        if (item.x.HasValue || item.px.HasValue) list.Add(item);
                    }
                }
            }
            return (epsWorld, epsPx, list);
        }

        private static bool TryMatchWorldOverrideSE(List<CoordOverrideSE> list,
                                                    double x, double z, double epsWorld,
                                                    out (int dx, int dy, string? style, string? anchor) hit)
        {
            foreach (var it in list)
            {
                if (it.x.HasValue && it.z.HasValue &&
                    Math.Abs(it.x.Value - x) <= epsWorld &&
                    Math.Abs(it.z.Value - z) <= epsWorld)
                { hit = (it.dx, it.dy, it.style, it.anchor); return true; }
            }
            hit = (0, 0, null, null); return false;
        }

        private static bool TryMatchPixelOverrideSE(List<CoordOverrideSE> list,
                                                    int px, int py, double epsPx,
                                                    out (int dx, int dy, string? style, string? anchor) hit)
        {
            foreach (var it in list)
            {
                if (it.px.HasValue && it.py.HasValue &&
                    Math.Abs(it.px.Value - px) <= epsPx &&
                    Math.Abs(it.py.Value - py) <= epsPx)
                { hit = (it.dx, it.dy, it.style, it.anchor); return true; }
            }
            hit = (0, 0, null, null); return false;
        }

        private static int MeasureMaxLineWidth(string text, string fontPath, int fontSizePx)
        {
            var lines = text.Replace("\r\n", "\n").Replace("\n", "\n").Split('\n');
            int maxW = 0;
            using var meas = new MagickImage(MagickColors.Transparent, 1, 1);
            meas.Settings.Font = fontPath;
            meas.Settings.FontPointsize = fontSizePx;
            foreach (var line in lines)
            {
                var tm = meas.FontTypeMetrics(line);
                int w = (int)Math.Ceiling(tm.TextWidth);
                if (w > maxW) maxW = w;
            }
            return maxW;
        }

        private static string ResolveStyleName(JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("Text", out var text) && text.ValueKind == JsonValueKind.Object &&
                    text.TryGetProperty("Styles", out var styles) && styles.ValueKind == JsonValueKind.Object)
                {
                    if (styles.TryGetProperty("poiShiftingEarth", out var _))
                        return "poiShiftingEarth";
                }
            }
            catch { }
            return "poiStandard";
        }

        private static string? MapOverlayFile(string? special)
        {
            if (string.IsNullOrWhiteSpace(special)) return null;
            var s = special.Trim();
            var sc = s.ToLowerInvariant();
            if (sc == "mountaintop") return "mountaintop_overlay.json";
            if (sc == "crater")      return "crater_overlay.json";
            if (sc == "noklateo")    return "noklateo_overlay.json";
            if (sc == "default" || sc == "rotted woods" || sc == "rotted_woods") return null;

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            {
                switch (code)
                {
                    case 1: return "mountaintop_overlay.json";
                    case 2: return "crater_overlay.json";
                    case 5: return "noklateo_overlay.json"; // some patterns use 5 for Noklateo
                    default: return null;
                }
            }
            return null;
        }

        // ---- Shifting_Earth overrides: world (x,z) and pixel (px,py) ----
        private sealed class CoordOverrideSE
        {
            public double? x; public double? z;
            public int? px; public int? py;
            public int dx; public int dy;
            public string? style;
            public string? anchor;
        }

        private sealed class ResolvedTextStyle
        {
            public string FontPath { get; }
            public int FontSize { get; }
            public MagickColor Fill { get; }
            public TextRenderer.GlowStyle? Glow { get; }
            public ResolvedTextStyle(string fontPath, int fontSize, MagickColor fill, TextRenderer.GlowStyle? glow)
            { FontPath = fontPath; FontSize = fontSize; Fill = fill; Glow = glow; }
        }

        private static ResolvedTextStyle ReadTextStyle(JsonElement root, string styleName, string cwd, int defaultSize)
        {
            var (fontPath, fontSize, fill, glow) = StyleResolver.ReadTextStyle(root, styleName, cwd, defaultSize);
            return new ResolvedTextStyle(fontPath, fontSize, fill, glow);
        }

        private static Dictionary<string, string> LoadPoiStrings(JsonElement root, string cwd)
        {
            try
            {
                string i18nFolder = (root.TryGetProperty("I18nFolder", out var f) && f.ValueKind == JsonValueKind.String)
                    ? (f.GetString() ?? "../i18n") : "../i18n";
                string i18nLang = (root.TryGetProperty("I18nLang", out var l) && l.ValueKind == JsonValueKind.String)
                    ? (l.GetString() ?? "en") : "en";

                var i18nPath = Path.Combine(ResolvePath(i18nFolder, cwd), i18nLang, "poi.json");
                var json = File.ReadAllText(i18nPath);
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return dict ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static string ResolvePath(string path, string cwd)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(cwd, path));
        }
    }
}
