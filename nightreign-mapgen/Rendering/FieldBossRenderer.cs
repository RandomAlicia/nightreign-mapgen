using System;
using System.Collections.Generic;
using ImageMagick;

namespace NightReign.MapGen
{
    public static class FieldBossRenderer
    {
        public static void Render(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { Console.WriteLine("[FieldBoss] No POIs."); return; }
            if (cfg.FieldBoss == null) { Console.WriteLine("[FieldBoss] Config missing."); return; }

            int total = 0, matched = 0, drawn = 0, missingIcon = 0, notInIndex = 0, skippedSubtype = 0;

            string[] prefixesSpace = { "Arena Boss -", "Field Boss -", "Strong Field Boss -", "Castle -" };
            string[] prefixesUnd   = { "Arena_Boss -", "Field_Boss -", "Strong_Field_Boss -", "Castle -" };

            foreach (var poi in pattern.pois)
            {
                if (string.IsNullOrWhiteSpace(poi.name)) continue;
                var n = poi.name.Trim();

                bool isField = false;
                foreach (var p in prefixesSpace) if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { isField = true; break; }
                if (!isField)
                    foreach (var p in prefixesUnd) if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { isField = true; break; }
                if (!isField) continue;

                total++;

                if (!indexLookup.TryGetValue(n, out var entry) || entry == null)
                {
                    notInIndex++;
                    continue;
                }
                matched++;

                string subtype;
                if (n.StartsWith("Arena Boss -", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Arena_Boss -", StringComparison.OrdinalIgnoreCase))
                    subtype = "Arena_Boss";
                else if (n.StartsWith("Strong Field Boss -", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Strong_Field_Boss -", StringComparison.OrdinalIgnoreCase))
                    subtype = "Strong_Field_Boss";
                else if (n.StartsWith("Field Boss -", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Field_Boss -", StringComparison.OrdinalIgnoreCase))
                    subtype = "Field_Boss";
                else if (n.StartsWith("Castle -", StringComparison.OrdinalIgnoreCase))
                    subtype = "Castle";
                else
                    subtype = "Field_Boss";

                SizeBox? box = subtype switch
                {
                    "Arena_Boss"         => cfg.FieldBoss.Arena_Boss,
                    "Strong_Field_Boss"  => cfg.FieldBoss.Strong_Field_Boss,
                    "Field_Boss"         => cfg.FieldBoss.Field_Boss,
                    "Castle"             => cfg.FieldBoss.Castle,
                    _ => null
                };
                if (box == null) { skippedSubtype++; continue; }

                var iconPath = Program.ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }

                Program.CompositeIconAt(background, iconPath, poi.x, poi.z, box.WidthPx, box.HeightPx);
                drawn++;
            }

            Console.WriteLine($"[FieldBoss] total={total} matchedIndex={matched} drawn={drawn} missingIcon={missingIcon} notInIndex={notInIndex} skippedSubtype={skippedSubtype}");
        }
    }
}
