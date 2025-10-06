using System;
using System.IO;
using System.Linq;
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
    }
}
