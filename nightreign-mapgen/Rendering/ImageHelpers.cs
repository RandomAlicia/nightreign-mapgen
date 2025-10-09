using System;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Small, self-contained helpers that were previously buried in Program.cs.
    /// </summary>
    public static class ImageHelpers
    {
        // Helper: convert hex to MagickColor (#RRGGBB or #RRGGBBAA, STRICT RRGGBBAA for 8 digits)
        public static MagickColor ParseHexToMagickColor(string hex)
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
                // STRICT: interpret as RRGGBBAA (no AARRGGBB heuristic)
                r = Convert.ToByte(s.Substring(0, 2), 16);
                g = Convert.ToByte(s.Substring(2, 2), 16);
                b = Convert.ToByte(s.Substring(4, 2), 16);
                a = Convert.ToByte(s.Substring(6, 2), 16);
            }
            else
            {
                return MagickColors.White;
            }
            
            return new MagickColor(r, g, b, a);
        }
        
        // Map world (x,z) to pixel (px,py) on a 1536×1536 canvas: px = 768 + x, py = 768 - z
        public static (int px, int py) WorldToPxPy1536(double x, double z)
        {
            int px = (int)Math.Round(768.0 + x);
            int py = (int)Math.Round(768.0 - z);
            return (px, py);
        }
        
        // Generic selector for POIs that extracts (name, x, z) using reflection.
        public static (string name, double x, double z) SelectNameXZ(object poi)
        {
            if (poi == null) return (string.Empty, 0, 0);
            var t = poi.GetType();
            
            // name / Name
            var nameProp = t.GetProperty("name") ?? t.GetProperty("Name");
            string name = nameProp?.GetValue(poi) as string ?? string.Empty;
            
            // pos / Pos -> x/X, z/Z
            var posProp = t.GetProperty("pos") ?? t.GetProperty("Pos");
            object pos = posProp?.GetValue(poi);
            double x = 0, z = 0;
            
            if (pos != null)
            {
                var pt = pos.GetType();
                var xProp = pt.GetProperty("x") ?? pt.GetProperty("X");
                var zProp = pt.GetProperty("z") ?? pt.GetProperty("Z");
                
                if (xProp != null)
                {
                    var xv = xProp.GetValue(pos);
                    x = xv is double xd ? xd : Convert.ToDouble(xv);
                }
                if (zProp != null)
                {
                    var zv = zProp.GetValue(pos);
                    z = zv is double zd ? zd : Convert.ToDouble(zv);
                }
            }
            
            return (name, x, z);
        }
    }
}
