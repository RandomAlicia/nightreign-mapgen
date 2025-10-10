using System;
using System.Collections.Generic;
using ImageMagick;

namespace NightReign.MapGen
{
    public static class MajorBaseRenderer
    {
        public static void Render(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { if (NightReign.MapGen.Program.Verbose) Console.WriteLine("[MajorBase] No POIs."); return; }
            if (cfg.MajorBase == null) { if (NightReign.MapGen.Program.Verbose) Console.WriteLine("[MajorBase] Config missing."); return; }
            
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
                    "Camp" => cfg.MajorBase.Camp,
                    "Fort" => cfg.MajorBase.Fort,
                    "Great_Church" or "GreatChurch" => cfg.MajorBase.Great_Church,
                    "Ruins" => cfg.MajorBase.Ruins,
                    _ => null
                };
                if (box == null) { skippedCat++; continue; }
                
                var iconPath = Program.ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }
                
                Program.CompositeIconAt(background, iconPath, poi.x, poi.z, box.WidthPx, box.HeightPx);
                drawn++;
            }
            
            Console.WriteLine($"[MajorBase] total={total} matchedIndex={matched} drawn={drawn} skippedCat={skippedCat} missingIcon={missingIcon} notInIndex={notInIndex}");
        }
    }
}