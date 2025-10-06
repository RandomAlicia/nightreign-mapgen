using System;
using System.Collections.Generic;
using ImageMagick;

namespace NightReign.MapGen
{
    public static class EventRenderer
    {
        public static void Render(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { Console.WriteLine("[Event] No POIs."); return; }
            if (cfg.Event == null) { Console.WriteLine("[Event] Config missing."); return; }

            int total = 0, matched = 0, drawn = 0, missingIcon = 0, notInIndex = 0, skippedSubtype = 0;

            foreach (var poi in pattern.pois)
            {
                if (string.IsNullOrWhiteSpace(poi.name)) continue;
                var n = poi.name.Trim();
                if (!n.StartsWith("Event -", StringComparison.OrdinalIgnoreCase)) continue;
                total++;

                if (!indexLookup.TryGetValue(n, out var entry) || entry == null)
                {
                    notInIndex++;
                    continue;
                }
                matched++;

                var idx = n.IndexOf("Event -", StringComparison.OrdinalIgnoreCase);
                string subtype = n.Substring(idx + "Event -".Length).Trim();

                var key = Program.NormalizeType(subtype);

                SizeBox? box = key switch
                {
                    "Scale_Bearing_Merchant" => cfg.Event.Scale_Bearing_Merchant,
                    "Meteor_Strike"          => cfg.Event.Meteor_Strike,
                    "Walking_Mausoleum"      => cfg.Event.Walking_Mausoleum,
                    _ => null
                };
                if (box == null) { skippedSubtype++; continue; }

                var iconPath = Program.ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }

                Program.CompositeIconAt(background, iconPath, poi.x, poi.z, box.WidthPx, box.HeightPx);
                drawn++;
            }

            Console.WriteLine($"[Event] total={total} matchedIndex={matched} drawn={drawn} missingIcon={missingIcon} notInIndex={notInIndex} skippedSubtype={skippedSubtype}");
        }
    }
}
