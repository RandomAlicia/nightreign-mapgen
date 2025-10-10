using System;
using System.Collections.Generic;
using ImageMagick;

namespace NightReign.MapGen
{
    public static class NightBossRenderer
    {
        public static void Render(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { if (NightReign.MapGen.Program.Verbose) Console.WriteLine("[NightBoss] No POIs."); return; }
            if (cfg.NightBoss == null || cfg.NightBoss.Default == null) { if (NightReign.MapGen.Program.Verbose) Console.WriteLine("[NightBoss] Config missing (NightBoss.Default)."); return; }
            
            int total = 0, matched = 0, drawn = 0, missingIcon = 0, notInIndex = 0;
            
            foreach (var poi in pattern.pois)
            {
                if (string.IsNullOrWhiteSpace(poi.name)) continue;
                var n = poi.name.Trim();
                if (!(n.StartsWith("Night Boss -", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Night_Boss -", StringComparison.OrdinalIgnoreCase)))
                continue;
                
                total++;
                
                if (!indexLookup.TryGetValue(n, out var entry) || entry == null)
                {
                    notInIndex++;
                    continue;
                }
                matched++;
                
                var iconPath = Program.ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }
                
                Program.CompositeIconAt(background, iconPath, poi.x, poi.z, cfg.NightBoss.Default.WidthPx, cfg.NightBoss.Default.HeightPx);
                drawn++;
            }
            
            Console.WriteLine($"[NightBoss] total={total} matchedIndex={matched} drawn={drawn} missingIcon={missingIcon} notInIndex={notInIndex}");
        }
    }
}