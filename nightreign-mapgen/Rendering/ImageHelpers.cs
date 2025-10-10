using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ImageMagick; // for MagickColor & Quantum

namespace NightReign.MapGen.Rendering
{
    public static class ImageHelpers
    {
        // =================== World → Pixel mapping ===================

        /// <summary>
        /// Legacy calibration: maps world (x,z) to pixels assuming a 1536x1536 canvas.
        /// </summary>
        public static (int px, int py) WorldToPxPy1536(double x, double z)
        {
            const double size = 1536.0;
            int px = (int)Math.Round(size * 0.5 + x);
            int py = (int)Math.Round(size * 0.5 - z);
            return (px, py);
        }

        /// <summary>
        /// Scales world coords for any background size.
        /// </summary>
        public static (int px, int py) WorldToPxPy(double x, double z, int canvasWidth, int canvasHeight)
        {
            double sx = canvasWidth / 1536.0;
            double sy = canvasHeight / 1536.0;
            int px = (int)Math.Round(canvasWidth * 0.5 + x * sx);
            int py = (int)Math.Round(canvasHeight * 0.5 - z * sy);
            return (px, py);
        }

        // =================== POI extraction ===================

        /// <summary>
        /// Extracts (name, x, z) from a POI object that may be:
        /// - System.Text.Json.JsonElement
        /// - IDictionary&lt;string, object&gt; / IDictionary&lt;string, JsonElement&gt;
        /// - A typed object with properties name/x/z or name/pos.{x,z}
        /// </summary>
        public static (string name, double x, double z) SelectNameXZ(object poi)
        {
            if (poi is null) return ("", 0, 0);

            // Fast path: JsonElement
            if (poi is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                string name = TryGetString(je, "name") ?? "";
                double x = TryGetDouble(je, "x") ?? TryGetDoubleFromNested(je, "pos", "x") ?? 0;
                double z = TryGetDouble(je, "z") ?? TryGetDoubleFromNested(je, "pos", "z") ?? 0;
                return (name, x, z);
            }

            // IDictionary paths
            if (poi is IDictionary dictObj)
            {
                string name = TryGetString(dictObj, "name") ?? "";
                double x = TryGetDouble(dictObj, "x") ?? TryGetDoubleFromNested(dictObj, "pos", "x") ?? 0;
                double z = TryGetDouble(dictObj, "z") ?? TryGetDoubleFromNested(dictObj, "pos", "z") ?? 0;
                return (name, x, z);
            }

            // Reflection path for typed POI classes
            var t = poi.GetType();
            var nameProp = t.GetProperty("name", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? t.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var xProp = t.GetProperty("x", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? t.GetProperty("X", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var zProp = t.GetProperty("z", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? t.GetProperty("Z", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            string n = nameProp?.GetValue(poi)?.ToString() ?? "";

            if (xProp != null && zProp != null)
            {
                double xx = ConvertToDouble(xProp.GetValue(poi));
                double zz = ConvertToDouble(zProp.GetValue(poi));
                return (n, xx, zz);
            }

            // Try nested pos.{x,z}
            var posProp = t.GetProperty("pos", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? t.GetProperty("Pos", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (posProp?.GetValue(poi) is object pos)
            {
                var posT = pos.GetType();
                var px = posT.GetProperty("x", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? posT.GetProperty("X", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var pz = posT.GetProperty("z", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? posT.GetProperty("Z", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (px != null && pz != null)
                {
                    double xx = ConvertToDouble(px.GetValue(pos));
                    double zz = ConvertToDouble(pz.GetValue(pos));
                    return (n, xx, zz);
                }
            }

            // Fallback
            return (n, 0, 0);
        }

        // =================== Color parsing for Magick.NET ===================

        /// <summary>
        /// Parses a hex or css-like color value to a MagickColor.
        /// Supports: "#RGB", "#ARGB", "#RRGGBB", "#AARRGGBB", "0x...", "rgb(r,g,b)", "rgba(r,g,b,a)", and named "transparent".
        /// Alpha component (if present) is expected first in ARGB forms; otherwise alpha=255.
        /// </summary>
        public static MagickColor ParseHexToMagickColor(string? input)
{
    if (string.IsNullOrWhiteSpace(input))
        return new MagickColor((byte)ToQuantum((byte)0), (byte)ToQuantum((byte)0), (byte)ToQuantum((byte)0), (byte)ToQuantum((byte)255)); // opaque black

    var s = input.Trim();

    // Named transparent
    if (string.Equals(s, "transparent", StringComparison.OrdinalIgnoreCase))
        return new MagickColor((byte)ToQuantum((byte)0), (byte)ToQuantum((byte)0), (byte)ToQuantum((byte)0), (byte)ToQuantum((byte)0));

    // rgb()/rgba()
    if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        return ParseRgbFunction(s);

    // strip prefixes
    if (s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1);
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);

    // Always RGBA for hex
    if (s.Length == 3) // RGB -> expand, alpha = 255
    {
        byte r = ExpandNibble(s[0]);
        byte g = ExpandNibble(s[1]);
        byte b = ExpandNibble(s[2]);
        return new MagickColor((byte)ToQuantum(r), (byte)ToQuantum(g), (byte)ToQuantum(b), (byte)ToQuantum((byte)255));
    }

    if (s.Length == 4) // RGBA (nibbles)
    {
        byte r = ExpandNibble(s[0]);
        byte g = ExpandNibble(s[1]);
        byte b = ExpandNibble(s[2]);
        byte a = ExpandNibble(s[3]);
        return new MagickColor((byte)ToQuantum(r), (byte)ToQuantum(g), (byte)ToQuantum(b), (byte)ToQuantum(a));
    }

    if (s.Length == 6) // RRGGBB, alpha = 255
    {
        byte r = Convert.ToByte(s.Substring(0, 2), 16);
        byte g = Convert.ToByte(s.Substring(2, 2), 16);
        byte b = Convert.ToByte(s.Substring(4, 2), 16);
        return new MagickColor((byte)ToQuantum(r), (byte)ToQuantum(g), (byte)ToQuantum(b), (byte)ToQuantum((byte)255));
    }

    if (s.Length == 8) // RRGGBBAA
    {
        byte r = Convert.ToByte(s.Substring(0, 2), 16);
        byte g = Convert.ToByte(s.Substring(2, 2), 16);
        byte b = Convert.ToByte(s.Substring(4, 2), 16);
        byte a = Convert.ToByte(s.Substring(6, 2), 16);
        return new MagickColor((byte)ToQuantum(r), (byte)ToQuantum(g), (byte)ToQuantum(b), (byte)ToQuantum(a));
    }

    // Fall back to MagickColor parser (handles some names)
    try
    {
        return new MagickColor(input);
    }
    catch
    {
        // Final fallback: opaque black
        return new MagickColor((byte)ToQuantum((byte)0), (byte)ToQuantum((byte)0), (byte)ToQuantum((byte)0), (byte)ToQuantum((byte)255));
    }
}


        private static MagickColor ParseRgbFunction(string s)
{
    // Expected formats:
    // rgb(r,g,b)
    // rgba(r,g,b,a)  where a can be 0..1, 0..255, or %
    try
    {
        var open = s.IndexOf('(');
        var close = s.IndexOf(')');
        if (open < 0 || close <= open) throw new FormatException();
        var parts = s.Substring(open + 1, close - open - 1).Split(',');
        if (parts.Length < 3) throw new FormatException();

        byte r = ParseByte(parts[0]);
        byte g = ParseByte(parts[1]);
        byte b = ParseByte(parts[2]);
        byte a = 255;

        if (parts.Length >= 4)
        {
            var aStr = parts[3].Trim();
            if (aStr.EndsWith("%", StringComparison.Ordinal))
            {
                var pct = double.Parse(aStr.TrimEnd('%').Trim(), System.Globalization.CultureInfo.InvariantCulture);
                a = (byte)Math.Clamp(Math.Round(255 * (pct / 100.0)), 0, 255);
            }
            else if (aStr.Contains(".", StringComparison.Ordinal))
            {
                var af = double.Parse(aStr, System.Globalization.CultureInfo.InvariantCulture);
                a = (byte)Math.Clamp(Math.Round(255 * af), 0, 255);
            }
            else
            {
                a = ParseByte(aStr);
            }
        }

        return new MagickColor((byte)ToQuantum(r), (byte)ToQuantum(g), (byte)ToQuantum(b), (byte)ToQuantum(a));
    }
    catch
    {
        return new MagickColor((byte)ToQuantum(0), (byte)ToQuantum(0), (byte)ToQuantum(0), (byte)ToQuantum(225));
    }
}


        private static byte ParseByte(string s)
        {
            s = s.Trim();
            if (s.EndsWith("%"))
            {
                var pct = double.Parse(s.TrimEnd('%').Trim(), System.Globalization.CultureInfo.InvariantCulture);
                return (byte)Math.Clamp(Math.Round(255 * (pct / 100.0)), 0, 255);
            }
            return (byte)Math.Clamp(int.Parse(s, System.Globalization.CultureInfo.InvariantCulture), 0, 255);
        }

        private static byte ExpandNibble(char c)
        {
            int v = Convert.ToInt32(c.ToString(), 16);
            return (byte)(v * 17); // e.g., 'A' -> 10 -> 170
        }

        private static ushort ToQuantum(byte v)
        {
            // Map 0..255 -> 0..Quantum.Max (Q8/Q16 safe)
            double max = Quantum.Max;
            return (ushort)Math.Round(v * (max / 255.0));
        }

        // ---------- internal helpers used above ----------

        private static string? TryGetString(JsonElement obj, string name)
        {
            if (obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString();
            if (obj.TryGetProperty(ToTitle(name), out el) && el.ValueKind == JsonValueKind.String)
                return el.GetString();
            return null;
        }

        private static double? TryGetDouble(JsonElement obj, string name)
        {
            if (obj.TryGetProperty(name, out var el) && el.TryGetDouble(out var v))
                return v;
            if (obj.TryGetProperty(ToTitle(name), out el) && el.TryGetDouble(out var v2))
                return v2;
            return null;
        }

        private static double? TryGetDoubleFromNested(JsonElement obj, string parent, string child)
        {
            if (obj.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object)
                return TryGetDouble(p, child);
            if (obj.TryGetProperty(ToTitle(parent), out var p2) && p2.ValueKind == JsonValueKind.Object)
                return TryGetDouble(p2, child);
            return null;
        }

        private static string? TryGetString(IDictionary dict, string key)
        {
            foreach (DictionaryEntry kv in dict)
            {
                var k = kv.Key?.ToString();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (kv.Value is string s) return s;
                    if (kv.Value is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString();
                    return kv.Value?.ToString();
                }
            }
            return null;
        }

        private static double? TryGetDouble(IDictionary dict, string key)
        {
            foreach (DictionaryEntry kv in dict)
            {
                var k = kv.Key?.ToString();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (kv.Value is null) return null;
                    if (kv.Value is double d) return d;
                    if (kv.Value is float f) return (double)f;
                    if (kv.Value is int i) return i;
                    if (kv.Value is long l) return l;
                    if (kv.Value is decimal m) return (double)m;
                    if (kv.Value is string s && double.TryParse(s, out var ds)) return ds;
                    if (kv.Value is JsonElement je && je.TryGetDouble(out var jd)) return jd;
                }
            }
            return null;
        }

        private static double? TryGetDoubleFromNested(IDictionary dict, string parentKey, string childKey)
        {
            foreach (DictionaryEntry kv in dict)
            {
                var k = kv.Key?.ToString();
                if (string.Equals(k, parentKey, StringComparison.OrdinalIgnoreCase) && kv.Value is object v)
                {
                    // nested JsonElement
                    if (v is JsonElement je && je.ValueKind == JsonValueKind.Object)
                    {
                        return TryGetDouble(je, childKey);
                    }
                    // nested dictionary
                    if (v is IDictionary d)
                    {
                        return TryGetDouble(d, childKey);
                    }
                    // nested typed object
                    var t = v.GetType();
                    var childProp = t.GetProperty(childKey, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                                ?? t.GetProperty(ToTitle(childKey), BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (childProp != null)
                        return ConvertToDouble(childProp.GetValue(v));
                }
            }
            return null;
        }

        private static double ConvertToDouble(object? v)
        {
            if (v is null) return 0;
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is int i) return i;
            if (v is long l) return l;
            if (v is decimal m) return (double)m;
            if (v is string s && double.TryParse(s, out var ds)) return ds;
            if (v is JsonElement je && je.TryGetDouble(out var jd)) return jd;
            return 0;
        }

        private static string ToTitle(string s) => string.IsNullOrEmpty(s)
            ? s
            : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}