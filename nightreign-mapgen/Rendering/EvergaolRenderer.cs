using System;
using System.Collections.Generic;
using ImageMagick;

namespace NightReign.MapGen
{
    public static class EvergaolRenderer
    {
        public static void Render(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { Console.WriteLine("[Evergaol] No POIs."); return; }
            if (cfg.Evergaol == null || cfg.Evergaol.Default == null) { Console.WriteLine("[Evergaol] Config missing (Evergaol.Default)."); return; }

            int total = 0, matched = 0, drawn = 0, missingIcon = 0, notInIndex = 0;

            foreach (var poi in pattern.pois)
            {
                if (string.IsNullOrWhiteSpace(poi.name)) continue;
                var n = poi.name.Trim();
                if (!n.StartsWith("Evergaol -", StringComparison.OrdinalIgnoreCase)) continue;
                total++;

                if (!indexLookup.TryGetValue(n, out var entry) || entry == null)
                {
                    notInIndex++;
                    continue;
                }
                matched++;

                var iconPath = Program.ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }

                Program.CompositeIconAt(background, iconPath, poi.x, poi.z, cfg.Evergaol.Default.WidthPx, cfg.Evergaol.Default.HeightPx);
                drawn++;
            }

            Console.WriteLine($"[Evergaol] total={total} matchedIndex={matched} drawn={drawn} missingIcon={missingIcon} notInIndex={notInIndex}");
        }
    }
}
