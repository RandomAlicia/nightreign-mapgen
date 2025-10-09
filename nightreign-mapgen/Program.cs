using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ImageMagick;

namespace NightReign.MapGen
{
    /// <summary>
    /// Slim, orchestration-only entry point. All heavy lifting has been moved into Rendering/* modules.
    /// </summary>
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
                
                // Load config + resolve core paths
                var cfg = LoadConfig("appsettings.json");
                var cwd = Directory.GetCurrentDirectory();
                
                var backgroundPath = ResolvePath(cfg.BackgroundPath ?? throw new InvalidOperationException("BackgroundPath missing."), cwd);
                var outputFolder   = ResolvePath(cfg.OutputFolder   ?? "output", cwd, allowCreateDir: true);
                
                // Pattern id + documents
                var patternPath = args[0];
                var id = GetPatternId(patternPath);
                if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException($"Could not determine a pattern id from: {patternPath}");
                Console.WriteLine($"[Info] Pattern id = {id}");
                
                var summary = LoadSummary(ResolvePath(cfg.SummaryPath ?? throw new InvalidOperationException("SummaryPath missing."), cwd));
                var patSummary = summary.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Pattern '{id}' not found in summary.json.");
                
                var patternDoc = LoadPattern(patternPath);
                
                // Index
                var indexPath = ResolvePath(cfg.IndexPath ?? "../data/index.json", cwd);
                var indexLookup = LoadIndex(indexPath);
                Console.WriteLine($"[Index] Entries loaded: {indexLookup.Count}");
                
                using var background = new MagickImage(backgroundPath);
                
                // 1) Backdrops & overlays
                Rendering.Overlays.ApplyBackdropAndShifting(background, patSummary, cfg, cwd);
                Rendering.Overlays.ApplyEventOverlays(background, patSummary, cwd);
                Rendering.Overlays.ApplyCastleOverlayIfNeeded(background, patSummary.Special, cwd);
                Rendering.Overlays.ApplyTreasureOverlay(background, patSummary, cfg, cwd);
                Rendering.Overlays.ApplyNightlordEmblem(background, patSummary, cfg, cwd);
                
                // 2) POIs
                Rendering.PoiRenderers.RenderAll(background, patternDoc, indexLookup, cfg, cwd);
                
                // 3) Labels
                Rendering.Labels.RenderAll(background, patternDoc, indexLookup, cfg, cwd, patSummary.Special, id, patternPath);
                
                // 4) Extras
                Rendering.Overlays.ApplySpawnPoint(background, patSummary, cwd);
                Rendering.Overlays.ApplySignature(background, cwd);
                Rendering.Overlays.ApplyIdStamp(background, id, patternPath, cwd);
                
                // 5) Save
                Rendering.Output.Save(background, outputFolder, id);
                
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
