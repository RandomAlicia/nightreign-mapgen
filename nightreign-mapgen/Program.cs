using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using ImageMagick;

namespace NightReign.MapGen
{
    public static partial class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    Console.WriteLine("Usage: dotnet run -- <path-to-pattern_xxx.json>");
                    return 1;
                }

                var cfg = LoadConfig("appsettings.json");
                var cwd = Directory.GetCurrentDirectory();

                var backgroundPath = ResolvePath(cfg.BackgroundPath ?? throw new InvalidOperationException("BackgroundPath missing."), cwd);
                var outputFolder   = ResolvePath(cfg.OutputFolder   ?? "output", cwd, allowCreateDir: true);

                var patternPath = args[0];
                var id = GetPatternId(patternPath);
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException($"Could not determine a pattern id from: {patternPath}");
                Console.WriteLine($"[Info] Pattern id = {id}");

                var summary = LoadSummary(ResolvePath(cfg.SummaryPath ?? throw new InvalidOperationException("SummaryPath missing."), cwd));
                var patSummary = summary.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                          ?? throw new InvalidOperationException($"Pattern '{id}' not found in summary.json.");

                var patternDoc = LoadPattern(patternPath);

                var indexPath = ResolvePath(cfg.IndexPath ?? "../data/index.json", cwd);
                var indexLookup = LoadIndex(indexPath);
                Console.WriteLine($"[Index] Entries loaded: {indexLookup.Count}");

                using var background = new MagickImage(backgroundPath); // 1) Background

                // 2) Nightlord backdrop (full canvas)
                if (!string.IsNullOrWhiteSpace(cfg.NightlordFolder) && !string.IsNullOrWhiteSpace(patSummary.Nightlord))
                {
                    var nightlordPath = System.IO.Path.Combine(ResolvePath(cfg.NightlordFolder!, cwd), $"backdrop_{patSummary.Nightlord}.png");
                    CompositeFullCanvasIfExists(background, nightlordPath);
                }

                // 3) Map raw overlay (full canvas)
                if (!string.IsNullOrWhiteSpace(cfg.MapRawFolder) && !string.IsNullOrWhiteSpace(patSummary.Special))
                {
                    var rawName = MapRawFilenameForSpecial(patSummary.Special!);
                    if (rawName is not null)
                    {
                        var rawPath = System.IO.Path.Combine(ResolvePath(cfg.MapRawFolder!, cwd), rawName);
                        CompositeFullCanvasIfExists(background, rawPath);
                    }
                }

                // 3a) Shifting earth overlay (full canvas) based on Special
                if (!string.IsNullOrWhiteSpace(patSummary.Special))
                {
                    var overName = MapShiftingOverlayForSpecial(patSummary.Special!);
                    if (overName is not null)
                    {
                        var overFolder = ResolvePath("../assets/map/shifting_earth", cwd);
                        var overPath = System.IO.Path.Combine(overFolder, overName);
                        CompositeFullCanvasIfExists(background, overPath);
                    }
                }

                // 3a-ii) Final event overlays (frenzy / rot)
                {
                    var frenzyFile = MapFrenzyOverlay(patSummary.Frenzy_Tower);
                    if (frenzyFile != null)
                    {
                        var evtFolder = ResolvePath("../assets/map/event", cwd);
                        var fpath = System.IO.Path.Combine(evtFolder, frenzyFile);
                        CompositeFullCanvasIfExists(background, fpath);
                    }
                }
                {
                    var blessFile = MapRotBlessingOverlay(patSummary.Rot_Blessing);
                    if (blessFile != null)
                    {
                        var evtFolder = ResolvePath("../assets/map/event", cwd);
                        var bpath = System.IO.Path.Combine(evtFolder, blessFile);
                        CompositeFullCanvasIfExists(background, bpath);
                    }
                }

                // 3b) Castle overlay (no resize) if Special in {0,1,2,3}
                if (!string.IsNullOrWhiteSpace(patSummary.Special))
                {
                    if (int.TryParse(patSummary.Special, out var sVal) && (sVal == 0 || sVal == 1 || sVal == 2 || sVal == 3))
                    {
                        var castlePath = ResolvePath("../assets/map/castle.png", cwd);
                        CompositeNoResizeIfExists(background, castlePath);
                    }
                }

                // 4) Treasure overlay (full canvas): file = treasure_{T*10 + S:D5}.png
                if (!string.IsNullOrWhiteSpace(cfg.TreasureFolder) && !string.IsNullOrWhiteSpace(patSummary.Treasure) && !string.IsNullOrWhiteSpace(patSummary.Special))
                {
                    if (TryComputeTreasureCode(patSummary.Treasure!, patSummary.Special!, out var code))
                    {
                        var treasureFile = $"treasure_{code:D5}.png";
                        var treasurePath = System.IO.Path.Combine(ResolvePath(cfg.TreasureFolder!, cwd), treasureFile);
                        CompositeFullCanvasIfExists(background, treasurePath);
                    }
                    else
                    {
                        Console.WriteLine($"[Warn] Could not parse treasure/special for id={id} (treasure='{patSummary.Treasure}', special='{patSummary.Special}').");
                    }
                }

                // 4b) Spawn point overlay (full canvas) based on summary.spawn_point_id
                {
                    var spawnFile = MapSpawnOverlay(patSummary.Spawn_Point_Id);
                    if (spawnFile != null)
                    {
                        var spawnFolder = ResolvePath("../assets/map/spawn_point", cwd);
                        var spawnPath = System.IO.Path.Combine(spawnFolder, spawnFile);
                        CompositeFullCanvasIfExists(background, spawnPath);
                    }
                }

                // 5) Nightlord emblem (anchored)
                if (!string.IsNullOrWhiteSpace(cfg.NightlordFolder) && !string.IsNullOrWhiteSpace(patSummary.Nightlord))
                {
                    var emblemName = MapNightlordIconFilename(patSummary.Nightlord!);
                    if (emblemName is not null)
                    {
                        var emblemPath = System.IO.Path.Combine(ResolvePath(cfg.NightlordFolder!, cwd), emblemName);
                        CompositeIconAnchored(background, emblemPath, cfg.NightlordIcon);
                    }
                }

                // 6) POIs - Major Base
                MajorBaseRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);

                // 7) POIs - Minor Base
                MinorBaseRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);

                // 8) POIs - Event
                EventRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);

                // 9) POIs - Evergaol
                EvergaolRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);

                // 10) POIs - Field Bosses
                FieldBossRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);

                // 11) POIs - Night Bosses
                NightBossRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);
                // 12) Labels - Major Base (Camp/Fort/Great_Church/Ruins)
                try
                {
                    // Convert indexLookup to <string, object> for resolver compatibility
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
                    NightReign.MapGen.Rendering.LabelerMajorBase.Label(
                        background,
                        patternDoc.pois,
                        p => (p.name, p.x, p.z),
                        indexObj,
                        Path.Combine(cwd, "appsettings.json"),
                        cwd,
                        WorldToPxPy1536,
                        "poiStandard"
                    );

// 12b) Labels - Minor Base (Sorcerers_Rise)
try
{
    // Prepare index lookup matching by 'name' like MajorBase
    var indexObj2 = new Dictionary<string, object>(StringComparer.Ordinal);
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
        indexObj2[k] = v;
    }

    NightReign.MapGen.Rendering.LabelerMinorBase.Label(
        background,
        patternDoc.pois,
        p => (p.name, p.x, p.z),
        indexObj2,
        Path.Combine(cwd, "appsettings.json"),
        cwd,
        WorldToPxPy1536,
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
    // Reuse indexObj built for MajorBase (name-keyed) so resolver matches by 'name'
    NightReign.MapGen.Rendering.LabelerEvergaol.Label(
        background,
        patternDoc.pois,
        p => (p.name, p.x, p.z),
        indexObj,
        Path.Combine(cwd, "appsettings.json"),
        cwd,
        WorldToPxPy1536,
        "poiStandard"
    );
}
catch (Exception e)
{
    Console.WriteLine($"[Evergaol Labels] skipped: {e.Message}");
}


// 12d) Labels - Field Boss (Field_Boss / Strong_Field_Boss / Arena_Boss / Castle)
try
{
    // Reuse name-keyed index (indexObj) so the resolver matches by 'name'
    NightReign.MapGen.Rendering.LabelerFieldBoss.Label(
        background,
        patternDoc.pois,
        p => (p.name, p.x, p.z),
        indexObj,
        Path.Combine(cwd, "appsettings.json"),
        cwd,
        WorldToPxPy1536,
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
                        System.IO.Path.Combine(cwd, "appsettings.json"),
                        cwd,
                        WorldToPxPy1536,
                        "poiNightBoss"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NightBoss Labels] skipped: {ex.Message}");
                }




                }
                catch (Exception e)
                {
                    Console.WriteLine($"[MajorBase Labels] skipped: {e.Message}");
                }

                // Save

                background.Format = MagickFormat.Png;
                var outputPath = System.IO.Path.Combine(outputFolder, $"{id}.png");
                background.Write(outputPath);
                Console.WriteLine($"[Wrote] {outputPath}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Error] {ex.Message}");
                return 2;
            }
        }
        
        // Helper: convert hex to MagickColor (#RRGGBB or #RRGGBBAA, STRICT RRGGBBAA for 8 digits)
        private static ImageMagick.MagickColor ParseHexToMagickColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return ImageMagick.MagickColors.White;
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
                return ImageMagick.MagickColors.White;
            }

            return new ImageMagick.MagickColor(r, g, b, a);
        }

        // Map world (x,z) to pixel (px,py) on a 1536×1536 canvas: px = 768 + x, py = 768 - z
        private static (int px, int py) WorldToPxPy1536(double x, double z)
        {
            int px = (int)Math.Round(768.0 + x);
            int py = (int)Math.Round(768.0 - z);
            return (px, py);
        }

        // Generic selector for POIs that extracts (name, x, z) using reflection.
        private static (string name, double x, double z) SelectNameXZ(object poi)
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