using System;
using System.Collections.Generic;
using ImageMagick;

namespace NightReign.MapGen
{
    public static class MinorBaseRenderer
    {
        public static void Render(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { Console.WriteLine("[MinorBase] No POIs."); return; }
            if (cfg.MinorBase == null) { Console.WriteLine("[MinorBase] Config missing."); return; }
            
            int total = 0, matched = 0, drawn = 0, skippedCat = 0, missingIcon = 0, notInIndex = 0;
            
            foreach (var poi in pattern.pois)
            {
                if (string.IsNullOrWhiteSpace(poi.name)) continue;
                total++;
                
                if (!indexLookup.TryGetValue(poi.name.Trim(), out var entry) || entry == null)
                {
                    notInIndex++;
                    continue;
                }
                matched++;
                
                var catNorm = Program.NormalizeType(entry.category ?? "");
                SizeBox? box = catNorm switch
                {
                    "Church"          => cfg.MinorBase.Church,
                    "Small_Camp"      => cfg.MinorBase.Small_Camp,
                    "Sorcerers_Rise"  => cfg.MinorBase.Sorcerers_Rise,
                    "Difficult_Sorcerers_Rise" => cfg.MinorBase.Sorcerers_Rise,
                    "Township"        => cfg.MinorBase.Township,
                    _ => null
                };
                if (box == null) { skippedCat++; continue; }
                
                var iconPath = Program.ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }
                
                Program.CompositeIconAt(background, iconPath, poi.x, poi.z, box.WidthPx, box.HeightPx);
                drawn++;
            }
            
            Console.WriteLine($"[MinorBase] total={total} matchedIndex={matched} drawn={drawn} skippedCat={skippedCat} missingIcon={missingIcon} notInIndex={notInIndex}");
        }
    }
}