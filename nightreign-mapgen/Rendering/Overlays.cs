using System;
using System.IO;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Backdrops, overlays, emblems, signatures, spawn points, and ID stamp.
    /// </summary>
    public static class Overlays
    {
        public static void ApplyBackdropAndShifting(MagickImage background, SummaryPattern patSummary, AppConfig cfg, string cwd)
        {
            // Nightlord backdrop (full canvas)
            if (!string.IsNullOrWhiteSpace(cfg.NightlordFolder) && !string.IsNullOrWhiteSpace(patSummary.Nightlord))
            {
                var nightlordPath = Path.Combine(Program.ResolvePath(cfg.NightlordFolder!, cwd), $"backdrop_{patSummary.Nightlord}.png");
                Program.CompositeFullCanvasIfExists(background, nightlordPath);
            }
            
            // Map raw overlay (full canvas)
            if (!string.IsNullOrWhiteSpace(cfg.MapRawFolder) && !string.IsNullOrWhiteSpace(patSummary.Special))
            {
                var rawName = Program.MapRawFilenameForSpecial(patSummary.Special!);
                if (rawName is not null)
                {
                    var rawPath = Path.Combine(Program.ResolvePath(cfg.MapRawFolder!, cwd), rawName);
                    Program.CompositeFullCanvasIfExists(background, rawPath);
                }
            }
            
            // Shifting earth overlay (full canvas) based on Special
            if (!string.IsNullOrWhiteSpace(patSummary.Special))
            {
                var overName = Program.MapShiftingOverlayForSpecial(patSummary.Special!);
                if (overName is not null)
                {
                    var overFolder = Program.ResolvePath("../assets/map/shifting_earth", cwd);
                    var overPath = Path.Combine(overFolder, overName);
                    Program.CompositeFullCanvasIfExists(background, overPath);
                }
            }
        }
        
        public static void ApplyEventOverlays(MagickImage background, SummaryPattern patSummary, string cwd)
        {
            // Frenzy overlay (full canvas)
            var frenzyFile = Program.MapFrenzyOverlay(patSummary.Frenzy_Tower);
            if (frenzyFile != null)
            {
                var evtFolder = Program.ResolvePath("../assets/map/event", cwd);
                var fpath = Path.Combine(evtFolder, frenzyFile);
                Program.CompositeFullCanvasIfExists(background, fpath);
            }
            
            // Rot/Blessing overlay (full canvas)
            var blessFile = Program.MapRotBlessingOverlay(patSummary.Rot_Blessing);
            if (blessFile != null)
            {
                var evtFolder = Program.ResolvePath("../assets/map/event", cwd);
                var bpath = Path.Combine(evtFolder, blessFile);
                Program.CompositeFullCanvasIfExists(background, bpath);
            }
        }
        
        public static void ApplyCastleOverlayIfNeeded(MagickImage background, string? special, string cwd)
        {
            if (!string.IsNullOrWhiteSpace(special) && int.TryParse(special, out var sVal) && (sVal == 0 || sVal == 1 || sVal == 2 || sVal == 3))
            {
                var castlePath = Program.ResolvePath("../assets/map/castle.png", cwd);
                Program.CompositeNoResizeIfExists(background, castlePath);
            }
        }
        
        public static void ApplyTreasureOverlay(MagickImage background, SummaryPattern patSummary, AppConfig cfg, string cwd)
        {
            if (!string.IsNullOrWhiteSpace(cfg.TreasureFolder) && !string.IsNullOrWhiteSpace(patSummary.Treasure) && !string.IsNullOrWhiteSpace(patSummary.Special))
            {
                if (Program.TryComputeTreasureCode(patSummary.Treasure!, patSummary.Special!, out var code))
                {
                    var treasureFile = $"treasure_{code:D5}.png";
                    var treasurePath = Path.Combine(Program.ResolvePath(cfg.TreasureFolder!, cwd), treasureFile);
                    Program.CompositeFullCanvasIfExists(background, treasurePath);
                }
                else
                {
                    Console.WriteLine($"[Warn] Could not parse treasure/special (treasure='{patSummary.Treasure}', special='{patSummary.Special}').");
                }
            }
        }
        
        public static void ApplyNightlordEmblem(MagickImage background, SummaryPattern patSummary, AppConfig cfg, string cwd)
        {
            if (!string.IsNullOrWhiteSpace(cfg.NightlordFolder) && !string.IsNullOrWhiteSpace(patSummary.Nightlord))
            {
                var emblemName = Program.MapNightlordIconFilename(patSummary.Nightlord!);
                if (emblemName is not null)
                {
                    var emblemPath = Path.Combine(Program.ResolvePath(cfg.NightlordFolder!, cwd), emblemName);
                    Program.CompositeIconAnchored(background, emblemPath, cfg.NightlordIcon);
                }
            }
        }
        
        public static void ApplySpawnPoint(MagickImage background, SummaryPattern patSummary, string cwd)
        {
            var spawnFile = Program.MapSpawnOverlay(patSummary.Spawn_Point_Id);
            if (spawnFile != null)
            {
                var spawnFolder = Program.ResolvePath("../assets/map/spawn_point", cwd);
                var spawnPath = Path.Combine(spawnFolder, spawnFile);
                Program.CompositeFullCanvasIfExists(background, spawnPath);
            }
        }
        
        public static void ApplySignature(MagickImage background, string cwd)
        {
            try
            {
                var sigPath = Program.ResolvePath("../assets/misc/signature.png", cwd);
                Program.CompositeFullCanvasIfExists(background, sigPath);
            }
            catch (Exception exSIG)
            {
                Console.WriteLine($"[Signature] skipped: {exSIG.Message}");
            }
        }
        
        public static void ApplyIdStamp(MagickImage background, string id, string patternPath, string cwd)
        {
            try
            {
                // Derive id from filename if needed (pattern_XXX.json -> XXX)
                string idText = id;
                if (string.IsNullOrWhiteSpace(idText) || idText.Length != 3)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(patternPath ?? string.Empty, @"pattern_(\d{3})\.json", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success) idText = m.Groups[1].Value;
                }
                
                // Load style from appsettings.json
                var appPath = Path.Combine(cwd, "appsettings.json");
                using var appFs = File.OpenRead(appPath);
                using var appDoc = System.Text.Json.JsonDocument.Parse(appFs);
                var appRoot = appDoc.RootElement;
                
                int idFontSize = 25;
                string idFill = "#FFFFFFFF";
                string? idFontPath = null;
                
                if (appRoot.TryGetProperty("Text", out var textEl) && textEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (textEl.TryGetProperty("Styles", out var styles) && styles.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (styles.TryGetProperty("id", out var idStyle) && idStyle.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            if (idStyle.TryGetProperty("FontSizePx", out var fs) && fs.ValueKind == System.Text.Json.JsonValueKind.Number)
                            idFontSize = fs.GetInt32();
                            if (idStyle.TryGetProperty("Fill", out var fill) && fill.ValueKind == System.Text.Json.JsonValueKind.String)
                            idFill = fill.GetString() ?? idFill;
                            if (idStyle.TryGetProperty("FontPath", out var fp) && fp.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                var cand = fp.GetString();
                                if (!string.IsNullOrWhiteSpace(cand))
                                idFontPath = Path.IsPathRooted(cand) ? cand : Program.ResolvePath(cand, cwd);
                            }
                        }
                    }
                }
                
                // Fallback font if the style didn't provide one
                if (string.IsNullOrWhiteSpace(idFontPath))
                {
                    var notoCand = Program.ResolvePath("../assets/font/NotoSans-Regular.ttf", cwd);
                    if (File.Exists(notoCand)) idFontPath = notoCand;
                }
                
                var settings = new MagickReadSettings
                {
                    Font = idFontPath,
                    FontPointsize = idFontSize,
                    FillColor = ImageHelpers.ParseHexToMagickColor(idFill),
                    BackgroundColor = MagickColors.Transparent,
                    TextEncoding = System.Text.Encoding.UTF8
                };
                
                using var labelImg = new MagickImage($"label:{idText}", settings);
                if (labelImg.Width > 0 && labelImg.Height > 0)
                {
                    const int padRightPx  = 20;
                    const int padBottomPx = 15;
                    
                    int x = (int)background.Width  - (int)labelImg.Width  - padRightPx;
                    int y = (int)background.Height - (int)labelImg.Height - padBottomPx;
                    if (x < 0) x = 0;
                    if (y < 0) y = 0;
                    background.Composite(labelImg, x, y, CompositeOperator.Over);
                }
            }
            catch (Exception exID)
            {
                Console.WriteLine($"[ID Stamp] skipped: {exID.Message}");
            }
        }
    }
}
