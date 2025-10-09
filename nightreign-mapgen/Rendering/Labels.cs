using System;
using System.Collections.Generic;
using System.IO;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// All label passes, isolated from Program.Main.
    /// </summary>
    public static class Labels
    {
        public static void RenderAll(
        MagickImage background,
        PatternDoc patternDoc,
        Dictionary<string, IndexEntry> indexLookup,
        AppConfig cfg,
        string cwd,
        string? specialValue,
        string patternId,
        string patternPath)
        {
            try
            {
                // Shared: name-keyed object map for resolver
                var indexObj = BuildNameKeyedIndex(indexLookup);
                
                // 12) Labels - Major Base
                NightReign.MapGen.Rendering.LabelerMajorBase.Label(
                background,
                patternDoc.pois,
                p => (p.name, p.x, p.z),
                indexObj,
                Path.Combine(cwd, "appsettings.json"),
                cwd,
                ImageHelpers.WorldToPxPy1536,
                "poiStandard"
                );
                
                // 12b) Labels - Minor Base (Sorcerers_Rise)
                try
                {
                    var indexObj2 = BuildNameKeyedIndex(indexLookup);
                    NightReign.MapGen.Rendering.LabelerMinorBase.Label(
                    background,
                    patternDoc.pois,
                    p => (p.name, p.x, p.z),
                    indexObj2,
                    Path.Combine(cwd, "appsettings.json"),
                    cwd,
                    ImageHelpers.WorldToPxPy1536,
                    "poiStandard"
                    );
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[MinorBase Labels] skipped: {e.Message}");
                }
                
                // 12c) Labels - Evergaol
                try
                {
                    NightReign.MapGen.Rendering.LabelerEvergaol.Label(
                    background,
                    patternDoc.pois,
                    p => (p.name, p.x, p.z),
                    indexObj,
                    Path.Combine(cwd, "appsettings.json"),
                    cwd,
                    ImageHelpers.WorldToPxPy1536,
                    "poiStandard"
                    );
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Evergaol Labels] skipped: {e.Message}");
                }
                
                // 12d) Labels - Field Boss
                try
                {
                    NightReign.MapGen.Rendering.LabelerFieldBoss.Label(
                    background,
                    patternDoc.pois,
                    p => (p.name, p.x, p.z),
                    indexObj,
                    Path.Combine(cwd, "appsettings.json"),
                    cwd,
                    ImageHelpers.WorldToPxPy1536,
                    "poiStandard"
                    );
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[FieldBoss Labels] skipped: {e.Message}");
                }
                
                // 12e) Labels - Night Boss
                try
                {
                    NightReign.MapGen.Rendering.LabelerNightBoss.Label(
                    background,
                    patternDoc.pois,
                    p => (p.name, p.x, p.z),
                    indexObj,
                    Path.Combine(cwd, "appsettings.json"),
                    cwd,
                    ImageHelpers.WorldToPxPy1536,
                    "poiNightBoss"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NightBoss Labels] skipped: {ex.Message}");
                }
                
                // 12f) Labels - Shifting Earth (attach_points overlays)
                try
                {
                    NightReign.MapGen.Rendering.LabelerShiftingEarth.Label(
                    background,
                    specialValue: specialValue,
                    appsettingsPath: Path.Combine(cwd, "appsettings.json"),
                    cwd: cwd
                    );
                }
                catch (Exception exSE)
                {
                    Console.WriteLine($"[ShiftingEarth Labels] skipped: {exSE.Message}");
                }
                
                // 12g) Special Event Banner (bottom-centered)
                try
                {
                    NightReign.MapGen.Rendering.LabelerSpecialEvent.Label(
                    background,
                    patternId: patternId,
                    appsettingsPath: Path.Combine(cwd, "appsettings.json"),
                    cwd: cwd,
                    bottomMarginPx: 24
                    );
                }
                catch (Exception exSEB)
                {
                    Console.WriteLine($"[SpecialEvent Banner] skipped: {exSEB.Message}");
                }
                
                // 12h) Special Icon overlay (Township/Merchant PNG)
                try
                {
                    NightReign.MapGen.Rendering.LabelerSpecialIcon.Label(
                    background,
                    patternId: patternId,
                    appsettingsPath: Path.Combine(cwd, "appsettings.json"),
                    cwd: cwd
                    );
                }
                catch (Exception exSI)
                {
                    Console.WriteLine($"[SpecialIcon] skipped: {exSI.Message}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Labels] skipped: {e.Message}");
            }
        }
        
        private static Dictionary<string, object> BuildNameKeyedIndex(Dictionary<string, IndexEntry> indexLookup)
        {
            var indexObj = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in indexLookup)
            {
                var v = kv.Value;
                string k = kv.Key;
                try
                {
                    var t = v.GetType();
                    var nameProp = t.GetProperty("name") ?? t.GetProperty("Name");
                    if (nameProp != null)
                    {
                        var nameVal = nameProp.GetValue(v) as string;
                        if (!string.IsNullOrWhiteSpace(nameVal)) k = nameVal;
                    }
                }
                catch { }
                indexObj[k] = v;
            }
            return indexObj;
        }
    }
}
